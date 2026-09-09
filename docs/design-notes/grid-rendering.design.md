---
description: Grid rendering — DIP-space computation, font auto-scaling, outlined text, external labels, and theme system.
globs:
  - src/Klikety/Overlay/GridRenderer.cs
  - src/Klikety/Overlay/LogCrosshairRenderer.cs
  - src/Klikety/Overlay/LogGridRenderer.cs
  - src/Klikety/Overlay/OverlayWindow.xaml
  - src/Klikety/Overlay/OverlayWindow.xaml.cs
  - src/Klikety/Overlay/OverlayHost.cs
  - src/Klikety/Overlay/SatelliteWindow.cs
  - src/Klikety/Config/ThemeLoader.cs
  - src/Klikety/Config/ThemeModel.cs
---

# Grid Rendering

## Overlay placement

`OverlayPlacement.Place` `SetWindowPos`es to physical `rcMonitor`, then sets Width/Height from `VisualTreeHelper.GetDpi` — not `Left`/`Top` (those are DIP of the *previous* monitor and send the HWND off-screen, e.g. −6144). Renderers map physical cells **relative to the overlay HWND origin** (`OverlayDip.WindowOrigin`), not the cell union — app-scope grids sit inside the window, not at the monitor’s top-left. `TransformFromDevice` of desktop coordinates is not canvas space. Scale is re-read every paint. Session activate waits for `DispatcherPriority.Loaded`. Satellites: `ShowActivated=false`, `SWP_NOACTIVATE`, dim fill ≤35%.

## IGridRenderer Interface

```csharp
interface IGridRenderer {
    void SetTransform(Matrix m);
    void RenderGrid(IReadOnlyList<GridCell> cells);
    void HighlightColumn(IReadOnlyList<GridCell> cells, int col);
    void HighlightCell(IReadOnlyList<GridCell> cells, GridCell cell);
    void RenderSubgrid(IReadOnlyList<GridCell> cells);
    void RenderSubgridOverGrid(IReadOnlyList<GridCell> backgroundCells, IReadOnlyList<GridCell> subgridCells);
    void HighlightColumnOverGrid(IReadOnlyList<GridCell> backgroundCells, IReadOnlyList<GridCell> subgridCells, int col);
    void HighlightCellOverGrid(IReadOnlyList<GridCell> backgroundCells, IReadOnlyList<GridCell> subgridCells, GridCell cell);
    void FlashInvalidKey(); void ClearCanvas();
}
```

`IGridRenderer` is extracted from `GridRenderer` for testability. `NavigatorCoordinator` takes `IGridRenderer?` — null-safe (all calls use `?.`). `FakeGridRenderer` records all calls for assertion in tests. `GridRenderer` constructor: `(Canvas, ThemeModel, LabelGenerator, double minLabelFontSize)`.

## DIP-Space Grid Computation

`GridRenderer` works entirely in DIP (device-independent pixel) space to avoid scaling artifacts at non-100% DPI:

- `EnsureTransform()` auto-reads the device→DIP matrix from `PresentationSource.FromVisual(_canvas)` on first render. Falls back to identity if unavailable.
- `ComputeRegionFromCells(cells)` transforms only the two corners (top-left of first cell, bottom-right of last cell) to DIP, producing a `Rect` region.
- `DipRectForCell(row, col, region, cols, rows)` subdivides the region evenly — no per-cell integer rounding, so cells tile perfectly at any DPI.

## Font Auto-Scaling

Labels auto-scale to fill a fraction of cell height:

- **L1 grid**: 80% of cell height (`heightFraction = 0.8`)
- **L2/L3 subgrids**: 90% of cell height (`heightFraction = 0.9`) for tighter packing before switching to external labels
- Primary constraint is height (cells are wider than tall), with a secondary cap at 95% of half-width to prevent horizontal overflow.
- Font size clamped to `[minLabelFontSize .. theme.LabelFontSize * 3]`.
- Method: `ComputeAutoFontSize(cellHalfWidth, cellHeight, heightFraction)`.

## Unified Grid Rendering

- `RenderGrid(cells)` renders the full-screen grid with labels. Used at L1 activation and on escape back to L1.
- `HighlightColumn(cells, col)` dims non-matching cells, highlights selected column. Used at L1.
- `HighlightCell(cells, cell)` crosshair-highlights a cell for L1 arrow navigation (row + column + intersection).
- `HighlightCellOverGrid(backgroundCells, subgridCells, cell)` crosshair-highlights a cell within a subgrid rendered over faint background grid. Used for L2/L3 arrow navigation. Supports external labels when cells are too small.
- `ClearCanvas()` — removes all children from the WPF Canvas. Called in `DeactivateOverlay()` before `Hide()`.
- `NavigatorCoordinator` tracks `_l1Cells`, `_subgridCells` (nullable), `_l2SubgridCells` for rendering context. L1 uses `HighlightColumn`/`RenderGrid`; L2+ uses `HighlightColumnOverGrid`. `OnCellEntered` stores subgrid cells; `OnLevelExited` (L3→L2) restores `_subgridCells = _l2SubgridCells`; `OnColumnUnhighlighted` at L1 clears subgrid state and calls `RenderGrid`.
- `RenderSubgridOverGrid(backgroundCells, subgridCells)` — renders parent grid as faint borders (no labels, 0.15 opacity) then subgrid on top with labels. Called by coordinator on `CellEntered`.
- `HighlightColumnOverGrid(backgroundCells, subgridCells, col)` — renders parent grid as faint background + subgrid with column highlighted. Called by coordinator on `ColumnHighlighted` at L2/L3.
- Private helpers: `RenderBackgroundGrid(cells)` draws faint borders, `RenderSubgridContent(cells)` draws subgrid without clearing canvas.
- All render methods share `AddCellRect(dipRect, fill, stroke, strokeThickness)` private helper — creates `Rectangle` shape and adds to Canvas.

## Outlined Text Rendering

All labels (inline and external) use two-layer `Path` rendering for crisp outlines that ensure readability over any background:

1. `FormattedText.BuildGeometry()` converts text to a `Geometry`.
2. Layer 1 (stroke-only): `Path` with `Fill=Transparent`, `Stroke=outlineBrush`, `StrokeThickness=thickness*2`, `StrokeLineJoin=Round`. Doubled thickness because only the outer half is visible.
3. Layer 2 (fill-only): `Path` with `Fill=foreground`, no stroke. Renders on top, covering the inner stroke bleed.

Method: `AddOutlinedText(text, typeface, fontSize, fill, outlineBrush, outlineThickness, opacity, areaX, areaY, areaWidth, areaHeight)`. Helper `MeasureText` returns `Size` for layout calculations without creating visual elements.

- Theme properties: `LabelOutlineColor` (default `#000000` dark / `#FFFFFF` light), `LabelOutlineThickness` (default `1.5`).

## External Label Rendering

When subgrid cells are too small to fit labels, the renderer switches to external label layout. Decision considers both dimensions:
- Height: `cellDipHeight < minLabelFontSize * 1.8`
- Half-width: `cellDipWidth / 2 < minLabelFontSize * 1.6` (wide chars like "W")

Method: `ShouldUseExternalLabels(cellDipHeight, cellDipWidth, minLabelFontSize)` — `internal static`, testable.

**Progressive reveal**: When awaiting first key, only column first-key labels are shown (top + bottom). After first key is pressed (`HighlightColumnOverGrid`), row second-key labels appear (left + right). This matches the natural key-entry order.

**Fan-out algorithm**: With 8 labels per side, labels at the grid edge may overlap. `ComputeFanOut(labelCount, maxLabelSize, gridExtent, standardMargin)` computes:
- `distance`: how far from the grid edge to place the label line. When labels fit at standard spacing, equals `standardMargin` (fontSize × 1.5). When labels would overlap, increases by half the extra width needed.
- `extent`: total width/height to spread labels across. Equals `gridExtent` when labels fit, otherwise `labelCount × (maxLabelSize + minGap)`.

Labels are evenly spaced across `extent`, centered on the grid center. Angled dashed connector lines link each label to its column/row center at the grid edge. When no fan-out is needed, connectors are straight (vertical for columns, horizontal for rows).

**Screen-edge-aware direction**: Labels only render on sides with enough space:
- Column labels: skip above if `gridTop < fanOutDist`; skip below if `screenHeight - gridBottom < fanOutDist`.
- Row labels: skip left if `gridLeft < fanOutDist`; skip right if `screenWidth - gridRight < fanOutDist`.
- If neither side has space, render both (clipped).

When external labels are active, no internal cell labels are rendered — cells show only background/highlight rectangles.

**Fan-out extent clamping**: `labelExtentStart` is clamped to `[0, screenDimension - labelExtent]` so labels never render off-screen when the subgrid is near a screen edge.

**Font size**: `ComputeExternalFontSize` uses `max(cellBased, minLabelFontSize)` — at L3 where cell-based auto-size would be tiny, the floor of `minLabelFontSize` (default 10 DIP) ensures readable labels.

**L3 legibility**: When `useExternalLabels` is true (cells too small for inline labels):
- Grid border opacity reduced to 30% and thickness halved (min 0.5px) — keeps grid structure visible without obscuring content.
- Column highlight fill at 30% opacity (vs 50% at L2) for better see-through.
- Alternating row bands (12% opacity, every other row) provide cross-hair visual aid during column highlight.

- Theme properties: `ExternalColLabelColor` (column labels, default `#FFCC00`), `ExternalRowLabelColor` (row labels, default `#66CCFF`), `ConnectorLineColor`, `ConnectorLineThickness`.
- Config: `MinLabelFontSize` (default 14.0 DIP) controls both the external-label threshold and the font floor.
- All renderers (GridRenderer, CrosshairRenderer, LogGridRenderer, LogCrosshairRenderer) use distinct brushes for column vs row external labels to differentiate axes visually.

## Theme System

- `ThemeModel` POCO: label font family/size/color/weight; cell border color + thickness; normal cell background color + opacity; dimmed cell overlay color + opacity; highlighted column background + border color; subgrid distinct border/label color; external label color (columns); external row label color (rows); connector line color + thickness; label outline color + thickness; small-cell background color + opacity.
- `ThemeLoader` resolves `"theme"` config value: bare name → `%APPDATA%\Klikety\themes\<name>.theme.json`; relative path → resolved from config folder only; must have `.theme.json` extension; path canonicalized; traversal sequences (`../`) rejected; rooted/absolute paths rejected via `Path.IsPathRooted`; fall back to built-in dark on any error + tray notification.
- Built-in `dark.theme.json` and `light.theme.json` shipped as embedded resources; extracted to `%APPDATA%\Klikety\themes\` on first run.

## GridRenderer Safety

- `AddLabel` bounds-checks `row + _labelRowOffset` and `col + _labelColOffset` against `_labelGenerator.Rows`/`Cols` before calling `LabelFor`. Out-of-range offsets (from `DynamicKeyReducer` edge cases) are silently skipped instead of throwing.

## LogCrosshairRenderer

- **Font sizing**: `ComputeGradualFontSize(Rect dipRect, double baseFontSize)` — font size = `cellExtent * 0.7` (where `cellExtent = min(width, height)`), minimum 1.0. Log grid cells encode distance from center via size, so larger cells naturally get bigger labels.
- **Border thickness**: `ScaledBorderThickness(Rect dipRect)` — `extent * 0.02 + theme.CellBorderThickness * 0.5`, clamped to `[1×, 4×]` of theme thickness. Scales with cell size for readability.
- **Element pooling**: Rectangles, text paths, and dashed connector lines reused across renders via index tracking (`_nextRect`, `_nextText`, `_nextLine`). Staleness detected via `Parent == null` after external canvas clear — pools reset on next render.
- **Flash animation**: `FlashInvalidKey` stops any in-flight animation (`BeginAnimation(null)`) before starting a new one, preventing handler accumulation on rapid key spam.
- **External labels**: When cross-arm cells are too small for inline labels, external labels render outside the small-cell zone with dashed connector lines. Same threshold as LogGridRenderer: `IsSmallCell(dipRect, minLabelFontSize)` checks `height < minLabelFontSize * 1.8` or `halfWidth < minLabelFontSize * 1.6`. Additional axis-specific checks: `IsNarrowColumn` (width only), `IsShortRow` (height only).
  - `RenderExternalColumnLabels`: scans center row for narrow columns (skips CenterCol — center bullet always inline). Computes bounding box of narrow cells, places labels above/below with overlap resolution via `LogGridRenderer.ResolveOverlaps`. Screen-edge-aware: skips side with insufficient room, fallback renders at least one side.
  - `RenderExternalRowLabels`: scans center column for short rows (skips CenterRow). Labels placed left/right of bounding box, same overlap resolution and screen-edge checks.
  - Opacity per render state mirrors inline label opacity: `RenderCross` all 1.0; `HighlightColumn` highlighted col 1.0, others 0.4, all rows 1.0; `HighlightRow` highlighted row 1.0, others 0.4, all cols 1.0; `HighlightCell` target axis 1.0, others 0.3. Connector line opacity matches its label.
  - Both axes rendered simultaneously in all render states.
  - Uses `_extLabelBrush` (column labels), `_extRowLabelBrush` (row labels), `_connectorBrush` from theme.
  - `ExternalFontSize()` = `max(minLabelFontSize, theme.LabelFontSize * 0.85)` — same formula as LogGridRenderer.

## LogGridRenderer

`LogGridRenderer` renders the LogGrid mode overlay with element pooling (rectangles, outlined text paths, dashed connector lines).

**Cell rendering** (`RenderCells`):
- Skips cells with `Width <= 0` or `Height <= 0` (zero-width cells from collapse). Does NOT skip small-but-positive cells — those are the boundary cells absorbing collapsed edge cells.
- `IsSmallCell`: cells where `Height < minLabelFontSize * 1.8` or `Width / 2 < minLabelFontSize * 1.6` — used for distinct background tinting.
- Inline labels rendered inside cells large enough to fit them; small cells show background only.

**External labels** (border column/row labels):
- **Progressive reveal**: Grid render → column labels only. Column highlight → row labels appear. Cell highlight → both axes.
- `RenderBorderColumnLabels`: Finds narrow columns (`IsNarrowColumn`), computes label positions at column centers, resolves overlaps via `ResolveOverlaps`, renders labels above/below the inner 4×4 cell area with dashed connector lines.
- `RenderBorderRowLabels`: Same for short rows. Labels left/right of inner 4×4 area. Uses `_extRowLabelBrush` (distinct from column `_extLabelBrush`).
- `ResolveOverlaps(positions, sizes, min, max, anchorCenter)`: iterative push-apart algorithm. Gap = `max(8.0, (max - min) * 0.01)`. Forward + backward passes up to 10 iterations. Re-centers group around anchor midpoint. Clamps to bounds.
- Guide (connector) lines: dashed, end at cell center ±1 from grid center (not at grid edge).
- Direction-aware: only renders on sides with sufficient room (above/below for columns, left/right for rows). Fallback: at least one side always renders.

**Element pooling**: Same pattern as LogCrosshairRenderer — `_rectPool`, `_textPool`, `_linePool` with `_nextRect`/`_nextText`/`_nextLine` indices. `UseRect`, `UseLabel`, `UseLine` reuse existing elements. Staleness detection via `Parent == null`.
