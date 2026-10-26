# Desktop Roaches Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build a portable Windows EXE with a Chinese control panel and realistic animated, click-through desktop cockroaches.

**Architecture:** A WinForms control panel owns one small layered, nonactivating, topmost window per insect. A shared timer advances independent motion models and animation; closing the panel disposes all windows. Image assets are embedded in the executable.

**Tech Stack:** C# / .NET Framework 4.x, WinForms, System.Drawing, Win32 layered windows; installed Framework csc compiler.

---

## Accepted behavior

- Each click appends the configured batch (default 5); 100 total maximum. No automatic spawning at startup.
- Primary monitor working area only; account for DPI, taskbar and display changes. Clamp whole sprite window, including appendages, inside bounds.
- Independent headings, random speeds and explicit temporary zero-speed states. Reflect or select inward headings on edge contact.
- Realistic transparent four-frame walking sprite, oriented along heading; freeze gait when stationary.
- Mouse clicks pass through all insect windows. Control panel stays usable.
- Pause/resume, clear all, exit. Ctrl+Alt+C clears and Ctrl+Alt+Q exits when registration succeeds; report registration conflicts.
- No persistence, network, startup registration or background activity after exit.

## Tasks and verification

1. Create `src/Motion.cs` and `tests/MotionTests.cs`. Use deterministic random seeds to verify batch limits, explicit stop/resume behavior, finite positions and velocities, narrow bounds, direction after collisions, long frame gaps and display resizing. Compile and run console test executable.
2. Generate a transparent realistic sprite sheet with the built-in ImageGen tool. Save it in `assets/roach-sheet.png`, inspect its transparency and four cells, and save its exact prompt in `assets/prompt.txt`.
3. Create `src/Program.cs` with a control panel, embedded sprite loader, layered windows, shared timer and hotkeys. Document Win32 interop and key rendering/cleanup code.
4. Create `build.ps1`, embed the sprite, compile release as a Windows GUI executable. Deliver `桌面蟑螂.exe` and `使用说明.txt`, with source included separately in the same project directory.
5. Run automated motion tests and a native integration smoke mode. Verify real window styles, adding/clearing, pause, hotkey dispatch, display-bound handling and disposal; save a control-panel/sprite preview for visual inspection. Package release ZIP and report any verification limitations honestly.

## Implementation choices

Use the current non-repository workspace in a dedicated subdirectory; no Git worktree or commits. The user has explicitly approved implementation in this session. Keep UI simple and use no third-party runtime packages.
