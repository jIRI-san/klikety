# Requirements

| ID | Requirement | Acceptance Criteria | Phases/Steps |
|----|-------------|---------------------|--------------|
| REQ-1 | Visible overlay labels use the active keyboard layout and update without resetting the navigation session. | `test:NavigatorCoordinatorTests.KeyboardLayoutChange_RebuildsLabelsAndRedrawsSession` · `test:LabelGeneratorTests.Rebuild_UsesNewResolverAndUpdatesLookup` · `file:src/Klikety/Overlay/OverlayWindow.xaml.cs#contains:WM_INPUTLANGCHANGE` | 1.1, 1.2, 1.3, 2.1, 2.2, 2.4, 3.1, 3.2, 4.1, 4.2 |
| REQ-2 | The key-press HUD uses the current layout for the first printable key after a switch. | `test:KeyPressProcessorTests.LayoutChange_RebuildsCacheBeforeResolvingKey` · `file:src/Klikety/Services/KeyPressProcessor.cs#contains:IKeyboardLayoutProvider` | 2.3, 2.4, 4.1, 4.2 |
| REQ-3 | Layout refresh redraws only the current visual state and emits no cursor, action, or cancel event. | `test:CrosshairSessionTests.Redraw_ReplaysCurrentVisualWithoutEvents` · `test:LogGridSessionTests.Redraw_FirstKeyStateReplaysIndicatorWithoutEvents` · `file:src/Klikety/Navigation/SessionManager.cs#contains:RedrawActiveSession` | 3.1, 3.2, 3.3, 4.1, 4.2 |
| REQ-4 | The overlay and HUD key paths perform at most one active-HKL read per key-down and do not poll. | `test:KeyPressProcessorTests.LayoutChange_RebuildsCacheBeforeResolvingKey` · `file:src/Klikety/Services/KeyPressProcessor.cs#contains:GetActiveKeyboardLayout` | 1.2, 2.2, 2.3 |
| REQ-5 | A missed `WM_INPUTLANGCHANGE` is recovered on the next overlay key-down. | `test:NavigatorCoordinatorTests.KeyboardLayoutChange_KeyEventFallbackRefreshesLabels` · `file:src/Klikety/NavigatorCoordinator.cs#contains:RefreshKeyboardLayoutIfChanged` | 2.1, 2.2, 2.4 |
