# References

## Repository evidence

- `docs/design-notes/.design-notes.md`: governance and required documentation maintenance.
- `docs/design-notes/navigation-modes.design.md`: mode/factory interface and app-scope overview. Its primary-only/non-goal and some per-mode key details are stale; current code plus state/rendering notes establish multi-display/shared-axis behavior for this plan.
- `docs/design-notes/state-machine.design.md`: coordinator/session/action ownership, display host lifecycle, help, and drag.
- `docs/design-notes/config.design.md`: JSONC defaults, version 8 migration, atomic persistence, validation, and notifications.
- `docs/design-notes/grid-rendering.design.md`: overlay-window-local DIP geometry, outlined labels, external-label helpers, and help layer.
- `docs/design-notes/win32-interop.design.md`: nonblocking keyboard hook, physical SendInput, target HWND capture, modifiers, DPI/display services.
- `docs/design-notes/testing.design.md` and `dev-rules.design.md`: hermetic fakes, live smoke-test separation, formatting, and targeted staging.
- `src/Klikety/Navigation/IModeSession.cs`, `ModeSessionFactory.cs`, `SessionManager.cs`: synchronous activation contract, hardcoded four-mode factory, all create/restart/resume paths.
- `src/Klikety/NavigatorCoordinator.cs`: pre-overlay HWND, chord/default-mode lists, broad non-QWERTY gate, help/display/macro dispatch precedence, teardown, action and recording handoff.
- `src/Klikety/Navigation/ActionDispatcher.cs`: bounds validation, modifier sampling, drag, and recording side effects.
- `src/Klikety/Config/ConfigModel.cs`, `ConfigLoader.cs`, `ConfigMigrator.cs`, `HelpBindingPolicy.cs`, `Resources/config.json`: version 8, shared horizontal/vertical VKeys, four-mode validation arrays/defaults and collision policy.
- `src/Klikety/Overlay/HelpOverlayContent.cs`, `src/Klikety/App.xaml.cs`: effective command projection, renderer/service construction, notification/bootstrap/disposal routes.
- `src/Klikety/Klikety.csproj`, `src/Klikety/app.manifest`, `Klikety.slnx`, `.github/workflows/release.yml`, `README.md`: .NET 10 Windows/WPF, PerMonitorV2, self-contained win-x64 app publish and ZIP distribution.
- Current help plan `docs/implementation-plans/standalone-2026-10-04-31191b-keybinding-help-overlay/plan.md`: observed phase/asset/evidence conventions. Current code wins over stale paused-help criteria in that draft.

## Official Windows/UIA evidence

Consulted 2026-10-07. Windows UIA API guidance applies to detection/threading/coordinates; older managed .NET Framework pages do not prove .NET 10 assembly/reference availability.

- [Windows UI Automation](https://learn.microsoft.com/en-us/windows/win32/winauto/entry-uiauto-win32): supported OS accessibility API, not screenshot recognition.
- [Control patterns overview](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-controlpatternsoverview): capabilities describe Invoke/Scroll/etc.; no single universal clickable flag.
- [Threading issues](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-threading): separate windowless MTA thread for desktop UIA; apartment lifetime affects cross-bitness element lifetime.
- [UIA and screen scaling](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-and-screen-scaling): UIA bounding rectangles/clickable points use physical coordinates.
- [Standard-control support](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-support-for-standard-controls): standard WPF/Win32/WinForms providers; custom/legacy support is not universal.
- [Assistive-technology security](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-securityoverview): UIAccess/integrity-level restrictions; normal UIA discovery/input is not a privilege bypass.
- [Chromium accessibility overview](https://chromium.googlesource.com/chromium/src/+/main/docs/accessibility/overview.md): accessibility trees bridge browser content to OS APIs; app/browser/provider behavior must be checked live.

## Planned verification commands

Run only during implementation, starting with the suites touched by that phase. Exact new test selectors may follow final class names.

```powershell
dotnet build Klikety.slnx
dotnet test src\Klikety.Tests\Klikety.Tests.csproj --filter "FullyQualifiedName~ElementHint|FullyQualifiedName~UiaWorker"
dotnet test src\Klikety.Tests\Klikety.Tests.csproj --filter "FullyQualifiedName~Config|FullyQualifiedName~Coordinator|FullyQualifiedName~Help|FullyQualifiedName~Macro"
dotnet format Klikety.slnx --verify-no-changes
dotnet publish src\Klikety\Klikety.csproj -r win-x64 --self-contained -c Release
dotnet test src\Klikety.SmokeTests\Klikety.SmokeTests.csproj --filter "FullyQualifiedName~ElementHint"
```

The final smoke selector requires live display/fixture infrastructure and is not evidence from CI. Release verification additionally checks/executes the worker inside the extracted publish/ZIP folder without using build-output paths.
