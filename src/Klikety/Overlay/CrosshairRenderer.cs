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
/// Renders crosshair-mode grids on the overlay Canvas.
/// Cross cells (center row + center column) get borders + single-char labels.
/// Non-cross cells are dimmed at 15% opacity.
/// </summary>
public sealed class CrosshairRenderer : ICrosshairRenderer {
    private readonly Canvas _canvas;
    private readonly ThemeModel _theme;
    private readonly AxisLabelGenerator _horizLabels;
    private readonly AxisLabelGenerator _vertLabels;

    private Matrix _transformFromDevice = Matrix.Identity;
    private bool _transformInitialized;

    // Cached brushes
    private readonly SolidColorBrush _cellBorderBrush;
    private readonly SolidColorBrush _cellBgBrush;
    private readonly SolidColorBrush _labelBrush;
    private readonly SolidColorBrush _dimBrush;
    private readonly SolidColorBrush _highlightBg;
    private readonly SolidColorBrush _highlightBorder;
    private readonly SolidColorBrush _crossBgBrush;
    private readonly SolidColorBrush _outlineBrush;
    private readonly Typeface _typeface;

    public CrosshairRenderer(
        Canvas canvas, ThemeModel theme,
        AxisLabelGenerator horizLabels, AxisLabelGenerator vertLabels) {
        _canvas = canvas;
        _theme = theme;
        _horizLabels = horizLabels;
        _vertLabels = vertLabels;

        _cellBorderBrush = BrushFromHex(theme.CellBorderColor);
        _cellBgBrush = BrushFromHex(theme.CellBackgroundColor, theme.CellBackgroundOpacity);
        _labelBrush = BrushFromHex(theme.LabelColor);
        _dimBrush = BrushFromHex(theme.DimmedOverlayColor, 0.15);
        _highlightBg = BrushFromHex(theme.HighlightedColumnBackground, 0.5);
        _highlightBorder = BrushFromHex(theme.HighlightedColumnBorderColor);
        _crossBgBrush = BrushFromHex(theme.HighlightedColumnBackground, 0.25);
        _outlineBrush = BrushFromHex(theme.LabelOutlineColor);

        var fontFamily = new FontFamily(theme.LabelFontFamily);
        var fontWeight = ParseFontWeight(theme.LabelFontWeight);
        _typeface = new Typeface(fontFamily, FontStyles.Normal, fontWeight, FontStretches.Normal);
    }

    public void SetTransform(Matrix transformFromDevice) {
        _transformFromDevice = transformFromDevice;
        _transformInitialized = true;
    }

    /// <summary>
    /// Renders the initial crosshair: cross cells with labels, non-cross cells dimmed.
    /// </summary>
    public void RenderCross(CrosshairGrid grid) {
        _canvas.Children.Clear();
        EnsureTransform();

        var region = ComputeRegion(grid);

        for (int row = 0; row < grid.Rows; row++) {
            for (int col = 0; col < grid.Cols; col++) {
                var dipRect = DipRectForCell(row, col, region, grid.Cols, grid.Rows);
                bool onCross = grid.IsOnCross(row, col);

                if (onCross) {
                    AddCellRect(dipRect, _cellBgBrush, _cellBorderBrush);
                    AddCrossLabel(dipRect, row, col, grid, _labelBrush);
                } else {
                    AddCellRect(dipRect, _dimBrush, Brushes.Transparent, 0);
                }
            }
        }
    }

    /// <summary>
    /// Highlights a specific column — vertical labels render at selected column.
    /// </summary>
    public void HighlightColumn(CrosshairGrid grid, int col) {
        _canvas.Children.Clear();
        EnsureTransform();

        var region = ComputeRegion(grid);
        int crossRow = grid.CenterRow;
        int crossCol = col;

        for (int r = 0; r < grid.Rows; r++) {
            for (int c = 0; c < grid.Cols; c++) {
                var dipRect = DipRectForCell(r, c, region, grid.Cols, grid.Rows);
                bool onShiftedCross = r == crossRow || c == crossCol;
                bool isIntersection = r == crossRow && c == crossCol;

                if (isIntersection) {
                    AddCellRect(dipRect, _highlightBg, _highlightBorder, _theme.CellBorderThickness * 2);
                    AddShiftedLabel(dipRect, r, c, crossRow, crossCol, grid, _labelBrush);
                } else if (onShiftedCross) {
                    AddCellRect(dipRect, _crossBgBrush, _cellBorderBrush);
                    AddShiftedLabel(dipRect, r, c, crossRow, crossCol, grid, _labelBrush, 0.7);
                } else {
                    AddCellRect(dipRect, _dimBrush, Brushes.Transparent, 0);
                }
            }
        }
    }

    /// <summary>
    /// Highlights a specific row — horizontal labels render at selected row.
    /// </summary>
    public void HighlightRow(CrosshairGrid grid, int row) {
        _canvas.Children.Clear();
        EnsureTransform();

        var region = ComputeRegion(grid);
        int crossRow = row;
        int crossCol = grid.CenterCol;

        for (int r = 0; r < grid.Rows; r++) {
            for (int c = 0; c < grid.Cols; c++) {
                var dipRect = DipRectForCell(r, c, region, grid.Cols, grid.Rows);
                bool onShiftedCross = r == crossRow || c == crossCol;
                bool isIntersection = r == crossRow && c == crossCol;

                if (isIntersection) {
                    AddCellRect(dipRect, _highlightBg, _highlightBorder, _theme.CellBorderThickness * 2);
                    AddShiftedLabel(dipRect, r, c, crossRow, crossCol, grid, _labelBrush);
                } else if (onShiftedCross) {
                    AddCellRect(dipRect, _crossBgBrush, _cellBorderBrush);
                    AddShiftedLabel(dipRect, r, c, crossRow, crossCol, grid, _labelBrush, 0.7);
                } else {
                    AddCellRect(dipRect, _dimBrush, Brushes.Transparent, 0);
                }
            }
        }
    }

    /// <summary>
    /// Highlights a cell — cross rendered at cell's row and column.
    /// </summary>
    public void HighlightCell(CrosshairGrid grid, GridCell cell) {
        _canvas.Children.Clear();
        EnsureTransform();

        var region = ComputeRegion(grid);
        int crossRow = cell.Row;
        int crossCol = cell.Col;

        for (int r = 0; r < grid.Rows; r++) {
            for (int c = 0; c < grid.Cols; c++) {
                var dipRect = DipRectForCell(r, c, region, grid.Cols, grid.Rows);
                bool onShiftedCross = r == crossRow || c == crossCol;
                bool isIntersection = r == crossRow && c == crossCol;

                if (isIntersection) {
                    AddCellRect(dipRect, _highlightBg, _highlightBorder, _theme.CellBorderThickness * 2);
                    AddShiftedLabel(dipRect, r, c, crossRow, crossCol, grid, _labelBrush);
                } else if (onShiftedCross) {
                    AddCellRect(dipRect, _crossBgBrush, _cellBorderBrush);
                    AddShiftedLabel(dipRect, r, c, crossRow, crossCol, grid, _labelBrush, 0.6);
                } else {
                    AddCellRect(dipRect, _dimBrush, Brushes.Transparent, 0);
                }
            }
        }
    }

    /// <summary>
    /// Renders a subgrid cross within a parent cell, with the parent grid dimmed.
    /// </summary>
    public void RenderSubgridCross(CrosshairGrid parentGrid, CrosshairGrid subgrid, GridCell parentCell) {
        _canvas.Children.Clear();
        EnsureTransform();

        // Render parent grid dimmed
        var parentRegion = ComputeRegion(parentGrid);
        for (int r = 0; r < parentGrid.Rows; r++) {
            for (int c = 0; c < parentGrid.Cols; c++) {
                var dipRect = DipRectForCell(r, c, parentRegion, parentGrid.Cols, parentGrid.Rows);
                bool isParentCell = r == parentCell.Row && c == parentCell.Col;
                if (!isParentCell) {
                    AddCellRect(dipRect, _dimBrush, Brushes.Transparent, 0);
                }
            }
        }

        // Render subgrid cross
        var subRegion = TransformBounds(parentCell.Bounds);
        for (int r = 0; r < subgrid.Rows; r++) {
            for (int c = 0; c < subgrid.Cols; c++) {
                var dipRect = DipRectForCell(r, c, subRegion, subgrid.Cols, subgrid.Rows);
                bool onCross = subgrid.IsOnCross(r, c);

                if (onCross) {
                    AddCellRect(dipRect, _cellBgBrush, _cellBorderBrush);
                    AddCrossLabel(dipRect, r, c, subgrid, _labelBrush, heightFraction: 0.9);
                } else {
                    AddCellRect(dipRect, _dimBrush, Brushes.Transparent, 0);
                }
            }
        }
    }

    /// <summary>
    /// Brief red flash to indicate an invalid key press.
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

    // --- Private helpers ---

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

    private Rect ComputeRegion(CrosshairGrid grid) {
        var first = grid.Cells[0].Bounds;
        var last = grid.Cells[^1].Bounds;
        var topLeft = _transformFromDevice.Transform(new Point(first.X, first.Y));
        var bottomRight = _transformFromDevice.Transform(new Point(
            last.X + last.Width, last.Y + last.Height));
        return new Rect(topLeft, bottomRight);
    }

    private Rect TransformBounds(System.Drawing.Rectangle bounds) {
        var topLeft = _transformFromDevice.Transform(new Point(bounds.X, bounds.Y));
        var bottomRight = _transformFromDevice.Transform(new Point(
            bounds.X + bounds.Width, bounds.Y + bounds.Height));
        return new Rect(topLeft, bottomRight);
    }

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
    /// Adds a single-char label for a cross cell. Center row cells get horiz label,
    /// center col cells get vert label, center cell gets both or a marker.
    /// </summary>
    private void AddCrossLabel(Rect dipRect, int row, int col, CrosshairGrid grid,
        Brush foreground, double opacity = 1.0, double heightFraction = 0.8) {
        string? label = GetCrossLabel(row, col, grid);
        if (label is null) {
            return;
        }

        double fontSize = ComputeAutoFontSize(dipRect.Width, dipRect.Height, heightFraction);
        AddOutlinedText(label, _typeface, fontSize, foreground, _outlineBrush,
            _theme.LabelOutlineThickness, opacity,
            dipRect.X, dipRect.Y, dipRect.Width, dipRect.Height);
    }

    private string? GetCrossLabel(int row, int col, CrosshairGrid grid) {
        bool onCenterRow = row == grid.CenterRow;
        bool onCenterCol = col == grid.CenterCol;

        if (onCenterRow && onCenterCol) {
            return "\u2022"; // bullet for center cell
        }

        if (onCenterRow) {
            // Horiz label: col maps to key index (skip center col)
            int keyIndex = col < grid.CenterCol ? col : col - 1;
            return keyIndex >= 0 && keyIndex < _horizLabels.Count
                ? _horizLabels.LabelFor(keyIndex) : null;
        }

        if (onCenterCol) {
            // Vert label: row maps to key index (skip center row)
            int keyIndex = row < grid.CenterRow ? row : row - 1;
            return keyIndex >= 0 && keyIndex < _vertLabels.Count
                ? _vertLabels.LabelFor(keyIndex) : null;
        }

        return null;
    }

    /// <summary>
    /// Adds a label for a shifted-cross cell. Labels appear along the shifted
    /// cross arms (crossRow for horiz labels, crossCol for vert labels).
    /// </summary>
    private void AddShiftedLabel(Rect dipRect, int row, int col,
        int crossRow, int crossCol, CrosshairGrid grid,
        Brush foreground, double opacity = 1.0) {
        string? label = GetShiftedCrossLabel(row, col, crossRow, crossCol, grid);
        if (label is null) {
            return;
        }

        double fontSize = ComputeAutoFontSize(dipRect.Width, dipRect.Height, 0.8);
        AddOutlinedText(label, _typeface, fontSize, foreground, _outlineBrush,
            _theme.LabelOutlineThickness, opacity,
            dipRect.X, dipRect.Y, dipRect.Width, dipRect.Height);
    }

    private string? GetShiftedCrossLabel(int row, int col, int crossRow, int crossCol, CrosshairGrid grid) {
        bool onHorizArm = row == crossRow;
        bool onVertArm = col == crossCol;

        if (onHorizArm && onVertArm) {
            return "\u2022"; // intersection marker
        }

        if (onHorizArm) {
            // Horizontal label: col maps to key index (skip center col)
            int keyIndex = col < grid.CenterCol ? col : col - 1;
            return keyIndex >= 0 && keyIndex < _horizLabels.Count
                ? _horizLabels.LabelFor(keyIndex) : null;
        }

        if (onVertArm) {
            // Vertical label: row maps to key index (skip center row)
            int keyIndex = row < grid.CenterRow ? row : row - 1;
            return keyIndex >= 0 && keyIndex < _vertLabels.Count
                ? _vertLabels.LabelFor(keyIndex) : null;
        }

        return null;
    }

    private static double ComputeAutoFontSize(double cellWidth, double cellHeight, double heightFraction) {
        double fontFromHeight = cellHeight * heightFraction / 1.2;
        double fontFromWidth = cellWidth * 0.95 / 0.55;
        double fontSize = Math.Min(fontFromHeight, fontFromWidth);
        return Math.Max(fontSize, 8.0);
    }

    private void AddOutlinedText(string text, Typeface typeface, double fontSize,
        Brush fill, Brush outlineBrush, double outlineThickness, double opacity,
        double areaX, double areaY, double areaWidth, double areaHeight) {
        var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, typeface, fontSize, fill, VisualTreeHelper.GetDpi(_canvas).PixelsPerDip);
        var geometry = ft.BuildGeometry(new Point(0, 0));
        var bounds = geometry.Bounds;

        double offsetX = areaX + (areaWidth - bounds.Width) / 2 - bounds.X;
        double offsetY = areaY + (areaHeight - bounds.Height) / 2 - bounds.Y;

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

        var rect = new Rectangle {
            Width = dipRect.Width,
            Height = dipRect.Height,
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = strokeThickness,
        };
        Canvas.SetLeft(rect, dipRect.X);
        Canvas.SetTop(rect, dipRect.Y);
        _canvas.Children.Add(rect);
    }

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
}
