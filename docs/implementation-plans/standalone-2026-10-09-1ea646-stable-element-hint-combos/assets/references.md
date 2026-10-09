# References

- Baseline: `b9bbe51`, merged progressive hints, cache and helper-recovery changes.
- [Element hints design](../../../design-notes/element-hints.design.md):
  progressive stable-within-session labels, capacity hierarchy and bounded helper.
- [Testing design](../../../design-notes/testing.design.md): regression conventions.
- `src/Klikety/Navigation/ElementHintsSession.cs`: index-derived labels/key lookup,
  progressive append, level schemes, relayout and teardown.
- `src/Klikety/Navigation/ElementHintHierarchy.cs`: groups depend on known members.
- `src/Klikety/Navigation/ModeSessionFactory.cs`: shared owner across sessions.
- `src/Klikety/Automation/ElementHintProtocol.cs`: current identities and caps.
- [UIA runtime-ID contract](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.automationelement.getruntimeid?view=windowsdesktop-10.0):
  opaque comparison IDs can be reused over time; not persistent semantic IDs.
