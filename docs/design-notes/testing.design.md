---
description: Test infrastructure — unit test patterns, fake services, testing seams, and smoke tests.
globs:
  - src/Klikety.Tests/**
  - src/Klikety.SmokeTests/**
---

# Test Infrastructure

## Unit Tests

- **Unit tests** (`Klikety.Tests`): xUnit, 700+ tests covering `GridCalculator`, `SubgridCalculator`, `LabelGenerator`, `ConfigLoader`, `ConfigMigrator`, `NavigatorStateMachine`, `ArrowNavigator`, `NavigatorCoordinator` integration, `GridRenderer` threshold/fan-out logic, `CrosshairStateMachine`, `CrosshairSession`, `LogCrosshairStateMachine`, `LogCrosshairSession`, `LogGridCalculator`, `DynamicKeyReducer`, `AppScopeCoordinator` (chord activation, bounds clipping, drag reset, mode switching), and keyboard help binding/content/layout/lifecycle.
- **Smoke tests** (`Klikety.SmokeTests`): `[Trait("Category", "Smoke")]`, exercises real Win32 P/Invoke on a live display. Not CI-safe.
- `InternalsVisibleTo` in `Klikety.csproj` exposes `internal` types (e.g. `NativeMethods`) to both test projects.
- `NavigatorCoordinator` integration tests inject fakes and simulate full hotkey→key→action flows without any Win32 calls, except `NativeMethods.GetPrimaryScreenBounds()` which is called in `OnHotKeyActivated` — this works in tests because it's real Win32 (not mocked).
- `LogLevel` read from config.
- `ILogger<T>` injected into Win32 services, state machine, and loader classes.

## Test Fakes

- `FakeHotKeyService` — records Register/Unregister calls, allows manual `Activated` event firing.
- `FakeKeyboardHookService` (with `SimulateKey`) — programmatically injects key events.
- `FakeMouseActionService` — records `(physicalX, physicalY, MouseAction)` calls; configurable typed `Result` and asynchronous `DragResult` simulate failure or pending completion.
- `FakeOverlayWindow` — tracks show/hide/focus-loss, rendered grid state, `AppScopeBorderVisible`/`AppScopeBorderBounds`, and can simulate keyboard-layout change events.
- Renderer fakes record `RenderCall` lists and `RebuildLabelsCalls` so layout refresh and no-event redraw behavior remain hermetic.
- `FakeForegroundWindowProvider` — configurable `Handle`, `Bounds`, `Title`. Returns configured values for matching handle; empty/default for zero or mismatched handle.
- `FakeDisplayCatalog` — configurable `DisplayCatalogResult`. Default is one 1920×1080 display (`\\.\DISPLAY1` / `\\?\FAKE#PRIMARY`). Catalog matcher tests inject `EnumeratedMonitor` / `CcdTarget` rows; they do not call Win32.
- `FakeSatelliteOverlay` — records `Show(bounds, number)` for `OverlayHostTests`.
- `DisplayTopologyStore` tests inject a temp `display-topologies.json` path (same pattern as `MacroStore`).
- Help tests: `HelpBindingTests` cover matching/collision policy; `HelpBindingModelTests` cover effective command and contextual prompt projection; `HelpOverlayCoordinatorTests` cover close-only resume, dismiss-and-forward navigation/actions/macros, modifier-only input, latches, cleanup, and layout refresh; `HelpKeyboardLayoutTests` cover bounds, non-overlap, 800×600 fit, 75%-width scaling, centered Space/auxiliary rows, text floor, and contained scrolling; `HelpKeyLabelTests` cover layout-resolved printable/fallback labels, including visible Esc/Space and Win32 control-character fallbacks.
- `HelpOverlayRenderingTests` measure and arrange actual WPF cards on an STA thread without showing windows. They check full wrapped-text height, row/Space separation, default-content fit at 800x600 and larger, and that the help view draws command cards without empty navigation rectangles; geometry-only bounds are not sufficient evidence against text clipping.

All fakes live in `Klikety.Tests/Fakes/`.

## Testing Seam

Settings tests use temporary fixture files and never the user's AppData config.
`SettingsConfigStoreTests` cover targeted JSONC preservation, collection comments,
backup/conflict/guarded-restore behavior, malformed input, mode defaults, compatibility
warnings, and validation rejection. `SettingsDraftTests`, `SettingsWindowTests`, and
`AppPathsTests` cover changed/reverted values, page draft retention, injected apply
failure, seven-page construction, action-binding save/reopen, focused key capture, and
fixture-root path composition. The hook-free settings demo is not evidence of runtime
registration. `--settings-runtime-fixture` is the isolated native host for registration
and recovery checks; use it only after confirming the fixture path and test hotkeys.
Live tray reuse, display scaling, and the native layout matrix remain manual checks.
`SettingsApplyTests`/`SettingsRecoveryTests` exercise the same operation gate, candidate
resource/replacement engine, shortcut inventory and recovery transaction used by App.
The explicit editable-field map drives store and real editor round trips; focused
validation/isolation/capture/migration tests cover errors and compatibility boundaries.
`SettingsAccessibilityTests` checks names, collapsed Advanced and logical bounds without
showing a window; it is not native DPI evidence. WPF UI tests share one xUnit collection
to avoid competing focus checks.
Footer Close checks cover clean close, cancelled/confirmed dirty or pending-apply
close, unchanged disk/runtime on closing, accessible naming and Save-to-Close tab
order. Logical footer bounds include both Save and Close at each tested width.
`SettingsColorDialogTests` cover exact RGB/alpha-byte synchronization, invalid-input
blocking/repair, format-preserving unchanged selections, modal OK/Cancel, accessible
controls/logical bounds, and all four real editor pick/cancel/save round trips. Color
selection is injected for editor checks; no tests launch Explorer or register hooks.
`SettingsModifierPickerTests` exercise all sixteen flag combinations, individual
toggles, summary/UIA patterns, popup keyboard traversal/dismissal/cleanup and strict
save/reopen preservation. The real editor field map still covers main, scroll and
nullable macro modifier controls. `SettingsThemeTests` force Light/Dark/Light only on
the test windows (never the desktop), check actual palette/resources and retained
draft/config bytes, and verify unrelated windows retain their default theme mode.
These checks do not claim actual Windows-preference-change or native DPI evidence.
Theme regression coverage includes enabled checkbox/expander/button/dropdown/input
text across every page and color-dialog labels/inputs/buttons, not only window colors.
It verifies actual Fluent button backgrounds and composited text contrast >= 4.5:1
in Light/Dark/Light while preserving the draft. Popup cleanup and layout tests still
cover control-template changes.
`SettingsTrayTests` checks the actual tray-item factory's label/accessibility and
Click callback without creating a taskbar icon or global runtime. `AppPathsTests`
checks the production/fixture/demo Settings path resolver, including a nonstandard
demo filename, without reading or writing the user's config. These are command/path
contracts, not proof of actual shell tray reuse or live runtime activation.
See [settings.design.md](settings.design.md) for the fixture-only fault controls and
native operator procedure.
The completed Settings plan retains genuine Sandbox tray/save/real-registration/
recovery results separately from managed evidence. Native interaction/DPI rows blocked
by guest `SendInput` error 5 or unavailable scaling were explicitly deferred to the
user, not passed. Do not report the separate live smoke suite as run or green from
the ordinary `Klikety.Tests` result.

- All Win32 service interfaces are the seam for testing.
- Integration tests are hermetic (no real display, no OS hooks, no timing dependencies).
- Smoke test project (`Klikety.SmokeTests`, `[Trait("Category","Smoke")]`) excluded from CI; covers real Win32 call verification.
- `FakeInputSender` captures native batches and supplies per-call requested/sent/error outcomes; virtual desktop and `IDelayProvider` are injected. `MouseInputSuccessTests` assert unchanged layout, geometry, modifier/button order, wheel data and 100/50 ms drag phases. `MouseInputFailureTests` exercise every incomplete click/scroll/drag prefix, release-only compensation, suppression and failed cleanup.
- `MacroCancellationTests`, `MacroInputFailureTests` and `MacroDragSequencingTests` verify dispatch/progress boundaries and completion-relative intervals. `InputFailureCallerTests` cover non-macro logging, async faults and unchanged recording contents/restoration.
- `ClickIndicatorLifecycleTests` use a queued fake dispatcher and view, not WPF/STA, to verify hide/unsubscribe, cancellation/completion races, cancellation before queued start, dispatcher-owned disposal and stale completions after reuse. `MacroPlaybackTeardownTests` hold a pending indicator to prove nonblocking disposal, shutdown cleanup without context pumping, marshalled progress/restoration and late-completion isolation during reload.
