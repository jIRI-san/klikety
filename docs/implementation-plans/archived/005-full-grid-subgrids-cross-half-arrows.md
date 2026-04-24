# 005 — Unified Grid (Remove Split-Screen) + Cross-Half Arrow Fix

## Problem

1. **L3 granularity insufficient.** Each level subdivides a cell into `firstKeys.Length × secondKeys.Length` (4×4 = 16 cells). On 4K, L3 cells are ~60×34px — not precise enough for small UI targets. Adding L4 adds keystroke latency.

2. **Arrow navigation locked to one screen half.** At L1, arrow nav defaults to left-half cells. After first key press, locked to that half. Escape resets to left half only.

3. **Split-screen is the dominant source of complexity** in state machine, coordinator, renderer, config, and tests — all for a feature that halves resolution at every level.

## Solution

**Remove the split-screen concept entirely.** Merge left+right key sets into a single unified set at all levels.

- **8 first keys** (A S D F J K L ;) → 8 columns
- **8 second keys** (W E R T Y U I O) → 8 rows
- **64 cells per level** instead of 16

| Level | Grid | Cells | 1080p cell | 4K cell |
|---|---|---|---|---|
| L1 | 8×8 | 64 | 240×135 | 480×270 |
| L2 | 8×8 | 64 | 30×17 | 60×34 |
| L3 | 8×8 | 64 | ~3.75×2 | ~7.5×4 |

4× resolution improvement per level. L2 at 4K (60×34px) suffices for most targets. L3 gives sub-10px precision.

Arrow navigation fixed as a side effect — single cell list at every level, no half-scoping.

## Design Decisions

**Flat key config.** Replace `KeySetsConfig { Left, Right }` with flat `firstKeys[]` + `secondKeys[]` on `ConfigModel`. Old `keySets.left/right` config structure replaced by top-level `firstKeys`/`secondKeys`. Config migration: concatenate left+right keys.

**Single LabelGenerator.** One instance with all 8 first keys and 8 second keys. `GridRenderer` constructor takes one `LabelGenerator` instead of two.

**No ScreenHalf.** Delete the enum. `ColumnHighlighted` event drops the `ScreenHalf` parameter: `Action<int, IReadOnlyList<GridCell>, int>` → `(col, cells, level)`.

**Uniform state machine.** `HandleFirstKey` and `HandleSecondKey` are the same at every level. `HandleL1FirstKey` (half-detection) deleted. `_activeFirstKeys`/`_activeSecondKeys` become `_firstKeys`/`_secondKeys` (set once in constructor).

**Single Activate parameter.** `Activate(IReadOnlyList<GridCell> cells, Point cursorOrigin)` — one cell list.

**Arrow nav uses full grid.** `HandleArrow` uses `_firstKeys.Length` as cols — traverses all 64 cells at every level.

## What Gets Deleted

| Component | Removed |
|---|---|
| `ScreenHalf` enum | Entire |
| `HalfKeySetsConfig` class | Entire |
| `KeySetsConfig` class | Entire |
| `ConfigModel.KeySets` | Replaced by `FirstKeys`/`SecondKeys` |
| `_leftKeys`/`_rightKeys`/`_activeHalf` | SM fields |
| `_leftL1Cells`/`_rightL1Cells`/`_activeFirstKeys`/`_activeSecondKeys` | SM fields |
| `HandleL1FirstKey` | Method — replaced by unified `HandleFirstKey` |
| Half-switching in `HandleSecondKey` | Re-entry logic at L1 |
| `_leftCells`/`_rightCells`/`_activeHalf` | Coordinator fields |
| Screen-halving in `OnHotKeyActivated` | Replaced by full-screen grid |
| `IGridRenderer.SetActiveHalf` | Method |
| `IGridRenderer.RenderBothHalves` | Method |
| `IGridRenderer.HighlightColumnSplitScreen` | Method |
| `IGridRenderer.HighlightCellSplitScreen` | Method |
| `GridRenderer._leftLabelGenerator`/`_rightLabelGenerator` | Fields — replaced by single `_labelGenerator` |
| `GridRenderer.RenderBothHalves` impl | ~60 lines |
| `GridRenderer.HighlightColumnSplitScreen` impl | ~40 lines |
| `GridRenderer.HighlightCellSplitScreen` impl | ~40 lines |
| `FakeGridRenderer` ScreenHalf tracking | `ActiveHalf`, `Half` on `RenderCall` |
| `ConfigLoader.Validate` half-overlap checks | Left/right first-key disjointness |
| `config.json` `keySets.left`/`keySets.right` | Structure |
| `config.schema.json` keySets schema | Structure |

## What Changes (Not Deleted)

| Component | Change |
|---|---|
| `ConfigModel` | Add `FirstKeys: VKey[]`, `SecondKeys: VKey[]` (flat) |
| `ConfigLoader.Validate` | Validate single `FirstKeys`/`SecondKeys`: no reserved keys, no overlap, no duplicates, non-empty |
| `NavigatorStateMachine` constructor | Takes `VKey[] firstKeys, VKey[] secondKeys` instead of two `HalfKeySetsConfig` |
| `SM.Activate` | `(IReadOnlyList<GridCell> cells, Point origin)` — single cell list |
| `SM.HandleFirstKey` | Used at all levels (no L1-special handling) |
| `SM.HandleSecondKey` | No re-entry half-switching at L1 — re-entry only re-selects column in same grid |
| `SM.HandleNavFirstKey` | Same but operates on a single set of cells |
| `SM.ResetToL1AwaitFirst` | Simpler — just reset `_arrowIndex`, `_currentLevelCells = _l1Cells`, state |
| `SM.HandleBackspace` at L1 | Just fires `ColumnUnhighlighted(1)`, state → `L1_AwaitFirst` (no half reset) |
| `ColumnHighlighted` event | `Action<int, IReadOnlyList<GridCell>, int>` — drop `ScreenHalf` param |
| Coordinator `OnHotKeyActivated` | Compute one full-screen grid: `GridCalculator.Calculate(screenBounds, firstKeys.Length, secondKeys.Length)` |
| Coordinator `OnColumnHighlighted` | Level 1 and level 2+ use same path: `HighlightColumn` or `HighlightColumnOverGrid` |
| Coordinator `OnCellHighlighted` | No split-screen branching — always `HighlightCell` or equivalent |
| Coordinator `OnLevelExited` | No per-half cell lookup — single stored cell list per level |
| Coordinator `OnColumnUnhighlighted` | No half reset |
| `GridRenderer` constructor | `(Canvas, ThemeModel, LabelGenerator, double minLabelFontSize)` |
| `GridRenderer.RenderGrid` | Already handles single cell list — becomes the L1 entry point |
| `App.xaml.cs` | Create one `LabelGenerator(config.FirstKeys, config.SecondKeys)`, pass to renderer and SM |
| `LabelGenerator` | Unchanged — already takes `VKey[] firstKeys, VKey[] secondKeys` |

## Implementation Steps

Steps 1–6 are a single atomic change — the project won't compile between steps. Implement all six before building.

### Step 1: Config model + loader + schema

1. Replace `KeySetsConfig`/`HalfKeySetsConfig` with flat properties on `ConfigModel`:
   ```csharp
   public VKey[] FirstKeys { get; init; } = [VKey.A, VKey.S, VKey.D, VKey.F, VKey.J, VKey.K, VKey.L, VKey.OemSemicolon];
   public VKey[] SecondKeys { get; init; } = [VKey.W, VKey.E, VKey.R, VKey.T, VKey.Y, VKey.U, VKey.I, VKey.O];
   ```
2. Update `ConfigLoader.Validate`: single `ValidateKeys(config.FirstKeys, config.SecondKeys, ...)`. Checks:
   - No reserved keys in either array
   - No duplicates within each array
   - `FirstKeys ∩ SecondKeys = ∅` (cross-set disjointness — a key in both arrays is ambiguous to the state machine)
   - Neither array empty
   - No overlap with action bindings
   - `maxItems` cap (16) in schema to prevent absurd grids
3. **Config migration detection.** After deserializing, check if raw JSON contains a `keySets` property (or `firstKeys`/`secondKeys` are absent). If detected, add a tray notification: "Config format changed. Delete %APPDATA%\Klikety\config.json to reset."
4. Update `config.json`:
   ```json
   "firstKeys": ["A", "S", "D", "F", "J", "K", "L", "OemSemicolon"],
   "secondKeys": ["W", "E", "R", "T", "Y", "U", "I", "O"]
   ```
5. Update `config.schema.json` — flat arrays with `maxItems: 16`, remove `keySets` object.
6. Update `ConfigLoaderTests` — new structure, cross-set disjointness, migration detection.

### Step 2: Delete ScreenHalf + split-screen IGridRenderer methods

1. Delete `ScreenHalf` enum from `NavigatorStateMachine.cs`.
2. Remove from `IGridRenderer`: `SetActiveHalf`, `RenderBothHalves`, `HighlightColumnSplitScreen`, `HighlightCellSplitScreen`.
3. Remove implementations from `GridRenderer`.
4. Remove `_leftLabelGenerator`/`_rightLabelGenerator`/`_activeLabelGenerator` from `GridRenderer`. Add single `_labelGenerator`.
5. Update `GridRenderer` constructor: `(Canvas, ThemeModel, LabelGenerator, double)`.
6. Update `FakeGridRenderer`: remove `ActiveHalf`, `Half` from `RenderCall`, remove split-screen methods.

### Step 3: State machine simplification

1. Constructor: `(VKey[] firstKeys, VKey[] secondKeys, ActionMapper, NavigationMode, int level3Threshold)`.
2. Replace `_leftKeys`/`_rightKeys` with `_firstKeys`/`_secondKeys`. Delete `_activeFirstKeys`/`_activeSecondKeys`/`_activeHalf`.
3. Replace `_leftL1Cells`/`_rightL1Cells` with single `_l1Cells`.
4. `Activate(IReadOnlyList<GridCell> cells, Point origin)` — stores `_l1Cells = cells`, `_currentLevelCells = cells`.
5. Delete `HandleL1FirstKey`. L1_AwaitFirst dispatches to `HandleFirstKey(vkey, L1_AwaitSecond, _l1Cells)` — same method used at all levels.
6. `HandleFirstKey`: uses `_firstKeys` (no half detection). `ColumnHighlighted` fires `(col, cells, level)`.
7. `HandleSecondKey`: remove L1 half-switching re-entry. Keep first-key re-entry (re-select column in same grid). Uses `_firstKeys`/`_secondKeys` directly.
8. `HandleNavFirstKey`: uses `_firstKeys`. Action-binding check remains (fires action if key matches). Only half-detection removed.
9. `ColumnHighlighted` event: `Action<int, IReadOnlyList<GridCell>, int>`.
10. `ResetToL1AwaitFirst`: `_currentLevelCells = _l1Cells; _arrowIndex = 0; State = L1_AwaitFirst; ColumnUnhighlighted(1);`
11. `HandleBackspace` at L1: calls `ResetToL1AwaitFirst()` (includes `_currentLevelCells` and `_arrowIndex` resets).
12. `HandleArrow`: `cols = _firstKeys.Length` (now 8).
13. `TryReselectCell`: uses `_firstKeys`/`_secondKeys` — works unchanged.

### Step 4: Coordinator simplification

1. Delete `_leftCells`/`_rightCells`/`_activeHalf` fields. Fields: `_l1Cells` (L1 grid for background), `_subgridCells` (active L2 or L3 subgrid, null when at L1).
2. `OnHotKeyActivated`:
   ```csharp
   var cells = GridCalculator.Calculate(screenBounds, config.FirstKeys.Length, config.SecondKeys.Length);
   _l1Cells = cells;
   _subgridCells = null;
   _stateMachine.Activate(cells, origin);
   _gridRenderer?.RenderGrid(cells);
   ```
3. `OnColumnHighlighted(int col, IReadOnlyList<GridCell> cells, int level)`:
   - L1: `_gridRenderer?.HighlightColumn(cells, col);`
   - L2+: `_gridRenderer?.HighlightColumnOverGrid(BackgroundCells, cells, col);` where `BackgroundCells` = `_subgridCells ?? _l1Cells`.
   - Does NOT overwrite `_l1Cells` or `_subgridCells`.
4. **`_subgridCells` lifecycle:**
   - **Set** in `OnCellEntered` when `subgridCells.Count > 0`: `_subgridCells = subgridCells`.
   - **Used** in `OnCellHighlighted` for L2/L3: `_gridRenderer?.HighlightCell(_subgridCells, cell)`. At L1: `_gridRenderer?.HighlightCell(_l1Cells, cell)`.
   - **Used** in `OnColumnHighlighted` at L2+: background = parent level's cells (`_subgridCells` when at L3 entering from L2, `_l1Cells` when at L2 entering from L1). Determine via: if coordinator is at L2, background = `_l1Cells`; if at L3, background = L2 subgrid. Track via `_l2SubgridCells` field (set once, stable during L3 navigation).
   - **Cleared** in `OnColumnUnhighlighted(1)` (escape/backspace back to L1): `_subgridCells = null`.
   - **Restored** in `OnLevelExited` (L3→L2): `_subgridCells = _l2SubgridCells`.
5. `OnCellEntered(cell, subgridCells, level)`:
   - Move cursor to cell center.
   - If `subgridCells.Count > 0`:
     - At level 1: `_l2SubgridCells = subgridCells; _subgridCells = subgridCells;` render over `_l1Cells`.
     - At level 2: `_subgridCells = subgridCells;` render over `_l2SubgridCells`.
   - If empty: action-only state, no rendering change.
6. `OnCellHighlighted(cell)`:
   - At L1 states: `_gridRenderer?.HighlightCell(_l1Cells, cell);`
   - At L2/L3 states: `_gridRenderer?.HighlightCell(_subgridCells!, cell);`
   - Determine level from `_stateMachine.State`.
7. `OnLevelExited(parentCell, cells, level)`:
   - Only fires from L3→L2. `_subgridCells = _l2SubgridCells;`
   - Move cursor, render `RenderSubgridOverGrid(_l1Cells, _l2SubgridCells)`.
8. `OnColumnUnhighlighted(level)`:
   - Level 1: `_subgridCells = null; _gridRenderer?.RenderGrid(_l1Cells);`
   - Level 2: `_gridRenderer?.RenderSubgrid(_subgridCells);` (re-render L2 subgrid without column highlight).
   - Level 3: `_gridRenderer?.RenderSubgrid(_subgridCells);`

### Step 5: App.xaml.cs wiring

```csharp
var labelGenerator = new LabelGenerator(config.FirstKeys, config.SecondKeys);
var gridRenderer = new GridRenderer(overlayWindow.Canvas, theme, labelGenerator, config.MinLabelFontSize);
var stateMachine = new NavigatorStateMachine(
    config.FirstKeys, config.SecondKeys, actionMapper, config.NavigationMode, config.Level3CellSizeThreshold);
```

### Step 6: Test updates

1. **NavigatorStateMachineTests**: construct with flat `VKey[]` arrays. Remove half-specific assertions. `ColumnHighlighted` captures `(col, cells, level)`. Test L1 first key → L1_AwaitSecond with correct column. Tests for re-entry at L1 AwaitSecond (first key re-selects column in same grid — no half switching).
2. **NavigatorCoordinatorTests**: construct with new config shape. Remove `SetActiveHalf`/`HighlightColumnSplitScreen`/`RenderBothHalves` assertions. Replace with `RenderGrid`/`HighlightColumn` assertions. New tests:
   - `_subgridCells` lifecycle: enter L2 → `_subgridCells` set, escape to L1 → `_subgridCells` null.
   - L2 `OnColumnHighlighted` does NOT overwrite `_l1Cells`.
   - Arrow highlight at L2 uses `_subgridCells`, not `_l1Cells`.
   - Full L1→L2→L3→escape→L2→escape→L1 round-trip.
3. **ConfigLoaderTests**: new flat key structure, cross-set disjointness, migration detection (old `keySets` in JSON → warning).
4. **New SM tests**: unified arrow navigation at L1 spanning full 64 cells (8 cols).

### Step 7: External label fan-out rendering

With 8 labels per side, labels need dynamic positioning to avoid overlap:

**Fan-out algorithm.** Compute the minimum distance from the subgrid edge where all labels fit side-by-side without overlapping. Each label is centered on its column/row edge; a dashed connector line links it to the grid.

- Measure rendered label width (column labels) or height (row labels).
- Compute inter-label spacing at the grid edge. If labels overlap at distance 0, increase distance until labels fit — fan-out at an angle from their grid attachment point.
- Label positions form a horizontal/vertical line at the fan-out distance; connectors are angled from grid edge to label position.

**Screen-edge-aware direction.** Labels fan out toward available screen space:
- Subgrid near top of screen → column labels fan out downward (below grid).
- Subgrid near bottom → column labels fan out upward (above grid).
- Subgrid near left edge → row labels fan out rightward (right of grid).
- Subgrid near right edge → row labels fan out leftward (left of grid).
- Subgrid in middle → column labels above AND below, row labels left AND right (current 4-sided behavior, at computed fan-out distance).

**Threshold.** When inter-label spacing at the grid edge ≥ label width, no fan-out needed — labels render at standard offset (current behavior). Fan-out only activates when labels would overlap.

**Implementation:** Modify `RenderExternalColumnLabels` and `RenderExternalRowLabels` in `GridRenderer` to compute fan-out distance. Helper: `ComputeFanOutDistance(labelCount, labelSize, gridExtent)` → distance in DIP where labels fit.

### Step 8: Design notes + config docs

Update `keyboard-navigator.design.md`:
- Remove split-screen section, `ScreenHalf` references, half-selection docs
- Remove arrow-only-mode left-half limitation (now fixed)
- Update state machine section: unified keys, simplified transitions, updated transition table
- Update config section: flat `firstKeys`/`secondKeys`, migration detection
- Update renderer section: no split-screen methods, external label fan-out
- Update test infrastructure section (test count)
- Update coordinator section: `_subgridCells`/`_l2SubgridCells` lifecycle

### Step 9: Manual perf check

After implementation, manually verify that key-event rendering latency is acceptable with 64 cells (4× current count). Check L1 grid render, L2 column highlight over L1 background, and arrow navigation at L2. If laggy, consider: retain Canvas children and toggle visibility instead of clear-and-rebuild. Not expected to be needed — WPF Canvas handles hundreds of lightweight shapes fine — but verify.

## Migration

**Breaking config change.** Old `keySets.left/right` structure no longer recognized. `ConfigLoader` detects the old format and surfaces a tray notification directing the user to delete `%APPDATA%\Klikety\config.json`.

## Open Questions

1. **L3 threshold.** With 64 cells per level, L2 cells on 4K are 60×34px — probably fine without L3. Consider raising default threshold or making L3 opt-in. Not blocking — default 0 still works.

## Risk

Low. This is primarily a deletion/simplification. The unified grid uses existing `HandleFirstKey`/`HandleSecondKey`/`GridCalculator.Calculate`/`SubgridCalculator.Calculate` — all already tested. Net code reduction ~200-300 lines. External label fan-out is the only new rendering complexity.
