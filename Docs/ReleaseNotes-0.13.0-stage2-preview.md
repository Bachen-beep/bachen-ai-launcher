# BaChen AI Launcher 0.13.0 Stage Two Preview

This preview validates the final Stage Two distribution and recovery controls.

## Highlights

- Thirty launcher self-tests, including offline and blocked-proxy behavior,
  interrupted downloads, failed replacement recovery, rollback preservation,
  and no-GPU dependency handling.
- Automated standalone, UI, silent install, installed self-test, uninstall, and
  user-data preservation qualification in paths containing Unicode and spaces.
- Automated distribution qualification under a temporary non-administrator
  Windows account with a Unicode username.
- Current and Windows 2022 GitHub-hosted runner compatibility verification.
- SPDX JSON SBOM published with every release and covered by SHA-256 checksums.
- GitHub Artifact Attestation remains the required free provenance control.
  Authenticode remains optional.

## Verify provenance

```powershell
gh attestation verify ".\BaChen.AI.Launcher.exe" --repo Bachen-beep/bachen-ai-launcher
gh attestation verify ".\BaChen-AI-Launcher-Setup-0.13.0.exe" --repo Bachen-beep/bachen-ai-launcher
```

Artifact Attestation proves which public repository, commit, and GitHub Actions
workflow produced each executable. It does not provide Authenticode publisher
identity and does not suppress Windows SmartScreen warnings.

## Preview status

This preview validates the release workflow. Promotion to release candidate and
stable remains blocked until the clean-machine matrix records its final external
Windows client evidence.
