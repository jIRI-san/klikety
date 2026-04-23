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
- `DeactivateOverlay()` is the single idempotent exit method called from every path: action fired, Escape at L1, focus loss, exception, Quit. It hides the overlay, calls `IKeyboardHookService.Disable()`, and resets `NavigatorStateMachine` to `Idle`. Safe to call multiple times.
- `OverlayWindow.Deactivated` event wires to `DeactivateOverlay()` to handle focus loss (Alt+Tab, OS notifications, background app stealing focus).
- Cursor position at `Activate()` time is saved; restored via `IMouseActionService.MoveTo(originPoint)` when Escape is pressed at L1 or focus is lost.
- If `IKeyboardHookService.Enable()` returns failure on activation, `DeactivateOverlay()` is called immediately and a tray notification is shown — overlay never becomes visible.

## State Machine

States (linear progression, with back-navigation via Escape):

```
Idle → L1_AwaitFirst → L1_AwaitSecond → L1_AwaitAction
                                       → L2_AwaitFirst → L2_AwaitSecond → L2_AwaitAction
                                                                         → L3_AwaitFirst → L3_AwaitSecond → L3_AwaitAction
```

### Split-Screen Half Selection

At L1, the screen is divided into left and right halves, each with its own key set (`HalfKeySetsConfig`). The state machine holds `_leftKeys` and `_rightKeys` and determines the active half on the first key press:

- `HandleL1FirstKey(VKey)` checks both halves' `FirstKeys` arrays to identify which half the key belongs to.
- Sets `_activeHalf`, `_activeFirstKeys`, `_activeSecondKeys` accordingly.
- All subsequent key handling (L1 second key, L2/L3 navigation) uses `_activeFirstKeys`/`_activeSecondKeys`.
- At L1 `AwaitSecond`, pressing a first key from the *other* half switches the active half (re-entry).
- `ColumnHighlighted` event signature: `Action<ScreenHalf, int>` — carries which half was selected.

### Transitions

| From | Input | To | Side-effect |
|---|---|---|---|
| `Idle` | `HotKeyService.Activated` | `L1_AwaitFirst` | Save cursor origin; show overlay; enable hook |
| `L1_AwaitFirst` | first-key VKey (left or right) | `L1_AwaitSecond` | Detect half; raise `ColumnHighlighted(half, col)` |
| `L1_AwaitSecond` | second-key VKey | `L1_AwaitAction` | Move cursor to L1 cell center; raise `CellEntered(cell, 1)` |
| `L1_AwaitAction` | action VKey | `Idle` | Raise `ActionRequested(point, action)`; `DeactivateOverlay()` |
| `L1_AwaitAction` | nav VKey | `L2_AwaitFirst` | Render L2 subgrid |
| `L2_AwaitFirst` | first-key VKey | `L2_AwaitSecond` | Raise `ColumnHighlighted(half, col)` within subgrid |
| `L2_AwaitSecond` | second-key VKey | `L2_AwaitAction` | Move cursor to L2 cell center; raise `CellEntered(cell, 2)`; check L3 threshold |
| `L2_AwaitAction` | action VKey | `Idle` | Raise `ActionRequested(point, action)`; `DeactivateOverlay()` |
| `L2_AwaitAction` | nav VKey | `L3_AwaitFirst` | Render L3 subgrid (only if threshold met) |
| `L*_AwaitFirst/Second/Action` | Escape (L2/L3) | parent `AwaitAction` | Move cursor back to parent cell center |
| `L1_Await*` | Escape | `Idle` | Raise `Cancelled(originPoint)`; `DeactivateOverlay()` |

### Events raised by state machine

- `ColumnHighlighted(ScreenHalf half, int col)` — first key received; identifies which screen half, dims non-matching cells, highlights column
- `CellHighlighted(GridCell cell)` — arrow navigation; highlight cell without dimming others
- `CellEntered(GridCell cell, int level)` — two-key pair complete; move cursor to cell center, switch to subgrid view
- `ActionRequested(Point physicalPoint, MouseAction action)` — fire mouse action
- `Cancelled(Point originPoint)` — restore cursor to saved origin

### NavigationMode

- `TwoKey` — only two-key grid scheme active; arrow VKeys and `VK_RETURN` ignored
- `Arrow` — only arrow navigation; first/second key pairs ignored
- `Both` (default) — both schemes active simultaneously; any state accepts either input type

Arrow VKeys (`VK_LEFT`, `VK_RIGHT`, `VK_UP`, `VK_DOWN`) and `VK_RETURN` are **always reserved** — may not appear in `firstKeys`, `secondKeys`, or `ActionBindings`. Validated at startup.

### Level-3 trigger

After completing L2 two-key pair, if the L2 cell's physical-pixel area exceeds `Level3CellSizeThreshold` (default sized for ~4K), L3 is automatically available. Nav VKey at `L2_AwaitAction` transitions to `L3_AwaitFirst`.

### Escape at L2/L3

Escape goes back one level: `L2_Await* → L1_AwaitAction`, `L3_Await* → L2_AwaitAction`. Cursor moves to parent cell center on each back-step. Escape at any L1 state raises `Cancelled(originPoint)`.

## Win32 Interop

All Win32 interaction is behind interfaces (`IHotKeyService`, `IKeyboardHookService`, `IMouseActionService`). Real implementations are thin P/Invoke wrappers. Fakes are injected in tests.

### Interfaces

```csharp
interface IHotKeyService   { event EventHandler Activated; bool Register(HotKeyConfig); void Unregister(); }
interface IKeyboardHookService { event EventHandler<VKey> KeyPressed; bool Enable(); void Disable(); }
interface IMouseActionService  { void MoveTo(Point physicalPoint); void SendAction(Point physicalPoint, MouseAction action); }
```

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

### Split-screen two-key grid

The screen is split in half. Left-hand keys control the left half, right-hand keys the right half.

- Left half: first keys `A S D F`, second keys `W E R T` → 4×4 = 16 cells
- Right half: first keys `J K L ;`, second keys `Y U I O` → 4×4 = 16 cells
- Total: 32 cells per level
- L2/L3 subgrids use the active half's key set (4×4 = 16 cells per sublevel)

Config structure:
```jsonc
"keySets": {
    "left": { "firstKeys": ["A","S","D","F"], "secondKeys": ["W","E","R","T"] },
    "right": { "firstKeys": ["J","K","L","OemSemicolon"], "secondKeys": ["Y","U","I","O"] }
}
```

`KeySetsConfig` has `Left` and `Right` properties of type `HalfKeySetsConfig`, each with `FirstKeys` and `SecondKeys` arrays.

### `LabelGenerator`

- Input: `VKey[]` firstKeys × `VKey[]` secondKeys (per half)
- Output: bijective map — each (row, col) pair → display string derived from `ToUnicode(vkey, HKL)`
- API: `LabelFor(int row, int col) → CellLabel`, `Cols`/`Rows` properties
- `GridRenderer` holds two instances (`_leftLabelGenerator`, `_rightLabelGenerator`), switches `_activeLabelGenerator` based on the active half.

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

- `ThemeModel` POCO: label font family/size/color/weight; cell border color + thickness; normal cell background color + opacity; dimmed cell overlay color + opacity; highlighted column background + border color; subgrid distinct border/label color; external label color; connector line color + thickness.
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

### Split-Screen Rendering

- `RenderBothHalves(leftCells, rightCells)` renders both halves in a single pass, switching `_activeLabelGenerator` for each half.
- `SetActiveHalf(ScreenHalf)` switches the active label generator for subsequent `HighlightColumn`/`HighlightCell`/`RenderSubgrid` calls.
- `NavigatorCoordinator` tracks `_activeHalf` and sets `_currentCells` to the active half's cell list on `ColumnHighlighted`.

### External Label Rendering

When subgrid cells are too small to fit labels (cell DIP height < `MinLabelFontSize * 1.8`), `GridRenderer.RenderSubgrid` switches to external label layout:

- Column first-keys rendered above the grid, centered over their columns.
- Row second-keys rendered to the left of the grid, centered on their rows.
- Dashed connector lines link each external label to its grid column/row.
- Theme properties: `ExternalLabelColor`, `ConnectorLineColor`, `ConnectorLineThickness`.
- Config: `MinLabelFontSize` (default 10.0 DIP) controls the threshold.
- Decision method: `GridRenderer.ShouldUseExternalLabels(cellDipHeight, minLabelFontSize)` — `internal static`, testable.

## Config

- Format: JSONC (`JsonCommentHandling.Skip`); stored at `%APPDATA%\Klikety\config.json`.
- Written on first run from embedded `config.json` template if absent.
- Key fields: `hotKey`, `actionBindings` (VKey → MouseAction), `keySets.left`/`keySets.right` (each with `firstKeys`/`secondKeys` VKey arrays), `level3CellSizeThreshold`, `logLevel`, `navigationMode`, `theme`.
- Validation at startup: reserved keys (Escape, hotkey modifiers, arrow VKeys, VK_RETURN) not in nav/action sets; action ↔ nav key overlap; left/right first-key overlap (must be disjoint); per-half first/second key overlap; all violations collected and surfaced via tray notification list.

## Logging

- `Microsoft.Extensions.Logging` with rolling file sink → `%APPDATA%\Klikety\logs\`.

## Test Infrastructure

- **Unit tests** (`Klikety.Tests`): xUnit, 75 tests covering `GridCalculator`, `SubgridCalculator`, `LabelGenerator`, `ConfigLoader`, `NavigatorStateMachine`, `ArrowNavigator`, `NavigatorCoordinator` integration, and `GridRenderer` threshold logic.
- **Test fakes** in `Klikety.Tests/Fakes/`: `FakeHotKeyService`, `FakeKeyboardHookService` (with `SimulateKey`), `FakeMouseActionService` (records calls), `FakeOverlayWindow` (tracks show/hide/focus-loss).
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
