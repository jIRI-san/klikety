# Legacy Evolution Review

The original Plan 021 review identified and resolved these design gaps before migration to the current artifact format:

1. Renderers need a coordinator access path to their label generators.
2. Active layout must use the foreground thread rather than `GetKeyboardLayout(0)`.
3. Full redraw must preserve the visual navigation state.
4. HUD layout lookup must remain testable.
5. Canvas clearing belongs to `SessionManager`, not individual sessions.
6. Every `IModeSession` implementation needs redraw behavior.
7. `WM_INPUTLANGCHANGE` needs a key-event fallback.
8. Mutable rebuilding needs a UI-thread constraint.
9. The LogGrid first-key indicator must use resolved labels.

The upgraded design retains those conclusions, replacing state-machine accessors with session-owned render-command replay and reusing the repository's existing `IKeyboardLayoutProvider` seam.
