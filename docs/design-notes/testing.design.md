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
- `FakeOverlayWindow` — tracks show/hide/focus-loss, rendered grid state, `AppScopeBorderVisible`/`AppScopeBorderBounds`.
- `FakeGridRenderer` — records `RenderCall` list (method name, cells, col) for assertion.
- `FakeForegroundWindowProvider` — configurable `Handle`, `Bounds`, `Title`. Returns configured values for matching handle; empty/default for zero or mismatched handle.
- `FakeDisplayCatalog` — configurable `DisplayCatalogResult`. Default is one 1920×1080 display (`\\.\DISPLAY1` / `\\?\FAKE#PRIMARY`). Catalog matcher tests inject `EnumeratedMonitor` / `CcdTarget` rows; they do not call Win32.
- `FakeSatelliteOverlay` — records `Show(bounds, number)` for `OverlayHostTests`.
- `DisplayTopologyStore` tests inject a temp `display-topologies.json` path (same pattern as `MacroStore`).

All fakes live in `Klikety.Tests/Fakes/`.

## Testing Seam

- All Win32 service interfaces are the seam for testing.
- Integration tests are hermetic (no real display, no OS hooks, no timing dependencies).
- Smoke test project (`Klikety.SmokeTests`, `[Trait("Category","Smoke")]`) excluded from CI; covers real Win32 call verification.
