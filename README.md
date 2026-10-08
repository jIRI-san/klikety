# Klikety

Keyboard-driven mouse navigator for Windows. Press a hotkey, select a screen region or accessible control with the keyboard, then dispatch a mouse action — without touching the mouse.

Also without touching the code. This paragraph is the only one I have written manually, the rest is AI generated as a test of how well things works end-to-end with more systemic approach to plans and to test and validate some skills which will come handy later. (It kinda works until it does not, so I need to up my plan-writing game significantly to be able to develop things without any passive-aggressive steering...)

![Animated PNG touring all five navigation modes, nested control hints and grid refinement](docs/screenshots/navigation-demo.png)

**All five modes in motion:** UniformGrid, Crosshair, LogCrosshair, LogGrid and
ElementHints, including nested control badges. Captions identify each view.
The loop then shows grid refinement: `J` → `T` selects a region, `G` → `Y`
refines the target, and Escape backs out. Selection moves the cursor, but no click is sent.
This is an animated PNG (APNG); unsupported viewers show its first frame.
[Static view](docs/screenshots/uniform-grid.png).

## Features

- **Unified 10×10 grid**: Full-screen grid with 10 columns (ASDFGHJKL;) and 10 rows (QWERTYUIOP). Press two keys to select any of 100 cells. Custom axis arrays and migrated installations may use a different size.
- **Multi-level zoom**: Level 1 → Level 2 → Level 3 subgrids for pixel-precise targeting.
- **Five navigation modes**: UniformGrid (two-key grid), Crosshair (axis-based), LogCrosshair (logarithmic center-focused), LogGrid (iterative log-scaled), and opt-in ElementHints (foreground-window UI Automation controls). Switch modes with chord keys while overlay is active.
- **LogCrosshair mode**: Logarithmically-scaled grid centered on cursor. Recenters on every navigation. Cross-arm cells grow proportionally for label readability.
- **LogGrid mode**: 10×10 log-scaled grid with iterative recentering. Two-key selection moves cursor and recomputes grid. Cells grow geometrically from center; sub-5px boundary cells collapse automatically. Explicit action dispatch (Space/X/C/V).
- **Arrow key navigation**: Optional arrow-key cell movement with crosshair highlight. Enter zooms into a cell; action keys (Space) click directly.
- **Auto-scaling labels**: Font sizes adapt to cell height (80% at L1, 90% at L2/L3). External labels with connector lines when cells get too small.
- **Outlined text**: Two-layer stroke+fill rendering ensures label readability over any background.
- **App-scope navigation**: Press a chord key (default `.` / OemPeriod) to scope the grid to the foreground application window. Partial off-screen windows clipped to screen bounds. All modes work within the scoped area.
- **Configurable actions**: Space = left click (default). Bind any key to right-click, double-click, middle-click, move-only (cursor move without click), or drag-and-drop.
- **Modifier-aware clicks**: Hold Shift, Ctrl, or Alt while pressing an action key to send modified clicks (Shift+click, Ctrl+click, etc.).
- **Keyboard help overlay**: Press `/` or `?` while navigation is active to show effective commands on a split keyboard. The same key or Escape closes help without acting; other keys close help and run normally from the current selection. Modifiers alone keep help open.
- **Drag-and-drop**: Two-point drag flow — navigate to start, press drag key, navigate to end, press action key. Supports left/right/middle drag with modifiers.
- **Global scroll hotkeys**: Optional global hotkeys for mouse wheel scrolling at cursor position (default: Ctrl+Alt+PageUp/PageDown). Configurable keys and scroll amount.
- **Keyboard layout aware**: Labels auto-adapt to QWERTY, DVORAK, Colemak, or any layout via Win32 `ToUnicodeEx`.
- **Theme support**: Built-in dark and light themes. Create custom `.theme.json` files.
- **System tray**: Runs in the tray with Settings, About, Open Config, Reload Configuration, Reset Configuration, Start with Windows, Show Key Presses, Pause/Resume Scroll Keys, and Quit.
- **Key press visualization**: Runtime-toggled floating HUD showing recent key presses with outlined text. Modifier combos shown as "Ctrl+C", repeated keys collapsed ("A ×3"), oldest-first staggered fade. Click-through, follows active monitor. Configurable font, color, corner, and timing.
- **Macro recording & playback**: Record sequences of mouse actions into 10 slots. Play back at configurable speed with per-step click indicator. Screen resolution and DPI validation on playback.
- **Window-relative macros**: Record macros scoped to a specific application window. Coordinates stored relative to window top-left. Playback validates window title, size, and DPI. Per-step drift detection aborts if the target window moves or loses focus. `StartFromCursor` option for drag operations.
- **JSONC config**: Comments allowed in `config.json`. Schema-validated with `config.schema.json`.
- **Debug logging**: All keystrokes and state transitions logged to `%APPDATA%\Klikety\logs\`.

## Navigation Modes in Pictures

Real production overlays over the same demo application in an offline Windows
Sandbox, with synthetic project data only. Expand a mode to see its native PNG;
open the image for full-size labels. [Capture procedure and provenance](docs/design-notes/readme-demo.design.md).
Regenerate the gallery with the repo's [/capture-demo skill](.github/skills/capture-demo/SKILL.md)
or `.\scripts\readme-demo\Capture-Demo.ps1`; it captures, verifies and publishes
the images automatically.

<details>
<summary><strong>UniformGrid</strong> — two-key cells and progressively finer subgrids</summary>

Open navigation with Alt+Space. Type a column key and a row key to select a cell;
repeat to refine the target, then press an action key when ready.

![UniformGrid: the default 10 by 10 screen grid](docs/screenshots/uniform-grid.png)
![UniformGrid: a local subgrid after selecting a cell](docs/screenshots/uniform-grid-zoom.png)

</details>

<details>
<summary><strong>Crosshair</strong> — uniform horizontal and vertical axis targeting</summary>

Open navigation, then press `N` before navigating. Axis keys select an
intersection; Enter opens a finer subgrid.

![Crosshair: labeled uniform axes over the dashboard](docs/screenshots/crosshair.png)

</details>

<details>
<summary><strong>LogCrosshair</strong> — small precise steps near the cursor, bigger steps farther away</summary>

Open navigation, then press `M`. The logarithmic cross recenters as you move;
small-cell labels fan out for readability.

![LogCrosshair: cursor-centered logarithmic axis bands](docs/screenshots/log-crosshair.png)

</details>

<details>
<summary><strong>LogGrid</strong> — two-key logarithmic cells with iterative recentering</summary>

Open navigation, then press comma. Each two-key selection moves the cursor and
recomputes the grid; clicking still requires an explicit action key.

![LogGrid: cells grow outward from the cursor position](docs/screenshots/log-grid.png)

</details>

<details>
<summary><strong>ElementHints</strong> — control labels and linked badges for nested actions</summary>

Enable ElementHints in Settings, open navigation, then press Tab.
Type a control label to select it, or a `+` label to open its group.
Small groups use one key: this combo box exposes its own action, button and edit
without dropping any target. Enter always returns to grid.
Nested badges and control outlines share colors and solid/dashed/dotted patterns;
displaced badges have matching leader lines, so color is not the only cue.

![ElementHints: translucent control labels and plus-marked groups](docs/screenshots/element-hints.png)
![ElementHints L2: one-key badges with matching colors and border patterns for the combo box, button and edit](docs/screenshots/element-hints-children.png)

</details>

## Prerequisites

- Windows 10 or later
- .NET 10 SDK (for building from source)

## Build

```powershell
dotnet build Klikety.slnx
```

### Publish

```powershell
dotnet publish src/Klikety/Klikety.csproj -r win-x64 --self-contained -c Release
```

## Run

```powershell
dotnet run --project src/Klikety/Klikety.csproj
```

Or run the published executable directly.

## Installation

1. Copy the published output to a permanent location (e.g. `%LOCALAPPDATA%\Klikety\`).
2. Enable "Start with Windows" from the tray icon menu, or manually add to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

## Configuration

Config file: `%APPDATA%\Klikety\config.json` (JSONC — comments allowed).

First run extracts default config and theme files automatically.

### Settings window

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

For isolated native apply/registration checks, use a fixture directory outside Klikety's
AppData folder:

```powershell
dotnet run --project src\Klikety\Klikety.csproj -c Release -- --settings-runtime-fixture C:\temp\KliketySettingsFixture
```

This test mode confines config, logs, macros, themes, and display topology to that
directory, disables the registry toggle, and uses Ctrl+Alt+Shift+F11/Pause for the main and
macro hotkeys. It registers real system hotkeys; do not continue if either registration
conflicts with another application. The fixture is not automatically deleted.
Its **Fixture: fail next operation** tray submenu provides one-shot candidate/recovery
faults and an external-edit case, all restricted to fixture files. Use a separate safe
host/display for these native checks; see the [operator procedure](docs/design-notes/settings.design.md#native-operator-procedure-plan-53-not-automated-evidence).

The implementation was checked with the ordinary managed suite and isolated native
save/registration/recovery scenarios. Remaining native interaction and 100/150/200%
display checks were explicitly deferred to the user's manual validation; they are
not recorded as passed. The operator procedure lists the safe follow-up checks.

### Configuration Reference

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
| `navigationMode` | string | `"both"` | Legacy field. Migrated to `modes.uniformGrid` on first load. |
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
| `macros.speedModifier` | double | `1.0` | Finite global playback speed multiplier; `0` uses the existing 100 ms fixed delay. Per-macro override takes precedence. |

### Element Hints (opt-in)

Labels stay near detected controls. Overlapping nested controls share a `+` group
badge; its next level keeps both parent actions and independent children.
Nested key badges stay beside controls, with matching colored text, outlines and
solid/dashed/dotted border patterns. Coincident outlines are inset separately;
displaced badges have matching leader lines. Black halos keep these cues readable
on light and dark backgrounds. Focus or selection shows the control's role/actions
in the footer; a scrolling role list remains the fallback for extreme layouts.
Large sets use suitable UIA containers or navigation-only capacity groups instead
of routine paging. Ordinary levels retain selected-only displacement connectors.
A small bottom footer shows state and fallback guidance; technical
discovery counts are in debug logs.

Enable ElementHints under **Settings > Navigation**, then **Save & apply**.
For manual JSON edits, add `"elementHints": { "enabled": true, "chordKey": "Tab",
"twoKey": true, "arrowKeys": true }` under `modes`, then choose **Reload
Configuration** from the tray (or restart the app).
Keep `uniformGrid.enabled` true. Version 9 migration leaves this mode disabled;
existing bindings/defaults are preserved. A configured collision is reported, not
silently rebound.

Open navigation and press Tab before mode lock. Small levels use one horizontal
label key; larger levels use horizontal/vertical pairs. Type a `+` badge's label
to open its group; type a control's label to select it
and preview the cursor **without clicking**; use Space or another action key to
perform the existing physical action. Groups cannot receive mouse actions.
For example, 250 flat targets use three groups with up to 100 controls each.
Optional arrows focus controls/groups within the level, never change pages;
`arrowKeys: false` leaves label navigation working. PgUp/PgDn handle rare paging
when the viewport cannot fit a useful group level. Escape clears an incomplete
label, otherwise returns to the previous level or cancels at L1.
**Enter returns to grid**, even while loading, on
failure, or after mode lock. Labels follow the current keyboard layout.
Open help with the configured help key (default `/`, Shift optional) to see your
current level/key scheme, group focus/selection state, effective action bindings,
modifier clicks and drag/fallback guidance. Help/Escape closes help without clearing
the selection; other non-modifier keys close help and run normally. Help updates
when discovery finishes. Macro setup must finish before navigation keys can run.

Discovery is limited to the application HWND captured before the overlay, clipped
to the navigation display/scope. A centered spinner runs during discovery; hint
badge backgrounds are translucent so underlying icons remain visible.
The bundled helper defaults to a 1500 ms scan deadline. Change **Settings >
Navigation > Element hints > Discovery timeout (ms)** or
`modes.elementHints.discoveryTimeoutMs` (100–60000 ms); for slow applications such
as Word, try `10000`. Existing configs retain 1500 ms when the field is absent.
The timeout includes helper startup; Enter/Escape remain available while loading.
The 500 ms action-validation deadline and bounded traversal (20000 nodes,
depth 64, 2000 targets) remain unchanged. Partial/empty/provider/permission failures stay visible.
Moved, replaced, covered or unavailable targets receive no input; reopen or use
grid. Cursor-only moves may land on a verified descendant of the selected control
(for example, a Copilot sidebar row's nested button); clicks and drags keep stricter
ownership checks. No names, text values or document contents are collected.

The helper ships in `uia-worker` with normal builds and self-contained publish;
copy the entire published folder. Missing helper files affect this mode, not grid
startup. No elevation, UIAccess, browser flags or remote processing are used.
Third-party UIA coverage and live physical-input/mixed-DPI compatibility are not
guaranteed; the human verification gate remains open.

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

## Theme Customization

Built-in themes: `dark`, `light`.

Create a custom theme at `%APPDATA%\Klikety\themes\mytheme.theme.json` and set `"theme": "mytheme"` in config. See `theme.schema.json` for the full schema.

## Testing

```powershell
# Unit + integration tests
dotnet test src/Klikety.Tests/Klikety.Tests.csproj

# Smoke tests (requires live display, not for CI)
dotnet test src/Klikety.SmokeTests/Klikety.SmokeTests.csproj
```

## Troubleshooting

| Problem | Solution |
|---|---|
| "Hotkey already in use" at startup | Another app registered the same hotkey. Change `hotKey` in config. |
| "Failed to install keyboard hook" | Run as a normal user (not elevated). Check antivirus isn't blocking `SetWindowsHookEx`. |
| Config validation errors | Check `%APPDATA%\Klikety\logs\` for details. Reserved keys (Escape, arrows, Return) cannot be in `horizontalKeys`/`verticalKeys`. PgUp/PgDn are also reserved for local commands when ElementHints is enabled. |
| Labels show VKey names instead of characters | Your keyboard layout may have dead keys. The fallback is intentional. |
| Log file location | `%APPDATA%\Klikety\logs\klikety-YYYYMMDD.log` (rolling daily) |

## License

MIT
