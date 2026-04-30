---
description: Grid rendering — DIP-space computation, font auto-scaling, outlined text, external labels, and theme system.
globs:
  - src/Klikety/Overlay/GridRenderer.cs
  - src/Klikety/Overlay/OverlayWindow.xaml
  - src/Klikety/Overlay/OverlayWindow.xaml.cs
  - src/Klikety/Config/ThemeLoader.cs
  - src/Klikety/Config/ThemeModel.cs
---

# Grid Rendering

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

- Theme properties: `ExternalLabelColor`, `ConnectorLineColor`, `ConnectorLineThickness`.
- Config: `MinLabelFontSize` (default 14.0 DIP) controls both the external-label threshold and the font floor.

## Theme System

- `ThemeModel` POCO: label font family/size/color/weight; cell border color + thickness; normal cell background color + opacity; dimmed cell overlay color + opacity; highlighted column background + border color; subgrid distinct border/label color; external label color; connector line color + thickness; label outline color + thickness.
- `ThemeLoader` resolves `"theme"` config value: bare name → `%APPDATA%\Klikety\themes\<name>.theme.json`; relative path → resolved from config folder only; must have `.theme.json` extension; path canonicalized; traversal sequences (`../`) rejected; rooted/absolute paths rejected via `Path.IsPathRooted`; fall back to built-in dark on any error + tray notification.
- Built-in `dark.theme.json` and `light.theme.json` shipped as embedded resources; extracted to `%APPDATA%\Klikety\themes\` on first run.

## GridRenderer Safety

- `AddLabel` bounds-checks `row + _labelRowOffset` and `col + _labelColOffset` against `_labelGenerator.Rows`/`Cols` before calling `LabelFor`. Out-of-range offsets (from `DynamicKeyReducer` edge cases) are silently skipped instead of throwing.

## LogCrosshairRenderer

- **Font sizing**: `ComputeGradualFontSize(Rect dipRect, double baseFontSize)` — font size = `cellExtent * 0.7` (where `cellExtent = min(width, height)`), minimum 1.0. Log grid cells encode distance from center via size, so larger cells naturally get bigger labels.
- **Border thickness**: `ScaledBorderThickness(Rect dipRect)` — `extent * 0.02 + theme.CellBorderThickness * 0.5`, clamped to `[1×, 4×]` of theme thickness. Scales with cell size for readability.
- **Element pooling**: Rectangles and text paths reused across renders via index tracking (`_nextRect`, `_nextText`). Staleness detected via `Parent == null` after external canvas clear — pools reset on next render.
- **Flash animation**: `FlashInvalidKey` stops any in-flight animation (`BeginAnimation(null)`) before starting a new one, preventing handler accumulation on rapid key spam.
