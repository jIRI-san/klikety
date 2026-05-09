---
description: Configuration system — loading, validation, migration, first-run extraction, tray integration, and logging.
globs:
  - src/Klikety/Config/**
  - src/Klikety/App.xaml.cs
---

# Configuration

## Config Format

- Format: JSONC (`JsonCommentHandling.Skip`); stored at `%APPDATA%\Klikety\config.json`.
- Written on first run from embedded `config.json` template if absent. **Not overwritten on subsequent runs** — changing defaults in the embedded template does not affect existing installs. When a config or theme default changes during development, the user's `%APPDATA%\Klikety\config.json` and `%APPDATA%\Klikety\themes\*.theme.json` must be updated manually (or the files deleted to trigger re-extraction).
- Key fields: `hotKey`, `actionBindings` (VKey → MouseAction), `firstKeys`/`secondKeys` (flat VKey arrays), `level3CellSizeThreshold`, `logLevel`, `theme`, `modes`, `scrollHotkeys`, `keyPressVisualization`, `macros`.
- `navigationMode` is a legacy field — migrated to `modes.uniformGrid.twoKey/arrowKeys` on first load. Kept in schema for backward compatibility. `ConfigModel` no longer has a `NavigationMode` property (dead code removed); the enum is only used internally by `NavigatorStateMachine` and `UniformGridSession`.
- Validation at startup: reserved keys (Escape, hotkey modifiers, arrow VKeys, VK_RETURN) not in nav/action sets; action ↔ nav key overlap; hotkey modifier VKeys checked against nav/action key sets; cross-set disjointness (`firstKeys ∩ secondKeys = ∅`); per-set duplicate check; scroll hotkey validation (`scrollAmount` ∈ [1, 100], scroll keys vs reserved/action/chord/nav keys, duplicate up/down rejection); macro key validation (full collision matrix: record/helper/slot keys vs reserved/action/nav/chord/scroll/hotkey; intra-macro uniqueness; speed modifier ≥ 0; slot keys array length); all violations collected and surfaced via tray notification list.

## `ConfigVersion`

Integer on `ConfigModel`. `0` = legacy (pre-modes shape), `1` = v1 (modes added), `2` = v2 (shared axis keys at root), `3` = v3 (LogGrid mode added), `4` = v4 (scroll hotkeys), `5` = current (macros). Used by the migration pre-pass to detect old configs.

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
- Atomic write: random temp file (`Path.GetRandomFileName()`) → `.bak` backup → rename. Write errors return `BlockingError`.
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

- Tray icon via `H.NotifyIcon.Wpf` (`TaskbarIcon` in XAML). No WinForms dependency.
- `ShutdownMode=OnExplicitShutdown` — process persists until "Quit" menu item calls `Application.Current.Shutdown()`.
- Context menu items: **About** (small `AboutWindow`), **Open Configuration Folder** (`Process.Start("explorer.exe", path)`), **Reset Configuration** (visible only with blocking violations — disposes coordinator, re-bootstraps), **Start with Windows** (toggle with checkmark), **Show Key Presses** (runtime toggle — creates/destroys visualization resources via activation transaction; see `key-press-visualization.design.md`), **Pause/Resume Scroll Keys** (visible only when scroll hotkeys enabled — toggles `Unregister()`/`Register()` without config change), **Quit** (disposes coordinator, hotkey service, scroll service, key press visualization, tray icon, logger factory).
- Tray notifications used for: hotkey conflict, hook install failure, config/key-binding violations, theme load failure.

## Logging

- `Microsoft.Extensions.Logging` with rolling file sink → `%APPDATA%\Klikety\logs\`.
- Config: `logLevel` (default `Debug`), `fileLoggingEnabled` (default `true`), `retainedLogFileCount` (default `7`).
- Debug-level logging in `NavigatorCoordinator`:
  - Every mapped keystroke: key name + state before processing.
  - State transitions: `{before} → {after}` when state changes.
  - All SM events: `ColumnHighlighted`, `CellHighlighted`, `CellEntered`, `LevelExited`, `ColumnUnhighlighted`, `ActionRequested`, `Cancelled`, `InvalidKeyPressed` — with relevant parameters (col, row, level, cell counts).
