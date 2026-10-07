---
description: Production native WPF settings editor, typed draft, targeted JSONC persistence, and isolated runtime verification.
globs:
  - src/Klikety/SettingsWindow.*
  - src/Klikety/SettingsColorDialog.*
  - src/Klikety/SettingsModifierPicker.cs
  - src/Klikety/Config/SettingsConfigStore.cs
  - src/Klikety/Config/SettingsDraft.cs
  - src/Klikety/Config/SettingsApplyModels.cs
  - src/Klikety/Config/Settings*Transaction.cs
  - src/Klikety/Config/SettingsRuntime*.cs
  - src/Klikety/Config/SettingsOperationGate.cs
  - src/Klikety/Config/SettingsShortcutInventory.cs
  - src/Klikety/Config/SettingsFixtureFaults.cs
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
Unsupported capture leaves the picker available; keys outside the supported VKey set
are not invented. F11 is supported; F12 is excluded (reserved for the Windows debugger).
Collection rows wrap rather than clipping; add/move/remove returns keyboard focus to
the affected row.
Field labels, picker/text input contents and adjacent capture buttons are vertically
centered; wrapping key rows keep the same shared control alignment.
Ordered-list reorder buttons use compact vector arrows with descriptive tooltips and
item-specific automation names; disabled arrows inherit the button's disabled foreground.
Every displayed local path has an **Open folder** button: the config file and local
`$schema` reference open their parent directories; the theme folder opens directly.
Relative schema files resolve against the config directory; `file:` URIs are supported,
while web references and unspecified metadata have no filesystem action. These actions
open Windows Explorer without saving the draft or editing separate files; launch/path
errors appear inline. Isolated demos use their own displayed paths, never AppData defaults.

All four HUD/playback-indicator color fields retain direct hex input and a **Choose color**
button. The small owned WPF `SettingsColorDialog` uses RGB and opacity sliders/numeric
inputs (exact bytes 0-255), synchronized `#RRGGBB`/`#AARRGGBB` input, and current/new
checkerboard previews. It uses the same hex validator as Settings, without WinForms,
third-party dependencies, a color wheel or an eyedropper. Invalid input disables OK and
explicitly identifies the last-valid preview; sliders/valid input can repair it. Enter
accepts, Escape/Cancel closes without changing the draft. Unchanged colors preserve their
original hex spelling/alpha representation. Choosing updates only the draft, not disk or
live HUD/indicator resources; Save & apply remains the only persistence/apply action.

Activation, both scroll shortcuts and the optional macro shortcut share
`SettingsModifierPicker`: one compact dropdown with independent Ctrl/Alt/Shift/Win
checkboxes, a combined summary and **None** when empty. The popup stays open across
selections. Space toggles a focused checkbox, arrows/Home/End move focus, F4 or Alt+Down
opens/closes, Enter/Escape closes, and Tab/Shift+Tab closes and continues page traversal.
UIA exposes ComboBox, ExpandCollapse and a read-only summary value, with standard
checkbox Toggle peers for each flag. Captured shortcuts update the same typed flags.
Serializing still uses the existing `HotKeyModifiers` representation; unchanged/reverted
flags do not dirty or rewrite the document. Unknown legacy flags remain visible as
invalid instead of being silently clamped; an explicit selection can repair them.
Disabling/removing the control closes its popup.

Settings and its color dialog use window-local WPF .NET 10 Fluent `ThemeMode="System"`
to follow Windows app light/dark, accent and high-contrast settings, including framework
theme-change handling. Their surfaces, text, borders and error status use dynamic
Fluent brush resources. Both windows merge Microsoft's Fluent dictionary before
declaring local sizing styles, which inherit the explicit `DefaultButtonStyle`,
`DefaultCheckBoxStyle`, etc. keys. Do not base an implicit type override on that same
type key: its self-lookup can resolve to the legacy theme and leave black text/Aero
controls on Fluent dark surfaces. The color-channel Slider style is explicitly keyed
so its base type lookup is unambiguous. The checkerboard
and selected color swatches intentionally retain their color-preview semantics.
No registry setting is written, no custom OS-theme watcher is added, and no new package
is needed. Application-level theme mode and navigation/HUD/indicator resources remain
unchanged: the user's configured overlay theme still controls navigation. Windows-local
theme changes do not save, reload or discard drafts.

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
`SettingsOperationGate` serializes the complete Save transaction against tray Reload/Reset
and rejects reentrancy/concurrent operations before reading or mutating disk.
`SettingsSaveTransaction` uses independent captured disk and runtime baselines, preserving
draft edits over the restored disk base. Newer external bytes require explicit Discard/
reload before retry. `SettingsShortcutInventory` includes main, both scroll and macro
registrations; owned shortcuts are deferred to activation, never unregistered just to probe.
`SettingsRuntimeResources` tracks candidate acquisition and reverse cleanup, transfers hook
ownership to the coordinator, and closes overlay/picker/playback windows. Cleanup failures
are reported while remaining resources are still released. Failed cleanups retain their
owners for retry, including native hook callbacks; failed unregistration cannot masquerade
as released ownership. A hook being disposed stops consumer dispatch/key suppression
even when unhook fails, so disposed HUD/navigation consumers receive no subsequent keys.
`SettingsRuntimeReplacement`
applies the captured config and preserves HUD state and pause only while scrolling stays
enabled; failed HUD creation releases the candidate. `SettingsLoggerLifetime` retains
previous factories through teardown, recovery, and outcome reporting.

`AppPaths` roots config, logs, macros, themes, and display-topology files. The hook-free
`--settings-demo <absolute-config-path>` remains useful for visual editing but is not
runtime evidence. `--settings-runtime-fixture <absolute-directory>` starts the actual
application/services against a dedicated fixture, refuses the real AppData directory
and reparse-point paths, skips first-run extraction and the registry toggle, and sets
Ctrl+Alt+Shift+F11/Pause as main/macro test hotkeys. This mode performs real registration;
confirm those combinations are available before continuing. Do not use it if it could
interfere with the user's running app. Fixture admission rejects AppData overlap in either
direction and reparse points in ancestors or existing contents; checks repeat before
configuration operations. The fixture disables the unrelated Debug hotkey too. It does
not clean its fixture automatically.

### Native operator procedure (plan 5.3; not automated evidence)

1. Use a separate Windows host/display where the user's Klikety and shortcuts are not
   affected. Choose a dedicated absolute directory outside the user's Klikety AppData
   tree and its ancestors, with no linked files/directories. Confirm main
   Ctrl+Alt+Shift+F11 and macro Ctrl+Alt+Shift+Pause are free. Any newly enabled scroll
   or replacement shortcut must also be free. Do not stop the user's running app.
2. Launch Release with `--settings-runtime-fixture <absolute-directory>`. Identify the
   fixture PID and its tray tooltip. Open Settings twice to confirm one reused window.
   Edit/save/reopen all seven pages; exercise real navigation, enabled scroll hotkeys,
   macro picker, theme/logging, and HUD active/off refresh. During navigation/recording/
   playback, saving must be rejected without changing config bytes.
3. Tray **Fixture: fail next operation** arms one-shot candidate faults for logger,
   overlay/coordinator, main, either scroll registration, macro, indicator, HUD, or
   disk-save. Choose a candidate fault, edit, and Save; inspect explicit failure,
   retained draft, original `.settings.bak`, and independent disk/runtime recovery.
   Arm both a candidate fault and **Recovery main** to exercise failed runtime recovery,
   or **Recovery disk-restore** for failed file restoration. HUD faults require HUD
   already enabled. Scroll-up/down faults require scrolling enabled.
4. **Candidate external-edit** appends a fixture-only comment after the saved candidate,
   then fails activation. Confirm recovery keeps that newer file and the original backup,
   while the draft remains and Save is disabled until explicit Discard/reload.
   For a genuine registration conflict, click **Fixture: reserve
   Ctrl+Alt+Shift+Backspace conflict**. Reservation failure is explicit and does not
   unregister another owner. If reserved, choose that chord as the Settings main
   shortcut; Save must refuse before disk mutation. Click the fixture control again to
   release only its registration; it is also released on fixture Quit. One-shot failure
   controls never simulate a passing real-registration check.
5. Run keyboard-only categories, Tab/Shift+Tab, capture/cancel, list and action operations,
   Advanced, validation focus, Save/Discard/close/quit. On the actual display, inspect
   100/150/200% with work area >=1280x720 **DIP** at each scaling; verify every actionable
   control and fixed footer is accessible. Logical layout tests do not establish this
   native matrix. Record unavailable rows as unverified.
6. Quit only the identified fixture instance. Retain its config and backup for inspection;
   if manual restoration is needed, copy its `.settings.bak` to its `config.json` while
   the fixture is stopped. Do not delete worktrees or alter production AppData/registry.

Fault control files are `settings-candidate-fault.txt` and
`settings-recovery-fault.txt`, both confined to the fixture root. They contain a supported
stage name and are consumed once. Normal/demo operation has no fault injector.

## Evidence and known limits

Automated fixtures cover JSONC preservation, comments/unknowns/BOM, backup and guarded
restore, no-op/conflict/deleted-file behavior, draft retention, all seven pages, action
binding/list save/reopen, physical capture and modifier/repeat/focus-loss cancellation,
all editable model leaves and metadata preservation, null/disabled fields and migration,
theme resolution versus color advisories, logger I/O, independent recovery combinations,
ownership/cleanup/logger lifetime, shortcut preflight, busy/reentrancy/concurrency, and
logical bounds at 980/1080/1280 DIP widths. The explicit field map lives in
`SettingsFieldCases`; `SettingsCoverageTests` fails when a new editable model leaf is
unmapped. `SettingsWindowTests` drives every editor to Save/reopen and covers confirmation,
validation targeting, category arrows and focus traversal. No test installs global hooks.
Focused tests do not establish live tray reuse, real registration/recovery, or the
100/150/200% DPI matrix. Those are native checks that require a separate, isolated
Windows display/runtime; leave each unavailable row unverified rather than treating the
demo or simulated layout as proof.
