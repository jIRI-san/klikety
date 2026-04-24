# 004 — Render Subgrid on Cell Entry + Keep L1 Grid Visible

## Problem

1. **Subgrid not rendered after second key.** After L1 first+second key, mouse moves to cell center but the overlay still shows the L1 column-highlighted view. The subgrid only appears when the user presses the *next* first key (which enters `HandleActionOrNav`). This makes it look broken — user expects to see the subgrid immediately after selecting a cell.

2. **L1 grid disappears when subgrid renders.** Every renderer method calls `_canvas.Children.Clear()`. When L2 renders, all L1 grid lines vanish. User wants L1 grid to remain visible (without labels) as spatial context behind the subgrid.

## Root Cause Analysis

| RC | Summary | Symptom |
|---|---|---|
| RC1 | Subgrid computation deferred to `HandleActionOrNav` — only happens when user presses a first key at `AwaitAction` | No subgrid visible after cell selection |
| RC2 | `CellEntered` event carries no subgrid cells — coordinator has nothing to render | Coordinator can only `MoveTo()` |
| RC3 | All render methods call `_canvas.Children.Clear()` — L1 grid destroyed when L2/L3 renders | L1 spatial context lost |

## Design Decisions

**Subgrid computed on cell entry.** `HandleSecondKey` computes the subgrid immediately when transitioning to `AwaitAction`. This removes subgrid computation from `HandleActionOrNav`, which becomes a pure column-selection-within-existing-subgrid method. The `CellEntered` event carries the subgrid cells so the coordinator can render immediately.

**L3 threshold checked on cell entry.** `ShouldActivateLevel3` is checked when computing L2→L3 subgrid in `HandleSecondKey`. If threshold not met, `CellEntered` carries an empty subgrid list — coordinator renders without subgrid (action-only state).

**Background grid = grid lines without labels.** The L1 (or L2) parent grid renders as faint grid lines (borders only, no labels, reduced opacity) behind the subgrid. This provides spatial context without visual clutter. New renderer method `RenderSubgridOverGrid` handles this composite render.

**`HandleActionOrNav` becomes `HandleNavFirstKey`.** Renamed for clarity since it no longer computes subgrids. It selects a column within the pre-computed subgrid cells and fires `ColumnHighlighted`.

**L2 HighlightColumn renders with L1 background.** At L2/L3, `OnColumnHighlighted` calls a renderer method that shows the parent grid as background + subgrid with column highlighted. Same for `HighlightCell` at L2/L3 via arrow nav.

---

## Wired Fix Plan

### Step 1: State machine — compute subgrid on cell entry

**Fixes:** RC1, RC2

**File:** `NavigatorStateMachine.cs`

Change `CellEntered` event signature:
```csharp
// Before
public event Action<GridCell, int>? CellEntered;

// After
public event Action<GridCell, IReadOnlyList<GridCell>, int>? CellEntered;
```

In `HandleSecondKey`, after selecting a cell and transitioning to `AwaitAction`, compute the subgrid:
```csharp
selectedCell = cells[index];
State = nextState; // e.g. L1_AwaitAction

// Compute subgrid for next level
IReadOnlyList<GridCell> subgridCells = [];
if (level == 1)
{
    subgridCells = SubgridCalculator.Calculate(selectedCell, _activeFirstKeys.Length, _activeSecondKeys.Length);
    _l2Cells = subgridCells;
    _currentLevelCells = subgridCells;
    _arrowIndex = 0;
}
else if (level == 2)
{
    if (SubgridCalculator.ShouldActivateLevel3(selectedCell, _level3Threshold))
    {
        subgridCells = SubgridCalculator.Calculate(selectedCell, _activeFirstKeys.Length, _activeSecondKeys.Length);
        _l3Cells = subgridCells;
        _currentLevelCells = subgridCells;
        _arrowIndex = 0;
    }
}

CellEntered?.Invoke(selectedCell, subgridCells, level);
```

### Step 2: Simplify `HandleActionOrNav` → column selection only

**Fixes:** RC1 (completes)

**File:** `NavigatorStateMachine.cs`

Rename `HandleActionOrNav` → `HandleNavFirstKey`. Remove `SubgridCalculator.Calculate` calls. The subgrid cells are already in `_l2Cells`/`_l3Cells`:

```csharp
private void HandleNavFirstKey(VKey vkey, int level)
{
    var action = _actionMapper.Map(vkey);
    if (action.HasValue)
    {
        var center = GridCalculator.CenterOf(level == 2 ? _l1SelectedCell : _l2SelectedCell);
        State = NavigatorState.Idle;
        ActionRequested?.Invoke(center, action.Value);
        return;
    }

    var cells = level == 2 ? _l2Cells : _l3Cells;
    if (cells.Count == 0) return; // L3 not available

    int col = Array.IndexOf(_activeFirstKeys, vkey);
    if (col < 0)
    {
        InvalidKeyPressed?.Invoke();
        return;
    }

    _currentLevelCells = cells;
    _arrowIndex = 0;
    _selectedCol = col;
    State = level == 2 ? NavigatorState.L2_AwaitSecond : NavigatorState.L3_AwaitSecond;
    ColumnHighlighted?.Invoke(_activeHalf, col, cells, level);
}
```

Call sites change:
```csharp
case NavigatorState.L1_AwaitAction:
    HandleNavFirstKey(vkey, 2);
    break;
case NavigatorState.L2_AwaitAction:
    HandleNavFirstKey(vkey, 3);
    break;
```

Remove `HandleActionFinal` — merge into `HandleNavFirstKey` for L3 (L3 `AwaitAction` only allows actions, no nav). Or keep `HandleActionFinal` unchanged since L3 has no next level.

### Step 3: New renderer methods

**Fixes:** RC3

**File:** `GridRenderer.cs`, `ServiceInterfaces.cs`

Add to `IGridRenderer`:
```csharp
void RenderSubgridOverGrid(
    IReadOnlyList<GridCell> backgroundCells,
    IReadOnlyList<GridCell> subgridCells);

void HighlightColumnOverGrid(
    IReadOnlyList<GridCell> backgroundCells,
    IReadOnlyList<GridCell> subgridCells,
    int col);
```

**`RenderSubgridOverGrid`** implementation:
1. `_canvas.Children.Clear()`
2. Render `backgroundCells` as faint grid lines: borders only at reduced opacity (e.g. 0.15), no labels. Use `AddCellRect(dipRect, Brushes.Transparent, borderBrush)` with low-opacity border brush.
3. Render `subgridCells` with full `RenderSubgrid` logic on top (labels, external labels if needed).

**`HighlightColumnOverGrid`** implementation:
1. `_canvas.Children.Clear()`
2. Render `backgroundCells` as faint grid lines (same as above).
3. Render `subgridCells` with column highlight logic (same as `HighlightColumn` but without the `Clear()`).

### Step 4: Update coordinator `OnCellEntered`

**Fixes:** RC1 rendering path

**File:** `NavigatorCoordinator.cs`

Update handler signature to match new event:
```csharp
private void OnCellEntered(GridCell cell, IReadOnlyList<GridCell> subgridCells, int level)
{
    var center = GridCalculator.CenterOf(cell);
    _mouseService.MoveTo(center);

    if (subgridCells.Count > 0)
    {
        // Render subgrid over current level's background grid
        _gridRenderer?.SetActiveHalf(_activeHalf);
        _gridRenderer?.RenderSubgridOverGrid(_currentCells, subgridCells);
    }
    // else: L3 threshold not met — stay on current view, action-only state
}
```

### Step 5: Update coordinator column/cell highlight at L2/L3

**File:** `NavigatorCoordinator.cs`

`OnColumnHighlighted` at level > 1 should preserve background:
```csharp
private void OnColumnHighlighted(ScreenHalf half, int col, IReadOnlyList<GridCell> cells, int level)
{
    _activeHalf = half;
    _gridRenderer?.SetActiveHalf(half);

    if (level == 1)
    {
        _currentCells = cells;
        _gridRenderer?.HighlightColumnSplitScreen(_leftCells, _rightCells, half, col);
    }
    else
    {
        // Keep parent grid as background
        _gridRenderer?.HighlightColumnOverGrid(_currentCells, cells, col);
    }
}
```

Note: `_currentCells` is NOT updated for L2/L3 column highlight — it retains the parent level cells (L1 half or L2 cells) so the background grid is correct. It's only updated on `CellEntered` (not needed — parent cells persist).

`OnCellHighlighted` (arrow nav) at L2/L3 — add background variant or keep as-is since arrow nav at subgrid level is less common. Initial implementation: keep `HighlightCell` for L2/L3 arrows (no background grid). Can enhance later.

### Step 6: Update `OnLevelExited` rendering

**File:** `NavigatorCoordinator.cs`

Track parent cells per level so background renders correctly on escape:
```csharp
private IReadOnlyList<GridCell> _l1ActiveCells = []; // active half's L1 cells

private void OnLevelExited(GridCell parentCell, IReadOnlyList<GridCell> cells, int level)
{
    var center = GridCalculator.CenterOf(parentCell);
    _mouseService.MoveTo(center);

    if (level == 1)
    {
        // Back to L1 — render both halves
        _currentCells = _activeHalf == ScreenHalf.Left ? _leftCells : _rightCells;
        _gridRenderer?.RenderBothHalves(_leftCells, _rightCells);
    }
    else
    {
        // Back to L2 — render L2 subgrid over L1 background
        _gridRenderer?.SetActiveHalf(_activeHalf);
        _gridRenderer?.RenderSubgridOverGrid(_currentCells, cells);
    }
}
```

### Step 7: Update `IGridRenderer` + `FakeGridRenderer`

**Files:** `ServiceInterfaces.cs`, `TestFakes.cs`

Add `RenderSubgridOverGrid` and `HighlightColumnOverGrid` to `IGridRenderer`.

Add to `FakeGridRenderer`:
```csharp
public void RenderSubgridOverGrid(IReadOnlyList<GridCell> bg, IReadOnlyList<GridCell> sub)
    => Calls.Add(new("RenderSubgridOverGrid", sub));

public void HighlightColumnOverGrid(IReadOnlyList<GridCell> bg, IReadOnlyList<GridCell> sub, int col)
    => Calls.Add(new("HighlightColumnOverGrid", sub, Col: col));
```

### Step 8: Update tests

**File:** `NavigatorStateMachineTests.cs`

- Update all `CellEntered += (cell, level)` → `CellEntered += (cell, subgridCells, level)`.
- New test: `SecondKey_AtL1_ComputesSubgridCells` — press A+W → `CellEntered` carries non-empty subgrid cells within the L1 cell bounds.
- New test: `SecondKey_AtL2_ThresholdNotMet_EmptySubgrid` — small grid → L2 second key carries empty subgrid.

**File:** `NavigatorCoordinatorTests.cs`

- New test: `CellEntered_RendersSubgridOverGrid` — press A+W → renderer has `RenderSubgridOverGrid` call.
- New test: `L2ColumnHighlighted_UsesHighlightColumnOverGrid` — press A+W+A → renderer has `HighlightColumnOverGrid` call.
- New test: `EscapeFromL2_RendersL1BothHalves` — already exists, verify still passes.

### Step 9: Update design notes

**File:** `keyboard-navigator.design.md`

- `CellEntered` event now carries subgrid cells
- `HandleActionOrNav` renamed to `HandleNavFirstKey`, no longer computes subgrids
- Subgrid computed on cell entry (in `HandleSecondKey`)
- L3 threshold checked on cell entry
- New renderer methods: `RenderSubgridOverGrid`, `HighlightColumnOverGrid`
- Background grid = borders only, no labels, reduced opacity
- L1 grid remains visible behind L2/L3 subgrids

---

## Implementation Order

1. Step 1 — Compute subgrid on cell entry
2. Step 2 — Simplify HandleActionOrNav
3. Step 3 — New renderer methods
4. Step 4 — Coordinator OnCellEntered
5. Step 5 — Coordinator column highlight at L2/L3
6. Step 6 — Coordinator OnLevelExited
7. Step 7 — IGridRenderer + FakeGridRenderer
8. Step 8 — Tests
9. Step 9 — Design notes

---

## Acceptance Criteria

- [ ] After L1 first+second key, subgrid renders immediately with labels
- [ ] L1 grid remains visible (grid lines, no labels) behind L2 subgrid
- [ ] L2 column highlight shows L1 background grid + subgrid with column highlighted
- [ ] Escape from L2 returns to full L1 split-screen view
- [ ] L3 threshold still prevents L3 when cells too small
- [ ] Arrow navigation at L2/L3 works within subgrid
- [ ] All existing tests pass (with updated signatures)
- [ ] 90+ tests (existing updated + new)
