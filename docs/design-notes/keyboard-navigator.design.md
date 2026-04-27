---
description: Overlay lifecycle, navigation state machine, Win32 interop patterns, and key scheme for the keyboard-driven mouse navigator.
globs:
  - src/Klikety/**
  - src/Klikety.Tests/**
  - src/Klikety.SmokeTests/**
---

# Keyboard Navigator Design Note

## Overlay Lifecycle

- `OverlayWindow` is a WPF window: `WindowStyle=None`, `AllowsTransparency=True`, `Topmost=True`, sized to primary screen bounds converted to DIPs via `PresentationSource` transform.
- `DeactivateOverlay()` is the single idempotent exit method called from every path: action fired, Escape at L1, focus loss, exception, Quit. It calls `IGridRenderer.ClearCanvas()`, hides the overlay, calls `IKeyboardHookService.Disable()`, and resets `NavigatorStateMachine` to `Idle`. Safe to call multiple times.
- `OverlayWindow.Deactivated` event wires to `DeactivateOverlay()` to handle focus loss (Alt+Tab, OS notifications, background app stealing focus).
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

## Win32 Interop

All Win32 interaction is behind interfaces (`IHotKeyService`, `IKeyboardHookService`, `IMouseActionService`). Real implementations are thin P/Invoke wrappers. Fakes are injected in tests.

### Interfaces

```csharp
interface IHotKeyService   { event EventHandler Activated; bool Register(HotKeyConfig); void Unregister(); }
interface IKeyboardHookService { event EventHandler<VKey> KeyPressed; bool Enable(); void Disable(); }
interface IMouseActionService  { void MoveTo(Point physicalPoint); void SendAction(Point physicalPoint, MouseAction action); }
interface IGridRenderer {
    void SetTransform(Matrix m);
    void RenderGrid(IReadOnlyList<GridCell> cells);
    void HighlightColumn(IReadOnlyList<GridCell> cells, int col);
    void HighlightCell(IReadOnlyList<GridCell> cells, GridCell cell);
    void RenderSubgrid(IReadOnlyList<GridCell> cells);
    void RenderSubgridOverGrid(IReadOnlyList<GridCell> backgroundCells, IReadOnlyList<GridCell> subgridCells);
    void HighlightColumnOverGrid(IReadOnlyList<GridCell> backgroundCells, IReadOnlyList<GridCell> subgridCells, int col);
    void HighlightCellOverGrid(IReadOnlyList<GridCell> backgroundCells, IReadOnlyList<GridCell> subgridCells, GridCell cell);
    void FlashInvalidKey(); void ClearCanvas();
}
```

`IGridRenderer` is extracted from `GridRenderer` for testability. `NavigatorCoordinator` takes `IGridRenderer?` — null-safe (all calls use `?.`). `FakeGridRenderer` records all calls for assertion in tests. `GridRenderer` constructor: `(Canvas, ThemeModel, LabelGenerator, double minLabelFontSize)`.

### `IKeyboardHookService` — `SetWindowsHookEx(WH_KEYBOARD_LL)`

- Hook installed only while overlay is visible; uninstalled in `DeactivateOverlay()`.
- Hook callback reads `VKey` + state from `KBDLLHOOKSTRUCT`, calls `CallNextHookEx` immediately, then posts `VKey` to UI thread via `Dispatcher.InvokeAsync` — no blocking work in callback (OS kills hook after ~300 ms).
- `KeyPressed` event raised on UI thread only.
- If `SetWindowsHookEx` returns null, `Enable()` returns `false`; overlay closed + tray notification.

### `IHotKeyService` — `RegisterHotKey`

- Registered via Win32 `RegisterHotKey` with dispatcher message loop handling `WM_HOTKEY`.
- Registration attempted at startup; failure (conflict) stored as validation error, surfaced as tray notification.
- `StartupValidator` probes registration and immediately unregisters to detect conflicts before full startup.

### `IMouseActionService` — `SendInput`

- `MoveTo`: normalizes physical-pixel coords to 0–65535 range using primary screen bounds, then sends `MOUSEINPUT` with `MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE`.
- `SendAction`: sends appropriate `MOUSEEVENTF_*DOWN/UP` pairs. Double-click = two left-click pairs in sequence.
- All geometry in physical pixels; DIP→physical conversion happens at WPF rendering boundary only, via `PresentationSource.CompositionTarget.TransformToDevice`.

### `NativeMethods.GetPrimaryScreenBounds()` — `GetMonitorInfoW`

- No WinForms dependency; no `Screen.PrimaryScreen`.
- **Important**: `LibraryImport` (source-generated) does NOT auto-resolve `W` suffix like `DllImport`. Must use `EntryPoint = "GetMonitorInfoW"` explicitly.
- Implementation:
  ```csharp
  var hMon = MonitorFromPoint(new POINT(0, 0), MONITOR_DEFAULTTOPRIMARY);
  var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
  GetMonitorInfo(hMon, ref info);
  // info.rcMonitor = physical-pixel bounds of primary monitor
  ```
- Returns `System.Drawing.Rectangle` (physical pixels). Used by `GridCalculator`, `SubgridCalculator`, `MouseActionService`, and `OverlayWindow` sizing.

### Keyboard layout independence

- All key handling uses VKey codes (physical-position stable across layouts).
- `LabelGenerator` derives display characters via `ToUnicode` / `MapVirtualKey` against the active HKL so on-screen labels reflect the user's keyboard layout.
- Fallback: if `ToUnicode` returns no character (dead key, unmapped), VKey name string is used as the label.

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

## Tray Integration

- Tray icon via `H.NotifyIcon.Wpf` (`TaskbarIcon` in XAML). No WinForms dependency.
- `ShutdownMode=OnExplicitShutdown` — process persists until "Quit" menu item calls `Application.Current.Shutdown()`.
- Context menu items: **About** (small `AboutWindow`), **Open Configuration Folder** (`Process.Start("explorer.exe", path)`), **Start with Windows** (toggle with checkmark), **Quit**.
- Tray notifications used for: hotkey conflict, hook install failure, config/key-binding violations, theme load failure.

## Theme System

- `ThemeModel` POCO: label font family/size/color/weight; cell border color + thickness; normal cell background color + opacity; dimmed cell overlay color + opacity; highlighted column background + border color; subgrid distinct border/label color; external label color; connector line color + thickness; label outline color + thickness.
- `ThemeLoader` resolves `"theme"` config value: bare name → `%APPDATA%\Klikety\themes\<name>.theme.json`; relative path → resolved from config folder only; must have `.theme.json` extension; path canonicalized; traversal sequences (`../`) rejected; fall back to built-in dark on any error + tray notification.
- Built-in `dark.theme.json` and `light.theme.json` shipped as embedded resources; extracted to `%APPDATA%\Klikety\themes\` on first run.

## Grid Rendering

### DIP-Space Grid Computation

`GridRenderer` works entirely in DIP (device-independent pixel) space to avoid scaling artifacts at non-100% DPI:

- `EnsureTransform()` auto-reads the device→DIP matrix from `PresentationSource.FromVisual(_canvas)` on first render. Falls back to identity if unavailable.
- `ComputeRegionFromCells(cells)` transforms only the two corners (top-left of first cell, bottom-right of last cell) to DIP, producing a `Rect` region.
- `DipRectForCell(row, col, region, cols, rows)` subdivides the region evenly — no per-cell integer rounding, so cells tile perfectly at any DPI.

### Font Auto-Scaling

Labels auto-scale to fill a fraction of cell height:

- **L1 grid**: 80% of cell height (`heightFraction = 0.8`)
- **L2/L3 subgrids**: 90% of cell height (`heightFraction = 0.9`) for tighter packing before switching to external labels
- Primary constraint is height (cells are wider than tall), with a secondary cap at 95% of half-width to prevent horizontal overflow.
- Font size clamped to `[minLabelFontSize .. theme.LabelFontSize * 3]`.
- Method: `ComputeAutoFontSize(cellHalfWidth, cellHeight, heightFraction)`.

### Unified Grid Rendering

- `RenderGrid(cells)` renders the full-screen grid with labels. Used at L1 activation and on escape back to L1.
- `HighlightColumn(cells, col)` dims non-matching cells, highlights selected column. Used at L1.
- `HighlightCell(cells, cell)` crosshair-highlights a cell for L1 arrow navigation (row + column + intersection).
- `HighlightCellOverGrid(backgroundCells, subgridCells, cell)` crosshair-highlights a cell within a subgrid rendered over faint background grid. Used for L2/L3 arrow navigation. Supports external labels when cells are too small.
- `ClearCanvas()` — removes all children from the WPF Canvas. Called in `DeactivateOverlay()` before `Hide()`.
- `NavigatorCoordinator` tracks `_l1Cells`, `_subgridCells` (nullable), `_l2SubgridCells` for rendering context. L1 uses `HighlightColumn`/`RenderGrid`; L2+ uses `HighlightColumnOverGrid`. `OnCellEntered` stores subgrid cells; `OnLevelExited` (L3→L2) restores `_subgridCells = _l2SubgridCells`; `OnColumnUnhighlighted` at L1 clears subgrid state and calls `RenderGrid`.
- `RenderSubgridOverGrid(backgroundCells, subgridCells)` — renders parent grid as faint borders (no labels, 0.15 opacity) then subgrid on top with labels. Called by coordinator on `CellEntered`.
- `HighlightColumnOverGrid(backgroundCells, subgridCells, col)` — renders parent grid as faint background + subgrid with column highlighted. Called by coordinator on `ColumnHighlighted` at L2/L3.
- Private helpers: `RenderBackgroundGrid(cells)` draws faint borders, `RenderSubgridContent(cells)` draws subgrid without clearing canvas.
- All render methods share `AddCellRect(dipRect, fill, stroke, strokeThickness)` private helper — creates `Rectangle` shape and adds to Canvas.

### Outlined Text Rendering

All labels (inline and external) use two-layer `Path` rendering for crisp outlines that ensure readability over any background:

1. `FormattedText.BuildGeometry()` converts text to a `Geometry`.
2. Layer 1 (stroke-only): `Path` with `Fill=Transparent`, `Stroke=outlineBrush`, `StrokeThickness=thickness*2`, `StrokeLineJoin=Round`. Doubled thickness because only the outer half is visible.
3. Layer 2 (fill-only): `Path` with `Fill=foreground`, no stroke. Renders on top, covering the inner stroke bleed.

Method: `AddOutlinedText(text, typeface, fontSize, fill, outlineBrush, outlineThickness, opacity, areaX, areaY, areaWidth, areaHeight)`. Helper `MeasureText` returns `Size` for layout calculations without creating visual elements.

- Theme properties: `LabelOutlineColor` (default `#000000` dark / `#FFFFFF` light), `LabelOutlineThickness` (default `1.5`).

### External Label Rendering

When subgrid cells are too small to fit labels, the renderer switches to external label layout. Decision considers both dimensions:
- Height: `cellDipHeight < minLabelFontSize * 1.8`
- Half-width: `cellDipWidth / 2 < minLabelFontSize * 1.6` (wide chars like "W")

Method: `ShouldUseExternalLabels(cellDipHeight, cellDipWidth, minLabelFontSize)` — `internal static`, testable.

**Progressive reveal**: When awaiting first key, only column first-key labels are shown (top + bottom). After first key is pressed (`HighlightColumnOverGrid`), row second-key labels appear (left + right). This matches the natural key-entry order.

**Fan-out algorithm**: With 8 labels per side, labels at the grid edge may overlap. `ComputeFanOut(labelCount, maxLabelSize, gridExtent, standardMargin)` computes:
- `distance`: how far from the grid edge to place the label line. When labels fit at standard spacing, equals `standardMargin` (fontSize × 1.5). When labels would overlap, increases by half the extra width needed.
- `extent`: total width/height to spread labels across. Equals `gridExtent` when labels fit, otherwise `labelCount × (maxLabelSize + minGap)`.

Labels are evenly spaced across `extent`, centered on the grid center. Angled dashed connector lines link each label to its column/row center at the grid edge. When no fan-out is needed, connectors are straight (vertical for columns, horizontal for rows).

**Screen-edge-aware direction**: Labels only render on sides with enough space:
- Column labels: skip above if `gridTop < fanOutDist`; skip below if `screenHeight - gridBottom < fanOutDist`.
- Row labels: skip left if `gridLeft < fanOutDist`; skip right if `screenWidth - gridRight < fanOutDist`.
- If neither side has space, render both (clipped).

When external labels are active, no internal cell labels are rendered — cells show only background/highlight rectangles.

**Fan-out extent clamping**: `labelExtentStart` is clamped to `[0, screenDimension - labelExtent]` so labels never render off-screen when the subgrid is near a screen edge.

**Font size**: `ComputeExternalFontSize` uses `max(cellBased, minLabelFontSize)` — at L3 where cell-based auto-size would be tiny, the floor of `minLabelFontSize` (default 10 DIP) ensures readable labels.

**L3 legibility**: When `useExternalLabels` is true (cells too small for inline labels):
- Grid border opacity reduced to 30% and thickness halved (min 0.5px) — keeps grid structure visible without obscuring content.
- Column highlight fill at 30% opacity (vs 50% at L2) for better see-through.
- Alternating row bands (12% opacity, every other row) provide cross-hair visual aid during column highlight.

- Theme properties: `ExternalLabelColor`, `ConnectorLineColor`, `ConnectorLineThickness`.
- Config: `MinLabelFontSize` (default 14.0 DIP) controls both the external-label threshold and the font floor.

## Config

- Format: JSONC (`JsonCommentHandling.Skip`); stored at `%APPDATA%\Klikety\config.json`.
- Written on first run from embedded `config.json` template if absent. **Not overwritten on subsequent runs** — changing defaults in the embedded template does not affect existing installs. When a config or theme default changes during development, the user's `%APPDATA%\Klikety\config.json` and `%APPDATA%\Klikety\themes\*.theme.json` must be updated manually (or the files deleted to trigger re-extraction).
- Key fields: `hotKey`, `actionBindings` (VKey → MouseAction), `firstKeys`/`secondKeys` (flat VKey arrays), `level3CellSizeThreshold`, `logLevel`, `navigationMode`, `theme`.
- Validation at startup: reserved keys (Escape, hotkey modifiers, arrow VKeys, VK_RETURN) not in nav/action sets; action ↔ nav key overlap; cross-set disjointness (`firstKeys ∩ secondKeys = ∅`); per-set duplicate check; all violations collected and surfaced via tray notification list.

## Logging

- `Microsoft.Extensions.Logging` with rolling file sink → `%APPDATA%\Klikety\logs\`.
- Config: `logLevel` (default `Debug`), `fileLoggingEnabled` (default `true`), `retainedLogFileCount` (default `7`).
- Debug-level logging in `NavigatorCoordinator`:
  - Every mapped keystroke: key name + state before processing.
  - State transitions: `{before} → {after}` when state changes.
  - All SM events: `ColumnHighlighted`, `CellHighlighted`, `CellEntered`, `LevelExited`, `ColumnUnhighlighted`, `ActionRequested`, `Cancelled`, `InvalidKeyPressed` — with relevant parameters (col, row, level, cell counts).

## Test Infrastructure

- **Unit tests** (`Klikety.Tests`): xUnit, 105 tests covering `GridCalculator`, `SubgridCalculator`, `LabelGenerator`, `ConfigLoader`, `NavigatorStateMachine`, `ArrowNavigator`, `NavigatorCoordinator` integration, and `GridRenderer` threshold/fan-out logic.
- **Test fakes** in `Klikety.Tests/Fakes/`: `FakeHotKeyService`, `FakeKeyboardHookService` (with `SimulateKey`), `FakeMouseActionService` (records calls), `FakeOverlayWindow` (tracks show/hide/focus-loss), `FakeGridRenderer` (records `RenderCall` list — method name, cells, col — for assertion).
- **Smoke tests** (`Klikety.SmokeTests`): `[Trait("Category", "Smoke")]`, exercises real Win32 P/Invoke on a live display. Not CI-safe.
- `InternalsVisibleTo` in `Klikety.csproj` exposes `internal` types (e.g. `NativeMethods`) to both test projects.
- `NavigatorCoordinator` integration tests inject fakes and simulate full hotkey→key→action flows without any Win32 calls, except `NativeMethods.GetPrimaryScreenBounds()` which is called in `OnHotKeyActivated` — this works in tests because it's real Win32 (not mocked).
- `LogLevel` read from config.
- `ILogger<T>` injected into Win32 services, state machine, and loader classes.

## Testing Seam

- All Win32 service interfaces are the seam for testing.
- `FakeKeyboardHookService.SimulateKey(VKey)` — programmatically injects key events.
- `FakeMouseActionService` — records `(physicalX, physicalY, MouseAction)` call list.
- `FakeOverlayWindow` — tracks show/hide, rendered grid state, supports focus-loss simulation.
- Integration tests are hermetic (no real display, no OS hooks, no timing dependencies).
- Smoke test project (`Klikety.SmokeTests`, `[Trait("Category","Smoke")]`) excluded from CI; covers real Win32 call verification.
