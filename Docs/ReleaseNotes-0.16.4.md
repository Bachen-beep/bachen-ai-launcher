# BaChen AI Launcher 0.16.4

This release replaces the advanced GitHub import form with repository analysis and one confirmation step.

## Changes

- Kept clean installations empty: no AI plugin, model source, environment, or model weights are bundled.
- Reduced first-time GitHub import input to the repository URL and user-selected install directory.
- Added local repository analysis for Python metadata, common and Gradio launch entries, runtime versions, dependency managers, categories, ports, and resource guidance.
- Added multi-entry selection with an automatic recommendation. Woosh repositories recommend DFlow while retaining Flow and VFlow choices.
- Added automatic `pip` or `uv` environment setup, including CPU/CUDA extra selection when declared by the project.
- Added a single bilingual confirmation window before environment installation and plugin registration.
- Wrapped long install paths, launch arguments, and descriptions instead of visually truncating them.
- Restored incomplete cached GitHub source automatically from the verified source archive.
- Added explicit notices for Hugging Face authorization and externally hosted model weights.
- Increased launcher self-test coverage to 83 checks.

## Security

- README commands are treated as analysis evidence only and are never executed directly.
- Unknown repositories without a safely detected launch entry are rejected instead of receiving a guessed command.
- GitHub imports remain pinned to an immutable commit and extracted with path traversal protection.

## Notes

- Repository analysis configures the launcher and Python environment. A project may still require model-license acceptance, provider login, or separately hosted weights before its first successful launch.
- Signed plugin packages remain the strongest option when a publisher provides verified commands, hashes, and model assets.
