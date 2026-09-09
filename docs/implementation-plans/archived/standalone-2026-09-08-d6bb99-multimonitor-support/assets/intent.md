# Intent

## Goal

Klikety overlay works on every active display. Activation covers all screens: the display under the mouse cursor gets the existing navigation overlay; every other active display gets a large stable number. Pressing that number moves the navigation overlay there.

## Desired outcome

A laptop that docks at work and at home can target any connected display without the overlay being stuck on the primary monitor. Display numbers stay the same for a given connected set, including after unplug/replug of that same set, and do not reshuffle when the navigation overlay moves. Clicks and cursor moves land on the chosen display.

## Success signals

- Two-or-more-display activation shows one overlay window per active display.
- Cursor display shows the current navigation mode; other displays show 1–9; pressing another display’s digit relocates navigation and recenters the cursor.
- Same CCD DevicePath set → same numbers after overlay switch, overlay close/reopen, and reconnect of that set.
- Mouse actions on a non-primary display hit the intended physical pixel.
- Single-display machines behave as today (no numbers, no extra windows).

## Non-goals

- Clone/mirror topologies.
- Click-to-select a satellite (keys only).
- A Windows display-arrangement UI or in-app number editor.
- Expanding macro screen-size/DPI lock off the primary display.
- Key-press HUD following the navigation overlay across displays.
- EDID serial matching across different docks/GPUs.
- Numpad keys as display-switch keys.
- App-scope spanning more than the active navigation display.
- Keeping the overlay open across hot-replug or Win+P.

## Definition of done

Requirements in `assets/requirements.md` have typed evidence. Display catalog, topology store, virtual-desktop `SendInput`, per-monitor overlay host, digit switch, app-scope clip, slot-key migration, and design-note updates are in the same change. Manual check on a real multi-display machine (including mixed DPI if available) passes.
