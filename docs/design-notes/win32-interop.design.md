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
interface IMouseActionService  { void MoveTo(Point physicalPoint); void SendAction(Point physicalPoint, MouseAction action); }
```

## `IKeyboardHookService` — `SetWindowsHookEx(WH_KEYBOARD_LL)`

- Hook installed only while overlay is visible; uninstalled in `DeactivateOverlay()`.
- Hook callback reads `VKey` + state from `KBDLLHOOKSTRUCT`, calls `CallNextHookEx` immediately, then posts `VKey` to UI thread via `Dispatcher.InvokeAsync` — no blocking work in callback (OS kills hook after ~300 ms).
- `KeyPressed` event raised on UI thread only.
- If `SetWindowsHookEx` returns null, `Enable()` returns `false`; overlay closed + tray notification.

## `IHotKeyService` — `RegisterHotKey`

- Registered via Win32 `RegisterHotKey` with dispatcher message loop handling `WM_HOTKEY`.
- Registration attempted at startup; failure (conflict) stored as validation error, surfaced as tray notification.
- `StartupValidator` probes registration and immediately unregisters to detect conflicts before full startup.

## `IMouseActionService` — `SendInput`

- `MoveTo`: normalizes physical-pixel coords to 0–65535 range using primary screen bounds, then sends `MOUSEINPUT` with `MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE`.
- `SendAction`: sends appropriate `MOUSEEVENTF_*DOWN/UP` pairs. Double-click = two left-click pairs in sequence.
- All geometry in physical pixels; DIP→physical conversion happens at WPF rendering boundary only, via `PresentationSource.CompositionTarget.TransformToDevice`.

## `NativeMethods.GetPrimaryScreenBounds()` — `GetMonitorInfoW`

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

## Keyboard layout independence

- All key handling uses VKey codes (physical-position stable across layouts).
- `LabelGenerator` derives display characters via `ToUnicode` / `MapVirtualKey` against the active HKL so on-screen labels reflect the user's keyboard layout.
- Fallback: if `ToUnicode` returns no character (dead key, unmapped), VKey name string is used as the label.
