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
/// Renders log-scale grid-mode grids on the overlay Canvas.
/// All cells have borders. Large cells get an inline two-char label (colChar + rowChar,
/// using two <see cref="AxisLabelGenerator"/> instances). Small cells (below the
/// external-label threshold) get a tinted background and no inline label; instead their
/// column/row is annotated with axis-edge external labels with fan-out connectors.
/// Uses element pooling: shapes are mutated (not recreated) across re-renders.
/// Staleness detected via <c>Parent == null</c> after external canvas clear.
/// </summary>
public sealed class LogGridRenderer : ILogGridRenderer {
    readonly Canvas _canvas;
    readonly ThemeModel _theme;
    readonly AxisLabelGenerator _colLabels;
    readonly AxisLabelGenerator _rowLabels;
    readonly double _minLabelFontSize;

    Matrix _transformFromDevice = Matrix.Identity;
    bool _transformInitialized;

    // Cached brushes
    readonly SolidColorBrush _cellBorderBrush;
    readonly SolidColorBrush _cellBgBrush;
    readonly SolidColorBrush _smallCellBgBrush;
    readonly SolidColorBrush _labelBrush;
    readonly SolidColorBrush _outlineBrush;
    readonly SolidColorBrush _extLabelBrush;
    readonly SolidColorBrush _extRowLabelBrush;
    readonly SolidColorBrush _connectorBrush;
    readonly SolidColorBrush _labelBorderBrush;
    readonly SolidColorBrush _dimBrush;
    readonly SolidColorBrush _highlightBgBrush;
    readonly SolidColorBrush _highlightBorderBrush;
    readonly SolidColorBrush _crosshairBrush;
    readonly Typeface _typeface;

    // Element pools
    readonly List<Rectangle> _rectPool = [];
    readonly List<(Path Outline, Path Fill)> _textPool = [];
    readonly List<Line> _linePool = [];
    int _nextRect;
    int _nextText;
    int _nextLine;

    Rectangle? _flashRect;

    public LogGridRenderer(
        Canvas canvas, ThemeModel theme,
        AxisLabelGenerator colLabels, AxisLabelGenerator rowLabels,
        double minLabelFontSize) {
        _canvas = canvas;
        _theme = theme;
        _colLabels = colLabels;
        _rowLabels = rowLabels;
        _minLabelFontSize = minLabelFontSize;

        _cellBorderBrush = BrushFromHex(theme.CellBorderColor);
        _cellBgBrush = BrushFromHex(theme.CellBackgroundColor, theme.CellBackgroundOpacity);
        _smallCellBgBrush = BrushFromHex(theme.SmallCellBackgroundColor, theme.SmallCellBackgroundOpacity);
        _labelBrush = BrushFromHex(theme.LabelColor);
        _outlineBrush = BrushFromHex(theme.LabelOutlineColor);
        _extLabelBrush = BrushFromHex(theme.ExternalLabelColor);
        _extRowLabelBrush = BrushFromHex(theme.ExternalRowLabelColor);
        _connectorBrush = BrushFromHex(theme.ConnectorLineColor);
        _labelBorderBrush = BrushFromHex("#0A1A3A", 0.85);
        _dimBrush = BrushFromHex(theme.DimmedOverlayColor, theme.DimmedOverlayOpacity);
        _highlightBgBrush = BrushFromHex(theme.HighlightedColumnBackground, 0.5);
        _highlightBorderBrush = BrushFromHex(theme.HighlightedColumnBorderColor);
        _crosshairBrush = BrushFromHex(theme.HighlightedColumnBackground, 0.25);

        var fontFamily = new FontFamily(theme.LabelFontFamily);
        var fontWeight = ParseFontWeight(theme.LabelFontWeight);
        _typeface = new Typeface(fontFamily, FontStyles.Normal, fontWeight, FontStretches.Normal);
    }

    public void SetTransform(Matrix transformFromDevice) {
        _transformFromDevice = transformFromDevice;
        _transformInitialized = true;
    }

    public void RenderGrid(LogGrid grid) {
        BeginRender();
        EnsureTransform();
        RenderCells(grid, highlightCol: -1, highlightCell: null);
        RenderLabelBorders(grid, showCols: true, showRows: false);
        EndRender();
    }

    public void HighlightColumn(LogGrid grid, int col) {
        BeginRender();
        EnsureTransform();
        RenderCells(grid, highlightCol: col, highlightCell: null);
        RenderLabelBorders(grid, showCols: false, showRows: true);
        EndRender();
    }

    public void HighlightCell(LogGrid grid, GridCell cell) {
        BeginRender();
        EnsureTransform();
        RenderCells(grid, highlightCol: -1, highlightCell: cell);
        RenderLabelBorders(grid, showCols: true, showRows: true);
        EndRender();
    }

    public void FlashInvalidKey() {
        if (_flashRect is null || _flashRect.Parent is null) {
            _flashRect = new Rectangle {
                Fill = new SolidColorBrush(Color.FromArgb(80, 255, 0, 0)),
                IsHitTestVisible = false,
            };
            _canvas.Children.Add(_flashRect);
        }

        _flashRect.Width = _canvas.ActualWidth;
        _flashRect.Height = _canvas.ActualHeight;
        _flashRect.Visibility = Visibility.Visible;
        _flashRect.Opacity = 1.0;
        Canvas.SetLeft(_flashRect, 0);
        Canvas.SetTop(_flashRect, 0);

        _flashRect.BeginAnimation(UIElement.OpacityProperty, null);

        var animation = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(200)) {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        animation.Completed += (_, _) => {
            if (_flashRect is not null) {
                _flashRect.Visibility = Visibility.Collapsed;
            }
        };
        _flashRect.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    public void ClearCanvas() {
        _canvas.Children.Clear();
        _rectPool.Clear();
        _textPool.Clear();
        _linePool.Clear();
        _flashRect = null;
        _indicatorOutline = null;
        _indicatorFill = null;
    }

    // --- First-key indicator ---

    Path? _indicatorOutline;
    Path? _indicatorFill;

    public void RenderFirstKeyIndicator(LogGrid grid, string label, System.Drawing.Rectangle screenBounds) {
        EnsureTransform();

        double fontSize = Math.Max(48, _canvas.ActualHeight * 0.08);
        var ft = new FormattedText(
            label,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            _typeface, fontSize, _labelBrush,
            VisualTreeHelper.GetDpi(_canvas).PixelsPerDip);

        var indicatorSize = new System.Drawing.Size((int)(ft.Width + 20), (int)(ft.Height + 20));
        var pos = QuadrantCornerHelper.GetIndicatorPosition(grid.CenterPoint, screenBounds, indicatorSize);

        var dipPos = _transformFromDevice.Transform(new Point(pos.X, pos.Y));
        var geometry = ft.BuildGeometry(new Point(0, 0));
        var bounds = geometry.Bounds;

        double offsetX = dipPos.X + 10 - bounds.X;
        double offsetY = dipPos.Y + 10 - bounds.Y;

        if (_indicatorOutline is null || _indicatorOutline.Parent is null) {
            _indicatorOutline = new Path { IsHitTestVisible = false };
            _indicatorFill = new Path { IsHitTestVisible = false };
            _canvas.Children.Add(_indicatorOutline);
            _canvas.Children.Add(_indicatorFill);
        }

        double outlineThick = Math.Max(_theme.LabelOutlineThickness * 3, fontSize * 0.1);

        _indicatorOutline!.Data = geometry;
        _indicatorOutline.Fill = Brushes.Transparent;
        _indicatorOutline.Stroke = _outlineBrush;
        _indicatorOutline.StrokeThickness = outlineThick;
        _indicatorOutline.StrokeLineJoin = PenLineJoin.Round;
        _indicatorOutline.Opacity = 0.9;
        _indicatorOutline.Visibility = Visibility.Visible;
        Canvas.SetLeft(_indicatorOutline, offsetX);
        Canvas.SetTop(_indicatorOutline, offsetY);

        _indicatorFill!.Data = geometry;
        _indicatorFill.Fill = _labelBrush;
        _indicatorFill.Stroke = null;
        _indicatorFill.Opacity = 0.9;
        _indicatorFill.Visibility = Visibility.Visible;
        Canvas.SetLeft(_indicatorFill, offsetX);
        Canvas.SetTop(_indicatorFill, offsetY);
    }

    public void HideFirstKeyIndicator() {
        if (_indicatorOutline is not null) {
            _indicatorOutline.Visibility = Visibility.Collapsed;
        }

        if (_indicatorFill is not null) {
            _indicatorFill.Visibility = Visibility.Collapsed;
        }
    }

    // --- Core rendering ---

    void RenderCells(LogGrid grid, int highlightCol, GridCell? highlightCell) {
        for (int row = 0; row < grid.Rows; row++) {
            for (int col = 0; col < grid.Cols; col++) {
                var cell = grid.CellAt(row, col);
                var dipRect = DipRect(cell);

                if (dipRect.Width <= 0 || dipRect.Height <= 0) {
                    continue;
                }

                bool isSmall = IsSmallCell(dipRect);

                Brush bg;
                Brush border;
                double borderThick;

                if (highlightCol >= 0) {
                    bool isHighlighted = col == highlightCol;
                    if (isHighlighted) {
                        bg = _highlightBgBrush;
                        border = _highlightBorderBrush;
                        borderThick = _theme.CellBorderThickness * 2;
                    } else {
                        bg = isSmall ? _smallCellBgBrush : _dimBrush;
                        border = _cellBorderBrush;
                        borderThick = _theme.CellBorderThickness;
                    }
                } else if (highlightCell.HasValue) {
                    bool isTarget = row == highlightCell.Value.Row && col == highlightCell.Value.Col;
                    bool inCrosshair = row == highlightCell.Value.Row || col == highlightCell.Value.Col;
                    bg = isTarget ? _highlightBgBrush
                        : inCrosshair ? _crosshairBrush
                        : isSmall ? _smallCellBgBrush
                        : _cellBgBrush;
                    border = inCrosshair ? _highlightBorderBrush : _cellBorderBrush;
                    borderThick = isTarget ? _theme.CellBorderThickness * 2
                        : inCrosshair ? _theme.CellBorderThickness * 1.5
                        : _theme.CellBorderThickness;
                } else {
                    bg = isSmall ? _smallCellBgBrush : _cellBgBrush;
                    border = _cellBorderBrush;
                    borderThick = _theme.CellBorderThickness;
                }

                UseRect(dipRect, bg, border, borderThick);

                if (!isSmall) {
                    double opacity = (highlightCol >= 0 && col != highlightCol) ? 0.3 : 1.0;
                    string label = _colLabels.LabelFor(col) + _rowLabels.LabelFor(row);
                    double fontSize = ComputeInlineFontSize(dipRect);
                    UseLabel(dipRect, label, fontSize, _labelBrush, opacity);
                }
            }
        }
    }

    void RenderLabelBorders(LogGrid grid, bool showCols, bool showRows) {
        double borderThickness = 2 * _minLabelFontSize;
        double screenWidth = Math.Max(_canvas.ActualWidth, 1);
        double screenHeight = Math.Max(_canvas.ActualHeight, 1);

        if (showCols && HasSmallColumns(grid)) {
            RenderBorderColumnLabels(grid, borderThickness, screenWidth);
        }

        if (showRows && HasSmallRows(grid)) {
            RenderBorderRowLabels(grid, borderThickness, screenHeight);
        }
    }

    bool HasSmallColumns(LogGrid grid) {
        for (int col = 0; col < grid.Cols; col++) {
            if (IsNarrowColumn(DipRect(grid.CellAt(0, col)))) {
                return true;
            }
        }

        return false;
    }

    bool HasSmallRows(LogGrid grid) {
        for (int row = 0; row < grid.Rows; row++) {
            if (IsShortRow(DipRect(grid.CellAt(row, 0)))) {
                return true;
            }
        }

        return false;
    }

    void RenderBorderColumnLabels(LogGrid grid, double borderThickness, double screenWidth) {
        // Find columns too narrow to fit an inline label (skip zero-width collapsed cells)
        var externalCols = new List<int>();
        for (int col = 0; col < grid.Cols; col++) {
            var dipRect = DipRect(grid.CellAt(0, col));
            if (dipRect.Width <= 0) {
                continue;
            }

            if (IsNarrowColumn(dipRect)) {
                externalCols.Add(col);
            }
        }

        if (externalCols.Count == 0) {
            return;
        }

        double fontSize = ExternalFontSize();

        // The inner 4×4 cells around center. Labels render just outside that area
        // vertically (above/below the inner rows).
        int halfCols = grid.Cols / 2;
        int halfRows = grid.Rows / 2;
        int innerTop = Math.Max(0, halfRows - 2);
        int innerBottom = Math.Min(grid.Rows, halfRows + 2);
        var innerTopDip = DipPoint(grid.ColEdges[0], grid.RowEdges[innerTop]);
        var innerBottomDip = DipPoint(grid.ColEdges[0], grid.RowEdges[innerBottom]);
        double clusterTop = innerTopDip.Y;
        double clusterBottom = innerBottomDip.Y;

        // Compute ideal positions: spread labels symmetrically from grid center.
        var gridCenterDip = DipPoint(grid.ColEdges[halfCols], grid.RowEdges[0]);
        double gridCenterX = gridCenterDip.X;

        var labelPositions = new double[externalCols.Count];
        var labelWidths = new double[externalCols.Count];
        for (int i = 0; i < externalCols.Count; i++) {
            int col = externalCols[i];
            labelWidths[i] = MeasureText(_colLabels.LabelFor(col), fontSize).Width;

            var colTL = DipPoint(grid.ColEdges[col], grid.RowEdges[0]);
            var colBR = DipPoint(grid.ColEdges[col + 1], grid.RowEdges[0]);
            labelPositions[i] = (colTL.X + colBR.X) / 2;
        }

        // Resolve overlaps
        double center = gridCenterX;
        ResolveOverlaps(labelPositions, labelWidths, 0, screenWidth, center);

        double labelHeight = MeasureText("X", fontSize).Height;
        double labelMargin = fontSize * 0.5;

        // Guide lines end at cell center of row halfRows-1 (above) / halfRows (below)
        int guideRowAbove = Math.Max(0, halfRows - 2);
        int guideRowBelow = Math.Min(grid.Rows - 2, halfRows);
        double guideYAbove = (DipPoint(0, grid.RowEdges[guideRowAbove]).Y + DipPoint(0, grid.RowEdges[guideRowAbove + 2]).Y) / 2;
        double guideYBelow = (DipPoint(0, grid.RowEdges[guideRowBelow]).Y + DipPoint(0, grid.RowEdges[guideRowBelow + 2]).Y) / 2;

        // Determine which sides have room
        bool showAbove = clusterTop >= labelHeight + labelMargin * 2;
        bool showBelow = (_canvas.ActualHeight - clusterBottom) >= labelHeight + labelMargin * 2;
        if (!showAbove && !showBelow) { showAbove = true; } // fallback: at least one side

        for (int i = 0; i < externalCols.Count; i++) {
            int col = externalCols[i];
            string label = _colLabels.LabelFor(col);
            var colTL = DipPoint(grid.ColEdges[col], grid.RowEdges[0]);
            var colBR = DipPoint(grid.ColEdges[col + 1], grid.RowEdges[0]);
            double anchorX = (colTL.X + colBR.X) / 2;
            double labelCenterX = labelPositions[i];
            var labelSize = MeasureText(label, fontSize);

            if (showAbove) {
                double topLabelY = clusterTop - labelMargin - labelSize.Height;
                UseLabel(new Rect(labelCenterX - labelSize.Width / 2, topLabelY, labelSize.Width, labelSize.Height),
                    label, fontSize, _extLabelBrush, 1.0);
                UseLine(anchorX, guideYAbove, labelCenterX, topLabelY + labelSize.Height + 2);
            }

            if (showBelow) {
                double bottomLabelY = clusterBottom + labelMargin;
                UseLabel(new Rect(labelCenterX - labelSize.Width / 2, bottomLabelY, labelSize.Width, labelSize.Height),
                    label, fontSize, _extLabelBrush, 1.0);
                UseLine(anchorX, guideYBelow, labelCenterX, bottomLabelY - 2);
            }
        }
    }

    void RenderBorderRowLabels(LogGrid grid, double borderThickness, double screenHeight) {
        // Find rows too short to fit an inline label (skip zero-height collapsed cells)
        var externalRows = new List<int>();
        for (int row = 0; row < grid.Rows; row++) {
            var dipRect = DipRect(grid.CellAt(row, 0));
            if (dipRect.Height <= 0) {
                continue;
            }

            if (IsShortRow(dipRect)) {
                externalRows.Add(row);
            }
        }

        if (externalRows.Count == 0) {
            return;
        }

        double fontSize = ExternalFontSize();
        double screenWidth = Math.Max(_canvas.ActualWidth, 1);

        // The inner 4×4 cells around center. Labels render just outside that area
        // horizontally (left/right of the inner columns).
        int halfCols = grid.Cols / 2;
        int halfRows = grid.Rows / 2;
        int innerLeft = Math.Max(0, halfCols - 2);
        int innerRight = Math.Min(grid.Cols, halfCols + 2);
        var innerLeftDip = DipPoint(grid.ColEdges[innerLeft], grid.RowEdges[0]);
        var innerRightDip = DipPoint(grid.ColEdges[innerRight], grid.RowEdges[0]);
        double clusterLeft = innerLeftDip.X;
        double clusterRight = innerRightDip.X;

        // Compute ideal positions: spread labels symmetrically from grid center.
        var gridCenterDip = DipPoint(grid.ColEdges[0], grid.RowEdges[grid.Rows / 2]);
        double gridCenterY = gridCenterDip.Y;

        var labelPositions = new double[externalRows.Count];
        var labelHeights = new double[externalRows.Count];
        for (int i = 0; i < externalRows.Count; i++) {
            int row = externalRows[i];
            labelHeights[i] = MeasureText(_rowLabels.LabelFor(row), fontSize).Height;

            var rowTL = DipPoint(grid.ColEdges[0], grid.RowEdges[row]);
            var rowBL = DipPoint(grid.ColEdges[0], grid.RowEdges[row + 1]);
            labelPositions[i] = (rowTL.Y + rowBL.Y) / 2;
        }

        // Resolve overlaps
        double center = gridCenterY;
        ResolveOverlaps(labelPositions, labelHeights, 0, screenHeight, center);

        double labelWidth = MeasureText("W", fontSize).Width;
        double labelMargin = fontSize * 0.5;

        // Guide lines end at cell center of col halfCols-1 (left) / halfCols (right)
        double guideXLeft = (DipPoint(grid.ColEdges[Math.Max(0, halfCols - 1)], 0).X + DipPoint(grid.ColEdges[halfCols], 0).X) / 2;
        double guideXRight = (DipPoint(grid.ColEdges[halfCols], 0).X + DipPoint(grid.ColEdges[Math.Min(grid.Cols, halfCols + 1)], 0).X) / 2;

        // Determine which sides have room
        bool showLeft = clusterLeft >= labelWidth + labelMargin * 2;
        bool showRight = (screenWidth - clusterRight) >= labelWidth + labelMargin * 2;
        if (!showLeft && !showRight) { showLeft = true; } // fallback

        for (int i = 0; i < externalRows.Count; i++) {
            int row = externalRows[i];
            string label = _rowLabels.LabelFor(row);
            var rowTL = DipPoint(grid.ColEdges[0], grid.RowEdges[row]);
            var rowBL = DipPoint(grid.ColEdges[0], grid.RowEdges[row + 1]);
            double anchorY = (rowTL.Y + rowBL.Y) / 2;
            double labelCenterY = labelPositions[i];
            var labelSize = MeasureText(label, fontSize);

            if (showLeft) {
                double leftLabelX = clusterLeft - labelMargin - labelSize.Width;
                UseLabel(new Rect(leftLabelX, labelCenterY - labelSize.Height / 2, labelSize.Width, labelSize.Height),
                    label, fontSize, _extRowLabelBrush, 1.0);
                UseLine(guideXLeft, anchorY, leftLabelX + labelSize.Width + 2, labelCenterY);
            }

            if (showRight) {
                double rightLabelX = clusterRight + labelMargin;
                UseLabel(new Rect(rightLabelX, labelCenterY - labelSize.Height / 2, labelSize.Width, labelSize.Height),
                    label, fontSize, _extRowLabelBrush, 1.0);
                UseLine(guideXRight, anchorY, rightLabelX - 2, labelCenterY);
            }
        }
    }

    /// <summary>
    /// Resolves label overlap by iteratively pushing apart labels that are too close.
    /// Labels start at their ideal positions (column/row centers) and only move
    /// when adjacent labels would overlap. After resolving, the group is re-centered
    /// around the anchor midpoint so connector lines have symmetric slopes.
    /// Clamped to [min, max].
    /// </summary>
    static void ResolveOverlaps(double[] positions, double[] sizes, double min, double max, double anchorCenter) {
        double gap = Math.Max(8.0, (max - min) * 0.01); // minimum gap between labels
        const int maxPasses = 10;

        for (int pass = 0; pass < maxPasses; pass++) {
            bool moved = false;

            // Forward pass: push right/down
            for (int i = 1; i < positions.Length; i++) {
                double prevEnd = positions[i - 1] + sizes[i - 1] / 2 + gap;
                double currStart = positions[i] - sizes[i] / 2;
                if (currStart < prevEnd) {
                    positions[i] = prevEnd + sizes[i] / 2;
                    moved = true;
                }
            }

            // Backward pass: push left/up
            for (int i = positions.Length - 2; i >= 0; i--) {
                double nextStart = positions[i + 1] - sizes[i + 1] / 2 - gap;
                double currEnd = positions[i] + sizes[i] / 2;
                if (currEnd > nextStart) {
                    positions[i] = nextStart - sizes[i] / 2;
                    moved = true;
                }
            }

            if (!moved) {
                break;
            }
        }

        // Re-center the label group around the anchor midpoint for symmetric slopes
        double labelLeft = positions[0] - sizes[0] / 2;
        double labelRight = positions[^1] + sizes[^1] / 2;
        double labelCenter = (labelLeft + labelRight) / 2;
        double shift = anchorCenter - labelCenter;
        for (int i = 0; i < positions.Length; i++) {
            positions[i] += shift;
        }

        // Clamp to bounds then re-resolve overlaps caused by clamping
        for (int i = 0; i < positions.Length; i++) {
            positions[i] = Math.Clamp(positions[i], min + sizes[i] / 2, max - sizes[i] / 2);
        }

        // Backward pass after clamping: push left/up from right edge
        for (int i = positions.Length - 2; i >= 0; i--) {
            double nextStart = positions[i + 1] - sizes[i + 1] / 2 - gap;
            double currEnd = positions[i] + sizes[i] / 2;
            if (currEnd > nextStart) {
                positions[i] = nextStart - sizes[i] / 2;
            }
        }

        // Forward pass: push right/down from left edge
        for (int i = 1; i < positions.Length; i++) {
            double prevEnd = positions[i - 1] + sizes[i - 1] / 2 + gap;
            double currStart = positions[i] - sizes[i] / 2;
            if (currStart < prevEnd) {
                positions[i] = prevEnd + sizes[i] / 2;
            }
        }

        // Final clamp
        for (int i = 0; i < positions.Length; i++) {
            positions[i] = Math.Clamp(positions[i], min + sizes[i] / 2, max - sizes[i] / 2);
        }
    }

    // --- Pool management ---

    void BeginRender() {
        _nextRect = 0;
        _nextText = 0;
        _nextLine = 0;

        if (_rectPool.Count > 0 && _rectPool[0].Parent is null) {
            _rectPool.Clear();
            _textPool.Clear();
            _linePool.Clear();
        }
    }

    void EndRender() {
        for (int i = _nextRect; i < _rectPool.Count; i++) {
            _rectPool[i].Visibility = Visibility.Collapsed;
        }

        for (int i = _nextText; i < _textPool.Count; i++) {
            _textPool[i].Outline.Visibility = Visibility.Collapsed;
            _textPool[i].Fill.Visibility = Visibility.Collapsed;
        }

        for (int i = _nextLine; i < _linePool.Count; i++) {
            _linePool[i].Visibility = Visibility.Collapsed;
        }
    }

    void UseRect(Rect dipRect, Brush fill, Brush stroke, double strokeThickness) {
        Rectangle rect;
        if (_nextRect < _rectPool.Count) {
            rect = _rectPool[_nextRect];
            rect.Visibility = Visibility.Visible;
        } else {
            rect = new Rectangle { IsHitTestVisible = false };
            _canvas.Children.Add(rect);
            _rectPool.Add(rect);
        }

        rect.Width = dipRect.Width;
        rect.Height = dipRect.Height;
        rect.Fill = fill;
        rect.Stroke = stroke;
        rect.StrokeThickness = strokeThickness;
        Canvas.SetLeft(rect, dipRect.X);
        Canvas.SetTop(rect, dipRect.Y);
        _nextRect++;
    }

    void UseLabel(Rect area, string text, double fontSize, Brush foreground, double opacity) {
        var ft = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            _typeface, fontSize, foreground,
            VisualTreeHelper.GetDpi(_canvas).PixelsPerDip);
        var geometry = ft.BuildGeometry(new Point(0, 0));
        var bounds = geometry.Bounds;

        double offsetX = area.X + (area.Width - bounds.Width) / 2 - bounds.X;
        double offsetY = area.Y + (area.Height - bounds.Height) / 2 - bounds.Y;

        Path outline, fill;
        if (_nextText < _textPool.Count) {
            (outline, fill) = _textPool[_nextText];
            outline.Visibility = Visibility.Visible;
            fill.Visibility = Visibility.Visible;
        } else {
            outline = new Path { IsHitTestVisible = false };
            fill = new Path { IsHitTestVisible = false };
            _canvas.Children.Add(outline);
            _canvas.Children.Add(fill);
            _textPool.Add((outline, fill));
        }

        double outlineThick = Math.Max(_theme.LabelOutlineThickness * 2, fontSize * 0.08);

        outline.Data = geometry;
        outline.Fill = Brushes.Transparent;
        outline.Stroke = _outlineBrush;
        outline.StrokeThickness = outlineThick;
        outline.StrokeLineJoin = PenLineJoin.Round;
        outline.Opacity = opacity;
        Canvas.SetLeft(outline, offsetX);
        Canvas.SetTop(outline, offsetY);

        fill.Data = geometry;
        fill.Fill = foreground;
        fill.Stroke = null;
        fill.Opacity = opacity;
        Canvas.SetLeft(fill, offsetX);
        Canvas.SetTop(fill, offsetY);

        _nextText++;
    }

    void UseLine(double x1, double y1, double x2, double y2) {
        Line line;
        if (_nextLine < _linePool.Count) {
            line = _linePool[_nextLine];
            line.Visibility = Visibility.Visible;
        } else {
            line = new Line {
                Stroke = _connectorBrush,
                StrokeThickness = _theme.ConnectorLineThickness,
                StrokeDashArray = [2, 2],
                IsHitTestVisible = false,
            };
            _canvas.Children.Add(line);
            _linePool.Add(line);
        }

        line.X1 = x1;
        line.Y1 = y1;
        line.X2 = x2;
        line.Y2 = y2;
        _nextLine++;
    }

    // --- Helpers ---

    void EnsureTransform() {
        if (_transformInitialized) {
            return;
        }

        var source = PresentationSource.FromVisual(_canvas);
        if (source?.CompositionTarget != null) {
            _transformFromDevice = source.CompositionTarget.TransformFromDevice;
            _transformInitialized = true;
        }
    }

    Rect DipRect(GridCell cell) {
        var tl = _transformFromDevice.Transform(new Point(cell.Bounds.X, cell.Bounds.Y));
        var br = _transformFromDevice.Transform(new Point(cell.Bounds.Right, cell.Bounds.Bottom));
        return new Rect(tl, br);
    }

    Point DipPoint(double physX, double physY) =>
        _transformFromDevice.Transform(new Point(physX, physY));

    bool IsSmallCell(Rect dipRect) =>
        dipRect.Height < _minLabelFontSize * 1.8 || dipRect.Width / 2 < _minLabelFontSize * 1.6;

    bool IsNarrowColumn(Rect dipRect) =>
        dipRect.Width / 2 < _minLabelFontSize * 1.6;

    bool IsShortRow(Rect dipRect) =>
        dipRect.Height < _minLabelFontSize * 1.8;

    double ComputeInlineFontSize(Rect dipRect) {
        double halfWidth = dipRect.Width / 2;
        double fontFromHeight = dipRect.Height * 0.8 / 1.2;
        double fontFromWidth = halfWidth * 0.95 / 0.55;
        double fontSize = Math.Min(fontFromHeight, fontFromWidth);
        return Math.Clamp(fontSize, _minLabelFontSize, _theme.LabelFontSize * 3);
    }

    double ExternalFontSize() =>
        Math.Max(_minLabelFontSize, _theme.LabelFontSize * 0.85);

    Size MeasureText(string text, double fontSize) {
        var ft = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            _typeface, fontSize, Brushes.Black,
            VisualTreeHelper.GetDpi(_canvas).PixelsPerDip);
        return new Size(ft.Width, ft.Height);
    }

    static SolidColorBrush BrushFromHex(string hex, double opacity = 1.0) {
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

    static FontWeight ParseFontWeight(string weight) =>
        weight.ToLowerInvariant() switch {
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
