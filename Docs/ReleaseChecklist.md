# Release Checklist

## Version and source

- Use `v<version>-stage6-preview` for the current preview, `v<version>-stage6-rc<n>`
  for current release candidates, and `v<version>` for stable releases. Existing
  Stage Two tags remain supported for historical rebuilds.
- Match the project version to the numeric tag version.
- Add `Docs/ReleaseNotes-<tag-without-v>.md`; the workflow rejects missing notes.
- Confirm the release commit is on `main` and Windows CI passes.
- Confirm `Catalog/plugin-index.json` validates against the embedded plugin-index
  public key and is included in `SHA256SUMS.txt`.

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
- Run `scripts/Test-Distribution.ps1` against the final launcher and installer;
  retain its report with the release evidence.
- Run `scripts/Test-StandardUserDistribution.ps1` from an elevated qualification
  host; verify that the retained report records `administrator=False`.

## Promotion

- Preview validates the distribution workflow and preview update channel.
- Release candidate requires the complete clean-machine matrix.
- Stable requires successful candidate evidence and no unresolved release blocker.
- Stage Two engineering closeout and production promotion are tracked separately
  in `StageTwoCloseout.md`; engineering completion must not be used to bypass the
  clean-client release gate.
