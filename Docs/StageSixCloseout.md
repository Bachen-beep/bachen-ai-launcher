# Stage Six Closeout

## Completed scope

- Added a six-step first-run wizard for storage, hardware review, plugin selection, license acceptance, installation progress, and final verification.
- Added a pinned Python 3.12.10 x64 managed runtime with exact size and SHA-256 verification.
- Added Manifest v6 model asset packages with HTTPS mirrors, resumable downloads, size checks, SHA-256 checks, safe extraction, and disk preflight accounting.
- Added a signed plugin index with remote loading and a verified bundled fallback.
- Added one real Woosh-DFlow source pinned to upstream commit `88006c57774a85bede9f87733c019664410d6f4e` and three official model assets pinned by size and SHA-256.
- Added installed signed-catalog evidence and launch-time command tamper detection.
- Preserved existing users by defaulting migrated settings to completed while new settings open the wizard automatically.
- Added a maintenance action to rerun first-time setup.

## Acceptance evidence

The Release build and automated launcher self-test pass. Tests cover signed-index fallback and tamper rejection, Manifest v6 signature tampering, small verified asset installation, disk accounting, managed runtime metadata, first-run state persistence, and installed command trust verification. The automated suite intentionally does not download the real Woosh model set.

## Remaining external checks

- Run the installer and first-run wizard under a clean standard Windows user profile.
- Perform one full Woosh installation with approximately 3.4 GB of model downloads and confirm the upstream Python dependency set on the target GPU.
- Add a separately backed-up plugin-index signing secret to GitHub only if future workflows need to generate a new index in CI. The current workflow publishes the already signed repository index and never requires the private key.
