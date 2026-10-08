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
`UiaTreeAlgorithmsTests` link the worker's production kernel without importing UIA
into the parent/tests. Synthetic trees exercise exact node/depth/target caps,
nested actions/passive text, cross-process descendants, identity omissions/reuse
and bounded covered-point/ancestry checks. Coordinator/session tests explicitly
cover display-topology events, config-disposal, focus-loss and late discovery/action
completion. STA rendering tests include 1x1-DIP containment and longer glyphs
without remapping frozen pages.
Rendering regressions also cover a top-edge collision without global list
fallback, no unselected connectors/outlines, selected-only displacement guides,
bounded severe-crowding presentation and page capacity that fits the narrower
list. Geometry tests inject their coordinate space rather than using host DPI.
`Klikety.WorkerFixture` is test-only; production has no hang command. Tests use fake
service/point-guard seams for deterministic action delivery.

`ElementHintSmokeTests` launch a controlled WPF fixture and the real bundled helper.
They require an interactive desktop that allows the fixture to become foreground;
`StaleTarget: Application lost foreground` is a fail-closed environmental blocker,
not permission to remove the foreground check. Physical mouse-input, third-party,
mixed-display and actual mixed-DPI coverage remain plan 4af565's human gate.
Fixture readiness follows `ContentRendered`, with ordinary activation return values
and native foreground HWND diagnostics. Root-only discovery/foreground-loss and
destroyed-window tests do not require acquiring foreground. Keep healthy/covered
cases failing if normal activation is denied; do not skip or weaken them. The
development packaging test includes typed outcome/timing diagnostics and retires
its helper even when the assertion fails.

Visual evidence for the clutter follow-up comes from an offline Windows Sandbox,
with only a read-only test package/config/harness mapping and a narrow writable
capture mapping. Guest-only keyboard activation/selection targets a harmless
WinForms fixture; no action keys or host input are sent. Screenshots, native
screen geometry, fixture bounds and application logs are actual guest evidence,
not replaced by WPF containment tests. Guest app readiness/foreground ownership
must be checked before treating a capture as an overlay; fixture-only or timed-out
captures are not successful label evidence. The observed 200% before/after and
bounded unsuccessful final-package attempts are documented in plan 4af565.
The final-code inspection subsequently used a separate .NET Framework WinForms
process with a real `Application.Run` loop. A bounded MTA test helper initialized
only this fixture's root/control-view metadata before the production scan.
Successful 48-target final captures are warm-condition evidence, not a
production timeout fix. The harness checks process handles before observing
Windows PowerShell 5.1 redirected-child exit codes, and retains raw geometry
arrays instead of its pipeline's `value`/`Count` JSON wrapper.

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
