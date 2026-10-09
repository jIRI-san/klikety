# Domain Model

## Terms and meanings

- **HKL:** Windows keyboard-layout handle for a thread.
- **Active layout:** The HKL associated with the foreground window's thread, supplied by `IKeyboardLayoutProvider`.
- **Label resolver:** Immutable `IKeyLabelResolver` mapping a physical `VKey` to its display text under one HKL.
- **Render command:** A session-owned idempotent delegate that redraws its current visual state without emitting navigation events.

## Actors and boundaries

- `OverlayWindow` receives WPF window messages and only reports a layout transition.
- `NavigatorCoordinator` owns layout comparison, resolver creation, and the fallback key-event check.
- `ModeSessionFactory` owns access to renderer instances.
- `KeyPressProcessor` owns HUD label-cache rebuilding.

## Interfaces and ownership

- `IKeyboardLayoutProvider` is the Win32 seam used by production and tests.
- Renderer interfaces own their label generators and expose `RebuildLabels`.
- `IModeSession.Redraw` is rendering-only; `SessionManager` clears the shared overlay canvas first.

## Invariants

- VKeys and navigation state never change because a label changes.
- Rebuild and overlay rendering occur on the UI dispatcher thread.
- A new resolver is created for every observed HKL change; resolvers are never mutated.
