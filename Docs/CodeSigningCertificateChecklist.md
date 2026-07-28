# Windows Code-Signing Certificate Checklist

Certificate enrollment requires the publisher's legal identity, contact details,
payment, and identity verification. These decisions cannot be automated by the
launcher repository.

## Operator actions

1. Choose an OV or EV code-signing certificate/provider supported by Microsoft
   SmartScreen and Windows `signtool.exe`.
2. Enroll using the final publisher name that should appear in Windows dialogs.
3. Prefer a hardware token or managed cloud-signing service. If a PFX is issued,
   keep it in an encrypted credential vault and use a unique strong password.
4. Add the certificate as `WINDOWS_SIGNING_PFX_BASE64` and its password as
   `WINDOWS_SIGNING_PFX_PASSWORD` in GitHub Actions repository secrets.
5. Run a preview tag, verify `Get-AuthenticodeSignature` reports `Valid`, then
   run the signed installer on clean Windows 10 and Windows 11 machines.

The release workflow signs the launcher before generating its update manifest,
signs the installer before final checksums, and refuses a non-preview tag when
the certificate secret is absent.
