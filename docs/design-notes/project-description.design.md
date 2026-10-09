---
description: Current project overview, supported navigation modes, source structure and documentation boundaries; keep README aligned.
globs:
  - README.md
  - src/Klikety/**
  - src/Klikety.UiaWorker/**
---

# Klikety Project Overview

Klikety is a .NET 10 WPF tray application for keyboard-driven physical mouse
navigation on Windows. It does not require elevation, UIAccess or remote services.
The default activation hotkey is Alt+Space; selecting a location does not click.
Action bindings dispatch clicks, cursor-only moves or two-point drags afterward.

## Navigation

| Mode | Default chord | Behavior |
|---|---|---|
| UniformGrid | Default mode | 10 horizontal × 10 vertical keys; progressively smaller L2/L3 grids, with small-cell key reduction. |
| Crosshair | N | Uniform axis bands; select an intersection and use Enter for finer navigation. |
| LogCrosshair | M | Cursor-centered logarithmic axes that recenter during navigation. |
| LogGrid | Comma | Log-scaled two-key cells; selection recenters without automatically clicking. |
| ElementHints | Tab, opt-in | Foreground-window UIA controls; adaptive one/pair labels, `+` groups and nested badges linked by color, border pattern and placement. |

Shared axes are root `horizontalKeys` and `verticalKeys`; migrated/custom configs
need not have 100 cells. Labels follow the active keyboard layout. Modes switch
before navigation locks them. ElementHints always retains Enter grid fallback.
Its optional arrows focus entries, while PgUp/PgDn handle rare paging; Escape
clears a partial label, otherwise pops a level or cancels.

Discovery uses a separately owned, bounded UIA worker, not the WPF dispatcher.
Badge fills are 40% opaque; a centered spinner shows pending discovery.
Controls appear progressively in breadth-first order; child counts do not collapse
panes or session lists. Capacity groups/pages keep labels readable and stable.
`modes.elementHints.discoveryTimeoutMs` defaults to 10000 ms, including startup,
and accepts 100-60000 ms. Validation and cleanup remain 500 ms. Fresh identity,
geometry and point ownership checks gate physical actions; cursor-only moves
also permit verified interactive descendants. Group entries never dispatch input.

## Source and Contracts

- `src/Klikety`: WPF overlay, tray/settings, configuration, native services,
  navigation, help, macros and key-press HUD.
- `src/Klikety.UiaWorker`: windowless provider traversal and fresh point validation.
- `src/Klikety.Tests`: managed regressions, protocol/worker fixtures and STA rendering.
- `src/Klikety.SmokeTests`: live-display checks, separate from ordinary CI tests.
- `.github/skills/capture-demo`: repo-specific `/capture-demo` workflow.
- `scripts/readme-demo`: offline Sandbox demo application, guarded capture,
  read-only hierarchy probe, lossless APNG encoding, browser verification and
  automatic gallery publication with per-run provenance.

The [root index](.design-notes.md) lists authoritative subsystem notes.
[README](../../README.md) is the user-facing overview and mode gallery.
[Demo capture](readme-demo.design.md) distinguishes illustrative native screenshots
from application compatibility, physical-input and DPI acceptance evidence.
The README animation covers all five modes, nested hints and grid refinement;
external captions identify its eleven captured views.
Those human verification gates remain open.

Archived implementation plans, prototype notes, the old monolithic navigator
note and maintenance reports are history, not current behavior specifications.
Preserve their evidence and qualify superseded statements rather than silently
rewriting past results. Keep README synchronized when this overview changes.
