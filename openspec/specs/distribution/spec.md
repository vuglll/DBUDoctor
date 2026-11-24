# distribution Specification

## Purpose
How DBUDoctor is packaged and published beyond the GitHub release and the dotnet tool.

## Requirements

### Requirement: Python wheels on PyPI
Each release SHALL publish the package `dbudoctor` to PyPI with one wheel per release binary: Linux x64 and ARM64, macOS ARM64 and Windows x64. Each wheel SHALL install the Native AOT binary as the `dbudoctor` command, and `python -m dbudoctor` SHALL run the same binary with the same arguments and exit code. Publishing SHALL use Trusted Publishing, with no stored token.

#### Scenario: Install with pip
- **WHEN** a user runs `pip install dbudoctor==X.Y.Z` on Linux x64
- **THEN** `dbudoctor --version` prints X.Y.Z, and no .NET runtime is needed

#### Scenario: Module entry point
- **WHEN** a user runs `python -m dbudoctor diff --base-json a.json --head-json b.json`
- **THEN** the binary runs with those arguments and its exit code is returned

### Requirement: Honest platform tags
Wheel platform tags SHALL come from the binary: Linux wheels SHALL be tagged `manylinux_2_<N>` with N the highest glibc version the binary requires, and the macOS wheel `macosx_<M>_0` with M its minimum macOS version. Python versions SHALL follow PEP 440.

#### Scenario: Linux binary built on Ubuntu 22.04
- **WHEN** the Linux x64 binary requires glibc 2.34 at most
- **THEN** its wheel is tagged `manylinux_2_34_x86_64`, and pip refuses it on an older glibc instead of installing a binary that cannot start

#### Scenario: Pre-release version
- **WHEN** the release version is `0.4.0-rc.1`
- **THEN** the wheels are version `0.4.0rc1`
