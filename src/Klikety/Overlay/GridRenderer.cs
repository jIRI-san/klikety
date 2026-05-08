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
/// Brushes and typefaces are cached per theme load to avoid per-render allocation.
/// </summary>
public sealed class GridRenderer : IGridRenderer {
    private readonly Canvas _canvas;
    private readonly ThemeModel _theme;
    private readonly LabelGenerator _labelGenerator;
    private readonly double _minLabelFontSize;

    // DIP transform — auto-initialized from canvas PresentationSource on first render
    private Matrix _transformFromDevice = Matrix.Identity;
    private bool _transformInitialized;

    // Label offset for L2/L3 reduced-key grids
    private int _labelColOffset;
    private int _labelRowOffset;

    // Cached theme-derived resources (created once in constructor)
    private readonly SolidColorBrush _cellBorderBrush;
    private readonly SolidColorBrush _cellBgBrush;
    private readonly SolidColorBrush _labelBrush;
    private readonly SolidColorBrush _dimBrush;
    private readonly SolidColorBrush _highlightBg;
    private readonly SolidColorBrush _highlightBorder;
    private readonly SolidColorBrush _crosshairBg;
    private readonly SolidColorBrush _subgridBorderBrush;
    private readonly SolidColorBrush _subgridLabelBrush;
    private readonly SolidColorBrush _outlineBrush;
    private readonly SolidColorBrush _extColLabelBrush;
    private readonly SolidColorBrush _extRowLabelBrush;
    private readonly SolidColorBrush _connectorBrush;
    private readonly SolidColorBrush _bgGridBorderBrush;
    private readonly SolidColorBrush _rowBandBrush;
    private readonly SolidColorBrush _subgridBorderBrush03;
    private readonly SolidColorBrush _highlightBg03;
    private readonly FontFamily _fontFamily;
    private readonly FontWeight _fontWeight;
    private readonly Typeface _typeface;

    // Reusable flash rectangle (prevents accumulation on rapid invalid keys)
    private Rectangle? _flashRect;

    public GridRenderer(Canvas canvas, ThemeModel theme, LabelGenerator labelGenerator, double minLabelFontSize = 10.0) {
        _canvas = canvas;
        _theme = theme;
        _labelGenerator = labelGenerator;
        _minLabelFontSize = minLabelFontSize;

        // Cache all theme-derived brushes
        _cellBorderBrush = BrushFromHex(theme.CellBorderColor);
        _cellBgBrush = BrushFromHex(theme.CellBackgroundColor, theme.CellBackgroundOpacity);
        _labelBrush = BrushFromHex(theme.LabelColor);
        _dimBrush = BrushFromHex(theme.DimmedOverlayColor, theme.DimmedOverlayOpacity);
        _highlightBg = BrushFromHex(theme.HighlightedColumnBackground, 0.5);
        _highlightBorder = BrushFromHex(theme.HighlightedColumnBorderColor);
        _crosshairBg = BrushFromHex(theme.HighlightedColumnBackground, 0.25);
        _subgridBorderBrush = BrushFromHex(theme.SubgridBorderColor);
        _subgridLabelBrush = BrushFromHex(theme.SubgridLabelColor);
        _outlineBrush = BrushFromHex(theme.LabelOutlineColor);
        _extColLabelBrush = BrushFromHex(theme.ExternalColLabelColor);
        _extRowLabelBrush = BrushFromHex(theme.ExternalRowLabelColor);
        _connectorBrush = BrushFromHex(theme.ConnectorLineColor);
        _bgGridBorderBrush = BrushFromHex(theme.CellBorderColor, 0.15);
        _rowBandBrush = BrushFromHex(theme.SubgridBorderColor, 0.12);
        _subgridBorderBrush03 = BrushFromHex(theme.SubgridBorderColor, 0.3);
        _highlightBg03 = BrushFromHex(theme.HighlightedColumnBackground, 0.3);

        // Cache font resources
        _fontFamily = new FontFamily(theme.LabelFontFamily);
        _fontWeight = ParseFontWeight(theme.LabelFontWeight);
        _typeface = new Typeface(_fontFamily, FontStyles.Normal, _fontWeight, FontStretches.Normal);
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
    /// Sets the label offset for rendering L2/L3 reduced-key grids.
    /// Offset is applied to row/col when looking up labels from the LabelGenerator.
    /// </summary>
    public void SetLabelOffset(int colOffset, int rowOffset) {
        _labelColOffset = colOffset;
        _labelRowOffset = rowOffset;
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
    /// Derives actual grid dimensions (cols, rows) from the cell list.
    /// </summary>
    private static (int Cols, int Rows) DimensionsFromCells(IReadOnlyList<GridCell> cells) {
        int maxCol = 0, maxRow = 0;
        for (int i = 0; i < cells.Count; i++) {
            if (cells[i].Col > maxCol) {
                maxCol = cells[i].Col;
            }

            if (cells[i].Row > maxRow) {
                maxRow = cells[i].Row;
            }
        }
        return (maxCol + 1, maxRow + 1);
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
        double fontFromHeight = cellHeight * heightFraction / 1.2;
        double fontFromWidth = cellHalfWidth * 0.95 / 0.55;
        double fontSize = Math.Min(fontFromHeight, fontFromWidth);
        return Math.Clamp(fontSize, _minLabelFontSize, _theme.LabelFontSize * 3);
    }

    /// <summary>
    /// Adds two outlined text labels for a cell: First centered in the left half,
    /// Second centered in the right half.
    /// </summary>
    private void AddLabel(Rect dipRect, int row, int col, Brush foreground, double opacity = 1.0, double heightFraction = 0.8) {
        int labelRow = row + _labelRowOffset;
        int labelCol = col + _labelColOffset;
        if (labelRow < 0 || labelRow >= _labelGenerator.Rows || labelCol < 0 || labelCol >= _labelGenerator.Cols) {
            return;
        }

        var cellLabel = _labelGenerator.LabelFor(labelRow, labelCol);
        double halfWidth = dipRect.Width / 2;
        double fontSize = ComputeAutoFontSize(halfWidth, dipRect.Height, heightFraction);

        AddOutlinedText(cellLabel.First, _typeface, fontSize, foreground, _outlineBrush, _theme.LabelOutlineThickness, opacity,
            dipRect.X, dipRect.Y, halfWidth, dipRect.Height);
        AddOutlinedText(cellLabel.Second, _typeface, fontSize, foreground, _outlineBrush, _theme.LabelOutlineThickness, opacity,
            dipRect.X + halfWidth, dipRect.Y, halfWidth, dipRect.Height);
    }

    /// <summary>
    /// Renders a single outlined text element centered within the given bounds.
    /// Two-layer rendering: stroke-only background + fill-only foreground.
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
            StrokeThickness = outlineThickness * 2,
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
    /// When cells are too small for inline labels (e.g. at L3), uses external labels.
    /// </summary>
    public void RenderGrid(IReadOnlyList<GridCell> cells) {
        _canvas.Children.Clear();
        EnsureTransform();

        var region = ComputeRegionFromCells(cells);
        var (cols, rows) = DimensionsFromCells(cells);

        var firstDip = DipRectForCell(0, 0, region, cols, rows);
        bool useExternalLabels = ShouldUseExternalLabels(firstDip.Height, firstDip.Width, _minLabelFontSize);

        double borderThickness = useExternalLabels ? Math.Max(0.5, _theme.CellBorderThickness * 0.5) : _theme.CellBorderThickness;
        var borderBrush = useExternalLabels ? _subgridBorderBrush03 : _cellBorderBrush;

        foreach (var cell in cells) {
            var dipRect = DipRectForCell(cell.Row, cell.Col, region, cols, rows);
            AddCellRect(dipRect, _cellBgBrush, borderBrush, borderThickness);
            if (!useExternalLabels) {
                AddLabel(dipRect, cell.Row, cell.Col, _labelBrush);
            }
        }

        if (useExternalLabels) {
            RenderExternalColumnLabels(cells, region);
            RenderExternalRowLabels(cells, region);
        }
    }

    /// <summary>
    /// Highlights a column and dims non-matching cells.
    /// </summary>
    public void HighlightColumn(IReadOnlyList<GridCell> cells, int col) {
        _canvas.Children.Clear();
        EnsureTransform();

        var region = ComputeRegionFromCells(cells);
        var (cols, rows) = DimensionsFromCells(cells);

        var firstDip = DipRectForCell(0, 0, region, cols, rows);
        bool useExternalLabels = ShouldUseExternalLabels(firstDip.Height, firstDip.Width, _minLabelFontSize);

        double borderThickness = useExternalLabels ? Math.Max(0.5, _theme.CellBorderThickness * 0.5) : _theme.CellBorderThickness;
        var borderBrush = useExternalLabels ? _subgridBorderBrush03 : _cellBorderBrush;
        var highlightBg = useExternalLabels ? _highlightBg03 : _highlightBg;

        foreach (var cell in cells) {
            var dipRect = DipRectForCell(cell.Row, cell.Col, region, cols, rows);
            bool isHighlighted = cell.Col == col;
            AddCellRect(dipRect,
                isHighlighted ? highlightBg : _dimBrush,
                isHighlighted ? _highlightBorder : borderBrush,
                isHighlighted ? borderThickness * 2 : borderThickness);
            if (!useExternalLabels) {
                AddLabel(dipRect, cell.Row, cell.Col, _labelBrush, isHighlighted ? 1.0 : 0.3);
            }
        }

        if (useExternalLabels) {
            RenderExternalColumnLabels(cells, region);
            RenderExternalRowLabels(cells, region);
        }
    }

    /// <summary>
    /// Highlights a single cell (arrow navigation) with crosshair (row + column + intersection).
    /// </summary>
    public void HighlightCell(IReadOnlyList<GridCell> cells, GridCell highlightedCell) {
        _canvas.Children.Clear();
        EnsureTransform();

        var region = ComputeRegionFromCells(cells);
        var (cols, rows) = DimensionsFromCells(cells);

        var firstDip = DipRectForCell(0, 0, region, cols, rows);
        bool useExternalLabels = ShouldUseExternalLabels(firstDip.Height, firstDip.Width, _minLabelFontSize);

        double borderThickness = useExternalLabels ? Math.Max(0.5, _theme.CellBorderThickness * 0.5) : _theme.CellBorderThickness;
        var borderBrush = useExternalLabels ? _subgridBorderBrush03 : _cellBorderBrush;

        foreach (var cell in cells) {
            var dipRect = DipRectForCell(cell.Row, cell.Col, region, cols, rows);
            bool isCell = cell.Row == highlightedCell.Row && cell.Col == highlightedCell.Col;
            bool inCrossHair = cell.Row == highlightedCell.Row || cell.Col == highlightedCell.Col;

            Brush bg = isCell ? _highlightBg
                : inCrossHair ? _crosshairBg
                : _cellBgBrush;
            Brush border = inCrossHair ? _highlightBorder : borderBrush;
            double thickness = isCell ? borderThickness * 2
                : inCrossHair ? borderThickness * 1.5
                : borderThickness;

            AddCellRect(dipRect, bg, border, thickness);
            if (!useExternalLabels) {
                AddLabel(dipRect, cell.Row, cell.Col, _labelBrush);
            }
        }

        if (useExternalLabels) {
            RenderExternalColumnLabels(cells, region);
            RenderExternalRowLabels(cells, region);
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
        var (cols, rows) = DimensionsFromCells(subgridCells);

        var firstDip = DipRectForCell(0, 0, region, cols, rows);
        bool useExternalLabels = ShouldUseExternalLabels(firstDip.Height, firstDip.Width, _minLabelFontSize);

        double borderThickness = useExternalLabels ? Math.Max(0.5, _theme.CellBorderThickness * 0.5) : _theme.CellBorderThickness;
        var borderBrush = useExternalLabels ? _subgridBorderBrush03 : _subgridBorderBrush;
        var highlightBg = useExternalLabels ? _highlightBg03 : _highlightBg;

        if (useExternalLabels) {
            double rowHeight = region.Height / rows;
            for (int r = 0; r < rows; r += 2) {
                var rowRect = new Rect(region.X, region.Y + r * rowHeight, region.Width, rowHeight);
                AddCellRect(rowRect, _rowBandBrush, Brushes.Transparent, 0);
            }
        }

        foreach (var cell in subgridCells) {
            var dipRect = DipRectForCell(cell.Row, cell.Col, region, cols, rows);
            bool isHighlighted = cell.Col == col;
            AddCellRect(dipRect,
                isHighlighted ? highlightBg : _dimBrush,
                isHighlighted ? _highlightBorder : borderBrush,
                isHighlighted ? borderThickness * 2 : borderThickness);
            if (!useExternalLabels) {
                AddLabel(dipRect, cell.Row, cell.Col, _subgridLabelBrush, isHighlighted ? 1.0 : 0.3, 0.9);
            }
        }

        if (useExternalLabels) {
            RenderExternalColumnLabels(subgridCells, region);
            RenderExternalRowLabels(subgridCells, region);
        }
    }

    /// <summary>
    /// Highlights a single cell (arrow navigation) within a subgrid, rendered over a faint background grid.
    /// </summary>
    public void HighlightCellOverGrid(IReadOnlyList<GridCell> backgroundCells, IReadOnlyList<GridCell> subgridCells, GridCell highlightedCell) {
        _canvas.Children.Clear();
        if (subgridCells.Count == 0) {
            return;
        }

        EnsureTransform();

        RenderBackgroundGrid(backgroundCells);

        var region = ComputeRegionFromCells(subgridCells);
        var (cols, rows) = DimensionsFromCells(subgridCells);

        var firstDip = DipRectForCell(0, 0, region, cols, rows);
        bool useExternalLabels = ShouldUseExternalLabels(firstDip.Height, firstDip.Width, _minLabelFontSize);

        double borderThickness = useExternalLabels ? Math.Max(0.5, _theme.CellBorderThickness * 0.5) : _theme.CellBorderThickness;
        var borderBrush = useExternalLabels ? _subgridBorderBrush03 : _subgridBorderBrush;

        if (useExternalLabels) {
            double rowHeight = region.Height / rows;
            for (int r = 0; r < rows; r += 2) {
                var rowRect = new Rect(region.X, region.Y + r * rowHeight, region.Width, rowHeight);
                AddCellRect(rowRect, _rowBandBrush, Brushes.Transparent, 0);
            }
        }

        foreach (var cell in subgridCells) {
            var dipRect = DipRectForCell(cell.Row, cell.Col, region, cols, rows);
            bool isCell = cell.Row == highlightedCell.Row && cell.Col == highlightedCell.Col;
            bool inCrossHair = cell.Row == highlightedCell.Row || cell.Col == highlightedCell.Col;

            Brush bg = isCell ? _highlightBg
                : inCrossHair ? _crosshairBg
                : _cellBgBrush;
            Brush border = inCrossHair ? _highlightBorder : borderBrush;
            double thickness = isCell ? borderThickness * 2
                : inCrossHair ? borderThickness * 1.5
                : borderThickness;

            AddCellRect(dipRect, bg, border, thickness);
            if (!useExternalLabels) {
                AddLabel(dipRect, cell.Row, cell.Col, _subgridLabelBrush, heightFraction: 0.9);
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
        int cols = _labelGenerator.Cols;
        int rows = _labelGenerator.Rows;

        foreach (var cell in cells) {
            var dipRect = DipRectForCell(cell.Row, cell.Col, region, cols, rows);
            AddCellRect(dipRect, Brushes.Transparent, _bgGridBorderBrush);
        }
    }

    /// <summary>
    /// Renders subgrid content (cells + labels) without clearing canvas.
    /// </summary>
    private void RenderSubgridContent(IReadOnlyList<GridCell> cells) {
        var region = ComputeRegionFromCells(cells);
        var (cols, rows) = DimensionsFromCells(cells);

        var firstDip = DipRectForCell(0, 0, region, cols, rows);
        bool useExternalLabels = ShouldUseExternalLabels(firstDip.Height, firstDip.Width, _minLabelFontSize);

        double borderThickness = useExternalLabels ? Math.Max(0.5, _theme.CellBorderThickness * 0.5) : _theme.CellBorderThickness;
        var borderBrush = useExternalLabels ? _subgridBorderBrush03 : _subgridBorderBrush;

        foreach (var cell in cells) {
            var dipRect = DipRectForCell(cell.Row, cell.Col, region, cols, rows);
            AddCellRect(dipRect, _cellBgBrush, borderBrush, borderThickness);
        }

        if (useExternalLabels) {
            RenderExternalLabels(cells, region);
        } else {
            foreach (var cell in cells) {
                AddLabel(DipRectForCell(cell.Row, cell.Col, region, cols, rows), cell.Row, cell.Col, _subgridLabelBrush, heightFraction: 0.9);
            }
        }
    }

    /// <summary>
    /// Renders first-key labels (top/bottom) and second-key labels (left/right) outside the subgrid.
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
        var (cols, rows) = DimensionsFromCells(cells);

        var firstDip = DipRectForCell(0, 0, region, cols, rows);
        double fontSize = ComputeExternalFontSize(firstDip);
        double standardMargin = fontSize * 1.5;

        double maxLabelWidth = 0;
        for (int c = 0; c < cols; c++) {
            var label = _labelGenerator.LabelFor(_labelRowOffset, c + _labelColOffset);
            var size = MeasureText(label.First, _typeface, fontSize);
            maxLabelWidth = Math.Max(maxLabelWidth, size.Width);
        }

        var (fanOutDist, labelExtent) = ComputeFanOut(cols, maxLabelWidth, region.Width, standardMargin);
        double labelExtentStart = region.X + region.Width / 2 - labelExtent / 2;
        double screenWidth = Math.Max(_canvas.ActualWidth, 1);
        labelExtentStart = Math.Clamp(labelExtentStart, 0, Math.Max(0, screenWidth - labelExtent));
        double labelSpacing = labelExtent / cols;

        double gridTop = region.Y;
        double gridBottom = region.Y + region.Height;

        double screenHeight = Math.Max(_canvas.ActualHeight, 1);
        bool showAbove = gridTop >= fanOutDist;
        bool showBelow = (screenHeight - gridBottom) >= fanOutDist;
        if (!showAbove && !showBelow) {
            showAbove = true;
            showBelow = true;
        }

        for (int c = 0; c < cols; c++) {
            var cellDip = DipRectForCell(0, c, region, cols, rows);
            var cellLabel = _labelGenerator.LabelFor(_labelRowOffset, c + _labelColOffset);
            double anchorX = cellDip.X + cellDip.Width / 2;
            double labelCenterX = labelExtentStart + (c + 0.5) * labelSpacing;
            var labelSize = MeasureText(cellLabel.First, _typeface, fontSize);

            if (showAbove) {
                double labelY = gridTop - fanOutDist;
                AddOutlinedText(cellLabel.First, _typeface, fontSize, _extColLabelBrush, _outlineBrush, _theme.LabelOutlineThickness, 1.0,
                    labelCenterX - labelSize.Width / 2, labelY, labelSize.Width, labelSize.Height);
                _canvas.Children.Add(CreateConnector(anchorX, gridTop, labelCenterX, labelY + labelSize.Height + 2, _connectorBrush));
            }

            if (showBelow) {
                double labelY = gridBottom + fanOutDist - labelSize.Height;
                AddOutlinedText(cellLabel.First, _typeface, fontSize, _extColLabelBrush, _outlineBrush, _theme.LabelOutlineThickness, 1.0,
                    labelCenterX - labelSize.Width / 2, labelY, labelSize.Width, labelSize.Height);
                _canvas.Children.Add(CreateConnector(anchorX, gridBottom, labelCenterX, labelY - 2, _connectorBrush));
            }
        }
    }

    /// <summary>
    /// Renders row second-key labels to the left and right of the grid.
    /// </summary>
    private void RenderExternalRowLabels(IReadOnlyList<GridCell> cells, Rect region) {
        var (cols, rows) = DimensionsFromCells(cells);

        var firstDip = DipRectForCell(0, 0, region, cols, rows);
        double fontSize = ComputeExternalFontSize(firstDip);
        double standardMargin = fontSize * 1.5;

        double maxLabelHeight = 0;
        for (int r = 0; r < rows; r++) {
            var label = _labelGenerator.LabelFor(r + _labelRowOffset, _labelColOffset);
            var size = MeasureText(label.Second, _typeface, fontSize);
            maxLabelHeight = Math.Max(maxLabelHeight, size.Height);
        }

        var (fanOutDist, labelExtent) = ComputeFanOut(rows, maxLabelHeight, region.Height, standardMargin);
        double labelExtentStart = region.Y + region.Height / 2 - labelExtent / 2;
        double screenHeight = Math.Max(_canvas.ActualHeight, 1);
        labelExtentStart = Math.Clamp(labelExtentStart, 0, Math.Max(0, screenHeight - labelExtent));
        double labelSpacing = labelExtent / rows;

        double gridLeft = region.X;
        double gridRight = region.X + region.Width;

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
            var cellLabel = _labelGenerator.LabelFor(r + _labelRowOffset, _labelColOffset);
            double anchorY = cellDip.Y + cellDip.Height / 2;
            double labelCenterY = labelExtentStart + (r + 0.5) * labelSpacing;
            var labelSize = MeasureText(cellLabel.Second, _typeface, fontSize);

            if (showLeft) {
                double labelX = gridLeft - fanOutDist;
                AddOutlinedText(cellLabel.Second, _typeface, fontSize, _extRowLabelBrush, _outlineBrush, _theme.LabelOutlineThickness, 1.0,
                    labelX, labelCenterY - labelSize.Height / 2, labelSize.Width, labelSize.Height);
                _canvas.Children.Add(CreateConnector(gridLeft, anchorY, labelX + labelSize.Width + 2, labelCenterY, _connectorBrush));
            }

            if (showRight) {
                double labelX = gridRight + fanOutDist - labelSize.Width;
                AddOutlinedText(cellLabel.Second, _typeface, fontSize, _extRowLabelBrush, _outlineBrush, _theme.LabelOutlineThickness, 1.0,
                    labelX, labelCenterY - labelSize.Height / 2, labelSize.Width, labelSize.Height);
                _canvas.Children.Add(CreateConnector(gridRight, anchorY, labelX - 2, labelCenterY, _connectorBrush));
            }
        }
    }

    /// <summary>
    /// Computes fan-out parameters when external labels would overlap at the grid edge.
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
    /// floored at _minLabelFontSize so labels stay readable at L3.
    /// </summary>
    private double ComputeExternalFontSize(Rect cellDip) {
        double cellBased = ComputeAutoFontSize(cellDip.Width / 2, cellDip.Height, 0.9);
        return Math.Max(cellBased, _minLabelFontSize);
    }

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
        try {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var brush = new SolidColorBrush(color) { Opacity = opacity };
            brush.Freeze();
            return brush;
        } catch {
            var fallback = new SolidColorBrush(Colors.Transparent) { Opacity = opacity };
            fallback.Freeze();
            return fallback;
        }
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
    /// Reuses a single Rectangle to prevent accumulation on rapid key presses.
    /// </summary>
    public void FlashInvalidKey() {
        if (_flashRect is null) {
            _flashRect = new Rectangle {
                Fill = new SolidColorBrush(Color.FromArgb(80, 255, 0, 0)),
                IsHitTestVisible = false,
            };
            _flashRect.Fill.Freeze();
        }

        _flashRect.Width = _canvas.ActualWidth;
        _flashRect.Height = _canvas.ActualHeight;
        Canvas.SetLeft(_flashRect, 0);
        Canvas.SetTop(_flashRect, 0);

        // Cancel any in-flight animation before starting a new one
        _flashRect.BeginAnimation(UIElement.OpacityProperty, null);
        _flashRect.Opacity = 1.0;

        if (_flashRect.Parent is null) {
            _canvas.Children.Add(_flashRect);
        }

        var animation = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(200)) {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        animation.Completed += (_, _) => {
            _flashRect.Visibility = Visibility.Collapsed;
            _canvas.Children.Remove(_flashRect);
        };
        _flashRect.Visibility = Visibility.Visible;
        _flashRect.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    /// <summary>
    /// Determines whether external labels should be used based on cell dimensions
    /// and minimum label font size.
    /// </summary>
    internal static bool ShouldUseExternalLabels(double cellDipHeight, double cellDipWidth, double minLabelFontSize)
        => cellDipHeight < (minLabelFontSize * 1.8) || (cellDipWidth / 2) < minLabelFontSize * 1.6;
}
