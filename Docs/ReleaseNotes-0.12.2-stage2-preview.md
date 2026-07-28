# BaChen AI Launcher 0.12.2 Stage Two Preview

This preview closes the remaining Stage Two release-automation gaps.

## Highlights

- Stable and Preview launcher update channels with persistent user selection.
- Version-specific release notes required and selected from the Git tag.
- Post-publication download, SHA-256, and GitHub Artifact Attestation checks.
- Dependabot monitoring for GitHub Actions dependencies.
- Updated release policy treating Artifact Attestation as the required free
  provenance control and Authenticode as an optional future enhancement.

## Verify provenance

```powershell
gh attestation verify ".\BaChen.AI.Launcher.exe" --repo Bachen-beep/bachen-ai-launcher
gh attestation verify ".\BaChen-AI-Launcher-Setup-0.12.2.exe" --repo Bachen-beep/bachen-ai-launcher
```

Artifact Attestation proves which public repository, commit, and GitHub Actions
workflow produced each file. It does not provide Authenticode publisher identity
and does not suppress Windows SmartScreen warnings.
