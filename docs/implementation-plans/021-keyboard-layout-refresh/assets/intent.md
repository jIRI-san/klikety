# Intent

## Goal

Keep Klikety's displayed key labels synchronized with the active Windows keyboard layout while navigation overlays or the optional key-press HUD are active.

## Desired outcome

Changing from ENG to CZE (or any other installed layout) updates labels without closing the overlay, restarting navigation, moving the pointer, or dispatching an action.

## Success signals

- Every renderer redraws with labels resolved from the current foreground-thread HKL.
- The HUD rebuilds its cached printable-key labels before displaying the first post-switch key.
- A missed window message is recovered on the next overlay key-down.

## Non-goals

- Changing navigation from physical VKeys to text input.
- Polling keyboard layout on a timer.
- Persisting layout-specific labels or user preferences.
- Supporting layout updates while no overlay or HUD is active.

## Definition of done

All typed evidence in `requirements.md` passes, the design notes describe the lifecycle, and the manual layout-switch check succeeds.
