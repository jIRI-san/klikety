# Klikety

Keyboard-driven mouse navigator for Windows. Press a hotkey, type two keys to select a screen region, then dispatch a mouse action — without touching the mouse.

![Screenshot placeholder](docs/screenshot.png)

## Features

- **Two-key grid navigation**: 9×8 grid covering the full primary monitor. First key selects column, second key selects row.
- **Multi-level zoom**: Level 1 → Level 2 → Level 3 subgrids for pixel-precise targeting.
- **Arrow key navigation**: Optional arrow-key cell movement with Enter to confirm (configurable via `navigationMode`).
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
| `keySets.firstKeys` | VKey[] | `["A","S","D","F","G","H","J","K","L"]` | Column selection keys (9 keys) |
| `keySets.secondKeys` | VKey[] | `["W","E","R","T","Y","U","I","O"]` | Row selection keys (8 keys) |
| `actionBindings` | object | `{}` | Map VKey names to actions: `LeftClick`, `RightClick`, `DoubleClick`, `MiddleClick`, `DragStart`, `DragEnd` |
| `level3CellSizeThreshold` | int | `40000` | Cell area (px²) above which level-3 subgrid activates |
| `logLevel` | string | `"Warning"` | Log level: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, `None` |
| `navigationMode` | string | `"Both"` | `TwoKey` (labels only), `Arrow` (arrows only), `Both` |
| `theme` | string | `"dark"` | Theme name or relative path to `.theme.json` |
| `fileLoggingEnabled` | bool | `false` | Enable file logging to `%APPDATA%\Klikety\logs\`. No log folder created when disabled. |
| `retainedLogFileCount` | int | `7` | Max rolling log files kept. Oldest deleted when exceeded. Only applies when `fileLoggingEnabled` is `true`. |

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
        "firstKeys": ["A", "O", "E", "U", "I", "D", "H", "T", "N"],
        "secondKeys": ["OemComma", "OemPeriod", "P", "Y", "F", "G", "C", "R"]
    }
}
```

For **Colemak**:

```jsonc
{
    "keySets": {
        "firstKeys": ["A", "R", "S", "T", "D", "H", "N", "E", "I"],
        "secondKeys": ["W", "F", "P", "G", "J", "L", "U", "Y"]
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
