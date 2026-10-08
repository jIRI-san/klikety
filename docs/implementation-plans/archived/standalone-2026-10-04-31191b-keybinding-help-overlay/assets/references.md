# References

- Current operator workshop and pre-draft scope confirmation, 2026-10-04. Session scratch proposal is draft context, not completed work.
- Current starting branch: jiri-san-keybinding-help-overlay; HEAD d424a32212c955bae231f30f9d047e82e5d9c259; dedicated Klikety worktree.
- Filtered Get-PlanIndex search on 2026-10-04: keybinding.help, keyboard.help, help.overlay, non.navigation; zero candidates and no errors. No historical artifacts selected or loaded. This bounded search is not proof of no other history.
- docs/design-notes/.design-notes.md: governance and note-update requirements.
- docs/design-notes/state-machine.design.md, navigation-modes.design.md: lifecycle, session ownership and dispatch.
- docs/design-notes/config.design.md: collision/migration and user-file preservation.
- docs/design-notes/grid-rendering.design.md: window-local DIP geometry, theme and layer ownership.
- docs/design-notes/win32-interop.design.md: input and modifier boundaries. Current KeyboardHookService is authoritative where prose lags suppression behavior.
- docs/design-notes/macros.design.md, testing.design.md, dev-rules.design.md: recorder prompts, fake-based tests, tooling.
- src/Klikety/NavigatorCoordinator.cs: current help insertion point before display digits/MacroHandler; macro suspend/resume and idempotent deactivate.
- src/Klikety/Services/ServiceInterfaces.cs and KeyboardHookService.cs: hook event currently key/direction only; regular keys suppressed, system keys passed through.
- src/Klikety/Overlay/OverlayWindow.xaml and .xaml.cs: RootCanvas/StatusCanvas, focus and hide cleanup.
- src/Klikety/Resources/config.json, Config/ConfigModel.cs, Config/ConfigLoader.cs: default actions/modes/macros, implicit Space and current validation.
- src/Klikety/Grid/IKeyLabelResolver.cs and Win32KeyLabelResolver.cs: active-layout unshifted glyph resolution.
- .github/skills/cip/assets/decision-protocol.md, pre-confirmation-review.md, plan-template.md, model-aliases.psd1: planning process.
