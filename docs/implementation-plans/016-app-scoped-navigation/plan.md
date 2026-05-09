# 016: App-Scoped Navigation

## Decisions

- **Chord key trigger**: app-scope is activated by a chord key (default `B`, configurable under `appScope.chordKey`). Follows the same mode-lock rule as other chord keys — must be pressed before any nav/arrow/action key (`_modeLocked == false`). Once activated, stays app-scoped until overlay closes. Auto-repeat guarded: `if (_appScoped) return;` at the top of the chord handler prevents redundant Hide→Show cycles from OS key repeat.
- **Pre-overlay HWND capture**: the foreground window HWND is captured in `OnHotKeyActivated` **before** `_overlayWindow.Show()` and stored as `_preOverlayHwnd`. At chord-press time, bounds are retrieved for this stored HWND — not via `GetForegroundWindow()` (which would return the overlay itself, since it is activated and focused). This eliminates the self-detection problem entirely.
- **Window bounds via DWM**: use `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` on the stored HWND to get the target window's exact visible bounds (excludes invisible shadow/border on Win10+). No fallback to `GetWindowRect` — DWM is always available on supported Windows versions (10+).
- **Minimized detection via `IsIconic`**: before querying DWM bounds, call `IsIconic(hwnd)` to explicitly detect minimized windows. DWM may return stale restored geometry for minimized windows — zero-area heuristic alone is insufficient.
- **Overlay shrinks**: the overlay window resizes to the target window's bounds (not full-screen with constrained grid). This is simpler — all grid calculators, renderers, and sessions already work with arbitrary `Rectangle screenBounds`.
- **Full window rect**: uses the full window rectangle (including title bar, borders), not just client area.
- **All modes supported**: UniformGrid, Crosshair, LogCrosshair, LogGrid all work within app-scoped bounds. Sessions receive `targetBounds` instead of `screenBounds` — no mode-specific changes needed.
- **One-time switch**: once the chord key is pressed, the overlay stays app-scoped for the rest of the session. No toggle back to full-screen.
- **Escape deactivates**: pressing Escape at L1 in app-scoped mode deactivates the overlay (same behavior as full-screen mode). No intermediate "return to full-screen" step.
- **Drag resets to full-screen**: drag mode always starts full-screen. User can press the app-scope chord key again during drag target selection if needed. The `ResetOverlayForDrag()` Hide→Show cycle is wrapped in `_switching` guard to suppress focus-loss deactivation.
- **Origin clamped on scope switch**: at chord-press time, `_origin` is re-queried to the current cursor position and clamped into the clipped bounds. This prevents out-of-bounds action points and degenerate grid calculations in Crosshair/LogCrosshair modes.
- **Multi-monitor clip**: if the foreground window extends beyond primary screen bounds, clip to the intersection with the primary screen. If the intersection is empty (window entirely on another monitor), flash error and stay full-screen.
- **Visual indicator**: a colored border (e.g. 2px accent color from theme) rendered on `StatusCanvas` (survives `ClearCanvas()` calls from renderers). Visible throughout app-scoped session; re-rendered after mode switches if `_appScoped`.
- **Config structure**: new `appScope` section at the top level of config with `chordKey` (default `B`). Config migration v5→v6 (after macros v4→v5). v5→v6 unconditional — macros migration (v4→v5) is a prerequisite.
- **Macro compatibility**: app-scope chord key works during macro recording (recorded as a navigation event, replayed via the mode session). Macro playback is screen-locked — no app-scope awareness needed at the playback level.
- **No `IOverlayWindow` API change for bounds**: `IOverlayWindow.Show()` currently sizes to primary screen. Add `Show(Rectangle bounds)` overload that sizes the overlay to arbitrary physical-pixel bounds. The parameterless `Show()` becomes a convenience calling `Show(primaryScreenBounds)`. The `Show(Rectangle)` overload pre-sets `Left`/`Top`/`Width`/`Height` before calling WPF `Show()` to avoid a visual flash at the previous size.
- **`ActiveBounds` property**: coordinator exposes `Rectangle ActiveBounds => _appScoped ? _appScopeBounds : _screenBounds;`. Used by `SwitchMode()`, `ResetOverlayForDrag()`, `OnSessionActionRequested`, and any other code that currently references `_screenBounds` for session activation or bounds validation.
- **`_currentModeName` tracking**: coordinator tracks the active mode name in a `string _currentModeName` field, set in `OnHotKeyActivated` (from `GetDefaultModeName()`) and updated in `SwitchMode()`. Used by `SwitchToAppScope()` to recreate the correct session.
- **`OnFocusLost` switching guard**: `OnFocusLost` adds `if (_switching) return;` before `DeactivateOverlay()`. This is required because the existing handler calls `DeactivateOverlay()` unconditionally — WPF fires `Deactivated` on `Hide()` during app-scope transition and drag reset, which would terminate the session mid-transition.

## Requirements

| ID | Requirement | Acceptance Criteria | Phases/Steps |
|----|-------------|---------------------|--------------|
| REQ-1 | Win32: get foreground window bounds via `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` | Returns physical-pixel `Rectangle` matching the visible window bounds; handles null/invalid HWND gracefully (returns empty Rectangle); `IsIconic(hwnd)` used for explicit minimized detection | 1.1 |
| REQ-2 | `IForegroundWindowProvider` interface for testability | Interface with `nint GetForegroundWindowHandle()` and `Rectangle GetWindowBounds(nint hwnd)` methods; production implementation uses DWM; fake returns configurable handle + bounds | 1.1, 1.2 |
| REQ-3 | `IOverlayWindow.Show(Rectangle bounds)` overload sizes overlay to arbitrary bounds | Overlay positioned and sized to given physical-pixel rectangle (converted to DIPs); existing parameterless `Show()` delegates to it with primary screen bounds | 1.3 |
| REQ-4 | App-scope chord key in coordinator key dispatch (same mode-lock rule as other chords) | Chord key processed before session when `_modeLocked == false`; pressing after any nav key → forwarded to session (which flashes invalid) | 2.1 |
| REQ-5 | On chord press: get stored HWND bounds, validate, resize overlay, re-activate session at new bounds with clamped origin | Session receives new bounds; grid recalculated for window area; origin re-queried and clamped into bounds | 2.1, 2.2 |
| REQ-6 | Minimized window → flash error, stay full-screen | `IsIconic(hwnd)` detects minimized state; `InvalidKeyPressed` flashed; overlay remains full-screen | 2.1 |
| REQ-7 | Multi-monitor clip: window beyond primary screen → clip to intersection | Bounds clipped to `_screenBounds` intersection; empty intersection → flash error + stay full-screen | 2.1 |
| REQ-8 | Visual indicator: colored border on overlay when app-scoped | 2px border rendered on overlay canvas (accent color from theme); visible throughout app-scoped session; cleared on deactivate | 2.3 |
| REQ-9 | Drag mode resets to full-screen; chord key available during drag | `ResetOverlayForDrag()` resizes overlay back to full-screen; `_appScoped` flag cleared; chord key can re-activate app scope during drag target selection | 2.4 |
| REQ-10 | Config: `appScope` section with `chordKey` (default `B`) | Config deserializes correctly; migration v5→v6 adds section if missing | 3.1, 3.2 |
| REQ-11 | Config validation: app-scope chord key vs all existing key sets | Chord key checked against reserved keys, action bindings, nav keys, other chord keys, scroll keys, macro keys; collisions → validation warning | 3.3 |
| REQ-12 | Macro compatibility: app-scope chord key recorded as part of navigation during macro recording | App-scope key enters normal key dispatch during recording; session re-created at new bounds; recording continues | 2.1 |

## Risks

| ID | Risk | Likelihood | Impact | Mitigation | Steps |
|----|------|------------|--------|------------|-------|
| RISK-1 | DWM unavailable or returns incorrect bounds for certain window types (UWP, maximized, admin-elevated) | Low | Medium | Validate bounds are non-zero and within primary screen; fallback to staying full-screen with flash error | 1.1, 2.1 |
| RISK-2 | Overlay resize causes rendering artifacts (WPF layout pass, PresentationSource transform edge cases) | Medium | Medium | Reuse existing DIP transform logic from `OverlayWindow.Show()`; test with various DPI scales | 1.3, 2.2 |
| RISK-3 | App-scope chord key conflicts with macro keys (plan 015) or future key additions | Low | Low | Full collision matrix in config validation covers all key sets including macro keys | 3.3 |
| RISK-4 | Focus loss when overlay resizes (WPF fires `Deactivated` on `Hide()` during app-scope transition and drag reset) | Medium | High | Add `if (_switching) return;` guard to `OnFocusLost` handler (currently calls `DeactivateOverlay()` unconditionally). Wrap both `SwitchToAppScope` and modified `ResetOverlayForDrag` in `_switching` guard. Test with `FakeOverlayWindow` that raises `FocusLost` on `Hide()` | 2.1, 2.2, 2.4 |
| RISK-5 | Session state lost during re-activation at new bounds | Low | Medium | Re-activate resets session state (mode lock cleared, new session at new bounds) — this is intentional and matches mode-switch behavior | 2.1, 2.2 |
| RISK-6 | Foreground window captured before overlay opens — self-detection N/A | Low | Low | Pre-overlay HWND capture eliminates the problem. Defensive guard: if stored HWND is zero or invalid → flash error | 2.1 |
| RISK-7 | `SwitchMode()` uses `_screenBounds` — mode switch during app-scope reverts to full-screen | Medium | High | `SwitchMode()` uses `ActiveBounds` property instead of `_screenBounds`. Same for `OnSessionActionRequested` bounds validation | 2.1 |
| RISK-8 | `Show(Rectangle)` flashes overlay at old size before repositioning (WPF `Show()` needed for `PresentationSource`) | Medium | Medium | Pre-set `Left`/`Top`/`Width`/`Height` before WPF `Show()` when window was previously shown (PresentationSource still available from first `Show()`) | 1.3 |
| RISK-9 | Adding `IForegroundWindowProvider` to `IPlatformServices` breaks all existing test setups (`FakePlatformServices`) | Low | Low | Routine — update `FakePlatformServices` and all test constructors in step 1.2 | 1.2 |

## Phase 1: Platform & Overlay Infrastructure
<!-- worktree: feature/016-app-scoped-navigation -->

- [x] 1.1 Add `IForegroundWindowProvider` interface + production implementation using `DwmGetWindowAttribute` (REQ-1, REQ-2, RISK-1) `M`
  - Add P/Invoke to `NativeMethods`:
    - `DwmGetWindowAttribute(IntPtr hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT, sizeof(RECT))` (from `dwmapi.dll`)
    - `IsIconic(IntPtr hwnd)` (from `user32.dll`) — explicit minimized detection
    - `GetForegroundWindow` already declared but private — add public `GetForegroundWindowHandle()` wrapper
  - Interface: `IForegroundWindowProvider` in `Services/` with two methods:
    - `nint GetForegroundWindowHandle()` — returns HWND of current foreground window
    - `Rectangle GetWindowBounds(nint hwnd)` — returns physical-pixel bounds for given HWND via DWM
  - Production implementation: `ForegroundWindowProvider`
    - `GetForegroundWindowHandle()`: calls `NativeMethods.GetForegroundWindow()`; returns `IntPtr.Zero` on failure
    - `GetWindowBounds(nint hwnd)`: calls `IsIconic(hwnd)` first → if minimized, return `Rectangle.Empty`. Otherwise `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` → convert `RECT` to `Rectangle`. DWM failure → return `Rectangle.Empty`
  - Add to `IPlatformServices` as `IForegroundWindowProvider ForegroundWindow { get; }`

- [x] 1.2 `FakeForegroundWindowProvider` for testing (REQ-2, RISK-9) `S`
  - Configurable `Handle` property (default: `IntPtr.Zero`)
  - Configurable `Bounds` property (default: `Rectangle.Empty`)
  - Add to `FakePlatformServices` — update all existing test constructors

- [x] 1.3 Add `IOverlayWindow.Show(Rectangle bounds)` overload (REQ-3, RISK-2, RISK-8) `M`
  - New interface method: `void Show(Rectangle bounds)` — sizes overlay to given physical-pixel rectangle
  - `OverlayWindow` implementation: same DIP transform logic as current `Show()`, but using provided bounds instead of `GetPrimaryScreenBounds()`
  - **Flash prevention**: when the window was previously shown (has existing `PresentationSource`), pre-set `Left`/`Top`/`Width`/`Height` to target bounds before calling WPF `Show()`. Only call `Show()`-then-reposition on first invocation when `PresentationSource` is not yet available.
  - Existing `Show()` delegates: calls `Show(NativeMethods.GetPrimaryScreenBounds())`
  - `FakeOverlayWindow`: record bounds in `Show(Rectangle)` call; parameterless `Show()` delegates with default bounds; optionally raise `FocusLost` on `Hide()` (configurable flag, default off) for testing RISK-4

- [ ] 1.4 Unit tests for `IForegroundWindowProvider` fake and `IOverlayWindow.Show(Rectangle)` (REQ-2, REQ-3) `S`
  - Fake returns configured bounds
  - `Show(Rectangle)` stores bounds for assertion

## Phase 2: Coordinator App-Scope Logic
<!-- worktree: -->

- [ ] 2.1 Chord key dispatch: app-scope activation in coordinator (REQ-4, REQ-5, REQ-6, REQ-7, REQ-12, RISK-1, RISK-4, RISK-5, RISK-6, RISK-7) [after: 1.1, 1.3] `L`
  - Add coordinator fields:
    - `_appScoped` bool (false by default, cleared on `DeactivateOverlay`)
    - `_appScopeBounds` Rectangle
    - `_appScopeChordKey` VKey? (from `ConfigModel.AppScope.ChordKey`) — null if disabled
    - `_preOverlayHwnd` nint — captured in `OnHotKeyActivated` before `Show()`
    - `_currentModeName` string — set in `OnHotKeyActivated` from `GetDefaultModeName()`, updated in `SwitchMode()`
  - **`ActiveBounds` property**: `Rectangle ActiveBounds => _appScoped ? _appScopeBounds : _screenBounds;` — used by `SwitchMode()`, `ResetOverlayForDrag()`, `OnSessionActionRequested` bounds validation, and any code that currently references `_screenBounds` for session bounds (RISK-7)
  - **`OnFocusLost` guard**: add `if (_switching) return;` before `DeactivateOverlay()` call in `OnFocusLost` handler (RISK-4)
  - **HWND capture**: in `OnHotKeyActivated`, before `_overlayWindow.Show()`, call `_preOverlayHwnd = _platform.ForegroundWindow.GetForegroundWindowHandle()`
  - Key dispatch: check `_appScopeChordKey` in the chord dispatch section (before mode chord keys, same `_modeLocked` gate). On match:
    1. **Auto-repeat guard**: if `_appScoped` → return immediately (prevents redundant Hide→Show from OS key repeat)
    2. Get bounds for stored HWND: `_platform.ForegroundWindow.GetWindowBounds(_preOverlayHwnd)` (uses pre-captured HWND, not current foreground)
    3. **Invalid HWND check**: if `_preOverlayHwnd == 0` or bounds are empty → flash invalid, return
    4. **Clip to primary screen**: intersect bounds with `_screenBounds`. If intersection is empty → flash invalid, return (REQ-7)
    5. **Clamp origin**: re-query cursor position via `_platform.Cursor.GetCursorPosition()`, clamp into clipped bounds: `x = Math.Clamp(cursor.X, bounds.Left, bounds.Right - 1)`, `y = Math.Clamp(cursor.Y, bounds.Top, bounds.Bottom - 1)`. Update `_origin`.
    6. Set `_appScoped = true`, store `_appScopeBounds = clippedBounds`
    7. Call `SwitchToAppScope(clippedBounds)` — performs overlay resize + session re-activation
  - `SwitchToAppScope(Rectangle bounds)`:
    1. Set `_switching = true`
    2. Unsubscribe old session events
    3. `_activeSession.Deactivate()`
    4. `_overlayWindow.ClearCanvas()`
    5. `_overlayWindow.Hide()` — may fire `FocusLost`, suppressed by `_switching` guard
    6. `_overlayWindow.Show(bounds)` — resizes overlay to window bounds
    7. Render app-scope border on `StatusCanvas` (REQ-8, step 2.3)
    8. Create new session via `_sessionFactory.Create(_currentModeName)`
    9. Subscribe events
    10. `session.Activate(bounds, _origin)` — origin is clamped cursor position
    11. `_activeSession = session`
    12. `_modeLocked = false` (allow further chord keys for mode switching)
    13. Set `_switching = false` in finally block
  - Wrap in try/catch/finally with `_switching` guard (same pattern as `SwitchMode()`)

- [ ] 2.2 Overlay resize + session re-activation flow (REQ-5, RISK-2, RISK-4) [after: 2.1] `M`
  - Test the resize path: overlay hides → shows at new bounds → session activated with correct bounds
  - Focus loss guard: `_switching` flag in `OnFocusLost` suppresses `DeactivateOverlay()` during the hide/show transition (added in 2.1)
  - Test with `FakeOverlayWindow.RaiseFocusLostOnHide = true` to simulate WPF `Deactivated` during `Hide()` — verify `_switching` guard prevents deactivation
  - Verify PresentationSource transform works correctly when overlay repositioned

- [ ] 2.3 App-scope visual border indicator (REQ-8) [after: 2.1, 2.7] `M` [discovery]
  - Render 2px border rectangle on `StatusCanvas` (not `RootCanvas`) — survives `ClearCanvas()` calls from renderers during normal key updates
  - Border color from theme `AppScopeBorderColor` (added in step 2.7); hardcoded fallback `#4488FF` if theme property missing
  - Border rendered in `SwitchToAppScope()` after session activation
  - Border survives mode switches and renderer updates (lives on `StatusCanvas`)
  - Border cleared on `DeactivateOverlay()` and `ClearStatusText()`

- [ ] 2.7 Theme: add `AppScopeBorderColor` to `ThemeModel` (REQ-8) [after: 1.3] `S`
  - Default color in dark theme: `#4488FF` (blue accent)
  - Default color in light theme: `#2266CC`
  - Add to embedded theme files + schema
  - Must be completed before 2.3 (border rendering references theme property)

- [ ] 2.4 Drag mode interaction with app-scope (REQ-9, RISK-4) [after: 2.1] `M`
  - `ResetOverlayForDrag()`: if `_appScoped`, wrap in `_switching` guard (same as `SwitchToAppScope`), resize overlay back to full-screen (`_overlayWindow.Hide()` → `_overlayWindow.Show()` with full screen bounds), clear `_appScoped = false`, clear border from `StatusCanvas`
  - The `_switching` guard prevents `OnFocusLost` from triggering `DeactivateOverlay()` during the Hide→Show cycle
  - Chord key for app-scope available during drag target selection (standard chord dispatch applies — user can press `B` again to scope to target app)
  - Completing drag → `DeactivateOverlay()` as normal (clears `_appScoped`)

- [ ] 2.5 `DeactivateOverlay` cleanup (REQ-5) [after: 2.1] `S`
  - Clear `_appScoped = false` in `DeactivateOverlay()`
  - Clear `_appScopeBounds`

- [ ] 2.6 Unit tests for app-scope coordinator logic (REQ-4, REQ-5, REQ-6, REQ-7, REQ-9, REQ-12, RISK-4, RISK-7) [after: 2.1, 2.4] `L`
  - **Chord press happy path**: overlay open → press app-scope chord → overlay resized to stored HWND bounds → session active with new bounds → origin clamped into bounds
  - **Mode lock**: press nav key first → app-scope chord forwarded to session (flashes invalid)
  - **Minimized**: `IsIconic` returns true for stored HWND → flash invalid, overlay stays full-screen
  - **Multi-monitor clip**: window extends beyond primary → clipped to intersection; empty intersection → flash error
  - **Invalid HWND**: stored HWND is zero → flash invalid
  - **Auto-repeat guard**: chord key pressed when already `_appScoped` → no-op (no Hide→Show cycle)
  - **Origin clamping**: cursor outside window bounds → clamped to nearest edge; cursor inside → unchanged
  - **Escape**: at L1 in app-scope → deactivates overlay, clears `_appScoped`
  - **Drag**: app-scoped → start drag → overlay resizes to full-screen via `_switching` guard, `_appScoped` cleared; chord key available during drag
  - **Mode switch**: app-scoped → mode chord key → stays app-scoped at same bounds (uses `ActiveBounds`, not `_screenBounds`); `_currentModeName` updated
  - **Focus loss during resize**: `FakeOverlayWindow.RaiseFocusLostOnHide = true` → `_switching` guard prevents deactivation during `SwitchToAppScope` and `ResetOverlayForDrag`
  - **Macro recording**: app-scope chord recorded; overlay resumes at window bounds after action

## Phase 3: Config & Validation
<!-- worktree: -->

- [ ] 3.1 Add `AppScopeConfig` to `ConfigModel`; config migration v5→v6 (REQ-10) [after: phase 2] `M`
  - `AppScopeConfig`: `VKey? ChordKey` (default `VKey.B`)
  - `ConfigModel.AppScope` property
  - `ConfigMigrator`: v5→v6 adds `appScope` section with defaults if missing. Unconditionally v5→v6 — macros migration (v4→v5) is a prerequisite and must land first. v5→v6 migration verifies `macros` section exists (from v4→v5) as a precondition guard.
  - Config round-trip: unknown fields preserved

- [ ] 3.2 Unit tests for config migration (REQ-10) [after: 3.1] `S`
  - Migration from pre-appScope config → section added with defaults
  - Already-migrated config → no mutations
  - Round-trip preserves unknown fields

- [ ] 3.3 Config validation for app-scope chord key (REQ-11, RISK-3) [after: 3.1] `M`
  - **Collision targets**: reserved keys (Escape, arrows, VK_RETURN, hotkey modifiers), `actionBindings`, `firstKeys`/`secondKeys`, mode chord keys (Crosshair, LogCrosshair, LogGrid), scroll hotkeys, macro keys (record, helper, slot keys)
  - Collision → validation warning; app-scope disabled for session
  - Null chord key → app-scope feature disabled (no chord key registered)

- [ ] 3.4 Unit tests for config validation (REQ-11) [after: 3.3] `S`
  - Chord key conflicts with action binding → warning
  - Chord key conflicts with mode chord → warning
  - Chord key conflicts with nav key → warning
  - Null chord key → no validation errors, feature disabled

## Phase 4: Polish & Documentation
<!-- worktree: -->

- [ ] 4.1 Logging: structured log entries for app-scope operations [after: 2.1] `S`
  - App-scope activated: log target window bounds (width × height)
  - App-scope rejected: log reason (minimized, empty intersection, self-detection)
  - Use source-generated `[LoggerMessage]` methods on coordinator partial class

- [ ] 4.2 Design note: update `navigation-modes.design.md` with app-scope section [after: phase 3] `S`
  - Document: chord key trigger, pre-overlay HWND capture, bounds acquisition (DWM + IsIconic), overlay resize, session re-activation, origin clamping, ActiveBounds property, drag interaction, macro compatibility, visual indicator on StatusCanvas, OnFocusLost switching guard
  - Update `.design-notes.md` index if scope changes

- [ ] 4.3 Design note: update `win32-interop.design.md` with DWM window bounds [after: 1.1] `S`
  - Document: `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)`, `IsIconic`, `IForegroundWindowProvider` (two-method API: handle + bounds), pre-capture pattern

- [ ] 4.4 Design note: update `config.design.md` with appScope config section [after: 3.1] `S`
  - Document: `AppScopeConfig` shape, migration v5→v6 (unconditional, macros v4→v5 prerequisite), validation rules
