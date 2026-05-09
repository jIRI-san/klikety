---
description: Key press visualization HUD — hook lifecycle, display manager, rendering, config, privacy, monitor tracking, activation transaction.
globs:
  - src/Klikety/Overlay/KeyPressWindow.xaml
  - src/Klikety/Overlay/KeyPressWindow.xaml.cs
  - src/Klikety/Services/KeyPressProcessor.cs
  - src/Klikety/Services/KeyPressDisplayManager.cs
  - src/Klikety/Services/IMonitorService.cs
  - src/Klikety/Services/MonitorService.cs
---

# Key Press Visualization

Runtime-only floating HUD that shows recent key presses with outlined text. Always off on startup; toggled via tray menu "Show Key Presses".

## Architecture

```
KeyboardHookService (2nd instance)
    │ KeyEvent
    ▼
KeyPressDisplayManager
    ├── KeyPressProcessor (modifier tracking, label cache, repeat detection)
    ├── KeyPressWindow (click-through WPF window, outlined text rendering)
    └── MonitorService (active monitor work area, DIP transform)
```

No DI container. All components created/destroyed in App.xaml.cs activation transaction.

## Hook Lifecycle

- Second `KeyboardHookService` instance (`_keyPressHook`), independent from the overlay hook.
- Same proven pattern: `SetWindowsHookEx(WH_KEYBOARD_LL)`, callback posts to UI thread via `Dispatcher.InvokeAsync`, `_generation` counter discards stale events.
- Both hooks call `CallNextHookEx`; Windows supports multiple hooks per process.
- Hook registered only while feature is enabled; always unregistered on disable or app quit.

## KeyPressProcessor

- Accepts `IKeyLabelResolver`, `IKeyStateProvider`, `TimeProvider` — all injectable for testing.
- **Label cache**: `Dictionary<VKey, string>` built at construction via `IKeyLabelResolver`. Avoids dead-key state corruption during live typing. Special keys (Enter, Tab, arrows, F1–F12, etc.) use a static lookup dictionary.
- **Modifier tracking**: Tracks Ctrl/Shift/Alt/Win state via key-down/key-up. On each non-modifier key-down, reconciles via `GetAsyncKeyState` to prevent stale "stuck" modifiers.
- **Repeat detection**: `IsRepeat(current, last, windowMs)` compares label and timestamp. Default window: 400ms.
- Modifier-only presses (Ctrl, Shift, Alt, Win alone) produce no entry.
- `ResetModifierState()` called on enable/disable transitions.

## KeyPressDisplayManager

- Manages `ObservableCollection<KeyPressDisplayItem>` on `KeyPressWindow.Items`.
- **Overflow eviction**: When new key exceeds `maxVisibleKeys`, oldest item removed immediately (no fade).
- **Repeat collapsing**: Consecutive identical keys within `repeatWindowMs` increment `RepeatCount` on last item instead of adding new entry.
- **Fade lifecycle**:
  - `_idleTimer` (DispatcherTimer, interval = `fadeTimeoutMs`): reset on each key press; fires to start fade.
  - `_fadeTimer` (DispatcherTimer, 16ms ≈ 60fps): decrements `Opacity` on each item per tick.
  - **Stagger**: Oldest items start fading first. Item `i` begins fading after `i × staggerTicks` ticks.
  - Items removed when `Opacity ≤ 0`.
  - `CancelFade()` on new key press: stops timer, resets all opacities to 1.0.
- **Positioning**: `CalculatePosition(corner, workArea, windowSize, margin)` — pure static method, tested for all 4 corners. Called on each key press via `MonitorService.GetActiveMonitorWorkArea()`.

## KeyPressWindow

- WPF `Window`: `WindowStyle=None`, `AllowsTransparency=True`, `Topmost=True`, `ShowActivated=False`, `Focusable=False`.
- Extended styles via `NativeMethods.SetClickThroughExStyle(hwnd)`: `WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`.
- **Rendering**: Path-based outlined text via `FormattedText.BuildGeometry()` + two-layer `Path` (stroke + fill). Same technique as `GridRenderer.AddOutlinedText`.
- `RebuildVisuals()` deferred until `OnSourceInitialized` (guards against `GetDpi` before presentation source exists).
- `PropertyChanged` handlers tracked per item in `Dictionary<KeyPressDisplayItem, PropertyChangedEventHandler>` and unsubscribed in `RebuildVisuals()` before clearing.
- `ItemsHost.Items` managed directly (no `ItemsSource` binding — avoids `InvalidOperationException` from mixing `ItemsSource` with direct `Items` manipulation).

## Monitor Tracking

- `MonitorService` wraps `NativeMethods.GetForegroundMonitorWorkArea()` and converts physical pixels → DIPs via `PresentationSource.CompositionTarget.TransformFromDevice`.
- `GetForegroundWindow()` → `MonitorFromWindow(hwnd, MONITOR_DEFAULTTOPRIMARY)` → `GetMonitorInfo` → `rcWork`.
- Fallback: primary monitor when `GetForegroundWindow()` returns zero (secure desktop, no foreground window).
- 96-DPI fallback when `PresentationSource` not yet available.

## Config Section

`keyPressVisualization` on `ConfigModel` — visual settings only, no enable/disable property.

```csharp
public sealed class KeyPressVisualizationConfig {
    public double FontSize { get; init; } = 72.0;
    public string FontColor { get; init; } = "#FFCC00";
    public string OutlineColor { get; init; } = "#000000";
    public double OutlineThickness { get; init; } = 2.0;
    public string Corner { get; init; } = "BottomRight";
    public int FadeTimeoutMs { get; init; } = 3000;
    public int FadeDurationMs { get; init; } = 500;
    public int MaxVisibleKeys { get; init; } = 3;
    public double Margin { get; init; } = 20.0;
    public int RepeatWindowMs { get; init; } = 400;
}
```

Validation in `ConfigLoader.ValidateKeyPressVisualization`: FontSize > 0, OutlineThickness ≥ 0, Margin ≥ 0, hex color format, FadeTimeoutMs ≥ 0, FadeDurationMs > 0, MaxVisibleKeys ∈ [1, 10], Corner enum, RepeatWindowMs ∈ [50, 1000].

## Activation Transaction (App.xaml.cs)

On enable (tray menu click):
1. Create `KeyboardHookService` → `KeyPressProcessor` → `KeyPressWindow` → `MonitorService` → `KeyPressDisplayManager`.
2. `hook.Enable()` — if fails: dispose all, show tray notification, leave unchecked.
3. Subscribe `hook.KeyEvent` → `displayManager.HandleKeyEvent`.
4. Show window, set menu checked, update tooltip to "Klikety (Key Display Active)".

On disable:
1. `hook.Disable()`, `processor.ResetModifierState()`, `displayManager.Dispose()`, `window.Close()`.
2. Null all references, uncheck menu, restore tooltip.

On quit: disable path runs if feature is active.

## Privacy

- Key labels never persisted to disk or included in logs at any level.
- All in-memory entries cleared on disable.
- `KeyPressEntry.ToString()` returns `"[KeyPressEntry]"` — prevents accidental logging.
- Password field detection out of scope (documented limitation).
- Secure desktop events not captured (Win32 limitation).
