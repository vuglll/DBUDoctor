# agent-skill Specification

## Purpose

An agent skill, generated from rule metadata, that teaches coding agents to run DBUDoctor on a bundle change, read its report and fix cost findings before opening a pull request.

## Requirements

### Requirement: Skill generated from rule metadata
The system SHALL generate an agent skill at build time in the Agent Skills layout:
- `skills/dbudoctor/SKILL.md`, with YAML front matter: `name: dbudoctor` and a `description` that says when to use it;
- `references/rules.md`;
- `references/output.md`.

Per-rule content SHALL come only from rule metadata: the title, when the rule fires, how to fix it and when to suppress it. The build SHALL fail when a rule lacks any of these fields.

#### Scenario: New rule appears automatically
- **WHEN** a rule DBU002 with complete metadata is added
- **THEN** the next build lists DBU002 in `references/rules.md` with its fix and suppression guidance, without manual edits to the skill

#### Scenario: Missing fix guidance
- **WHEN** a rule has no fix guidance in its metadata
- **THEN** the build fails and names the rule

### Requirement: Skill says when and how to run DBUDoctor
The generated SKILL.md SHALL tell an agent to run `dbudoctor diff --format json` after editing bundle configuration, before opening a pull request, and when asked what a change will cost.

#### Scenario: Required sections
- **WHEN** the skill is generated
- **THEN** SKILL.md contains the sections "When to use", "How to run", "Reading results", "Fixing findings" and "Guardrails"

### Requirement: Skill says how to report results
The generated SKILL.md SHALL tell an agent to:
- show the confidence with every monthly figure;
- never present Unknown or Low-confidence figures as facts;
- give rate-only changes as hourly rates.

It SHALL also explain what each exit code means.

#### Scenario: Reporting guidance present
- **WHEN** the skill is generated
- **THEN** its "Reading results" section names the four confidence levels and the exit codes 0, 1 and 2

### Requirement: Skill carries fixes and a guardrail
The generated SKILL.md SHALL point to `references/rules.md` for the fix for each rule. It SHALL tell an agent never to weaken production configuration only to silence a finding. Instead, the agent SHALL suggest a suppression with a reason, and only with the user's agreement.

#### Scenario: Guardrail present
- **WHEN** the skill is generated
- **THEN** its "Guardrails" section contains the rule against weakening production configuration to silence a finding

### Requirement: Size budget
SKILL.md SHALL stay under 200 lines. Rule details and output fields SHALL live in the reference files, so an agent loads them only when needed.

#### Scenario: Growing rule set
- **WHEN** the rule set grows from 3 to 19 rules
- **THEN** SKILL.md stays under 200 lines, and only `references/rules.md` grows

#### Scenario: Budget exceeded
- **WHEN** a change makes SKILL.md 200 lines or longer
- **THEN** the build fails and reports the line count

### Requirement: Committed copy stays current
The generated skill SHALL be committed under `skills/dbudoctor/`, and CI SHALL fail when the committed copy differs from a fresh generation.

#### Scenario: Stale skill
- **WHEN** a pull request changes rule metadata without regenerating the skill
- **THEN** CI fails and names the files that differ

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
