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

## Stage Two closeout verification on 2026-07-28

| Scenario | Result | Evidence |
| --- | --- | --- |
| Published `0.12.2` standalone EXE | Pass | 18 self-tests; SHA-256 `5E4A253EAE3A1FE007FA9159DF442EB229F0A05D0DF13F432DCD229D0F8FE1D3` |
| Published `0.12.2` installer | Pass | Silent per-user install to an isolated path containing Chinese characters and spaces |
| Isolated UI startup | Pass | Separate config and data environment overrides; launcher remained running without starting an AI model |
| Installed EXE self-test | Pass | All 18 self-tests completed from the isolated installation |
| Silent uninstall and preservation | Pass | Program removed; explicit marker in isolated configuration directory preserved |
| Post-publication provenance | Pass | Workflow re-downloaded assets, verified SHA-256, and verified both GitHub Artifact Attestations |

## Automated qualification for `0.13.1` on 2026-07-28

| Scenario | Result | Evidence |
| --- | --- | --- |
| Current Windows runner | Pass | Build, 30 self-tests, single-file publish, and release-boundary checks |
| Windows 2022 runner | Pass | Build, 30 self-tests, and self-contained EXE execution |
| Standard Windows user | Pass | Temporary local account report recorded `administrator=False` |
| Unicode username and paths | Pass | Unicode account, config, data, and install paths completed install, UI, self-test, and uninstall |
| Offline and blocked proxy | Pass | Update failure isolation preserved existing plugin data in both cases |
| No-GPU dependency behavior | Pass | Enforced CUDA dependency reports unavailable without crashing |
| Interrupted download | Pass | Partial staging directory removed and existing data preserved |
| Interrupted replacement | Pass | Previous executable automatically restored; replacement staging removed |
| Rollback backup | Pass | Previous executable content preserved before successful replacement |
| Immutable release publication | Pass | Draft-first upload published all assets once, then post-publication verification succeeded |
| SPDX SBOM | Pass | `BaChen-AI-Launcher.spdx.json` published and covered by `SHA256SUMS.txt` |
| Artifact Attestation | Pass | Launcher and installer provenance verified after publishing `v0.13.1-stage2-preview` |

## Required before production

| Environment or scenario | Status | Acceptance |
| --- | --- | --- |
| Clean Windows 10 x64 standard user | Pending external VM | Install, launch, upgrade, rollback, uninstall |
| Clean Windows 11 x64 standard user | Pending external VM | Same as Windows 10; no development SDK installed |
| Chinese username and path | Pass | Standard-user distribution qualification completed with Unicode username and paths |
| No NVIDIA GPU | Automated pass; external recommended | Enforced dependency and failure behavior pass; physical no-GPU client remains a compatibility observation |
| Offline and proxy-restricted network | Pass | Clear update failure; installed plugin fixture remained usable |
| Interrupted launcher update | Pass | Partial download cleaned; failed replacement restored current EXE; rollback backup retained |
| Artifact Attestation | Pass | Launcher and installer provenance verified for `v0.13.1-stage2-preview` |
| Windows SmartScreen disclosure | Required for each release | Release notes state that Artifact Attestation does not provide publisher reputation |

Do not promote a preview tag to a production release until all pending rows have
recorded machine details, artifact hash, date, result, and any retained logs.
Authenticode remains optional and does not replace these test results.
