# References

- Source snapshot: d424a32212c955bae231f30f9d047e82e5d9c259; initial dirty path docs/repository-maintenance.md; current worktree is the session's jiri-san-improved-umbrella checkout, branch jiri-san-repository-cleanup-survey.
- docs/repository-maintenance.md: RCS-MACRO-CANCEL-INDICATOR, RCS-MOUSE-PARTIAL-SEND, RCS-MACRO-TIMING-CONTRACT, RCS-PLAN-000021-IDENTITY.
- docs/design-notes/.design-notes.md: documentation maintenance and strongly typed composition guidance.
- docs/design-notes/macros.design.md: cancellation, indicator, timing and recording lifecycle; observed timing wording conflict is superseded by current operator interval choice.
- docs/design-notes/win32-interop.design.md: normalization/modifier conventions; drag description is stale relative to the current phased implementation and is updated within the changed input contract.
- docs/design-notes/testing.design.md: hermetic fakes and separate live-display smoke tests.
- src/Klikety/Navigation/MacroPlayer.cs, MacroHandler.cs and ActionDispatcher.cs; src/Klikety/Overlay/ClickIndicatorWindow.cs; src/Klikety/Services/MouseActionService.cs and ServiceInterfaces.cs.
- src/Klikety.Tests/MacroPlayerTests.cs, MacroRecorderTests.cs and MouseNormalizationTests.cs; src/Klikety.Tests/Fakes/TestFakes.cs.
- Filtered planning index: macro, SendInput, keyboard-layout, 000021 and RelativeTimeMs; 8 matched records, 1 active/7 archived, no parse errors. The active/archive 000021 collision is not a parse error.
- Selected historical reader attempts: legacy 015/017 rejected as noncanonical; 000015/000017 rejected as nonunique in inventory. No accepted historical artifacts; do not treat these records as consumed authority.
- Runtime: app and tests target net10.0-windows; WPF; xUnit 2.9.2; existing Microsoft.Extensions.Logging. No consequential version-specific external uncertainty required web research.
