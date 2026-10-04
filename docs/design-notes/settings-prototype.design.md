---
description: Production native WPF settings editor, typed draft, targeted JSONC persistence, and isolated runtime verification.
globs:
  - src/Klikety/SettingsWindow.*
  - src/Klikety/Config/SettingsConfigStore.cs
  - src/Klikety/Config/SettingsDraft.cs
  - src/Klikety/Config/SettingsApplyModels.cs
  - src/Klikety/Config/AppPaths.cs
  - src/Klikety.Tests/Settings*Tests.cs
---

# Settings Sidebar

The seven-category native WPF editor grew from the exploratory sidebar prototype.
The prototype established layout direction only; it is not evidence of production
runtime behavior. Settings is opened from the real tray entry and reuses one modeless
window. Common controls precede collapsed Advanced sections; General diagnostics and
indicator visuals are Advanced. Each page scrolls independently of the persistent
sidebar/footer.

## Coverage and runtime-only boundaries

- **General**: main activation shortcut, log level, file logging, retained file count,
  plus read-only config path/version/schema metadata.
- **Navigation**: all four mode enabled/default/chord/two-key/arrow values, each mode's
  LogCrosshair and LogGrid sizes, optional app-scope chord, and Level-3 threshold.
- **Key bindings**: action mapping editor and ordered horizontal/vertical key lists.
- **Appearance**: theme reference and label-size floor. Theme file contents remain separate.
- **Scrolling**: enablement, up/down shortcuts, and amount. Pause is runtime-only.
- **Macros**: enablement, nullable global hotkey, record/helper/slot keys, speed, and all
  playback-indicator properties. Macro recordings remain in `macros.json`.
- **Key-press HUD**: every visual/timing property. HUD enablement remains runtime-only.

Startup registration is still a tray registry toggle, never JSON. No settings control
activates a hook while editing. Shortcut capture is focused on a picker, uses stable
`VKey` values, ignores modifier-only/repeat events, and cancels on Escape or focus loss.
Unsupported keys remain selectable from the readable picker.

## Draft and validation

`SettingsDraft` holds the loaded `ConfigModel` baseline and changed JSON leaves. Page
switching does not discard draft state; changed-back values clear their dirty state.
Save/Discard/close feedback is inline and the status text is a polite live region.
Save errors retain the candidate; successful apply rebases the editor. Failed apply
retains the draft and separately reports disk and runtime recovery outcomes.

`ConfigLoader.ReadSettings` is strict and never migrates or substitutes parse defaults.
Version 7 is required; malformed JSON/UTF-8, required nulls, duplicate properties (also
inside arrays), unsupported versions, and save-blocking validation prevent replacement.
Existing LogGrid-axis and macro slot-count compatibility warnings stay advisory.
Settings additionally requires valid log-level text, retained count >= 1, finite positive
label sizing, finite macro speed >= 0, safe HUD/indicator values, and collision-free keys.
Zero macro speed preserves the existing 100 ms fixed-delay behavior.

Playback-indicator radii and animation duration below schema floors are advisory for
untouched legacy values. Editing those fields requires radii >= 1 DIP and duration >=
100 ms. Non-finite values, invalid colors, nonpositive radii/duration, and negative stroke
thickness are unsafe and block Settings saving. Runtime construction reports invalid
indicator values instead of silently substituting a red brush.

## JSONC persistence

`SettingsConfigStore` works from the original UTF-8 byte snapshot, including a possible
BOM. It tokenizes nested objects/arrays and patches only changed scalar leaves, inserting
new members with safe defaults. It does not serialize `ConfigModel` wholesale.

Changed collections retain matched raw item text and ordering, preserve comments inside
the same array/object after add/move/delete, and keep unknown fields. Comment
re-association with a deleted entry is not promised. Changed/new fragments may normalize
spacing/escaping; untouched scalar text, unknown fragments, and BOM remain byte-stable.
No-op saves do not replace the file or create a backup.

Save writes a same-directory temporary file, flushes it, compares the live file with the
accepted snapshot, then atomically replaces it with `config.json.settings.bak` containing
the exact previous bytes. External edits/deletion are rejected rather than merged.
The compare and replace are separate filesystem operations; this optimistic conflict
check cannot prevent a final race from a non-cooperating writer.

After an unaccepted apply failure, guarded recovery restores previous bytes only if disk
still matches the candidate written by Settings. Recovery never overwrites
`.settings.bak`; newer external bytes are retained and reported as a conflict.

## Runtime apply and fixtures

The real tray flow is `App.SetupTrayContextMenu` -> `ShowSettings` -> typed draft ->
strict store preview/save -> idle and theme/hotkey preflight -> captured-model bootstrap
-> status/recovery. Busy Save, tray Reload, and Reset are rejected before disk mutation.
Runtime and disk baselines are captured independently. Failed candidate composition is
cleaned up; recovery attempts the previous runtime config and independently reports
runtime/disk success. HUD enabled state and paused scroll registrations are preserved.
Logger factories remain alive through teardown/recovery reporting, and click-indicator
windows are closed when their owning coordinator is disposed.

`AppPaths` roots config, logs, macros, themes, and display-topology files. The hook-free
`--settings-demo <absolute-config-path>` remains useful for visual editing but is not
runtime evidence. `--settings-runtime-fixture <absolute-directory>` starts the actual
application/services against a dedicated fixture, refuses the real AppData directory
and reparse-point paths, skips first-run extraction and the registry toggle, and sets
Ctrl+Alt+Shift+F12/F11 as main/macro test hotkeys. This mode performs real registration;
confirm those combinations are available before continuing. Do not use it if it could
interfere with the user's running app. It does not clean its fixture automatically.

## Evidence and known limits

Automated fixtures cover JSONC preservation, comments/unknowns/BOM, backup and guarded
restore, no-op/conflict/deleted-file behavior, draft retention, all seven pages, action
binding save/reopen, focused key capture, fixture paths, and injected apply failure.
Focused tests do not establish live tray reuse, real registration/recovery, or the
100/150/200% DPI matrix. Those are native checks that require a separate, isolated
Windows display/runtime; leave each unavailable row unverified rather than treating the
demo or simulated layout as proof.
