"""Tests for build_wheel.py. Run: python3 -m unittest discover -s tools/wheels

With DBUDOCTOR_BINARY and DBUDOCTOR_RID set to a real binary for this machine, the install test
builds a wheel, installs it in a fresh virtual environment and runs it (CI does this per platform).
"""
import os
import pathlib
import struct
import subprocess
import sys
import tempfile
import unittest
import venv
import zipfile

sys.path.insert(0, str(pathlib.Path(__file__).parent))
import build_wheel  # noqa: E402

ROOT = pathlib.Path(__file__).resolve().parents[2]


def mach_o(minos: int) -> bytes:
    header = struct.pack("<IiiIIIII", 0xFEEDFACF, 0x0100000C, 0, 2, 1, 24, 0, 0)
    build_version = struct.pack("<IIIIII", 0x32, 24, 1, minos, minos, 0)
    return header + build_version + b"\0" * 64


class Tags(unittest.TestCase):
    def test_glibc_takes_the_highest_version(self):
        data = b"\0GLIBC_2.17\0GLIBC_2.34\0GLIBC_2.2.5\0GLIBC_PRIVATE\0"
        self.assertEqual((2, 34), build_wheel.glibc_requirement(data))
        self.assertEqual("manylinux_2_34_x86_64", build_wheel.platform_tag("linux-x64", data))
        self.assertEqual("manylinux_2_34_aarch64", build_wheel.platform_tag("linux-arm64", data))

    def test_glibc_with_only_three_part_versions(self):
        self.assertEqual((2, 2), build_wheel.glibc_requirement(b"\0GLIBC_2.2.5\0"))

    def test_binary_without_glibc_is_rejected(self):
        with self.assertRaises(ValueError):
            build_wheel.glibc_requirement(b"musl")

    def test_macos_minimum_from_build_version(self):
        data = mach_o((14 << 16) | (2 << 8))
        self.assertEqual((14, 2), build_wheel.macos_minimum(data))
        self.assertEqual("macosx_14_0_arm64", build_wheel.platform_tag("osx-arm64", data))

    def test_windows(self):
        self.assertEqual("win_amd64", build_wheel.platform_tag("win-x64", b"MZ"))

    def test_unknown_rid(self):
        with self.assertRaises(ValueError):
            build_wheel.platform_tag("osx-x64", b"")


class Versions(unittest.TestCase):
    def test_pep440(self):
        self.assertEqual("0.3.0", build_wheel.pep440("0.3.0"))
        self.assertEqual("0.4.0rc1", build_wheel.pep440("0.4.0-rc.1"))
        self.assertEqual("0.4.0b2", build_wheel.pep440("0.4.0-beta.2"))
        self.assertEqual("0.4.0a3", build_wheel.pep440("0.4.0-alpha.3"))
        self.assertEqual("0.3.0.dev12", build_wheel.pep440("0.3.0.dev12"))
        with self.assertRaises(ValueError):
            build_wheel.pep440("0.4.0-preview")


class Contents(unittest.TestCase):
    def test_wheel_layout_and_record(self):
        with tempfile.TemporaryDirectory() as tmp:
            binary = pathlib.Path(tmp, "dbudoctor")
            binary.write_bytes(b"\x7fELF\0GLIBC_2.28\0")
            wheel = build_wheel.build("linux-x64", binary, "0.4.0-rc.1", ROOT / "README.md", ROOT / "LICENSE", pathlib.Path(tmp, "out"))

            self.assertEqual("dbudoctor-0.4.0rc1-py3-none-manylinux_2_28_x86_64.whl", wheel.name)
            with zipfile.ZipFile(wheel) as z:
                names = set(z.namelist())
                self.assertEqual({
                    "dbudoctor/__init__.py",
                    "dbudoctor/__main__.py",
                    "dbudoctor-0.4.0rc1.data/scripts/dbudoctor",
                    "dbudoctor-0.4.0rc1.dist-info/licenses/LICENSE",
                    "dbudoctor-0.4.0rc1.dist-info/METADATA",
                    "dbudoctor-0.4.0rc1.dist-info/WHEEL",
                    "dbudoctor-0.4.0rc1.dist-info/RECORD",
                }, names)
                script = z.getinfo("dbudoctor-0.4.0rc1.data/scripts/dbudoctor")
                self.assertEqual(0o755, (script.external_attr >> 16) & 0o777)
                metadata = z.read("dbudoctor-0.4.0rc1.dist-info/METADATA").decode()
                self.assertIn("Version: 0.4.0rc1", metadata)
                self.assertIn("License-Expression: Apache-2.0", metadata)
                self.assertIn("Tag: py3-none-manylinux_2_28_x86_64", z.read("dbudoctor-0.4.0rc1.dist-info/WHEEL").decode())
                record = z.read("dbudoctor-0.4.0rc1.dist-info/RECORD").decode().splitlines()
                self.assertEqual(len(names), len(record))
                self.assertIn("dbudoctor-0.4.0rc1.dist-info/RECORD,,", record)

    def test_windows_binary_keeps_exe(self):
        with tempfile.TemporaryDirectory() as tmp:
            binary = pathlib.Path(tmp, "dbudoctor.exe")
            binary.write_bytes(b"MZ")
            wheel = build_wheel.build("win-x64", binary, "0.3.0", ROOT / "README.md", ROOT / "LICENSE", pathlib.Path(tmp, "out"))
            with zipfile.ZipFile(wheel) as z:
                self.assertIn("dbudoctor-0.3.0.data/scripts/dbudoctor.exe", z.namelist())


@unittest.skipUnless(os.environ.get("DBUDOCTOR_BINARY") and os.environ.get("DBUDOCTOR_RID"), "needs a real binary")
class Install(unittest.TestCase):
    def test_pip_install_and_run(self):
        binary = pathlib.Path(os.environ["DBUDOCTOR_BINARY"])
        version = os.environ.get("DBUDOCTOR_VERSION", "0.0.0")
        with tempfile.TemporaryDirectory() as tmp:
            wheel = build_wheel.build(os.environ["DBUDOCTOR_RID"], binary, version, ROOT / "README.md", ROOT / "LICENSE", pathlib.Path(tmp, "out"))
            env_dir = pathlib.Path(tmp, "venv")
            venv.create(env_dir, with_pip=True)
            bin_dir = env_dir / ("Scripts" if sys.platform == "win32" else "bin")
            python = bin_dir / ("python.exe" if sys.platform == "win32" else "python")
            subprocess.run([str(python), "-m", "pip", "install", "--quiet", "--no-index", str(wheel)], check=True)

            command = bin_dir / ("dbudoctor.exe" if sys.platform == "win32" else "dbudoctor")
            direct = subprocess.run([str(command), "--version"], check=True, capture_output=True, text=True).stdout
            module = subprocess.run([str(python), "-m", "dbudoctor", "--version"], check=True, capture_output=True, text=True).stdout
            self.assertTrue(direct.startswith(version.split(".dev")[0]), direct)
            self.assertEqual(direct, module)

            bad_direct = subprocess.run([str(command), "--no-such-flag"], capture_output=True)
            bad_module = subprocess.run([str(python), "-m", "dbudoctor", "--no-such-flag"], capture_output=True)
            self.assertNotEqual(0, bad_direct.returncode)
            self.assertEqual(bad_direct.returncode, bad_module.returncode)


if __name__ == "__main__":
    unittest.main()
