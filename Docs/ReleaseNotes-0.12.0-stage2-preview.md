# BaChen AI Launcher 0.12.0 Stage Two Preview

This preview introduces the distribution, installation, update, rollback, and
diagnostic foundation needed before a public production release.

## Highlights

- Lightweight per-user installer; AI plugins and model weights are not bundled.
- Signed launcher update manifests with SHA-256 download verification.
- Automatic update checks, remind-later, skip-version, backup, and rollback.
- Plugin manifest v3 with explicit upstream license acceptance.
- English and Simplified Chinese installer interface.
- Optional data removal during uninstall; preservation remains the default.
- Persistent rotating logs, crash reports, and redacted diagnostic export.
- Windows CI, reproducible release workflow, and production signing gate.

## Installation

Download `BaChen-AI-Launcher-Setup-0.12.0.exe`. The installer adds the launcher
for the current Windows user and does not require administrator privileges.
Existing plugin locations and model weights are not moved.

Standalone users may download `BaChen.AI.Launcher.exe` instead.

## Preview warning

This preview may be published without Authenticode while certificate enrollment
is in progress. Verify `SHA256SUMS.txt` before running it. Production tags are
blocked unless a valid Windows signing certificate is configured.

## Data and licenses

Settings are stored under `%LocalAppData%\BaChen AI Launcher`; the default data
directory is `%UserProfile%\Documents\BaChen AI Launcher Data`. Uninstall keeps
both unless the user explicitly chooses removal.

Woosh, Stable Audio, IndexTTS, Python environments, model weights, and generated
media are not included. Each plugin remains governed by its upstream license.
