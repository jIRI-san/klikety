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
/// Renders log-crosshair-mode grids on the overlay Canvas.
/// Cross cells (center row + center column) get borders + single-char labels.
/// Non-cross cells are dimmed at 15% opacity. Degenerate cells are skipped.
/// Uses element pooling: shapes are mutated (not recreated) across re-renders.
/// Staleness detected via <c>Parent == null</c> after external canvas clear.
/// </summary>
public sealed class LogCrosshairRenderer : ILogCrosshairRenderer {
    readonly Canvas _canvas;
    readonly ThemeModel _theme;
    readonly AxisLabelGenerator _horizLabels;
    readonly AxisLabelGenerator _vertLabels;
    readonly double _minLabelFontSize;

    Matrix _transformFromDevice = Matrix.Identity;
    bool _transformInitialized;

    // Cached brushes
    readonly SolidColorBrush _cellBorderBrush;
    readonly SolidColorBrush _cellBgBrush;
    readonly SolidColorBrush _labelBrush;
    readonly SolidColorBrush _dimBrush;
    readonly SolidColorBrush _highlightBg;
    readonly SolidColorBrush _highlightBorder;
    readonly SolidColorBrush _crossBgBrush;
    readonly SolidColorBrush _outlineBrush;
    readonly SolidColorBrush _extLabelBrush;
    readonly SolidColorBrush _extRowLabelBrush;
    readonly SolidColorBrush _connectorBrush;
    readonly Typeface _typeface;

    // Element pools — reused across renders to reduce GC pressure
    readonly List<Rectangle> _rectPool = [];
    readonly List<(Path Outline, Path Fill)> _textPool = [];
    readonly List<Line> _linePool = [];
    int _nextRect;
    int _nextText;
    int _nextLine;

    // Pooled flash overlay (single instance, reused)
    Rectangle? _flashRect;

    public LogCrosshairRenderer(
        Canvas canvas, ThemeModel theme,
        AxisLabelGenerator horizLabels, AxisLabelGenerator vertLabels,
        double minLabelFontSize) {
        _canvas = canvas;
        _theme = theme;
        _horizLabels = horizLabels;
        _vertLabels = vertLabels;
        _minLabelFontSize = minLabelFontSize;

        _cellBorderBrush = BrushFromHex(theme.CellBorderColor);
        _cellBgBrush = BrushFromHex(theme.CellBackgroundColor, theme.CellBackgroundOpacity);
        _labelBrush = BrushFromHex(theme.LabelColor);
        _dimBrush = BrushFromHex(theme.DimmedOverlayColor, 0.15);
        _highlightBg = BrushFromHex(theme.HighlightedColumnBackground, 0.5);
        _highlightBorder = BrushFromHex(theme.HighlightedColumnBorderColor);
        _crossBgBrush = BrushFromHex(theme.HighlightedColumnBackground, 0.25);
        _outlineBrush = BrushFromHex(theme.LabelOutlineColor);
        _extLabelBrush = BrushFromHex(theme.ExternalLabelColor);
        _extRowLabelBrush = BrushFromHex(theme.ExternalRowLabelColor);
        _connectorBrush = BrushFromHex(theme.ConnectorLineColor);

        var fontFamily = new FontFamily(theme.LabelFontFamily);
        var fontWeight = ParseFontWeight(theme.LabelFontWeight);
        _typeface = new Typeface(fontFamily, FontStyles.Normal, fontWeight, FontStretches.Normal);
    }

    public void SetTransform(Matrix transformFromDevice) {
        _transformFromDevice = transformFromDevice;
        _transformInitialized = true;
    }

    public void RenderCross(LogCrosshairGrid grid) {
        BeginRender();
        EnsureTransform();

        for (int row = 0; row < grid.Rows; row++) {
            for (int col = 0; col < grid.Cols; col++) {
                if (grid.IsDegenerate(row, col)) {
                    continue;
                }

                var dipRect = DipRect(grid.CellAt(row, col));
                bool onCross = grid.IsOnCross(row, col);

                if (onCross) {
                    UseRect(dipRect, _cellBgBrush, _cellBorderBrush, ScaledBorderThickness(dipRect));
                    bool isCenter = row == grid.CenterRow && col == grid.CenterCol;
                    if (isCenter || !IsSmallCell(dipRect, _minLabelFontSize)) {
                        UseCrossLabel(dipRect, row, col, grid, _labelBrush);
                    }
                } else {
                    UseRect(dipRect, _dimBrush, Brushes.Transparent, 0);
                }
            }
        }

        RenderExternalColumnLabels(grid);
        RenderExternalRowLabels(grid);
        EndRender();
    }

    public void HighlightColumn(LogCrosshairGrid grid, int col) {
        BeginRender();
        EnsureTransform();

        for (int row = 0; row < grid.Rows; row++) {
            for (int c = 0; c < grid.Cols; c++) {
                if (grid.IsDegenerate(row, c)) {
                    continue;
                }

                var dipRect = DipRect(grid.CellAt(row, c));
                bool onCross = grid.IsOnCross(row, c);
                bool isHighlightCol = c == col;
                bool isCenter = row == grid.CenterRow && c == grid.CenterCol;
                bool skipInline = !isCenter && IsSmallCell(dipRect, _minLabelFontSize);

                if (isHighlightCol && row == grid.CenterRow) {
                    UseRect(dipRect, _highlightBg, _highlightBorder, ScaledBorderThickness(dipRect) * 2);
                    if (!skipInline) UseCrossLabel(dipRect, row, c, grid, _labelBrush);
                } else if (isHighlightCol && onCross) {
                    UseRect(dipRect, _crossBgBrush, _highlightBorder, ScaledBorderThickness(dipRect));
                    if (!skipInline) UseCrossLabel(dipRect, row, c, grid, _labelBrush);
                } else if (onCross) {
                    UseRect(dipRect, _cellBgBrush, _cellBorderBrush, ScaledBorderThickness(dipRect));
                    if (!skipInline) UseCrossLabel(dipRect, row, c, grid, _labelBrush, 0.4);
                } else {
                    UseRect(dipRect, _dimBrush, Brushes.Transparent, 0);
                }
            }
        }

        RenderExternalColumnLabels(grid, highlightCol: col, defaultOpacity: 0.4, highlightOpacity: 1.0);
        RenderExternalRowLabels(grid);
        EndRender();
    }

    public void HighlightRow(LogCrosshairGrid grid, int row) {
        BeginRender();
        EnsureTransform();

        for (int r = 0; r < grid.Rows; r++) {
            for (int col = 0; col < grid.Cols; col++) {
                if (grid.IsDegenerate(r, col)) {
                    continue;
                }

                var dipRect = DipRect(grid.CellAt(r, col));
                bool onCross = grid.IsOnCross(r, col);
                bool isHighlightRow = r == row;
                bool isCenter = r == grid.CenterRow && col == grid.CenterCol;
                bool skipInline = !isCenter && IsSmallCell(dipRect, _minLabelFontSize);

                if (isHighlightRow && col == grid.CenterCol) {
                    UseRect(dipRect, _highlightBg, _highlightBorder, ScaledBorderThickness(dipRect) * 2);
                    if (!skipInline) UseCrossLabel(dipRect, r, col, grid, _labelBrush);
                } else if (isHighlightRow && onCross) {
                    UseRect(dipRect, _crossBgBrush, _highlightBorder, ScaledBorderThickness(dipRect));
                    if (!skipInline) UseCrossLabel(dipRect, r, col, grid, _labelBrush);
                } else if (onCross) {
                    UseRect(dipRect, _cellBgBrush, _cellBorderBrush, ScaledBorderThickness(dipRect));
                    if (!skipInline) UseCrossLabel(dipRect, r, col, grid, _labelBrush, 0.4);
                } else {
                    UseRect(dipRect, _dimBrush, Brushes.Transparent, 0);
                }
            }
        }

        RenderExternalColumnLabels(grid);
        RenderExternalRowLabels(grid, highlightRow: row, defaultOpacity: 0.4, highlightOpacity: 1.0);
        EndRender();
    }

    public void HighlightCell(LogCrosshairGrid grid, GridCell cell) {
        BeginRender();
        EnsureTransform();

        for (int r = 0; r < grid.Rows; r++) {
            for (int c = 0; c < grid.Cols; c++) {
                if (grid.IsDegenerate(r, c)) {
                    continue;
                }

                var dipRect = DipRect(grid.CellAt(r, c));
                bool onCross = grid.IsOnCross(r, c);
                bool isTarget = r == cell.Row && c == cell.Col;
                bool onTargetCross = r == cell.Row || c == cell.Col;
                bool isCenter = r == grid.CenterRow && c == grid.CenterCol;
                bool skipInline = !isCenter && IsSmallCell(dipRect, _minLabelFontSize);

                if (isTarget) {
                    UseRect(dipRect, _highlightBg, _highlightBorder, ScaledBorderThickness(dipRect) * 2);
                    if (!skipInline) UseCrossLabel(dipRect, r, c, grid, _labelBrush);
                } else if (onTargetCross && onCross) {
                    UseRect(dipRect, _crossBgBrush, _highlightBorder, ScaledBorderThickness(dipRect));
                    if (!skipInline) UseCrossLabel(dipRect, r, c, grid, _labelBrush, 0.6);
                } else if (onCross) {
                    UseRect(dipRect, _cellBgBrush, _cellBorderBrush, ScaledBorderThickness(dipRect));
                    if (!skipInline) UseCrossLabel(dipRect, r, c, grid, _labelBrush, 0.3);
                } else {
                    UseRect(dipRect, _dimBrush, Brushes.Transparent, 0);
                }
            }
        }

        RenderExternalColumnLabels(grid, highlightCol: cell.Col, defaultOpacity: 0.3, highlightOpacity: 1.0);
        RenderExternalRowLabels(grid, highlightRow: cell.Row, defaultOpacity: 0.3, highlightOpacity: 1.0);
        EndRender();
    }

    void RenderExternalColumnLabels(LogCrosshairGrid grid, int highlightCol = -1,
        double defaultOpacity = 1.0, double highlightOpacity = 1.0) {
        // Scan center row for narrow columns (skip degenerate, zero-width, and CenterCol)
        var externalCols = new List<int>();
        for (int col = 0; col < grid.Cols; col++) {
            if (col == grid.CenterCol) continue;
            if (grid.IsDegenerate(grid.CenterRow, col)) continue;
            var dipRect = DipRect(grid.CellAt(grid.CenterRow, col));
            if (dipRect.Width <= 0) continue;
            if (IsNarrowColumn(dipRect, _minLabelFontSize)) {
                externalCols.Add(col);
            }
        }

        if (externalCols.Count == 0) return;

        double fontSize = ExternalFontSize();

        // Compute bounding box (top/bottom) of narrow cells on center row
        double bboxTop = double.MaxValue;
        double bboxBottom = double.MinValue;
        foreach (int col in externalCols) {
            var dipRect = DipRect(grid.CellAt(grid.CenterRow, col));
            bboxTop = Math.Min(bboxTop, dipRect.Y);
            bboxBottom = Math.Max(bboxBottom, dipRect.Y + dipRect.Height);
        }

        // Compute label positions (x = column center) and sizes
        var labelPositions = new double[externalCols.Count];
        var labelWidths = new double[externalCols.Count];
        for (int i = 0; i < externalCols.Count; i++) {
            int col = externalCols[i];
            string? label = GetCrossLabel(grid.CenterRow, col, grid);
            labelWidths[i] = label is not null ? MeasureText(label, fontSize).Width : 0;
            var dipRect = DipRect(grid.CellAt(grid.CenterRow, col));
            labelPositions[i] = dipRect.X + dipRect.Width / 2;
        }

        // Resolve overlaps using anchor center (grid center column x)
        var centerDip = DipRect(grid.CellAt(grid.CenterRow, grid.CenterCol));
        double anchorCenterX = centerDip.X + centerDip.Width / 2;
        double screenWidth = Math.Max(_canvas.ActualWidth, 1);
        LogGridRenderer.ResolveOverlaps(labelPositions, labelWidths, 0, screenWidth, anchorCenterX);

        double labelHeight = MeasureText("X", fontSize).Height;
        double labelMargin = fontSize * 0.5;

        // Screen-edge check (after resolution): determine which sides have room
        bool showAbove = bboxTop >= labelHeight + labelMargin * 2;
        bool showBelow = (_canvas.ActualHeight - bboxBottom) >= labelHeight + labelMargin * 2;
        if (!showAbove && !showBelow) { showAbove = true; } // fallback: at least one side

        for (int i = 0; i < externalCols.Count; i++) {
            int col = externalCols[i];
            string? label = GetCrossLabel(grid.CenterRow, col, grid);
            if (label is null) continue;

            double opacity = col == highlightCol ? highlightOpacity : defaultOpacity;
            var dipRect = DipRect(grid.CellAt(grid.CenterRow, col));
            double anchorX = dipRect.X + dipRect.Width / 2;
            double labelCenterX = labelPositions[i];
            var labelSize = MeasureText(label, fontSize);

            if (showAbove) {
                double topLabelY = bboxTop - labelMargin - labelSize.Height;
                UseExternalLabel(new Rect(labelCenterX - labelSize.Width / 2, topLabelY, labelSize.Width, labelSize.Height),
                    label, fontSize, _extLabelBrush, opacity);
                UseLine(anchorX, bboxTop, labelCenterX, topLabelY + labelSize.Height + 2, opacity);
            }

            if (showBelow) {
                double bottomLabelY = bboxBottom + labelMargin;
                UseExternalLabel(new Rect(labelCenterX - labelSize.Width / 2, bottomLabelY, labelSize.Width, labelSize.Height),
                    label, fontSize, _extLabelBrush, opacity);
                UseLine(anchorX, bboxBottom, labelCenterX, bottomLabelY - 2, opacity);
            }
        }
    }

    void RenderExternalRowLabels(LogCrosshairGrid grid, int highlightRow = -1,
        double defaultOpacity = 1.0, double highlightOpacity = 1.0) {
        // Scan center column for short rows (skip degenerate, zero-height, and CenterRow)
        var externalRows = new List<int>();
        for (int row = 0; row < grid.Rows; row++) {
            if (row == grid.CenterRow) continue;
            if (grid.IsDegenerate(row, grid.CenterCol)) continue;
            var dipRect = DipRect(grid.CellAt(row, grid.CenterCol));
            if (dipRect.Height <= 0) continue;
            if (IsShortRow(dipRect, _minLabelFontSize)) {
                externalRows.Add(row);
            }
        }

        if (externalRows.Count == 0) return;

        double fontSize = ExternalFontSize();

        // Compute bounding box (left/right) of short cells on center column
        double bboxLeft = double.MaxValue;
        double bboxRight = double.MinValue;
        foreach (int row in externalRows) {
            var dipRect = DipRect(grid.CellAt(row, grid.CenterCol));
            bboxLeft = Math.Min(bboxLeft, dipRect.X);
            bboxRight = Math.Max(bboxRight, dipRect.X + dipRect.Width);
        }

        // Compute label positions (y = row center) and sizes
        var labelPositions = new double[externalRows.Count];
        var labelHeights = new double[externalRows.Count];
        for (int i = 0; i < externalRows.Count; i++) {
            int row = externalRows[i];
            string? label = GetCrossLabel(row, grid.CenterCol, grid);
            labelHeights[i] = label is not null ? MeasureText(label, fontSize).Height : 0;
            var dipRect = DipRect(grid.CellAt(row, grid.CenterCol));
            labelPositions[i] = dipRect.Y + dipRect.Height / 2;
        }

        // Resolve overlaps using anchor center (grid center row y)
        var centerDip = DipRect(grid.CellAt(grid.CenterRow, grid.CenterCol));
        double anchorCenterY = centerDip.Y + centerDip.Height / 2;
        double screenHeight = Math.Max(_canvas.ActualHeight, 1);
        LogGridRenderer.ResolveOverlaps(labelPositions, labelHeights, 0, screenHeight, anchorCenterY);

        double labelWidth = MeasureText("W", fontSize).Width;
        double labelMargin = fontSize * 0.5;

        // Screen-edge check (after resolution): determine which sides have room
        bool showLeft = bboxLeft >= labelWidth + labelMargin * 2;
        bool showRight = (_canvas.ActualWidth - bboxRight) >= labelWidth + labelMargin * 2;
        if (!showLeft && !showRight) { showLeft = true; } // fallback: at least one side

        for (int i = 0; i < externalRows.Count; i++) {
            int row = externalRows[i];
            string? label = GetCrossLabel(row, grid.CenterCol, grid);
            if (label is null) continue;

            double opacity = row == highlightRow ? highlightOpacity : defaultOpacity;
            var dipRect = DipRect(grid.CellAt(row, grid.CenterCol));
            double anchorY = dipRect.Y + dipRect.Height / 2;
            double labelCenterY = labelPositions[i];
            var labelSize = MeasureText(label, fontSize);

            if (showLeft) {
                double leftLabelX = bboxLeft - labelMargin - labelSize.Width;
                UseExternalLabel(new Rect(leftLabelX, labelCenterY - labelSize.Height / 2, labelSize.Width, labelSize.Height),
                    label, fontSize, _extRowLabelBrush, opacity);
                UseLine(bboxLeft, anchorY, leftLabelX + labelSize.Width + 2, labelCenterY, opacity);
            }

            if (showRight) {
                double rightLabelX = bboxRight + labelMargin;
                UseExternalLabel(new Rect(rightLabelX, labelCenterY - labelSize.Height / 2, labelSize.Width, labelSize.Height),
                    label, fontSize, _extRowLabelBrush, opacity);
                UseLine(bboxRight, anchorY, rightLabelX - 2, labelCenterY, opacity);
            }
        }
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

        // Stop any in-flight animation before starting a new one
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

    // --- Pool management ---

    void BeginRender() {
        _nextRect = 0;
        _nextText = 0;
        _nextLine = 0;

        // Detect external canvas clear (all pooled elements removed)
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

    void UseRect(Rect dipRect, Brush fill, Brush stroke, double strokeThickness = -1) {
        if (strokeThickness < 0) {
            strokeThickness = _theme.CellBorderThickness;
        }

        Rectangle rect;
        if (_nextRect < _rectPool.Count) {
            rect = _rectPool[_nextRect];
            rect.Visibility = Visibility.Visible;
        } else {
            rect = new Rectangle();
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

    void UseCrossLabel(Rect dipRect, int row, int col, LogCrosshairGrid grid,
        Brush foreground, double opacity = 1.0) {
        string? label = GetCrossLabel(row, col, grid);
        if (label is null) {
            return;
        }

        double fontSize = ComputeGradualFontSize(dipRect, row, col, grid);
        var ft = new FormattedText(label, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, _typeface, fontSize, foreground,
            VisualTreeHelper.GetDpi(_canvas).PixelsPerDip);
        var geometry = ft.BuildGeometry(new System.Windows.Point(0, 0));
        var bounds = geometry.Bounds;

        double offsetX = dipRect.X + (dipRect.Width - bounds.Width) / 2 - bounds.X;
        double offsetY = dipRect.Y + (dipRect.Height - bounds.Height) / 2 - bounds.Y;

        Path outline, fill;
        if (_nextText < _textPool.Count) {
            (outline, fill) = _textPool[_nextText];
            outline.Visibility = Visibility.Visible;
            fill.Visibility = Visibility.Visible;
        } else {
            outline = new Path();
            fill = new Path();
            _canvas.Children.Add(outline);
            _canvas.Children.Add(fill);
            _textPool.Add((outline, fill));
        }

        outline.Data = geometry;
        outline.Fill = Brushes.Transparent;
        outline.Stroke = _outlineBrush;
        outline.StrokeThickness = Math.Max(_theme.LabelOutlineThickness * 2, fontSize * 0.08);
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

    void UseLine(double x1, double y1, double x2, double y2, double opacity = 1.0) {
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
        line.Opacity = opacity;
        _nextLine++;
    }

    void UseExternalLabel(Rect area, string text, double fontSize, Brush foreground, double opacity) {
        var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, _typeface, fontSize, foreground,
            VisualTreeHelper.GetDpi(_canvas).PixelsPerDip);
        var geometry = ft.BuildGeometry(new System.Windows.Point(0, 0));
        var bounds = geometry.Bounds;

        double offsetX = area.X - bounds.X;
        double offsetY = area.Y - bounds.Y;

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

        outline.Data = geometry;
        outline.Fill = Brushes.Transparent;
        outline.Stroke = _outlineBrush;
        outline.StrokeThickness = Math.Max(_theme.LabelOutlineThickness * 2, fontSize * 0.08);
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

    Size MeasureText(string text, double fontSize) {
        var ft = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            _typeface, fontSize, Brushes.Black,
            VisualTreeHelper.GetDpi(_canvas).PixelsPerDip);
        return new Size(ft.Width, ft.Height);
    }

    double ExternalFontSize() =>
        Math.Max(_minLabelFontSize, _theme.LabelFontSize * 0.85);

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
        var tl = _transformFromDevice.Transform(new System.Windows.Point(cell.Bounds.X, cell.Bounds.Y));
        var br = _transformFromDevice.Transform(new System.Windows.Point(
            cell.Bounds.Right, cell.Bounds.Bottom));
        return new Rect(tl, br);
    }

    /// <summary>
    /// Computes scaled border thickness based on cell size.
    /// Larger cells get proportionally thicker borders.
    /// </summary>
    double ScaledBorderThickness(Rect dipRect) {
        double extent = Math.Min(dipRect.Width, dipRect.Height);
        double scaled = extent * 0.02 + _theme.CellBorderThickness * 0.5;
        return Math.Clamp(scaled, _theme.CellBorderThickness, _theme.CellBorderThickness * 4);
    }

    string? GetCrossLabel(int row, int col, LogCrosshairGrid grid) {
        bool onCenterRow = row == grid.CenterRow;
        bool onCenterCol = col == grid.CenterCol;

        if (onCenterRow && onCenterCol) {
            return "\u2022";
        }

        if (onCenterRow) {
            int keyIndex = col < grid.CenterCol ? col : col - 1;
            return keyIndex >= 0 && keyIndex < _horizLabels.Count
                ? _horizLabels.LabelFor(keyIndex) : null;
        }

        if (onCenterCol) {
            int keyIndex = row < grid.CenterRow ? row : row - 1;
            return keyIndex >= 0 && keyIndex < _vertLabels.Count
                ? _vertLabels.LabelFor(keyIndex) : null;
        }

        return null;
    }

    double ComputeGradualFontSize(Rect dipRect, int row, int col, LogCrosshairGrid grid) {
        double baseFontSize = _theme.LabelFontSize > 0 ? _theme.LabelFontSize : 14.0;

        return ComputeGradualFontSize(dipRect, baseFontSize);
    }

    /// <summary>
    /// Font size scales directly with cell extent — log grid cells already encode distance
    /// from center via their size (larger = farther). Use 70% of cell extent, clamped.
    /// </summary>
    internal static double ComputeGradualFontSize(Rect dipRect, double baseFontSize) {
        double cellExtent = Math.Min(dipRect.Width, dipRect.Height);
        double fontSize = cellExtent * 0.7;

        if (fontSize < 1.0) {
            return 1.0;
        }

        return fontSize;
    }

    internal static bool IsSmallCell(Rect dipRect, double minLabelFontSize) =>
        dipRect.Height < minLabelFontSize * 1.8 || dipRect.Width / 2 < minLabelFontSize * 1.6;

    internal static bool IsNarrowColumn(Rect dipRect, double minLabelFontSize) =>
        dipRect.Width / 2 < minLabelFontSize * 1.6;

    internal static bool IsShortRow(Rect dipRect, double minLabelFontSize) =>
        dipRect.Height < minLabelFontSize * 1.8;

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
