# BaChen AI Launcher 0.16.2 Stage 6 Preview

This preview focuses on clean-machine reliability and plugin management.

## Changes

- Removed legacy Woosh, Stable Audio 3, and IndexTTS path and port controls from global settings.
- Added an Uninstall button beside Launch and Stop in the selected plugin panel.
- Changed the main NVIDIA VRAM display to GiB while retaining exact `nvidia-smi` MiB values in tooltips and diagnostics.
- Changed new installations to the Preview update channel and added Preview fallback when no Stable release exists.
- Added a clear validation error when a GitHub-imported Python plugin has no launch entry arguments.
- Added copyable launch failure details with exit code, executable, arguments, working directory, port, and recent errors.

## Installation Notes

- A clean installation contains no preinstalled AI plugins.
- Generic GitHub imports still require a correct repository-specific executable, launch arguments, dependencies, and model weights.
- Use signed catalog packages when available for fully managed installation.
