# BaChen AI Launcher 0.12.1 Stage Two Preview

This preview adds public build provenance to the Stage Two distribution stack.

## Highlights

- GitHub Artifact Attestation for the standalone launcher and installer.
- Signed launcher update manifests with SHA-256 download verification.
- Automatic update checks, remind-later, skip-version, backup, and rollback.
- Plugin manifest v3 with explicit upstream license acceptance.
- English and Simplified Chinese installer interface.
- Optional data removal during uninstall; preservation remains the default.
- Persistent rotating logs, crash reports, and redacted diagnostic export.

## Verify provenance

Install GitHub CLI and verify either downloaded executable:

```powershell
gh attestation verify ".\BaChen.AI.Launcher.exe" --repo Bachen-beep/bachen-ai-launcher
gh attestation verify ".\BaChen-AI-Launcher-Setup-0.12.1.exe" --repo Bachen-beep/bachen-ai-launcher
```

Artifact Attestation proves which repository, commit, and GitHub Actions workflow
produced the files. It does not replace Authenticode and does not suppress
Windows SmartScreen warnings.

## Installation and data

The installer adds the launcher for the current Windows user and does not
require administrator privileges. AI plugins, Python environments, model
weights, and generated media are not bundled. Existing plugin locations and
model weights are not moved. Uninstall preserves settings and plugin data unless
the user explicitly chooses removal.
