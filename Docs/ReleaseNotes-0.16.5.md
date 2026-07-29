# BaChen AI Launcher 0.16.5

This maintenance release fixes `uv` environment installation on clean Windows computers.

## Fixes

- Moved `uv.exe` into a launcher-managed external tools environment instead of installing it inside the plugin environment that it needs to replace.
- Pinned `uv sync` to the launcher's managed Python 3.12 interpreter, preventing a repository `.python-version` or system Python 3.13 installation from silently changing the selected runtime.
- Removed the self-hosted `uv sync --active` flow that could fail with Windows `Access denied (os error 5)` while deleting `.venv\Scripts`.
- Reuses the external `uv` tool across plugins while keeping each plugin's `.venv` isolated.
- Increased launcher self-test coverage to 84 checks.

## Recovery

Users affected by 0.16.4 can install 0.16.5 and analyze the same repository again. The verified source download is reused, and the incomplete plugin environment is repaired by the external `uv` synchronization step.
