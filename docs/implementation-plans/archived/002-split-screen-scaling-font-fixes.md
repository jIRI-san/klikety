# 002: Split-Screen Grid, Scaling Fix, Second-Key Fix, Font Auto-Scaling

**Status: COMPLETE** — All phases implemented and verified (75 tests passing).

## Decisions

- Screen split in half: left half handled by left-hand keys (ASDF + WERT), right half by right-hand keys (JKL; + YUIO)
- Each half is an independent 4×4 grid (16 cells per half per level)
- Grid calculation done in DIP space to avoid physical-pixel rounding at non-100% scaling
- Physical pixels retained only for cursor movement (`MoveTo`, `SendAction`)
- Font size auto-scaled so each label character fills 80% of cell height (L1) or 90% (L2/L3 subgrids)
- `MinLabelFontSize` remains the floor for auto-scaling
- DIP grid uses region-based approach: transform only corners, subdivide evenly (no per-cell rounding)
- Kept existing `GridCalculator`/`SubgridCalculator` for physical-pixel cell computation; DIP conversion in `GridRenderer` only

## Requirements

| ID | Requirement | Steps |
|----|-------------|-------|
| REQ-1 | Second key press moves cursor and provides visual feedback | 1.1 |
| REQ-2 | Grid cells align perfectly at 125% and other non-100% scaling | 2.1–2.3 |
| REQ-3 | Font size auto-scales to fill cell height | 3.1 |
| REQ-4 | Screen split into left/right halves with independent key sets | 4.1–4.7 |

## Phases

Execution order: Phase 1 (second-key fix) → Phase 2 (DIP grid) → Phase 3 (font) → Phase 4 (split-screen) → Phase 5 (docs).

### Phase 1: Second-Key Fix (REQ-1)

- [x] 1.1 In `NavigatorCoordinator.OnCellEntered`: call `_mouseService.MoveTo(center)` for all levels (was missing for level 1). [touched: `NavigatorCoordinator.cs`]

### Phase 2: DIP-Space Grid Calculation (REQ-2)

- [x] 2.1 `GridRenderer.EnsureTransform()` auto-reads device→DIP matrix from `PresentationSource` on first render. [touched: `Overlay/GridRenderer.cs`]
- [x] 2.2 `ComputeRegionFromCells(cells)` transforms only two corners to DIP. `DipRectForCell(row, col, region, cols, rows)` subdivides evenly — no per-cell rounding. [touched: `Overlay/GridRenderer.cs`]
- [x] 2.3 Removed old `ToDip` method. All render methods use region-based DIP rects. Physical-pixel grid calc kept for `GridCalculator`/`SubgridCalculator`. [touched: `Overlay/GridRenderer.cs`]

### Phase 3: Font Auto-Scaling (REQ-3)

- [x] 3.1 `ComputeAutoFontSize(cellHalfWidth, cellHeight, heightFraction)`: primary driver is cell height (80% L1, 90% L2/L3), secondary cap at 95% of half-width. Clamped to `[minLabelFontSize, theme.LabelFontSize * 3]`. `AddLabel` passes `heightFraction` parameter. [touched: `Overlay/GridRenderer.cs`]

### Phase 4: Split-Screen Left/Right Halves (REQ-4)

- [x] 4.1 `HalfKeySetsConfig` class with `FirstKeys`/`SecondKeys`. `KeySetsConfig` has `Left`/`Right` properties. Defaults: left ASDF/WERT, right JKL;/YUIO. [touched: `Config/ConfigModel.cs`]
- [x] 4.2 Updated `config.json` template and `config.schema.json` for `keySets.left`/`keySets.right` structure. [touched: `Resources/config.json`, `Resources/config.schema.json`]
- [x] 4.3 Two `LabelGenerator` instances (left, right) created in `App.xaml.cs` and passed to `GridRenderer`. [touched: `App.xaml.cs`]
- [x] 4.4 `GridRenderer`: dual label generators (`_leftLabelGenerator`, `_rightLabelGenerator`), `RenderBothHalves()`, `SetActiveHalf()`. [touched: `Overlay/GridRenderer.cs`]
- [x] 4.5 `NavigatorStateMachine`: `ScreenHalf` enum, `HandleL1FirstKey` detects half from key, `ColumnHighlighted(ScreenHalf, int)`, half-switching at L1 `AwaitSecond`. Constructor takes `HalfKeySetsConfig` left/right. [touched: `Navigation/NavigatorStateMachine.cs`]
- [x] 4.6 `NavigatorCoordinator`: computes left/right cell lists from screen halves, tracks `_activeHalf`, removed `LabelGenerator` dependency. [touched: `NavigatorCoordinator.cs`]
- [x] 4.7 Tests updated: `ConfigLoaderTests` (new JSON format), `NavigatorStateMachineTests` (HalfKeySetsConfig constructor, ScreenHalf event), `NavigatorCoordinatorTests` (removed LabelGenerator param). All 75 tests passing. [touched: test files]

### Phase 5: Cleanup & Docs

- [x] 5.1 Updated `keyboard-navigator.design.md` (split-screen, DIP grid, font auto-scaling sections) and `README.md` (features, config table, layout examples). [touched: docs]
- [x] 5.2 No dead code remaining from old single-grid model.
