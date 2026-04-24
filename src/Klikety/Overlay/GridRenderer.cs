using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

using Klikety.Config;
using Klikety.Grid;
using Klikety.Services;

namespace Klikety.Overlay;

/// <summary>
/// Renders grid cells, labels, highlights, and dimming on the overlay Canvas.
/// Clears children on each transition to prevent memory accumulation.
/// </summary>
public sealed class GridRenderer : IGridRenderer {
    private readonly Canvas _canvas;
    private readonly ThemeModel _theme;
    private readonly LabelGenerator _labelGenerator;
    private readonly double _minLabelFontSize;

    // DIP transform — auto-initialized from canvas PresentationSource on first render
    private Matrix _transformFromDevice = Matrix.Identity;
    private bool _transformInitialized;

    public GridRenderer(Canvas canvas, ThemeModel theme, LabelGenerator labelGenerator, double minLabelFontSize = 10.0) {
        _canvas = canvas;
        _theme = theme;
        _labelGenerator = labelGenerator;
        _minLabelFontSize = minLabelFontSize;
    }

    /// <summary>
    /// Sets the device→DIP transform matrix. Call once when overlay is shown.
    /// If never called, auto-initialized from the canvas PresentationSource.
    /// </summary>
    public void SetTransform(Matrix transformFromDevice) {
        _transformFromDevice = transformFromDevice;
        _transformInitialized = true;
    }

    /// <summary>
    /// Auto-initializes transform from the canvas's PresentationSource if not set.
    /// </summary>
    private void EnsureTransform() {
        if (_transformInitialized) {
            return;
        }

        var source = PresentationSource.FromVisual(_canvas);
        if (source?.CompositionTarget != null) {
            _transformFromDevice = source.CompositionTarget.TransformFromDevice;
            _transformInitialized = true;
        }
    }

    /// <summary>
    /// Computes the DIP region covered by a set of cells, accounting for DPI scaling.
    /// Transforms only the corners of the full cell range to DIP, then uses that
    /// as the basis for even subdivision — no per-cell int rounding.
    /// </summary>
    private Rect ComputeRegionFromCells(IReadOnlyList<GridCell> cells) {
        var first = cells[0].Bounds;
        var last = cells[^1].Bounds;
        var topLeft = _transformFromDevice.Transform(new Point(first.X, first.Y));
        var bottomRight = _transformFromDevice.Transform(new Point(
            last.X + last.Width,
            last.Y + last.Height));
        return new Rect(topLeft, bottomRight);
    }

    /// <summary>
    /// Computes the DIP rect for a specific cell by evenly subdividing a region.
    /// No int rounding — cells tile perfectly.
    /// </summary>
    private static Rect DipRectForCell(int row, int col, Rect region, int totalCols, int totalRows) {
        double cellWidth = region.Width / totalCols;
        double cellHeight = region.Height / totalRows;
        return new Rect(
            region.X + col * cellWidth,
            region.Y + row * cellHeight,
            cellWidth,
            cellHeight);
    }

    /// <summary>
    /// Computes auto-scaled font size so characters fill a fraction of cell height.
    /// <paramref name="heightFraction"/> is 0.8 for L1, 0.9 for L2/L3 subgrids.
    /// Clamped to [_minLabelFontSize .. theme.LabelFontSize * 3].
    /// </summary>
    private double ComputeAutoFontSize(double cellHalfWidth, double cellHeight, double heightFraction = 0.8) {
        // Target: fill heightFraction of cell height
        // WPF text line height ≈ 1.2 × fontSize
        double fontFromHeight = cellHeight * heightFraction / 1.2;

        // Also cap by half-width so character doesn't overflow horizontally
        // Approximate character aspect ratio for Segoe UI: width ≈ 0.55 × fontSize
        double fontFromWidth = cellHalfWidth * 0.95 / 0.55;

        double fontSize = Math.Min(fontFromHeight, fontFromWidth);
        return Math.Clamp(fontSize, _minLabelFontSize, _theme.LabelFontSize * 3);
    }

  /// <summary>
  /// Adds two outlined text labels for a cell: First centered in the left half,
  /// Second centered in the right half. Uses FormattedText geometry for crisp
  /// stroke outlines that ensure readability over any background.
  /// </summary>
  private void AddLabel(Rect dipRect, int row, int col, Brush foreground, double opacity = 1.0, double heightFraction = 0.8) {
    var cellLabel = _labelGenerator.LabelFor(row, col);
        double halfWidth = dipRect.Width / 2;
        var fontFamily = new FontFamily(_theme.LabelFontFamily);
        var fontWeight = ParseFontWeight(_theme.LabelFontWeight);
        double fontSize = ComputeAutoFontSize(halfWidth, dipRect.Height, heightFraction);
    var outlineBrush = BrushFromHex(_theme.LabelOutlineColor);
    double outlineThickness = _theme.LabelOutlineThickness;

    var typeface = new Typeface(fontFamily, FontStyles.Normal, fontWeight, FontStretches.Normal);

    AddOutlinedText(cellLabel.First, typeface, fontSize, foreground, outlineBrush, outlineThickness, opacity,
        dipRect.X, dipRect.Y, halfWidth, dipRect.Height);
    AddOutlinedText(cellLabel.Second, typeface, fontSize, foreground, outlineBrush, outlineThickness, opacity,
        dipRect.X + halfWidth, dipRect.Y, halfWidth, dipRect.Height);
  }

  /// <summary>
  /// Renders a single outlined text element centered within the given bounds.
  /// Two-layer rendering: stroke-only background + fill-only foreground.
  /// This prevents the stroke from eating into the letter fill.
  /// </summary>
  private void AddOutlinedText(string text, Typeface typeface, double fontSize,
      Brush fill, Brush outlineBrush, double outlineThickness, double opacity,
      double areaX, double areaY, double areaWidth, double areaHeight) {
    var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight, typeface, fontSize, fill, VisualTreeHelper.GetDpi(_canvas).PixelsPerDip);
    var geometry = ft.BuildGeometry(new Point(0, 0));
    var bounds = geometry.Bounds;

    double offsetX = areaX + (areaWidth - bounds.Width) / 2 - bounds.X;
    double offsetY = areaY + (areaHeight - bounds.Height) / 2 - bounds.Y;

    // Layer 1: stroke-only outline (renders behind)
    var outline = new Path {
      Data = geometry,
      Fill = Brushes.Transparent,
      Stroke = outlineBrush,
      StrokeThickness = outlineThickness * 2, // doubled since only outer half is visible
      StrokeLineJoin = PenLineJoin.Round,
      Opacity = opacity,
    };
    Canvas.SetLeft(outline, offsetX);
    Canvas.SetTop(outline, offsetY);
    _canvas.Children.Add(outline);

    // Layer 2: fill-only text (renders on top, covers inner stroke)
    var fillPath = new Path {
      Data = geometry,
      Fill = fill,
      Opacity = opacity,
    };
    Canvas.SetLeft(fillPath, offsetX);
    Canvas.SetTop(fillPath, offsetY);
    _canvas.Children.Add(fillPath);
  }

  /// <summary>
  /// Clears all canvas children. Called during deactivation to prevent stale frame flash.
  /// </summary>
  public void ClearCanvas() => _canvas.Children.Clear();

    private void AddCellRect(Rect dipRect, Brush fill, Brush stroke, double strokeThickness = -1) {
        if (strokeThickness < 0) {
            strokeThickness = _theme.CellBorderThickness;
        }

        var bg = new Rectangle {
            Width = dipRect.Width,
            Height = dipRect.Height,
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = strokeThickness,
        };
        Canvas.SetLeft(bg, dipRect.X);
        Canvas.SetTop(bg, dipRect.Y);
        _canvas.Children.Add(bg);
    }

    /// <summary>
    /// Renders the full grid (all cells with borders and labels).
    /// </summary>
    public void RenderGrid(IReadOnlyList<GridCell> cells) {
        _canvas.Children.Clear();
        EnsureTransform();

        var region = ComputeRegionFromCells(cells);
        int cols = _labelGenerator.Cols;
        int rows = _labelGenerator.Rows;

        var borderBrush = BrushFromHex(_theme.CellBorderColor);
        var bgBrush = BrushFromHex(_theme.CellBackgroundColor, _theme.CellBackgroundOpacity);
        var labelBrush = BrushFromHex(_theme.LabelColor);

        foreach (var cell in cells) {
            var dipRect = DipRectForCell(cell.Row, cell.Col, region, cols, rows);
            AddCellRect(dipRect, bgBrush, borderBrush);
            AddLabel(dipRect, cell.Row, cell.Col, labelBrush);
        }
    }

    /// <summary>
    /// Highlights a column and dims non-matching cells.
    /// </summary>
    public void HighlightColumn(IReadOnlyList<GridCell> cells, int col) {
        _canvas.Children.Clear();
        EnsureTransform();

        var region = ComputeRegionFromCells(cells);
        int cols = _labelGenerator.Cols;
        int rows = _labelGenerator.Rows;

        var borderBrush = BrushFromHex(_theme.CellBorderColor);
        var normalBg = BrushFromHex(_theme.CellBackgroundColor, _theme.CellBackgroundOpacity);
        var dimBrush = BrushFromHex(_theme.DimmedOverlayColor, _theme.DimmedOverlayOpacity);
        var highlightBg = BrushFromHex(_theme.HighlightedColumnBackground, 0.5);
        var highlightBorder = BrushFromHex(_theme.HighlightedColumnBorderColor);
        var labelBrush = BrushFromHex(_theme.LabelColor);

        foreach (var cell in cells) {
            var dipRect = DipRectForCell(cell.Row, cell.Col, region, cols, rows);
            bool isHighlighted = cell.Col == col;
            AddCellRect(dipRect,
                isHighlighted ? highlightBg : dimBrush,
                isHighlighted ? highlightBorder : borderBrush);
            AddLabel(dipRect, cell.Row, cell.Col, labelBrush, isHighlighted ? 1.0 : 0.3);
        }
    }

    /// <summary>
    /// Highlights a single cell (arrow navigation) without dimming others.
    /// </summary>
    public void HighlightCell(IReadOnlyList<GridCell> cells, GridCell highlightedCell) {
        _canvas.Children.Clear();
        EnsureTransform();

        var region = ComputeRegionFromCells(cells);
        int cols = _labelGenerator.Cols;
        int rows = _labelGenerator.Rows;

        var borderBrush = BrushFromHex(_theme.CellBorderColor);
        var bgBrush = BrushFromHex(_theme.CellBackgroundColor, _theme.CellBackgroundOpacity);
        var highlightBg = BrushFromHex(_theme.HighlightedColumnBackground, 0.5);
        var highlightBorder = BrushFromHex(_theme.HighlightedColumnBorderColor);
        var labelBrush = BrushFromHex(_theme.LabelColor);

        foreach (var cell in cells) {
            var dipRect = DipRectForCell(cell.Row, cell.Col, region, cols, rows);
      bool isCell = cell.Row == highlightedCell.Row && cell.Col == highlightedCell.Col;
      bool inCrossHair = cell.Row == highlightedCell.Row || cell.Col == highlightedCell.Col;

      Brush bg = isCell ? highlightBg
          : inCrossHair ? BrushFromHex(_theme.HighlightedColumnBackground, 0.25)
          : bgBrush;
      Brush border = inCrossHair ? highlightBorder : borderBrush;
      double thickness = isCell ? _theme.CellBorderThickness * 2
          : inCrossHair ? _theme.CellBorderThickness * 1.5
          : _theme.CellBorderThickness;

      AddCellRect(dipRect, bg, border, thickness);
      AddLabel(dipRect, cell.Row, cell.Col, labelBrush);
        }
    }

    /// <summary>
    /// Renders a subgrid within a parent cell's bounds using distinct subgrid styling.
    /// If cells are too small for labels at MinLabelFontSize, renders labels externally.
    /// </summary>
    public void RenderSubgrid(IReadOnlyList<GridCell> cells) {
        _canvas.Children.Clear();

        if (cells.Count == 0) {
            return;
        }

        EnsureTransform();

        var region = ComputeRegionFromCells(cells);
        int cols = _labelGenerator.Cols;
        int rows = _labelGenerator.Rows;

        var firstDip = DipRectForCell(0, 0, region, cols, rows);
        bool useExternalLabels = ShouldUseExternalLabels(firstDip.Height, firstDip.Width, _minLabelFontSize);

        double borderOpacity = useExternalLabels ? 0.3 : 1.0;
        double borderThickness = useExternalLabels ? Math.Max(0.5, _theme.CellBorderThickness * 0.5) : _theme.CellBorderThickness;
        var borderBrush = BrushFromHex(_theme.SubgridBorderColor, borderOpacity);
        var bgBrush = BrushFromHex(_theme.CellBackgroundColor, _theme.CellBackgroundOpacity);

        // Draw cell backgrounds
        foreach (var cell in cells) {
            var dipRect = DipRectForCell(cell.Row, cell.Col, region, cols, rows);
            AddCellRect(dipRect, bgBrush, borderBrush, borderThickness);
        }

        if (useExternalLabels) {
            RenderExternalLabels(cells, region);
        } else {
            // Subgrids use 90% height fill for tighter packing before going external
            var labelBrush = BrushFromHex(_theme.SubgridLabelColor);
            foreach (var cell in cells) {
                AddLabel(DipRectForCell(cell.Row, cell.Col, region, cols, rows), cell.Row, cell.Col, labelBrush, heightFraction: 0.9);
            }
        }
    }

    /// <summary>
    /// Renders a subgrid over a faint background grid (parent level grid lines without labels).
    /// </summary>
    public void RenderSubgridOverGrid(IReadOnlyList<GridCell> backgroundCells, IReadOnlyList<GridCell> subgridCells) {
        _canvas.Children.Clear();
        if (subgridCells.Count == 0) {
            return;
        }

        EnsureTransform();

        RenderBackgroundGrid(backgroundCells);
        RenderSubgridContent(subgridCells);
    }

    /// <summary>
    /// Highlights a column within a subgrid, rendered over a faint background grid.
    /// </summary>
    public void HighlightColumnOverGrid(IReadOnlyList<GridCell> backgroundCells, IReadOnlyList<GridCell> subgridCells, int col) {
        _canvas.Children.Clear();
        if (subgridCells.Count == 0) {
            return;
        }

        EnsureTransform();

        RenderBackgroundGrid(backgroundCells);

        var region = ComputeRegionFromCells(subgridCells);
        int cols = _labelGenerator.Cols;
        int rows = _labelGenerator.Rows;

        var firstDip = DipRectForCell(0, 0, region, cols, rows);
        bool useExternalLabels = ShouldUseExternalLabels(firstDip.Height, firstDip.Width, _minLabelFontSize);

        double borderOpacity = useExternalLabels ? 0.3 : 1.0;
        double borderThickness = useExternalLabels ? Math.Max(0.5, _theme.CellBorderThickness * 0.5) : _theme.CellBorderThickness;
        var borderBrush = BrushFromHex(_theme.SubgridBorderColor, borderOpacity);
        var dimBrush = BrushFromHex(_theme.DimmedOverlayColor, _theme.DimmedOverlayOpacity);
        var highlightBg = BrushFromHex(_theme.HighlightedColumnBackground, useExternalLabels ? 0.3 : 0.5);
        var highlightBorder = BrushFromHex(_theme.HighlightedColumnBorderColor);
        var labelBrush = BrushFromHex(_theme.SubgridLabelColor);

        // When cells are too small for inline labels (L3), add alternating row bands
        // for cross-hair legibility before drawing cells on top.
        if (useExternalLabels) {
            var rowBandBrush = BrushFromHex(_theme.SubgridBorderColor, 0.12);
            double rowHeight = region.Height / rows;
            for (int r = 0; r < rows; r += 2) {
                var rowRect = new Rect(region.X, region.Y + r * rowHeight, region.Width, rowHeight);
                AddCellRect(rowRect, rowBandBrush, Brushes.Transparent, 0);
            }
        }

        foreach (var cell in subgridCells) {
            var dipRect = DipRectForCell(cell.Row, cell.Col, region, cols, rows);
            bool isHighlighted = cell.Col == col;
            AddCellRect(dipRect,
                isHighlighted ? highlightBg : dimBrush,
                isHighlighted ? highlightBorder : borderBrush,
                isHighlighted ? borderThickness * 2 : borderThickness);
            if (!useExternalLabels) {
                AddLabel(dipRect, cell.Row, cell.Col, labelBrush, isHighlighted ? 1.0 : 0.3, 0.9);
            }
        }

        if (useExternalLabels) {
      RenderExternalColumnLabels(subgridCells, region);
      RenderExternalRowLabels(subgridCells, region);
    }
    }

    /// <summary>
    /// Highlights a single cell (arrow navigation) within a subgrid, rendered over a faint background grid.
    /// Supports external labels when cells are too small for inline text.
    /// </summary>
    public void HighlightCellOverGrid(IReadOnlyList<GridCell> backgroundCells, IReadOnlyList<GridCell> subgridCells, GridCell highlightedCell) {
        _canvas.Children.Clear();
        if (subgridCells.Count == 0) {
            return;
        }

        EnsureTransform();

        RenderBackgroundGrid(backgroundCells);

        var region = ComputeRegionFromCells(subgridCells);
        int cols = _labelGenerator.Cols;
        int rows = _labelGenerator.Rows;

        var firstDip = DipRectForCell(0, 0, region, cols, rows);
        bool useExternalLabels = ShouldUseExternalLabels(firstDip.Height, firstDip.Width, _minLabelFontSize);

        double borderOpacity = useExternalLabels ? 0.3 : 1.0;
        double borderThickness = useExternalLabels ? Math.Max(0.5, _theme.CellBorderThickness * 0.5) : _theme.CellBorderThickness;
        var borderBrush = BrushFromHex(_theme.SubgridBorderColor, borderOpacity);
        var bgBrush = BrushFromHex(_theme.CellBackgroundColor, _theme.CellBackgroundOpacity);
        var highlightBg = BrushFromHex(_theme.HighlightedColumnBackground, 0.5);
        var highlightBorder = BrushFromHex(_theme.HighlightedColumnBorderColor);
        var labelBrush = BrushFromHex(_theme.SubgridLabelColor);

        if (useExternalLabels) {
            var rowBandBrush = BrushFromHex(_theme.SubgridBorderColor, 0.12);
            double rowHeight = region.Height / rows;
            for (int r = 0; r < rows; r += 2) {
                var rowRect = new Rect(region.X, region.Y + r * rowHeight, region.Width, rowHeight);
                AddCellRect(rowRect, rowBandBrush, Brushes.Transparent, 0);
            }
        }

        foreach (var cell in subgridCells) {
            var dipRect = DipRectForCell(cell.Row, cell.Col, region, cols, rows);
      bool isCell = cell.Row == highlightedCell.Row && cell.Col == highlightedCell.Col;
      bool inRow = cell.Row == highlightedCell.Row;
      bool inCol = cell.Col == highlightedCell.Col;
      bool inCrossHair = inRow || inCol;

      Brush bg = isCell ? highlightBg
          : inCrossHair ? BrushFromHex(_theme.HighlightedColumnBackground, 0.25)
          : bgBrush;
      Brush border = inCrossHair ? highlightBorder : borderBrush;
      double thickness = isCell ? borderThickness * 2
          : inCrossHair ? borderThickness * 1.5
          : borderThickness;

      AddCellRect(dipRect, bg, border, thickness);
      if (!useExternalLabels) {
                AddLabel(dipRect, cell.Row, cell.Col, labelBrush, heightFraction: 0.9);
            }
        }

        if (useExternalLabels) {
            RenderExternalColumnLabels(subgridCells, region);
            RenderExternalRowLabels(subgridCells, region);
        }
    }

    /// <summary>
    /// Renders background grid cells as faint borders only (no labels, no fill).
    /// </summary>
    private void RenderBackgroundGrid(IReadOnlyList<GridCell> cells) {
        if (cells.Count == 0) {
            return;
        }

        var region = ComputeRegionFromCells(cells);
        // Use L1 grid dimensions — background cells are always from a known half
        int cols = cells.Max(c => c.Col) + 1;
        int rows = cells.Max(c => c.Row) + 1;

        var borderBrush = BrushFromHex(_theme.CellBorderColor, 0.15);

        foreach (var cell in cells) {
            var dipRect = DipRectForCell(cell.Row, cell.Col, region, cols, rows);
            AddCellRect(dipRect, Brushes.Transparent, borderBrush);
        }
    }

    /// <summary>
    /// Renders subgrid content (cells + labels) without clearing canvas.
    /// </summary>
    private void RenderSubgridContent(IReadOnlyList<GridCell> cells) {
        var region = ComputeRegionFromCells(cells);
        int cols = _labelGenerator.Cols;
        int rows = _labelGenerator.Rows;

        var firstDip = DipRectForCell(0, 0, region, cols, rows);
        bool useExternalLabels = ShouldUseExternalLabels(firstDip.Height, firstDip.Width, _minLabelFontSize);

        double borderOpacity = useExternalLabels ? 0.3 : 1.0;
        double borderThickness = useExternalLabels ? Math.Max(0.5, _theme.CellBorderThickness * 0.5) : _theme.CellBorderThickness;
        var borderBrush = BrushFromHex(_theme.SubgridBorderColor, borderOpacity);
        var bgBrush = BrushFromHex(_theme.CellBackgroundColor, _theme.CellBackgroundOpacity);

        foreach (var cell in cells) {
            var dipRect = DipRectForCell(cell.Row, cell.Col, region, cols, rows);
            AddCellRect(dipRect, bgBrush, borderBrush, borderThickness);
        }

        if (useExternalLabels) {
            RenderExternalLabels(cells, region);
        } else {
            var labelBrush = BrushFromHex(_theme.SubgridLabelColor);
            foreach (var cell in cells) {
                AddLabel(DipRectForCell(cell.Row, cell.Col, region, cols, rows), cell.Row, cell.Col, labelBrush, heightFraction: 0.9);
            }
        }
    }

    /// <summary>
    /// Renders first-key labels (top/bottom) and second-key labels (left/right) outside the subgrid.
    /// Column labels shown when awaiting first key; row labels added after column selected.
    /// </summary>
    private void RenderExternalLabels(IReadOnlyList<GridCell> cells, Rect region, bool includeRowLabels = false) {
        RenderExternalColumnLabels(cells, region);
        if (includeRowLabels) {
            RenderExternalRowLabels(cells, region);
        }
    }

    /// <summary>
    /// Renders column first-key labels above and below the grid.
    /// </summary>
    private void RenderExternalColumnLabels(IReadOnlyList<GridCell> cells, Rect region) {
        var extLabelBrush = BrushFromHex(_theme.ExternalLabelColor);
    var outlineBrush = BrushFromHex(_theme.LabelOutlineColor);
    double outlineThickness = _theme.LabelOutlineThickness;
    var connectorBrush = BrushFromHex(_theme.ConnectorLineColor);
        var fontFamily = new FontFamily(_theme.LabelFontFamily);
        var fontWeight = ParseFontWeight(_theme.LabelFontWeight);
    var typeface = new Typeface(fontFamily, FontStyles.Normal, fontWeight, FontStretches.Normal);

    int cols = _labelGenerator.Cols;
        int rows = _labelGenerator.Rows;

        var firstDip = DipRectForCell(0, 0, region, cols, rows);
        double fontSize = ComputeExternalFontSize(firstDip, region, cols, rows);
        double standardMargin = fontSize * 1.5;

        // Measure max label width to detect overlap
        double maxLabelWidth = 0;
        for (int c = 0; c < cols; c++) {
            var label = _labelGenerator.LabelFor(0, c);
      var size = MeasureText(label.First, typeface, fontSize);
      maxLabelWidth = Math.Max(maxLabelWidth, size.Width);
    }

        var (fanOutDist, labelExtent) = ComputeFanOut(cols, maxLabelWidth, region.Width, standardMargin);
        double labelExtentStart = region.X + region.Width / 2 - labelExtent / 2;
        double screenWidth = Math.Max(_canvas.ActualWidth, 1);
        labelExtentStart = Math.Clamp(labelExtentStart, 0, Math.Max(0, screenWidth - labelExtent));
        double labelSpacing = labelExtent / cols;

        double gridTop = region.Y;
        double gridBottom = region.Y + region.Height;

        // Screen-edge-aware direction
        double screenHeight = Math.Max(_canvas.ActualHeight, 1);
        bool showAbove = gridTop >= fanOutDist;
        bool showBelow = (screenHeight - gridBottom) >= fanOutDist;
        if (!showAbove && !showBelow) {
            showAbove = true;
            showBelow = true;
        }

        for (int c = 0; c < cols; c++) {
            var cellDip = DipRectForCell(0, c, region, cols, rows);
            var cellLabel = _labelGenerator.LabelFor(0, c);
            double anchorX = cellDip.X + cellDip.Width / 2;
            double labelCenterX = labelExtentStart + (c + 0.5) * labelSpacing;
      var labelSize = MeasureText(cellLabel.First, typeface, fontSize);

      if (showAbove) {
        double labelY = gridTop - fanOutDist;
        AddOutlinedText(cellLabel.First, typeface, fontSize, extLabelBrush, outlineBrush, outlineThickness, 1.0,
            labelCenterX - labelSize.Width / 2, labelY, labelSize.Width, labelSize.Height);
        _canvas.Children.Add(CreateConnector(anchorX, gridTop, labelCenterX, labelY + labelSize.Height + 2, connectorBrush));
      }

            if (showBelow) {
        double labelY = gridBottom + fanOutDist - labelSize.Height;
        AddOutlinedText(cellLabel.First, typeface, fontSize, extLabelBrush, outlineBrush, outlineThickness, 1.0,
            labelCenterX - labelSize.Width / 2, labelY, labelSize.Width, labelSize.Height);
        _canvas.Children.Add(CreateConnector(anchorX, gridBottom, labelCenterX, labelY - 2, connectorBrush));
            }
        }
    }

    /// <summary>
    /// Renders row second-key labels to the left and right of the grid.
    /// Fan-out applied when labels would overlap vertically.
    /// </summary>
    private void RenderExternalRowLabels(IReadOnlyList<GridCell> cells, Rect region) {
        var extLabelBrush = BrushFromHex(_theme.ExternalLabelColor);
    var outlineBrush = BrushFromHex(_theme.LabelOutlineColor);
    double outlineThickness = _theme.LabelOutlineThickness;
    var connectorBrush = BrushFromHex(_theme.ConnectorLineColor);
        var fontFamily = new FontFamily(_theme.LabelFontFamily);
        var fontWeight = ParseFontWeight(_theme.LabelFontWeight);
    var typeface = new Typeface(fontFamily, FontStyles.Normal, fontWeight, FontStretches.Normal);

    int cols = _labelGenerator.Cols;
        int rows = _labelGenerator.Rows;

        var firstDip = DipRectForCell(0, 0, region, cols, rows);
        double fontSize = ComputeExternalFontSize(firstDip, region, cols, rows);
        double standardMargin = fontSize * 1.5;

        // Measure max label height to detect overlap
        double maxLabelHeight = 0;
        for (int r = 0; r < rows; r++) {
            var label = _labelGenerator.LabelFor(r, 0);
      var size = MeasureText(label.Second, typeface, fontSize);
      maxLabelHeight = Math.Max(maxLabelHeight, size.Height);
    }

        var (fanOutDist, labelExtent) = ComputeFanOut(rows, maxLabelHeight, region.Height, standardMargin);
        double labelExtentStart = region.Y + region.Height / 2 - labelExtent / 2;
        double screenHeight = Math.Max(_canvas.ActualHeight, 1);
        labelExtentStart = Math.Clamp(labelExtentStart, 0, Math.Max(0, screenHeight - labelExtent));
        double labelSpacing = labelExtent / rows;

        double gridLeft = region.X;
        double gridRight = region.X + region.Width;

        // Screen-edge-aware direction
        double screenWidth = Math.Max(_canvas.ActualWidth, 1);
        bool showLeft = gridLeft >= fanOutDist;
        bool showRight = (screenWidth - gridRight) >= fanOutDist;
        if (!showLeft && !showRight) {
            showLeft = true;
            showRight = true;
        }

        for (int r = 0; r < rows; r++) {
            int cellIndex = r * cols;
            if (cellIndex >= cells.Count) {
                break;
            }

            var cellDip = DipRectForCell(r, 0, region, cols, rows);
            var cellLabel = _labelGenerator.LabelFor(r, 0);
            double anchorY = cellDip.Y + cellDip.Height / 2;
            double labelCenterY = labelExtentStart + (r + 0.5) * labelSpacing;
      var labelSize = MeasureText(cellLabel.Second, typeface, fontSize);

      if (showLeft) {
        double labelX = gridLeft - fanOutDist;
        AddOutlinedText(cellLabel.Second, typeface, fontSize, extLabelBrush, outlineBrush, outlineThickness, 1.0,
            labelX, labelCenterY - labelSize.Height / 2, labelSize.Width, labelSize.Height);
        _canvas.Children.Add(CreateConnector(gridLeft, anchorY, labelX + labelSize.Width + 2, labelCenterY, connectorBrush));
      }

            if (showRight) {
        double labelX = gridRight + fanOutDist - labelSize.Width;
        AddOutlinedText(cellLabel.Second, typeface, fontSize, extLabelBrush, outlineBrush, outlineThickness, 1.0,
            labelX, labelCenterY - labelSize.Height / 2, labelSize.Width, labelSize.Height);
        _canvas.Children.Add(CreateConnector(gridRight, anchorY, labelX - 2, labelCenterY, connectorBrush));
            }
        }
    }

    /// <summary>
    /// Computes fan-out parameters when external labels would overlap at the grid edge.
    /// Returns (distance from edge, total extent to spread labels across).
    /// When labels fit at standard spacing, returns (standardMargin, gridExtent).
    /// </summary>
    internal static (double distance, double extent) ComputeFanOut(
        int labelCount, double maxLabelSize, double gridExtent, double standardMargin, double minGap = 2.0) {
        double spacingAtEdge = gridExtent / labelCount;
        if (spacingAtEdge >= maxLabelSize + minGap) {
            return (standardMargin, gridExtent);
        }

        double requiredExtent = labelCount * (maxLabelSize + minGap);
        double spread = (requiredExtent - gridExtent) / 2;
        double distance = standardMargin + spread;
        return (distance, requiredExtent);
    }

    /// <summary>
    /// Computes font size for external labels. Uses cell-based auto-size
    /// floored at _minLabelFontSize (default 10) so labels stay readable at L3.
    /// </summary>
    private double ComputeExternalFontSize(Rect cellDip, Rect region, int cols, int rows) {
        double cellBased = ComputeAutoFontSize(cellDip.Width / 2, cellDip.Height, 0.9);
        return Math.Max(cellBased, _minLabelFontSize);
    }

    private static TextBlock CreateExternalLabel(string text, Brush foreground, FontFamily fontFamily, double fontSize, FontWeight fontWeight) =>
        new() {
            Text = text,
            Foreground = foreground,
            FontFamily = fontFamily,
            FontSize = fontSize,
            FontWeight = fontWeight,
            TextAlignment = TextAlignment.Center,
        };

  /// <summary>
  /// Measures text size using FormattedText (no visual element needed).
  /// </summary>
  private Size MeasureText(string text, Typeface typeface, double fontSize) {
    var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight, typeface, fontSize, Brushes.Black, VisualTreeHelper.GetDpi(_canvas).PixelsPerDip);
    return new Size(ft.Width, ft.Height);
  }

  private Line CreateConnector(double x1, double y1, double x2, double y2, Brush stroke) =>
        new() {
            X1 = x1, Y1 = y1,
            X2 = x2, Y2 = y2,
            Stroke = stroke,
            StrokeThickness = _theme.ConnectorLineThickness,
            StrokeDashArray = [2, 2],
        };

    private static SolidColorBrush BrushFromHex(string hex, double opacity = 1.0) {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        var brush = new SolidColorBrush(color) { Opacity = opacity };
        brush.Freeze();
        return brush;
    }

    private static FontWeight ParseFontWeight(string weight) {
        return weight.ToLowerInvariant() switch {
            "thin" => FontWeights.Thin,
            "extralight" => FontWeights.ExtraLight,
            "light" => FontWeights.Light,
            "medium" => FontWeights.Medium,
            "semibold" => FontWeights.SemiBold,
            "bold" => FontWeights.Bold,
            "extrabold" => FontWeights.ExtraBold,
            "black" => FontWeights.Black,
            _ => FontWeights.Normal,
        };
    }

    /// <summary>
    /// Brief red flash over the overlay to indicate an invalid key press.
    /// Non-blocking — uses a WPF animation that removes itself on completion.
    /// </summary>
    public void FlashInvalidKey() {
        var flash = new Rectangle {
            Width = _canvas.ActualWidth,
            Height = _canvas.ActualHeight,
            Fill = new SolidColorBrush(Color.FromArgb(80, 255, 0, 0)),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(flash, 0);
        Canvas.SetTop(flash, 0);
        _canvas.Children.Add(flash);

        var animation = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(200)) {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        animation.Completed += (_, _) => _canvas.Children.Remove(flash);
        flash.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    /// <summary>
    /// Determines whether external labels should be used based on cell dimensions
    /// and minimum label font size. Checks both height (line height ~1.8×) and
    /// half-width (wide chars like W need ~1.6× fontSize).
    /// </summary>
    internal static bool ShouldUseExternalLabels(double cellDipHeight, double cellDipWidth, double minLabelFontSize)
        => cellDipHeight < (minLabelFontSize * 1.8) || (cellDipWidth / 2) < minLabelFontSize * 1.6;
}
