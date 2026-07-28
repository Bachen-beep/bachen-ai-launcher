# BaChen AI Launcher 0.13.1 Stage Two Preview

This preview supersedes the incomplete immutable `0.13.0` preview and validates
the final Stage Two distribution and recovery controls.

## Highlights

- Thirty launcher self-tests covering offline and blocked-proxy behavior,
  interrupted downloads, failed replacement recovery, rollback preservation,
  and no-GPU dependency handling.
- Automated distribution qualification under a temporary non-administrator
  Windows account with a Unicode username and Unicode installation paths.
- Silent install, isolated UI startup, installed self-tests, uninstall, and
  default user-data preservation verified before publication.
- SPDX JSON SBOM covered by the published SHA-256 checksum file.
- Draft-first asset upload followed by one-time publication, compatible with
  GitHub Release Immutability.
- GitHub Artifact Attestation for the launcher and installer. Authenticode
  remains optional.

## Verify provenance

```powershell
gh attestation verify ".\BaChen.AI.Launcher.exe" --repo Bachen-beep/bachen-ai-launcher
gh attestation verify ".\BaChen-AI-Launcher-Setup-0.13.1.exe" --repo Bachen-beep/bachen-ai-launcher
```

Artifact Attestation proves which public repository, commit, and GitHub Actions
workflow produced each executable. It does not provide Authenticode publisher
identity and does not suppress Windows SmartScreen warnings.

## Preview status

Promotion to release candidate and stable remains blocked until the final
external Windows client row in the clean-machine matrix is recorded.
