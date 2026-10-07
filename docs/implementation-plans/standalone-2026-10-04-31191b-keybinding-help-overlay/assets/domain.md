# Domain Model

## Terms and meanings

- Help binding: overlay-local key/modifier match; not a registered global shortcut.
- Help entry: immutable key/chord, display label, command label, category, availability/reason, and keyboard/auxiliary placement.
- Navigation anchors: muted key shapes with no command labels.
- Help visible: presentation flag that intercepts command dispatch without replacing the active mode session.
- Active display: navigation overlay monitor, not the app-scope target rectangle or satellite displays.

## Actors and boundaries

- User invokes help through captured keyboard input.
- Coordinator owns dispatch ordering and overlay lifecycle.
- Existing macro/drag/session owners retain their state; help reads projections.
- WPF view renders in the existing overlay window and takes no new focus.
- OS/global hotkeys remain an independent boundary.

## Interfaces and ownership

- Config owns enablement and help key/modifier options.
- Hook events carry separate Shift/Ctrl/Alt/Win flags with a compatible default event argument; mouse ActionModifiers remain unchanged. Hook callback remains bounded and asynchronously dispatched.
- Typed builder owns effective entries; renderer owns visual layout only.
- IOverlayWindow exposes help show/hide/update capabilities for fake-based tests.
- MacroHandler exposes read-only names/slots/substate as needed, not mutable storage.

## Invariants

- While visible navigation help is open, overlay commands do not reach display, macro, mode/scope, or session dispatch. Exception: existing OS global-hotkey paths.
- Help open/close preserves session identity, cursor/action point, selection/level, origin, mode lock, scope, drag, and recorder state.
- Opening help requires a visible overlay and active session; activation-period presses are ignored. Key-up removes debounce state and releases latches without toggling.
- One coordinator-owned idempotent cleanup clears help flags/latches before hiding, teardown, disposal or macro suspension. Global picker activation uses the existing suspension/hook handoff when navigation is visible.
- Keyboard-layout refresh rebuilds visible help; a size/DPI viewport signal relays out help without mutating the session.
- Existing system-key pass-through and global registration semantics are retained.
