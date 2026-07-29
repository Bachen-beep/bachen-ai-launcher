# BaChen AI Launcher 0.16.6

This interaction update changes the runtime log drawer to expand downward.

## Changes

- Keeps the collapsed runtime-log header in place while expanding the window and log content downward.
- Preserves the existing animated transition, filters, copy, clear, and collapse controls.
- Adjusts only the overflow distance when the window is close to the bottom of the working area, keeping the expanded log visible above the taskbar.
- Leaves maximized window bounds unchanged.
- Increased launcher self-test coverage to 86 checks.
