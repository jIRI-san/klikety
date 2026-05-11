---
description: Overlay lifecycle, NavigatorStateMachine states/transitions/events, key scheme, and arrow navigation for the uniform-grid navigator.
globs:
  - src/Klikety/NavigatorCoordinator.cs
  - src/Klikety/Navigation/NavigatorStateMachine.cs
  - src/Klikety/Navigation/ArrowNavigator.cs
  - src/Klikety/Navigation/UniformGridSession.cs
  - src/Klikety/Navigation/SessionManager.cs
  - src/Klikety/Navigation/ActionDispatcher.cs
  - src/Klikety/Navigation/DebounceHandler.cs
  - src/Klikety/Grid/LabelGenerator.cs
  - src/Klikety/Grid/GridCalculator.cs
  - src/Klikety/Grid/SubgridCalculator.cs
  - src/Klikety/Grid/GridCell.cs
---

# State Machine & Overlay Lifecycle

## Overlay Lifecycle

- `OverlayWindow` is a WPF window: `WindowStyle=None`, `AllowsTransparency=True`, `Topmost=True`, sized to primary screen bounds converted to DIPs via `PresentationSource` transform.
- `DeactivateOverlay()` is the single idempotent exit method called from every path: action fired, Escape at L1, focus loss, exception, Quit. It calls `IGridRenderer.ClearCanvas()`, hides the overlay, calls `IKeyboardHookService.DrainAndDisable()`, and clears session/debounce/drag state. Safe to call multiple times.
- `NavigatorCoordinator` delegates to four helper classes via composition: `SessionManager` (session lifecycle and scope state), `ActionDispatcher` (action dispatch and drag-drop), `MacroHandler` (recording/playback/picker), and `DebounceHandler` (hotkey debounce). Overlay visibility remains exclusively owned by the coordinator.
- `NavigatorCoordinator` implements `IDisposable`. `Dispose()` unsubscribes from all service events (`Activated`, `KeyEvent`, `FocusLost`), disposes `MacroHandler`, `SessionManager`, and `DebounceHandler`, calls `DeactivateOverlay()`, and closes the overlay window. Called by `App.xaml.cs` on coordinator replacement (config reset) and application quit.
- `OverlayWindow.Deactivated` event wires to `DeactivateOverlay()` to handle focus loss (Alt+Tab, OS notifications, background app stealing focus).
- `OverlayWindow.Show()` wrapped in try/catch for `InvalidOperationException` (no `PresentationSource` available) — prevents activation failure from leaving the overlay in an indeterminate state.
- Cursor position at `Activate()` time is saved; restored via `IMouseActionService.MoveTo(originPoint)` when Escape is pressed at L1 or focus is lost.
- Escape from L2/L3 to L1 also restores cursor to origin position (coordinator calls `MoveTo(_origin)` on `ColumnUnhighlighted(1)`).
- If `IKeyboardHookService.Enable()` returns failure on activation, `DeactivateOverlay()` is called immediately and a tray notification is shown — overlay never becomes visible.

## State Machine

States (linear progression, with back-navigation via Escape):

```
Idle → L1_AwaitFirst → L1_AwaitSecond → L1_AwaitAction
                                       → L2_AwaitFirst → L2_AwaitSecond → L2_AwaitAction
                                                                         → L3_AwaitFirst → L3_AwaitSecond → L3_AwaitAction
```

### Unified Grid

The full screen is covered by a single grid. `NavigatorStateMachine` takes flat `VKey[] firstKeys` and `VKey[] secondKeys` arrays (default 8 keys each: left+right hand combined). The same key sets are used at all levels (L1/L2/L3). No half-selection, no `ScreenHalf` enum.

- `Activate(l1Cells, cursorOrigin)` — takes a single full-screen cell list.
- `HandleFirstKey(VKey, nextState, cells, level)` — used identically at L1/L2/L3. Indexes into `_firstKeys`.
- `HandleSecondKey(VKey, ...)` — indexes into `_secondKeys`. Re-entry: pressing a first key at `AwaitSecond` restarts column selection in the same cell list.
- `ColumnHighlighted` event signature: `Action<int, IReadOnlyList<GridCell>, int>` — column index, cell list, level.
- Arrow navigation uses `_firstKeys.Length` as column count at all levels.

### Action point tracking

`_actionPoint` tracks the physical-pixel position where the next action should fire. Initialized to `_originPoint` (cursor position at overlay open) in `Activate()`. Updated on every cursor-moving operation:

| Method | Update |
|---|---|
| `Activate` | Set to `_originPoint` (cursor position at overlay open) |
| `HandleSecondKey` | Center of selected cell |
| `TryReselectCell` | Center of reselected cell |
| `HandleArrow` | Center of arrow-navigated cell |
| `HandleEnter` | Center of Enter-zoomed cell |
| `ResetToL1AwaitFirst` | Restored to `_originPoint` |
| L3→L2 escape | Center of `_l2SelectedCell` |

All action dispatch methods (`TryHandleActionKey`, `HandleNavFirstKey`, `HandleActionFinal`) use `_actionPoint` directly. No cell-index lookup at dispatch time.

**No-nav quick click**: If the user opens the overlay and immediately presses an action key without any navigation, the action fires at the cursor's original position. Same principle at L2/L3 — action fires at current cursor position (parent cell center) if no further navigation occurred.

### Transitions

| From | Input | To | Side-effect |
|---|---|---|---|
| `Idle` | `HotKeyService.Activated` | `L1_AwaitFirst` | Save cursor origin; show overlay; enable hook |
| `L1_AwaitFirst` | first-key VKey | `L1_AwaitSecond` | Raise `ColumnHighlighted(col, cells, 1)` |
| `L1_AwaitSecond` | second-key VKey | `L1_AwaitAction` | Compute L2 subgrid; move cursor; raise `CellEntered(cell, subgridCells, 1)` |
| `L1_AwaitAction` | action VKey | `Idle` | Raise `ActionRequested(point, action)`; `DeactivateOverlay()` |
| `L1_AwaitAction` | nav VKey | `L2_AwaitSecond` | Select column in pre-computed L2 subgrid; raise `ColumnHighlighted(col, subCells, 2)` |
| `L2_AwaitFirst` | first-key VKey | `L2_AwaitSecond` | Raise `ColumnHighlighted(col, cells, 2)` within subgrid |
| `L2_AwaitSecond` | second-key VKey | `L2_AwaitAction` | Compute L3 subgrid (if threshold met); move cursor; raise `CellEntered(cell, subgridCells, 2)` |
| `L2_AwaitAction` | action VKey | `Idle` | Raise `ActionRequested(point, action)`; `DeactivateOverlay()` |
| `L2_AwaitAction` | nav VKey | `L3_AwaitSecond` | Select column in pre-computed L3 subgrid (only if available); raise `ColumnHighlighted(col, subCells, 3)` |
| `L2_AwaitAction` | Escape | `L1_AwaitFirst` | Full reset; raise `ColumnUnhighlighted(1)` |
| `L2_AwaitFirst` / `L2_AwaitSecond` | Escape | `L1_AwaitFirst` | Reset to full grid; raise `ColumnUnhighlighted(1)` |
| `L3_Await*` | Escape | `L2_AwaitFirst` | Clear L3 cells; restore L2 grid; raise `ColumnUnhighlighted(2)` |
| `L1_AwaitAction` / `L1_AwaitSecond` | Escape | `L1_AwaitFirst` | Reset; raise `ColumnUnhighlighted(1)` |
| `L1_AwaitFirst` | Escape | `Idle` | Raise `Cancelled(originPoint)`; `DeactivateOverlay()` |
| `L*_AwaitAction` (deepest) | valid second key | same state | Re-select cell in same column; raise `CellEntered(cell, [], level)` |

### Events raised by state machine

- `ColumnHighlighted(int col, IReadOnlyList<GridCell> cells, int level)` — first key received; column, cell list, level
- `CellHighlighted(GridCell cell)` — arrow navigation; crosshair highlight (row + column + intersection cell)
- `CellEntered(GridCell cell, IReadOnlyList<GridCell> subgridCells, int level)` — two-key pair complete; coordinator moves cursor to cell center and renders subgrid over parent grid. Empty `subgridCells` = no next-level subgrid (L3 threshold not met).
- `ActionRequested(Point physicalPoint, MouseAction action)` — fire mouse action. **Coordinator hides overlay before sending action** so `SendInput` click reaches the underlying window, not the overlay.
- `Cancelled(Point originPoint)` — restore cursor to saved origin
- `LevelExited(GridCell parentCell, IReadOnlyList<GridCell> cells, int level)` — Escape from L3; coordinator re-renders parent level's grid
- `ColumnUnhighlighted(int level)` — Escape; coordinator re-renders full grid (L1) or subgrid
- `InvalidKeyPressed()` — unrecognized key at any await state

## Drag-and-Drop Mode

`ActionDispatcher` manages drag state via `_dragMode` bool and `_dragStartPoint` field. Flow:

1. **Start**: `DragDrop` action key pressed (and `!_dragMode`) → store `_dragStartPoint = point`, set `_dragMode = true`, call `ResetOverlayForDrag()`.
2. **ResetOverlayForDrag**: unsubscribe old session → deactivate → `ClearCanvas` → create new default-mode session → activate at `_dragStartPoint` → `ShowStatusText("Select drag target")`. Mode switching (chord keys) allowed during drag phase.
3. **Complete**: second action key pressed in drag mode → action matrix determines button (LeftClick/DoubleClick → left, RightClick → right, MiddleClick → middle). MoveOnly/DragDrop → invalid (ignored with log). Calls `ClearStatusText`, `DeactivateOverlay`, `SendDrag(start, end, button, modifiers)`, clears `_dragMode`.
4. **Cancel**: Escape or focus loss during drag → `_dragMode = false`, clear status text, restore cursor to `_origin` (overlay-open position, not `_dragStartPoint`), `DeactivateOverlay`.

Status text lives in a separate XAML layer (`StatusCanvas`) above the main `RootCanvas`. `ClearCanvas` clears only `RootCanvas` — status text survives mode switches.

### NavigationMode

- `TwoKey` — only two-key grid scheme active; arrow VKeys and `VK_RETURN` ignored
- `Arrow` — only arrow navigation; first/second key pairs ignored
- `Both` (default) — both schemes active simultaneously; any state accepts either input type

Arrow VKeys (`VK_LEFT`, `VK_RIGHT`, `VK_UP`, `VK_DOWN`) and `VK_RETURN` are **always reserved** — may not appear in `firstKeys`, `secondKeys`, or `ActionBindings`. Validated at startup.

### Level-3 trigger

After completing L2 two-key pair, if the L2 cell's physical-pixel area exceeds `Level3CellSizeThreshold`, L3 is automatically available. Nav VKey at `L2_AwaitAction` transitions to `L3_AwaitFirst`. Default threshold is `0` (L3 always active). Set to a positive value to disable L3 on small cells.

### Deepest-level cell reselection

At the deepest level's `AwaitAction` (L3 always; L2 only when L3 threshold not met), pressing a valid second key re-selects a different cell in the same column without restarting the level. `TryReselectCell` fires `CellEntered` with empty subgrid cells.

### Escape behavior

Escape resets both key presses at every level:
- `L1_AwaitAction` / `L1_AwaitSecond` → `L1_AwaitFirst` — full grid visible, raises `ColumnUnhighlighted(1)`, `_actionPoint` restored to `_originPoint`
- `L2_Await*` (all sub-states) → `L1_AwaitFirst` — full reset, raises `ColumnUnhighlighted(1)`, `_actionPoint` restored to `_originPoint`
- `L3_Await*` (all sub-states) → `L2_AwaitFirst` — clears `_l3Cells`, restores `_currentLevelCells` to `_l2Cells`, `_actionPoint` set to center of `_l2SelectedCell`, raises `ColumnUnhighlighted(2)`. Coordinator restores `_subgridCells = _l2SubgridCells` and renders `RenderSubgridOverGrid`.
- `L1_AwaitFirst` → `Idle` — raises `Cancelled(originPoint)`

`ResetToL1AwaitFirst()` helper resets `_currentLevelCells` to `_l1Cells`, `_arrowIndex` to 0, fires `ColumnUnhighlighted(1)`.

### Subgrid Computation

Subgrid computation happens on cell entry in `HandleSecondKey`. When the second key completes a cell selection, the SM immediately computes the next-level subgrid via `SubgridCalculator.Calculate(parentCell, cols, rows)`, stores it in `_l2Cells`/`_l3Cells`, and passes it via the `CellEntered` event. For L2→L3, `ShouldActivateLevel3` is checked first — if threshold not met, empty subgrid cells are passed.

`HandleNavFirstKey` selects a column within the pre-computed subgrid cells and fires `ColumnHighlighted`. If cells are empty (L3 unavailable), the key is silently ignored.

### Arrow Navigation

Arrow navigation operates on `_currentLevelCells` at whatever level is active, using `_firstKeys.Length` as the column count. Navigation wraps at grid edges via `ArrowNavigator` stateless helpers.

**Enter key** (Both/Arrow modes): At L1/L2, Enter zooms into the arrow-selected cell — computes subgrid and transitions to the next level's `AwaitFirst` state (L1→`L2_AwaitFirst`, L2→`L3_AwaitFirst`). At L3, Enter is a no-op.

**Action keys from any state** (Both mode only): `TryHandleActionKey` intercepts action-mapped VKeys (Space, X, C, V) before two-key dispatch. Fires `ActionRequested` on the current arrow-selected cell regardless of `AwaitFirst`/`AwaitSecond`/`AwaitAction` sub-state. This allows arrow-navigate → Space to click without needing Enter first.

**Crosshair highlight**: `HighlightCell` and `HighlightCellOverGrid` render the selected cell's entire row and column with highlight styling (25% opacity), with the intersection cell at full highlight (50% opacity + thicker border).

## Key Scheme

### Unified 8×8 grid

The full screen is covered by a single grid using combined left+right hand keys:

- First keys: `A S D F J K L ;` (8 keys → 8 columns)
- Second keys: `W E R T Y U I O` (8 keys → 8 rows)
- Total: 8×8 = 64 cells per level
- L2/L3 subgrids use the same key sets (64 cells per sublevel)

Config structure:
```jsonc
"firstKeys": ["A","S","D","F","J","K","L","OemSemicolon"],
"secondKeys": ["W","E","R","T","Y","U","I","O"]
```

`ConfigModel.FirstKeys` and `ConfigModel.SecondKeys` are flat `VKey[]` arrays.

### `LabelGenerator`

- Input: `VKey[]` firstKeys × `VKey[]` secondKeys
- Output: bijective map — each (row, col) pair → display string derived from `ToUnicode(vkey, HKL)`
- API: `LabelFor(int row, int col) → CellLabel`, `Cols`/`Rows` properties
- `GridRenderer` holds a single `_labelGenerator` instance used for all rendering.

### Arrow navigation (`ArrowNavigator` helper)

- Tracks `selectedIndex` in a flat cell list for the current grid level.
- `MoveLeft/Right/Up/Down(int currentIndex, int cols, int total) → int` with wrapping — stateless pure functions.
- Used by state machine when `NavigationMode` is `Arrow` or `Both`.
