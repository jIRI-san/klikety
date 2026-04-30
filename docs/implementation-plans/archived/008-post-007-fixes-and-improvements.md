# 008: Post-007 Fixes and Improvements [DONE]

## Decisions

- **[Done]** Single shared key config (`horizontalKeys`/`verticalKeys`) at root `ConfigModel` level used by all 3 modes — no per-mode axis keys. Config version already at 2; `ConfigMigrator` handles v0→v2 and v1→v2 paths.
- `CrossArrowNavigator` redesigned: free 2D movement (Left/Right set horiz position, Up/Down set vert position independently). Replaces current center-row/center-col restriction. Signature remains backward-compatible (same params) — behavior change only.
- Auto-enter L2 after both axis keys pressed (no Enter needed). `BothSet` becomes a transient state (immediately triggers L2). Trade-off: axis re-press correction requires Escape from L2 + re-enter. Accepted for simpler UX.
- Enter key behavior: (a) `AwaitInput` with arrow displacement → confirm at arrow position and enter L2; (b) single axis set → confirm at center of unset axis and enter L2; (c) `BothSet` with subgrid disabled (`IsDisabled`) → fire action at intersection (no L2).
- Arrow keys restricted to `AwaitInput` state only (before any axis key). Once an axis key is pressed, arrows are disabled — use Escape to return to `AwaitInput` for arrow nav. Keeps SM simpler.
- Level stack contract for L2:
  - **Renderer injection**: parent session passes its own `IGridRenderer` (which `UniformGridSession` accepts as `IGridRenderer?`). `CrosshairRenderer`/`LogCrosshairRenderer` implement `IGridRenderer` (already true via overlay canvas sharing). L2 renders subgrid directly onto same canvas.
  - **Pop conditions**: L2 `Cancelled` event (Escape at L2's top level) → pop stack, parent resumes.
  - **Visual restoration on pop**: parent re-renders L1 cross at previously-selected axis positions (both axes highlighted). Cursor restored to L1 intersection cell center.
- LogCrosshair L2 entry: uses same level-stack mechanism as Crosshair. Requires adding `SubgridEntered` event to `LogCrosshairStateMachine` + auto-trigger logic on both-axes-set and Enter handling.
- Schema files always overwritten on startup via atomic temp-file + rename. Failures logged + skipped (non-blocking). User config and theme files still skip-if-exists.
- `DynamicKeyReducer` for UniformGrid: applied at L2 and L3, using `hasCenterCell: false`. Drops outermost keys symmetrically to keep cells ≥ `minCellPx` (hardcoded 20px). Reduced key count propagated to `SubgridCalculator`, `LabelGenerator` (per-level instance), and arrow nav column count.
- LogCrosshair square cells: forced by using min(width, height) for both dimensions per cell. Overlap on shorter axis acceptable. Cells form a sparse grid on the longer axis — hit-testing uses axis key index (not spatial position), so overlap doesn't affect navigation.
- LogCrosshair font sizing: `baseFontSize * (1 + distanceIndex * scaleFactor)` where `scaleFactor = (maxFontSize - baseFontSize) / (maxIndex * baseFontSize)`. Clamped per-cell to `min(cellExtent * 0.8, prevCellEdge - currentLabelStart)` to prevent overlap. Iterative: if overlap detected, cap to available space.

## Requirements

| ID | Requirement | Acceptance Criteria | Phases/Steps |
|----|-------------|---------------------|--------------|
| REQ-1 | Schema files always overwritten on startup | Given a stale `config.schema.json` on disk, when app starts, then file content matches embedded resource. `config.json` and theme files NOT overwritten if they exist. On write failure (locked file), app continues with warning log. | 1.1 |
| REQ-2 | DynamicKeyReducer applied to UniformGrid at L2/L3 | Given an 8-key horiz axis and a 200px parent cell, when L2 activates with minCellPx=20, then `DynamicKeyReducer.ComputeActiveKeys` is called with `hasCenterCell: false` and the subgrid uses fewer keys. Reduced dimensions propagated to `SubgridCalculator` and `LabelGenerator`. L3 similarly reduced from L2 extent. | 2.1, 2.2 |
| REQ-3 | Crosshair arrow nav: free 2D movement | Given crosshair in `AwaitInput`, pressing Left/Right moves horiz position (any row), Up/Down moves vert position (any col). Selected cell = intersection. Wrapping at grid edges. Arrows disabled once an axis key is pressed. | 3.1, 3.2 |
| REQ-4 | Crosshair visual re-render on axis selection | When horiz key pressed, vertical column of labels renders at that column position. When vert key pressed, horizontal row renders at that row position. Arrow nav follows same visual shift. | 3.3, 3.4 |
| REQ-5 | Crosshair auto-enter L2 after both keys | When both horiz and vert axis keys pressed, L2 subgrid opens automatically (no Enter). Enter in `AwaitInput` with arrow displacement → enter L2 at arrow position. Enter with single axis set → enter L2 at center of unset axis. Both axes set but `IsDisabled` → fire action at intersection. | 4.1, 4.2 |
| REQ-6 | Crosshair/LogCrosshair L2 level stack | After entering L2, axis keys + arrow keys handled by L2 session (not L1 SM). Escape from L2 returns to L1 with axes re-highlighted. L2 action fires at L2-computed position. L2 session receives parent's `IGridRenderer` for rendering. | 4.3, 4.4 |
| REQ-7 | LogCrosshair arrow navigation | Arrow keys in LogCrosshair work identically to Crosshair: free 2D in `AwaitInput`, shift crosshair, Enter to confirm. Degenerate cells rejected. Arrows disabled after axis key press. | 5.1 |
| REQ-8 | LogCrosshair Enter/second-key L2 entry | Both axes set or Enter in LogCrosshair → compute uniform subgrid at intersection cell → enter L2 via `SubgridEntered` event. L2 keys handled by level stack. Requires adding `SubgridEntered` event to `LogCrosshairStateMachine`. | 5.1, 5.2 |
| REQ-9 | LogCrosshair square cells | Off-center cells rendered as squares (height = width = min dimension). Vertical cells may overlap horizontally. Labels remain at original axis position. Hit-testing by key index (unaffected by overlap). | 6.1 |
| REQ-10 | LogCrosshair gradual font sizing | Font size increases for cells further from center. Fonts must not overlap along the axis. Size capped iteratively per-cell if overlap with adjacent label detected. | 6.2 |

## Risks

| ID | Risk | Likelihood | Impact | Mitigation | Steps |
|----|------|------------|--------|------------|-------|
| RISK-1 | CrosshairStateMachine rewrite scope creep — arrow nav + axis re-render + auto-L2 are tightly coupled changes | Medium | High | Implement arrow nav first in isolation (3.1-3.2), then layer rendering (3.3-3.4), then auto-L2 (4.1-4.2). Each phase independently testable. | 3.1, 3.2, 3.3, 4.1 |
| RISK-2 | Level stack complexity — L2 session events must integrate cleanly with coordinator without breaking existing event wiring | Medium | Medium | Crosshair level stack implemented first as reference (4.3); LogCrosshair reuses same pattern (5.2). Integration tested end-to-end. Explicit contract defined in Decisions. | 4.3, 4.4, 5.2 |
| RISK-3 | LogCrosshair square cells cause visual artifacts at extreme aspect ratios or small screens | Low | Low | Accept overlap on short axis. Clamp cell to screen bounds. Hit-testing by key index avoids ambiguity. Visual-only issue, no functional impact. | 6.1 |
| RISK-4 | DynamicKeyReducer edge cases — very small parent cells could disable L2/L3 entirely | Low | Low | Already handled: `IsDisabled` check in `DynamicKeyReduction`. SM skips subgrid when disabled. Both-axes-set with `IsDisabled` → fire action instead. Covered by existing + new tests. | 2.1, 4.1 |
| RISK-5 | Schema overwrite file-lock or permission failure on startup | Low | Low | Atomic write (temp + rename). On failure: log warning, continue startup. No blocking error. | 1.1 |
| RISK-6 | `CrossArrowNavigator` behavior change affects `LogCrosshairStateMachine` compile — both SMs use same helper | Low | Medium | Signature unchanged (behavior-only change). LogCrosshair SM compiles but gets new 2D behavior immediately. Step 5.1 adjusts LogCrosshair-specific state gating. | 3.1, 5.1 |

## Phase 1: Schema Deployment Fix
<!-- worktree: -->

- [x] 1.1 `FirstRunExtractor`: split files into always-overwrite (schemas) and skip-if-exists (config, themes). Use atomic write (temp file + `File.Move` with overwrite) for schemas. Wrap in try/catch per file — log warning on failure, continue startup. Unit test: temp dir with stale schema → `EnsureDefaults()` → content updated; read-only file → no crash, returns gracefully. (REQ-1, RISK-5) `S`

## Phase 2: DynamicKeyReducer for UniformGrid
<!-- worktree: -->

- [x] 2.1 `NavigatorStateMachine`: call `DynamicKeyReducer.ComputeActiveKeys(fullKeys, parentCellExtent, minCellPx: 20, hasCenterCell: false)` when computing L2/L3 subgrids in `HandleSecondKey`. Store `DynamicKeyReduction` per level (horiz + vert). Pass reduced key count to `SubgridCalculator.Calculate(parentCell, reducedCols, reducedRows)`. Use reduced keys array for `HandleFirstKey`/`HandleSecondKey` index lookup at L2/L3. Arrow nav uses `reducedHorizKeys.Length` as column count at deeper levels. (REQ-2, RISK-4) `M`
- [x] 2.2 Unit tests: `NavigatorStateMachineTests` — verify L2 uses reduced key count (SubgridCalculator receives reduced cols/rows); verify L3 uses further-reduced count; verify `IsDisabled` skips L3 when parent cell too small; verify arrow nav at L2 uses reduced column count for wrapping. (REQ-2) [after: 2.1] `S`

## Phase 3: Crosshair Arrow Nav + Visual Re-render
<!-- worktree: -->

- [x] 3.1 Rewrite `CrossArrowNavigator.Move()`: remove center-row/center-col restriction. Left/Right change column (wrapping), Up/Down change row (wrapping), independently. Return new `(row, col)` as intersection. Keep same method signature — behavior change only. Both `CrosshairStateMachine` and `LogCrosshairStateMachine` get new behavior immediately. (REQ-3, RISK-1, RISK-6) `S`
- [x] 3.2 Update `CrossArrowNavigatorTests`: test free 2D movement — from any cell, all 4 directions work. Test wrapping at edges. Remove old center-restriction tests. (REQ-3) [after: 3.1] `S`
- [x] 3.3 `CrosshairStateMachine` + `CrosshairRenderer`: on `HorizSelected`, render vertical labels at selected column (not center). On `VertSelected`, render horizontal labels at selected row. Arrow nav (only in `AwaitInput`) fires `CursorMoveRequested` to intersection cell center + re-renders cross at new position. Arrows disabled after any axis key pressed (state gate: `AwaitInput` only). (REQ-3, REQ-4, RISK-1) [after: 3.1] `M`
- [x] 3.4 Unit tests: `CrosshairSessionTests` — verify renderer receives correct column/row positions on axis selection. Verify arrow nav triggers re-render at shifted position. Verify arrow key ignored in `HorizSet`/`VertSet` states. (REQ-3, REQ-4) [after: 3.3] `S`

## Phase 4: Crosshair Auto-L2 + Level Stack
<!-- worktree: -->

- [x] 4.1 `CrosshairStateMachine`: when both axes set (second axis key pressed), auto-compute L2 subgrid via `DynamicKeyReducer` (`hasCenterCell: false`) + `SubgridCalculator` and fire `SubgridEntered`. `BothSet` is now transient (no resting in that state). Enter behavior: (a) `AwaitInput` with arrow displacement → fire `SubgridEntered` at arrow position; (b) single axis set → fire `SubgridEntered` at center of unset axis; (c) both axes set but `IsDisabled` → fire `CellSelected`/`ActionRequested` at intersection (no L2). (REQ-5, RISK-1, RISK-4) [after: 3.3] `M`
- [x] 4.2 Unit tests: `CrosshairStateMachineTests` — verify two keys → `SubgridEntered` without Enter. Verify single key + Enter → `SubgridEntered` at center of unset axis. Verify `AwaitInput` + arrow + Enter → `SubgridEntered` at arrow position. Verify both-axes + `IsDisabled` → action at intersection (no L2). (REQ-5) [after: 4.1] `S`
- [x] 4.3 `CrosshairSession` level stack: on `SubgridEntered`, construct `UniformGridSession` with reduced keys, subgrid bounds, and parent's `IGridRenderer` (cast/pass-through — `CrosshairRenderer` renders to the same canvas). Route `OnKey` to L2 session while active. L2 `ActionRequested` → bubble to parent `ActionRequested`. L2 `Cancelled` → pop stack, re-render L1 cross at previously-selected axis positions (both axes highlighted), restore cursor to L1 intersection cell center. (REQ-6, RISK-2) [after: 4.1] `L`
- [x] 4.4 Integration tests: `CrosshairSessionTests` — full flow: activate → two axis keys → L2 activates → L2 two-key → action fires at L2 position. Escape from L2 → back to L1 with axes re-highlighted. Verify renderer receives L1 restore call on pop. (REQ-6, RISK-2) [after: 4.3] `M`

## Phase 5: LogCrosshair Nav + L2 Entry
<!-- worktree: -->

- [x] 5.1 `LogCrosshairStateMachine`: adjust state gating for free 2D arrow navigation (arrows only in `AwaitInput`, disabled after axis key — already gets new `CrossArrowNavigator` behavior from 3.1). Add visual re-render on axis selection (same pattern as Crosshair). Add `SubgridEntered` event. Enter handling: (a) `AwaitInput` with arrow → fire `SubgridEntered` at arrow position; (b) single axis → fire at center of unset axis; (c) both axes → auto-fire `SubgridEntered`. Degenerate cell rejection preserved. (REQ-7, REQ-8, RISK-6) [after: 3.1] `M`
- [x] 5.2 `LogCrosshairSession` level stack: on `SubgridEntered`, compute uniform subgrid at intersection cell → construct `UniformGridSession` with reduced keys and parent's renderer. Same pop/restore contract as Crosshair 4.3. (REQ-8, RISK-2) [after: 4.3, 5.1] `M`

## Phase 6: LogCrosshair Visual Polish
<!-- worktree: -->

- [x] 6.1 `LogGridCalculator`: force square cells — each cell uses `min(width, height)` for both dimensions. Cells form sparse grid on longer axis (gaps between cells OK). Labels remain at original axis offsets. Hit-testing uses key index (spatial overlap irrelevant). Adjust edge positions to clamp within screen bounds. (REQ-9, RISK-3) [after: 5.1] `M`
- [x] 6.2 `LogCrosshairRenderer`: font size scales with cell distance from center via `baseFontSize * (1 + distanceIndex * scaleFactor)`. `scaleFactor` derived from `(maxFontSize - baseFontSize) / (maxIndex * baseFontSize)` where `maxFontSize = min(outerCellExtent * 0.8, theme.LabelFontSize * 3)`. Per-cell cap: if computed size would overlap adjacent label (edge-to-edge along axis < computed size), clamp to available space. (REQ-10) [after: 6.1] `M`
