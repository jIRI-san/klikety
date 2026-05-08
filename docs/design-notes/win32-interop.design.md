---
description: Win32 P/Invoke patterns — service interfaces, keyboard hook, hotkey registration, mouse input, monitor info, and keyboard layout independence.
globs:
  - src/Klikety/Services/**
  - src/Klikety/Interop/**
  - src/Klikety/Input/**
---

# Win32 Interop

All Win32 interaction is behind interfaces (`IHotKeyService`, `IKeyboardHookService`, `IMouseActionService`). Real implementations are thin P/Invoke wrappers. Fakes are injected in tests.

## Interfaces

```csharp
interface IHotKeyService   { event EventHandler Activated; bool Register(HotKeyConfig); void Unregister(); }
interface IKeyboardHookService { event EventHandler<VKey> KeyPressed; bool Enable(); void Disable(); }
interface IMouseActionService  { void MoveTo(Point physicalPoint); void SendAction(Point physicalPoint, MouseAction action, ActionModifiers modifiers = ActionModifiers.None); void SendScroll(int wheelDelta); }
interface IModifierDetector    { ActionModifiers GetCurrentModifiers(); }
interface IScrollHotKeyService { List<string> Register(); void Unregister(); bool IsRegistered; }
```

## `IKeyboardHookService` — `SetWindowsHookEx(WH_KEYBOARD_LL)`

- Hook installed only while overlay is visible; uninstalled in `DeactivateOverlay()`.
- Hook callback reads `VKey` + state from `KBDLLHOOKSTRUCT`, calls `CallNextHookEx` immediately, then posts `VKey` to UI thread via `Dispatcher.InvokeAsync` — no blocking work in callback (OS kills hook after ~300 ms).
- `KeyEvent` event raised on UI thread only.
- If `SetWindowsHookEx` returns null, `Enable()` returns `false`; overlay closed + tray notification.
- `Disable()`: only nulls `_hookProc` (allowing GC) if `UnhookWindowsHookEx` returns success. Prevents crash from collected callback if unhook fails.

## `IHotKeyService` — `RegisterHotKey`

- Registered via Win32 `RegisterHotKey` with dispatcher message loop handling `WM_HOTKEY`.
- Registration attempted at startup; failure (conflict) stored as validation error, surfaced as tray notification.
- `StartupValidator` probes registration and immediately unregisters to detect conflicts before full startup.

## `IMouseActionService` — `SendInput`

- `MoveTo`: normalizes physical-pixel coords to 0–65535 range using primary screen bounds, then sends `MOUSEINPUT` with `MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE`. Guards against zero-dimension screens with `Math.Max(bounds.Width - 1, 1)` divisor.
- `SendAction`: calls `MoveTo` first, then sends appropriate `MOUSEEVENTF_*DOWN/UP` pairs. `MoveOnly` action returns after `MoveTo` — no click inputs sent. Double-click = two left-click pairs in sequence. When `modifiers != None`, wraps all click pairs in `KEYDOWN`/`KEYUP` for Shift/Ctrl/Alt via a single `SendInput` call.
- `INPUT` struct uses nested union pattern (`INPUT` → `INPUT_UNION`) for correct x64 alignment. The runtime handles padding between `type` and the union.
- Partial `SendInput` sends trigger compensating `KEYUP` events to prevent stuck modifiers.
- All geometry in physical pixels; DIP→physical conversion happens at WPF rendering boundary only, via `PresentationSource.CompositionTarget.TransformToDevice`.
- `SendScroll`: sends `MOUSEEVENTF_WHEEL` at current cursor position. `mouseData` = `WHEEL_DELTA (120) × scrollAmount`. Positive = up, negative = down. No cursor move.

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

## Keyboard layout independence

- All key handling uses VKey codes (physical-position stable across layouts).
- `LabelGenerator` derives display characters via `ToUnicode` / `MapVirtualKey` against the active HKL so on-screen labels reflect the user's keyboard layout.
- Fallback: if `ToUnicode` returns no character (dead key, unmapped), VKey name string is used as the label.
