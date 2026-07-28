# GitHub Security Baseline

Recorded on 2026-07-28 for `Bachen-beep/bachen-ai-launcher`.

## Repository controls

- Repository visibility: Public.
- Release immutability: Enabled.
- `main` classic branch protection: Enabled.
- Force pushes to `main`: Disabled.
- Deletion of `main`: Disabled.
- Pull requests and required status checks: Not yet mandatory for the current
  single-maintainer workflow.

## Security and dependency controls

- Private vulnerability reporting: Enabled.
- Dependency graph: Enabled.
- Dependabot version updates for GitHub Actions: Weekly.
- Dependabot vulnerability alerts: Enabled.
- Dependabot malware alerts: Enabled.
- Dependabot security updates: Enabled.
- Grouped security updates: Enabled.
- Public-repository partner secret detection: Active by GitHub default.
- Paid Secret Protection: Not enabled.

## Promotion rule

Before the first stable release or adding a collaborator, require Pull Requests
for `main` and make Windows CI a required status check. Keep direct administrator
push available only while the repository has a single maintainer and preview
releases remain under active development.
