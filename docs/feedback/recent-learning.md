# Recent learning

Source plan: `31191b keybinding-help-overlay`
Source commit: `b3c20e2645d2ce3448ecea8c36ab0dda3825f10c`

## Lessons

- Measure wrapped WPF command cards before placing rows; fixed key heights do not prove that text fits. — `src/Klikety.Tests/HelpOverlayRenderingTests.cs`
- Latch help and Escape as close-only inputs; dismiss and forward other non-modifier keys through the existing dispatcher once. — `src/Klikety/NavigatorCoordinator.cs`
- Check staggered-row extents and exact boundary tests before accepting a review claim about keyboard width. — `src/Klikety.Tests/HelpKeyboardLayoutTests.cs`
- Add default keyboard bindings through collision-aware versioned migration without stealing configured commands. — `src/Klikety/Config/ConfigMigrator.cs`
