## Why

Databricks users work in Python. Today DBUDoctor installs with `dotnet tool install` (needs the .NET 10 SDK) or a manual download from GitHub releases. `pip install dbudoctor` would work in notebooks (`%pip install dbudoctor`), in any CI system and in `requirements.txt`, with no .NET. The roadmap lists a PyPI wheel as a parallel track.

## What Changes

- New capability `distribution`: each release publishes platform wheels to PyPI as `dbudoctor`, each holding the Native AOT binary, the way `ruff` and `uv` ship.
  - Wheels are `py3-none-<platform>`: `manylinux_2_<N>_x86_64`, `manylinux_2_<N>_aarch64`, `macosx_<M>_0_arm64`, `win_amd64`. N is the highest glibc version the binary needs and M its minimum macOS, both read from the binary, so the tags never claim more than the binary supports.
  - The binary is installed as a script, so `dbudoctor` is on `PATH` with no Python start-up cost; `python -m dbudoctor` runs the same binary.
  - Python versions follow PEP 440 (`0.4.0-rc.1` becomes `0.4.0rc1`).
- Linux binaries are built on Ubuntu 22.04 instead of 24.04, so they run on glibc 2.34 and later (Databricks Runtime 13.3 LTS and later run Ubuntu 22.04). This applies to the GitHub release archives too.
- `tools/wheels/build_wheel.py` builds one wheel from one binary with the Python standard library only.
- The release workflow publishes to PyPI with Trusted Publishing (environment `pypi`, no stored token). A new manual workflow, `pypi-test.yml`, builds the same wheels as `<version>.dev<run>` and publishes them to TestPyPI (environment `testpypi`). The binary build moves to a reusable workflow, `binaries.yml`, shared by both.
- CI builds a wheel per platform, installs it in a fresh virtual environment and runs it.
- The agent skill's install instructions add `pip install dbudoctor`.

Not in this change: an Intel macOS wheel (there is no osx-x64 binary yet), musllinux (Alpine), and a source distribution (there is nothing to build from source in Python).

Facts, checked 2026-11-10:
- https://pypi.org/pypi/dbudoctor/json returns 404: the name is free.
- Binary wheels with the executable in `<name>-<version>.data/scripts/` are installed onto `PATH` by pip (wheel spec, PEP 427 / "Binary distribution format").
- Platform tags: https://packaging.python.org/en/latest/specifications/platform-compatibility-tags/ (`manylinux_x_y`, PEP 600).
- Trusted Publishing: https://docs.pypi.org/trusted-publishers/ (pending publishers can be added before the first upload).

## Capabilities

### New Capabilities

- `distribution`: how DBUDoctor is packaged and published (PyPI wheels).

### Modified Capabilities

- `agent-skill`: install instructions name pip as well.

## Impact

- New: `tools/wheels/build_wheel.py`, `.github/workflows/binaries.yml`, `.github/workflows/pypi-test.yml`.
- Changed: `release.yml` (calls `binaries.yml`, adds a `pypi` job), `ci.yml` (Linux AOT on 22.04, wheel smoke test), the skill generator, README install section, CONTRIBUTING release steps.
- Maintainer setup: PyPI and TestPyPI pending publishers (done by the maintainer).
