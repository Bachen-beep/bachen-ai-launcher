# BaChen AI Launcher 0.16.3

This release improves GitHub connectivity and advanced repository imports.

## Changes

- Restored Stable as the default launcher update channel now that stable releases are available.
- Added automatic Stable fallback when the Preview GitHub API is unavailable or rate limited.
- Added an optional HTTP/HTTPS GitHub proxy and connection test in Launcher Settings.
- Added specific diagnostics for GitHub API rate limits, timeouts, proxy failures, and manual downloads.
- Accepted full GitHub URLs, `.git` URLs, SSH URLs, and `owner/repository` in Add Model.
- Added automatic repository name, default branch, managed directory, and common Python entry-point detection.
- Passed the configured WebUI port through `GRADIO_SERVER_PORT` and `PORT` for imported Python plugins.
- Increased high-DPI layout widths for settings labels and file-selection controls.

## Notes

- Generic GitHub imports remain an advanced workflow. Some repositories require model authorization, external weights, custom CUDA packages, or repository-specific arguments.
- Prefer signed BaChen plugin packages when available because their launch commands, dependencies, and model assets are verified in advance.
