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

Duplicate-policy regressions replay the Copilot patternless ListItem/full-size
Invoke-only Button metadata and physical preview point. They preserve independent
nested buttons/links/editors/toggles, own-pattern rows, standalone patternless
rows, coincident siblings, cross-process and unequal-full-bound cases. Failed,
invalid and truncated branches retain uncertain wrapper hints. Canonicalization
keeps physical ordering and unique original tokens, with no extra provider reads.
The live evidence uses only captured foreground-root identities, ancestry, types,
capabilities, bounds and bounded passive hit points in an owned MTA child under
the existing watchdog. No accessibility names/values, host screenshots, target
settings changes or input injection are used. This is provider metadata/point
evidence, not observed physical-input or universal duplicate elimination.

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
