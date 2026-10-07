# Domain Model

## Terms and meanings

- Target application: HWND captured before Klikety displays its overlay, together with process/root identity. It is not whatever is foreground after overlay activation.
- Candidate: an enabled, onscreen UIA element with meaningful interaction semantics and nonempty geometry inside the target/display intersection.
- Snapshot: immutable candidates, completion/partial status, diagnostics, and a session-bound snapshot ID. It is a point-in-time view, not a live tree.
- Target token: helper-local identity for an element in one snapshot. It does not survive helper termination or application restart.
- Hint: a displayed two-VKey label for one target on one page. Glyphs follow the current keyboard layout; identity follows VKeys.
- Selected point: a provisional interior point used for cursor preview. It is not permission to inject input.
- Validated point: a fresh, bounded result confirming element identity/state/geometry and physical hit-test ownership after the overlay is hidden.
- Grid fallback: an explicit mode-local Enter command switching to UniformGrid at the current point and preserving the current display/app-scope geometry.
- Partial result: retained candidates with an explicit incomplete flag/reason; a successful nonempty tree is not proof of complete application coverage.

## Actors and boundaries

- User and keyboard hook initiate navigation/actions; the hook performs no UIA or process waiting.
- Coordinator owns overlay visibility, focus, current activation generation, action preparation, and notification routing.
- SessionManager owns active mode, scope, target context, session subscriptions, and transitions.
- ElementHintsSession owns selection/page/prefix state, immutable discovery results, and request cancellation.
- UIA supervisor owns one helper process/transport and its watchdog. The helper owns COM/UIA objects on a windowless MTA thread.
- ActionDispatcher and IMouseActionService retain the physical action/drag/modifier implementation.
- Accessibility providers belong to target apps and may fail, hang, or supply incomplete data. Their outputs require validation.

## Interfaces and ownership

- Preserve `IModeSession.Activate(Rectangle, Point)` for existing modes. Supply an explicit immutable element-target context when creating the new session; do not use global HWND state inside the factory.
- Introduce a typed async discovery/validation seam returning DTOs and typed failure outcomes. Inject fake transport/clock/process ownership and UI-dispatch seams where needed.
- Retain UIA references only inside the helper. Share a small versioned DTO/protocol source between app and worker without a dependency from the worker onto the WPF application assembly.
- Use a narrow optional action-preparation/fallback capability for the new session rather than making every grid mode implement asynchronous discovery.
- Cancellation belongs to the activation/session generation, not renderer visibility. A separate bounded pending-action transaction survives the temporary validation hide, but not cancellation/replacement/disposal.
- Renderer redraw uses stored state and emits no cursor, action, scan, or cancel events.

## Invariants

- A label maps to one retained target on its displayed page. Redraw and layout-glyph refresh do not remap it.
- UIA and transport geometry is physical desktop pixels; conversion to window-local DIPs occurs at the rendering boundary.
- Discovery and validation do not invoke controls, enter text, or inject input.
- Action injection requires a current selection, current validation, matching activation identity, and final existing bounds checks.
- Cancellation, helper failure, and stale completion never dispatch an action or macro step.
- Hidden/disabled controls and passive text are not interchangeable with clickable targets. Readonly edit controls can still be focus targets; textbox values are not needed.
- Runtime IDs are scoped identities, not global persistent IDs. Retaining a HWND alone is insufficient to prove window identity.
- The helper inherits the caller's normal permission level; UIA detection does not bypass SendInput privilege restrictions.
