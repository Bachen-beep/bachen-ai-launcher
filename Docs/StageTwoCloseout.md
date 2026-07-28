# Stage Two Closeout

## Engineering status

Stage Two engineering scope is complete as of `v0.13.1-stage2-preview`.

- Launcher and installer distribution are automated.
- Signed update metadata, SHA-256 verification, rollback, and interrupted-update
  recovery are covered by the 30-test launcher suite.
- Standard-user, Unicode path, isolated UI, install, uninstall, and data
  preservation qualification run before every release.
- Releases publish an SPDX SBOM and GitHub Artifact Attestations, upload all
  assets as a draft, publish once, and then re-download and verify every asset.
- Authenticode is optional and is not a Stage Two gate.

## Production promotion status

The preview is not a Stable production release. The remaining gate is external
environment evidence on clean Windows 10 and Windows 11 client virtual machines
without development SDKs. This cannot be replaced by a GitHub-hosted Windows
Server runner or by changing the matrix status without executing the tests.

After those two rows pass, publish `v0.13.1-stage2-rc1`. If the candidate has no
blocking defect, publish `v0.13.1` from the same qualified source commit.
