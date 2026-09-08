# Decisions

<!-- Key decisions made during planning — one bullet per decision. Extended rationale goes in assets/decisions/<topic>.md. -->

- Topology + spatial persist: fingerprint = sorted CCD DevicePaths; unknown set assigned 1..N by (left, top, DevicePath); known set reuses numbers. Overlay host is not part of the fingerprint.
- Display switch is allowed any time the overlay is visible, not only at L1.
- After switch: new L1, keep current mode name, cursor at target center, cancel app-scope and drag.
- Live topology/`WM_DISPLAYCHANGE` while overlay is visible dismisses (`DeactivateOverlay`); next activation re-enumerates.
- Default `macros.slotKeys` become F1–F10. Config 6→7 migrates only the exact old default D0–D9. Custom slot keys kept unless they collide with D1–D9 (validation error).
- D1–D9 are reserved display-switch keys while this feature exists; consumed on the overlay before `MacroHandler`.
- App-scope clips to the active navigation display’s `rcMonitor`, not the primary display.
- Satellite digit size: 40% of min DIP side, clamp 96–400; theme label colors; outlined text like grid labels.
- Virtual-desktop `SendInput` (`MOUSEEVENTF_VIRTUALDESK`) is in this plan; macros stay primary-size-locked.
- Per-display WPF windows, not one window spanning the virtual desktop.
- Clone/mirror, click-to-select, EDID-across-docks, HUD follow, and in-app display UI are non-goals.
