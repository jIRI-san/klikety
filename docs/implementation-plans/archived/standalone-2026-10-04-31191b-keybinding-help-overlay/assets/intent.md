# Intent

## Goal

Make non-navigation Klikety commands discoverable through keyboard-shaped help while the navigation overlay is active.

## Desired outcome

Split keyboard halves show actual bindings near the center of the active display. The / ? key opens/closes help without requiring Shift; help is a reversible presentation state, not a navigation mode.

### Selected operator wording and confirmed interpretation

- Source/date: current conversation, 2026-10-04.
- Operator wording: "rendering left and right side of the keyboard keys slightly offset from the center of active screen"; "i do not want to havte to press shift, just the key which has the ? on it"; "we should support even the shifted ?".
- Confirmed interpretation: configurable VKey binding, default OemQuestion with Shift optional; split staggered layout on the active navigation display.
- Scope/exception: operator selected "Confirm this scope (Recommended)" before standard-plan drafting. Confirmed help during visible navigation, drag targeting, and recording prompts; paused overlay command dispatch; exact selection preservation; custom keys shown in auxiliary labels; picker/playback windows excluded; global OS hotkeys and recording time continue.
- Original proposal is draft context, not completed implementation. After both mandatory review passes and the operator-selected edits, the operator selected "Confirm plan 31191b (Recommended)" on 2026-10-04, confirming intent, requirements, risks and decisions together, including the visual envelope below.

## Success signals

- User can locate mouse actions, mode/scope switches, macros, and display switches from effective bindings.
- Slash and shifted slash produce the same default help toggle.
- Closing help returns to the same navigation state and cursor position.

## Non-goals

- Help for separate picker/playback windows or when navigation is hidden.
- A character-following question-mark shortcut, scan-code remapping, or configurable physical keyboard geometry.
- Suspending OS global hotkeys, recording timing, or changing existing macro timing semantics.
- Settings editor, mouse-interactive command launcher, new dependencies, or unrelated navigation refactoring.

### Delegated discretion and deferred choices

- Authorized choice and bounds: tune offsets, colors, compact labels, and auxiliary layout within the split keyboard design; keep all bindings discoverable and status prompts readable.
- Confirmed measurable visual envelope: fit split layout at 800x600 DIP and above, text floor 12 DIP; contained scrolling below that size.
- Operator-selected conflict handling: use versioned, conflict-aware migration; add the default binding when collision-free, otherwise persist disabled help with a warning. Invalid explicit help bindings are ineffective at runtime. Preserve unknown/user fields; stop if this requires silent rebinding or overwriting user settings.

## Definition of done

REQ-1 through REQ-8 delivered; automated evidence passes; live verification completes; related docs match delivered behavior. Planning confirmation does not claim completed implementation; this request creates the plan, not application code.
