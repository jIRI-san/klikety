# Domain Model

## Terms and meanings

- **Active display** — a display Windows currently enumerates as attached and desktop-visible (`EnumDisplayMonitors`). Not a disconnected CCD target.
- **Navigation overlay** — the existing `OverlayWindow` session: grid/crosshair/log modes, hook owner, focus owner.
- **Satellite overlay** — a separate click-through window on a non-navigation display showing that display’s number.
- **DevicePath** — CCD monitor device path from `DisplayConfigGetDeviceInfo` (`DISPLAYCONFIG_TARGET_DEVICE_NAME.monitorDevicePath`). Identity key. Not `HMONITOR`, not `\\.\DISPLAYn`.
- **Topology fingerprint** — ordinal-sorted unique DevicePaths of the current active set, joined. Position and which display hosts navigation are not part of the fingerprint.
- **Display number** — integer 1–9 shown on satellites and bound to `VKey.D1`–`VKey.D9`. Unnumbered displays (10th+) cannot be selected by key.
- **Overlay host** — owns the navigation overlay plus all satellite windows for one activation. Coordinator still owns session/hook lifecycle.

## Actors and boundaries

- Coordinator: activation, hook, digit dispatch, display-change dismiss, app-scope clip target.
- Overlay host: create/show/hide/close per-display windows; never `Activate()` satellites.
- Display catalog: enumerate active displays (bounds, DPI, DevicePath, GDI name). Fail closed on identity mismatch.
- Topology store: read/write `%APPDATA%\Klikety\display-topologies.json`. Not `config.json`.
- `MouseActionService`: virtual-desktop absolute coordinates. Macros keep primary `IScreenBoundsProvider` (non-goal).

## Interfaces and ownership

- New `IDisplayCatalog` on `IPlatformServices`. Fake in tests. Do not overload `IMonitorService` (HUD work-area helper).
- `IOverlayWindow` remains the navigation overlay seam. Satellites are not `IOverlayWindow`.
- Digit keys reserved in `ConfigLoader` the same way Escape/arrows are reserved today, scoped to D1–D9.

## Invariants

- Same fingerprint → same numbers, independent of navigation-host display and of overlay open/close.
- New fingerprint (including a different dock’s set, or a partial unplug) → spatial 1..N by `(left, top, DevicePath)`. No reserved holes.
- Rearrange in Windows Settings with the same DevicePath set does **not** renumber (fingerprint ignores rects).
- One navigation overlay. Satellites never take keyboard focus.
- Geometry for grid/session is that display’s `rcMonitor` physical pixels. DIP conversion is per window `PresentationSource`.
