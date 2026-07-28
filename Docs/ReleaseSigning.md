# Release Signing

BaChen AI Launcher uses two independent signing layers:

1. RSA-SHA256 signs `launcher-update.json`. The public key is embedded in the
   launcher; the private key must never enter Git.
2. Authenticode signs Windows EXE files. This requires an OV or EV certificate
   from a trusted certificate authority or signing service.

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

## Authenticode release gate

The current development executable is unsigned. Before a public production
release, configure `signtool.exe` with SHA-256 file and timestamp digests and a
trusted RFC 3161 timestamp server. Verify every release artifact with
`Get-AuthenticodeSignature` after signing and before uploading it.
