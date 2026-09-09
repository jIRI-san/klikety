---
description: Win32 P/Invoke patterns — service interfaces, keyboard hook, hotkey registration, mouse input, monitor info, foreground window bounds, and keyboard layout independence.
globs:
  - src/Klikety/Services/**
  - src/Klikety/Interop/**
  - src/Klikety/Input/**
---

# Win32 Interop

All Win32 interaction is behind interfaces (`IHotKeyService`, `IKeyboardHookService`, `IMouseActionService`, `IForegroundWindowProvider`). Real implementations are thin P/Invoke wrappers. Fakes are injected in tests.

## Interfaces

```csharp
interface IHotKeyService   { event EventHandler Activated; bool Register(HotKeyConfig); void Unregister(); }
interface IKeyboardHookService { event EventHandler<VKey> KeyPressed; bool Enable(); void Disable(); }
interface IMouseActionService  { void MoveTo(Point physicalPoint); void SendAction(Point physicalPoint, MouseAction action, ActionModifiers modifiers = ActionModifiers.None); void SendScroll(int wheelDelta, ActionModifiers modifiers = ActionModifiers.None); void SendDrag(Point start, Point end, MouseAction button, ActionModifiers modifiers = ActionModifiers.None); }
interface IModifierDetector    { ActionModifiers GetCurrentModifiers(); }
interface IScrollHotKeyService { List<string> Register(); void Unregister(); bool IsRegistered; }
interface IScreenBoundsProvider { Rectangle GetPrimaryScreenBounds(); double GetDpiScale(); }
interface IForegroundWindowProvider { nint GetForegroundWindowHandle(); Rectangle GetWindowBounds(nint hwnd); string GetWindowTitle(nint hwnd); }
interface IDisplayCatalog { DisplayCatalogResult GetSnapshot(); }
```

`IDisplayCatalog` is also on `IPlatformServices`. Identity key is CCD `monitorDevicePath`. Empty/duplicate DevicePath or CCD-active-count ≠ `EnumDisplayMonitors` count → hard failure, no partial map.

## `IKeyboardHookService` — `SetWindowsHookEx(WH_KEYBOARD_LL)`

- Hook installed only while overlay is visible; uninstalled in `DeactivateOverlay()`.
- Hook callback reads `VKey` + state from `KBDLLHOOKSTRUCT`, calls `CallNextHookEx` immediately, then posts `VKey` to UI thread via `Dispatcher.InvokeAsync` — no blocking work in callback (OS kills hook after ~300 ms).
- `KeyEvent` event raised on UI thread only.
- If `SetWindowsHookEx` returns null, `Enable()` returns `false`; overlay closed + tray notification.
- `Disable()`: only nulls `_hookProc` (allowing GC) if `UnhookWindowsHookEx` returns success. Prevents crash from collected callback if unhook fails.
- Constructor accepts optional `ILogger? logger = null`. Logs at Debug level: "Keyboard hook enabled", "Keyboard hook disabled", "Keyboard hook enable failed". Uses `[LoggerMessage]` source generator. Does not log captured key values.
- Supports multiple instances per process (e.g. overlay hook + key press display hook). Each instance owns its own `WH_KEYBOARD_LL` hook and `_generation` counter.

## `IHotKeyService` — `RegisterHotKey`

- Registered via Win32 `RegisterHotKey` with dispatcher message loop handling `WM_HOTKEY`.
- Registration attempted at startup; failure (conflict) stored as validation error, surfaced as tray notification.
- `StartupValidator` probes registration and immediately unregisters to detect conflicts before full startup.

## `IMouseActionService` — `SendInput`

- `MoveTo`: normalizes physical-pixel coords to 0–65535 against the virtual desktop (`SM_*VIRTUALSCREEN`), then sends `MOUSEINPUT` with `MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK`. Guards against zero-dimension screens with `Math.Max(bounds.Width - 1, 1)` divisor. `NormalizeAbsolute(point, virtualScreen)` is unit-tested with a negative origin.
- `SendAction`: calls `MoveTo` first, then sends appropriate `MOUSEEVENTF_*DOWN/UP` pairs. `MoveOnly` action returns after `MoveTo` — no click inputs sent. Double-click = two left-click pairs in sequence. When `modifiers != None`, wraps all click pairs in `KEYDOWN`/`KEYUP` for Shift/Ctrl/Alt via a single `SendInput` call.
- `INPUT` struct uses nested union pattern (`INPUT` → `INPUT_UNION`) for correct x64 alignment. The runtime handles padding between `type` and the union.
- Partial `SendInput` sends trigger compensating `KEYUP` events to prevent stuck modifiers.
- `SendDrag`: single `SendInput` call with move-to-start + button-down + move-to-end + button-up, plus modifier KEYDOWN/KEYUP bracket. Button mapping: `LeftClick`/`DoubleClick` → left, `RightClick` → right, `MiddleClick` → middle. `MoveOnly`/`DragDrop` defensively rejected (return without action).
- All geometry in physical pixels; DIP→physical conversion happens at WPF rendering boundary only, via `PresentationSource.CompositionTarget.TransformToDevice`.
- `SendScroll`: sends `MOUSEEVENTF_WHEEL` at current cursor position. `mouseData` = `WHEEL_DELTA (120) × scrollAmount`. Positive = up, negative = down. No cursor move. When `modifiers != None`, wraps wheel event in `KEYDOWN`/`KEYUP` bracket via single `SendInput` call with partial-send compensation.

## `IScrollHotKeyService` — `RegisterHotKey`

- Separate from `IHotKeyService`; owns its own `HwndSource` and hotkey IDs (`0x2000`, `0x2001`).
- Registers two hotkeys: scroll up (default Ctrl+Alt+PageUp) and scroll down (default Ctrl+Alt+PageDown).
- On `WM_HOTKEY`: dispatches to `IMouseActionService.SendScroll(+delta)` or `SendScroll(-delta)` based on hotkey ID.
- `Register()` returns list of failure descriptions (for tray notification). `Unregister()` / `Dispose()` clean up.
- Disabled by default in config (`scrollHotkeys.enabled: false`). Tray menu "Pause/Resume Scroll Keys" toggle calls `Unregister()`/`Register()` without config change.
- Config validation: `scrollAmount` ∈ [1, 100]; scroll keys checked against reserved/action/chord/navigation keys; duplicate up/down rejection.

## `IModifierDetector` — `GetAsyncKeyState`

- `ActionModifiers` is a `[Flags]` enum: `None = 0, Shift = 1, Ctrl = 2, Alt = 4`. Separate from `HotKeyModifiers`.
- `ModifierDetector.GetCurrentModifiers()` reads physical key state via `GetAsyncKeyState(VK_SHIFT/VK_CONTROL/VK_MENU)` at action dispatch time.
- Called in `NavigatorCoordinator.OnSessionActionRequested` — modifier detection is a coordinator concern, not a session concern.
- `MoveOnly` action always passes `ActionModifiers.None` (modifiers irrelevant for cursor-only move).
- `GetAsyncKeyState` returns 0 during secure desktop (UAC, lock screen) — harmless fallback to no modifiers.

## `NativeMethods.GetPrimaryScreenBounds()` — `GetMonitorInfoW`

- No WinForms dependency; no `Screen.PrimaryScreen`.
- **Important**: `LibraryImport` (source-generated) does NOT auto-resolve `W` suffix like `DllImport`. Must use `EntryPoint = "GetMonitorInfoW"` explicitly.
- Checks `GetMonitorInfo` return value; falls back to 1920×1080 at (0,0) if the call fails.
- Implementation:
  ```csharp
  var hMon = MonitorFromPoint(new POINT(0, 0), MONITOR_DEFAULTTOPRIMARY);
  var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
  if (!GetMonitorInfo(hMon, ref info))
      return new Rectangle(0, 0, 1920, 1080); // safe fallback
  // info.rcMonitor = physical-pixel bounds of primary monitor
  ```
- Returns `System.Drawing.Rectangle` (physical pixels). Used by `GridCalculator`, `SubgridCalculator`, `MouseActionService`, and `OverlayWindow` sizing.

## `NativeMethods.GetForegroundMonitorWorkArea()` — `MonitorFromWindow`

- `GetForegroundWindow()` → `MonitorFromWindow(hwnd, MONITOR_DEFAULTTOPRIMARY)` → `GetMonitorInfo` → `rcWork`.
- Returns `System.Drawing.Rectangle` (physical pixels). Consumer (`MonitorService`) converts to DIPs via `PresentationSource.CompositionTarget.TransformFromDevice`.
- Fallback: primary monitor when `GetForegroundWindow()` returns zero.
- Used by `KeyPressDisplayManager` for HUD positioning on active monitor.

## `IForegroundWindowProvider` — `DwmGetWindowAttribute` + `IsIconic`

- Two-method API: `GetForegroundWindowHandle()` returns the HWND of the foreground window; `GetWindowBounds(nint hwnd)` returns the window's physical-pixel bounds.
- `GetWindowBounds` uses `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` for accurate bounds (excludes invisible DWM borders). `Marshal.SizeOf<RECT>()` cached in a static field.
- `IsIconic(hwnd)` check: minimized windows return `Rectangle.Empty` — callers must validate.
- Bounds validation: zero or negative width/height → `Rectangle.Empty`.
- Pre-capture pattern: `NavigatorCoordinator` captures `_preOverlayHwnd` via `GetForegroundWindowHandle()` before showing the overlay (in `OnHotKeyActivated`). The app-scope chord later uses this saved handle to get the target window's bounds — ensuring the overlay's own HWND isn't captured.
- Real implementation: `Win32ForegroundWindowProvider` wraps `NativeMethods`. Fake: `FakeForegroundWindowProvider` with configurable `Handle` and `Bounds` properties.

- Two-method API: `GetForegroundWindowHandle()` returns the current foreground window HWND; `GetWindowBounds(nint hwnd)` returns physical-pixel bounds for a given HWND.
- Bounds acquired via `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` — returns the visible window rect excluding invisible Win10+ shadow/border.
- Minimized detection: `IsIconic(hwnd)` called before DWM query. DWM may return stale restored geometry for minimized windows.
- Pre-capture pattern: HWND captured in `OnHotKeyActivated` before `Show()` to avoid self-detection (overlay becomes foreground after `Show()`). Bounds retrieved for stored HWND at chord-press time.
- Failure → `Rectangle.Empty`: null/zero HWND, minimized, DWM failure, or zero-area bounds.
- Production: `Win32ForegroundWindowProvider` wraps `NativeMethods`. Fake: `FakeForegroundWindowProvider` with configurable `Handle` and `Bounds`.
- Part of `IPlatformServices`; injected via DI.
- `Marshal.SizeOf<RECT>()` cached in a static `RectSize` field to avoid per-call reflection.

## `NativeMethods.GetWindowTitle(nint)` — `GetWindowTextW` + `GetWindowTextLengthW`

- Zero/invalid HWND → `string.Empty`.
- `GetWindowTextLengthW` returns 0 for windows with no title text → `string.Empty`.
- Allocates `char[length + 1]` buffer, calls `GetWindowTextW`, constructs string from copied count, trims whitespace.
- `DllImport` (not `LibraryImport`) for `GetWindowTextW` because `char[]` buffer requires `CharSet.Unicode` marshalling.
- Used by `IForegroundWindowProvider.GetWindowTitle(nint)` for window-relative macro recording (captures target window title).
- Fake: `FakeForegroundWindowProvider.Title` property — returns configured title for matching handle, empty for zero/mismatched handle.

## `NativeMethods.GetPrimaryMonitorDpiScale()` — `GetDpiForMonitor`

- P/Invoke to `shcore.dll!GetDpiForMonitor` with `MDT_EFFECTIVE_DPI`.
- Returns `dpiX / 96.0` for the primary monitor. Fallback: `1.0` if call fails.
- Exposed via `IScreenBoundsProvider.GetDpiScale()`. Used by macro recording to tag captures with display scale.
- `GetMonitorDpiScale(hMonitor)` is the same call for an arbitrary `HMONITOR`.

## `IDisplayCatalog` / `DisplayCatalog`

- `EnumDisplayMonitors` + `GetMonitorInfoW` (`MONITORINFOEX.szDevice`, `rcMonitor`) + `GetDpiForMonitor`.
- CCD: `GetDisplayConfigBufferSizes` / `QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS)` / `DisplayConfigGetDeviceInfo` for source GDI name and `monitorDevicePath`.
- Match GDI name (`szDevice` ↔ `viewGdiDeviceName`). Do not pair leftovers.
- Virtual-screen rect: `GetSystemMetrics(SM_XVIRTUALSCREEN/Y/CX/CY)`.
- `DisplaySnapshot.FindContaining(point)` uses physical `rcMonitor.Contains`.
- `DllImport` (not `LibraryImport`) for `EnumDisplayMonitors`, `MONITORINFOEX`, and CCD structs — callbacks and `ByValTStr` are not source-generated.

## `DisplayNumbering` / `DisplayTopologyStore`

- Fingerprint = ordinal-sorted unique CCD DevicePaths. Rects and which display hosts navigation are not part of it.
- Unknown fingerprint: order by `(Left, Top, DevicePath)` ordinal, assign `1..N`, cap at 9 (remainder unnumbered). Persist `%APPDATA%\Klikety\display-topologies.json` (not `config.json`).
- Known fingerprint: reuse stored DevicePath → number even if rects moved. Do not rewrite the file.
- Partial unplug is a new fingerprint (spatial 1..N, no reserved holes).
- Tests inject the store path. Matcher/numbering tests do not call Win32.

## `NativeMethods.SetClickThroughExStyle(hwnd)`

- Applies `WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` via `GetWindowLongPtrW`/`SetWindowLongPtrW`.
- `LibraryImport` with explicit `EntryPoint` (W-suffix required, same as `GetMonitorInfoW`).
- Used by `KeyPressWindow.OnSourceInitialized` to make the HUD click-through and non-activating.

## Keyboard layout independence

- All key handling uses VKey codes (physical-position stable across layouts).
- `IKeyboardLayoutProvider.GetActiveKeyboardLayout()` queries the foreground window's thread. Do not use `GetKeyboardLayout(0)`, which only reports the calling thread's layout.
- `OverlayWindow` attaches a `WM_INPUTLANGCHANGE` hook after its `HwndSource` is available and removes it in `Hide()`. Its event only signals a changed message HKL; `NavigatorCoordinator` reads the authoritative provider HKL, creates a new immutable `Win32KeyLabelResolver`, rebuilds all renderer labels, then redraws the active session.
- `NavigatorCoordinator` also compares HKL on each non-debounced overlay key-down. This recovers if a topmost/non-activating overlay does not receive `WM_INPUTLANGCHANGE`; it performs no timer polling.
- `LabelGenerator` and `AxisLabelGenerator` retain their VKey arrays and rebuild resolved labels and reverse lookups in place. Rebuild runs on the UI dispatcher with WPF rendering, so generators have no concurrent access.
- `LabelGenerator` derives display characters via `ToUnicode` / `MapVirtualKey` against the active HKL so on-screen labels reflect the user's keyboard layout.
- Fallback: if `ToUnicode` returns no character (dead key, unmapped), VKey name string is used as the label.
