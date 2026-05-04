# 010: LogGrid Mode — Grid Calculator + Renderer (Phase 1)

> **Scope**: Foundation phase — grid calculator + renderer + visual integration. No runtime mode activation. Full mode integration (SM, session, config, factory, chord dispatch) is Plan 011.

## Decisions
- New 4th navigation mode named **LogGrid** — distinct from existing LogCrosshair
- 10×10 grid (no center row/col). Cursor position at activation → center intersection of 4 inner cells
- Uses `firstKeys` (10) × `secondKeys` (10) from UniformGrid config for cell addressing. **Mapping**: `firstKeys` → columns (horiz), `secondKeys` → rows (vert). Config validation in Plan 011 enforces `cols = rows = 10` for LogGrid mode (variable key counts not supported by this mode).
- Independent growth ratio per half-axis (left/right and up/down solve their own binary-search ratio against available distance)
- No L2 subgrid — single-level iterative refinement via recentering
- Recenters grid after every two-key cell selection (cursor moves to cell center, grid recomputed around new center)
- **External labels are axis-based** (column headers on top/bottom, row headers on left/right) — not per-cell. Small cells show no inline label; user reads column label + row label from edges to identify the cell. Threshold applied per-cell individually: cell below threshold → gets external labels. Uses existing `ShouldUseExternalLabels` constants (height < `minLabelFontSize × 1.8`, half-width < `minLabelFontSize × 1.6`). Axis headers shown whenever any cell in a column/row needs external labels. Two `AxisLabelGenerator` instances produce single-char labels; inline cells display combined "XY" (horiz char + vert char).
- Small cells (below threshold) receive a distinct background tint (new theme property)
- New dedicated renderer (`ILogGridRenderer` / `LogGridRenderer`) — separate from `LogCrosshairRenderer`. All label rendering uses `AddOutlinedText` (two-layer Path: stroke + fill) for readability.
- **Calculator class**: `LogScaleGridCalculator` (new class, avoids naming collision with existing `LogGridCalculator` which serves LogCrosshair).
- **`LogGrid` result type**: `Cells` (`GridCell[]`, length = cols×rows), `Cols` (int), `Rows` (int), `CenterPoint` (Point — cursor position at computation time), `ColEdges` (`double[]`, length = cols+1), `RowEdges` (`double[]`, length = rows+1). No degenerate-cell concept — all cells are valid and selectable. `CellAt(row, col)` accessor.
- **Geometric series per half-axis**: `cellsPerHalf = cols/2` (or `rows/2`). Innermost cell extent = `logBaseSize`. Series: `baseSize · (1 + r + r² + … + r^(cellsPerHalf-1)) = halfDistance`. **Single fallback rule**: when `halfDistance < cellsPerHalf × baseSize`, cells are uniform size `halfDistance / cellsPerHalf` (ratio irrelevant). This handles both "near-edge" and "very-near-edge" cases without contradiction.
- **Integer rounding for gap-free tiling**: cells constructed via shared-edge rounding: `cell[i].Right = cell[i+1].Left = Round(edge[i+1])`. Tests verify no gaps with odd-dimension bounds.
- **Action dispatch** (Phase 2 concern, documented here for design awareness): iterative recentering terminates when user presses an action key (Space/X/C/V) at any time — fires at current cursor position. Same pattern as existing LogCrosshair.
- **Config in Phase 1**: `logBaseSize` hardcoded to `10` (matching existing `ModeConfig.LogBaseSize` default). Config integration deferred to Plan 011. With `logBaseSize = 10` and half-screen ~960px, growth ratio ≈ 2.3, outermost cells ≈ 290px — acceptable spread.
- Overlaps between cells are acceptable; gaps are not
- No `TreatWarningsAsErrors` or `AnalysisLevel` in csproj — standard nullable + implicit usings. Zero build warnings expected.
- **DPI handling**: `ILogGridRenderer` includes `SetTransform(Matrix)` for physical→DIP coordinate translation. `LogGridRenderer` uses `EnsureTransform()` auto-detection pattern (consistent with all existing renderers).
- **Forward compatibility**: `ILogGridRenderer` includes `HighlightCell(LogGrid, GridCell)` (even if unused in Phase 1) to avoid interface-breaking change in Plan 011.
- **Existing theme file limitation**: existing installations with `dark.theme.json`/`light.theme.json` already on disk won't receive new properties; `ThemeModel` defaults apply. Acceptable — visual outcome is correct via defaults.

## Requirements

| ID | Requirement | Acceptance Criteria | Phases/Steps |
|----|-------------|---------------------|--------------|
| REQ-1 | Compute N×M log-scaled grid (default 10×10) from center point within screen bounds | Given center + bounds + logBaseSize + cols + rows, returns `cols × rows` `GridCell` instances covering full bounds with no gaps | 1.1, 1.2 |
| REQ-2 | Cell sizes grow geometrically from center outward, independently per half-axis | For each half-axis: `cell[i+1].extent >= cell[i].extent` (monotonic growth). Uniform fallback when `halfDistance < cellsPerHalf × baseSize`. | 1.1, 1.2 |
| REQ-3 | Off-center cursor: asymmetric halves have independent growth ratios | Short side smaller cells, long side larger cells; all cells tile the bounds | 1.1, 1.2 |
| REQ-4 | No gap between any two adjacent cells in the grid | Shared-edge rounding; union of all cell bounds covers entire screen bounds rectangle | 1.1, 1.2 |
| REQ-5 | Renderer draws all cells with borders | All cells have visible border rectangles on the overlay Canvas | 2.2 |
| REQ-6 | Large cells get inline two-character labels (outlined text) | Cells above threshold display "XY" label using `AddOutlinedText` | 2.2 |
| REQ-7 | Small cells get axis-based external labels at grid edges | Column headers at top/bottom, row headers at left/right, with fan-out connectors. External labels also use outlined text. | 2.2 |
| REQ-8 | Small cells have distinct background tint | Cells below threshold use `SmallCellBackgroundColor`/`SmallCellBackgroundOpacity` | 2.1, 2.2 |
| REQ-9 | Renderer exposes `HighlightColumn(grid, col)` | Calling `HighlightColumn` dims non-column cells and highlights column cells. Key-input trigger deferred to Plan 011. | 2.2 |
| REQ-10 | Visual integration for manual verification | Debug-only session activates overlay and renders log grid at cursor position | 2.4 |
| REQ-11 | New theme properties for small-cell tint | `SmallCellBackgroundColor`/`SmallCellBackgroundOpacity` on `ThemeModel`; `theme.schema.json` updated; `ThemeLoader` color validation updated | 2.1 |

## Risks

| ID | Risk | Likelihood | Impact | Mitigation | Steps |
|----|------|------------|--------|------------|-------|
| RISK-1 | Grid algorithm edge cases (cursor at screen corner/edge) produce sub-pixel cells | Medium | Medium | Uniform fallback when halfDistance insufficient. Tests for extreme positions. Sub-pixel cells are handled by external-label system. | 1.1, 1.2 |
| RISK-2 | Axis-based external labels visually confusing with 10 labels per edge | Low | Medium | Reuse proven fan-out algorithm. Visual test in step 2.4. | 2.2, 2.4 |
| RISK-3 | Performance: 100-cell re-render on recentering may lag | Low | Medium | Element pooling for rects, text paths, and connector `Line` elements. Grid computation O(n). | 2.2 |
| RISK-4 | Calculator API changes needed when Plan 011 discovers session-integration issues | Low | Low | `LogGrid` result type is simple. `HighlightCell` included for forward compatibility. | 1.1 |

## Phase 1: Grid Calculator
<!-- worktree: feature/010-log-grid-grid-calculator-step-1-1 -->

- [x] 1.1 New class `LogScaleGridCalculator` in `src/Klikety/Grid/`. Static method `Calculate(Point center, Rectangle bounds, int logBaseSize, int cols, int rows)` → `LogGrid`. Algorithm: split each axis at center into two halves (`cols/2` cells left, `cols/2` cells right; same for rows). Per half-axis binary search: find ratio `r` such that `baseSize · (1 + r + r² + … + r^(cellsPerHalf-1)) = halfDistance`. **Uniform fallback**: when `halfDistance < cellsPerHalf × baseSize`, each cell gets `halfDistance / cellsPerHalf` (no binary search). Last-cell absorption: outermost edges pinned to bounds. Shared-edge rounding (`Round(edge)` used consistently for adjacent cell boundaries). Result: `LogGrid` record with `Cells` (GridCell[cols×rows]), `Cols`, `Rows`, `CenterPoint`, `ColEdges` (double[cols+1]), `RowEdges` (double[rows+1]), `CellAt(row, col)`. (REQ-1, REQ-2, REQ-3, REQ-4, RISK-1) `M`

- [x] 1.2 Unit tests `LogScaleGridCalculatorTests` — property-based assertions: (a) correct cell count (cols × rows), (b) full bounds coverage with no gaps (shared-edge rounding → cell[i].Right == cell[i+1].Left), (c) monotonic growth outward from center per half-axis (or uniform when fallback applies), (d) all 4 screen corners as center, (e) mid-edge positions, (f) asymmetric screen (1920×1080, 1080×1920, 3840×2160), (g) cursor at exact bounds edge, (h) very small bounds (50×50) to test uniform fallback, (i) odd-dimension bounds (1919×1079). No exact-pixel assertions. (REQ-1, REQ-2, REQ-3, REQ-4, RISK-1) [after: 1.1] `M`

## Phase 2: Renderer
<!-- worktree: -->

- [ ] 2.1 Add `SmallCellBackgroundColor` (default `#1A4A7A`) and `SmallCellBackgroundOpacity` (default `0.5`) to `ThemeModel`. Update `theme.schema.json` with new properties. Update `ThemeLoader.ValidateColors` to include the new color property. Add to both `dark.theme.json` and `light.theme.json` embedded resources. (REQ-11, REQ-8) `S`

- [ ] 2.2 Implement `ILogGridRenderer` interface and `LogGridRenderer` class. Interface: `SetTransform(Matrix)`, `RenderGrid(LogGrid)`, `HighlightColumn(LogGrid, int col)`, `HighlightCell(LogGrid, GridCell)`, `FlashInvalidKey()`, `ClearCanvas()`. Renderer implementation: (a) `EnsureTransform()` for DIP conversion, (b) draw all cells with borders, (c) per-cell threshold check → inline two-char labels (using two `AxisLabelGenerator` instances, combined "XY") with `AddOutlinedText`, (d) axis-based external labels at edges with fan-out connectors for cells below threshold — also outlined text, (e) tinted background for below-threshold cells, (f) `HighlightColumn` dims non-column + highlights column, (g) `HighlightCell` highlights single cell (row+col crosshair pattern). Element pooling for rectangles, text `Path` pairs, and `Line` connector elements. (REQ-5, REQ-6, REQ-7, REQ-8, REQ-9, RISK-2, RISK-3) [after: 1.1, 2.1] `L`

- [ ] 2.3 Create `FakeLogGridRenderer` (implements `ILogGridRenderer`, records method calls) for use by Plan 011 session integration tests. (REQ-5, REQ-6, REQ-7) [after: 2.2] `S`

- [ ] 2.4 Visual integration: create `DebugLogGridSession : IModeSession` (guarded by `#if DEBUG`) that on `Activate()` computes the log grid at cursor position and calls `LogGridRenderer.RenderGrid`. Instantiated directly in `App.xaml.cs` under `#if DEBUG` with a dedicated debug hotkey — bypasses `ModeSessionFactory` and coordinator chord dispatch (those belong to Plan 011). (REQ-10, RISK-2) [after: 2.2] `S`
