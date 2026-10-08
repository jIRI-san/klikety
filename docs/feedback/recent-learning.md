# Recent learning

Source plan: `0b37c2 native-settings-sidebar`
Source commit: `e0991863cbccf4facb0339f6b87a41670e7e523a`

## Lessons

- Capture disk and active runtime baselines independently; permit explicit retry after stable disk restoration and require reload after disk divergence. — `src/Klikety/Config/SettingsSaveTransaction.cs`
- Retain failed native cleanup ownership and quiesce hook consumer dispatch before retrying disposal. — `src/Klikety/Services/KeyboardHookService.cs`
- Native observations and unsupported input/display rows are distinct from managed tests; user-authorized deferrals must be reconfirmed and committed before finalization. — `docs/implementation-plans/standalone-2026-10-04-0b37c2-native-settings-sidebar/assets/intent.md`
