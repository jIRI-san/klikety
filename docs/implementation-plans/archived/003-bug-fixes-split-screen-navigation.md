# 003 — Bug Fixes: Split-Screen Navigation

## Bugs Reported (Manual Testing)

1. Left-side first key renders right-side key labels
2. Second key doesn't work / L2 subgrid broken
3. Inactive half disappears instead of dimming
4. Arrow navigation broken (left side; locked into first-activated half)
5. Previous overlay frame flashes on activation

## Root Cause Analysis

| RC | Summary | Symptoms |
|---|---|---|
| RC1 | `OnColumnHighlighted` never calls `SetActiveHalf` — label generator stuck on right | Bug 1 |
| RC2 | L1 cells merged into one list — right-half index maps to left cells | Bug 2 |
| RC3 | `ColumnHighlighted` event has no level/cells — coordinator always renders L1 | Bug 2 |
| RC4 | `OnCellEntered(level>1)` prematurely renders next-level subgrid | Bug 2 |
| RC5 | All render methods clear entire canvas — other half disappears | Bug 3 |
| RC6 | `HandleActionOrNav` dead code — double state assignment + dead `nextNavState` param | Code quality |
| RC7 | Arrow nav uses 32-cell merged list with 4-col layout → 8×4 grid | Bug 4 |
| RC8 | Backspace at AwaitSecond raises no re-render event, doesn't reset arrow scope | Minor visual |
| RC9 | Canvas children survive Hide → stale frame flashes on next Show | Bug 5 |
| RC10 | Escape from L2→L1 / L3→L2 fires `CellEntered` but Step 5 removes rendering from that handler — canvas shows stale child-level grid | Escape back-nav |
| RC11 | Arrow highlight at L1 calls `HighlightCell` which clears canvas — inactive half disappears (same as RC5 but via arrow path) | Bug 3 via arrows |
| RC12 | Coordinator's `_currentCells` becomes stale on L3→L2 escape — holds L3 cells, not L2 | Escape renders wrong grid |

---

## Design Decisions

**Arrow navigation at L1 is half-scoped.** Arrow keys navigate within the active half only. Before any first key is pressed, arrows navigate the left half by default. Pressing a first key from either half switches the arrow scope to that half. Cross-half arrow navigation is not supported — users switch halves via first-key press. This is acceptable because `NavigationMode.Arrow` is a secondary mode; the primary use case is two-key. Limitation: `NavigationMode.Arrow` alone cannot reach the right half — must be combined with two-key or documented as left-half-only.

**Active non-highlighted cells use `dimBrush` at all levels.** `HighlightColumnSplitScreen` (L1), `HighlightCellSplitScreen` (L1 arrow), and `HighlightColumn` (L2/L3) all use `dimBrush` for non-highlighted cells in the active half. The inactive half at L1 uses lower opacity (0.3 labels) to create a third visual tier.

**`ColumnUnhighlighted` event name.** Named for what happened (column selection was undone), not for what the UI should do. The coordinator decides the rendering response based on level.

**Subgrid computation lives in the state machine.** `HandleActionOrNav` computes subgrid cells via `SubgridCalculator.Calculate` and stores them in `_l2Cells`/`_l3Cells`. The coordinator no longer computes subgrids — it receives cells via the enriched `ColumnHighlighted` event or the `LevelExited` event.

---

## Wired Fix Plan

### Step 1: State machine — separate L1 cell lists

**Fixes:** RC2, RC7

**File:** `NavigatorStateMachine.cs`

Add fields:
```csharp
private IReadOnlyList<GridCell> _leftL1Cells = [];
private IReadOnlyList<GridCell> _rightL1Cells = [];
```

Change `Activate` signature:
```csharp
// Before
public void Activate(IReadOnlyList<GridCell> l1Cells, Point cursorOrigin)
{
    _l1Cells = l1Cells;
    _originPoint = cursorOrigin;
    _arrowIndex = 0;
    _currentLevelCells = l1Cells;
    _activeHalf = ScreenHalf.Left;
    _activeFirstKeys = _leftKeys.FirstKeys;
    _activeSecondKeys = _leftKeys.SecondKeys;
    State = NavigatorState.L1_AwaitFirst;
}

// After
public void Activate(
    IReadOnlyList<GridCell> leftL1Cells,
    IReadOnlyList<GridCell> rightL1Cells,
    Point cursorOrigin)
{
    _leftL1Cells = leftL1Cells;
    _rightL1Cells = rightL1Cells;
    _l1Cells = leftL1Cells; // default for arrow nav before half is selected
    _originPoint = cursorOrigin;
    _arrowIndex = 0;
    _currentLevelCells = leftL1Cells;
    _activeHalf = ScreenHalf.Left;
    _activeFirstKeys = _leftKeys.FirstKeys;
    _activeSecondKeys = _leftKeys.SecondKeys;
    State = NavigatorState.L1_AwaitFirst;
}
```

Update `HandleL1FirstKey` — set `_l1Cells` and `_currentLevelCells` to the chosen half:
```csharp
private void HandleL1FirstKey(VKey vkey)
{
    int col = Array.IndexOf(_leftKeys.FirstKeys, vkey);
    if (col >= 0)
    {
        _activeHalf = ScreenHalf.Left;
        _activeFirstKeys = _leftKeys.FirstKeys;
        _activeSecondKeys = _leftKeys.SecondKeys;
        _l1Cells = _leftL1Cells;
        _currentLevelCells = _leftL1Cells;
        _selectedCol = col;
        State = NavigatorState.L1_AwaitSecond;
        ColumnHighlighted?.Invoke(_activeHalf, col, _leftL1Cells, 1);
        return;
    }

    col = Array.IndexOf(_rightKeys.FirstKeys, vkey);
    if (col >= 0)
    {
        _activeHalf = ScreenHalf.Right;
        _activeFirstKeys = _rightKeys.FirstKeys;
        _activeSecondKeys = _rightKeys.SecondKeys;
        _l1Cells = _rightL1Cells;
        _currentLevelCells = _rightL1Cells;
        _selectedCol = col;
        State = NavigatorState.L1_AwaitSecond;
        ColumnHighlighted?.Invoke(_activeHalf, col, _rightL1Cells, 1);
        return;
    }

    InvalidKeyPressed?.Invoke();
}
```

Update `HandleSecondKey` half-switching at L1 to also swap `_l1Cells`/`_currentLevelCells`:
```csharp
// In HandleSecondKey, the L1 half-switching block:
if (level == 1)
{
    col = Array.IndexOf(_leftKeys.FirstKeys, vkey);
    if (col >= 0)
    {
        _activeHalf = ScreenHalf.Left;
        _activeFirstKeys = _leftKeys.FirstKeys;
        _activeSecondKeys = _leftKeys.SecondKeys;
        _l1Cells = _leftL1Cells;
        _currentLevelCells = _leftL1Cells;
        _selectedCol = col;
        ColumnHighlighted?.Invoke(_activeHalf, col, _leftL1Cells, 1);
        return;
    }
    col = Array.IndexOf(_rightKeys.FirstKeys, vkey);
    if (col >= 0)
    {
        _activeHalf = ScreenHalf.Right;
        _activeFirstKeys = _rightKeys.FirstKeys;
        _activeSecondKeys = _rightKeys.SecondKeys;
        _l1Cells = _rightL1Cells;
        _currentLevelCells = _rightL1Cells;
        _selectedCol = col;
        ColumnHighlighted?.Invoke(_activeHalf, col, _rightL1Cells, 1);
        return;
    }
}
```

Update `Reset`:
```csharp
public void Reset()
{
    State = NavigatorState.Idle;
    _leftL1Cells = [];
    _rightL1Cells = [];
    _l1Cells = [];
    _l2Cells = [];
    _l3Cells = [];
    _currentLevelCells = [];
}
```

---

### Step 2: Remove dead code in `HandleActionOrNav`

**Fixes:** RC6

**File:** `NavigatorStateMachine.cs`

Remove dead `State = nextNavState;` assignment AND the unused `nextNavState` parameter:

```csharp
// Before — HandleActionOrNav signature
private void HandleActionOrNav(VKey vkey, NavigatorState nextNavState, GridCell parentCell, int nextLevel)

// After
private void HandleActionOrNav(VKey vkey, GridCell parentCell, int nextLevel)
```

Update call sites in `HandleTwoKey`:
```csharp
// Before
case NavigatorState.L1_AwaitAction:
    HandleActionOrNav(vkey, NavigatorState.L2_AwaitFirst, _l1SelectedCell, 2);
    break;
case NavigatorState.L2_AwaitAction:
    HandleActionOrNav(vkey, NavigatorState.L3_AwaitFirst, _l2SelectedCell, 3);
    break;

// After
case NavigatorState.L1_AwaitAction:
    HandleActionOrNav(vkey, _l1SelectedCell, 2);
    break;
case NavigatorState.L2_AwaitAction:
    HandleActionOrNav(vkey, _l2SelectedCell, 3);
    break;
```

Remove dead assignment inside the method body:
```csharp
// Before
_currentLevelCells = subCells;
_arrowIndex = 0;
_selectedCol = col;
State = nextNavState;                                          // ← dead
State = nextLevel == 2 ? NavigatorState.L2_AwaitSecond : NavigatorState.L3_AwaitSecond;

// After
_currentLevelCells = subCells;
_arrowIndex = 0;
_selectedCol = col;
State = nextLevel == 2 ? NavigatorState.L2_AwaitSecond : NavigatorState.L3_AwaitSecond;
```

Note: `HandleActionOrNav` skips `AwaitFirst` and goes directly to `AwaitSecond` — the first key typed at the action level doubles as the first key of the next level. This is intentional.

---

### Step 3: Enrich `ColumnHighlighted` event with cells and level

**Fixes:** RC3

**File:** `NavigatorStateMachine.cs`

Change event signature:
```csharp
// Before
public event Action<ScreenHalf, int>? ColumnHighlighted;

// After
public event Action<ScreenHalf, int, IReadOnlyList<GridCell>, int>? ColumnHighlighted;
//                  half       col   cells                    level
```

Update all call sites (Step 1 already shows the L1 calls with new signature). Remaining:

| Location | Invocation |
|---|---|
| `HandleFirstKey` (L2/L3) | `Invoke(_activeHalf, col, cells, level)` — derive level from state (below) |
| `HandleSecondKey` re-entry (same half) | `Invoke(_activeHalf, col, cells, level)` — use existing `level` param |
| `HandleActionOrNav` | `Invoke(_activeHalf, col, subCells, nextLevel)` |

`HandleFirstKey` — derive level from next state:
```csharp
private void HandleFirstKey(VKey vkey, NavigatorState nextState, IReadOnlyList<GridCell> cells)
{
    int col = Array.IndexOf(_activeFirstKeys, vkey);
    if (col < 0) { InvalidKeyPressed?.Invoke(); return; }

    _selectedCol = col;
    State = nextState;
    int level = nextState == NavigatorState.L2_AwaitSecond ? 2 : 3;
    ColumnHighlighted?.Invoke(_activeHalf, col, cells, level);
}
```

---

### Step 4: Escape rendering — replace `CellEntered` with `LevelExited` event

**Fixes:** RC10, RC12

**Files:** `NavigatorStateMachine.cs`, `NavigatorCoordinator.cs`

Currently `HandleEscape` at L3→L2 fires `CellEntered(_l2SelectedCell, 2)` and at L2→L1 fires `CellEntered(_l1SelectedCell, 1)`. After Step 6 removes rendering from `OnCellEntered`, these calls only move the cursor — the child-level canvas stays visible. Additionally, the coordinator's `_currentCells` can be stale (pointing to L3 cells after L3→L2 escape).

Add a new event that carries the parent cell **and** the cell list to render:
```csharp
public event Action<GridCell, IReadOnlyList<GridCell>, int>? LevelExited;
//                  parentCell  cellsToRender            level (level being returned TO)
```

Update `HandleEscape`:
```csharp
private void HandleEscape()
{
    switch (State)
    {
        case NavigatorState.L3_AwaitFirst:
        case NavigatorState.L3_AwaitSecond:
        case NavigatorState.L3_AwaitAction:
            State = NavigatorState.L2_AwaitAction;
            _currentLevelCells = _l2Cells;
            _arrowIndex = 0;
            LevelExited?.Invoke(_l2SelectedCell, _l2Cells, 2);
            break;

        case NavigatorState.L2_AwaitFirst:
        case NavigatorState.L2_AwaitSecond:
        case NavigatorState.L2_AwaitAction:
            State = NavigatorState.L1_AwaitAction;
            _currentLevelCells = _l1Cells;
            _arrowIndex = 0;
            LevelExited?.Invoke(_l1SelectedCell, _l1Cells, 1);
            break;

        default:
            State = NavigatorState.Idle;
            Cancelled?.Invoke(_originPoint);
            break;
    }
}
```

Wire in coordinator:
```csharp
// In constructor
_stateMachine.LevelExited += OnLevelExited;

private void OnLevelExited(GridCell parentCell, IReadOnlyList<GridCell> cells, int level)
{
    var center = GridCalculator.CenterOf(parentCell);
    _mouseService.MoveTo(center);
    _currentCells = cells;

    if (level == 1)
    {
        // Returned to L1 — re-render both halves
        _gridRenderer?.RenderBothHalves(_leftCells, _rightCells);
    }
    else
    {
        // Returned to L2 — re-render the L2 subgrid with correct cells
        _gridRenderer?.SetActiveHalf(_activeHalf);
        _gridRenderer?.RenderSubgrid(cells);
    }
}
```

---

### Step 5: Coordinator — fix `OnColumnHighlighted`, call `SetActiveHalf`

**Fixes:** RC1, RC3

**File:** `NavigatorCoordinator.cs`

```csharp
// Before
private void OnColumnHighlighted(ScreenHalf half, int col)
{
    _activeHalf = half;
    _currentCells = half == ScreenHalf.Left ? _leftCells : _rightCells;
    _gridRenderer?.HighlightColumn(_currentCells, col);
}

// After
private void OnColumnHighlighted(ScreenHalf half, int col, IReadOnlyList<GridCell> cells, int level)
{
    _activeHalf = half;
    _currentCells = cells;
    _gridRenderer?.SetActiveHalf(half);

    if (level == 1)
        _gridRenderer?.HighlightColumnSplitScreen(_leftCells, _rightCells, half, col);
    else
        _gridRenderer?.HighlightColumn(cells, col);
}
```

---

### Step 6: Coordinator — fix `OnCellEntered` and `OnCellHighlighted`

**Fixes:** RC4, RC11

**File:** `NavigatorCoordinator.cs`

`OnCellEntered` — remove premature subgrid render:
```csharp
// Before
private void OnCellEntered(GridCell cell, int level)
{
    var center = GridCalculator.CenterOf(cell);
    _mouseService.MoveTo(center);

    if (level > 1)
    {
        var activeKeys = ...;
        var subCells = SubgridCalculator.Calculate(...);
        _currentCells = subCells;
        _gridRenderer?.RenderSubgrid(subCells);
    }
}

// After
private void OnCellEntered(GridCell cell, int level)
{
    var center = GridCalculator.CenterOf(cell);
    _mouseService.MoveTo(center);
    // Subgrid rendering driven by ColumnHighlighted at L2/L3 via HandleActionOrNav.
}
```

`OnCellHighlighted` — level-aware split-screen rendering:
```csharp
// Before
private void OnCellHighlighted(GridCell cell)
{
    _gridRenderer?.HighlightCell(_currentCells, cell);
}

// After
private void OnCellHighlighted(GridCell cell)
{
    if (_stateMachine.State is NavigatorState.L1_AwaitFirst or NavigatorState.L1_AwaitSecond
                            or NavigatorState.L1_AwaitAction)
        _gridRenderer?.HighlightCellSplitScreen(_leftCells, _rightCells, _activeHalf, cell);
    else
        _gridRenderer?.HighlightCell(_currentCells, cell);
}
```

Also update `OnHotKeyActivated` — no longer merges cells:
```csharp
// Before
var allCells = new List<GridCell>(...);
allCells.AddRange(_leftCells);
allCells.AddRange(_rightCells);
_currentCells = allCells;
_stateMachine.Activate(allCells, origin);

// After
_currentCells = _leftCells; // default before half is selected
_stateMachine.Activate(_leftCells, _rightCells, origin);
```

---

### Step 7: Renderer — add `HighlightColumnSplitScreen` and `HighlightCellSplitScreen`

**Fixes:** RC5, RC11

**File:** `GridRenderer.cs`

**`HighlightColumnSplitScreen`** — renders both halves, active half with column highlight, inactive half dimmed:

```csharp
public void HighlightColumnSplitScreen(
    IReadOnlyList<GridCell> leftCells,
    IReadOnlyList<GridCell> rightCells,
    Navigation.ScreenHalf activeHalf,
    int col)
{
    _canvas.Children.Clear();
    EnsureTransform();

    var borderBrush = BrushFromHex(_theme.CellBorderColor);
    var dimBrush = BrushFromHex(_theme.DimmedOverlayColor, _theme.DimmedOverlayOpacity);
    var highlightBg = BrushFromHex(_theme.HighlightedColumnBackground, 0.5);
    var highlightBorder = BrushFromHex(_theme.HighlightedColumnBorderColor);
    var labelBrush = BrushFromHex(_theme.LabelColor);

    // Render inactive half (dimmed)
    var inactiveCells = activeHalf == Navigation.ScreenHalf.Left ? rightCells : leftCells;
    var inactiveGen = activeHalf == Navigation.ScreenHalf.Left ? _rightLabelGenerator : _leftLabelGenerator;
    if (inactiveCells.Count > 0)
    {
        _activeLabelGenerator = inactiveGen;
        var region = ComputeRegionFromCells(inactiveCells);
        foreach (var cell in inactiveCells)
        {
            var dipRect = DipRectForCell(cell.Row, cell.Col, region, inactiveGen.Cols, inactiveGen.Rows);
            AddCellRect(dipRect, dimBrush, borderBrush);
            AddLabel(dipRect, cell.Row, cell.Col, labelBrush, 0.3);
        }
    }

    // Render active half (column highlight + dimmed non-highlighted)
    var activeCells = activeHalf == Navigation.ScreenHalf.Left ? leftCells : rightCells;
    var activeGen = activeHalf == Navigation.ScreenHalf.Left ? _leftLabelGenerator : _rightLabelGenerator;
    if (activeCells.Count > 0)
    {
        _activeLabelGenerator = activeGen;
        var region = ComputeRegionFromCells(activeCells);
        foreach (var cell in activeCells)
        {
            var dipRect = DipRectForCell(cell.Row, cell.Col, region, activeGen.Cols, activeGen.Rows);
            bool isHighlighted = cell.Col == col;
            AddCellRect(dipRect,
                isHighlighted ? highlightBg : dimBrush,
                isHighlighted ? highlightBorder : borderBrush);
            AddLabel(dipRect, cell.Row, cell.Col, labelBrush, isHighlighted ? 1.0 : 0.3);
        }
    }
}
```

**`HighlightCellSplitScreen`** — renders both halves, active half with single cell highlight:

```csharp
public void HighlightCellSplitScreen(
    IReadOnlyList<GridCell> leftCells,
    IReadOnlyList<GridCell> rightCells,
    Navigation.ScreenHalf activeHalf,
    GridCell highlightedCell)
{
    _canvas.Children.Clear();
    EnsureTransform();

    var borderBrush = BrushFromHex(_theme.CellBorderColor);
    var dimBrush = BrushFromHex(_theme.DimmedOverlayColor, _theme.DimmedOverlayOpacity);
    var highlightBg = BrushFromHex(_theme.HighlightedColumnBackground, 0.5);
    var highlightBorder = BrushFromHex(_theme.HighlightedColumnBorderColor);
    var labelBrush = BrushFromHex(_theme.LabelColor);

    // Inactive half (dimmed)
    var inactiveCells = activeHalf == Navigation.ScreenHalf.Left ? rightCells : leftCells;
    var inactiveGen = activeHalf == Navigation.ScreenHalf.Left ? _rightLabelGenerator : _leftLabelGenerator;
    if (inactiveCells.Count > 0)
    {
        _activeLabelGenerator = inactiveGen;
        var region = ComputeRegionFromCells(inactiveCells);
        foreach (var cell in inactiveCells)
        {
            var dipRect = DipRectForCell(cell.Row, cell.Col, region, inactiveGen.Cols, inactiveGen.Rows);
            AddCellRect(dipRect, dimBrush, borderBrush);
            AddLabel(dipRect, cell.Row, cell.Col, labelBrush, 0.3);
        }
    }

    // Active half (cell highlight)
    var activeCells = activeHalf == Navigation.ScreenHalf.Left ? leftCells : rightCells;
    var activeGen = activeHalf == Navigation.ScreenHalf.Left ? _leftLabelGenerator : _rightLabelGenerator;
    if (activeCells.Count > 0)
    {
        _activeLabelGenerator = activeGen;
        var region = ComputeRegionFromCells(activeCells);
        foreach (var cell in activeCells)
        {
            var dipRect = DipRectForCell(cell.Row, cell.Col, region, activeGen.Cols, activeGen.Rows);
            bool isHighlighted = cell.Row == highlightedCell.Row && cell.Col == highlightedCell.Col;
            AddCellRect(dipRect,
                isHighlighted ? highlightBg : dimBrush,
                isHighlighted ? highlightBorder : borderBrush,
                isHighlighted ? _theme.CellBorderThickness * 2 : _theme.CellBorderThickness);
            AddLabel(dipRect, cell.Row, cell.Col, labelBrush, isHighlighted ? 1.0 : 0.3);
        }
    }
}
```

**Extract `AddCellRect` helper** to reduce duplication across all render methods:

```csharp
private void AddCellRect(Rect dipRect, Brush fill, Brush stroke,
    double strokeThickness = -1)
{
    if (strokeThickness < 0) strokeThickness = _theme.CellBorderThickness;
    var bg = new Rectangle
    {
        Width = dipRect.Width, Height = dipRect.Height,
        Fill = fill, Stroke = stroke, StrokeThickness = strokeThickness,
    };
    Canvas.SetLeft(bg, dipRect.X);
    Canvas.SetTop(bg, dipRect.Y);
    _canvas.Children.Add(bg);
}
```

Use `AddCellRect` in existing `RenderCellsInRegion`, `HighlightColumn`, `HighlightCell`, `RenderSubgrid` to reduce repetition.

---

### Step 8: Backspace re-render + reset arrow scope

**Fixes:** RC8

**File:** `NavigatorStateMachine.cs`

Add event:
```csharp
public event Action<int>? ColumnUnhighlighted;
```

Update `HandleBackspace` — at L1, also reset active half to left (matching `Activate` defaults):
```csharp
private void HandleBackspace()
{
    switch (State)
    {
        case NavigatorState.L1_AwaitSecond:
            // Reset to left-half defaults (matching Activate initial state)
            _activeHalf = ScreenHalf.Left;
            _activeFirstKeys = _leftKeys.FirstKeys;
            _activeSecondKeys = _leftKeys.SecondKeys;
            _l1Cells = _leftL1Cells;
            _currentLevelCells = _leftL1Cells;
            _arrowIndex = 0;
            State = NavigatorState.L1_AwaitFirst;
            ColumnUnhighlighted?.Invoke(1);
            break;
        case NavigatorState.L2_AwaitSecond:
            State = NavigatorState.L2_AwaitFirst;
            ColumnUnhighlighted?.Invoke(2);
            break;
        case NavigatorState.L3_AwaitSecond:
            State = NavigatorState.L3_AwaitFirst;
            ColumnUnhighlighted?.Invoke(3);
            break;
    }
}
```

**File:** `NavigatorCoordinator.cs`

Wire event:
```csharp
// In constructor
_stateMachine.ColumnUnhighlighted += OnColumnUnhighlighted;

private void OnColumnUnhighlighted(int level)
{
    _gridRenderer?.SetActiveHalf(_activeHalf);
    if (level == 1)
        _gridRenderer?.RenderBothHalves(_leftCells, _rightCells);
    else
        _gridRenderer?.RenderSubgrid(_currentCells);
}
```

---

### Step 9: Clear stale canvas in `DeactivateOverlay`

**Fixes:** RC9

Clearing in `DeactivateOverlay()` ensures the canvas is already clean when the window is next shown — avoids both the stale-frame flash and the potential empty-frame flash from clearing in `Show()`.

**File:** `NavigatorCoordinator.cs`

```csharp
// Before
public void DeactivateOverlay()
{
    if (_deactivating) return;
    _deactivating = true;
    try
    {
        _hookService.Disable();
        _overlayWindow.Hide();
        _stateMachine.Reset();
    }
    finally
    {
        _deactivating = false;
    }
}

// After
public void DeactivateOverlay()
{
    if (_deactivating) return;
    _deactivating = true;
    try
    {
        _hookService.Disable();
        _gridRenderer?.ClearCanvas();
        _overlayWindow.Hide();
        _stateMachine.Reset();
    }
    finally
    {
        _deactivating = false;
    }
}
```

**File:** `GridRenderer.cs`

Add public method:
```csharp
public void ClearCanvas() => _canvas.Children.Clear();
```

---

### Step 10: Extract `IGridRenderer` for testability

**Fixes:** Review finding — coordinator tests pass `null` renderer, can't verify rendering calls.

**Files:** `Services/ServiceInterfaces.cs` (or new file), `GridRenderer.cs`, `NavigatorCoordinator.cs`, `Klikety.Tests/Fakes/TestFakes.cs`

Extract interface from `GridRenderer`:
```csharp
public interface IGridRenderer
{
    void SetTransform(System.Windows.Media.Matrix transformFromDevice);
    void SetActiveHalf(Navigation.ScreenHalf half);
    void RenderBothHalves(IReadOnlyList<GridCell> leftCells, IReadOnlyList<GridCell> rightCells);
    void RenderGrid(IReadOnlyList<GridCell> cells);
    void HighlightColumn(IReadOnlyList<GridCell> cells, int col);
    void HighlightCell(IReadOnlyList<GridCell> cells, GridCell highlightedCell);
    void HighlightColumnSplitScreen(IReadOnlyList<GridCell> leftCells, IReadOnlyList<GridCell> rightCells,
        Navigation.ScreenHalf activeHalf, int col);
    void HighlightCellSplitScreen(IReadOnlyList<GridCell> leftCells, IReadOnlyList<GridCell> rightCells,
        Navigation.ScreenHalf activeHalf, GridCell highlightedCell);
    void RenderSubgrid(IReadOnlyList<GridCell> cells);
    void FlashInvalidKey();
    void ClearCanvas();
}
```

Change coordinator field type:
```csharp
// Before
private readonly GridRenderer? _gridRenderer;

// After
private readonly IGridRenderer? _gridRenderer;
```

Add `FakeGridRenderer` in test fakes:
```csharp
public sealed class FakeGridRenderer : IGridRenderer
{
    public record RenderCall(string Method, IReadOnlyList<GridCell>? Cells = null,
        Navigation.ScreenHalf? Half = null, int? Col = null);

    public List<RenderCall> Calls { get; } = [];
    public Navigation.ScreenHalf? ActiveHalf { get; private set; }

    public void SetTransform(System.Windows.Media.Matrix m) { }
    public void SetActiveHalf(Navigation.ScreenHalf half)
    {
        ActiveHalf = half;
        Calls.Add(new("SetActiveHalf", Half: half));
    }
    public void RenderBothHalves(IReadOnlyList<GridCell> l, IReadOnlyList<GridCell> r)
        => Calls.Add(new("RenderBothHalves"));
    public void RenderGrid(IReadOnlyList<GridCell> cells)
        => Calls.Add(new("RenderGrid", cells));
    public void HighlightColumn(IReadOnlyList<GridCell> cells, int col)
        => Calls.Add(new("HighlightColumn", cells, Col: col));
    public void HighlightCell(IReadOnlyList<GridCell> cells, GridCell cell)
        => Calls.Add(new("HighlightCell", cells));
    public void HighlightColumnSplitScreen(IReadOnlyList<GridCell> l, IReadOnlyList<GridCell> r,
        Navigation.ScreenHalf half, int col)
        => Calls.Add(new("HighlightColumnSplitScreen", Half: half, Col: col));
    public void HighlightCellSplitScreen(IReadOnlyList<GridCell> l, IReadOnlyList<GridCell> r,
        Navigation.ScreenHalf half, GridCell cell)
        => Calls.Add(new("HighlightCellSplitScreen", Half: half));
    public void RenderSubgrid(IReadOnlyList<GridCell> cells)
        => Calls.Add(new("RenderSubgrid", cells));
    public void FlashInvalidKey()
        => Calls.Add(new("FlashInvalidKey"));
    public void ClearCanvas()
        => Calls.Add(new("ClearCanvas"));
}
```

---

### Step 11: Update tests

### `NavigatorStateMachineTests.cs`

All `sm.Activate(CreateGrid(), ...)` calls → `sm.Activate(leftGrid, rightGrid, ...)`.

Need two grids:
```csharp
private static IReadOnlyList<GridCell> CreateLeftGrid()
    => GridCalculator.Calculate(new Rectangle(0, 0, 150, 200), 3, 2);

private static IReadOnlyList<GridCell> CreateRightGrid()
    => GridCalculator.Calculate(new Rectangle(150, 0, 150, 200), 3, 2);
```

Update `ColumnHighlighted` event subscriptions to match new signature `(half, col, cells, level)`.

New tests:
- **Right-half first key selects right cells**: press `VKey.J` → `ColumnHighlighted` fires with `ScreenHalf.Right`, right cells, level 1.
- **Right-half second key selects from right cell list**: press `J` then `U` → `CellEntered` with a cell from the right grid (`bounds.X >= 150`).
- **Half switching at L1_AwaitSecond**: press `A` (left), then `J` (right) → switches to right half, `ColumnHighlighted` fires with right cells.
- **Backspace fires ColumnUnhighlighted and resets to left**: press `J` (right half), then `Backspace` → `ColumnUnhighlighted(1)` fired, state = `L1_AwaitFirst`, `_activeHalf` = Left.
- **LevelExited fires on Escape from L2 with correct cells**: navigate to L2, press Escape → `LevelExited(_l1SelectedCell, _l1Cells, 1)` fired, state = `L1_AwaitAction`.
- **LevelExited from L3 carries L2 cells**: navigate to L3, press Escape → `LevelExited(_l2SelectedCell, _l2Cells, 2)` fired.

### `NavigatorCoordinatorTests.cs`

Update `CreateCoordinator` to inject `FakeGridRenderer` instead of `null`:
```csharp
private static (..., FakeGridRenderer Renderer) CreateCoordinator(...)
{
    ...
    var renderer = new FakeGridRenderer();
    var coordinator = new NavigatorCoordinator(
        hotKey, hook, mouse, overlay, sm, renderer, config, NullLogger.Instance);
    return (..., renderer);
}
```

New tests:
- **Left first key calls SetActiveHalf(Left) + HighlightColumnSplitScreen**: press `A` → verify renderer calls.
- **Right first key dispatches to right-half point**: press `J`+`Y`+`Space` → mouse action point `X >= screenWidth/2`.
- **CellEntered no longer renders subgrid**: press `A`+`W` → renderer has no `RenderSubgrid` call.
- **Escape from L2 calls RenderBothHalves**: press `A`+`W`+`A`+`W` (L2 cell), Escape → renderer has `RenderBothHalves` call.
- **Escape from L3 calls RenderSubgrid with L2 cells**: navigate to L3, Escape → renderer has `RenderSubgrid` with L2 cell list.
- **Backspace calls SetActiveHalf + RenderBothHalves**: press `A`, Backspace → renderer has both calls.
- **DeactivateOverlay calls ClearCanvas**: activate + deactivate → renderer has `ClearCanvas` call.

---

### Step 12: Update design notes

Update `keyboard-navigator.design.md`:
- `Activate` signature change (separate left/right cell lists)
- `ColumnHighlighted` event signature (now carries cells + level)
- New `LevelExited` event (carries parent cell + cell list + level)
- New `ColumnUnhighlighted` event
- `HandleActionOrNav` parameter cleanup
- `IGridRenderer` interface
- `HighlightColumnSplitScreen` / `HighlightCellSplitScreen` renderer methods
- `ClearCanvas` called in `DeactivateOverlay`
- Arrow nav is half-scoped at L1 (design decision)
- Arrow-only mode limited to left half (documented limitation)
- Subgrid computation responsibility lives in state machine

---

## Implementation Order

1. Step 1 — State machine: separate L1 cell lists
2. Step 2 — Remove dead code in HandleActionOrNav
3. Step 3 — Enrich ColumnHighlighted event
4. Step 4 — Escape rendering via LevelExited event
5. Step 5 — Coordinator OnColumnHighlighted fix
6. Step 6 — Coordinator OnCellEntered / OnCellHighlighted / OnHotKeyActivated fix
7. Step 7 — Renderer HighlightColumnSplitScreen + HighlightCellSplitScreen + AddCellRect
8. Step 8 — Backspace re-render + arrow scope reset
9. Step 9 — Clear canvas in DeactivateOverlay
10. Step 10 — Extract IGridRenderer + FakeGridRenderer
11. Step 11 — Update tests
12. Step 12 — Update design notes
