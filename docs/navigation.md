# Navigation

[Documentation](README.md) | [Getting started](getting-started.md) | [Settings](settings.md)

Press **Alt+Space** to open the default mode. Before navigating locks the mode,
use a mode chord to switch. All bindings here describe the first-run config;
Settings can change them. Shared ordered horizontal and vertical key lists drive
the labels; custom or migrated configs need not use a 10 by 10 grid.

## Modes in pictures

These are real production overlays over a synthetic demo application in an
offline Windows Sandbox. Expand a mode for native screenshots, then open an
image for full-size labels. They illustrate navigation and cursor previews,
not clicks or universal application compatibility.
[Capture procedure and provenance](design-notes/readme-demo.design.md).

<details>
<summary><strong>UniformGrid</strong> - two-key cells and progressively finer subgrids</summary>

The default grid has ten columns (`ASDFGHJKL;`) and ten rows (`QWERTYUIOP`).
Type a column key and a row key; repeat in the smaller L2/L3 grids for finer
targeting. Optional arrows move the highlighted cell, and Enter refines it.
The Level-3 threshold controls when the third grid is available.

![UniformGrid: the default 10 by 10 screen grid](screenshots/uniform-grid.png)
![UniformGrid: a local subgrid after selecting a cell](screenshots/uniform-grid-zoom.png)

</details>

<details>
<summary><strong>Crosshair</strong> - uniform horizontal and vertical axes</summary>

Open navigation, then press **N**. Axis keys select an intersection; Enter
opens a finer subgrid.

![Crosshair: labeled uniform axes over the dashboard](screenshots/crosshair.png)

</details>

<details>
<summary><strong>LogCrosshair</strong> - small steps nearby, larger steps farther away</summary>

Open navigation, then press **M**. Logarithmic axes are centered on the cursor
and recenter as you move. Cells grow farther from the center; small labels
fan out for readability.

![LogCrosshair: cursor-centered logarithmic axis bands](screenshots/log-crosshair.png)

</details>

<details>
<summary><strong>LogGrid</strong> - logarithmic cells with iterative recentering</summary>

Open navigation, then press **comma**. Each two-key selection moves the cursor
and recomputes the grid. Cells grow geometrically away from the cursor, and
very small boundary cells collapse. An action key is still required to click.

![LogGrid: cells grow outward from the cursor position](screenshots/log-grid.png)

</details>

<details>
<summary><strong>ElementHints</strong> - control labels and nested action groups</summary>

Enable ElementHints in Settings, open navigation, then press **Tab**.
Type a control label to select it or press an outlined region's number to open it.
Small groups use one key; this combo box exposes its own action, button, and
edit field without dropping any target. Enter always returns to grid.
Badges and control outlines share colors and solid/dashed/dotted patterns;
displaced badges have matching leader lines, so color is not the only cue.

These captures predate numbered region navigation; the old `+` group badges
are now numbered and have rounded region outlines.

![ElementHints: translucent control labels and plus-marked groups](screenshots/element-hints.png)
![ElementHints L2: linked one-key badges for the combo box, button, and edit](screenshots/element-hints-children.png)

</details>

## Actions and app scope

Selecting a location previews the cursor, **without clicking**. Press **Space**
for left-click, **X** for double-click, **C** for middle-click, **V** for
right-click, or **B** for move-only. Hold Shift, Ctrl, or Alt while pressing an
action key for a modified click. Bindings are configurable.

For drag-and-drop, choose the start point and press **Z**, choose the endpoint,
then press the action key for the mouse button. Left, right, and middle drags
and modifiers are supported.

Press **period** before navigating to scope the overlay to the foreground
application window. Off-screen portions are clipped to the navigation display.
All five modes can work within that area. Set `appScope.chordKey` to `null`
to disable this chord.

Labels follow the foreground application's active keyboard layout, including
DVORAK and Colemak. The VKey arrays describe physical key positions; changing
layouts does not require replacing them with printed letters. For custom
positions, edit the axes in Settings. Keep them disjoint and avoid action,
mode, help, scope, macro, and reserved keys. Labels scale with cells and use
outlined text; small cells can place labels outside with connector lines.

## Contextual help

Press **slash** or **question mark** while navigating to see the effective
commands on a split keyboard. The same key or Escape closes help without
acting or losing the selection. Other non-modifier keys close help and run
normally from the current state; modifiers alone leave it open.

Help reflects configured actions, label scheme, mode, focus/selection, drag
state, and fallback. It refreshes as ElementHints discovery progresses.
The help key and optional Shift requirement are configurable.

## ElementHints (opt-in)

Enable **Settings > Navigation > Element hints**, then **Save & apply**.
Keep UniformGrid enabled for fallback. Migration to config version 9 leaves
hints off and preserves existing defaults/bindings; conflicts are reported,
not rebound.

For a manual JSONC edit, merge this block into `modes`, then Reload from the
tray or restart:

```jsonc
"elementHints": {
  "enabled": true,
  "default": false,
  "chordKey": "Tab",
  "twoKey": true,
  "arrowKeys": true,
  "discoveryTimeoutMs": 10000,
  "cacheWindowCount": 5
}
```

Small levels use one horizontal label key; larger levels use horizontal/vertical
pairs. Numbered rounded outlines show groups of already discovered controls;
press **1-9** to switch to the matching top-level region from any depth. Its
siblings' numbers and outlines stay visible, and the active region has a thicker
outline. Nested groups open with letter labels, leaving the numbers free for
switching regions. Groups retain parent actions and independent
children. Toolbars/panes can form logical regions before labels overflow;
large target sets use suitable UIA containers or spatially partitioned groups
(250 flat targets, for example, become three groups of up to 100 controls).
Growing scans add new overflow regions without changing existing control labels.
Groups are navigation-only and cannot receive mouse actions. In this mode,
number keys no longer switch monitors and the other monitors' numbers are hidden.
Returning to grid restores monitor-number shortcuts.
The root region page stays pinned while inside a region; return to L1 with
Escape before paging to other root regions.

Arrow keys focus entries within the level, not pages. Disabling arrows does
not disable label navigation. PgUp/PgDn handle rare paging when a useful level
cannot fit. Escape clears an incomplete label, otherwise returns to the parent
level or cancels at L1. **Enter returns to grid**, including while loading, after
failure, or after mode lock. Macro setup prompts must finish before navigation
keys can run.

Hints stay near controls. Nested badges use matched outlines, border patterns,
and placement; coincident outlines are inset separately. Black halos preserve
contrast. Focus/selection shows the control's role/actions in the footer; extreme
layouts fall back to a scrolling role list. Ordinary levels show displacement
guides only for the selected target. Region numbers stay attached to their
outlines even when control labels use the right-side list. Group labels are
larger and bold, with stronger badge backings; top-level numbers never dim when
a control is selected or a first key is entered. Only a number that
cannot fit near its region uses the list too. Status-bar region outlines are
not cropped to make room for the footer. Badge fills are translucent, and a centered
spinner indicates discovery. It keeps rotating across newly discovered batches
rather than restarting each time. Region outlines cover known members, not
unfinished scan areas, so progress remains global rather than per group.

For inconsistent scans or region navigation, enable **Debug** logging and file
logging in settings. Logs are under `%APPDATA%\Klikety\logs\`.
`HintDiag` entries correlate each activation's discovery counts, source-cache
hits, remembered label assignments, list-fallback reason, region geometry and
number-key routing. These diagnostics do not include control names, values or
document text.

### Discovery, safety, and compatibility

Discovery scans only the application window captured before the overlay,
clipped to the navigation display/scope. Controls appear progressively in
breadth-first order; discovered labels stay stable and are usable before the
scan finishes. Child counts do not collapse panes or session lists; grouping
serves logical regions, label capacity and nested actions.

The default **10000 ms** deadline includes worker startup. Adjust it in
**Settings > Navigation > Element hints > Discovery timeout (ms)**; the valid
range is 100-60000 ms. Existing explicit deadlines are preserved.
Enter/Escape remain available while waiting.

An empty scan or transient provider error before any usable hints triggers one
short settling delay and retry within the **same** deadline. An empty cached
tree is refreshed before retrying. Timeouts, permission/identity failures,
protocol errors and pending cleanup are not retried; persistent failures remain
visible. This cannot guarantee recovery from a blocked provider call.

The helper caches up to **5 recently used windows** by default. Set
`cacheWindowCount` to 0-20 in Settings; 0 disables reuse. Windows of the same
app count separately. UIA change events invalidate affected branches; coarse
events or expired metadata require a fresh scan. Cache reuse never replaces
fresh action validation, and the helper retires after 30 seconds idle.

Combo assignments also have **best-effort memory** for the same number of
recent windows. Rediscovered controls in unchanged levels keep their keys,
even after the helper retires. New controls cannot take combos reserved for
controls still being discovered. Pages may have holes; empty slots do nothing.
An earlier page appearing later does not interrupt the page you are using.

Memory lasts for the current Klikety configuration/runtime, not across app
restarts. Rebuilt controls, changed groups, viewport/key changes and window
eviction can reset combos. Partial/cancelled scans remember only allocated
entries; unseen controls may lose their mappings next time. Group/page navigation
is not guaranteed to stay identical when content changes. Set `cacheWindowCount`
to 0 to disable both metadata reuse and combo memory.

Traversal stays bounded at 20000 nodes, depth 64, and 2000 targets. The independent
action-validation and cleanup deadlines remain 500 ms. Partial, empty, timeout,
provider, and permission outcomes remain visible. If helper cleanup remains
pending, the warning identifies its stage; reopen hints to retry. A replacement
helper cannot start until the old helper and pending pipe/startup work are
confirmed retired.

Before input, the helper rechecks target identity, geometry, and point ownership.
Moved, replaced, covered, or unavailable targets receive no input; reopen hints
or use grid. Move-only may use a verified interactive descendant; clicks/drags
retain stricter ownership checks. Macros remain coordinate-based, not semantic
UIA recordings.

No accessibility names, text values, or document contents are collected.
Processing is local in the bundled `uia-worker`; no elevation, UIAccess,
browser flags, or remote service is required. Missing helper files affect
hints, not grid startup; copy the whole published folder.

Not every app exposes usable UIA controls. Third-party physical-input coverage
and mixed-display/mixed-DPI compatibility are not guaranteed; native human
verification gates remain open. Screenshots are warmed-provider illustration,
not evidence that every app fits the default discovery deadline.
