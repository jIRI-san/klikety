---
description: Configuration system — loading, validation, migration, first-run extraction, tray integration, and logging.
globs:
  - src/Klikety/Config/**
  - src/Klikety/App.xaml.cs
---

# Configuration

## ElementHints (version 9)

Version 9 additively migrates `modes.elementHints` with disabled defaults, Tab chord
and `twoKey=true`/`arrowKeys=true` defaults. Only `twoKey` remains required for
adaptive single/pair labels; arrows optionally focus controls/groups, never page.
PgUp/PgDn are reserved from local label/action/chord/scope/help/macro bindings
while hints are enabled. Modified global scroll shortcuts keep their separate role.
Unknown fields, keys and defaults are
preserved. Partial element settings merge defaults without overriding explicit
values. `ElementHintsPolicy` requires enabled UniformGrid fallback, valid disjoint
label axes and collision-free commands. Invalid settings suppress its factory,
default and chord dispatch and produce a configuration violation. All mode/help/
scope/macro/scroll collision lists include the fifth mode.
`modes.elementHints.discoveryTimeoutMs` optionally controls discovery including
helper startup: default 10000 ms, valid integer range 100-60000 ms. Explicit
existing deadlines are preserved. Missing fields
retain the default without rewriting existing version-9 files; newly migrated and
first-run hint blocks include it. `ModeConfig` carries the property for the shared
mode shape, but only ElementHints consumes/edits it. Out-of-range values remain
unchanged, produce violations, suppress hint dispatch and block Settings saving
even with hints disabled. Validation and helper cleanup stay fixed at 500 ms.
Child-count collapsing has been removed. The former `rootGroupChildThreshold`
and `groupChildThreshold` fields are ignored if present in an existing version-9
file; loading does not rewrite it. Neither field is exposed in Settings or the
default config/schema. `cacheWindowCount`
(default 5, integer 0-20) bounds recently used window caches; `0` disables reuse.
Windows of one app count separately. This is an optional additive version-9 field,
included in first-run/migrated blocks, validated even when hints are disabled,
and edited only on the Element hints card. Invalid values are reported, never
clamped. Config replacement shuts down the previous owned cache helper.
Native Settings edits all five mode blocks and the shared default picker. Its
strict read requires current version 9 rather than silently migrating during
editing. With ElementHints as the default, enabled UniformGrid needs no separate
chord because the session already exposes Enter fallback.

## Config Format

- Format: JSONC (`JsonCommentHandling.Skip`); stored at `%APPDATA%\Klikety\config.json`.
- Written on first run from embedded `config.json` template if absent. **Not overwritten on subsequent runs** — changing defaults in the embedded template does not affect existing installs. When a config or theme default changes during development, the user's `%APPDATA%\Klikety\config.json` and `%APPDATA%\Klikety\themes\*.theme.json` must be updated manually (or the files deleted to trigger re-extraction).
- Key fields: `hotKey`, `actionBindings` (VKey → MouseAction), `helpBinding`, ordered `horizontalKeys`/`verticalKeys` arrays, `level3CellSizeThreshold`, `logLevel`, `theme`, `modes`, `scrollHotkeys`, `keyPressVisualization`, `macros`, `appScope`.
- `navigationMode` is a legacy field — migrated to `modes.uniformGrid.twoKey/arrowKeys` on first load. Kept in schema for backward compatibility. `ConfigModel` no longer has a `NavigationMode` property (dead code removed); the enum is only used internally by `NavigatorStateMachine` and `UniformGridSession`.
- Validation includes reserved-key/collision checks for action, navigation, mode, scroll, macro, and app-scope bindings. Settings separates blocking errors from existing advisory warnings (including LogGrid axis policy and macro slot-list length). Macro speed must be finite and ≥ 0; zero retains the existing 100 ms fixed-delay behavior. Settings requires a named log level, retained log count ≥ 1, finite positive label size, and safe indicator colors/numbers. Existing playback-indicator radius/duration values below the schema floor remain warnings until edited; edited values must meet radius ≥ 1 DIP and duration ≥ 100 ms.

## `ConfigVersion`

Integer on `ConfigModel`. `0` = legacy (pre-modes shape), `1` = v1 (modes added), `2` = v2 (shared axis keys at root), `3` = v3 (LogGrid mode added), `4` = v4 (scroll hotkeys), `5` = v5 (macros), `6` = v6 (app-scope), `7` = v7 (F1–F10 slot keys; D1–D9 reserved), `8` = overlay help binding, `9` = current (opt-in ElementHints). Used by the migration pre-pass to detect old configs.

## Config Migration (`ConfigMigrator`)

`ConfigMigrator.MigrateIfNeeded(path)` runs a `JsonNode`-based pre-pass before deserialization:

- Detects old shape (has `navigationMode`, no `modes`) and transforms to new shape.
- Maps legacy `navigationMode` to per-mode `twoKey`/`arrowKeys` on `UniformGrid`.
- Key set migration: preserves user's original `firstKeys`/`secondKeys` (no silent 8→10 expansion). Crosshair/LogCrosshair get 10-key defaults.
- Conflict handling: N/M chord keys conflicting with `actionBindings` → auto-disable mode. New 10-key axis keys conflicting with `actionBindings` → fall back to legacy key set.
- Post-migration normalization: at least one enabled mode, exactly one default.
- v2→v3: adds `logGrid` mode block if missing. Chord key (OemComma) conflict → auto-disable.
- v3→v4: adds `scrollHotkeys` section with disabled defaults if missing. Preserves existing user-added `scrollHotkeys`.
- v4→v5: adds `macros` section with enabled defaults if missing. Preserves existing user-added `macros`. Default: `enabled: true`, `globalHotKey: Ctrl+Alt+Shift+M`, `recordKey: OemPipe` (backslash), `helperKey: OemTilde` (backtick), `slotKeys: [D0..D9]`, `speedModifier: 1.0`.
- v5→v6: adds `appScope` section if missing or null. Conflict-aware: if default chord key (`OemPeriod`) collides with `actionBindings`, `horizontalKeys`, or `verticalKeys`, sets `chordKey: null` (feature auto-disabled) with migration warning. Default: `chordKey: "OemPeriod"`. Preserves existing user-added `appScope`.
- Atomic write: random temp file (`Path.GetRandomFileName()`) → `.bak` backup → rename. Write errors return `BlockingError`.
- v6→v7: default `macros.slotKeys` D0–D9 become F1–F10 only when the array is exactly that default. Custom slot keys are kept. D1–D9 are reserved (display switch) in slotKeys, horizontalKeys, verticalKeys, actionBindings, and chord keys. D0 is not reserved.
- v7→v8: adds `helpBinding` with enabled `OemQuestion` and optional Shift when absent. If the default key conflicts with navigation, action, mode, app-scope, macro, or reserved keys, migration persists the help binding disabled and emits a warning; it never steals a configured command. Existing help settings and unknown fields are preserved. Explicit runtime conflicts or invalid VKeys make help ineffective and are reported rather than rebound.
- `configVersion` > known → fail-closed with blocking error.
- Mixed shape (`modes` + `navigationMode`) → `modes` wins.
- Unknown fields preserved (round-trip).
- Idempotent: already-migrated configs produce no mutations.

## First-Run Extraction (`FirstRunExtractor`)

`FirstRunExtractor.EnsureDefaults()` extracts embedded resources to `%APPDATA%\Klikety\` on startup. Two categories:

- **Always-overwrite** (schemas): `config.schema.json`, `themes/theme.schema.json`. Written via atomic temp-file + `File.Move(overwrite: true)` on every startup. Ensures users get latest schema after upgrades.
- **Skip-if-exists** (user-editable): `config.json`, `themes/dark.theme.json`, `themes/light.theme.json`. Written only when absent (first run).

Failure handling: per-file try/catch for `IOException` and `UnauthorizedAccessException`. Returns `List<string>` of warning messages from the internal testable overload. Public overload writes warnings via `Trace.TraceWarning`. Non-blocking — app continues regardless of extraction failures.

## Tray Integration

- **Settings...** is the first context-menu item in normal production launches and opens
  one reusable native WPF seven-category editor; repeat clicks activate it and restore it
  from minimized state rather than opening another draft. Closing releases the window
  reference so the next click opens a fresh session. Its config file comes from the same
  captured `AppPaths` root as the runtime; demo mode retains its explicit filename.
  Each page writes to a
  typed draft; focused key capture is local to its picker and does not install a global hook.
  Save validates the full candidate through `ConfigLoader.ReadSettings`, patches changed
  JSONC leaves/collections with `SettingsConfigStore`, then reloads a captured model only
  while navigation and macro work are idle. Disk and runtime recovery outcomes are reported
  separately; drafts survive validation/apply/recovery failures. Comments, unknown members,
  BOM, untouched scalar spelling, collection order, and orphan comments within their
  original container are retained. The previous exact file is kept in `.settings.bak`;
  external edits block saving and a final compare/replace race remains. Current config version only;
  no parse-default saving or implicit migration in the editor. Full behavior and limits are
  in [settings.design.md](settings.design.md).

- **Isolated runtime fixture** (`--settings-runtime-fixture <absolute-directory>`) uses
  `AppPaths` to confine config, logs, macros, themes, and topology to a fixture folder.
  It avoids first-run extraction and registry toggles, applies real runtime registrations,
  and uses dedicated Ctrl+Alt+Shift+F11/Pause hotkeys. Verify registration availability before
  relying on the fixture; the hook-free `--settings-demo` is not runtime evidence.

- Tray icon via `H.NotifyIcon.Wpf` (`TaskbarIcon` in XAML). No WinForms dependency.
- `ShutdownMode=OnExplicitShutdown` — process persists until "Quit" menu item calls `Application.Current.Shutdown()`.
- Context menu items: **Settings...**, **About** (small `AboutWindow`), **Open Configuration Folder** (`Process.Start("explorer.exe", path)`), **Reload Configuration** (reload/apply edited JSONC while idle), **Reset Configuration** (visible only with blocking violations — disposes coordinator, re-bootstraps), **Start with Windows** (toggle with checkmark), **Show Key Presses** (runtime toggle — creates/destroys visualization resources via activation transaction; see `key-press-visualization.design.md`), **Pause/Resume Scroll Keys** (visible only when scroll hotkeys enabled — toggles `Unregister()`/`Register()` without config change), **Quit** (disposes coordinator, hotkey service, scroll service, key press visualization, tray icon, logger factory).
- Tray notifications used for: hotkey conflict, hook install failure, config/key-binding violations, theme load failure.

## Logging

- `Microsoft.Extensions.Logging` with rolling file sink → `%APPDATA%\Klikety\logs\`.
- Omitted-field model defaults: `logLevel: Warning`, `fileLoggingEnabled: false`, `retainedLogFileCount: 7`. The extracted first-run template explicitly enables `Debug` file logging. Existing configs retain their values.
- Debug-level logging in `NavigatorCoordinator`:
  - Every mapped keystroke: key name + state before processing.
  - State transitions: `{before} → {after}` when state changes.
  - All SM events: `ColumnHighlighted`, `CellHighlighted`, `CellEntered`, `LevelExited`, `ColumnUnhighlighted`, `ActionRequested`, `Cancelled`, `InvalidKeyPressed` — with relevant parameters (col, row, level, cell counts).
