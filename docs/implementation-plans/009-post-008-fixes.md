# 009: Post-008 Fixes — L3 Access, Crosshair Escape, LogCrosshair Crash

## Decisions

- **minCellPx = 5** for all modes (was 20 for UniformGrid SM, 5 for Crosshair/LogCrosshair SM). This yields ~5×5px cells at deepest level via existing `DynamicKeyReducer` math — no fixed per-level key reduction needed, the formula handles it naturally.
- `UniformGridSession` accepts a `minCellPx` parameter (was hardcoded 20 inside `NavigatorStateMachine`). Crosshair/LogCrosshair L2 sessions pass their own `_minCellPx` field through to the inner `UniformGridSession` — no hardcoded literal.
- On crosshair/log-crosshair L2 pop (Escape from inner uniform grid), SM fires a new `SubgridExited` event. Session subscribes and re-renders full cross + highlights the arrow-position cell. SM resets to `AwaitInput` internally with `_actionPoint` set to cell center. This keeps the rendering-via-SM-events pattern intact.
- LogCrosshair font sizing: replace `Math.Clamp(baseFontSize, 8.0, cellFitSize)` with `Math.Min(baseFontSize, Math.Max(1.0, cellFitSize))`. General case: cap computed font to `cellFitSize` (not 8.0 floor) to prevent overflow for cells between 1–8 DIP. Return 1.0 immediately when `cellFitSize < 1.0`.
- `EnterSubgrid` in both SMs: if `SubgridEntered` handler throws (L2 session construction failure), catch and remain in `BothSet` with `CellSelected` fired as fallback (same as `IsDisabled` path).

## Requirements

| ID | Requirement | Acceptance Criteria | Phases/Steps |
|----|-------------|---------------------|--------------|
| REQ-1 | UniformGrid L3 accessible with default 10-key config | Given 1920×1080 screen with 10 horiz × 10 vert keys, when L2 cell selected, then L3 activates (not disabled). L3 cells ≥ 5px. | 1.1 |
| REQ-2 | Crosshair L2 uniform grid reaches L3 | Given crosshair mode, when L2 uniform grid cell selected and L2 cell ≥ 10px per axis, then L3 activates. Edge cells smaller than 10px per axis may disable L3 (expected). | 1.2 |
| REQ-3 | Crosshair/LogCrosshair single Escape from L2 returns to navigable L1 | Given crosshair with L2 active, when Escape pressed (from L2 AwaitFirst), then crosshair SM fires `SubgridExited`, transitions to `AwaitInput`, `_actionPoint` at L2-entry cell center, cross rendered with arrow-position cell highlighted, arrows/axis keys work immediately. | 2.1, 2.2, 2.3 |
| REQ-4 | LogCrosshair renders without crash | Given LogCrosshair activation on any screen/DPI, when grid computed with cells having DIP extent < 10, then no exception thrown. Labels capped to `cellFitSize` (no 8.0 floor overflow). Cells < 1.0 DIP skip label entirely. | 3.1 |
| REQ-5 | Design note reflects LogCrosshair L2 capability | `navigation-modes.design.md` updated to document L2 support in LogCrosshair mode (no longer "flat/no subgrid"). | 4.1 |

## Risks

| ID | Risk | Likelihood | Impact | Mitigation | Steps |
|----|------|------------|--------|------------|-------|
| RISK-1 | minCellPx=5 makes deep-level cells invisible on high-DPI (physical 5px = 2.5 DIP at 200%) | Low | Low | Cells are for precision targeting only (user already sees general area from L1/L2). Labels not rendered below threshold — cells still function for navigation. | 1.1, 1.2 |
| RISK-2 | Existing tests assume minCellPx=20 behavior (fewer keys at L2/L3) | Medium | Low | Tests that verify reduction counts updated to match new 5px threshold. Tests that mock `IsDisabled` still valid (just triggered at smaller cells). | 1.1, 1.3 |
| RISK-3 | SM stuck in BothSet if L2 session construction throws | Low | Medium | `EnterSubgrid` wraps `SubgridEntered?.Invoke` in try/catch — falls back to `CellSelected` path on failure. | 2.2 |

## Phase 1: minCellPx Unification
<!-- worktree: -->

- [x] 1.1 Change `NavigatorStateMachine` default `minCellPx` from 20 to 5. Add `minCellPx` parameter to `UniformGridSession` constructor (default 5), pass through to `NavigatorStateMachine`. Update `ModeSessionFactory.CreateUniformGrid()` to pass `minCellPx: 5`. (REQ-1, RISK-1) `S`
- [x] 1.2 In `CrosshairSession.OnSubgridEntered` and `LogCrosshairSession.OnSubgridEntered`: pass `_minCellPx` (the session's existing field, not a literal) to the inner `UniformGridSession` constructor. Verify `DynamicKeyReducer` calls in these sessions already use `_minCellPx`. (REQ-2, RISK-1) `S`
- [x] 1.3 Update tests: `NavigatorStateMachineTests` that verify L2/L3 reduction behavior — adjust expectations for 5px threshold. Verify L3 now activates on standard screen sizes. Update any tests asserting old `minCellPx=20` reduction counts. (REQ-1, RISK-2) [after: 1.1] `M`

## Phase 2: Crosshair L2 Pop Behavior
<!-- worktree: -->

- [x] 2.1 Add `ResetToAwaitInput(int row, int col)` to `CrosshairStateMachine` and `LogCrosshairStateMachine`: resets state to `AwaitInput`, sets `_arrowRow`/`_arrowCol` to given position, clears `_horizIndex`/`_vertIndex` to -1, computes `_actionPoint` as center of `_grid.CellAt(row, col)`. Fires new `SubgridExited` event (signature: `Action?`). (REQ-3, RISK-3) `S`
- [x] 2.2 In `CrosshairSession.OnL2Cancelled` and `LogCrosshairSession.OnL2Cancelled`: after `PopL2()`, call `_sm.ResetToAwaitInput(_lastVertRow, _lastHorizCol)`. Subscribe to `SubgridExited` event in constructor — handler renders full cross via `_renderer?.RenderCross(_grid)` then highlights arrow-position cell via `_renderer?.HighlightCell(_grid, cell)`, fires `CursorMoveRequested` at cell center. Also: wrap `SubgridEntered?.Invoke` in `EnterSubgrid` with try/catch — on failure, fall back to `CellSelected` path (prevents stuck BothSet). (REQ-3, RISK-3) [after: 2.1] `M`
- [x] 2.3 Unit tests: (a) Escape from crosshair L2 → SM in AwaitInput, `_actionPoint` at L2-entry cell center, `SubgridExited` fired. (b) Session test: pop → full cross rendered + cell highlighted + cursor at correct position. (c) Axis key or arrow works immediately after pop. (d) Pop → axis keys → re-enter L2 (no immediate re-trigger). (e) Update existing tests that assert old pop behavior (HighlightCell without RenderCross, BothSet retention). (REQ-3) [after: 2.2] `M`

## Phase 3: LogCrosshair Crash Fix
<!-- worktree: -->

- [x] 3.1 In `LogCrosshairRenderer.ComputeGradualFontSize`: (a) Early return `1.0` when `cellFitSize < 1.0`. (b) For the `maxIndex == 0 || distanceIndex == 0` branch: replace `Math.Clamp(baseFontSize, 8.0, cellFitSize)` with `Math.Min(baseFontSize, cellFitSize)` (no 8.0 floor — cap to cell, floor to 1.0 from guard above). (c) General case: final floor changed from `Math.Max(capped, 8.0)` to `Math.Max(capped, 1.0)` — labels will be small but won't overflow cells. (REQ-4) `S`
- [x] 3.2 Unit test: call `ComputeGradualFontSize` (via rendering a grid with tiny cells at high DPI mock) — verify no throw, returned size ≤ cellFitSize for all cells. (REQ-4) [after: 3.1] `S`

## Phase 4: Design Note Update
<!-- worktree: -->

- [x] 4.1 Update `navigation-modes.design.md`: remove "Flat (no subgrid)" from LogCrosshair description. Document L2 support via `SubgridEntered` event and `UniformGridSession` level stack. Document `SubgridExited` event on both crosshair SMs. (REQ-5) [after: 2.2] `S`
