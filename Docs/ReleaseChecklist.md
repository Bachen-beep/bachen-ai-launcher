# Release Checklist

## Version and source

- Use `v<version>-stage2-preview` for previews, `v<version>-stage2-rc<n>` for
  release candidates, and `v<version>` for stable releases.
- Match the project version to the numeric tag version.
- Add `Docs/ReleaseNotes-<tag-without-v>.md`; the workflow rejects missing notes.
- Confirm the release commit is on `main` and Windows CI passes.

## Security and artifacts

- Keep `LAUNCHER_UPDATE_PRIVATE_KEY_BASE64` in GitHub Actions secrets only.
- Back up the update signing key in an encrypted credential vault.
- Build only the launcher, installer, signed update manifest, checksums, and SBOM.
- Generate and publish the SPDX SBOM for every release.
- Require Artifact Attestation for both executable files.
- Require the post-publication SHA-256 and Attestation verification step.
- State the SmartScreen limitation when Authenticode is unavailable.
- Follow `UpdateSigningKeyRotation.md` for scheduled rotation or key compromise.

## Test evidence

- Complete every required row in `CleanMachineTestMatrix.md`.
- Record Windows version, standard-user status, artifact SHA-256, date, result,
  and retained diagnostic log location.
- Verify install, launch, update, rollback, and uninstall without starting an AI
  model unless that test explicitly requires one.

## Promotion

- Preview validates the distribution workflow and preview update channel.
- Release candidate requires the complete clean-machine matrix.
- Stable requires successful candidate evidence and no unresolved release blocker.
