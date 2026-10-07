# Recent learning

Source plan: `31191b keybinding-help-overlay`
Source commit: `a4a4e35fef9631565c8c60c9efd4c43febe3158e`

## Lessons

- Measure wrapped WPF command cards before placing rows; fixed key heights do not prove that text fits. — `src/Klikety.Tests/HelpOverlayRenderingTests.cs`
- Latch help and Escape as close-only inputs; dismiss and forward other non-modifier keys through the existing dispatcher once. — `src/Klikety/NavigatorCoordinator.cs`
- Check staggered-row extents and exact boundary tests before accepting a review claim about keyboard width. — `src/Klikety.Tests/HelpKeyboardLayoutTests.cs`
- Add default keyboard bindings through collision-aware versioned migration without stealing configured commands. — `src/Klikety/Config/ConfigMigrator.cs`
