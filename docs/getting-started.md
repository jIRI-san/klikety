# Getting Started

[Documentation](README.md) | [Navigation](navigation.md) | [Settings and files](settings.md)

## Install

Klikety requires **Windows 10 or later**. Download a Windows package from
[Releases](https://github.com/jIRI-san/klikety/releases), extract the entire
package to a permanent folder, and run `Klikety.exe`. Keep the `uia-worker`
folder and other published files beside the executable. Self-contained builds
do not need a separate .NET runtime installation.

A folder such as `%LOCALAPPDATA%\Klikety\` works for the application files.
Preferences live separately under `%APPDATA%\Klikety\`; first startup creates
the default config, schemas, macro file, and themes. See
[folders and files](settings.md#folders-and-files).

The app stays in the system tray. Right-click its icon for **Settings...**,
**About**, **Open Configuration Folder**, **Reload Configuration**, and **Quit**.
Turn on **Start with Windows** there if wanted; this uses the current executable
location, so keep the installed folder stable. No elevation is required.

To build instead, see [Development](development.md).

## Choose your first target

With the default config:

1. Focus the application you want to use and press **Alt+Space**.
2. In UniformGrid, type the displayed column key, then the row key. Repeat in
   the smaller grid to refine the point.
3. Press **Space** to left-click, or another action key from the table below.

Selection moves the cursor without clicking. Escape backs out of a navigation
level or cancels at the outer level. Open **help** while navigating to see the
effective commands for your mode and selection.

## Default controls

These are first-run bindings, not a requirement for migrated or custom configs.
Mode/scope chords work before navigation locks the current mode.

| Key | Command |
|---|---|
| Alt+Space | Open navigation. |
| N / M / comma | Switch to Crosshair / LogCrosshair / LogGrid. |
| Tab | Switch to ElementHints, once enabled in Settings. |
| Period | Scope navigation to the foreground application window. |
| Space / X / C / V | Left / double / middle / right click. |
| B | Move only. |
| Z | Start a two-point drag; choose its end and press an action key for the button. |
| Arrows | Move focus/selection, if enabled for the current mode. |
| Enter | Grid refinement where supported; always grid fallback in ElementHints. |
| Escape | Back out or cancel; closes help first if it is open. |
| Slash or question mark | Open/close contextual help. |
| Ctrl+Alt+PageUp / PageDown | Scroll up/down, after enabling Scrolling in Settings. |
| Ctrl+Alt+Shift+M | Open the macro picker. |

Printed labels adapt to the active keyboard layout. Use the displayed label
rather than assuming QWERTY letters. Hotkeys and bindings can be changed in
Settings; collisions are reported rather than silently stealing a command.

## Tray toggles

**Show Key Presses** enables a click-through keyboard HUD on the active monitor.
It starts off each time the app launches. Its appearance is configured in
Settings, but enablement is not saved. It can expose sensitive input on screen.

**Pause Scroll Keys** / **Resume Scroll Keys** appears when scrolling is enabled.
It temporarily releases/re-registers the scroll shortcuts without changing JSON.

**Reset Configuration** appears only when blocking config problems exist.
Use Settings or the [troubleshooting guide](settings.md#troubleshooting) first;
reset replaces preferences with defaults.
