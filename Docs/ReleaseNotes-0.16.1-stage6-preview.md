# BaChen AI Launcher 0.16.1 Stage 6 Preview

This corrective preview aligns clean-machine behavior with the plugin-managed launcher design.

## Fixes

- Clean installations now start with an empty plugin library and no placeholder built-in models.
- Existing legacy entries are retained only when their local deployment directory actually exists.
- The header reads the real NVIDIA GPU model from `nvidia-smi`; multi-GPU systems select the device with the largest VRAM capacity.
- Add Model now resolves a GitHub branch to an immutable commit, downloads and safely extracts that commit, and optionally creates a managed Python environment.
- Model registration happens only after the configured executable and required files pass validation.
- Launch preflight now checks declared dependencies and Python processes use deterministic UTF-8 logging.
- Early process exits include the process exit code in persistent diagnostics.

## Compatibility

The installer still contains only the launcher. Model source, Python environments, dependencies, and weights are downloaded after the user explicitly adds a GitHub repository.
