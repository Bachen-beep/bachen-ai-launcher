# Update Signing Key Rotation

## Scope

The RSA update key signs `launcher-update.json`. It is separate from GitHub
Artifact Attestation and optional Authenticode signing. Never store the private
key in the repository, release assets, diagnostics, or launcher installation.

## Routine rotation

1. Generate a new RSA 3072-bit or stronger key pair on an offline workstation.
2. Store two encrypted private-key backups in separate credential vaults.
3. Add the new public key and key ID to the launcher while retaining the current
   public key.
4. Publish a transition release signed by the current key. Confirm that users
   have received the launcher containing both trusted public keys.
5. Change `LAUNCHER_UPDATE_PRIVATE_KEY_BASE64` to the new private key and publish
   a preview release signed by the new key.
6. Verify update installation from the transition release, then publish the
   release candidate and stable release.
7. Retain the old public key for one compatibility window. Destroy active copies
   of the old private key after the rollback window expires.

Never replace the embedded public key and signing private key in the same release.
Older launchers would otherwise reject the new manifest before they can update.

## Suspected compromise

1. Stop release workflows and remove the compromised Actions secret.
2. Preserve workflow audit logs and record the last known-good release hash.
3. Publish a GitHub security advisory identifying affected versions and hashes.
4. Ship a manually installed recovery release that removes the compromised
   public key and introduces a new key. Artifact Attestation must verify its
   repository and workflow origin.
5. Resume automatic updates only after preview and clean-machine qualification.

Artifact Attestation cannot revoke a leaked RSA update key. It provides build
provenance so users can distinguish an official recovery build from an artifact
created outside the repository workflow.
