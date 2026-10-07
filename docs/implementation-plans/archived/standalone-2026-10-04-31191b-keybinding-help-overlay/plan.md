# 31191b: Keybinding help overlay
<!-- plan-id: 31191b -->
<!-- cip-stage: drafted -->
<!-- planning-confirmed: sha256:61920d54c228222278d220ba6ac10853b63ae7afd2461ee01a33be0598e49d85 -->
<!-- execution-mode: manual -->
<!-- scope: plan -->
<!-- evidence: required -->
<!-- phase-budget-points: 6 -->
<!-- expected-packages: none -->

## Assets

- Intent - [assets/intent.md](assets/intent.md)
- Domain model - [assets/domain.md](assets/domain.md)
- Design - [assets/design.md](assets/design.md)
- Requirements - [assets/requirements.md](assets/requirements.md)
- Risks - [assets/risks.md](assets/risks.md)
- Decisions - [assets/decisions.md](assets/decisions.md)
- References - [assets/references.md](assets/references.md)

## Phase 1: Working help slice

- [x] 1.1 Add help binding and state-preserving toggle with a minimal split view (REQ-1, REQ-2, REQ-3, RISK-1, RISK-2, RISK-3) `L`
  <details><summary>Implementation contract</summary>

  **Outcome:** default slash or shifted slash opens a visible split help layer; closing it returns to the same selection.

  **Likely touchpoints:** ConfigModel, ConfigLoader, ConfigMigrator, config resource/schema, KeyHookEventArgs, KeyboardHookService, NavigatorCoordinator, IOverlayWindow, OverlayWindow and fakes.

  **Constraints:** use one overlay window and existing hook; route help before display/macro/session commands. Gate opening on visible overlay plus active session; ignore activation-period help presses. Snapshot Shift/Ctrl/Alt/Win in a separate hook flags type with a compatible optional/default event argument, not ActionModifiers. On key-up preserve debounce removal, release help/Escape latches, then return without toggling. Latch toggle and close keys through release; no action or navigation dispatch while help is visible. Versioned migration adds default help only when collision-free; otherwise persist disabled help with an actionable warning. Invalid explicit bindings remain ineffective at runtime.

  **Verify:** HelpBindingTests and HelpOverlayCoordinatorTests exercise defaults, modifier matching, collisions, repeat suppression, exact resume, and teardown; build the app.

  **Stop/escalate when:** the default key cannot be enabled for existing configs without stealing a binding, or hook changes would alter existing system-key pass-through. Surface the conflict instead of silently rebinding or changing capture semantics.

  </details>

- [x] 1.2 Populate effective mouse, mode, scope, help, and display bindings (REQ-4, RISK-4) [after: 1.1] `M`
  <details><summary>Implementation contract</summary>

  **Outcome:** the split view reflects actual configured/runtime commands, including implicit Space and assigned displays.

  **Likely touchpoints:** typed help model/builder, existing effective-action resolver, SessionManager, NavigatorCoordinator, layout helpers.

  **Constraints:** no template-based legend; reuse binding resolution; no navigation command labels; custom off-block keys remain discoverable in an auxiliary strip; disabled features omitted, temporarily unavailable commands muted with reasons.

  **Verify:** HelpBindingModelTests cover Space default/override, custom bindings, disabled and unavailable modes, scope lock, and display numbering.

  </details>

## Phase 2: Contextual content and readable layout

- [x] 2.1 Add macro and drag contextual help (REQ-2, REQ-3, REQ-4, REQ-5, RISK-3, RISK-5) [after: 1.2] `M`
  <details><summary>Implementation contract</summary>

  **Outcome:** recording prompts, macro slots/names, and drag-target actions have accurate help without changing their state.

  **Likely touchpoints:** MacroHandler read-only projection, MacroRecorder substate, ActionDispatcher drag context, help builder and coordinator tests.

  **Constraints:** opening help records no step; recording time keeps running; no help UI in picker/playback windows. Global picker activation while navigation is visible uses the existing helper-picker hook-disable/suspend handoff, clearing help before picker Show and preserving session/close-resume behavior. One idempotent coordinator-owned ClearHelpState clears flag and latches before suspension, hide, deactivation, or disposal. Other global-hotkey behavior remains unchanged.

  **Verify:** HelpBindingModelTests and HelpOverlayCoordinatorTests cover recording slot/overwrite/start-from-cursor prompts, empty/named slots, drag completion meanings, and macro/global suspension cleanup.

  **Stop/escalate when:** a recording prompt cannot expose its meaning without allowing paused inputs to mutate the recorder, or a global callback resumes a stale help view.

  </details>

- [x] 2.2 Finish split layout, theme, layout glyphs, and viewport fitting (REQ-6, REQ-7, RISK-6) [after: 1.2] `L`
  <details><summary>Implementation contract</summary>

  **Outcome:** two staggered keyboard halves sit around the active display's center with a clear gap, readable commands, and dark/light styling.

  **Likely touchpoints:** help view/layout helper, OverlayWindow XAML/code, ThemeModel/resources/schema, IKeyLabelResolver and Win32 resolver.

  **Constraints:** positions are a canonical keyboard approximation; symbols follow active layout; Space below, macro keys above; reserve space for status prompts/borders. Use DIP/window-local geometry; no help on satellites. Rebuild visible help on keyboard-layout refresh; expose one viewport-change signal from SizeChanged/DpiChanged for help relayout without session mutation. Confirmed visual envelope: fit at 800x600 DIP and above with command text at least 12 DIP; below that viewport, use a contained scrollable help region rather than clipped commands.

  **Verify:** HelpKeyboardLayoutTests prove bounds and non-overlap at representative sizes; HelpKeyLabelTests prove label refresh/fallback; inspect dark/light contrast, custom keys, long macro names, and 100/150/200% DPI.

  **Stop/escalate when:** the confirmed split layout cannot fit required entries at the text floor without obscuring status prompts; request a layout decision rather than omit bindings.

  </details>

## Phase 3: Verification and documentation

- [x] 3.1 Complete regression coverage and update help documentation (REQ-1, REQ-2, REQ-3, REQ-4, REQ-5, REQ-6, REQ-7, REQ-8, RISK-1, RISK-2, RISK-3, RISK-4, RISK-5, RISK-6) [after: 2.1, 2.2] `M`
  <details><summary>Implementation contract</summary>

  **Outcome:** automated behavior/layout checks pass, existing navigation behavior remains intact, and docs describe the delivered binding and lifecycle.

  **Likely touchpoints:** Klikety.Tests coordinator/config/hook/help suites, README, root design-note index, new keybinding-help note, related config/state/rendering/interop/macros/testing notes.

  **Constraints:** no new dependencies; targeted tests first, then existing impacted regressions; use repository formatting tooling for code. Sync project-description when changing its feature summary.

  **Verify:** build plus the named help suites and existing coordinator/config/macro/layout regressions; documentation evidence in REQ-8.

  **Stop/escalate when:** required scenarios cannot be automated with the existing seams or a coupled regression remains unresolved; do not weaken acceptance criteria.

  </details>

- [x] 3.2 Verify live keyboard, display, and focus behavior (REQ-1, REQ-2, REQ-3, REQ-5, REQ-6, REQ-7) [after: 3.1] @human `S`
  <details><summary>Live verification</summary>

  **Review history (2026-10-07):** the user reviewed the running build and approved integration: "looks good merge to main, push". Manual-review corrections are implemented, including dismiss-and-forward for non-modifier keys; Escape/help close only, and modifier-only presses keep help open. This user-approved behavior supersedes the original paused-input criterion; that criterion is not claimed passed. Detailed live validation was still awaiting operator completion at that handoff.

  **Human validation (2026-10-07):** the user subsequently confirmed "i validated the implementation, you can archite the plan on main branch". This is explicit completion of the human gate for the delivered, user-approved behavior, not a claim that the historical paused-input criterion passed or an invented per-scenario test log. The historical steps below are preserved. The user separately selected "Authorize one replacement review, then archive and push (Recommended)" to replace the interrupted final reviewer; archival still requires a complete clean review and the installed finalization flow.

  **Steps:**
  1. Run the built app with a backed-up test config; test slash and Shift+slash, custom binding, held keys, and Escape at intermediate selections in every enabled mode.
  2. Check recording prompts and drag targeting; try activation/global hotkeys and Alt+Tab while help is open.
  3. Inspect dark/light themes, long/custom bindings, alternate keyboard layout, and multiple displays with negative origins at 100/150/200% DPI.

  **Verify:** help opens without cursor/selection/focus changes; paused overlay commands cause no action; close resumes exactly; all labels remain discoverable within the active display; no stale help after exit or macro suspension.

  **Rollback:** quit the test build and restore the backed-up config/theme files; report any failed scenario before completion.

  </details>
