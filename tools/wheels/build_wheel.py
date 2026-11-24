"""Build one PyPI wheel for DBUDoctor from one Native AOT binary.

Usage:
  python3 build_wheel.py --rid linux-x64 --binary publish/dbudoctor --version 0.3.0 \
      --readme README.md --license LICENSE --out wheels/

The wheel is py3-none-<platform>. The binary goes to <name>-<version>.data/scripts/, so pip puts
`dbudoctor` on PATH with no Python start-up cost; the small `dbudoctor` package adds
`python -m dbudoctor`. Platform tags come from the binary itself: the highest GLIBC_2.x symbol
version for Linux, the LC_BUILD_VERSION minimum for macOS. Standard library only.
"""
import argparse
import base64
import hashlib
import pathlib
import re
import struct
import sys
import zipfile

NAME = "dbudoctor"
SUMMARY = "Estimates what a Databricks bundle change will cost, from the pull request diff."
HOMEPAGE = "https://github.com/vuglll/DBUDoctor"

INIT_PY = '''"""DBUDoctor: Databricks bundle cost review. The `dbudoctor` command is a native binary."""
import os
import shutil
import sys
import sysconfig

__version__ = "{version}"


def find_dbudoctor_bin() -> str:
    """Path of the installed dbudoctor binary."""
    exe = "dbudoctor" + (".exe" if sys.platform == "win32" else "")
    candidates = [sysconfig.get_path("scripts")]
    user_scheme = {{"win32": "nt_user", "darwin": "osx_framework_user"}}.get(sys.platform, "posix_user")
    try:
        candidates.append(sysconfig.get_path("scripts", scheme=user_scheme))
    except KeyError:
        pass
    for directory in candidates:
        path = os.path.join(directory, exe)
        if os.path.isfile(path):
            return path
    found = shutil.which(exe)
    if found:
        return found
    raise FileNotFoundError(exe)
'''

MAIN_PY = '''import os
import subprocess
import sys

from dbudoctor import find_dbudoctor_bin


def main() -> int:
    binary = find_dbudoctor_bin()
    if sys.platform == "win32":
        return subprocess.call([binary, *sys.argv[1:]])
    os.execv(binary, [binary, *sys.argv[1:]])
    return 0


if __name__ == "__main__":
    sys.exit(main())
'''


def pep440(version: str) -> str:
    """0.3.0 stays; 0.4.0-rc.1 -> 0.4.0rc1, -beta.2 -> b2, -alpha.3 -> a3, -dev.4 -> .dev4."""
    if re.fullmatch(r"\d+\.\d+\.\d+(\.dev\d+)?", version):
        return version
    m = re.fullmatch(r"(\d+\.\d+\.\d+)-(alpha|beta|rc|dev)\.?(\d+)", version)
    if not m:
        raise ValueError(f"cannot convert version {version!r} to PEP 440")
    base, kind, number = m.groups()
    return base + {"alpha": "a", "beta": "b", "rc": "rc", "dev": ".dev"}[kind] + number


def glibc_requirement(data: bytes) -> tuple[int, int]:
    """Highest GLIBC_2.x version the ELF binary references."""
    versions = {(2, int(m.group(1))) for m in re.finditer(rb"GLIBC_2\.(\d+)(?![\d.])", data)}
    versions |= {(2, int(m.group(1))) for m in re.finditer(rb"GLIBC_2\.(\d+)\.\d+", data)}
    if not versions:
        raise ValueError("no GLIBC_2.x symbol versions found; is this a glibc ELF binary?")
    return max(versions)


def macos_minimum(data: bytes) -> tuple[int, int]:
    """Minimum macOS version from LC_BUILD_VERSION (or LC_VERSION_MIN_MACOSX) of a thin 64-bit Mach-O."""
    magic = struct.unpack_from("<I", data, 0)[0]
    if magic != 0xFEEDFACF:
        raise ValueError("not a thin 64-bit little-endian Mach-O binary")
    ncmds = struct.unpack_from("<I", data, 16)[0]
    offset = 32
    for _ in range(ncmds):
        cmd, size = struct.unpack_from("<II", data, offset)
        if cmd == 0x32:  # LC_BUILD_VERSION: platform, minos, sdk, ntools
            minos = struct.unpack_from("<I", data, offset + 12)[0]
            return minos >> 16, (minos >> 8) & 0xFF
        if cmd == 0x24:  # LC_VERSION_MIN_MACOSX: version, sdk
            version = struct.unpack_from("<I", data, offset + 8)[0]
            return version >> 16, (version >> 8) & 0xFF
        offset += size
    raise ValueError("no minimum macOS version in the binary")


def platform_tag(rid: str, data: bytes) -> str:
    if rid in ("linux-x64", "linux-arm64"):
        major, minor = glibc_requirement(data)
        arch = "x86_64" if rid == "linux-x64" else "aarch64"
        return f"manylinux_{major}_{minor}_{arch}"
    if rid == "osx-arm64":
        major, minor = macos_minimum(data)
        # Since macOS 11 the minor version is not part of the tag's compatibility for arm64.
        return f"macosx_{major}_0_arm64" if major >= 11 else f"macosx_{major}_{minor}_arm64"
    if rid == "win-x64":
        return "win_amd64"
    raise ValueError(f"unsupported runtime identifier {rid!r}")


def _record_line(path: str, content: bytes) -> str:
    digest = base64.urlsafe_b64encode(hashlib.sha256(content).digest()).rstrip(b"=").decode()
    return f"{path},sha256={digest},{len(content)}"


def build(rid: str, binary: pathlib.Path, version: str, readme: pathlib.Path, license_file: pathlib.Path, out: pathlib.Path) -> pathlib.Path:
    data = binary.read_bytes()
    py_version = pep440(version)
    tag = platform_tag(rid, data)
    dist_info = f"{NAME}-{py_version}.dist-info"
    scripts = f"{NAME}-{py_version}.data/scripts"
    exe = NAME + (".exe" if rid.startswith("win") else "")

    metadata = "\n".join([
        "Metadata-Version: 2.4",
        f"Name: {NAME}",
        f"Version: {py_version}",
        f"Summary: {SUMMARY}",
        f"Home-page: {HOMEPAGE}",
        "Author: Nemanja Markovic",
        "License-Expression: Apache-2.0",
        "License-File: LICENSE",
        f"Project-URL: Source, {HOMEPAGE}",
        f"Project-URL: Changelog, {HOMEPAGE}/blob/main/CHANGELOG.md",
        "Keywords: databricks,cost,finops,asset-bundles,pull-request",
        "Classifier: Development Status :: 4 - Beta",
        "Classifier: Environment :: Console",
        "Classifier: Intended Audience :: Developers",
        "Classifier: Topic :: Software Development :: Quality Assurance",
        "Requires-Python: >=3.8",
        "Description-Content-Type: text/markdown",
        "",
        readme.read_text(encoding="utf-8"),
    ])
    wheel_meta = "\n".join([
        "Wheel-Version: 1.0",
        "Generator: dbudoctor build_wheel.py",
        "Root-Is-Purelib: false",
        f"Tag: py3-none-{tag}",
        "",
    ])

    files: list[tuple[str, bytes, int]] = [
        (f"{NAME}/__init__.py", INIT_PY.format(version=py_version).encode(), 0o644),
        (f"{NAME}/__main__.py", MAIN_PY.encode(), 0o644),
        (f"{scripts}/{exe}", data, 0o755),
        (f"{dist_info}/licenses/LICENSE", license_file.read_bytes(), 0o644),
        (f"{dist_info}/METADATA", metadata.encode("utf-8"), 0o644),
        (f"{dist_info}/WHEEL", wheel_meta.encode(), 0o644),
    ]
    record_path = f"{dist_info}/RECORD"
    record = "\n".join([_record_line(p, c) for p, c, _ in files] + [f"{record_path},,", ""]).encode()

    out.mkdir(parents=True, exist_ok=True)
    wheel = out / f"{NAME}-{py_version}-py3-none-{tag}.whl"
    with zipfile.ZipFile(wheel, "w", compression=zipfile.ZIP_DEFLATED) as z:
        for path, content, mode in files + [(record_path, record, 0o644)]:
            info = zipfile.ZipInfo(path, date_time=(2020, 1, 1, 0, 0, 0))
            info.external_attr = (0o100000 | mode) << 16
            info.compress_type = zipfile.ZIP_DEFLATED
            z.writestr(info, content)
    return wheel


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--rid", required=True)
    parser.add_argument("--binary", required=True, type=pathlib.Path)
    parser.add_argument("--version", required=True)
    parser.add_argument("--readme", required=True, type=pathlib.Path)
    parser.add_argument("--license", required=True, type=pathlib.Path)
    parser.add_argument("--out", required=True, type=pathlib.Path)
    args = parser.parse_args(argv)
    wheel = build(args.rid, args.binary, args.version, args.readme, args.license, args.out)
    print(wheel)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
