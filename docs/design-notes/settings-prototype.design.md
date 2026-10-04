---
description: Workshop-only native WPF settings sidebar, draft editing, targeted JSONC persistence, and isolated demo.
globs:
  - src/Klikety/SettingsWindow.*
  - src/Klikety/Config/SettingsConfigStore.cs
  - src/Klikety.Tests/SettingsConfigStoreTests.cs
---

# Settings Sidebar Workshop Prototype

This is one approved exploratory vertical slice, not production delivery. Native WPF,
850 x 650 DIP, single reusable modeless window opened by the real tray **Settings...**
entry. Common controls precede collapsed Advanced expanders; pages scroll independently
of the persistent sidebar/footer.

## Implemented scope

- General: activation modifiers and trigger-key picker.
- Navigation: enabled modes, default mode, arrow/two-key toggles, mode chord pickers,
  optional app-scope chord; advanced log center sizes and level-3 area threshold.
- Appearance: built-in theme selection (current custom name retained) and minimum
  overlay label font size. Does not edit theme contents or theme the settings window.
- Key bindings, Scrolling, Macros, Key-press HUD: explicitly unfinished information
  pages, with no pretend functional controls. Existing JSON remains untouched.
- Startup is still a registry tray toggle; HUD enablement is still runtime-only.
  Macro recordings are not part of the config editor.

Controls use standard WPF keyboard interaction, focus indicators, labels/automation IDs,
and layout-aware key labels plus stable VKey names. The picker does not capture a
global key combination. Default selection does not auto-enable modes or invent chord
keys: enabled non-default modes require distinct chords under existing validation.
Uniform grid chord dispatch is wired in `NavigatorCoordinator.BuildChordKeyMap` so
making another mode the default does not strand an enabled Uniform grid.

## Real integration

`App.SetupTrayContextMenu` -> `ShowSettings` -> `SettingsWindow` local draft ->
`SettingsConfigStore.Preview` -> `ConfigLoader.ReadSettings` -> theme/hotkey preflight ->
`SettingsConfigStore.Save` -> `App.ApplySettings` -> `ReloadConfiguration` ->
`BootstrapCoordinator` -> existing `ConfigLoader.Load`, renderers, services, registrations.

Production path remains `%APPDATA%\Klikety\config.json`. `ConfigModel` stays init-only.
`ReadSettings` uses the loader's deserializer and binding rules but never runs migration,
returns parse-default fallbacks, or saves malformed input. Version 7 is required;
required null sections, ambiguous duplicate object properties, and invalid UTF-8 are
rejected. Existing violations are visible; all must be resolved before save.

Dirty state compares controls to their loaded values. Switching pages retains the draft.
Discard confirms when dirty, then reads the latest disk state. Closing/quit confirms
unsaved changes or pending apply. Save reports validation, I/O, and apply errors inline.
After a successful write, an apply issue leaves **Save & apply** enabled for retry
without rewriting the file. Save failure retains the draft.

## JSONC persistence and draft trade-offs

The writer tokenizes UTF-8 into byte spans, replacing changed scalar values only.
Unknown members, untouched values, comments, trailing commas, and BOM are retained.
Missing members are inserted after the last member value, before its trailing
comma/comments. Newly inserted mode objects materialize their effective defaults:
otherwise creating an init-only `ModeConfig` from a partial object resets omitted
booleans to false. There is no blind serialization of the root model.

**Comment/format limitation:** replaced scalar spelling/escaping is normalized;
new members/sections use generated compact JSON and LF indentation, not the original
formatting style. Comments remain in place but are not interpreted or re-associated
with inserted members. Existing file comments are not globally rewritten.

Save uses a random same-directory temporary file, flushed to disk, then `File.Replace`
with `config.json.settings.bak`. Snapshot bytes are compared just before replace;
external edits are rejected rather than merged. This is optimistic conflict detection,
not a filesystem compare-and-swap: a competing writer at the final check/replace
boundary remains a production-hardening gap. Backup is the previous exact file.

Apply uses existing teardown/bootstrap, not a rollback transaction. A saved file
remains saved if registration/bootstrap fails, and the UI distinguishes that state.
The backup allows manual recovery. Reload recreates an active HUD with current values;
the tray reflects its state. Existing scroll pause state is not a persisted setting.

## Isolated native demo

```powershell
dotnet run --project src\Klikety\Klikety.csproj -c Release --no-build -- --settings-demo C:\path\outside\KliketyAppData\config.json
```

Demo creates only absent embedded config/dark/light fixtures under the given directory.
It refuses the real Klikety AppData directory and skips first-run extraction,
registry UI, global hotkeys, keyboard hooks, macro store, and coordinator bootstrap.
The tray and window are labeled **ISOLATED DEMO**. Save, JSONC persistence, theme
validation, and config reload are real; runtime navigation/apply is intentionally
disabled, not mocked as successful. The demo may coexist with the user's Klikety.

Focused checks: `SettingsConfigStoreTests` and
`CoordinatorModeSwitchingTests.SettingsUniformGridChord_BeforeLock_RendersUniformGrid`.
These cover comments/unknowns/BOM/backup, missing sections/default semantics, malformed
input, binding/mode/size rejection, external-edit detection, preflight failure, reload,
and the newly exposed chord dispatch. They do not establish live global registration,
full reload rollback, all accessibility/DPI combinations, or production readiness.
