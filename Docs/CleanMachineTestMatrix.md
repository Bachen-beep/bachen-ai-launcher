# Clean-Machine Test Matrix

## Completed locally on 2026-07-28

| Scenario | Result | Evidence |
| --- | --- | --- |
| Release build on Windows 11 x64 | Pass | 0 warnings, 0 errors |
| Self-contained EXE without model launch | Pass | 14 launcher self-tests |
| Per-user silent install to isolated directory | Pass | Installed EXE ran all self-tests |
| Silent uninstall with data preservation default | Pass | Program files removed; explicit test report remained until test cleanup |
| English and Simplified Chinese installer compile | Pass | Inno Setup 6.7.3 compiled both language resources |
| Signed update manifest generation and tamper rejection | Pass | RSA signature and mutation self-tests |
| Diagnostic secret redaction | Pass | Token fixture absent from exported report |

## Required before production

| Environment or scenario | Status | Acceptance |
| --- | --- | --- |
| Clean Windows 10 x64 standard user | Pending external VM | Install, launch, upgrade, rollback, uninstall |
| Clean Windows 11 x64 standard user | Pending external VM | Same as Windows 10; no development SDK installed |
| Chinese username and path | Pending external account/VM | Install and plugin configuration succeed |
| No NVIDIA GPU | Pending external VM | Launcher opens and reports GPU limitation without crashing |
| Offline and proxy-restricted network | Partially covered | Clear update error; installed plugins remain usable |
| Interrupted launcher update | Pending release asset | Current EXE restored or preserved |
| Authenticode reputation | Blocked by certificate | Both launcher and installer show a valid publisher |

Do not promote a preview tag to a production release until all pending rows have
recorded machine details, artifact hash, date, result, and any retained logs.
