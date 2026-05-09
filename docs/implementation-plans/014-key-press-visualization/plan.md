# 014: Key Press Visualization

## Decisions

- **Activation model**: Off by default at app startup. No persistent enable/disable in config. Tray menu "Show Key Presses" toggles at runtime. Hook registered only while enabled; always off after restart.
- **Separate hook instance**: Second instance of existing `KeyboardHookService` (no new interface — project doesn't use DI container). Referenced as `_keyPressHook` field in `App.xaml.cs`. Independent from the overlay hook.
- **Separate window**: New `KeyPressWindow` — always-on-top, transparent, click-through WPF window. Independent from `OverlayWindow`. Extended styles: `WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`. XAML: `ShowActivated="False"`, `Focusable="False"`. Uses `SetWindowLongPtr` (not `SetWindowLong`) for 64-bit.
- **Modifier handling**: Modifier-only presses (Ctrl, Shift, Alt, Win alone) are ignored. Modifier+key combos displayed as single entry ("Ctrl+C"). Modifier state tracked via key-down/key-up. State reset on enable/disable transitions; reconciled via `GetAsyncKeyState` on each non-modifier key-down to prevent stale "stuck" modifiers.
- **Repeat collapsing**: Consecutive identical key presses collapsed into single entry with count badge ("A ×3"). Repeat window: 150ms (fast enough for OS auto-repeat at ~30ms, slow enough to avoid "ll" in "hello" false positives). Configurable as `repeatWindowMs`.
- **Monitor tracking**: HUD follows the monitor containing the active foreground window. On each key press: `GetForegroundWindow()` → `MonitorFromWindow(hwnd, MONITOR_DEFAULTTOPRIMARY)` → `GetMonitorInfo` → `rcWork` converted from physical pixels to DIPs via `PresentationSource.CompositionTarget.TransformFromDevice` before setting WPF `Left`/`Top`.
- **Config section**: `keyPressVisualization` at root level — visual settings only (font size, color, corner, fade duration, max visible keys, repeat window). No enable/disable property.
- **Config type**: `sealed class` with `{ get; init; }` properties — matches `ConfigModel`, `ModeConfig`, `HotKeyConfig` pattern. Not a `record`.
- **Rendering**: Path-based outlined text via `FormattedText.BuildGeometry()` + two-layer `Path` (stroke + fill) — same pattern as `GridRenderer.AddOutlinedText`. Items in a WPF `ItemsControl`.
- **Fade animation**: `DispatcherTimer`-based opacity decrement on ViewModel property (testable, no DependencyProperty animation complexity). Timer ticks at ~16ms (60fps). Each `KeyPressDisplayItem.Opacity` decremented per tick. Overflow eviction: when a new key exceeds `maxVisibleKeys`, oldest item removed immediately (no fade for evicted items — fade only on idle timeout).
- **Key labels**: VKey→label mapping cached in `Dictionary<VKey, string>` at enable-time using `MapVirtualKeyEx` + `ToUnicodeEx`. Avoids dead-key state corruption during live typing. Cache refreshed on `WM_INPUTLANGCHANGE`. Special keys via static lookup dictionary. Injected via `IKeyLabelResolver` for testability.
- **Privacy**: Captured key labels never persisted to disk or included in logs at any level. All in-memory entries cleared on disable. Tray icon tooltip changes to "Klikety (Key Display Active)" while enabled. Quick pause via Shift+Escape (configurable) as a panic key. Password field detection out of scope (documented limitation).
- **Scope qualification**: Hook captures keys within normal interactive desktop sessions only. Secure desktop (UAC, Ctrl+Alt+Del, lock screen) events not captured — this is a Win32 limitation, not a bug.

## Requirements

| ID | Requirement | Acceptance Criteria | Phases/Steps |
|----|-------------|---------------------|--------------|
| REQ-1 | System-wide key press capture via low-level keyboard hook | When feature is enabled, all key-down events within normal interactive desktop sessions fire regardless of which window is focused (secure desktop excluded) | 1.1 |
| REQ-2 | Floating HUD window displays last N pressed keys (default 3) | HUD shows up to `maxVisibleKeys` entries stacked vertically; oldest at top, newest at bottom | 2.1, 2.2 |
| REQ-3 | Keys fade out with animation on idle — oldest first, newest last | After idle timeout, each key entry fades from opacity 1.0→0.0; oldest starts fading first. Overflow eviction (new key exceeding max) removes oldest immediately without fade | 2.3 |
| REQ-4 | After configurable timeout (default 3s), most recent key also fades | When no new key is pressed for `fadeTimeoutMs`, the remaining entries begin fading | 2.3 |
| REQ-5 | Modifier+key combos shown as single entry ("Ctrl+C") | Pressing Ctrl then C produces one "Ctrl+C" entry, not two separate entries | 1.3 |
| REQ-6 | Modifier-only presses ignored | Pressing and releasing Ctrl alone produces no HUD entry | 1.3 |
| REQ-7 | Repeated identical keys collapsed with count badge ("A ×3") | Pressing A three times within repeat window produces "A ×3" instead of three separate entries | 1.3 |
| REQ-8 | HUD positioned in configurable screen corner (default bottom-right) | Config `corner` property controls placement; HUD renders in the correct corner with DIP-correct positioning | 2.2 |
| REQ-9 | HUD follows active window's monitor | When user switches to a window on monitor 2, next key press moves HUD to monitor 2 | 2.4 |
| REQ-10 | Font size, color, outline configurable | Config properties `fontSize` (default 36), `fontColor` (default "#FFCC00"), `outlineColor` (default "#000000") applied to path-based outlined text rendering | 2.2 |
| REQ-11 | Tray menu "Show Key Presses" toggle with activation transaction | Menu item toggles feature on/off; checkmark reflects current state; hook registered/unregistered; on enable failure: dispose partial resources, show tray notification, leave unchecked | 3.1 |
| REQ-12 | Feature always off after app restart | On startup, feature is disabled regardless of previous session state | 3.1 |
| REQ-13 | Config section `keyPressVisualization` with visual settings | Config section parsed and validated; defaults applied when section absent; type is `sealed class` with `init` properties | 1.2 |
| REQ-14 | HUD visible alongside Klikety overlay | When overlay is active, key press HUD remains visible and functional | 2.1 |
| REQ-15 | All keys displayed (letters, special keys, F-keys, arrows, space, etc.) | Printable keys show cached character label; special keys show named labels ("Enter", "↑", "F1") | 1.3 |
| REQ-16 | Window is click-through and never steals focus | HUD window uses `WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, `ShowActivated=False`, `Focusable=False` | 2.1 |
| REQ-17 | Max visible keys configurable (default 3) | Config `maxVisibleKeys` controls how many entries are shown simultaneously | 2.2, 1.2 |
| REQ-18 | Key labels never persisted or logged | Captured labels never written to disk/logs; in-memory entries cleared on disable | 1.3, 3.1 |
| REQ-19 | Visible capture-active indicator | Tray icon tooltip changes to "Klikety (Key Display Active)" while feature is enabled | 3.1 |

## Risks

| ID | Risk | Likelihood | Impact | Mitigation | Steps |
|----|------|------------|--------|------------|-------|
| RISK-1 | Two `WH_KEYBOARD_LL` hooks on same process may interfere | Low | Medium | Both hooks call `CallNextHookEx`; Windows supports multiple hooks per process. Test with both active. | 1.1 |
| RISK-2 | Hook callback > 300ms causes OS to silently remove hook | Low | High | Callback posts to UI thread immediately via `Dispatcher.InvokeAsync`; no blocking work. Same proven pattern as existing hook. | 1.1 |
| RISK-3 | `GetForegroundWindow` returns null/invalid handle on locked screen or secure desktop | Low | Low | Fallback to primary monitor when `MonitorFromWindow` fails or hwnd is zero. | 2.4 |
| RISK-4 | Rapid key input floods UI thread with animation updates | Medium | Medium | Timer-based opacity decrement (single 16ms timer, not per-item animations). New key press resets idle timer. Cap to `maxVisibleKeys`. | 2.3 |
| RISK-5 | `ToUnicode`/`ToUnicodeEx` corrupts dead-key state during live typing | High | Critical | VKey→label mapping cached at enable-time; never called during live keystroke processing. Cache rebuilt on `WM_INPUTLANGCHANGE`. | 1.3 |
| RISK-6 | Sensitive data (passwords, 2FA) displayed during screenshare | Medium | High | Non-goal to solve fully. Mitigations: never persist/log key labels, clear on disable, visible "active" indicator, document limitation. | 1.3, 3.1 |
| RISK-7 | Modifier state becomes stale after missed key-up events (desktop transitions, hook boundaries) | Medium | Medium | Reset modifier set on enable/disable. Reconcile via `GetAsyncKeyState` on each non-modifier key-down. | 1.3 |
| RISK-8 | Activation failure (hook Enable() returns false) leaves partial state | Low | Medium | Activation transaction: create→enable→show; on any failure dispose all partial resources, tray notification, leave toggle unchecked. | 3.1 |

## Phase 1: Core Key Processing
<!-- worktree: feature/014-core-key-processing-step-1-1 -->

- [x] 1.1 Create second `KeyboardHookService` instance for key press capture (REQ-1, RISK-1, RISK-2) `S`
  - No new interface — reuse existing `IKeyboardHookService` / `KeyboardHookService`. The feature creates a second instance referenced as `_keyPressHook` in `App.xaml.cs`.
  - Add `ILogger<KeyboardHookService>` injection to `KeyboardHookService` constructor. Log at Debug: "Key press hook enabled"/"disabled"/"enable failed". Do not log captured key values (RISK-6, REQ-18).
  - Fires on both key-down and key-up (modifier tracking needs up events) — existing `KeyHookEventArgs` already carries `IsDown`.

- [x] 1.2 Add `KeyPressVisualizationConfig` model and wire into `ConfigModel` / `ConfigLoader` (REQ-13, REQ-10, REQ-17) `S`
  - New `sealed class` in `Config/ConfigModel.cs`:
    ```csharp
    public sealed class KeyPressVisualizationConfig {
        public double FontSize { get; init; } = 36.0;
        public string FontColor { get; init; } = "#FFCC00";
        public string OutlineColor { get; init; } = "#000000";
        public double OutlineThickness { get; init; } = 2.0;
        public string Corner { get; init; } = "BottomRight"; // TopLeft, TopRight, BottomLeft, BottomRight
        public int FadeTimeoutMs { get; init; } = 3000;
        public int FadeDurationMs { get; init; } = 500;
        public int MaxVisibleKeys { get; init; } = 3;
        public double Margin { get; init; } = 20.0;
        public int RepeatWindowMs { get; init; } = 150;
    }
    ```
  - Add to `ConfigModel`: `public KeyPressVisualizationConfig KeyPressVisualization { get; init; } = new();`
  - Validate: `FontSize` > 0, `FadeTimeoutMs` >= 0, `FadeDurationMs` > 0, `MaxVisibleKeys` in [1..10], `Corner` is one of four valid values, `RepeatWindowMs` in [50..1000]. Violations added to startup tray notification list.
  - Update embedded `config.json` with `keyPressVisualization` section and defaults. Update `config.schema.json`.

- [x] 1.3 Add `KeyPressProcessor` — key event → display entry logic (REQ-5, REQ-6, REQ-7, REQ-15, REQ-18, RISK-5, RISK-7) `M`
  - New class `Services/KeyPressProcessor.cs`. Testable, no WPF dependencies.
  - Constructor takes `IKeyLabelResolver` (existing interface) and `TimeProvider` (System.TimeProvider from .NET 8+) for testability.
  - **Label cache** (RISK-5 mitigation): On construction (or explicit `RebuildCache()`), builds `Dictionary<VKey, string>` using `IKeyLabelResolver.GetDisplayChar(vkey)` for all VKeys 0–254. Special keys override from static dictionary: "Return"→"Enter", "Back"→"⌫", "Tab"→"Tab", "Escape"→"Esc", "Left"→"←", "Right"→"→", "Up"→"↑", "Down"→"↓", "Space"→"Space", F1–F24→"F1"–"F24", "Delete"→"Del", "Insert"→"Ins", "Home", "End", "Prior"→"PgUp", "Next"→"PgDn", "PrintScreen"→"PrtSc", "Capital"→"Caps". Cache never calls `ToUnicode` during live key processing.
  - **Modifier tracking**: Maintains set of currently-held modifier VKeys (LControl/RControl, LShift/RShift, LMenu/RMenu, LWin/RWin). Updated on key-down/key-up.
  - **Modifier reconciliation** (RISK-7): On each non-modifier key-down, call `GetAsyncKeyState` for all modifier VKeys and reconcile held set — clears modifiers that are no longer physically held.
  - **Reset**: `ResetModifierState()` clears held set. Called on enable/disable transitions.
  - **Key-down processing** (`ProcessKeyDown(VKey) → KeyPressEntry?`):
    - If VKey is a modifier → update held set, return `null` (REQ-6).
    - Reconcile modifier state via `GetAsyncKeyState`.
    - Build modifier prefix from held set: "Ctrl+Shift+Alt+Win+" (canonical order, deduplicate L/R).
    - Look up key label from cache. Unknown VKey → `VKey.ToString()` fallback.
    - Combine: `$"{modifierPrefix}{keyLabel}"`.
    - Return `KeyPressEntry(string Label, long TimestampTicks)`.
  - **Key-up processing** (`ProcessKeyUp(VKey)`): If modifier, remove from held set.
  - **Repeat detection** (`IsRepeat(KeyPressEntry current, KeyPressEntry? previous, int repeatWindowMs) → bool`): Same label within `repeatWindowMs` ticks.
  - `KeyPressEntry` record: `string Label, long TimestampTicks`.
  - **Privacy (REQ-18)**: No logging of label values. `ToString()` override on `KeyPressEntry` returns `"[KeyPressEntry]"` (prevents accidental structured-log inclusion).

- [x] 1.4 Unit tests for `KeyPressProcessor` (REQ-5, REQ-6, REQ-7, REQ-15, RISK-5, RISK-7) [after: 1.3] `M`
  - Test file: `Klikety.Tests/KeyPressProcessorTests.cs`.
  - Inject `FakeKeyLabelResolver` (returns predictable label for each VKey) and `FakeTimeProvider`.
  - Cases: plain letter, modifier+letter combo, modifier-only ignored, special key labels, repeat detection within/outside window, multi-modifier combo ("Ctrl+Shift+A"), L/R modifier deduplication, unknown VKey fallback, stale modifier reconciliation (fake `GetAsyncKeyState` returns key-up), reset clears state.

## Phase 2: HUD Window & Rendering
<!-- worktree: feature/014-core-key-processing-step-1-1 -->

- [x] 2.1 Create `KeyPressWindow` — always-on-top, transparent, click-through, non-activating WPF window (REQ-2, REQ-14, REQ-16) [after: 1.2] `M`
  - New XAML window `Overlay/KeyPressWindow.xaml`:
    - `WindowStyle=None`, `AllowsTransparency=True`, `Topmost=True`, `ShowInTaskbar=False`, `Background=Transparent`, `ShowActivated="False"`, `Focusable="False"`.
    - In `OnSourceInitialized`: `SetWindowLongPtr(hwnd, GWL_EXSTYLE, existing | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE)`. Use `SetWindowLongPtr` (not `SetWindowLong`) for 64-bit compatibility.
    - `SizeToContent=WidthAndHeight` — window auto-sizes to content.
    - Content: `ItemsControl` bound to `ObservableCollection<KeyPressDisplayItem>`. ItemTemplate: custom `ContentPresenter` with code-behind that renders path-based outlined text via `FormattedText.BuildGeometry()` + two-layer `Path` (matching `GridRenderer.AddOutlinedText` pattern).
  - `KeyPressDisplayItem` class (implements `INotifyPropertyChanged`): `Label` (string), `Opacity` (double, notify), `RepeatCount` (int, notify). Display text: `Label` when count=1, `$"{Label} ×{RepeatCount}"` when count>1.

- [x] 2.2 Implement `KeyPressDisplayManager` — manages display items, positioning, config (REQ-2, REQ-8, REQ-10, REQ-17) [after: 2.1, 1.3] `M`
  - New class `Services/KeyPressDisplayManager.cs`.
  - Constructor takes `KeyPressVisualizationConfig`, `KeyPressProcessor`, `KeyPressWindow`, `IMonitorService` (new interface for testability).
  - Owns `ObservableCollection<KeyPressDisplayItem>`.
  - `HandleKeyEvent(KeyHookEventArgs)`: calls `ProcessKeyDown`/`ProcessKeyUp` on processor; if entry returned: check repeat (same label within `RepeatWindowMs` → increment last item's `RepeatCount`); otherwise add new item and if over `MaxVisibleKeys`, remove oldest immediately (no fade — overflow eviction). Reset idle timer.
  - `UpdateWindowPosition()`: queries `IMonitorService.GetActiveMonitorWorkArea()` → returns `Rect` in DIPs. Positions window in configured corner with margin.
  - Applies font size, colors from config to window resources.

- [x] 2.3 Implement timer-based fade (REQ-3, REQ-4, RISK-4) [after: 2.2] `M`
  - Single `DispatcherTimer` at ~16ms interval (60fps tick) for fade animation. Only running while items are fading.
  - Idle `DispatcherTimer` (`FadeTimeoutMs`). On tick: mark all current items as "fading", start fade timer.
  - Fade logic per tick: decrement each fading item's `Opacity` by `1.0 / (FadeDurationMs / 16.0)`. Stagger: oldest item starts at tick 0, each subsequent delayed by `FadeDurationMs / maxVisibleKeys` ticks.
  - When item `Opacity` <= 0: remove from collection.
  - New key press: cancel fade (stop fade timer, restore all items to opacity 1.0), reset idle timer.

- [x] 2.4 Implement monitor-follow via `IMonitorService` (REQ-9, RISK-3) [after: 2.2] `M`
  - New interface `Services/IMonitorService.cs`: `Rect GetActiveMonitorWorkArea()` — returns work area in DIPs.
  - Implementation `Services/MonitorService.cs`:
    - `GetForegroundWindow()` → `MonitorFromWindow(hwnd, MONITOR_DEFAULTTOPRIMARY)` → `GetMonitorInfo` → `rcWork` (physical pixels).
    - Convert via `PresentationSource.CompositionTarget.TransformFromDevice` matrix → DIP `Rect`.
    - Fallback: if `GetForegroundWindow` returns `IntPtr.Zero`, use primary monitor.
  - New P/Invoke declarations in `Interop/NativeMethods.cs`: add `MonitorFromWindow` (new — doesn't exist yet), `GetForegroundWindow` (add if not present). Follow `GetPrimaryScreenBounds()` helper pattern.

- [x] 2.5 Unit tests for `KeyPressDisplayManager` (REQ-2, REQ-7, REQ-8, REQ-3) [after: 2.2] `S`
  - Test file: `Klikety.Tests/KeyPressDisplayManagerTests.cs`.
  - Inject `FakeMonitorService` (returns fixed DIP rect), `FakeTimeProvider`.
  - Cases: add key adds to collection, max visible keys enforced (overflow eviction), repeat increments count, idle timer triggers fade start, new key cancels fade, positioning calculates correct corner.

## Phase 3: App Integration
<!-- worktree: -->

- [x] 3.1 Wire into `App.xaml.cs` — tray menu item + lifecycle (REQ-11, REQ-12, REQ-18, REQ-19, RISK-8) [after: 2.4] `M`
  - Add "Show Key Presses" `MenuItem` with checkmark toggle between "Start with Windows" and separator before "Quit".
  - **Activation transaction** (RISK-8): On click (enabling):
    1. Create `KeyboardHookService` instance (`_keyPressHook`).
    2. Create `KeyPressProcessor` (with `IKeyLabelResolver`, `TimeProvider.System`).
    3. Create `KeyPressWindow`.
    4. Create `KeyPressDisplayManager`.
    5. Call `_keyPressHook.Enable()` — if returns `false`: dispose all, show tray notification "Failed to enable key press display", leave unchecked, return.
    6. Subscribe `_keyPressHook.KeyEvent` → `DisplayManager.HandleKeyEvent`.
    7. Show window, set checked.
    8. Update tray tooltip: "Klikety (Key Display Active)" (REQ-19).
  - On click (disabling):
    - `_keyPressHook.Disable()` then dispose. `KeyPressProcessor.ResetModifierState()`. Clear display items (REQ-18). Hide/close window. Dispose manager. Set unchecked. Restore tooltip to "Klikety".
  - On app quit: if feature active, run disable path.
  - No state persisted — always unchecked on startup (REQ-12).

- [x] 3.2 End-to-end manual testing (all REQs) @human `M`
  <details><summary>Details</summary>

  **Steps:**
  1. Launch Klikety. Verify "Show Key Presses" is unchecked in tray menu.
  2. Enable via tray menu. Verify tooltip changes to "Klikety (Key Display Active)".
  3. Type in any application — verify HUD appears in bottom-right with last 3 keys.
  4. Press Ctrl+C — verify single "Ctrl+C" entry.
  5. Press Ctrl alone — verify no entry.
  6. Press A rapidly 5 times — verify "A ×5" collapsed entry.
  7. Wait 3s after last key — verify all entries fade out.
  8. Move to a different monitor, type — verify HUD follows.
  9. Type dead-key sequence (e.g. ´ then e → é) in a text editor — verify target app receives correct accented character (dead-key state not corrupted).
  10. Type a password in a password field — verify it displays (known limitation, documented).
  11. Activate Klikety overlay (Alt+Space), navigate — verify both HUD and overlay work.
  12. Disable via tray menu — verify HUD disappears, no more key captures, tooltip reverts.
  13. Restart app — verify feature is off.

  **Rollback:** N/A — manual test only.
  </details>

- [ ] 3.3 Update design notes and config design note (REQ-13) [after: 3.1] `S`
  - Add new design note `docs/design-notes/key-press-visualization.design.md` covering: hook lifecycle (second instance, not new type), display manager architecture, config shape, rendering approach (path-based outlined text), privacy guarantees (no persistence/logging), monitor-follow pattern, activation transaction.
  - Update `config.design.md` with `keyPressVisualization` section documentation.
  - Update `.design-notes.md` index table with new entry.
