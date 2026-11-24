# Security policy

DBUDoctor runs in pull request pipelines and can hold credentials: a GitHub token to post its comment, and, with calibration, a Databricks token or OAuth credentials to read `system.billing.usage`. Reports about how it handles them are welcome.

## Reporting a vulnerability

Please do not open a public issue. Report it privately through GitHub: **Security → Report a vulnerability** on https://github.com/vuglll/DBUDoctor (a private security advisory).

Include the version (`dbudoctor --version`), how you run it (CLI, GitHub Action, pip, dotnet tool), and the steps to reproduce. You will get an answer within 7 days. A fix is released as a patch version, and the advisory is published with credit unless you prefer otherwise.

## Supported versions

Only the latest release gets security fixes. DBUDoctor is 0.x: upgrade to the latest version before reporting.

## What is in scope

- Leaking a token or secret: in reports, PR comments, logs, the calibration cache or error messages.
- SQL injection through bundle content into calibration queries.
- The GitHub Action running untrusted code from a fork with write permissions.
- Tampered release artifacts or packages (binaries are published with `SHA256SUMS`; nuget.org and PyPI use Trusted Publishing).

Price or estimate errors are not security issues: open a normal issue for those.

## How DBUDoctor handles credentials

- It never prints tokens, and the calibration cache stores usage figures only, never tokens.
- Calibration runs read-only `SELECT` statements; resource IDs are validated and passed as statement parameters.
- On pull requests from forks, the Action falls back to the job summary instead of commenting.
