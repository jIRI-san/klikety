# Klikety

Keyboard-driven mouse navigator for Windows. Press a hotkey, type two keys to select a screen region, then dispatch a mouse action — without touching the mouse.

Also without touching the code. This paragraph is the only one I have written manually, the rest is AI generated as a test of how well things works end-to-end with more systemic approach to plans and to test and validate some skills which will come handy later. (It kinda works until it does not, so I need to up my plan-writing game significantly to be able to develop things without any passive-aggressive steering...)

![Screenshot placeholder](docs/screenshot.png)

## Features

- **Unified 8×8 grid**: Full-screen grid with 8 columns (ASDFJKL;) and 8 rows (WERTYUIO). Press two keys to select any of 64 cells.
- **Multi-level zoom**: Level 1 → Level 2 → Level 3 subgrids for pixel-precise targeting.
- **Four navigation modes**: UniformGrid (two-key grid), Crosshair (axis-based), LogCrosshair (logarithmic center-focused), and LogGrid (iterative log-scaled). Switch modes with chord keys while overlay is active.
- **LogCrosshair mode**: Logarithmically-scaled grid centered on cursor. Recenters on every navigation. Cross-arm cells grow proportionally for label readability.
- **LogGrid mode**: 10×10 log-scaled grid with iterative recentering. Two-key selection moves cursor and recomputes grid. Cells grow geometrically from center; sub-5px boundary cells collapse automatically. Explicit action dispatch (Space/X/C/V).
- **Arrow key navigation**: Optional arrow-key cell movement with crosshair highlight. Enter zooms into a cell; action keys (Space) click directly.
- **Auto-scaling labels**: Font sizes adapt to cell height (80% at L1, 90% at L2/L3). External labels with connector lines when cells get too small.
- **Outlined text**: Two-layer stroke+fill rendering ensures label readability over any background.
- **Configurable actions**: Space = left click (default). Bind any key to right-click, double-click, middle-click, move-only (cursor move without click), or drag-and-drop.
- **Modifier-aware clicks**: Hold Shift, Ctrl, or Alt while pressing an action key to send modified clicks (Shift+click, Ctrl+click, etc.).
- **Drag-and-drop**: Two-point drag flow — navigate to start, press drag key, navigate to end, press action key. Supports left/right/middle drag with modifiers.
- **Global scroll hotkeys**: Optional global hotkeys for mouse wheel scrolling at cursor position (default: Ctrl+Alt+PageUp/PageDown). Configurable keys and scroll amount.
- **Keyboard layout aware**: Labels auto-adapt to QWERTY, DVORAK, Colemak, or any layout via Win32 `ToUnicodeEx`.
- **Theme support**: Built-in dark and light themes. Create custom `.theme.json` files.
- **System tray**: Runs in the tray with About, Open Config, Reset Configuration, Start with Windows, Show Key Presses, Pause/Resume Scroll Keys, and Quit.
- **Key press visualization**: Runtime-toggled floating HUD showing recent key presses with outlined text. Modifier combos shown as "Ctrl+C", repeated keys collapsed ("A ×3"), oldest-first staggered fade. Click-through, follows active monitor. Configurable font, color, corner, and timing.
- **JSONC config**: Comments allowed in `config.json`. Schema-validated with `config.schema.json`.
- **Debug logging**: All keystrokes and state transitions logged to `%APPDATA%\Klikety\logs\`.

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

### Configuration Reference

| Field | Type | Default | Description |
|---|---|---|---|
| `hotKey.modifiers` | string (flags) | `"Alt"` | Modifier keys: `Alt`, `Control`, `Shift`, `Win` (combine with `,`) |
| `hotKey.key` | string (VKey) | `"Space"` | Trigger key (any VKey name) |
| `firstKeys` | VKey[] | `["A","S","D","F","J","K","L","OemSemicolon"]` | Column selection keys (8 keys = 8 columns) |
| `secondKeys` | VKey[] | `["W","E","R","T","Y","U","I","O"]` | Row selection keys (8 keys = 8 rows) |
| `actionBindings` | object | `{}` | Map VKey names to actions: `LeftClick`, `RightClick`, `DoubleClick`, `MiddleClick`, `MoveOnly`, `DragDrop` |
| `scrollHotkeys.enabled` | bool | `false` | Enable global scroll hotkeys |
| `scrollHotkeys.scrollUpKey` | HotKeyConfig | Ctrl+Alt+PageUp | Scroll up hotkey |
| `scrollHotkeys.scrollDownKey` | HotKeyConfig | Ctrl+Alt+PageDown | Scroll down hotkey |
| `scrollHotkeys.scrollAmount` | int | `3` | Wheel ticks per hotkey press (1–100) |
| `level3CellSizeThreshold` | int | `0` | Cell area (px²) above which level-3 subgrid activates. 0 = always active. |
| `modes` | object | see below | Navigation mode config. Each mode has `enabled`, `default`, `chordKey`, `twoKey`, `arrowKeys`. |
| `modes.logCrosshair.logBaseSize` | int | `20` | Base cell size (px) for LogCrosshair center cell. Range: 2–50. |
| `modes.logGrid.logGridBaseSize` | int | `10` | Base cell size (px) for LogGrid center cells. Range: 2–50. |
| `navigationMode` | string | `"both"` | Legacy field. Migrated to `modes.uniformGrid` on first load. |
| `theme` | string | `"dark"` | Theme name or relative path to `.theme.json` |
| `logLevel` | string | `"Warning"` | Log level: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, `None` |
| `fileLoggingEnabled` | bool | `false` | Enable file logging to `%APPDATA%\Klikety\logs\`. |
| `retainedLogFileCount` | int | `7` | Max rolling log files kept. Oldest deleted when exceeded. Only applies when `fileLoggingEnabled` is `true`. |
| `minLabelFontSize` | double | `14.0` | Min label font size (DIP). When subgrid cells are too small, labels render outside the grid with connector lines. |
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

### Example: Custom Action Bindings

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

For **DVORAK**:

```jsonc
{
    "firstKeys": ["A", "O", "E", "U", "H", "T", "N", "S"],
    "secondKeys": ["OemComma", "OemPeriod", "P", "Y", "F", "G", "C", "R"]
}
```

For **Colemak**:

```jsonc
{
    "firstKeys": ["A", "R", "S", "T", "N", "E", "I", "O"],
    "secondKeys": ["W", "F", "P", "G", "J", "L", "U", "Y"]
}
```

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
| Config validation errors | Check `%APPDATA%\Klikety\logs\` for details. Reserved keys (Escape, arrows, Return) cannot be in `firstKeys`/`secondKeys`. |
| Labels show VKey names instead of characters | Your keyboard layout may have dead keys. The fallback is intentional. |
| Log file location | `%APPDATA%\Klikety\logs\klikety-YYYYMMDD.log` (rolling daily) |

## License

MIT
