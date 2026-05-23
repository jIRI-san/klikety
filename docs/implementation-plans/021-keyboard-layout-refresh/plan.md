# 021: Keyboard Layout Refresh

<!-- execution-mode: manual -->
<!-- scope: step -->

## Decisions

- Labels refresh on `WM_INPUTLANGCHANGE` while overlay is active (real-time detection, not polling). Secondary fallback: coordinator checks HKL on each key event if the WM message doesn't arrive.
- `LabelGenerator` and `AxisLabelGenerator` gain a mutable `Rebuild(IKeyLabelResolver)` method — existing immutable construction stays for startup, Rebuild used for hot-swap. **Single-thread constraint**: `Rebuild()` is only called on the UI dispatcher thread (both `WM_INPUTLANGCHANGE` delivery and rendering run on the same dispatcher — no concurrent access possible).
- Renderer interfaces gain `RebuildLabels(IKeyLabelResolver)` method — each renderer forwards to its internal label generators. This provides the coordinator with a clean access path without exposing generator internals.
- `IModeSession.Redraw()` re-renders the current visual state. Sessions switch on their SM's current state and replay the appropriate renderer call (no cursor movement, no event firing). No-op when SM is in `Idle` state or session is unactivated.
- `SessionManager.RedrawActiveSession()` calls `IOverlayWindow.ClearCanvas()` before delegating to `session.Redraw()` — sessions don't hold an `IOverlayWindow` reference.
- `KeyPressProcessor` checks HKL on each `ProcessKeyDown` via an injected `Func<nint> hklProvider` (not direct `NativeMethods` calls). Rebuilds cache inline when changed. Resolver factory `Func<nint, IKeyLabelResolver>` injected for testability.
- `Win32KeyLabelResolver` remains immutable — a new instance is created with fresh HKL on each layout change. This avoids threading concerns.
- SM state accessors: add `internal` read-only properties to state machines for all fields needed by `Redraw()` — current state, selected column/row indices, arrow position, current cell lists, selected cells.
- All HKL comparisons use `NativeMethods.GetActiveKeyboardLayout()` (queries foreground window's thread), not `GetKeyboardLayout(0)` (which only returns current thread's layout).
- LogGrid first-key indicator text routed through `AxisLabelGenerator.LabelFor()` to ensure layout-correctness.

## Requirements

| ID | Requirement | Acceptance Criteria | Phases/Steps |
|----|-------------|---------------------|--------------|
| REQ-1 | Overlay labels reflect the active keyboard layout at all times while visible | When user switches from ENG to CZE layout while overlay is active, grid labels update within one frame to show Czech characters | 1.1, 1.2, 1.3, 2.1, 2.2, 2.3, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6 |
| REQ-2 | HUD (KeyPressProcessor) labels reflect the active keyboard layout | When user types after switching layout, key-press HUD shows correct characters for the new layout | 1.2, 2.4 |
| REQ-3 | Navigation state is preserved across label refresh | When layout changes mid-navigation (e.g. at L2 with column highlighted), user remains at the same navigation state after refresh — only labels change | 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7 |
| REQ-4 | No performance regression in key event hot path | ProcessKeyDown adds at most one `GetActiveKeyboardLayout()` call; overlay key handling adds no per-key overhead (WM-based detection) | 2.1, 2.3, 2.4 |
| REQ-5 | Fallback on `WM_INPUTLANGCHANGE` failure | If WM_INPUTLANGCHANGE is not delivered (topmost window edge case), secondary HKL check on each key event in coordinator catches the change | 2.1, 2.3 |

## Risks

| ID | Risk | Likelihood | Impact | Mitigation | Steps |
|----|------|------------|--------|------------|-------|
| RISK-1 | `WM_INPUTLANGCHANGE` not delivered to overlay window (topmost, non-activatable alternative styles) | Medium | Medium | Primary: `HwndSource.AddHook`. Secondary: coordinator checks HKL on each key event (same pattern as KeyPressProcessor). Between the two, at least one fires. | 2.1, 2.3, 3.1 |
| RISK-2 | `Redraw()` introduces rendering glitches (stale geometry, flash) | Low | Medium | `SessionManager.RedrawActiveSession()` calls `ClearCanvas()` before delegating. Each session replays full render — same code path as normal transitions. Validate with manual testing across all 4 modes. | 3.1, 3.2, 3.3, 3.4, 3.5, 4.1 |
| RISK-3 | SM state accessors expose internal state that could be misused | Low | Low | Accessors are `internal` visibility. Only used by the session's own `Redraw()`. | 3.1, 3.2, 3.3, 3.4 |

## Phase 1: Label Infrastructure

- [ ] 1.1 Add `Rebuild(IKeyLabelResolver)` to `LabelGenerator` — re-resolves all cached labels and rebuilds `_labelToCell` dictionary using stored VKey arrays. Store `VKey[] _firstKeys` and `VKey[] _secondKeys` as fields (set in constructor, used by Rebuild). (REQ-1) `S`
- [ ] 1.2 Add `Rebuild(IKeyLabelResolver)` to `AxisLabelGenerator` — re-resolves all cached labels and rebuilds `_labelToIndex`. Store `VKey[] _keys` as a field. (REQ-1, REQ-2) `S`
- [ ] 1.3 Add `RebuildLabels(IKeyLabelResolver)` to renderer interfaces (`IGridRenderer`, `ICrosshairRenderer`, `ILogCrosshairRenderer`, `ILogGridRenderer`). Each renderer implementation forwards to its internal `LabelGenerator.Rebuild()` and/or `AxisLabelGenerator.Rebuild()`. Update all test fakes (no-op implementation). (REQ-1) `S`
- [ ] 1.4 Unit tests: `LabelGenerator.Rebuild` with a switchable fake resolver returns new labels; `CellFor` lookups work against new labels. Same for `AxisLabelGenerator`. (REQ-1) `S`

## Phase 2: Detection & Trigger

- [ ] 2.1 Add `event Action? KeyboardLayoutChanged` to `IOverlayWindow`. In `OverlayWindow`, hook `WM_INPUTLANGCHANGE` (0x0051) via `HwndSource.AddHook` in `Show()` (after `PresentationSource` is available). Remove hook in `Hide()`. On message: compare new HKL (from lParam) vs stored `_lastHkl`; if different, update stored and raise event. Guard against null `HwndSource`. (REQ-1, REQ-5, RISK-1) `M`
- [ ] 2.2 `NavigatorCoordinator`: on `OnHotKeyActivated`, compare `NativeMethods.GetActiveKeyboardLayout()` vs stored `_lastHkl`. If different, create fresh `Win32KeyLabelResolver`, call `RebuildLabels(resolver)` on all renderers (via `ModeSessionFactory` or direct references), update `_lastHkl`. This covers layout changes while overlay was hidden. (REQ-1, RISK-1) `S`
- [ ] 2.3 `NavigatorCoordinator`: subscribe to `KeyboardLayoutChanged` event. Handler creates fresh `Win32KeyLabelResolver`, calls `RebuildLabels(resolver)` on all renderers, then calls `_sessionManager.RedrawActiveSession()`. Also: as secondary fallback, check HKL on each `OnKeyEvent` — if changed, trigger same rebuild+redraw path. (REQ-1, REQ-5, RISK-1) `M`
- [ ] 2.4 `KeyPressProcessor`: add constructor parameters `Func<nint> hklProvider` and `Func<nint, IKeyLabelResolver> resolverFactory`. Store `_lastHkl` (init from `hklProvider()`). In `ProcessKeyDown`, call `hklProvider()` and compare. If different, call `RebuildCache(resolverFactory(newHkl))` and update `_lastHkl`. Production wiring in `App.xaml.cs`: `() => NativeMethods.GetActiveKeyboardLayout()` and `hkl => new Win32KeyLabelResolver(hkl)`. (REQ-2, REQ-4) `S`
- [ ] 2.5 Unit tests: Coordinator rebuild on simulated layout change event; KeyPressProcessor auto-rebuild on HKL change (inject fake `hklProvider` that returns configurable values, and fake resolver factory). (REQ-1, REQ-2) `M`

## Phase 3: Session Redraw

- [ ] 3.1 Add `Redraw()` to `IModeSession` interface. Add no-op `Redraw()` to `DebugLogGridSession`. Add `RedrawActiveSession()` to `SessionManager` — calls `IOverlayWindow.ClearCanvas()` then `_activeSession?.Redraw()`. (REQ-1, REQ-3) `S`
- [ ] 3.2 `NavigatorStateMachine`: expose `internal` read-only properties: `CurrentState`, `SelectedCol`, `CurrentLevelCells`, `L1Cells`, `L2Cells`, `L3Cells`, `L1SelectedCell`, `L2SelectedCell`, `ArrowIndex`. (REQ-3, RISK-3) [after: 3.1] `S`
- [ ] 3.3 `UniformGridSession.Redraw()`: switch on `_stateMachine.CurrentState` → replay appropriate renderer call (`RenderGrid`, `HighlightColumn`, `HighlightCell`, `RenderSubgridOverGrid`, `HighlightColumnOverGrid`, `HighlightCellOverGrid`). Call `SetLabelOffset` before render. No-op when state is `Idle`. (REQ-1, REQ-3, RISK-2) [after: 3.2] `M`
- [ ] 3.4 `CrosshairStateMachine`/`LogCrosshairStateMachine`: expose `internal` read-only properties: `CurrentState`, `HorizIndex`, `VertIndex`, `ArrowRow`, `ArrowCol`, `SelectedCell`. (REQ-3, RISK-3) [after: 3.1] `S`
- [ ] 3.5 `CrosshairSession.Redraw()`: switch on SM state → replay `RenderCross`/`HighlightColumn`/`HighlightRow`/`HighlightCell`. If L2 active, delegate to `_l2Session.Redraw()`. No-op when `Idle`. (REQ-1, REQ-3, RISK-2) [after: 3.4] `M`
- [ ] 3.6 `LogCrosshairSession.Redraw()`: switch on SM state → `RenderCross(_grid)` + optional `HighlightColumn`/`HighlightRow`/`HighlightCell`. If L2 active, delegate. No-op when `Idle`. (REQ-1, REQ-3, RISK-2) [after: 3.4] `S`
- [ ] 3.7 `LogGridStateMachine`: expose `internal` read-only properties: `CurrentState`, `SelectedCol`, `ArrowRow`, `ArrowCol`. (REQ-3, RISK-3) [after: 3.1] `S`
- [ ] 3.8 `LogGridSession.Redraw()`: switch on SM state → `RenderGrid`/`HighlightColumn`/`HighlightCell`. In `FirstKeySet`: call `RenderFirstKeyIndicator` with label from `AxisLabelGenerator.LabelFor(col)` (not raw VKey). No-op when `Idle`. (REQ-1, REQ-3, RISK-2) [after: 3.7] `S`
- [ ] 3.9 Unit tests: each session's `Redraw()` in each SM state calls the expected renderer method with correct args. Verify no cursor-move or action events fire. Test no-op on `Idle` state. (REQ-3, RISK-2) `M`

## Phase 4: Integration & Polish

- [ ] 4.1 Manual testing: switch ENG↔CZE while overlay is in each state (L1 full grid, L1 column highlighted, L2 subgrid, crosshair with one axis, crosshair with both axes, LogGrid with first key). Verify labels update, navigation state preserved. Test HUD key labels after layout switch. (REQ-1, REQ-2, REQ-3, RISK-2) @human `M`
  <details><summary>Details</summary>

  **Steps:**
  1. Open overlay (hotkey), observe labels in ENG layout.
  2. Switch to CZE layout via Win+Space (or taskbar).
  3. Verify labels change to Czech characters.
  4. Continue navigating — state should be preserved.
  5. Repeat for each mode (UniformGrid, Crosshair, LogCrosshair, LogGrid).
  6. Repeat at each navigation depth (L1 grid, L1 column highlighted, L2 subgrid).
  7. Test HUD: type keys, switch layout, verify new characters appear.
  8. Test edge case: switch layout rapidly back and forth — no crash, no stale labels.

  **Rollback:** git revert the feature branch.

  </details>
- [ ] 4.2 Update `docs/design-notes/win32-interop.design.md` — expand "Keyboard layout independence" section with: WM_INPUTLANGCHANGE detection mechanism, Rebuild flow, single-thread constraint, secondary key-event fallback. (REQ-1) `S`
- [ ] 4.3 Run full test suite, verify zero warnings, `dotnet format`. (REQ-4) `S`
