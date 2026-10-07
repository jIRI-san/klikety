---
description: Test infrastructure — unit test patterns, fake services, testing seams, and smoke tests.
globs:
  - src/Klikety.Tests/**
  - src/Klikety.SmokeTests/**
---

# Test Infrastructure

## Unit Tests

- **Unit tests** (`Klikety.Tests`): xUnit, 700+ tests covering `GridCalculator`, `SubgridCalculator`, `LabelGenerator`, `ConfigLoader`, `ConfigMigrator`, `NavigatorStateMachine`, `ArrowNavigator`, `NavigatorCoordinator` integration, `GridRenderer` threshold/fan-out logic, `CrosshairStateMachine`, `CrosshairSession`, `LogCrosshairStateMachine`, `LogCrosshairSession`, `LogGridCalculator`, `DynamicKeyReducer`, `AppScopeCoordinator` (chord activation, bounds clipping, drag reset, mode switching).
- **Smoke tests** (`Klikety.SmokeTests`): `[Trait("Category", "Smoke")]`, exercises real Win32 P/Invoke on a live display. Not CI-safe.
- `InternalsVisibleTo` in `Klikety.csproj` exposes `internal` types (e.g. `NativeMethods`) to both test projects.
- `NavigatorCoordinator` integration tests inject fakes and simulate full hotkey→key→action flows without any Win32 calls, except `NativeMethods.GetPrimaryScreenBounds()` which is called in `OnHotKeyActivated` — this works in tests because it's real Win32 (not mocked).
- `LogLevel` read from config.
- `ILogger<T>` injected into Win32 services, state machine, and loader classes.

## Test Fakes

- `FakeHotKeyService` — records Register/Unregister calls, allows manual `Activated` event firing.
- `FakeKeyboardHookService` (with `SimulateKey`) — programmatically injects key events.
- `FakeMouseActionService` — records `(physicalX, physicalY, MouseAction)` call list.
- `FakeOverlayWindow` — tracks show/hide/focus-loss, rendered grid state, `AppScopeBorderVisible`/`AppScopeBorderBounds`, and can simulate keyboard-layout change events.
- Renderer fakes record `RenderCall` lists and `RebuildLabelsCalls` so layout refresh and no-event redraw behavior remain hermetic.
- `FakeForegroundWindowProvider` — configurable `Handle`, `Bounds`, `Title`. Returns configured values for matching handle; empty/default for zero or mismatched handle.
- `FakeDisplayCatalog` — configurable `DisplayCatalogResult`. Default is one 1920×1080 display (`\\.\DISPLAY1` / `\\?\FAKE#PRIMARY`). Catalog matcher tests inject `EnumeratedMonitor` / `CcdTarget` rows; they do not call Win32.
- `FakeSatelliteOverlay` — records `Show(bounds, number)` for `OverlayHostTests`.
- `DisplayTopologyStore` tests inject a temp `display-topologies.json` path (same pattern as `MacroStore`).

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
See [settings.design.md](settings.design.md) for the fixture-only fault controls and
native operator procedure.

- All Win32 service interfaces are the seam for testing.
- Integration tests are hermetic (no real display, no OS hooks, no timing dependencies).
- Smoke test project (`Klikety.SmokeTests`, `[Trait("Category","Smoke")]`) excluded from CI; covers real Win32 call verification.
