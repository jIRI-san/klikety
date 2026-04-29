---
description: Navigation mode system — IModeSession interface, mode switching, Crosshair and LogCrosshair modes with their state machines, renderers, and grid calculators.
globs:
  - src/Klikety/Navigation/IModeSession.cs
  - src/Klikety/Navigation/ModeSessionFactory.cs
  - src/Klikety/Navigation/CrosshairSession.cs
  - src/Klikety/Navigation/CrosshairStateMachine.cs
  - src/Klikety/Navigation/LogCrosshairSession.cs
  - src/Klikety/Navigation/LogCrosshairStateMachine.cs
  - src/Klikety/Navigation/CrossArrowNavigator.cs
  - src/Klikety/Navigation/UniformGridSession.cs
  - src/Klikety/Grid/CrosshairGridCalculator.cs
  - src/Klikety/Grid/LogGridCalculator.cs
  - src/Klikety/Grid/DynamicKeyReducer.cs
  - src/Klikety/Grid/AxisLabelGenerator.cs
  - src/Klikety/Overlay/CrosshairRenderer.cs
  - src/Klikety/Overlay/LogCrosshairRenderer.cs
  - src/Klikety/Config/NavigationMode.cs
---

# Navigation Modes

Three navigation modes, each implementing `IModeSession` with independent configuration via `ModeConfig`:

- **UniformGrid** — two-key grid scheme using `firstKeys`/`secondKeys`. L1→L2→L3 level stack.
- **Crosshair** — cross-style axis key navigation with uniform grid cells. Supports L2 subgrid.
- **LogCrosshair** — logarithmic-scaled cross grid centered on cursor. Flat (no subgrid).

## `IModeSession` Interface

```csharp
interface IModeSession {
    event Action<Point, MouseAction>? ActionRequested;
    event Action? Cancelled;
    event Action<Point>? CursorMoveRequested;
    void Activate(Rectangle screenBounds, Point origin);
    void OnKey(VKey key);
    void Deactivate();
}
```

All modes implement this interface. `NavigatorCoordinator` owns the active session, subscribing to events on activation and calling `Deactivate()` on teardown. Sessions are stateless between `Deactivate()`→`Activate()` cycles (SM reset internally).

## `ModeSessionFactory`

Creates `IModeSession` instances by mode name. Constructor: `(ConfigModel, ActionMapper, IGridRenderer?, ICrosshairRenderer?, ILogCrosshairRenderer?)`. Renderers are nullable — null when the mode's renderer cannot be constructed (e.g., disabled mode with null keys).

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
- Enter: computes L2 subgrid via `DynamicKeyReducer`. If cell too small → fires `CellSelected` + stays in `BothSet` (user picks action). Otherwise fires `SubgridEntered`.
- Escape LIFO: `BothSet` → clears last-set axis → single-axis state. Single-axis → `AwaitInput`. `AwaitInput` → `Cancelled`.

**Dynamic Key Reduction** (`DynamicKeyReducer`): At L2/L3, outermost keys are dropped symmetrically to keep cells ≥ `minCellPx`. Formula: `maxKeys = parentExtentPx / minCellPx - 1` (Crosshair: round to even). Returns `DynamicKeyReduction(ActiveKeys, OriginalStartIndex)`. `IsDisabled` when `< 2` keys remain.

**Renderer** (`CrosshairRenderer`, implements `ICrosshairRenderer`): Mode-specific API — `RenderCross`, `HighlightColumn`, `HighlightRow`, `HighlightCell`, `RenderSubgridCross`, `FlashInvalidKey`. Cross cells (center row + center column) get borders + single-char labels. Non-cross cells dimmed at 15% opacity. Uses DIP-space grid computation like `GridRenderer`.

**Session** (`CrosshairSession`): Wraps SM + renderer. Constructs SM once and reuses across `Activate`/`Deactivate` cycles. Events map SM callbacks → `CursorMoveRequested` + renderer calls.

## LogCrosshair Mode

**Grid**: `LogGridCalculator` produces a `(N+1)×(M+1)` grid with logarithmic cell sizing. Cells grow geometrically from center outward.

Algorithm:
1. Center cell has `logBaseSize` pixels width/height (default 5).
2. Growth ratio found via binary search per axis: `baseSize · (r + r² + … + r^cellsPerSide) = availableDistance`.
3. The shorter half-axis constrains the ratio; outermost cells on the longer side absorb remaining space (last-cell absorption).
4. Cells with width or height < 1 pixel are flagged as degenerate.
5. All edge positions clamped to screen bounds.

Result type: `LogCrosshairGrid` with `IsDegenerate(row, col)` method. Same structure as `CrosshairGrid` (Cells, Cols, Rows, CenterCol, CenterRow, CellAt, CenterCell, IsOnCross).

**State machine** (`LogCrosshairStateMachine`): States: `Idle`, `AwaitInput`, `HorizSet`, `VertSet`. Flat — no subgrid, no L2/L3.

- Same axis-key mapping as Crosshair (key index → grid index, skipping center).
- `HorizSet`/`VertSet` track the last-set axis. Both can be set simultaneously — state reflects which was set most recently.
- Degenerate cells: axis key or arrow targeting a degenerate cell fires `InvalidKeyPressed` (rejected).
- Enter: no-op (flat mode, no subgrid).
- Arrow navigation: `CrossArrowNavigator` in `AwaitInput` only. Arrows into degenerate cells also fire `InvalidKeyPressed`.
- Escape LIFO: `HorizSet` with vert set → clears horiz → `VertSet`. `HorizSet` alone → `AwaitInput` + fires `AxisCleared`. `VertSet` similarly. `AxisCleared` event resets `_arrowRow`/`_arrowCol` to grid center.
- `AxisCleared` event: notifies session to re-render base cross and move cursor back to center cell.

**Renderer** (`LogCrosshairRenderer`, implements `ILogCrosshairRenderer`): Same visual API as `ICrosshairRenderer` minus `RenderSubgridCross`. Uses **element pooling**: rectangles and text paths are reused across renders via index tracking. Staleness detected via `Parent == null` after external canvas clear. `FlashInvalidKey` uses a pooled single rectangle (collapsed when not animating).

**Session** (`LogCrosshairSession`): Wraps SM + renderer. Grid computed at `Activate()` using cursor origin as center point. `AxisCleared` → re-renders cross + moves cursor to center cell center.

## `AxisLabelGenerator`

Per-axis label generator for Crosshair/LogCrosshair modes. Maps VKey array indices to single-character display labels via `IKeyLabelResolver`. Used by both renderers for cross-cell labeling.

## `CrossArrowNavigator`

Stateless helper for crosshair/log-crosshair grid arrow navigation. Free 2D movement: Left/Right change column, Up/Down change row, independently from any position. Wrapping at grid edges. The `centerRow`/`centerCol` parameters are retained for API compatibility but unused — restriction gating (arrows only in `AwaitInput`) is handled by the calling state machine.

## `ModeConfig`

Per-mode settings: `Enabled`, `Default`, `ChordKey` (nullable — default mode has none), `ArrowKeys`, `TwoKey`, `LogBaseSize` (LogCrosshair only), `HorizontalKeys`/`VerticalKeys` (Crosshair/LogCrosshair axis keys, nullable — null means use defaults).

Bool properties default to `false` and arrays to `null`. Usable defaults live in `ModesConfig` property initializers. The config loader (JsonDocument pre-pass) merges partial user overrides onto those defaults.

## `ModesConfig`

Container with three named properties (`UniformGrid`, `Crosshair`, `LogCrosshair`), each a `ModeConfig` with appropriate defaults. Lives on `ConfigModel.Modes`.

## Multi-monitor Non-Goal

Multi-monitor support is explicitly out of scope. Cursor outside primary screen bounds at activation time → suppress overlay + tray notification. Layout change mid-session = known limitation.
