# Stage Two Release Plan

## Distribution boundary

- Publish the launcher and installer only.
- Do not package AI plugins, Python environments, model weights, generated
  media, credentials, local paths, configuration, logs, or user data.
- Store mutable data outside the installation directory.
- Preserve `%LocalAppData%\BaChen AI Launcher` and the configured data root on
  normal uninstall unless the user explicitly removes them.

## Release outputs

1. `BaChen AI Launcher.exe`: self-contained Windows x64 executable.
2. `BaChen-AI-Launcher-Setup-<version>.exe`: lightweight installer.
3. `launcher-update.json`: signed update metadata for the standalone EXE.
4. SHA-256 checksum file and GitHub release notes.

## Acceptance criteria

- Windows CI restores, builds, and runs all launcher self-tests.
- Tag builds publish a single-file launcher and compile the Inno Setup package.
- Update metadata is RSA-SHA256 signed and contains version, minimum compatible
  version, download URL, SHA-256, release notes URL, and publication time.
- The launcher rejects unsigned, tampered, downgraded, or hash-mismatched
  updates before replacing the executable.
- Replacement is performed by a separate process, keeps one backup, and can
  restore that backup after a failed replacement.
- Installer upgrades preserve configuration and plugin data by default.
- Persistent logs rotate, and diagnostics can be exported with sensitive paths
  and environment values redacted.
- Preview and production releases must create GitHub Artifact Attestations for
  both executable artifacts so users can verify repository and workflow origin.
- Production releases may be published without Authenticode when the release
  notes clearly disclose the Windows SmartScreen limitation. Authenticode is a
  future publisher-identity enhancement, not the Stage Two release gate.
- The release workflow must download its published assets, verify all listed
  SHA-256 values, and verify both Artifact Attestations before succeeding.

## Clean-machine matrix

Test on Windows 10 and Windows 11 with standard-user permissions. Cover a
machine without .NET, Python, Git, NVIDIA hardware, or network access; paths
containing Chinese and spaces; occupied plugin ports; low system memory and
VRAM; upgrade over an existing installation; interrupted update; rollback;
and uninstall while preserving data.

## Release gate

Do not publish a production release until all required clean-machine rows have
recorded evidence. RSA signatures protect update metadata, SHA-256 protects the
downloaded bytes, and GitHub Artifact Attestation proves build provenance.
These controls do not provide Authenticode publisher identity or suppress
Windows SmartScreen warnings.
