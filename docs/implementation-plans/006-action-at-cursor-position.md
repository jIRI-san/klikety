# 006 — Fire Action at Cursor Position (No-Nav Quick Click)

## Problem

When the overlay opens and the user immediately presses an action key (Space, etc.) without any navigation, the action fires at `_currentLevelCells[0]` center (top-left grid cell) instead of the mouse cursor's original position. Same issue at L2/L3: if the user presses action at `L2_AwaitFirst` without navigating within the subgrid, action fires at L2 cell 0 instead of the cursor's current position (center of the parent L1 cell).

Additionally, escaping from L2 back to L1 does not restore the cursor to its original position — the cursor stays wherever L2 left it.

## Desired Behavior

1. **No-nav action at L1**: Open overlay → press action key → click/double-click fires at the cursor's original position (where the mouse was before overlay opened). Cursor does not move.
2. **No-nav action at L2**: Enter L1 cell (two-key pair moves cursor to L1 cell center) → press action key at `L2_AwaitFirst` → fires at cursor's current position (L1 cell center = L2 subgrid center).
3. **No-nav action at L3**: Enter L2 cell → press action key at `L3_AwaitFirst` → fires at cursor's current position (L2 cell center).
4. **Navigated action**: If arrow keys or two-key input updated the position, fire at the navigated position.
5. **Escape cursor restoration**:
   - L3 → L2: cursor restores to L2 cell center (already works via `LevelExited`).
   - L2 → L1: cursor restores to original position (currently missing).
   - L1 → close: cursor restores to original position (already works via `Cancelled`).

## Solution

Track `_actionPoint` in the state machine — the position where an action should fire. Updated on every cursor-moving operation. All action dispatch uses `_actionPoint` instead of computing position from cell index.

## State Machine Changes (`NavigatorStateMachine.cs`)

### Add `_actionPoint` field

```csharp
private Point _actionPoint;
```

### Initialize in `Activate()`

```csharp
_actionPoint = cursorOrigin;
```

### Update `_actionPoint` on cursor-moving operations

| Method | Update |
|---|---|
| `HandleSecondKey` (two-key cell entry) | `_actionPoint = GridCalculator.CenterOf(selectedCell)` — after computing `selectedCell` |
| `TryReselectCell` (deepest-level reselect) | `_actionPoint = GridCalculator.CenterOf(selectedCell)` — after setting `selectedCell` |
| `HandleArrow` | `_actionPoint = GridCalculator.CenterOf(_currentLevelCells[_arrowIndex])` — after updating `_arrowIndex` |
| `HandleEnter` (arrow zoom) | `_actionPoint = GridCalculator.CenterOf(cell)` — after selecting cell |
| `ResetToL1AwaitFirst` (escape to L1) | `_actionPoint = _originPoint` — restore to origin |
| L3→L2 escape handler | `_actionPoint = GridCalculator.CenterOf(_l2SelectedCell)` — restore to L2 parent |

### Use `_actionPoint` in action dispatch

Replace position computation in these methods:

**`TryHandleActionKey`** (Both mode, any state):
```csharp
// Before:
if (_arrowIndex < 0 || _arrowIndex >= _currentLevelCells.Count) return false;
var center = GridCalculator.CenterOf(_currentLevelCells[_arrowIndex]);
State = NavigatorState.Idle;
ActionRequested?.Invoke(center, action.Value);

// After:
State = NavigatorState.Idle;
ActionRequested?.Invoke(_actionPoint, action.Value);
```

**`HandleNavFirstKey`** (AwaitAction, action key branch):
```csharp
// Before:
var center = GridCalculator.CenterOf(parentCell);
State = NavigatorState.Idle;
ActionRequested?.Invoke(center, action.Value);

// After:
State = NavigatorState.Idle;
ActionRequested?.Invoke(_actionPoint, action.Value);
```

**`HandleActionFinal`** (L3 AwaitAction, action key):
```csharp
// Before:
var cells = _l3Cells.Count > 0 ? _l3Cells : _l2Cells;
if (_arrowIndex >= 0 && _arrowIndex < cells.Count) {
    var center = GridCalculator.CenterOf(cells[_arrowIndex]);
    State = NavigatorState.Idle;
    ActionRequested?.Invoke(center, action.Value);
}

// After: remove cells variable, guard, and center computation
State = NavigatorState.Idle;
ActionRequested?.Invoke(_actionPoint, action.Value);
```

### Guard removal rationale

The `_arrowIndex` bounds checks in `TryHandleActionKey` and `HandleActionFinal` are no longer needed since we don't index into cells. The `_actionPoint` is always valid because it's initialized to `_originPoint` in `Activate()` and updated on every cursor-moving operation — there is no code path that leaves it unset while the SM is active.

## Coordinator Changes (`NavigatorCoordinator.cs`)

### Store origin point

Add `_origin` field. Replace the existing `var origin = NativeMethods.GetCursorPosition()` local in `OnHotKeyActivated` with `_origin = NativeMethods.GetCursorPosition()`. Pass `_origin` to `_stateMachine.Activate(_l1Cells, _origin)`. Single call — no risk of cursor moving between two `GetCursorPosition()` invocations.

```csharp
private Point _origin;
// in OnHotKeyActivated (replaces existing var origin):
_origin = NativeMethods.GetCursorPosition();
_stateMachine.Activate(_l1Cells, _origin);
```

### Restore cursor on L2→L1 escape

In `OnColumnUnhighlighted`, when `level == 1`, move cursor to origin:
```csharp
private void OnColumnUnhighlighted(int level) {
    if (level == 1) {
        _mouseService.MoveTo(_origin);  // ← NEW
        _subgridCells = null;
        _l2SubgridCells = null;
        _gridRenderer?.RenderGrid(_l1Cells);
    } else { ... }
}
```

This covers all L1 escape paths: from `L1_AwaitSecond`, `L1_AwaitAction`, and all `L2_Await*` states — all call `ResetToL1AwaitFirst()` which raises `ColumnUnhighlighted(1)`.

## ActionRequested Point Change

Currently `OnActionRequested` receives the point from the SM and calls `SendAction(point, action)`. The SM now sends `_actionPoint` which is already the correct position. No coordinator change needed for action dispatch.

However, the `OnActionRequested` handler does NOT call `MoveTo` — it only calls `SendAction`. For the no-nav case at L1, cursor is already at the right position (never moved). For the navigated case, cursor was already moved by `OnCellEntered` (two-key) or stays at origin with crosshair (arrow). `SendAction` with `MOUSEEVENTF_ABSOLUTE` will move+click in one operation. This is correct.

## Test Impact

### Existing tests that verify action position

Tests in `NavigatorStateMachineTests.cs` and `NavigatorCoordinatorTests.cs` that assert `ActionRequested` point will need updates where:
- Action fired from `L1_AwaitFirst` without prior navigation — point changes from cell[0] center to origin.
- Verify `_actionPoint` tracks correctly through sequences.

### New test cases

| Test | Scenario |
|---|---|
| Action at L1 without navigation | Activate → Space → ActionRequested at origin |
| Action at L2 without L2 navigation | Activate → two-key L1 → Space at L2_AwaitFirst → ActionRequested at L1 cell center |
| Action at L3 without L3 navigation | Activate → two-key L1 → two-key L2 → Space at L3_AwaitFirst → ActionRequested at L2 cell center |
| Action after arrow at L1 | Activate → Arrow Right → Space → ActionRequested at cell[1] center |
| Action after arrow at L2 | Activate → two-key L1 → Arrow Down → Space → ActionRequested at L2 subgrid cell center |
| Action after reselect at L2 | Activate → two-key L1 → two-key L2 (no L3) → reselect different cell → Space → ActionRequested at reselected cell center |
| Action after reselect at L3 | Activate → two-key L1 → two-key L2 → two-key L3 → reselect → Space → ActionRequested at reselected cell center |
| TwoKey action at L1_AwaitAction | Activate → two-key L1 → Space (via HandleNavFirstKey) → ActionRequested at L1 cell center |
| TwoKey action at L3_AwaitAction | Activate → full nav to L3 → Space (via HandleActionFinal) → ActionRequested at L3 cell center |
| Escape L2→L1 cursor restore | Activate → two-key L1 → Escape → verify MoveTo(origin) |
| Escape L1_AwaitAction→L1_AwaitFirst cursor restore | Activate → two-key L1 (at L1_AwaitAction) → Escape → verify MoveTo(origin) |

### Coordinator integration tests

| Test | Scenario |
|---|---|
| No-nav quick click coordinator | Activate → Space → SendAction called with origin point |
| L2 escape restores cursor | Activate → two-key L1 → Escape → MoveTo(origin) called |

## Risk

Low. Changes are confined to:
- `_actionPoint` tracking in SM (additive field + updates)
- Action dispatch methods (replace computed position with tracked position)
- `OnColumnUnhighlighted` level 1 cursor restore (one `MoveTo` call)
- No rendering changes, no config changes, no new events.

## Non-Goals

- Changing the visual overlay behavior (grid rendering, highlights).
- Changing the `SendAction` implementation in `MouseActionService`.
- Adding cursor movement during arrow navigation (cursor still only visually highlights, doesn't physically move until action fires or overlay closes).
