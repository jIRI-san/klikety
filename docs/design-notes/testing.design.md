---
description: Test infrastructure — unit test patterns, fake services, testing seams, and smoke tests.
globs:
  - src/Klikety.Tests/**
  - src/Klikety.SmokeTests/**
---

# Test Infrastructure

## Element-hint evidence

`ElementHintProtocolTests`, `UiaWorkerSupervisorTests`, `ElementHintsConfigTests`,
`ElementHintsStateMachineTests`, `ElementHintsCoordinatorTests` and actual STA
`ElementHintRenderingTests` cover bounded framing, hung/crashed/malformed children,
parent-crash job cleanup, cancellation/recovery, labels, config, hidden-overlay
validation, original modifiers, late-result rejection and rendered containment.
`Klikety.WorkerFixture` is test-only; production has no hang command. Tests use fake
service/point-guard seams for deterministic action delivery.

`ElementHintSmokeTests` launch a controlled WPF fixture and the real bundled helper.
They require an interactive desktop that allows the fixture to become foreground;
`StaleTarget: Application lost foreground` is a fail-closed environmental blocker,
not permission to remove the foreground check. Physical mouse-input, third-party,
mixed-display and actual mixed-DPI coverage remain plan 4af565's human gate.

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
- `FakeMouseActionService` — records `(physicalX, physicalY, MouseAction)` call list.
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

- All Win32 service interfaces are the seam for testing.
- Integration tests are hermetic (no real display, no OS hooks, no timing dependencies).
- Smoke test project (`Klikety.SmokeTests`, `[Trait("Category","Smoke")]`) excluded from CI; covers real Win32 call verification.
