# 001: Keyboard-Driven Mouse Navigator

## Decisions
- WPF transparent fullscreen overlay on primary monitor only (multi-monitor out of scope)
- Key scheme: first key = QWERTY home row (a,s,d,f,g,h,j,k,l = 9 keys); second key = top row non-pinky (w,e,r,t,y,u,i,o = 8 keys); yields 9×8 = 72 cells per level
- Navigation has 2 standard levels; level-3 auto-activated when screen DPI/resolution makes level-2 cell larger than a target size threshold
- Escape cancels navigation and restores cursor to its position at the time the overlay was activated
- First key press: dim non-matching cells, highlight matching column
- Action keys: Space = left-click (default); right-click, middle-click, double-click = user-assigned in JSON config
- Navigation keys move cursor to cell center at each level; action key (Space etc.) is pressed separately after any level to fire — cursor always at center of most-recently-selected cell
- Action can be fired after any navigation level (L1 or L2 or L3); no requirement to complete all levels
- Hotkey capture dialog is out of scope; schema + inline config comments are sufficient documentation
- Config format is JSONC (JSON with Comments); parsed via `JsonCommentHandling.Skip`; stored at `%APPDATA%\Klikety\config.json`; includes global hotkey, action bindings, key sets
- Ship `config.schema.json` alongside the app with `description` on every property, enum values, defaults, and examples; referenced via `$schema` in config file
- Ship a default `config.json` with inline `//` comments documenting every field; written to `%APPDATA%\Klikety\` on first run if absent
- Global hotkey via Win32 `RegisterHotKey`; conflict detected at startup (return value check); surfaced as tray notification
- System tray icon (WinForms NotifyIcon interop); `ShutdownMode=OnExplicitShutdown`; no main window; context menu: "About", "Open Configuration Folder", "Quit"
- Win32 `SendInput` for mouse movement and clicks; absolute coordinates normalized to 0–65535 range; all geometry in physical pixels; single DIP→physical-pixel conversion at WPF rendering boundary using `PresentationSource` transform
- Screen bounds from `Screen.PrimaryScreen.Bounds` (WinForms); same source used by `GridCalculator` and `MouseActionService` to keep geometry consistent
- Low-level keyboard hook (`SetWindowsHookEx WH_KEYBOARD_LL`) for key capture while overlay is active; unhooked when overlay closes; hook callback does minimal work and dispatches to UI thread via `Dispatcher.InvokeAsync` to avoid OS timeout (~300ms)
- Keyboard input model is virtual-key (`VKey` enum); character translation used only for display labels; config action bindings reference stable VKey names; navigation key sets defined as VKey lists
- `DeactivateOverlay()` is a single idempotent method called from every exit path (action, cancel/Escape, focus loss, exception, quit); hides overlay, disables hook, resets state machine
- `OverlayWindow.Deactivated` event triggers `DeactivateOverlay()` to handle focus loss (Alt+Tab, OS notifications, background app stealing focus)
- If `SetWindowsHookEx` returns null on activation, overlay is immediately closed and a tray notification is shown
- Theme file resolution: bare name → `%APPDATA%\Klikety\themes\<name>.theme.json`; relative path → resolved relative to config folder only; must have `.theme.json` extension; path is canonicalized and traversal sequences (`../`) are rejected; invalid/inaccessible file → fall back to built-in dark with tray notification
- Key-binding conflict policy: reserved keys (Escape, hotkey modifiers) may not be used as action or navigation keys; action keys and navigation keys must not overlap; validated at startup with tray notification listing each violation
- Structured file logging via `Microsoft.Extensions.Logging` → rolling file in `%APPDATA%\Klikety\logs\`; wired into Win32 services and state machine; log level configurable in config
- Text selection action: deferred to TODO backlog
- Arrow key navigation: arrow VKeys (`VK_LEFT/RIGHT/UP/DOWN`) move a highlighted-cell cursor within the current grid level; `VK_RETURN` fires the action (default left-click) at the current cell; these VKeys do not conflict with the two-key grid scheme and are always reserved (may not appear in `firstKeys`, `secondKeys`, or `ActionBindings`); behaviour controlled by config `"navigationMode"`: `"twoKey"` (grid-only), `"arrow"` (arrow-only), `"both"` (default — both schemes active simultaneously)
- Keyboard layout independence: VKey codes are physical-position codes, stable across layouts; `LabelGenerator` derives display characters via Win32 `ToUnicode`/`MapVirtualKey` against the active HKL so labels reflect the user's layout on screen; default `firstKeys`/`secondKeys` in config cover QWERTY and QWERTZ (identical home/top rows); DVORAK and Colemak users override these in config; default `config.json` comments include DVORAK and Colemak example key sets
- Level-3 trigger: if computed level-2 cell physical-pixel area exceeds configurable threshold (default sized for ~4K)
- Theme system: separate `theme.json` files; built-in `dark.theme.json` and `light.theme.json` shipped as embedded resources, extracted to `%APPDATA%\Klikety\themes\` on first run alongside `config.json`
- `config.json` `"theme"` field: bare name (e.g. `"dark"`) resolves to `themes/<name>.theme.json`; relative path resolved from config folder
- `theme.schema.json` shipped and referenced via `$schema` in each theme file; extracted on first run
- `ThemeModel` POCO: label font family, size, color, weight; cell border color + thickness; normal cell background color + opacity; dimmed cell overlay color + opacity; highlighted column background + border color; subgrid distinct style flag
- Win32 service interfaces (`IHotKeyService`, `IKeyboardHookService`, `IMouseActionService`, `IOverlayWindow`) are the seam for testing; real implementations are thin P/Invoke wrappers; fakes are injected in tests
- Integration tests are hermetic and CI-safe — no real display, no OS hooks, no timing dependencies; smoke test project (excluded from CI) covers manual verification of real Win32 calls
- "Start with Windows" toggle via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`; no admin rights required; tray context menu item "Start with Windows" shown with a checkmark reflecting current registry state; toggling writes or removes the value; value points to the published exe path (resolved via `Environment.ProcessPath`); state stored in registry only (not in config)

## Requirements

| ID | Requirement | Steps |
|----|-------------|-------|
| REQ-1 | Configurable global hotkey triggers the navigator overlay | 4.1 |
| REQ-2 | WPF transparent fullscreen overlay on primary monitor | 6.1 |
| REQ-3 | Level-1 9×8 grid with home-row × top-row key labels displayed | 3.1, 3.2, 6.2 |
| REQ-4 | First key press dims non-matching cells, highlights matching column | 5.1, 6.3 |
| REQ-5 | Second key press moves cursor to cell center; shows level-2 subgrid | 3.3, 5.1, 6.4 |
| REQ-6 | Level-2 navigation with same key scheme within the selected cell | 3.3, 5.1 |
| REQ-7 | Level-3 navigation auto-activated by resolution/DPI threshold | 3.3, 5.1 |
| REQ-8 | Configured action performed at center of currently selected cell; can be fired after any navigation level | 4.4, 7.4 |
| REQ-9 | Supported actions: left-click, right-click, middle-click, double-click | 4.4 |
| REQ-10 | Space = left-click (default); other action keys user-assigned in config | 2.1, 5.2 |
| REQ-11 | Overlay closes after action; cursor remains at cell center | 5.1, 7.4 |
| REQ-12 | Escape at L1 closes overlay and restores cursor to its position at activation time; Escape at L2/L3 goes back one level (cursor moves back to parent cell center) | 5.1, 4.4 |
| REQ-13 | JSONC config: global hotkey, action bindings (VKey names), key sets (VKey lists), level-3 threshold | 2.1, 2.2 |
| REQ-13a | `config.schema.json` shipped with app; all properties documented with descriptions, types, defaults, examples | 2.4 |
| REQ-13b | Default `config.json` with inline `//` comments documenting every field; written on first run if absent | 2.5 |
| REQ-14 | Startup hotkey conflict detection with tray notification | 2.3, 4.2 |
| REQ-15 | System tray icon; context menu: "About" (small WPF window: app name, version, author, link), "Open Configuration Folder" (opens `%APPDATA%\Klikety\` in Explorer), "Quit"; no main window at startup | 7.1, 7.6 |
| REQ-16 | Unit tests: grid logic, label generation, config, state machine | 8.1, 8.2, 8.3 |
| REQ-17 | Integration tests: end-to-end navigation + action flow | 8.5 |
| REQ-18 | `ThemeModel` POCO; `ThemeLoader` reads `theme.json` by name or path from config; validates required fields | 2.6, 2.7 |
| REQ-18a | `theme.schema.json` with descriptions, types, defaults, examples for all theme properties | 2.8 |
| REQ-18b | Built-in `dark.theme.json` and `light.theme.json` shipped as embedded resources; extracted to `%APPDATA%\Klikety\themes\` on first run | 2.9 |
| REQ-19 | `DeactivateOverlay()` idempotent method covers all exit paths: action, cancel/Escape, focus loss, exception, quit | 7.7 |
| REQ-20 | Focus loss on overlay triggers immediate cancel and `DeactivateOverlay()` | 6.1, 7.7 |
| REQ-21 | Hook install failure on activation triggers immediate overlay close + tray notification | 7.2 |
| REQ-22 | Key-binding conflict validation at startup: reserved keys, action/navigation overlaps; tray notification lists violations | 2.2, 7.1 |
| REQ-23 | Structured file logging via `Microsoft.Extensions.Logging` → rolling file in `%APPDATA%\Klikety\logs\`; log level in config | 2.10 |
| REQ-24 | Arrow key navigation alternative: arrow VKeys move cell cursor within current grid level; Enter fires left-click; active when `navigationMode` is `"arrow"` or `"both"`; arrow + Enter VKeys are always reserved and excluded from conflict validation pool | 2.1, 5.1, 5.3 |
| REQ-25 | `LabelGenerator` derives display characters via Win32 `ToUnicode`/`MapVirtualKey` against active HKL; labels correct for any active layout; DVORAK/Colemak supported via `firstKeys`/`secondKeys`; default config comments include DVORAK and Colemak examples | 2.5, 3.2 |
| REQ-26 | `README.md` at repo root: project description, screenshot/GIF placeholder, prerequisites, build instructions, installation, configuration reference (all config fields with types/defaults/examples), theme customization, keyboard layout setup (DVORAK/Colemak), troubleshooting common issues | 8.7 |
| REQ-27 | "Start with Windows" tray menu toggle; reads/writes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`; checkmark reflects current state; no admin rights required | 7.8 |

## Phase 1: Project Foundation

- [ ] 1.1 Scaffold solution: `Klikety.sln`, `src/Klikety/Klikety.csproj` (WPF, net9.0-windows), `src/Klikety.Tests/Klikety.Tests.csproj` (xUnit, net9.0-windows) (REQ-1, REQ-15)
- [ ] 1.2 Create design note `docs/design-notes/keyboard-navigator.design.md` covering overlay lifecycle, state machine, Win32 interop, and key scheme (REQ-3, REQ-16)

## Phase 2: Config System

- [ ] 2.1 Define `ConfigModel` POCO: `HotKey` (modifier + `VKey` enum), `ActionBindings` (`VKey` → `MouseAction` enum), `KeySets` (first-key `VKey` list, second-key `VKey` list), `Level3CellSizeThreshold` (int physical pixels²), `LogLevel`, `NavigationMode` enum (`TwoKey` | `Arrow` | `Both`; default `Both`) (REQ-13, REQ-24)
- [ ] 2.2 `ConfigLoader`: read `%APPDATA%\Klikety\config.json` via `System.Text.Json` with `JsonCommentHandling.Skip`; apply defaults when file absent or fields missing; validate key set constraints and key-binding conflicts (reserved keys, action/navigation overlaps; arrow VKeys + `VK_RETURN` always excluded from `firstKeys`/`secondKeys`/`ActionBindings` as permanently reserved); collect all violations and return as list for tray notification (REQ-13, REQ-22, REQ-24) [after: 2.1]
- [ ] 2.3 `StartupValidator`: attempt `RegisterHotKey` probe; if conflict, store validation error for tray notification at startup (REQ-14) [after: 2.1]
- [ ] 2.4 Author `config.schema.json` (embedded resource): full JSON Schema covering every `ConfigModel` field with `description`, `type`, `default`, and examples; VKey names enumerated; referenced via `$schema` in default config (REQ-13a) [after: 2.1]
- [ ] 2.5 Author default `config.json` template (embedded resource): all fields present with sensible defaults and `//` comments explaining each; include commented-out DVORAK and Colemak example `firstKeys`/`secondKeys` blocks; `ConfigLoader` writes it to `%APPDATA%\Klikety\` on first run if absent (REQ-13b, REQ-25) [after: 2.4]
- [ ] 2.6 Define `ThemeModel` POCO: label font family/size/color/weight; cell border color + thickness; normal cell background color + opacity; dimmed cell overlay color + opacity; highlighted column background + border color; subgrid distinct border/label color (REQ-18) [after: none]
- [ ] 2.7 `ThemeLoader`: resolve `"theme"` value from config — bare name → `%APPDATA%\Klikety\themes\<name>.theme.json`; relative path → resolved relative to config folder only; must have `.theme.json` extension; canonicalize and reject path traversal (`../`); parse JSONC via `JsonCommentHandling.Skip`; validate required fields; fall back to built-in dark on error with tray notification (REQ-18) [after: 2.6, 2.2]
- [ ] 2.8 Author `theme.schema.json` (embedded resource): full JSON Schema for `ThemeModel` with `description`, `type`, `default`, and color format examples; extracted to `%APPDATA%\Klikety\themes\` on first run (REQ-18a) [after: 2.6]
- [ ] 2.9 Author `dark.theme.json` and `light.theme.json` (embedded resources): JSONC with `$schema` reference and `//` comments on every property; extracted to `%APPDATA%\Klikety\themes\` on first run (REQ-18b) [after: 2.8]
- [ ] 2.10 Configure `Microsoft.Extensions.Logging` with a rolling file sink writing to `%APPDATA%\Klikety\logs\`; `LogLevel` read from config; inject `ILogger<T>` into Win32 services and state machine (REQ-23) [after: 2.1]

## Phase 3: Grid Logic (pure, unit-testable)

- [ ] 3.1 `GridCalculator`: screen bounds from `Screen.PrimaryScreen.Bounds` (physical pixels) + key set sizes → `IReadOnlyList<GridCell>` (physical-pixel bounds, row/col indices); test vectors for 96/120/144/192 DPI (REQ-3) [after: none]
- [ ] 3.2 `LabelGenerator`: first-key `VKey` list × second-key `VKey` list → bijective display-char labels; display chars derived via Win32 `ToUnicode`/`MapVirtualKey` against current HKL so labels reflect the active keyboard layout; `LabelFor(row, col)` and `CellFor(label)`; falls back to VKey name string if `ToUnicode` returns no character (dead key, unmapped) (REQ-3, REQ-25) [after: none]
- [ ] 3.3 `SubgridCalculator`: parent `GridCell` (physical pixels) + key sets → level-2 cells; level-3 threshold comparison in physical pixels²; test vectors for DPI variants (REQ-5, REQ-6, REQ-7) [after: 3.1]

## Phase 4: Win32 Services

- [ ] 4.1 Define service interfaces: `IHotKeyService`, `IKeyboardHookService`, `IMouseActionService`, `IOverlayWindow`; all production and test code depends only on these (REQ-1, REQ-4, REQ-8) [after: none]
- [ ] 4.2 `HotKeyService` : `IHotKeyService` — `RegisterHotKey` / `UnregisterHotKey` via dispatcher message loop; raises `Activated`; reports conflict on failed registration (REQ-1, REQ-14) [after: 4.1, 2.3]
- [ ] 4.3 `KeyboardHookService` : `IKeyboardHookService` — `SetWindowsHookEx(WH_KEYBOARD_LL)` + `UnhookWindowsHookEx`; enabled only while overlay is visible; hook callback does minimal work (read `VKey` + `KeyboardState` from `KBDLLHOOKSTRUCT`, call `CallNextHookEx`, post to `Dispatcher.InvokeAsync`); raises `KeyPressed(VKey)` on UI thread (REQ-4, REQ-12) [after: 4.1]
- [ ] 4.4 `MouseActionService` : `IMouseActionService` — `MoveTo(physicalPoint)` normalizes to 0–65535 using `Screen.PrimaryScreen.Bounds`; `SendInput` for `MOUSEMOVE` + `MOUSEEVENTF_LEFTDOWN/UP`, `RIGHTDOWN/UP`, `MIDDLEDOWN/UP`, double-click (two click pairs) (REQ-8, REQ-9) [after: 4.1]

## Phase 5: Navigation State Machine

- [ ] 5.1 `NavigatorStateMachine`: states `Idle → L1_AwaitFirst → L1_AwaitSecond → L1_AwaitAction → L2_AwaitFirst → L2_AwaitSecond → L2_AwaitAction → L3_AwaitFirst → L3_AwaitSecond → L3_AwaitAction`; input events are `VKey` values; on completing a two-key pair cursor moves to cell center and machine enters `_AwaitAction`; in `_AwaitAction` an action `VKey` fires `ActionRequested(point, action)` and a navigation `VKey` starts the next level; Escape at L2/L3 goes back one level and moves cursor to parent cell center; Escape at L1 raises `Cancelled` with saved origin position for cursor restore; captures cursor position at `Activate()` call for restore-on-cancel; raises `ColumnHighlighted(col)`, `CellEntered(cell, level)`, `ActionRequested(point, action)`, `Cancelled(originPoint)`; when `NavigationMode` is `Arrow` or `Both`: arrow VKeys in any non-Idle state move a `selectedIndex` within the current grid level and raise `CellHighlighted(cell)`, `VK_RETURN` in any state fires `ActionRequested` at current cell with `LeftClick` (REQ-3–REQ-12, REQ-24) [after: 3.1, 3.2, 3.3]
- [ ] 5.3 `ArrowNavigator` helper (used by state machine when `NavigationMode` includes `Arrow`): tracks `selectedIndex` within a flat cell list for the current level; `MoveLeft/Right/Up/Down(currentIndex, cols)` → next index with wrap; stateless pure functions, fully unit-testable (REQ-24) [after: 3.1]
- [ ] 5.2 `ActionMapper`: config `ActionBindings` (`VKey` → `MouseAction`) + pressed `VKey` → `MouseAction` enum; Space `VKey` always maps to `LeftClick` if unbound (REQ-10) [after: 2.2]

## Phase 6: Overlay UI

- [ ] 6.1 `OverlayWindow`: WPF window `WindowStyle=None`, `AllowsTransparency=True`, `Topmost=True`, sized to `Screen.PrimaryScreen.Bounds` converted to DIPs via `PresentationSource`; keyboard focus captured on show; `Deactivated` event wired to `DeactivateOverlay()` for focus-loss handling (REQ-2, REQ-20) [after: none]
- [ ] 6.2 `GridRenderer` (WPF `DrawingVisual` / `Canvas`): draw cell borders + label text centered in cell; explicit `Children.Clear()` + visual child detach on each transition to prevent memory accumulation (REQ-3) [after: 6.1, 3.2]
- [ ] 6.3 On `ColumnHighlighted` event: reduce opacity of non-matching cells, highlight matching column (REQ-4) [after: 6.2, 5.1]
- [ ] 6.5 On `CellHighlighted` event (arrow navigation): render distinct highlight border/background on the currently selected cell without dimming others; clear previous highlight before drawing new one (REQ-24) [after: 6.2, 5.1]
- [ ] 6.4 On `CellEntered` event: clear level-N grid via `GridRenderer` clear, draw level-(N+1) subgrid within cell bounds (REQ-5, REQ-7) [after: 6.2, 3.3, 5.1]

## Phase 7: App Wiring & Tray

- [ ] 7.1 `App.xaml.cs` startup: `ShutdownMode=OnExplicitShutdown`; create `NotifyIcon` with tray icon and context menu ("About", "Open Configuration Folder", "Quit"); load config + logging; run `StartupValidator`; show tray notification(s) for all validation violations (REQ-15, REQ-14, REQ-22) [after: 2.2, 2.3, 2.10]
- [ ] 7.2 Wire `HotKeyService.Activated` → capture cursor origin → show `OverlayWindow` → call `KeyboardHookService.Enable()`; if `Enable()` returns failure → immediately call `DeactivateOverlay()` + show tray notification (REQ-1, REQ-2, REQ-21) [after: 4.1, 6.1, 4.2, 5.1]
- [ ] 7.3 Wire `KeyboardHookService.KeyPressed(VKey)` → `NavigatorStateMachine.OnKey(VKey)` (REQ-3–REQ-12) [after: 4.2, 5.1]
- [ ] 7.4 Wire `NavigatorStateMachine.ActionRequested` → `MouseActionService` (move + click) → `DeactivateOverlay()` (REQ-8, REQ-11) [after: 5.1, 4.3, 6.1]
- [ ] 7.4b Wire `NavigatorStateMachine.Cancelled(originPoint)` → `MouseActionService.MoveTo(originPoint)` → `DeactivateOverlay()` (REQ-12) [after: 5.1, 4.4, 6.1]
- [ ] 7.5 Wire `NavigatorStateMachine` state-change events → `OverlayWindow` visual updates (REQ-4, REQ-5, REQ-7) [after: 5.1, 6.3, 6.4]
- [ ] 7.6 "About" menu item → show small WPF window (`AboutWindow`): app name, version (from assembly), brief description, GitHub link; "Open Configuration Folder" → `Process.Start("explorer.exe", configFolderPath)` (REQ-15) [after: 7.1]
- [ ] 7.7 Implement `DeactivateOverlay()`: hide `OverlayWindow`, call `KeyboardHookService.Disable()`, reset `NavigatorStateMachine` to `Idle`; method is idempotent (safe to call multiple times); called from action, cancel, focus-loss, exception handler, and Quit (REQ-19) [after: 4.3, 6.1, 5.1]
- [ ] 7.8 `StartupRegistryService`: `IsEnabled()` reads `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Klikety`; `Enable()` writes value = `Environment.ProcessPath`; `Disable()` removes value; tray context menu item "Start with Windows" wired to toggle + checkmark refresh; checkmark set on tray menu creation from `IsEnabled()` (REQ-27) [after: 7.1]

## Phase 8: Tests

- [ ] 8.1 Unit tests: `GridCalculator`, `SubgridCalculator`, `LabelGenerator` — various screen sizes, 96/120/144/192 DPI, key set sizes; assert physical-pixel bounds and level-3 threshold trigger (REQ-16) [after: 3.1, 3.2, 3.3]
- [ ] 8.2 Unit tests: `ConfigLoader` — valid JSONC, missing file (defaults applied), partial config, malformed JSON, invalid key sets, key-binding conflicts (each violation type) (REQ-16) [after: 2.2]
- [ ] 8.3 Unit tests: `NavigatorStateMachine` — all state transitions with `VKey` inputs, Escape at each level (L3→L2→L1→closed with origin restore), action dispatch for each `MouseAction`, invalid/unbound `VKey` ignored; arrow navigation: `VK_LEFT/RIGHT/UP/DOWN` advance `selectedIndex` correctly at grid edges (wrap), `VK_RETURN` dispatches `ActionRequested` at current cell; `NavigationMode.TwoKey` suppresses arrow handling (REQ-16, REQ-24) [after: 5.1, 5.3]
- [ ] 8.3a Unit tests: `ArrowNavigator` — boundary wrapping for all directions, single-row/single-column edge cases, 1×1 grid (REQ-24) [after: 5.3]
- [ ] 8.3b Unit tests: `LabelGenerator` — QWERTY, DVORAK, and Colemak key sets produce correct display labels; `ToUnicode` failure (dead key) falls back to VKey name (REQ-25) [after: 3.2]
- [ ] 8.4 Define test fakes: `FakeKeyboardHookService.SimulateKey(VKey)`, `FakeMouseActionService` (records `(physicalX, physicalY, MouseAction)` calls), `FakeOverlayWindow` (tracks show/hide + rendered grid + focus-loss simulation) (REQ-17) [after: 4.1]
- [ ] 8.5 Integration tests — full wiring via `NavigatorCoordinator` with fakes injected; scenarios: (REQ-17) [after: 7.4, 8.4]
  - hotkey → overlay shown, L1 grid rendered
  - first `VKey` → `ColumnHighlighted`, non-matching cells dimmed
  - two L1 `VKey`s → cursor at L1 cell center (physical pixels), enters `L1_AwaitAction`, L2 subgrid rendered
  - action `VKey` at `L1_AwaitAction` → `FakeMouseActionService` receives L1 cell center coords + correct `MouseAction`, overlay closed
  - two L1 + two L2 `VKey`s → cursor at L2 cell center; action `VKey` → correct coords + action dispatched, overlay closed
  - navigation `VKey` at `L1_AwaitAction` → starts L2 navigation
  - Escape at `L1_AwaitFirst` → overlay closed, cursor restored to origin position
  - Escape at `L1_AwaitAction` → overlay closed, cursor restored to origin position
  - Escape at `L2_AwaitAction` → returns to L1 `AwaitAction` state, cursor moves to L1 cell center
  - focus-loss simulation → `DeactivateOverlay()` called, overlay closed, hook disabled
  - hook install failure → overlay immediately closed, tray notification shown
  - configured non-Space action `VKey` → correct `MouseAction` dispatched
  - invalid/unbound `VKey` at any state → no transition, no action
  - malformed config → defaults applied, validation violations reported
  - arrow VKeys move highlight in `NavigationMode.Both`; `VK_RETURN` dispatches action at highlighted cell
  - `NavigationMode.TwoKey` → arrow VKeys ignored, `VK_RETURN` ignored
  - arrow VKeys in `firstKeys` / `secondKeys` / `ActionBindings` → validation violation reported at startup
- [ ] 8.6 Smoke test project `src/Klikety.SmokeTests/` (excluded from CI via `[Trait("Category", "Smoke")]`): exercises real `HotKeyService`, `KeyboardHookService`, `MouseActionService` on a live display for manual verification [after: 4.2, 4.3, 4.4]
- [ ] 8.7 Write `README.md` at repo root (REQ-26): project description + feature list; screenshot/GIF placeholder; prerequisites (.NET 9 SDK, Windows 10+); build instructions (`dotnet build`, `dotnet publish -r win-x64 --self-contained`); installation (copy to `%LOCALAPPDATA%`, startup shortcut); configuration reference table (every `ConfigModel` field: type, default, valid values, example); theme customization (built-in themes, creating a custom `.theme.json`); keyboard layout setup section (DVORAK and Colemak `firstKeys`/`secondKeys` examples); troubleshooting (hotkey conflict, hook install failure, config validation errors, log file location) [after: 2.5, 7.1]
