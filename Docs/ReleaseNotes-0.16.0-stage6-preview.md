# BaChen AI Launcher 0.16.0 Stage 6 Preview

This preview adds a first-run installation experience for clean Windows systems.

## Highlights

- Six-step first-run wizard with resumable progress and persisted state.
- Managed Python 3.12.10 x64 runtime with no PATH changes or administrator requirement.
- Signed plugin catalog with an offline bundled fallback and launch-time tamper checks.
- Manifest v6 support for separately verified model asset packages.
- Real pinned Woosh-DFlow source and official model asset metadata.
- Plugin catalog published alongside the launcher and covered by `SHA256SUMS.txt`.

## Preview limitations

- The automated release qualification uses small fixture packages and does not download the full Woosh model set.
- Hugging Face and other gated providers still require users to create their own accounts and accept upstream terms.
- GitHub Artifact Attestation proves release provenance but does not provide Authenticode publisher reputation or suppress Windows SmartScreen warnings.
