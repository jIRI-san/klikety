using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Klikety.Config;
using Klikety.Grid;

namespace Klikety.Overlay;

/// <summary>
/// Renders grid cells, labels, highlights, and dimming on the overlay Canvas.
/// Clears children on each transition to prevent memory accumulation.
/// </summary>
public sealed class GridRenderer
{
    private readonly Canvas _canvas;
    private readonly ThemeModel _theme;
    private readonly LabelGenerator _labelGenerator;

    // DIP transform — set once when overlay is shown
    private Matrix _transformFromDevice = Matrix.Identity;

    public GridRenderer(Canvas canvas, ThemeModel theme, LabelGenerator labelGenerator)
    {
        _canvas = canvas;
        _theme = theme;
        _labelGenerator = labelGenerator;
    }

    /// <summary>
    /// Sets the device→DIP transform matrix. Call once when overlay is shown.
    /// </summary>
    public void SetTransform(Matrix transformFromDevice)
    {
        _transformFromDevice = transformFromDevice;
    }

    /// <summary>
    /// Renders the full grid (all cells with borders and labels).
    /// </summary>
    public void RenderGrid(IReadOnlyList<GridCell> cells)
    {
        _canvas.Children.Clear();

        var borderBrush = BrushFromHex(_theme.CellBorderColor);
        var bgBrush = BrushFromHex(_theme.CellBackgroundColor, _theme.CellBackgroundOpacity);
        var labelBrush = BrushFromHex(_theme.LabelColor);

        foreach (var cell in cells)
        {
            var dipRect = ToDip(cell.Bounds);

            // Cell background
            var bg = new Rectangle
            {
                Width = dipRect.Width,
                Height = dipRect.Height,
                Fill = bgBrush,
                Stroke = borderBrush,
                StrokeThickness = _theme.CellBorderThickness,
            };
            Canvas.SetLeft(bg, dipRect.X);
            Canvas.SetTop(bg, dipRect.Y);
            _canvas.Children.Add(bg);

            // Label
            var label = new TextBlock
            {
                Text = _labelGenerator.LabelFor(cell.Row, cell.Col),
                Foreground = labelBrush,
                FontFamily = new FontFamily(_theme.LabelFontFamily),
                FontSize = _theme.LabelFontSize,
                FontWeight = ParseFontWeight(_theme.LabelFontWeight),
                TextAlignment = TextAlignment.Center,
            };
            label.Measure(new Size(dipRect.Width, dipRect.Height));
            Canvas.SetLeft(label, dipRect.X + (dipRect.Width - label.DesiredSize.Width) / 2);
            Canvas.SetTop(label, dipRect.Y + (dipRect.Height - label.DesiredSize.Height) / 2);
            _canvas.Children.Add(label);
        }
    }

    /// <summary>
    /// Highlights a column and dims non-matching cells.
    /// </summary>
    public void HighlightColumn(IReadOnlyList<GridCell> cells, int col)
    {
        _canvas.Children.Clear();

        var borderBrush = BrushFromHex(_theme.CellBorderColor);
        var normalBg = BrushFromHex(_theme.CellBackgroundColor, _theme.CellBackgroundOpacity);
        var dimBrush = BrushFromHex(_theme.DimmedOverlayColor, _theme.DimmedOverlayOpacity);
        var highlightBg = BrushFromHex(_theme.HighlightedColumnBackground, 0.5);
        var highlightBorder = BrushFromHex(_theme.HighlightedColumnBorderColor);
        var labelBrush = BrushFromHex(_theme.LabelColor);

        foreach (var cell in cells)
        {
            var dipRect = ToDip(cell.Bounds);
            bool isHighlighted = cell.Col == col;

            var bg = new Rectangle
            {
                Width = dipRect.Width,
                Height = dipRect.Height,
                Fill = isHighlighted ? highlightBg : dimBrush,
                Stroke = isHighlighted ? highlightBorder : borderBrush,
                StrokeThickness = _theme.CellBorderThickness,
            };
            Canvas.SetLeft(bg, dipRect.X);
            Canvas.SetTop(bg, dipRect.Y);
            _canvas.Children.Add(bg);

            var label = new TextBlock
            {
                Text = _labelGenerator.LabelFor(cell.Row, cell.Col),
                Foreground = labelBrush,
                FontFamily = new FontFamily(_theme.LabelFontFamily),
                FontSize = _theme.LabelFontSize,
                FontWeight = ParseFontWeight(_theme.LabelFontWeight),
                TextAlignment = TextAlignment.Center,
                Opacity = isHighlighted ? 1.0 : 0.3,
            };
            label.Measure(new Size(dipRect.Width, dipRect.Height));
            Canvas.SetLeft(label, dipRect.X + (dipRect.Width - label.DesiredSize.Width) / 2);
            Canvas.SetTop(label, dipRect.Y + (dipRect.Height - label.DesiredSize.Height) / 2);
            _canvas.Children.Add(label);
        }
    }

    /// <summary>
    /// Highlights a single cell (arrow navigation) without dimming others.
    /// </summary>
    public void HighlightCell(IReadOnlyList<GridCell> cells, GridCell highlightedCell)
    {
        _canvas.Children.Clear();

        var borderBrush = BrushFromHex(_theme.CellBorderColor);
        var bgBrush = BrushFromHex(_theme.CellBackgroundColor, _theme.CellBackgroundOpacity);
        var highlightBg = BrushFromHex(_theme.HighlightedColumnBackground, 0.5);
        var highlightBorder = BrushFromHex(_theme.HighlightedColumnBorderColor);
        var labelBrush = BrushFromHex(_theme.LabelColor);

        foreach (var cell in cells)
        {
            var dipRect = ToDip(cell.Bounds);
            bool isHighlighted = cell.Row == highlightedCell.Row && cell.Col == highlightedCell.Col;

            var bg = new Rectangle
            {
                Width = dipRect.Width,
                Height = dipRect.Height,
                Fill = isHighlighted ? highlightBg : bgBrush,
                Stroke = isHighlighted ? highlightBorder : borderBrush,
                StrokeThickness = isHighlighted ? _theme.CellBorderThickness * 2 : _theme.CellBorderThickness,
            };
            Canvas.SetLeft(bg, dipRect.X);
            Canvas.SetTop(bg, dipRect.Y);
            _canvas.Children.Add(bg);

            var label = new TextBlock
            {
                Text = _labelGenerator.LabelFor(cell.Row, cell.Col),
                Foreground = labelBrush,
                FontFamily = new FontFamily(_theme.LabelFontFamily),
                FontSize = _theme.LabelFontSize,
                FontWeight = ParseFontWeight(_theme.LabelFontWeight),
                TextAlignment = TextAlignment.Center,
            };
            label.Measure(new Size(dipRect.Width, dipRect.Height));
            Canvas.SetLeft(label, dipRect.X + (dipRect.Width - label.DesiredSize.Width) / 2);
            Canvas.SetTop(label, dipRect.Y + (dipRect.Height - label.DesiredSize.Height) / 2);
            _canvas.Children.Add(label);
        }
    }

    /// <summary>
    /// Renders a subgrid within a parent cell's bounds using distinct subgrid styling.
    /// </summary>
    public void RenderSubgrid(IReadOnlyList<GridCell> cells)
    {
        _canvas.Children.Clear();

        var borderBrush = BrushFromHex(_theme.SubgridBorderColor);
        var bgBrush = BrushFromHex(_theme.CellBackgroundColor, _theme.CellBackgroundOpacity);
        var labelBrush = BrushFromHex(_theme.SubgridLabelColor);

        foreach (var cell in cells)
        {
            var dipRect = ToDip(cell.Bounds);

            var bg = new Rectangle
            {
                Width = dipRect.Width,
                Height = dipRect.Height,
                Fill = bgBrush,
                Stroke = borderBrush,
                StrokeThickness = _theme.CellBorderThickness,
            };
            Canvas.SetLeft(bg, dipRect.X);
            Canvas.SetTop(bg, dipRect.Y);
            _canvas.Children.Add(bg);

            var label = new TextBlock
            {
                Text = _labelGenerator.LabelFor(cell.Row, cell.Col),
                Foreground = labelBrush,
                FontFamily = new FontFamily(_theme.LabelFontFamily),
                FontSize = _theme.LabelFontSize,
                FontWeight = ParseFontWeight(_theme.LabelFontWeight),
                TextAlignment = TextAlignment.Center,
            };
            label.Measure(new Size(dipRect.Width, dipRect.Height));
            Canvas.SetLeft(label, dipRect.X + (dipRect.Width - label.DesiredSize.Width) / 2);
            Canvas.SetTop(label, dipRect.Y + (dipRect.Height - label.DesiredSize.Height) / 2);
            _canvas.Children.Add(label);
        }
    }

    private Rect ToDip(System.Drawing.Rectangle physicalRect)
    {
        var topLeft = _transformFromDevice.Transform(new Point(physicalRect.X, physicalRect.Y));
        var bottomRight = _transformFromDevice.Transform(new Point(
            physicalRect.X + physicalRect.Width,
            physicalRect.Y + physicalRect.Height));
        return new Rect(topLeft, bottomRight);
    }

    private static SolidColorBrush BrushFromHex(string hex, double opacity = 1.0)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        var brush = new SolidColorBrush(color) { Opacity = opacity };
        brush.Freeze();
        return brush;
    }

    private static FontWeight ParseFontWeight(string weight)
    {
        return weight.ToLowerInvariant() switch
        {
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
