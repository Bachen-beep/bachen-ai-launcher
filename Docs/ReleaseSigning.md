# Release Signing

BaChen AI Launcher uses two independent signing layers:

1. RSA-SHA256 signs `launcher-update.json`. The public key is embedded in the
   launcher; the private key must never enter Git.
2. GitHub Artifact Attestation records build provenance for each Windows EXE.
3. Optional Authenticode signing adds Windows publisher identity and reputation.

## Update manifest key

The current public key id is `bachen-launcher-release-2026`. The corresponding
private key is stored only in the release operator's protected local profile.
Back it up to an encrypted credential vault before the first public release.

For GitHub Actions, add the PKCS#8 private key as a base64-encoded repository
secret named `LAUNCHER_UPDATE_PRIVATE_KEY_BASE64`. Do not store the key, its
base64 value, or a passphrase in repository files, Actions variables, logs, or
release assets.

Generate a signed manifest locally:

```powershell
.\scripts\New-UpdateManifest.ps1 `
  -Version "0.12.0" `
  -ExecutablePath ".\artifacts\release\BaChen AI Launcher.exe" `
  -DownloadUrl "https://github.com/Bachen-beep/bachen-ai-launcher/releases/download/v0.12.0/BaChen.AI.Launcher.exe" `
  -ReleaseNotesUrl "https://github.com/Bachen-beep/bachen-ai-launcher/releases/tag/v0.12.0" `
  -PrivateKeyPath "<protected-private-key-path>" `
  -OutputPath ".\artifacts\launcher-update.json"
```

## Artifact Attestation release gate

Every preview, release candidate, and stable tag must attest both the standalone
launcher and installer. The release workflow then downloads the published files,
recalculates their SHA-256 values, and runs `gh attestation verify` against the
public repository. A failed verification fails the workflow.

Users can verify downloaded files with:

```powershell
gh attestation verify ".\BaChen.AI.Launcher.exe" --repo Bachen-beep/bachen-ai-launcher
gh attestation verify ".\BaChen-AI-Launcher-Setup-<version>.exe" --repo Bachen-beep/bachen-ai-launcher
```

## Optional Authenticode

The current executable is not Authenticode signed. A production release is
allowed when Artifact Attestation and the remaining release gates pass, but its
release notes and documentation must state that Windows SmartScreen can show an
unknown-publisher warning. If a trusted certificate is added later, configure
`signtool.exe` with SHA-256 file and timestamp digests and a trusted RFC 3161
timestamp server, then verify every artifact before publishing it.
