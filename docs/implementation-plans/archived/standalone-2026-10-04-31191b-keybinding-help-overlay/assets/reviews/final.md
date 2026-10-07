## Source

95eb0782e1df8e0a7432ede8246fe40a08eaf75b

## Scope

- README.md
- docs/design-notes/.design-notes.md
- docs/design-notes/config.design.md
- docs/design-notes/grid-rendering.design.md
- docs/design-notes/keybinding-help.design.md
- docs/design-notes/macros.design.md
- docs/design-notes/state-machine.design.md
- docs/design-notes/testing.design.md
- docs/design-notes/win32-interop.design.md
- docs/implementation-plans/standalone-2026-10-04-31191b-keybinding-help-overlay/assets/decisions.md
- docs/implementation-plans/standalone-2026-10-04-31191b-keybinding-help-overlay/assets/design.md
- docs/implementation-plans/standalone-2026-10-04-31191b-keybinding-help-overlay/assets/domain.md
- docs/implementation-plans/standalone-2026-10-04-31191b-keybinding-help-overlay/assets/intent.md
- docs/implementation-plans/standalone-2026-10-04-31191b-keybinding-help-overlay/assets/references.md
- docs/implementation-plans/standalone-2026-10-04-31191b-keybinding-help-overlay/assets/requirements.md
- docs/implementation-plans/standalone-2026-10-04-31191b-keybinding-help-overlay/assets/reviews/final.md
- docs/implementation-plans/standalone-2026-10-04-31191b-keybinding-help-overlay/assets/risks.md
- docs/implementation-plans/standalone-2026-10-04-31191b-keybinding-help-overlay/plan.md
- src/Klikety.Tests/ConfigLoaderTests.cs
- src/Klikety.Tests/ConfigMigratorTests.cs
- src/Klikety.Tests/CoordinatorTestHelper.cs
- src/Klikety.Tests/Fakes/TestFakes.cs
- src/Klikety.Tests/HelpBindingTests.cs
- src/Klikety.Tests/HelpKeyLabelTests.cs
- src/Klikety.Tests/HelpKeyboardLayoutTests.cs
- src/Klikety.Tests/HelpOverlayContentTests.cs
- src/Klikety.Tests/HelpOverlayCoordinatorTests.cs
- src/Klikety.Tests/HelpOverlayRenderingTests.cs
- src/Klikety/Config/ConfigLoader.cs
- src/Klikety/Config/ConfigMigrator.cs
- src/Klikety/Config/ConfigModel.cs
- src/Klikety/Config/HelpBindingPolicy.cs
- src/Klikety/Grid/Win32KeyLabelResolver.cs
- src/Klikety/Navigation/ActionMapper.cs
- src/Klikety/Navigation/MacroHandler.cs
- src/Klikety/Navigation/MacroRecorder.cs
- src/Klikety/NavigatorCoordinator.cs
- src/Klikety/Overlay/HelpKeyboardLayout.cs
- src/Klikety/Overlay/HelpOverlayContent.cs
- src/Klikety/Overlay/OverlayWindow.xaml
- src/Klikety/Overlay/OverlayWindow.xaml.cs
- src/Klikety/Resources/config.json
- src/Klikety/Resources/config.schema.json
- src/Klikety/Services/KeyboardHookService.cs
- src/Klikety/Services/ServiceInterfaces.cs

## Completed tasks

- [x] Replacement CR: binding, versioned migration, collisions, hook modifier snapshots — complete
- [x] Replacement CR: keyup, latches, close-only preservation, exactly-once forwarding, macro/global-hotkey lifecycle — complete
- [x] Replacement CR and deterministic adjudication: effective content, measured WPF layout, tests, compaction, documentation — complete
- [x] Native initial verdict was findings, with task 3 failed: sole P2 rejected by missing 0.58-unit third-row stagger. Both extents are center +/- 8.58 units, span 17.16 units. Exact 75%-width theory passed all 3 cases; no src changes since full tested build. No retry or code change required. — complete

## Findings

None.

## Verdict

clean
