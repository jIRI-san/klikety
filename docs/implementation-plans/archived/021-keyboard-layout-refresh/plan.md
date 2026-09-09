# 021: Keyboard Layout Refresh [DONE]
<!-- plan-id: 000021 -->
<!-- cip-stage: drafted -->
<!-- planning-confirmed: sha256:9ce632d01bf74c5760aeea70dcb008538755e97656dcc3af95f65f8f6feffad5 -->
<!-- Legacy folder retained so links to Plan 021 remain valid. -->

<!-- execution-mode: manual -->
<!-- scope: step -->
<!-- evidence: required -->
<!-- phase-budget-points: 7 -->
<!-- expected-packages: none -->

## Assets

`plan.md` holds only metadata, this index, and executable steps. Supporting context is loaded from `assets/` on demand.

- Intent — [assets/intent.md](assets/intent.md)
- Domain model — [assets/domain.md](assets/domain.md)
- Approved design — [assets/design.md](assets/design.md)
- Requirements — [assets/requirements.md](assets/requirements.md)
- Risks — [assets/risks.md](assets/risks.md)
- Decisions — [assets/decisions.md](assets/decisions.md)
- References — [assets/references.md](assets/references.md)
- Legacy review history — [assets/reviews/legacy-evolution.md](assets/reviews/legacy-evolution.md)

## Phase 1: Rebuildable label infrastructure

- [x] 1.1 Rebuild cached grid and axis labels from a fresh resolver (REQ-1) `S`
- [x] 1.2 Expose renderer label rebuilding through the factory without exposing generator internals (REQ-1, REQ-4) [after: 1.1] `M`
  <details><summary>Implementation contract</summary>

  **Outcome:** All active renderers rebuild their existing label generators from one new immutable `Win32KeyLabelResolver`.

  **Likely touchpoints:** `LabelGenerator`, `AxisLabelGenerator`, renderer interfaces and implementations, `ModeSessionFactory`.

  **Constraints:** Rebuild occurs only on the UI dispatcher; physical VKeys and configured key arrays remain unchanged.

  **Verify:** `test:LabelGeneratorTests.Rebuild_UsesNewResolverAndUpdatesLookup` · `test:AxisLabelGeneratorTests.Rebuild_UsesNewResolverAndUpdatesLookup`

  </details>
- [x] 1.3 Add focused rebuilding tests and update renderer fakes (REQ-1) [after: 1.2] `M`

## Phase 2: Detect active-layout changes

- [x] 2.1 Raise an overlay layout-change event from `WM_INPUTLANGCHANGE` and remove its hook while hidden (REQ-1, REQ-5, RISK-1) `M`
  <details><summary>Implementation contract</summary>

  **Outcome:** The active overlay observes a new HKL from `WM_INPUTLANGCHANGE`; the coordinator obtains the authoritative foreground-thread HKL from `IKeyboardLayoutProvider`.

  **Constraints:** The message hook is attached only after a `HwndSource` exists and detached on hide. Do not call Win32 directly from the coordinator or HUD processor.

  **Verify:** `test:NavigatorCoordinatorTests.KeyboardLayoutChange_RebuildsLabelsAndRedrawsSession` · `file:src/Klikety/Overlay/OverlayWindow.xaml.cs#contains:WM_INPUTLANGCHANGE`

  </details>
- [x] 2.2 Refresh overlay labels during activation and as a key-event fallback (REQ-1, REQ-4, REQ-5) [after: 2.1] `M`
- [x] 2.3 Make the key-press HUD rebuild its cache when `IKeyboardLayoutProvider` reports a changed HKL (REQ-2, REQ-4) `S`
- [x] 2.4 Add deterministic coordinator and HUD layout-switch tests (REQ-1, REQ-2, REQ-5) [after: 2.2, 2.3] `M`

## Phase 3: Replay current session visuals

- [x] 3.1 Add a no-event `Redraw()` session contract and manager entry point that clears then replays the active visual state (REQ-3, RISK-2) `M`
  <details><summary>Implementation contract</summary>

  **Outcome:** Sessions retain an idempotent command for their last rendering operation, including label offsets and the LogGrid first-key indicator. Redraw never moves the cursor or fires action/cancel events.

  **Constraints:** Do not expose mutable state-machine internals. Nested sessions retain their own render state; LogCrosshair preserves its parent visual while its renderer-free L2 session is active.

  **Verify:** `test:CrosshairSessionTests.Redraw_ReplaysCurrentVisualWithoutEvents` · `test:LogGridSessionTests.Redraw_FirstKeyStateReplaysIndicatorWithoutEvents`

  </details>
- [x] 3.2 Route the LogGrid first-key indicator through its renderer-owned axis labels (REQ-1, REQ-3) [after: 3.1] `S`
- [x] 3.3 Add mode-session redraw coverage for grid, crosshair, log-crosshair, and log-grid states (REQ-3, RISK-2) [after: 3.1, 3.2] `M`

## Phase 4: Document and manually validate

- [x] 4.1 Update interop, HUD, and test design notes for the layout-refresh lifecycle (REQ-1, REQ-2, REQ-3) [after: 1.3, 2.4, 3.3] `S`
- [x] 4.2 Manually switch ENG and CZE layouts in visible overlay and HUD states (REQ-1, REQ-2, REQ-3, RISK-1, RISK-2) @human [after: 4.1] `M`
  <details><summary>Details</summary>

  **Steps:**
  1. Activate each navigation mode and stop at a grid, highlighted column or row, nested session, and LogGrid first-key state.
  2. Switch between ENG and CZE with Win+Space while the overlay remains visible.
  3. Enable the key-press HUD, type a printable key, switch layouts, then type the same key.

  **Verify:** Overlay labels update without resetting navigation or moving the cursor; the HUD’s post-switch label matches the new layout; rapid repeated switches do not leave stale labels.

  **Rollback:** Revert the implementation commit; no user data is migrated or persisted.

  </details>
