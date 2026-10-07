---
description: Overlay-local keyboard help binding, effective command projection, split keyboard layout, and reversible pause/resume behavior.
globs:
  - src/Klikety/Config/HelpBindingPolicy.cs
  - src/Klikety/Config/ConfigModel.cs
  - src/Klikety/Config/ConfigMigrator.cs
  - src/Klikety/NavigatorCoordinator.cs
  - src/Klikety/Services/KeyboardHookService.cs
  - src/Klikety/Overlay/Help*.cs
  - src/Klikety/Overlay/OverlayWindow.xaml*
  - src/Klikety/Navigation/MacroHandler.cs
---

# Keyboard Help Overlay

## Binding and Hook Events

- `helpBinding` defaults to enabled `OemQuestion`, with Shift optional. Ctrl, Alt, and Win are rejected. The key is configurable and can require Shift.
- `ConfigMigrator` advances config version 7 to 8. It adds the default only when collision-free; a collision persists a disabled binding and migration warning. Existing settings and unknown JSON fields are preserved. Invalid explicit VKeys or collisions remain ineffective at runtime and are logged; runtime never silently rebinds.
- Collision checks include the implicit Space left-click, configured actions/navigation/modes/scope/macros, reserved keys, and overlapping unmodified/Shift-only global hotkey chords. Ctrl/Alt/Win global chords cannot match the overlay help binding.
- `HookModifierFlags` is a separate flags enum from `ActionModifiers`. `KeyHookEventArgs` retains a compatible optional modifier argument. `KeyboardHookService` captures modifier state in the hook callback, including Win, and posts both key-down and key-up events to the UI dispatcher.

## Dispatch and State Preservation

- Help is a presentation layer owned by `NavigatorCoordinator`, not a state in `NavigatorStateMachine`. Opening requires a visible overlay and active session; activation-period input cannot open it.
- The help binding is checked before display, macro, app-scope, mode, and session commands. While help is visible, overlay key-downs do not reach those handlers; only the help binding and Escape close it. Help/Escape keys are latched until key-up. Key-up updates debounce and latch state, then returns without toggling.
- The overlay renders help on a separate `HelpCanvas`; closing it only hides that canvas, preserving session identity, selection, cursor, scope, drag and recorder state. Recording time continues and opening help does not create a macro step.
- `ClearHelpState()` is idempotent and clears the visible flag and latches before hide, deactivation, disposal, or macro suspension. The global macro picker uses the existing hook-disable/suspend/resume handoff and does not expose help in picker or playback windows.

## Effective Content

`HelpOverlayContentBuilder` projects the effective non-navigation commands from configuration and runtime state:

- Explicit action bindings plus `ActionMapper`'s implicit/overridden Space behavior.
- Enabled mode chords, app-scope chord, assigned display numbers, help/Escape, and macro record/helper/slot keys.
- Current drag target meanings; recording slot, overwrite, and start-from-cursor prompts; macro slot names; and reasons for commands that are temporarily unavailable.
- Navigation keys are unlabeled anchors. Custom command keys outside the keyboard diagram are placed in an auxiliary strip. Disabled features are omitted.

The separate macro global hotkey remains an OS-level command, not an overlay-local key and not part of the paused help dispatch.

## Layout and Labels

- `HelpKeyboardLayout.Compute` creates a canonical split-keyboard approximation with function/digit rows above, staggered left/right halves around the active display center, and Space below the center gap.
- Keyboard width targets 75% of the active display's DIP width and shrinks when height is limiting. Keycaps and command text scale together, with a 40-DIP key-unit floor preserving readability at 800x600. Space is a wide keycap centered on the display; each auxiliary row (including Escape and macro controls) is centered beneath the keyboard instead of anchored to the screen's left edge.
- Positions use active overlay-window DIP dimensions. `OverlayWindow` uses a contained `ScrollViewer` for viewports below 800×600 DIP or content exceeding the viewport. Command labels render at a 12-DIP minimum.
- The help canvas remains hit-testable only so its contained `ScrollViewer` can receive wheel/scrollbar input; it has no command click handlers and cannot dispatch navigation or actions.
- Printable glyphs come from the active `IKeyLabelResolver`; control/whitespace translations use readable VKey-name fallbacks. Help explicitly labels Escape as `Esc` and Space as `Space`, never an invisible character. No slash glyph is forced across layouts.
- Keyboard-layout refresh rebuilds visible help labels. One viewport event covers size and DPI changes and only relayouts the help view. Satellites and separate picker/playback windows do not render help.
- Category accents use the current theme; unavailable commands are visually muted while their full reason remains in the prompt area.

## Test Seams

`HelpBindingTests`, `HelpBindingModelTests`, `HelpOverlayCoordinatorTests`, `HelpKeyboardLayoutTests`, and `HelpKeyLabelTests` use the existing fake hook, overlay, platform, and key-label resolver seams. Live keyboard/focus/display/DPI checks remain an explicit manual verification gate.
