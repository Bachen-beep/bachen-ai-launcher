# BaChen AI Launcher 0.13.2 Stage Two Preview

This preview fixes update-channel status reporting.

## Fixes

- A Stable channel with no published Stable release now reports that no Stable
  version is available instead of falsely reporting a GitHub network failure.
- An empty Preview release list reports that no Preview version is available.
- Real timeouts, DNS failures, proxy failures, and GitHub server errors remain
  classified as network errors.
- Added regression self-tests for missing Stable, missing Preview, and server
  failure classification.

## Verify provenance

```powershell
gh attestation verify ".\BaChen.AI.Launcher.exe" --repo Bachen-beep/bachen-ai-launcher
gh attestation verify ".\BaChen-AI-Launcher-Setup-0.13.2.exe" --repo Bachen-beep/bachen-ai-launcher
```
