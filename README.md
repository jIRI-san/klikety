# Klikety

Keyboard-driven mouse navigator for Windows. Press a hotkey, type two keys to select a screen region, then dispatch a mouse action — without touching the mouse.

![Screenshot placeholder](docs/screenshot.png)

## Features

- **Split-screen two-key grid**: Screen split in half. Left-hand keys (ASDF/WERT) control the left half, right-hand keys (JKL;/YUIO) the right. 4×4 = 16 cells per half.
- **Multi-level zoom**: Level 1 → Level 2 → Level 3 subgrids for pixel-precise targeting.
- **Arrow key navigation**: Optional arrow-key cell movement with Enter to confirm (configurable via `navigationMode`).
- **Auto-scaling labels**: Font sizes adapt to cell height (80% at L1, 90% at L2/L3). External labels with connector lines when cells get too small.
- **Configurable actions**: Space = left click (default). Bind any key to right-click, double-click, middle-click, or drag.
- **Keyboard layout aware**: Labels auto-adapt to QWERTY, DVORAK, Colemak, or any layout via Win32 `ToUnicodeEx`.
- **Theme support**: Built-in dark and light themes. Create custom `.theme.json` files.
- **System tray**: Runs in the tray with About, Open Config, Start with Windows, and Quit.
- **JSONC config**: Comments allowed in `config.json`. Schema-validated with `config.schema.json`.

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
| `keySets.left.firstKeys` | VKey[] | `["A","S","D","F"]` | Left half column selection keys |
| `keySets.left.secondKeys` | VKey[] | `["W","E","R","T"]` | Left half row selection keys |
| `keySets.right.firstKeys` | VKey[] | `["J","K","L","OemSemicolon"]` | Right half column selection keys |
| `keySets.right.secondKeys` | VKey[] | `["Y","U","I","O"]` | Right half row selection keys |
| `actionBindings` | object | `{}` | Map VKey names to actions: `LeftClick`, `RightClick`, `DoubleClick`, `MiddleClick`, `DragStart`, `DragEnd` |
| `level3CellSizeThreshold` | int | `0` | Cell area (px²) above which level-3 subgrid activates. 0 = always active. |
| `logLevel` | string | `"Warning"` | Log level: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, `None` |
| `navigationMode` | string | `"Both"` | `TwoKey` (labels only), `Arrow` (arrows only), `Both` |
| `theme` | string | `"dark"` | Theme name or relative path to `.theme.json` |
| `fileLoggingEnabled` | bool | `false` | Enable file logging to `%APPDATA%\Klikety\logs\`. No log folder created when disabled. |
| `retainedLogFileCount` | int | `7` | Max rolling log files kept. Oldest deleted when exceeded. Only applies when `fileLoggingEnabled` is `true`. |
| `minLabelFontSize` | double | `10.0` | Min label font size (DIP). When subgrid cells are too small, labels render outside the grid with connector lines. |

### Example: Custom Action Bindings

```jsonc
{
    "actionBindings": {
        "Space": "LeftClick",     // default, can be omitted
        "X": "RightClick",
        "C": "DoubleClick",
        "V": "MiddleClick",
        "Z": "DragStart",
        "B": "DragEnd"
    }
}
```

### Keyboard Layout Setup

For **DVORAK**:

```jsonc
{
    "keySets": {
        "left": {
            "firstKeys": ["A", "O", "E", "U"],
            "secondKeys": ["OemComma", "OemPeriod", "P", "Y"]
        },
        "right": {
            "firstKeys": ["H", "T", "N", "S"],
            "secondKeys": ["F", "G", "C", "R"]
        }
    }
}
```

For **Colemak**:

For **Colemak**:

```jsonc
{
    "keySets": {
        "left": {
            "firstKeys": ["A", "R", "S", "T"],
            "secondKeys": ["W", "F", "P", "G"]
        },
        "right": {
            "firstKeys": ["N", "E", "I", "O"],
            "secondKeys": ["J", "L", "U", "Y"]
        }
    }
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
