---
description: Navigation mode system — IModeSession interface, mode switching, Crosshair and LogCrosshair modes with their state machines, renderers, and grid calculators.
globs:
  - src/Klikety/Navigation/IModeSession.cs
  - src/Klikety/Navigation/ModeSessionFactory.cs
  - src/Klikety/Navigation/CrosshairSession.cs
  - src/Klikety/Navigation/CrosshairStateMachine.cs
  - src/Klikety/Navigation/LogCrosshairSession.cs
  - src/Klikety/Navigation/LogCrosshairStateMachine.cs
  - src/Klikety/Navigation/LogGridSession.cs
  - src/Klikety/Navigation/LogGridStateMachine.cs
  - src/Klikety/Navigation/CrossArrowNavigator.cs
  - src/Klikety/Navigation/UniformGridSession.cs
  - src/Klikety/Grid/CrosshairGridCalculator.cs
  - src/Klikety/Grid/LogGridCalculator.cs
  - src/Klikety/Grid/LogScaleGridCalculator.cs
  - src/Klikety/Grid/LogGrid.cs
  - src/Klikety/Grid/DynamicKeyReducer.cs
  - src/Klikety/Grid/AxisLabelGenerator.cs
  - src/Klikety/Config/LogGridKeyPolicy.cs
  - src/Klikety/Overlay/CrosshairRenderer.cs
  - src/Klikety/Overlay/LogCrosshairRenderer.cs
  - src/Klikety/Overlay/LogGridRenderer.cs
  - src/Klikety/Config/NavigationMode.cs
---

# Navigation Modes

Four navigation modes, each implementing `IModeSession` with independent configuration via `ModeConfig`:

- **UniformGrid** — two-key grid scheme using `firstKeys`/`secondKeys`. L1→L2→L3 level stack.
- **Crosshair** — cross-style axis key navigation with uniform grid cells. Supports L2 subgrid.
- **LogCrosshair** — logarithmic-scaled cross grid centered on cursor. Supports L2 subgrid via `SubgridEntered` event and `UniformGridSession` level stack.
- **LogGrid** — iterative two-key selection with log-scaled grid and recentering. Explicit-action mode (Space/X/C/V always required). Arrow navigation moves selection without recentering; recenter on Enter (arrow-selected) or two-key selection. Grid cells shrink geometrically toward screen edges; sub-5px cells at the boundary are collapsed to zero-width, effectively removing keys from the outside inward as the cursor approaches the screen edge.

## `IModeSession` Interface

```csharp
interface IModeSession {
    event Action<Point, MouseAction>? ActionRequested;
    event Action? Cancelled;
    event Action<Point>? CursorMoveRequested;
    void Activate(Rectangle screenBounds, Point origin);
    void OnKey(VKey key);
    void Redraw();
    void Deactivate();
}
```

All modes implement this interface. `NavigatorCoordinator` owns the active session, subscribing to events on activation and calling `Deactivate()` on teardown. Sessions are stateless between `Deactivate()`→`Activate()` cycles (SM reset internally). `Redraw()` replays only the current visual state; it never emits cursor, action, or cancel events. `SessionManager.RedrawActiveSession()` clears the shared canvas before invoking it. Sessions store their last rendering command, including label offsets and indicator state, rather than exposing state-machine internals.

## `ModeSessionFactory`

Creates `IModeSession` instances by mode name. Constructor: `(ConfigModel, ActionMapper, IGridRenderer?, ICrosshairRenderer?, ILogCrosshairRenderer?, ILogGridRenderer?)`. Renderers are nullable — null when the mode's renderer cannot be constructed (e.g., disabled mode with null keys). `RebuildLabels(IKeyLabelResolver)` forwards a fresh resolver to every available renderer.

## Mode Switching (Chord Dispatch)

- Each mode except the default has a `ChordKey` (`VKey?`).
- Chord keys are processed before the active session when `_modeLocked` is false.
- After any nav/arrow/action key is pressed, `_modeLocked = true` — subsequent chord keys go to the session (which flashes invalid).
- `SwitchMode()`: unsubscribe old → old.Deactivate() → ClearCanvas() → create new via factory → subscribe → new.Activate(). Wrapped in try/catch/finally with `_switching` guard.
- Supported catch types: `NotSupportedException`, `ArgumentException`, `InvalidOperationException` — all call `DeactivateOverlay()`.

## Crosshair Mode

**Grid**: `CrosshairGridCalculator` produces a `(N+1)×(M+1)` uniform grid where N = `horizontalKeys.Length`, M = `verticalKeys.Length`. Center cell at `(M/2, N/2)`. Used for both L1 and L2 subgrids.

**State machine** (`CrosshairStateMachine`): States: `Idle`, `AwaitInput`, `HorizSet`, `VertSet`, `BothSet`.

- Axis keys map to grid columns (horiz) or rows (vert), skipping center column/row.
- Key index → grid index: `keyIndex < center ? keyIndex : keyIndex + 1`.
- Same-axis re-press overrides the previous selection.
- `_lastSetWasHoriz` tracks which axis was set most recently for LIFO Escape from `BothSet`.
- `_actionPoint` updated eagerly on every axis key to the cell center at the intersection of selected axes (or center row/col if only one axis set).
- Arrow navigation via `CrossArrowNavigator` — only in `AwaitInput` state (before any axis key).
- Enter: computes L2 subgrid via `DynamicKeyReducer`. If cell too small → fires `CellSelected` + stays in `BothSet` (user picks action). Otherwise fires `SubgridEntered`. If `SubgridEntered` handler throws specific exceptions (`InvalidOperationException`, `NotSupportedException`, `ArgumentException`), catches and falls back to `CellSelected` path (prevents stuck `BothSet`).
- Escape LIFO: `BothSet` → clears last-set axis → single-axis state. Single-axis → `AwaitInput`. `AwaitInput` → `Cancelled`.
- `SubgridExited` event: fired by `ResetToAwaitInput(row, col)` when L2 session is popped. Resets state to `AwaitInput` with `_arrowRow`/`_arrowCol` at the given position, `_actionPoint` at cell center. Session handler re-renders full cross + highlights the arrow-position cell.

**Dynamic Key Reduction** (`DynamicKeyReducer`): At L2/L3, outermost keys are dropped symmetrically to keep cells ≥ `minCellPx` (default 5). Formula: `maxKeys = parentExtentPx / minCellPx - 1` (Crosshair: round to even). Returns `DynamicKeyReduction(ActiveKeys, OriginalStartIndex)`. `IsDisabled` when `< 2` keys remain. `UniformGridSession` accepts a `minCellPx` constructor parameter (default 5) and passes it to `NavigatorStateMachine`. Crosshair/LogCrosshair sessions pass their `_minCellPx` field through to the inner `UniformGridSession`.

**Renderer** (`CrosshairRenderer`, implements `ICrosshairRenderer`): Mode-specific API — `RenderCross`, `HighlightColumn`, `HighlightRow`, `HighlightCell`, `RenderSubgridCross`, `FlashInvalidKey`. Cross cells (center row + center column) get borders + single-char labels. Non-cross cells dimmed at 15% opacity. Uses DIP-space grid computation like `GridRenderer`.

**Session** (`CrosshairSession`): Wraps SM + renderer. Constructs SM once and reuses across `Activate`/`Deactivate` cycles. Events map SM callbacks → `CursorMoveRequested` + renderer calls.

## LogCrosshair Mode

**Grid**: `LogGridCalculator` produces a `(N+1)×(M+1)` grid with logarithmic cell sizing. Cells grow geometrically from center outward. **Cross-arm cells** (center row and center column) expand proportionally in their perpendicular dimension: horizontal arm cells grow in height (30% of width, minimum = center row height), vertical arm cells grow in width (30% of height, minimum = center column width). This ensures labels remain readable on all cross cells regardless of aspect ratio.

Algorithm:
1. Center cell has `logBaseSize` pixels width/height (default 5).
2. Growth ratio found via binary search per axis: `baseSize · (r + r² + … + r^cellsPerSide) = availableDistance`.
3. The shorter half-axis constrains the ratio; outermost cells on the longer side absorb remaining space (last-cell absorption).
4. Cross-arm cells are anchored on the center axis (centerY for horiz arm, centerX for vert arm) and expand symmetrically.
5. Cells with width or height < 1 pixel are flagged as degenerate.
6. All edge positions clamped to screen bounds (cross-arm cells may extend beyond their row/col band for readability).

Result type: `LogCrosshairGrid` with `IsDegenerate(row, col)` method. Same structure as `CrosshairGrid` (Cells, Cols, Rows, CenterCol, CenterRow, CellAt, CenterCell, IsOnCross).

**State machine** (`LogCrosshairStateMachine`): States: `Idle`, `AwaitInput`, `HorizSet`, `VertSet`, `BothSet`.

- Same axis-key mapping as Crosshair (key index → grid index, skipping center).
- `HorizSet`/`VertSet` track the last-set axis. Both can be set simultaneously — state reflects which was set most recently.
- Degenerate cells: axis key or arrow targeting a degenerate cell fires `InvalidKeyPressed` (rejected).
- Enter: computes L2 subgrid via `DynamicKeyReducer`. If cell too small → fires `CellSelected` + stays in `BothSet`. Otherwise fires `SubgridEntered`. If handler throws specific exceptions (`InvalidOperationException`, `NotSupportedException`, `ArgumentException`), falls back to `CellSelected`.
- Arrow navigation: `CrossArrowNavigator` in `AwaitInput` only. Arrows into degenerate cells also fire `InvalidKeyPressed`.
- Escape LIFO: `BothSet` → clears last-set axis → single-axis state. `HorizSet`/`VertSet` alone → `AwaitInput` + fires `AxisCleared`. `AwaitInput` → `Cancelled`.
- `AxisCleared` event: notifies session to recenter grid and re-render base cross and move cursor back to center cell.
- `SubgridExited` event: fired by `ResetToAwaitInput(row, col)` when L2 session is popped. Same semantics as Crosshair — resets to `AwaitInput`, sets action point, session recenters grid + re-renders cross + highlights cell.
- `UpdateGrid(grid, newCenter)`: replaces the current grid and resets state to `AwaitInput`. Used by session to recenter on navigation.

**Renderer** (`LogCrosshairRenderer`, implements `ILogCrosshairRenderer`): Same visual API as `ICrosshairRenderer` minus `RenderSubgridCross`. Uses **element pooling**: rectangles, text paths, and dashed connector lines reused across renders via index tracking. Staleness detected via `Parent == null` after external canvas clear. `FlashInvalidKey` uses a pooled single rectangle (collapsed when not animating). **External labels**: cross-arm cells below the small-cell threshold (`height < minLabelFontSize * 1.8` or `halfWidth < minLabelFontSize * 1.6`) skip inline labels and instead render external labels outside the small-cell zone with dashed connectors. Center cell bullet (`•`) always renders inline. Both column and row external labels render in all states. Opacity matches inline patterns per render state. Reuses `LogGridRenderer.ResolveOverlaps` for label spread.

**Session** (`LogCrosshairSession`): Wraps SM + renderer. Grid computed at `Activate()` using cursor origin as center point. **Recenters grid** on every navigation event (horiz key, vert key, arrow move, axis clear, subgrid exit) via `RecenterGrid(newCenter)` — recalculates the log grid centered on the new cursor position, calls `SM.UpdateGrid`, and re-renders. This ensures the logarithmic scale always reflects distance from the current cursor position.

Supports L2 level stack via `CrosshairSession` with dynamically-reduced key sets. L2 session is created with null renderer (visual feedback handled by the crosshair renderer wired into the L2 session itself).

## LogGrid Mode

**Grid**: `LogScaleGridCalculator` produces an `N×M` grid (default 10×10) centered on the cursor position. Cell sizes grow geometrically from center outward, with independent growth ratios per half-axis (left/right and up/down each solve their own binary search). The cursor position becomes the shared edge between the two innermost cells on each axis.

Algorithm (`LogScaleGridCalculator.Calculate`):
1. Clamp center to screen bounds.
2. For each axis, `ComputeAxisEdges` builds edge positions. Center = dividing edge. Each half-axis: solve `FindHalfRatio` via binary search to find ratio r ≥ 1 such that `baseSize · (1 + r + r² + … + r^(n-1)) = halfDistance`.
3. Uniform fallback: when `halfDistance < n · baseSize`, returns r = −1 → uniform cell sizes.
4. Outermost edges pinned to bounds (last-cell absorption). Intermediate edges clamped.
5. **Cell collapsing** (`CollapseSmallCells`): After edge computation, scans from center outward on each half. When the first cell < 5px is found, all cells from that cell outward to the screen edge are collapsed to zero-width (edges set to the boundary value). This removes keys from the outside inward — e.g., on the left side: `asdfg` → `sdfg` → `dfg` → `fg` → `g`.

Result type: `LogGrid` with `Cells`, `Cols`, `Rows`, `CenterPoint`, `ColEdges`, `RowEdges`.

**State machine** (`LogGridStateMachine`): States: `Idle`, `AwaitInput`, `FirstKeySet`, `ArrowCellSet`, `PostTwoKeyRecenter`.

- First key (horizontal): maps key index → column index via `_horizKeyMap`. Rejects columns with `Width < 5px` (collapsed cells). Sets state to `FirstKeySet`, fires `FirstKeySelected`.
- Second key (vertical): maps key index → row index via `_vertKeyMap`. Only valid in `FirstKeySet`. Rejects rows with `Height < 5px`. Fires `CellSelected`, state → `PostTwoKeyRecenter`.
- Arrow keys: accepted in `AwaitInput`, `ArrowCellSet`, or `PostTwoKeyRecenter`. Moves `_arrowRow`/`_arrowCol` ±1, but refuses to enter cells with width or height < 5px. Fires `ArrowMoved`.
- Enter in `ArrowCellSet`: fires `ArrowRecenterRequested` → session recenters grid on that cell's center.
- Escape: fires `Cancelled` from any state.
- Action keys: fire `ActionRequested` with current `_actionPoint` from any state.
- `UpdateGrid(LogGrid)`: replaces grid, resets to `AwaitInput` at grid center. Used after recentering.

**Session** (`LogGridSession`): Wraps SM + renderer + `LogScaleGridCalculator`. Computes grid at `Activate()` and on every recenter. Two-key selection → recenter immediately. Arrow-selected Enter → recenter. Recenter: moves cursor to cell center, recomputes grid centered there, calls `SM.UpdateGrid`, re-renders. The first-key indicator receives its column index; `LogGridRenderer` resolves that index through its rebuilt axis label generator.

**Key policy** (`LogGridKeyPolicy`): Maps mode config horizontal/vertical key arrays to grid column/row indices.

## `AxisLabelGenerator`

Per-axis label generator for Crosshair/LogCrosshair modes. Maps VKey array indices to single-character display labels via `IKeyLabelResolver`. Used by both renderers for cross-cell labeling.

## `CrossArrowNavigator`

Stateless helper for crosshair/log-crosshair grid arrow navigation. Free 2D movement: Left/Right change column, Up/Down change row, independently from any position. Wrapping at grid edges. The `centerRow`/`centerCol` parameters are retained for API compatibility but unused — restriction gating (arrows only in `AwaitInput`) is handled by the calling state machine.

## `ModeConfig`

Per-mode settings: `Enabled`, `Default`, `ChordKey` (nullable — default mode has none), `ArrowKeys`, `TwoKey`, `LogBaseSize` (LogCrosshair only), `LogGridBaseSize` (LogGrid only), `HorizontalKeys`/`VerticalKeys` (Crosshair/LogCrosshair axis keys, nullable — null means use defaults).

Bool properties default to `false` and arrays to `null`. Usable defaults live in `ModesConfig` property initializers. The config loader (JsonDocument pre-pass) merges partial user overrides onto those defaults.

## `ModesConfig`

Container with four named properties (`UniformGrid`, `Crosshair`, `LogCrosshair`, `LogGrid`), each a `ModeConfig` with appropriate defaults. Lives on `ConfigModel.Modes`. LogGrid defaults: `Enabled=true`, `ChordKey=OemComma`, `ArrowKeys=true`, `TwoKey=true`, `LogGridBaseSize=10`.

## Multi-monitor Non-Goal

Multi-monitor support is explicitly out of scope. Cursor outside primary screen bounds at activation time → suppress overlay + tray notification. Layout change mid-session = known limitation.

## App-Scope Navigation

Scopes the overlay grid to a single application window instead of the full screen. Triggered by a configurable chord key (`AppScopeConfig.ChordKey`, default `B`).

**Activation flow**:
1. `NavigatorCoordinator` captures `_preOverlayHwnd` (foreground window HWND) before showing overlay in `OnHotKeyActivated`.
2. When the chord key is pressed (before mode lock), coordinator calls `IForegroundWindowProvider.GetWindowBounds(_preOverlayHwnd)` using the pre-captured handle.
3. Bounds validated: `IsIconic` (minimized), zero/negative dimensions, empty intersection with `_screenBounds` → rejection with flash message.
4. Partial off-screen windows clipped to screen bounds. Cursor origin clamped into clipped bounds.
5. `SwitchToAppScope(clipped)`: deactivates current session → clears canvas → creates new session at clipped bounds → sets `_appScoped = true`. Overlay stays full-screen — grid renderers use screen-space DIP coordinates that assume canvas origin matches screen origin. Resizing the overlay would break coordinate mapping.

**`ActiveBounds` property**: returns `_appScopeBounds` when `_appScoped`, else `_screenBounds`. Used by `SwitchMode` and `OnSessionActionRequested` for bounds validation.

**Switching guard** (`_switching`): prevents `OnFocusLost` from triggering `DeactivateOverlay` during the hide/show cycle in `SwitchToAppScope` and `ResetOverlayForDrag`.

**Drag interaction**: `ResetOverlayForDrag` clears `_appScoped`/`_appScopeBounds`, removes visual border. Overlay is already full-screen so no resize needed. Chord key remains available during drag mode.

**Visual indicator**: 2px rectangle on `StatusCanvas` via `IOverlayWindow.SetAppScopeBorder(bool visible, Rectangle bounds)`. Converts physical-pixel bounds to DIPs using `TransformFromDevice` for correct positioning on the full-screen canvas. Uses `ThemeModel.AppScopeBorderColor`.

**Mode switching**: `SwitchMode` uses `ActiveBounds`, so mode changes within app-scope stay scoped to the window.

**Escape/deactivation**: `DeactivateOverlay` clears all app-scope state (`_appScoped`, `_appScopeBounds`, `_preOverlayHwnd`, border).

**Auto-repeat guard**: chord key dispatch checks `_appScoped` to prevent re-processing held keys.
