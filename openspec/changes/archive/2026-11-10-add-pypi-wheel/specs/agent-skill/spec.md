## MODIFIED Requirements

### Requirement: Distribution and version
Each GitHub Release SHALL attach the skill. `dbudoctor skill export --dir <path>` SHALL write the skill for the running version into the given directory, and SHALL NOT overwrite an existing skill folder unless `--force` is given. SKILL.md SHALL state the DBUDoctor version it was generated for, and SHALL tell the agent how to install DBUDoctor when the command is missing.

#### Scenario: Export for Claude Code
- **WHEN** a user runs `dbudoctor skill export --dir .claude/skills/dbudoctor` and the folder does not exist
- **THEN** the folder is created with SKILL.md and the reference files for the running version

#### Scenario: Existing folder
- **WHEN** the target folder already exists and `--force` is not given
- **THEN** DBUDoctor exits with code 2 and changes nothing

#### Scenario: DBUDoctor not installed
- **WHEN** an agent follows the skill and `dbudoctor` is not on `PATH`
- **THEN** the skill's install instructions name `pip install dbudoctor`, the `dotnet tool` package and the release binaries
