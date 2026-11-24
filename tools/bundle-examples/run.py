"""Run DBUDoctor across databricks/bundle-examples without workspace auth.

Usage: python3 run.py <work-dir> <path to dbudoctor.dll>
  <work-dir> must contain a clone of https://github.com/databricks/bundle-examples; results go to
  <work-dir>/runs (results.json plus one Markdown report per run). Set DBUDOCTOR_CLOUD=aws to price
  bundles that have no workspace host.

Python-defined bundles need a `.venv` in their folder first (Python 3.10+), for example:
  python3.12 -m venv .venv && .venv/bin/pip install "databricks-bundles==<version in pyproject.toml>"
contrib/dbt_factory also needs `.venv/bin/pip install -e . "dbt-databricks>=1.8.0,<2.0.0"`.

Pass 1 (new): every bundle at HEAD, base = the same bundle with no resources.
Pass 2 (history): every commit that touched a bundle's YAML, parent vs commit.
validate exits 1 without auth but still prints the resolved bundle; we keep that JSON.
"""
import json, os, subprocess, sys, pathlib, shutil

SCRATCH = pathlib.Path(sys.argv[1])
REPO = SCRATCH / "bundle-examples"
DLL = sys.argv[2]
OUT = SCRATCH / "runs"
shutil.rmtree(OUT, ignore_errors=True)
OUT.mkdir()
ENV = {**os.environ, "DBUDOCTOR_REGION": "us-east-1", "DATABRICKS_HOST": "", "NO_COLOR": "1"}


def validate(bundle_dir, target):
    args = ["databricks", "bundle", "validate", "--output", "json"]
    if target:
        args += ["--target", target]
    p = subprocess.run(args, cwd=bundle_dir, capture_output=True, text=True, env=ENV, timeout=180)
    if "no such target" in p.stderr:
        return None, p.returncode, ["no such target"]
    try:
        doc = json.loads(p.stdout)
    except json.JSONDecodeError:
        return None, p.returncode, p.stderr.strip().splitlines()[-1:] if p.stderr.strip() else ["no output"]
    return doc, p.returncode, None


def python_resources(bundle_dir, rel, doc):
    """Load resources defined in Python, as the CLI would after authenticating.

    Without auth, validate stops before its Python step, so call the same loader
    (`python -m databricks.bundles.build`) with the venv at HEAD (the history pass checks out
    worktrees without one). Target presets that the CLI applies afterwards are skipped.
    """
    python = (doc.get("python") or {})
    if not python.get("resources") and not python.get("mutators"):
        return doc
    venv = REPO / rel / ".venv" / "bin" / "python"
    if not venv.exists():
        return doc
    work = OUT / "python"
    work.mkdir(exist_ok=True)
    current = doc
    for phase in [p for p, key in (("load_resources", "resources"), ("apply_mutators", "mutators")) if python.get(key)]:
        (work / "in.json").write_text(json.dumps(current))
        p = subprocess.run([str(venv), "-m", "databricks.bundles.build", "--phase", phase, "--input", str(work / "in.json"),
                            "--output", str(work / "out.json"), "--diagnostics", str(work / "diag.json")],
                           cwd=bundle_dir, capture_output=True, text=True, timeout=300)
        if p.returncode != 0 or not (work / "out.json").exists():
            print("python", phase, bundle_dir, p.stderr[-300:], file=sys.stderr)
            return current
        current = json.loads((work / "out.json").read_text())
        (work / "out.json").unlink()
    return current


def resolve(bundle_dir, rel):
    """Prefer the prod target (what DBUDoctor estimates by default), else the default target."""
    for target in ("prod", "production", None):
        doc, code, err = validate(bundle_dir, target)
        if doc is not None:
            return python_resources(bundle_dir, rel, doc), target or doc.get("bundle", {}).get("target"), code, None
    return None, None, code, err


def doctor(base, head, name, fmt):
    b, h = OUT / f"{name}.base.json", OUT / f"{name}.head.json"
    b.write_text(json.dumps(base)); h.write_text(json.dumps(head))
    p = subprocess.run(["dotnet", DLL, "diff", "--base-json", str(b), "--head-json", str(h), "--format", fmt, "--config", str(SCRATCH / "empty.yml")],
                       capture_output=True, text=True, env=ENV, cwd=OUT, timeout=120)
    return p.returncode, p.stdout, p.stderr


def bundles(root):
    return sorted(p.parent for p in root.rglob("databricks.yml") if ".git" not in p.parts and ".venv" not in p.parts)


(SCRATCH / "empty.yml").write_text("# defaults\n")
results = {"new": [], "history": []}

# Pass 1
for d in bundles(REPO):
    rel = str(d.relative_to(REPO))
    name = rel.replace("/", "__")
    doc, target, code, err = resolve(d, rel)
    if doc is None:
        results["new"].append({"bundle": rel, "validate": f"failed ({code}): {err}"})
        continue
    base = {**doc, "resources": {}}
    rc, out, errtxt = doctor(base, doc, name, "json")
    entry = {"bundle": rel, "target": target, "validate_exit": code, "doctor_exit": rc,
             "resources": {k: len(v) for k, v in (doc.get("resources") or {}).items()}}
    if rc in (0, 1):
        rep = json.loads(out)
        entry["report"] = rep
        _, md, _ = doctor(base, doc, name, "markdown")
        (OUT / f"{name}.md").write_text(md)
    else:
        entry["error"] = errtxt.strip()[-800:]
    results["new"].append(entry)
    print("new", rel, target, rc, file=sys.stderr)

# Pass 2
log = subprocess.run(["git", "log", "--format=%H %P", "--", "*.yml", "*.yaml"], cwd=REPO, capture_output=True, text=True).stdout.split("\n")
wt = SCRATCH / "wt"
for line in filter(None, log):
    sha, *parents = line.split()
    if len(parents) != 1:
        continue
    parent = parents[0]
    changed = subprocess.run(["git", "diff", "--name-only", parent, sha, "--", "*.yml", "*.yaml"], cwd=REPO, capture_output=True, text=True).stdout.split()
    pairs = {}
    for rev in (parent, sha):
        shutil.rmtree(wt, ignore_errors=True)
        subprocess.run(["git", "worktree", "prune"], cwd=REPO)
        subprocess.run(["git", "worktree", "add", "-q", "--detach", str(wt), rev], cwd=REPO, check=True, capture_output=True)
        dirs = {b for b in bundles(wt) if any(c.startswith(str(b.relative_to(wt)) + "/") or str(b.relative_to(wt)) == "." for c in changed)}
        for b in dirs:
            rel = str(b.relative_to(wt))
            doc, target, code, err = resolve(b, rel)
            pairs.setdefault(rel, {})[rev] = (doc, target)
    shutil.rmtree(wt, ignore_errors=True)
    subprocess.run(["git", "worktree", "prune"], cwd=REPO)
    subj = subprocess.run(["git", "log", "-1", "--format=%s", sha], cwd=REPO, capture_output=True, text=True).stdout.strip()
    for rel, revs in pairs.items():
        (bdoc, _), (hdoc, target) = revs.get(parent, (None, None)), revs.get(sha, (None, None))
        if hdoc is None:
            continue  # bundle deleted or unresolvable at head
        if bdoc is None:
            bdoc = {**hdoc, "resources": {}}  # bundle added in this commit
        name = f"h_{sha[:7]}__{rel.replace('/', '__')}"
        rc, out, errtxt = doctor(bdoc, hdoc, name, "json")
        entry = {"commit": sha[:7], "subject": subj, "bundle": rel, "target": target, "doctor_exit": rc}
        if rc in (0, 1):
            entry["report"] = json.loads(out)
            _, md, _ = doctor(bdoc, hdoc, name, "markdown")
            (OUT / f"{name}.md").write_text(md)
        else:
            entry["error"] = errtxt.strip()[-800:]
        results["history"].append(entry)
        print("hist", sha[:7], rel, rc, file=sys.stderr)

(OUT / "results.json").write_text(json.dumps(results, indent=1))
print("done", len(results["new"]), len(results["history"]), file=sys.stderr)
