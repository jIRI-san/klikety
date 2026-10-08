# Settings and Files

[Documentation](README.md) | [Getting started](getting-started.md) | [Navigation](navigation.md)

## Configuration

Config file: `%APPDATA%\Klikety\config.json` (JSONC — comments allowed).

First run extracts default config and theme files automatically.

## Settings window

Choose **Settings...** from the tray to edit General, Navigation, Key bindings, Appearance,
Scrolling, Macros, or Key-press HUD. Edits remain in a draft while switching pages.
**Save & apply** validates and applies the complete candidate while navigation and macro
activity are idle; invalid input, external edits, or activation failures are reported in
the window. Failed apply keeps the draft and reports disk/runtime recovery separately.
When disk recovery succeeds, retry can rebuild the runtime; failed disk restoration
or newer external bytes require explicit reload. **Close** beside Save (Alt+C) uses
the same unsaved-change confirmation as the title bar and never saves.

The editor patches only changed values in `config.json`. It preserves comments, unknown
properties, the UTF-8 BOM, and untouched value text; changed fragments may be reformatted.
The previous exact config bytes are kept in `config.json.settings.bak`. If another process
edits the file, Settings refuses to overwrite it; choose **Discard** to reload those edits.
The final file comparison and replacement are separate filesystem operations, so a
non-cooperating writer can still race that last boundary.

Startup registration remains the **Start with Windows** tray toggle, and HUD enablement
remains a runtime-only tray toggle. Macro recordings stay in `macros.json`; theme contents
stay in their separate theme files. Shortcut capture is focused on the selected field,
does not install a global hook, and can be cancelled with Escape or by moving focus.

### Pages

| Page | Controls |
|---|---|
| General | Activation hotkey; Advanced contains logging and read-only config path/version/schema metadata. |
| Navigation | Enabled/default modes, chord keys, two-key and arrow navigation, grid sizes, app scope, Level-3 threshold, and ElementHints discovery timeout. |
| Key bindings | Action keys, ordered horizontal/vertical keys, and overlay help. |
| Appearance | Overlay theme reference and minimum label font size. |
| Scrolling | Enablement, up/down shortcuts, and wheel amount. |
| Macros | Enablement, global picker hotkey, record/helper/slot keys, playback timing, and click-indicator appearance. |
| Key-press HUD | Font, colors, outline, corner, margin, visible key count, and fade/repeat timing. |

Common controls appear before collapsed **Advanced** sections. Modifier pickers
combine Ctrl, Alt, Shift, and Win; **None** means no flags are selected. **Capture** records a
shortcut only while that picker has focus. Modifier-only keys and repeats are
ignored; Escape or focus loss cancels capture. F11 is available; F12 is reserved
by Windows and excluded.

HUD and playback-indicator colors accept hex input or **Choose color**. The dialog
edits RGB and opacity with a preview; OK updates the draft, Cancel leaves it alone.
Settings and the color dialog follow Windows app light/dark preferences. This is
separate from the overlay's configured theme.

### Validation and recovery

The editor requires the current config version (9); startup/Reload migrates older
files before editing. Invalid keys, collisions, malformed files, and unsafe numeric
or color values block saving. Errors appear inline and target the affected field.
Compatibility warnings remain visible without silently changing old values.

On apply failure, read the disk and runtime outcomes separately. If disk restoration
succeeded, retry **Save & apply**. If restoration failed or another process changed
the file, use **Discard** to reload the current disk file before retrying; this
discards your draft. Quit and restart if the runtime cannot recover. Keep the
backup for inspection rather than assuming the failed candidate is active.

For native registration/recovery testing, see the isolated
[runtime fixture](development.md#isolated-settings-runtime-fixture).

## Folders and files

Use **Open Configuration Folder** in the tray to open `%APPDATA%\Klikety\`.
Settings also has **Open folder** buttons beside the config file, local schema
reference, and theme folder. These open Explorer without saving the draft.

| Path under `%APPDATA%\Klikety\` | Purpose |
|---|---|
| `config.json` | JSONC preferences and bindings. Created if missing; preserved on later starts. |
| `config.json.settings.bak` | Exact previous config bytes from the latest non-no-op Settings save. |
| `config.json.bak` | Backup made by config migration or reset; separate from the Settings backup. |
| `config.schema.json` | Config editor schema, refreshed at startup. |
| `macros.json` | Ten recorded macro slots, separate from Settings preferences. |
| `macros.schema.json` | Macro editor schema, refreshed at startup. |
| `themes\dark.theme.json`, `themes\light.theme.json` | Built-in overlay themes, extracted only when absent. |
| `themes\theme.schema.json` | Theme editor schema, refreshed at startup. |
| `themes\<name>.theme.json` | Your custom overlay themes. |
| `display-topologies.json` | Saved display-topology mappings. |
| `logs\klikety-YYYYMMDD.log` | Rolling diagnostics when file logging is enabled. |

The executable folder and data folder are separate. Keep the entire published
application together, including `uia-worker`; do not copy only `Klikety.exe`.
Back up `config.json`, `macros.json`, custom themes, and any display mappings you
want to retain. Editing generated schemas is not persistent.

Existing preferences and themes are not replaced when the application template
changes. Older configs migrate on load while preserving custom fields and keys.
Do not edit `configVersion` manually. A config from a newer unsupported version
is rejected instead of downgraded.

## Configuration reference

Defaults below describe the extracted first-run config. When logging fields are
omitted, the model instead defaults to `Warning` and file logging off; existing
config files are not overwritten by changes to the template.

| Field | Type | Default | Description |
|---|---|---|---|
| `hotKey.modifiers` | string (flags) | `"Alt"` | Modifier keys: `Alt`, `Control`, `Shift`, `Win` (combine with `,`) |
| `hotKey.key` | string (VKey) | `"Space"` | Trigger key (any VKey name) |
| `horizontalKeys` | VKey[] | `["A","S","D","F","G","H","J","K","L","OemSemicolon"]` | Ordered column-selection keys |
| `verticalKeys` | VKey[] | `["Q","W","E","R","T","Y","U","I","O","P"]` | Ordered row-selection keys |
| `actionBindings` | object | X/C/V/B/Z bindings below | Map VKey names to actions: `LeftClick`, `RightClick`, `DoubleClick`, `MiddleClick`, `MoveOnly`, `DragDrop`. Space implicitly left-clicks. |
| `helpBinding.enabled` | bool | `true` | Enable the overlay-local keyboard help binding. |
| `helpBinding.key` | string (VKey) | `"OemQuestion"` | Key that opens/closes help (`/` or `?` on the default layout). |
| `helpBinding.requireShift` | bool | `false` | Require Shift for the help key. Ctrl, Alt, and Win are never accepted. |
| `scrollHotkeys.enabled` | bool | `false` | Enable global scroll hotkeys |
| `scrollHotkeys.scrollUpKey` | HotKeyConfig | Ctrl+Alt+PageUp | Scroll up hotkey |
| `scrollHotkeys.scrollDownKey` | HotKeyConfig | Ctrl+Alt+PageDown | Scroll down hotkey |
| `scrollHotkeys.scrollAmount` | int | `3` | Wheel ticks per hotkey press (1–100) |
| `level3CellSizeThreshold` | int | `0` | Cell area (px²) above which level-3 subgrid activates. 0 = always active. |
| `modes` | object | see below | Navigation mode config. Each mode has `enabled`, `default`, `chordKey`, `twoKey`, `arrowKeys`. |
| `modes.logCrosshair.logBaseSize` | int | `10` | Base cell size (px) for LogCrosshair center cell. Range: 2–50. |
| `modes.logGrid.logGridBaseSize` | int | `10` | Base cell size (px) for LogGrid center cells. Range: 2–50. |
| `modes.elementHints.discoveryTimeoutMs` | int | `1500` | Discovery deadline including helper startup, 100–60000 ms. Does not change action validation or cleanup deadlines. |
| `navigationMode` | string | Not emitted | Legacy field. Migrated to `modes.uniformGrid` on first load. |
| `theme` | string | `"dark"` | Theme name or relative path to `.theme.json` |
| `logLevel` | string | `"Debug"` | Log level: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, `None` |
| `fileLoggingEnabled` | bool | `true` | Enable file logging to `%APPDATA%\Klikety\logs\`. |
| `retainedLogFileCount` | int | `7` | Max rolling log files kept. Oldest deleted when exceeded. Only applies when `fileLoggingEnabled` is `true`. |
| `minLabelFontSize` | double | `14.0` | Min label font size (DIP). When subgrid cells are too small, labels render outside the grid with connector lines. |
| `appScope.chordKey` | string (VKey) | `"OemPeriod"` | Chord key to scope grid to foreground window. `null` disables. |
| `keyPressVisualization.fontSize` | double | `72.0` | Font size (DIP) for key labels in the HUD. |
| `keyPressVisualization.fontColor` | string | `"#FFCC00"` | Hex color for key label fill. |
| `keyPressVisualization.outlineColor` | string | `"#000000"` | Hex color for key label outline stroke. |
| `keyPressVisualization.outlineThickness` | double | `2.0` | Outline stroke thickness (DIP). |
| `keyPressVisualization.corner` | string | `"BottomRight"` | HUD corner: `TopLeft`, `TopRight`, `BottomLeft`, `BottomRight`. |
| `keyPressVisualization.fadeTimeoutMs` | int | `3000` | Idle time (ms) before entries start fading. |
| `keyPressVisualization.fadeDurationMs` | int | `500` | Duration (ms) of the fade-out animation. |
| `keyPressVisualization.maxVisibleKeys` | int | `3` | Max simultaneous key entries shown (1–10). |
| `keyPressVisualization.margin` | double | `20.0` | Margin (DIP) from screen edge. |
| `keyPressVisualization.repeatWindowMs` | int | `400` | Time window (ms) for collapsing repeated keys (50–1000). |
| `macros.enabled` | bool | `true` | Enable macro recording and playback. |
| `macros.globalHotKey` | HotKeyConfig | Ctrl+Alt+Shift+M | Global hotkey to open macro picker (null to disable). |
| `macros.recordKey` | string (VKey) | `"OemPipe"` | Key to start/stop macro recording while overlay is active. |
| `macros.helperKey` | string (VKey) | `"OemTilde"` | Open the macro picker while navigation is active. |
| `macros.slotKeys` | VKey[] | `["F1", ..., "F10"]` | Ten slot-selection keys. |
| `macros.speedModifier` | double | `1.0` | Finite global playback speed multiplier; `0` uses the existing 100 ms fixed delay. Per-macro override takes precedence. |
| `macros.playbackIndicator` | object | see schema | Click-indicator fill/stroke colors, stroke width, radii, and animation duration. |

The extracted template's mode defaults are:

| Mode | Enabled | Default | Chord | Two-key / arrows |
|---|---|---|---|---|
| `uniformGrid` | Yes | Yes | None | Both on |
| `crosshair` | Yes | No | `N` | Both on |
| `logCrosshair` | Yes | No | `M` | Both on |
| `logGrid` | Yes | No | `OemComma` | Both on |
| `elementHints` | No | No | `Tab` | Both on |

Keep exactly one enabled default mode. ElementHints requires enabled UniformGrid
fallback and adaptive two-key support; arrows are optional. See
[ElementHints](navigation.md#elementhints-opt-in) for enablement, grouping,
timeouts, privacy, and compatibility limits.

### Custom Action Bindings

```jsonc
{
    "actionBindings": {
        "Space": "LeftClick",     // default, can be omitted
        "X": "DoubleClick",
        "C": "MiddleClick",
        "V": "RightClick",
        "B": "MoveOnly",         // move cursor without clicking
        "Z": "DragDrop"          // two-point drag-and-drop
    }
}
```

### Keyboard Layout Setup

Labels follow the foreground application's active keyboard layout, including
**DVORAK** and **Colemak**. Default VKey arrays describe physical key positions;
changing layouts does not require replacing them with printed letters.
For custom positions, edit `horizontalKeys` and `verticalKeys` in Settings.
Keep axes disjoint and avoid action, mode, help, scope, macro and reserved keys.
`firstKeys`/`secondKeys` belong to historical config formats, not current examples.

## Theme customization

Built-in themes: `dark`, `light`.

Copy a built-in theme to `%APPDATA%\Klikety\themes\mytheme.theme.json`, edit it,
and choose `mytheme` in **Settings > Appearance** (or set `"theme": "mytheme"`
in config and Reload). Relative `.theme.json` paths are also supported.
See `themes\theme.schema.json` for font, outline, border, background, highlight,
subgrid, connector, and app-scope colors. The editor changes the theme reference,
not the theme file itself.

## Troubleshooting

| Problem | Solution |
|---|---|
| Activation or scroll/macro hotkey conflict | Choose an unused shortcut in Settings or edit its JSONC binding and Reload. |
| Keyboard hook cannot be installed | Run as a normal user; inspect the reported error and local security-software restrictions. |
| Config validation errors | Check `%APPDATA%\Klikety\logs\` for details. Reserved keys (Escape, arrows, Return) cannot be in `horizontalKeys`/`verticalKeys`. PgUp/PgDn are also reserved for local commands when ElementHints is enabled. |
| Labels show VKey names instead of characters | Your keyboard layout may have dead keys. The fallback is intentional. |
| ElementHints is empty, partial, or times out | Use Enter for grid fallback. Increase discovery timeout for a slow provider; copy the bundled helper if it is missing. Not every app exposes usable controls. |
| Save refused while navigation or macros are active | Finish or cancel the current operation, then retry. |
| Config changed outside Settings | Discard/reload the draft before editing again; newer disk bytes are not overwritten. |

Logs can include mapped navigation keys and state transitions. File logging and
retention are in **Settings > General > Advanced**. **Show Key Presses** exposes
recent keyboard input on screen; leave it off when entering sensitive text.
