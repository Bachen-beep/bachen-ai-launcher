# Stage Five Closeout

## Completed scope

- Download plugins from a primary HTTPS source or ordered mirrors.
- Show total size, transferred bytes, percentage, speed, and current state.
- Pause, resume with HTTP Range, retry transient failures, and reuse a verified-size completed download.
- Check disk space and GPU availability before installation.
- Create a Python virtual environment and optionally install a requirements file.
- Guide users to the official Hugging Face authorization page without accepting terms for them.
- Verify both Hugging Face identity and actual gated model file access.
- Store optional read-only tokens in Windows Credential Manager and provide explicit deletion.
- Clean failed installation staging while preserving completed package downloads.
- Keep signed manifest v2-v4 compatibility through Manifest v5.

## Acceptance evidence

The launcher self-test installs a signed Python plugin into a new data directory, creates `.venv`, verifies the plugin dependencies, exercises resumable download and retry, validates authorization classifications, and confirms failed-install cleanup and credential deletion.
