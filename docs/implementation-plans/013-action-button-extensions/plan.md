# 013: Action Button Extensions

## Decisions

- `MouseAction` enum extended with `MoveOnly` (move cursor, no click) and `DragDrop` (two-point drag).
- Modifier detection via injectable `IModifierDetector` interface wrapping `GetAsyncKeyState` — follows the project's pattern of placing all Win32 calls behind testable interfaces. `FakeModifierDetector` for tests.
- `IMouseActionService.SendAction` gains `ActionModifiers modifiers` parameter. `SendAction` injects `KEYDOWN` before and `KEYUP` after the click via `SendInput` for each active modifier. `SendInput` return count validated; on partial sends, compensating `KEYUP` events issued for any modifiers sent down, and a warning is logged.
- `IMouseActionService` extended with `SendScroll(int wheelDelta)` and `SendDrag(Point start, Point end, MouseAction button, ActionModifiers modifiers)`.
- Scroll hotkeys: new `ScrollHotKeyService` using Win32 `RegisterHotKey` with unique IDs (separate from overlay activation hotkey). Default keys: Ctrl+Alt+PageUp / Ctrl+Alt+PageDown. Disabled by default in config.
- Scroll sends `MOUSEEVENTF_WHEEL` at current cursor position. `wheelDelta` = `WHEEL_DELTA` (120) × `scrollAmount` from config.
- Tray menu gets "Pause Scroll Keys" / "Resume Scroll Keys" toggle (visible only when scroll is enabled in config).
- Drag-and-drop two-point flow: first press of drag action key stores start point and resets overlay for second navigation; second action key press (any action) determines button (left/right) and executes drag with captured modifiers. Escape during second phase aborts and restores cursor to original origin.
- Status text "Select drag target" rendered by `IOverlayWindow.ShowStatusText(string)` — top of screen, centered, semi-transparent, outlined via existing geometry-path two-layer rendering (`BuildGeometry` → stroke + fill), theme-driven colors. Rendered in a separate layer outside the main Canvas (e.g. a `Grid` overlay element in XAML) so `ClearCanvas()` does not affect it. Cleared by `IOverlayWindow.ClearStatusText()`. Re-shown after mode switches during drag phase.
- `VKey` enum extended with `Prior = 0x21` (PageUp) and `Next = 0x22` (PageDown).
- JSON property name: `scrollHotkeys` (camelCase, matching existing JSON convention). C# property: `ScrollHotKeys` with `[JsonPropertyName("scrollHotkeys")]`. All plan references use `scrollHotkeys` for JSON and `ScrollHotKeys` for C#.
- Config version bump 3 → 4. Migration adds `scrollHotkeys` section with disabled defaults. `MoveOnly` and `DragDrop` are new valid values for existing `actionBindings` — no structural migration needed.
- Scroll config validation: `scrollAmount` must be ≥ 1 and ≤ 100; scroll up/down keys checked against reserved keys, action keys, chord keys, and navigation keys; duplicate up/down key rejection. Violations surfaced via startup tray notification.
- `ActionModifiers` is a new `[Flags]` enum: `None = 0, Shift = 1, Ctrl = 2, Alt = 4`. Separate from `HotKeyModifiers` (different values and semantics).
- Keyboard hook already calls `CallNextHookEx` (passes keys through) — modifier keys held by the user are visible to `GetAsyncKeyState` at all times.
- For drag-and-drop SendInput sequence: move-to-start → `BUTTONDOWN` → move-to-end → `BUTTONUP`, plus modifier KEYDOWN/KEYUP if held. Single `SendInput` call with 4+ inputs (4 base + 2 per modifier).
- Drag second-phase action matrix: `LeftClick`/`DoubleClick` → left drag, `RightClick` → right drag, `MiddleClick` → middle drag, `MoveOnly` → invalid (flash), `DragDrop` → invalid (flash). `!_dragMode` guard on the DragDrop-start branch prevents accidental restart.
- `DragDrop` and `MoveOnly` enum values guarded in `SendAction`: `DragDrop` returns after `MoveTo` (same as `MoveOnly`) as a defensive fallback — should never reach `SendAction` in normal flow.
- Drag-abort cursor restoration: `_origin` (overlay-open cursor position) is the cancel restore target throughout, not `_dragStartPoint`. `_dragStartPoint` used only for `SendDrag`. On Escape or focus loss during drag phase, cursor restores to `_origin`.

## Requirements

| ID | Requirement | Acceptance Criteria | Phases/Steps |
|----|-------------|---------------------|--------------|
| REQ-1 | Move-only action moves cursor without clicking | When `MoveOnly` action fires, cursor moves to target point, overlay closes, no mouse button event sent | 1.1, 1.2, 1.3, 1.4 |
| REQ-2 | Modifier-aware actions send Shift/Ctrl/Alt with clicks | When user holds Shift and presses Space, `SendInput` sends `VK_SHIFT` down before `LEFTDOWN` and up after `LEFTUP`; same for Ctrl, Alt, and combinations | 2.1, 2.2, 2.3, 2.4, 2.5 |
| REQ-3 | Modifier detection uses physical key state at action time | `GetAsyncKeyState(VK_SHIFT/VK_CONTROL/VK_MENU)` called when action key is dispatched; result passed through to `SendAction` | 2.1, 2.2, 2.3 |
| REQ-4 | Global scroll hotkeys send mouse wheel events at cursor position | Ctrl+Alt+PageUp sends wheel-up; Ctrl+Alt+PageDown sends wheel-down; events fire at current cursor position via `MOUSEEVENTF_WHEEL` | 3.1, 3.2, 3.3, 3.4, 3.5 |
| REQ-5 | Scroll hotkeys configurable and disabled by default | Config `scrollHotkeys` section: `enabled` (bool, default false), `scrollUpKey`/`scrollDownKey` (hotkey configs), `scrollAmount` (int, default 3) | 3.1, 3.2, 3.6 |
| REQ-6 | Scroll hotkeys pausable from tray menu | Tray context menu shows "Pause Scroll Keys" toggle when scroll is enabled; toggling unregisters/re-registers hotkeys without config change | 3.5 |
| REQ-7 | Drag-and-drop via two-point overlay flow | User navigates to start → presses drag key → overlay resets with status text → navigates to end → presses action key → drag executes between the two points | 4.1, 4.2, 4.3, 4.4, 4.5 |
| REQ-8 | Drag-and-drop action key determines button type | Second action key press maps to left/right button: `LeftClick`/`DoubleClick` → left drag, `RightClick` → right drag, `MiddleClick` → middle drag | 4.3 |
| REQ-9 | Drag-and-drop supports modifier keys | Modifiers held at second action press are injected into the SendInput drag sequence (Ctrl+drag for copy, Shift+drag for move, etc.) | 4.3 |
| REQ-10 | Status text visible during drag target selection | "Select drag target" text at top center of overlay, semi-transparent, outlined, distinct color; cleared when drag completes or is aborted | 4.2, 4.4 |
| REQ-11 | Escape during drag target selection aborts drag | Pressing Escape during second navigation phase cancels drag, restores cursor to original overlay-open origin, closes overlay | 4.2 |
| REQ-12 | Config migration v3→v4 | Existing configs gain `scrollHotkeys` section with disabled defaults; no data loss; idempotent | 5.1, 5.2 |
| REQ-13 | VKey enum includes PageUp/PageDown | `VKey.Prior = 0x21` and `VKey.Next = 0x22` added for scroll hotkey config | 3.1 |
| REQ-14 | Drag-and-drop via DragDrop action re-enters overlay with mode selection | After pressing drag key, overlay resets to mode-selection state (chord keys work, default mode activates); user can switch modes before navigating to target | 4.2 |
| REQ-15 | MoveOnly and DragDrop are valid actionBindings values | Config parser accepts `"B": "MoveOnly"` and `"Z": "DragDrop"` in `actionBindings` | 1.1, 4.1 |
| REQ-16 | Existing action behavior unchanged | All current actions (LeftClick, RightClick, MiddleClick, DoubleClick) without modifiers behave identically | 2.3 |

## Risks

| ID | Risk | Likelihood | Impact | Mitigation | Steps |
|----|------|------------|--------|------------|-------|
| RISK-1 | SendInput drag sequence too fast for some apps (drop target not recognized) | Medium | Medium | Single SendInput call with 3 inputs is atomic; if issues arise, add configurable inter-step delay as a follow-up | 4.3 |
| RISK-2 | Scroll hotkey Ctrl+Alt+PgUp/PgDn conflicts with existing app shortcuts | Low | Low | Hotkeys are user-configurable; disabled by default; `RegisterHotKey` failure surfaces via tray notification | 3.3 |
| RISK-3 | Modifier state race — user releases modifier between action-key press and SendInput execution | Low | Medium | Explicitly inject modifier KEYDOWN/KEYUP via SendInput; don't rely on physical key state at execution time | 2.3 |
| RISK-4 | Drag-and-drop overlay reset loses user context (which mode they were in) | Low | Low | Reset to default mode (same as fresh overlay open); user can chord-switch during second navigation | 4.2 |
| RISK-5 | `GetAsyncKeyState` returns stale state on UI thread dispatch delay | Low | Low | Hook uses `Dispatcher.InvokeAsync`; `GetAsyncKeyState` called in the dispatched handler reads current physical state; latency is < 1 frame | 2.1 |
| RISK-6 | Partial `SendInput` sends leave modifier keys stuck down in target app | Low | High | Validate `SendInput` return count; on partial sends, issue compensating KEYUP events for modifiers; log warning | 2.2, 4.3 |
| RISK-7 | Status text destroyed by `ClearCanvas` during mode switch in drag phase | Medium | Medium | Status text rendered in separate XAML layer outside main Canvas; `ClearCanvas` only clears the Canvas children | 4.4 |

## Phase 1: Move-Only Action
<!-- worktree: feature/013-move-only-step-1-1 -->

- [x] 1.1 Extend `MouseAction` enum and `ActionMapper` (REQ-1, REQ-15) `S`
  - Add `MoveOnly = 4` to `MouseAction` enum in `Config/MouseAction.cs`
  - `ActionMapper` requires no changes — it already maps any `MouseAction` value from config
  - Update embedded `Resources/config.json` comment: available actions list includes `MoveOnly`

- [x] 1.2 Handle `MoveOnly` in `MouseActionService.SendAction` (REQ-1) `S`
  - After `MoveTo(physicalPoint)`, if `action == MouseAction.MoveOnly`, return early — no click inputs sent
  - Pattern: `if (action == MouseAction.MoveOnly) return;` immediately after `MoveTo`

- [x] 1.3 Add default binding in embedded config (REQ-1, REQ-15) `S`
  - Add `"B": "MoveOnly"` to `actionBindings` in `Resources/config.json`
  - Update `Resources/config.schema.json` — add `MoveOnly` to the `MouseAction` enum definition

- [x] 1.4 Unit tests for move-only action (REQ-1) `S`
  - `ActionMapper` test: `"B"` maps to `MouseAction.MoveOnly`
  - `NavigatorCoordinatorTests`: verify `MoveOnly` action calls `MoveTo` but no `SendAction` click — wait, `SendAction` is the single entry point. Test that `SendAction(point, MoveOnly)` results in cursor move without click. Use `IMouseActionService` fake to verify.
  - Coordinator test: `OnSessionActionRequested` with `MoveOnly` calls `DeactivateOverlay` then `SendAction`; verify no error

## Phase 2: Modifier-Aware Actions
<!-- worktree: feature/013-move-only-step-1-1 -->

- [x] 2.1 Add `ActionModifiers` enum and `IModifierDetector` interface (REQ-2, REQ-3) [after: 1.2] `S`
  - New file `Config/ActionModifiers.cs`:
    ```csharp
    [Flags]
    public enum ActionModifiers {
        None = 0,
        Shift = 1,
        Ctrl = 2,
        Alt = 4,
    }
    ```
  - New interface `Services/IModifierDetector.cs`:
    ```csharp
    public interface IModifierDetector {
        ActionModifiers GetCurrentModifiers();
    }
    ```
  - New implementation `Services/ModifierDetector.cs`:
    ```csharp
    public sealed class ModifierDetector : IModifierDetector {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
        private const int VK_SHIFT = 0x10;
        private const int VK_CONTROL = 0x11;
        private const int VK_MENU = 0x12;
        public ActionModifiers GetCurrentModifiers() {
            var mods = ActionModifiers.None;
            if ((GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0) mods |= ActionModifiers.Shift;
            if ((GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0) mods |= ActionModifiers.Ctrl;
            if ((GetAsyncKeyState(VK_MENU) & 0x8000) != 0) mods |= ActionModifiers.Alt;
            return mods;
        }
    }
    ```
  - New fake `Klikety.Tests/Fakes/FakeModifierDetector.cs`: returns configurable `ActionModifiers` value
  - Inject `IModifierDetector` into `NavigatorCoordinator` constructor

- [x] 2.2 Update `IMouseActionService.SendAction` signature (REQ-2) [after: 2.1] `S`
  - Change: `void SendAction(Point physicalPoint, MouseAction action, ActionModifiers modifiers = ActionModifiers.None);`
  - Update `MouseActionService.SendAction` implementation: when `modifiers != None`, prepend `KEYDOWN` inputs for each active modifier flag before the click, and append `KEYUP` inputs after
  - Guard `DragDrop` and `MoveOnly` in the action switch: both return after `MoveTo` — no click inputs sent. `DragDrop` should never reach `SendAction` in normal flow, but defensive guard prevents garbage mouse events.
  - Win32 `INPUT` for key events: `type = INPUT_KEYBOARD`, `wVk` = `VK_SHIFT`/`VK_CONTROL`/`VK_MENU`, `dwFlags = 0` for down, `KEYEVENTF_KEYUP = 0x0002` for up
  - Add `INPUT_KEYBOARD = 1` and `KEYEVENTF_KEYUP = 0x0002` constants; add `KEYBDINPUT` struct
  - Extend `INPUT` struct to hold `KEYBDINPUT` via explicit layout or union pattern
  - Validate `SendInput` return count; on partial send, issue compensating `KEYUP` for any modifiers sent down, log warning via `ILogger` (RISK-6)

- [~] 2.3 Wire modifier capture in coordinator (REQ-2, REQ-3, REQ-16, RISK-3, RISK-5) [after: 2.2] `M`
  - Inject `IModifierDetector` into `NavigatorCoordinator` constructor (alongside existing services)
  - In `NavigatorCoordinator.OnSessionActionRequested`:
    ```csharp
    var modifiers = _modifierDetector.GetCurrentModifiers();
    DeactivateOverlay();
    _mouseService.SendAction(point, action, modifiers);
    ```
  - `MoveOnly` action: pass `ActionModifiers.None` always (modifiers irrelevant for cursor-only move)
  - Create `ModifierDetector` instance in `App.xaml.cs` and pass to coordinator constructor
  - Update `IMouseActionService` fake in tests to accept and record `ActionModifiers` parameter

- [ ] 2.4 Update `IModeSession.ActionRequested` event — no change needed (REQ-16) `S`
  - Sessions fire `ActionRequested(Point, MouseAction)` — unchanged. Modifier detection is a coordinator concern, not a session concern. Verify no session changes needed.

- [ ] 2.5 Unit tests for modifier-aware actions (REQ-2, REQ-3, REQ-16) `M`
  - `FakeModifierDetector` tests: configure fake to return `Shift | Ctrl`; verify coordinator passes those modifiers through to `IMouseActionService` fake
  - `MouseActionService` tests (smoke tests): verify `SendAction` with `Shift` modifier sends 4 inputs (KEYDOWN, LEFTDOWN, LEFTUP, KEYUP); with `Shift | Ctrl` sends 6 inputs; with `None` sends 2 (unchanged behavior)
  - `NavigatorCoordinator` tests: inject `FakeModifierDetector` returning `Shift`; fire action; verify `IMouseActionService` fake received `Shift`
  - Test `MoveOnly + modifiers`: `SendAction(point, MoveOnly, Shift)` still returns after `MoveTo` — modifiers ignored
  - Test `DragDrop` reaching `SendAction`: returns after `MoveTo` (defensive guard)

## Phase 3: Global Scroll Hotkeys
<!-- worktree: -->

- [ ] 3.1 Add `Prior`/`Next` to VKey enum and config model (REQ-13, REQ-5) `S`
  - `VKey.Prior = 0x21` (PageUp), `VKey.Next = 0x22` (PageDown) in `Input/VKey.cs`
  - New class `Config/ScrollHotKeyConfig.cs`:
    ```csharp
    public sealed class ScrollHotKeyConfig {
        public bool Enabled { get; init; } // default false
        public HotKeyConfig ScrollUpKey { get; init; } = new() {
            Modifiers = HotKeyModifiers.Control | HotKeyModifiers.Alt,
            Key = VKey.Prior,
        };
        public HotKeyConfig ScrollDownKey { get; init; } = new() {
            Modifiers = HotKeyModifiers.Control | HotKeyModifiers.Alt,
            Key = VKey.Next,
        };
        public int ScrollAmount { get; init; } = 3;
    }
    ```
  - Add `ScrollHotKeys` property to `ConfigModel`: `[JsonPropertyName("scrollHotkeys")] public ScrollHotKeyConfig ScrollHotKeys { get; init; } = new();`

- [ ] 3.2 Add `SendScroll` to `IMouseActionService` (REQ-4) [after: 3.1] `S`
  - Interface: `void SendScroll(int wheelDelta);`
  - Implementation: `SendInput` with `MOUSEEVENTF_WHEEL = 0x0800`, `mouseData = wheelDelta`
  - `wheelDelta` = `WHEEL_DELTA (120) * scrollAmount` (positive = up, negative = down)
  - No cursor move — wheel event fires at current cursor position

- [ ] 3.3 Create `ScrollHotKeyService` (REQ-4, REQ-5, RISK-2) [after: 3.2] `M`
  - New interface `Services/IScrollHotKeyService.cs`: `Register`, `Unregister`, `Dispose`, `bool IsRegistered`
  - New file `Services/ScrollHotKeyService.cs`
  - Creates its own `HwndSource` (separate from `HotKeyService`) for `WM_HOTKEY` messages
  - Registers two hotkeys with unique IDs (`0x2000`, `0x2001`) via `RegisterHotKey`
  - On `WM_HOTKEY`: calls `IMouseActionService.SendScroll(+delta)` or `SendScroll(-delta)` based on which ID fired
  - Constructor: `(ScrollHotKeyConfig config, IMouseActionService mouseService)`
  - `Register()` → registers both hotkeys; returns list of failures (for tray notification)
  - `Unregister()` → unregisters both
  - `IDisposable` — unregisters and disposes HwndSource
  - Testability: logic tested via `IScrollHotKeyService` fake; Win32 `RegisterHotKey` assertions scoped to smoke tests only

- [ ] 3.4 Wire `ScrollHotKeyService` in `App.xaml.cs` (REQ-4, REQ-5) [after: 3.3] `S`
  - Create `ScrollHotKeyService` after config load, before coordinator
  - If `config.ScrollHotKeys.Enabled`: call `Register()`; surface failures via tray notification
  - Dispose on app shutdown alongside `HotKeyService`

- [ ] 3.5 Tray menu pause/resume toggle (REQ-6) [after: 3.4] `M`
  - Add "Pause Scroll Keys" / "Resume Scroll Keys" menu item to tray context menu
  - Visible only when `config.ScrollHotKeys.Enabled == true`
  - Toggle calls `_scrollService.Unregister()` / `_scrollService.Register()`
  - Menu item text updates to reflect current state; checkmark when active

- [ ] 3.6 Update embedded config and schema (REQ-5) [after: 3.1] `S`
  - Add `scrollHotkeys` section (camelCase) to `Resources/config.json` with `enabled: false` and defaults
  - Update `Resources/config.schema.json` with `scrollHotkeys` object schema

- [ ] 3.8 Scroll config validation (REQ-5) [after: 3.4] `S`
  - In `ConfigLoader` validation pass:
    - `scrollAmount` must be ≥ 1 and ≤ 100; clamp or surface violation via tray notification
    - Scroll up/down keys checked against reserved keys (Escape, arrows, Return), action keys, chord keys, and navigation keys
    - Duplicate up/down key rejection
    - Violations collected and surfaced via startup tray notification (same pattern as existing validation)

- [ ] 3.7 Unit tests for scroll hotkeys (REQ-4, REQ-5, REQ-13) [after: 3.3] `M`
  - Config deserialization test: verify `scrollHotkeys` JSON property round-trips correctly with `[JsonPropertyName]`
  - Config validation tests: `scrollAmount` < 1 → violation; `scrollAmount` > 100 → violation; scroll key conflicting with action key → violation
  - Scroll dispatch logic tests (via `IScrollHotKeyService` fake): verify correct delta sign routing
  - `MouseActionService.SendScroll` tests scoped to smoke tests (Win32 `SendInput`)

## Phase 4: Drag-and-Drop
<!-- worktree: -->

- [ ] 4.1 Add `DragDrop` to `MouseAction` enum (REQ-7, REQ-15) [after: 1.1] `S`
  - Add `DragDrop = 5` to `MouseAction` enum
  - Update embedded config comment listing available actions
  - Add `"Z": "DragDrop"` to default `actionBindings` in `Resources/config.json`
  - Update `Resources/config.schema.json` — add `DragDrop` to enum

- [ ] 4.2 Add drag-mode state and overlay reset to coordinator (REQ-7, REQ-10, REQ-11, REQ-14, RISK-4) [after: 2.3, 4.1] `L`
  - New coordinator fields: `Point _dragStartPoint`, `bool _dragMode`
  - In `OnSessionActionRequested`, when `action == MouseAction.DragDrop` **and `!_dragMode`**:
    - Store `_dragStartPoint = point`
    - Set `_dragMode = true`
    - Call `ResetOverlayForDrag()`:
      1. Unsubscribe from current session events
      2. `_activeSession.Deactivate()`
      3. `_overlayWindow.ClearCanvas()`
      4. `_modeLocked = false` (allow chord switching)
      5. Create new session via `_sessionFactory` (default mode)
      6. Subscribe to new session events
      7. Wrap `_activeSession.Activate(_screenBounds, _dragStartPoint)` in try/catch for `NotSupportedException`, `ArgumentException`, `InvalidOperationException` (same pattern as `SwitchMode`); on failure: `_dragMode = false`, `ClearStatusText()`, `DeactivateOverlay()`
      8. `_overlayWindow.ShowStatusText("Select drag target")` — **after** `Activate()` so it renders on top; status text is in separate XAML layer, survives `ClearCanvas`
    - Return (do not deactivate overlay)
  - In `OnSessionActionRequested`, when `_dragMode == true`:
    - **Action matrix**: `LeftClick`/`DoubleClick` → left drag, `RightClick` → right drag, `MiddleClick` → middle drag, `MoveOnly` → invalid (flash + return), `DragDrop` → invalid (flash + return)
    - Capture `var modifiers = _modifierDetector.GetCurrentModifiers()`
    - `_overlayWindow.ClearStatusText()`
    - `DeactivateOverlay()`
    - `_mouseService.SendDrag(_dragStartPoint, point, action, modifiers)`
    - `_dragMode = false`
  - In `OnSessionCancelled`:
    - If `_dragMode`: set `_dragMode = false`, clear status text, restore cursor to `_origin` (original overlay-open position, **not** `_dragStartPoint`), `DeactivateOverlay()`
  - In `DeactivateOverlay`: if `_dragMode` was true, restore cursor to `_origin` first; reset `_dragMode = false`, `_overlayWindow.ClearStatusText()`
  - Mode switching during drag phase: after `SwitchMode` calls `ClearCanvas`, status text survives (separate layer); no re-show needed

- [ ] 4.3 Add `SendDrag` to `IMouseActionService` (REQ-7, REQ-8, REQ-9, RISK-1, RISK-3) [after: 2.2] `M`
  - Interface: `void SendDrag(Point start, Point end, MouseAction button, ActionModifiers modifiers = ActionModifiers.None);`
  - Implementation in `MouseActionService`:
    1. Compute normalized coordinates for start and end points
    2. Build `INPUT[]` array:
       - Modifier KEYDOWN inputs (if any)
       - `MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE` to start point
       - Button DOWN at start (based on `button` → left/right/middle)
       - `MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE` to end point
       - Button UP at end
       - Modifier KEYUP inputs (if any)
    3. Single `SendInput` call with all inputs
  - Button mapping: `LeftClick`/`DoubleClick` → `LEFTDOWN`/`LEFTUP`; `RightClick` → `RIGHTDOWN`/`RIGHTUP`; `MiddleClick` → `MIDDLEDOWN`/`MIDDLEUP`
  - `MoveOnly` and `DragDrop` should never reach `SendDrag` (coordinator rejects them); defensive guard returns without action if they do
  - Validate `SendInput` return count; on partial send, issue compensating `KEYUP` and `BUTTONUP` events; log warning (RISK-6)

- [ ] 4.4 Add `ShowStatusText` / `ClearStatusText` to overlay (REQ-10, RISK-7) [after: 4.2] `M`
  - Interface `IOverlayWindow`: add `void ShowStatusText(string text)` and `void ClearStatusText()`
  - Implementation in `OverlayWindow`:
    - Add a `Grid` overlay element in XAML **above** the main `Canvas` — status text lives in this separate layer, unaffected by `ClearCanvas()` which only clears Canvas children
    - Outlined text via existing geometry-path two-layer rendering (`BuildGeometry` → stroke path + fill path), matching the codebase pattern for all outlined text
    - Theme-driven colors: add `StatusTextFillColor`, `StatusTextOutlineColor`, `StatusTextBackgroundColor` to theme model — no hard-coded colors
    - Large font, semi-transparent background (`Opacity 0.85`), centered horizontally, positioned at top ~5% of screen height
    - `ShowStatusText`: create geometry paths, add to status layer
    - `ClearStatusText`: remove from status layer
    - `ClearCanvas`: does **not** touch status layer
  - Update `IOverlayWindow` fake in tests

- [ ] 4.5 Unit tests for drag-and-drop (REQ-7, REQ-8, REQ-9, REQ-10, REQ-11, REQ-14) `L`
  - Coordinator tests:
    - `DragDrop` action stores start point, resets overlay, sets drag mode
    - Second action (e.g. `LeftClick`) in drag mode calls `SendDrag` with start/end points and modifiers
    - `RightClick` in drag mode uses right button
    - Escape in drag mode aborts: `_dragMode` cleared, cursor restored to origin
    - `DragDrop` in drag mode → invalid (flash), not restart
    - `MoveOnly` in drag mode → invalid (flash), not execute
  - `MouseActionService` tests:
    - `SendDrag` with no modifiers sends 4 inputs: move-to-start, button-down, move-to-end, button-up
    - `SendDrag` with `Shift` sends 6 inputs: shift-down, move, down, move, up, shift-up
  - Overlay tests: `ShowStatusText` creates element; `ClearStatusText` removes it; `ClearCanvas` also clears status text

## Phase 5: Config Migration
<!-- worktree: -->

- [ ] 5.1 Migrate config v3 → v4 (REQ-12) [after: 3.1, 4.1] `M`
  - In `ConfigMigrator.MigrateIfNeeded`:
    - Detect `configVersion == 3` (or missing `scrollHotkeys`)
    - Add `scrollHotkeys` node (camelCase) with defaults: `{ "enabled": false, "scrollUpKey": { "modifiers": "Control, Alt", "key": "Prior" }, "scrollDownKey": { "modifiers": "Control, Alt", "key": "Next" }, "scrollAmount": 3 }`
    - Bump `configVersion` to `4`
    - No changes to `actionBindings` — `MoveOnly` and `DragDrop` are opt-in (user adds them manually or gets them from fresh config)
  - Atomic write via existing temp-file + `.bak` pattern
  - Idempotent: already-v4 configs produce no mutations

- [ ] 5.2 Config migration tests (REQ-12) [after: 5.1] `S`
  - v3 config → migrates to v4 with `scrollHotKeys` defaults
  - v4 config → no mutation
  - v3 config with existing `scrollHotKeys` (manual addition) → preserved, version bumped
  - Roundtrip: unknown fields preserved
