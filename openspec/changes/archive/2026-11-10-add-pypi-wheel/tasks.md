## 1. Wheel builder

- [x] 1.1 `tools/wheels/build_wheel.py` (standard library only): one wheel from one binary, with the `dbudoctor` package (`python -m dbudoctor`), the binary under `.data/scripts/`, METADATA 2.4 with the README, the license and a correct RECORD
- [x] 1.2 Platform tags read from the binary: highest `GLIBC_2.x` for Linux, `LC_BUILD_VERSION` minimum macOS for macOS; PEP 440 versions
- [x] 1.3 Tests for the builder (`tools/wheels/test_build_wheel.py`): tags, version conversion, wheel contents, and an install of a local wheel that runs `--version`

## 2. Workflows

- [x] 2.1 `binaries.yml` (reusable): AOT archives per platform, Linux on Ubuntu 22.04; `release.yml` calls it
- [x] 2.2 `release.yml`: `pypi` job building the wheels and publishing with Trusted Publishing
- [x] 2.3 `pypi-test.yml`: manual run that publishes `<version>.dev<run>` wheels to TestPyPI
- [x] 2.4 `ci.yml`: Linux AOT on Ubuntu 22.04, and per platform a wheel installed in a fresh virtual environment and run

## 3. Docs and skill

- [x] 3.1 Skill install instructions add `pip install dbudoctor` (regenerate the skill)
- [x] 3.2 README install section, CONTRIBUTING release steps (PyPI and TestPyPI), CHANGELOG Unreleased, roadmap

## 4. Verification

- [x] 4.1 All tests green, `openspec validate add-pypi-wheel --strict` passing, CI wheel smoke tests green on all four platforms
- [x] 4.2 First TestPyPI upload installs and runs (after the maintainer adds the pending publishers)
