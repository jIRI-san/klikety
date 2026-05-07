# 012: External Labels for Small Cells in LogCrosshair Mode

## Decisions

- Small-cell threshold matches LogGridRenderer: `height < minLabelFontSize * 1.8` or `halfWidth < minLabelFontSize * 1.6`.
- No progressive reveal — both column and row external labels rendered in all render states.
- Adaptive label placement: labels placed at the outer edge of the bounding box of cells strictly below threshold + margin. The bounding box includes only cells that fail `IsSmallCell`; the first readable cell on each side is excluded. Labels cluster near center (small cells are near center in log scale).
- Reuse `LogGridRenderer.ResolveOverlaps` by widening visibility to `internal static` — simplest sharing, no new class, no duplication.
- Label text computed via `GetCrossLabel` (center-skip index formula). Center cell (`CenterRow × CenterCol`) always excluded from external label scanning — its bullet is always inline.
- Screen-edge side-availability checks run after overlap resolution (not before), since resolution may expand the label cluster beyond original bounds. Label positions and connector endpoints clamped to screen bounds after final placement.
- Opacity for external labels in each render state mirrors the inline opacity for the same cell: RenderCross → 1.0; HighlightColumn → highlighted col 1.0, all other cols 0.4, all rows 1.0; HighlightRow → highlighted row 1.0, all other rows 0.4, all cols 1.0; HighlightCell → target axis 1.0, target-cross axis 0.6, remaining 0.3. Connector line opacity matches its label.
- `ILogCrosshairRenderer` interface unchanged — external labels are an internal rendering detail of `LogCrosshairRenderer`.
- Center cell bullet (`•`) always inline, never externalized.
- Degenerate cells excluded from external labels.
- External label opacity follows the same highlight/dim pattern as inline labels in each render state.

## Requirements

| ID | Requirement | Acceptance Criteria | Phases/Steps |
|----|-------------|---------------------|--------------|
| REQ-1 | Small cross-arm cells render external labels instead of inline labels | When a cross-arm cell's DIP rect is below threshold, no inline label renders; an external label appears outside the small-cell zone | 2.1, 2.2, 2.3 |
| REQ-2 | Threshold uses `minLabelFontSize` from config, same formula as LogGridRenderer | `IsSmallCell` returns true iff `height < minLabelFontSize * 1.8` or `halfWidth < minLabelFontSize * 1.6` | 1.1, 1.2 |
| REQ-3 | Column labels placed above/below horizontal arm at the transition boundary | External column labels appear above and/or below the bounding box of small horiz-arm cells, with connectors pointing inward to cell centers | 2.1 |
| REQ-4 | Row labels placed left/right of vertical arm at the transition boundary | External row labels appear left and/or right of the bounding box of small vert-arm cells, with connectors pointing inward to cell centers | 2.2 |
| REQ-5 | Dashed connector lines from each external label to its cell center | Each external label has a dashed line connecting to the corresponding cell's center at the arm edge | 2.1, 2.2 |
| REQ-6 | Overlapping labels resolved via push-apart algorithm | When labels cluster (small cells near center), overlap resolution spreads them with minimum gap | 2.1, 2.2 |
| REQ-7 | Screen-edge-aware label direction | Labels skip the side with insufficient room; fallback renders at least one side | 2.1, 2.2 |
| REQ-8 | Element pooling for connector lines | Line elements reused across re-renders via pool pattern matching existing rect/text pools | 1.3 |
| REQ-9 | Theme colors for external labels | Uses `ExternalLabelColor` (columns), `ExternalRowLabelColor` (rows), `ConnectorLineColor`/`ConnectorLineThickness` from theme — no new theme properties | 1.3 |
| REQ-10 | External label opacity matches inline label patterns per render state | RenderCross: all 1.0. HighlightColumn: highlighted col 1.0, other cols 0.4, all rows 1.0. HighlightRow: highlighted row 1.0, other rows 0.4, all cols 1.0. HighlightCell: target axis 1.0, target-cross axis 0.6, others 0.3. Connector line opacity matches its label | 2.3 |
| REQ-11 | Large cells keep inline labels unchanged | Cells above the small-cell threshold render inline labels exactly as before; zero behavioral change for readable cells | 2.3 |
| REQ-12 | Both axes shown simultaneously | Column and row external labels render in all states, not gated by key entry order | 2.1, 2.2, 2.3 |

## Risks

| ID | Risk | Likelihood | Impact | Mitigation | Steps |
|----|------|------------|--------|------------|-------|
| RISK-1 | Labels near center may obscure cross-arm cell rectangles | Medium | Low | Offset from arm edge + margin + connector lines provide visual separation; discovery step allows tuning margins | 2.1, 2.2 |
| RISK-2 | Very small logBaseSize → many external labels → visual clutter | Low | Low | Overlap resolution spreads labels; ExternalFontSize floor keeps labels compact; test with default config | 2.1, 2.2 |
| RISK-3 | Cross-arm cell height expansion complicates bounding box calculation | Low | Medium | Use actual DIP rects of cells on center row/col (already account for expansion); test with varied logBaseSize values | 2.1, 2.2, 3.1 |

## Phase 1: Renderer Infrastructure
<!-- worktree: feature/012-logcrosshair-external-labels-renderer-infrastructure-step-1-1 -->

- [x] 1.1 Add `minLabelFontSize` to `LogCrosshairRenderer` constructor and wire in `App.xaml.cs` (REQ-2) `S`
  - Constructor signature: `(Canvas, ThemeModel, AxisLabelGenerator, AxisLabelGenerator, double minLabelFontSize)`
  - Store as `readonly double _minLabelFontSize`
  - `App.xaml.cs`: pass `config.MinLabelFontSize` as last arg to `new LogCrosshairRenderer(...)` (line ~112)

- [ ] 1.2 Add small-cell detection methods to `LogCrosshairRenderer` (REQ-2) `S`
  - `internal static bool IsSmallCell(Rect dipRect, double minLabelFontSize)` → `dipRect.Height < minLabelFontSize * 1.8 || dipRect.Width / 2 < minLabelFontSize * 1.6`
  - `internal static bool IsNarrowColumn(Rect dipRect, double minLabelFontSize)` → `dipRect.Width / 2 < minLabelFontSize * 1.6`
  - `internal static bool IsShortRow(Rect dipRect, double minLabelFontSize)` → `dipRect.Height < minLabelFontSize * 1.8`
  - `internal static` with parameter for testability (same pattern as `ComputeGradualFontSize`)

- [ ] 1.3 Add line pool, MeasureText, external label brushes, ExternalFontSize (REQ-8, REQ-9) `S`
  - `readonly List<Line> _linePool = []` + `int _nextLine`
  - `void UseLine(double x1, double y1, double x2, double y2, double opacity = 1.0)` — pooled dashed line using `_connectorBrush` + `_theme.ConnectorLineThickness`, `StrokeDashArray = [2, 2]`, sets `line.Opacity = opacity`
  - `void UseExternalLabel(Rect area, string text, double fontSize, Brush foreground, double opacity)` — outlined text at arbitrary position (not cell-centered). Same two-layer Path rendering as `UseCrossLabel` but positioned at `area.X/Y` directly. Uses existing text pool.
  - `Size MeasureText(string text, double fontSize)` — `FormattedText` measurement (same pattern as LogGridRenderer)
  - `double ExternalFontSize()` → `Math.Max(_minLabelFontSize, _theme.LabelFontSize * 0.85)`
  - Cached brushes in constructor: `_extLabelBrush` from `theme.ExternalLabelColor`, `_extRowLabelBrush` from `theme.ExternalRowLabelColor`, `_connectorBrush` from `theme.ConnectorLineColor`
  - Update `BeginRender`: reset `_nextLine`; add `_linePool.Clear()` to the existing staleness branch (alongside `_rectPool.Clear()` and `_textPool.Clear()`)
  - Update `EndRender`: collapse unused lines via `_linePool[i].Visibility = Visibility.Collapsed` for `i` in `[_nextLine .. _linePool.Count)` — same pattern as rect/text pools

- [ ] 1.4 Make `LogGridRenderer.ResolveOverlaps` `internal static` (REQ-6) `S`
  - Change `static void ResolveOverlaps(...)` → `internal static void ResolveOverlaps(...)` in `LogGridRenderer.cs`
  - Zero functional change; purely visibility widening

## Phase 2: External Label Rendering

- [ ] 2.1 Implement `RenderExternalColumnLabels` for narrow columns on horizontal arm (REQ-1, REQ-3, REQ-5, REQ-6, REQ-7, RISK-1, RISK-2, RISK-3) [after: 1.3, 1.4] [discovery] `M`
  - Method: `void RenderExternalColumnLabels(LogCrosshairGrid grid, int highlightCol = -1, double defaultOpacity = 1.0, double highlightOpacity = 1.0)` — `highlightCol` = column to highlight (-1 = none); other cols use `defaultOpacity`; highlighted col uses `highlightOpacity`
  - Scan center row for narrow columns: skip degenerate cells, skip zero-width cells, **skip CenterCol** (center bullet always inline)
  - Label text via `GetCrossLabel(grid.CenterRow, col, grid)` — reuses existing center-skip index formula
  - If none narrow → return early
  - Compute bounding box top/bottom (DIP) of cells strictly below threshold on the center row — these cluster near CenterCol
  - Column labels placed above (`y = bboxTop - margin - labelHeight`) and/or below (`y = bboxBottom + margin`)
  - X position: column center (midpoint of cell DIP rect), resolved via `LogGridRenderer.ResolveOverlaps`
  - Post-resolution: clamp label positions to `[0, screenWidth]`; clamp connector endpoints to screen bounds
  - Screen-edge check (after resolution): skip above if resolved labels extend above screen top; skip below if labels extend below screen bottom; fallback: at least one side
  - Dashed connector line from each label to column center at the arm edge (bboxTop for above labels, bboxBottom for below labels), with `opacity` matching its label
  - Use `_extLabelBrush` for label text; `ExternalFontSize()` for font size; render via `UseExternalLabel`

- [ ] 2.2 Implement `RenderExternalRowLabels` for short rows on vertical arm (REQ-1, REQ-4, REQ-5, REQ-6, REQ-7, RISK-1, RISK-2, RISK-3) [after: 1.3, 1.4] [discovery] `M`
  - Method: `void RenderExternalRowLabels(LogCrosshairGrid grid, int highlightRow = -1, double defaultOpacity = 1.0, double highlightOpacity = 1.0)` — `highlightRow` = row to highlight (-1 = none); other rows use `defaultOpacity`; highlighted row uses `highlightOpacity`
  - Scan center column for short rows: skip degenerate cells, skip zero-height cells, **skip CenterRow** (center bullet always inline)
  - Label text via `GetCrossLabel(row, grid.CenterCol, grid)` — reuses existing center-skip index formula
  - If none short → return early
  - Compute bounding box left/right (DIP) of cells strictly below threshold on the center column — these cluster near CenterRow
  - Row labels placed left (`x = bboxLeft - margin - labelWidth`) and/or right (`x = bboxRight + margin`)
  - Y position: row center (midpoint of cell DIP rect), resolved via `LogGridRenderer.ResolveOverlaps`
  - Post-resolution: clamp label positions to `[0, screenHeight]`; clamp connector endpoints to screen bounds
  - Screen-edge check (after resolution): skip left if resolved labels extend past screen left; skip right if labels extend past screen right; fallback: at least one side
  - Dashed connector line from each label to row center at the arm edge (bboxLeft for left labels, bboxRight for right labels), with `opacity` matching its label
  - Use `_extRowLabelBrush` for label text; `ExternalFontSize()` for font size; render via `UseExternalLabel`

- [ ] 2.3 Integrate external labels into all render methods (REQ-1, REQ-10, REQ-11, REQ-12) [after: 2.1, 2.2] `M`
  - **RenderCross**: in the cell loop, skip `UseCrossLabel` for small cross cells (but always render center bullet inline). After the cell loop, call `RenderExternalColumnLabels(grid)` + `RenderExternalRowLabels(grid)` before `EndRender()`.
  - **HighlightColumn(grid, col)**: skip inline for small cells. Call `RenderExternalColumnLabels(grid, highlightCol: col, defaultOpacity: 0.4, highlightOpacity: 1.0)` + `RenderExternalRowLabels(grid)` (all rows at default 1.0).
  - **HighlightRow(grid, row)**: skip inline for small cells. Call `RenderExternalRowLabels(grid, highlightRow: row, defaultOpacity: 0.4, highlightOpacity: 1.0)` + `RenderExternalColumnLabels(grid)` (all cols at default 1.0).
  - **HighlightCell(grid, cell)**: skip inline for small cells. Call `RenderExternalColumnLabels(grid, highlightCol: cell.Col, defaultOpacity: 0.3, highlightOpacity: 1.0)` + `RenderExternalRowLabels(grid, highlightRow: cell.Row, defaultOpacity: 0.3, highlightOpacity: 1.0)`. Cells on the target cross (same row or col as target) get 0.6 — achieved by passing target-cross opacity as `defaultOpacity` on the matching axis: column labels where col != target.Col but row == target.Row → these are row labels, handled by `highlightOpacity` on that axis.
  - Center cell (CenterRow × CenterCol) inline bullet always rendered regardless of cell size.

## Phase 3: Tests & Documentation

- [ ] 3.1 Unit tests for threshold detection and layout helpers (REQ-1, REQ-2, RISK-3) [after: 2.3] `M`
  - `IsSmallCell` / `IsNarrowColumn` / `IsShortRow` at boundary values (at threshold, just above, just below) — callable as `internal static` with `minLabelFontSize` parameter
  - `ExternalFontSize` uses `minLabelFontSize` floor
  - `ResolveOverlaps` callable from `LogGridRenderer` (existing tests cover correctness; verify `internal` visibility works from test project)
  - Existing `ComputeGradualFontSize` tests remain passing
  - Test opacity parameter resolution: verify `highlightCol`/`highlightRow` parameters produce correct per-label opacity values for each render state (RenderCross, HighlightColumn, HighlightRow, HighlightCell)
  - Test center-cell exclusion: verify CenterRow/CenterCol cells are never included in the external label scan

- [ ] 3.2 Update design notes (REQ-1) [after: 2.3] `S`
  - `grid-rendering.design.md`: add LogCrosshairRenderer external labels subsection covering threshold, adaptive placement, overlap resolution, connector lines, pooling
  - `navigation-modes.design.md`: note external label support in LogCrosshair mode section
