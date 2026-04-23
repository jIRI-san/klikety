using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
    private readonly double _minLabelFontSize;

    // DIP transform — set once when overlay is shown
    private Matrix _transformFromDevice = Matrix.Identity;

    public GridRenderer(Canvas canvas, ThemeModel theme, LabelGenerator labelGenerator, double minLabelFontSize = 10.0)
    {
        _canvas = canvas;
        _theme = theme;
        _labelGenerator = labelGenerator;
        _minLabelFontSize = minLabelFontSize;
    }

    /// <summary>
    /// Sets the device→DIP transform matrix. Call once when overlay is shown.
    /// </summary>
    public void SetTransform(Matrix transformFromDevice)
    {
        _transformFromDevice = transformFromDevice;
    }

  /// <summary>
  /// Adds two TextBlocks for a cell label: First centered in the left half,
  /// Second centered in the right half.
  /// </summary>
  private void AddLabel(Rect dipRect, int row, int col, Brush foreground, double opacity = 1.0)
  {
    var cellLabel = _labelGenerator.LabelFor(row, col);
    double halfWidth = dipRect.Width / 2;
    var fontFamily = new FontFamily(_theme.LabelFontFamily);
    var fontWeight = ParseFontWeight(_theme.LabelFontWeight);

    var first = new TextBlock
    {
      Text = cellLabel.First,
      Foreground = foreground,
      FontFamily = fontFamily,
      FontSize = _theme.LabelFontSize,
      FontWeight = fontWeight,
      TextAlignment = TextAlignment.Center,
      Opacity = opacity,
    };
    first.Measure(new Size(halfWidth, dipRect.Height));
    Canvas.SetLeft(first, dipRect.X + (halfWidth - first.DesiredSize.Width) / 2);
    Canvas.SetTop(first, dipRect.Y + (dipRect.Height - first.DesiredSize.Height) / 2);
    _canvas.Children.Add(first);

    var second = new TextBlock
    {
      Text = cellLabel.Second,
      Foreground = foreground,
      FontFamily = fontFamily,
      FontSize = _theme.LabelFontSize,
      FontWeight = fontWeight,
      TextAlignment = TextAlignment.Center,
      Opacity = opacity,
    };
    second.Measure(new Size(halfWidth, dipRect.Height));
    Canvas.SetLeft(second, dipRect.X + halfWidth + (halfWidth - second.DesiredSize.Width) / 2);
    Canvas.SetTop(second, dipRect.Y + (dipRect.Height - second.DesiredSize.Height) / 2);
    _canvas.Children.Add(second);
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

      AddLabel(dipRect, cell.Row, cell.Col, labelBrush);
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

      AddLabel(dipRect, cell.Row, cell.Col, labelBrush, isHighlighted ? 1.0 : 0.3);
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

      AddLabel(dipRect, cell.Row, cell.Col, labelBrush);
    }
    }

    /// <summary>
    /// Renders a subgrid within a parent cell's bounds using distinct subgrid styling.
    /// If cells are too small for labels at MinLabelFontSize, renders labels externally.
    /// </summary>
    public void RenderSubgrid(IReadOnlyList<GridCell> cells)
    {
        _canvas.Children.Clear();

        if (cells.Count == 0) return;

        var firstDip = ToDip(cells[0].Bounds);
        bool useExternalLabels = ShouldUseExternalLabels(firstDip.Height, _minLabelFontSize);

        var borderBrush = BrushFromHex(_theme.SubgridBorderColor);
        var bgBrush = BrushFromHex(_theme.CellBackgroundColor, _theme.CellBackgroundOpacity);

        // Draw cell backgrounds
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
        }

        if (useExternalLabels)
        {
            RenderExternalLabels(cells);
        }
        else
        {
            var labelBrush = BrushFromHex(_theme.SubgridLabelColor);
            foreach (var cell in cells)
            {
                AddLabel(ToDip(cell.Bounds), cell.Row, cell.Col, labelBrush);
            }
        }
    }

    /// <summary>
    /// Renders labels outside the subgrid: column keys along the top, row keys along
    /// the left side, with connector lines linking labels to their grid column/row.
    /// </summary>
    private void RenderExternalLabels(IReadOnlyList<GridCell> cells)
    {
        var extLabelBrush = BrushFromHex(_theme.ExternalLabelColor);
        var connectorBrush = BrushFromHex(_theme.ConnectorLineColor);
        var fontFamily = new FontFamily(_theme.LabelFontFamily);
        var fontWeight = ParseFontWeight(_theme.LabelFontWeight);
        double fontSize = Math.Max(_minLabelFontSize, _theme.LabelFontSize * 0.8);

        // Determine grid bounds from cells
        var gridTopLeft = ToDip(cells[0].Bounds);
        var lastCell = ToDip(cells[^1].Bounds);
        double gridLeft = gridTopLeft.X;
        double gridTop = gridTopLeft.Y;
        double gridRight = lastCell.X + lastCell.Width;
        double gridBottom = lastCell.Y + lastCell.Height;

        int cols = _labelGenerator.Cols;
        int rows = _labelGenerator.Rows;

        // External column labels (first keys) above the grid
        double labelMargin = fontSize * 1.5;
        for (int c = 0; c < cols && c < cells.Count; c++)
        {
            var cellDip = ToDip(cells[c].Bounds);
            var cellLabel = _labelGenerator.LabelFor(0, c);
            double colCenter = cellDip.X + cellDip.Width / 2;

            // Label above grid
            var tb = new TextBlock
            {
                Text = cellLabel.First,
                Foreground = extLabelBrush,
                FontFamily = fontFamily,
                FontSize = fontSize,
                FontWeight = fontWeight,
                TextAlignment = TextAlignment.Center,
            };
            tb.Measure(new Size(cellDip.Width, labelMargin));
            Canvas.SetLeft(tb, colCenter - tb.DesiredSize.Width / 2);
            Canvas.SetTop(tb, gridTop - labelMargin);
            _canvas.Children.Add(tb);

            // Connector line from label bottom to grid top
            var line = new Line
            {
                X1 = colCenter, Y1 = gridTop - labelMargin + tb.DesiredSize.Height + 2,
                X2 = colCenter, Y2 = gridTop,
                Stroke = connectorBrush,
                StrokeThickness = _theme.ConnectorLineThickness,
                StrokeDashArray = [2, 2],
            };
            _canvas.Children.Add(line);
        }

        // External row labels (second keys) to the left of the grid
        for (int r = 0; r < rows; r++)
        {
            int cellIndex = r * cols;
            if (cellIndex >= cells.Count) break;

            var cellDip = ToDip(cells[cellIndex].Bounds);
            var cellLabel = _labelGenerator.LabelFor(r, 0);
            double rowCenter = cellDip.Y + cellDip.Height / 2;

            // Label to left of grid
            var tb = new TextBlock
            {
                Text = cellLabel.Second,
                Foreground = extLabelBrush,
                FontFamily = fontFamily,
                FontSize = fontSize,
                FontWeight = fontWeight,
                TextAlignment = TextAlignment.Center,
            };
            tb.Measure(new Size(labelMargin, cellDip.Height));
            Canvas.SetLeft(tb, gridLeft - labelMargin);
            Canvas.SetTop(tb, rowCenter - tb.DesiredSize.Height / 2);
            _canvas.Children.Add(tb);

            // Connector line from label right to grid left
            var line = new Line
            {
                X1 = gridLeft - labelMargin + tb.DesiredSize.Width + 2, Y1 = rowCenter,
                X2 = gridLeft, Y2 = rowCenter,
                Stroke = connectorBrush,
                StrokeThickness = _theme.ConnectorLineThickness,
                StrokeDashArray = [2, 2],
            };
            _canvas.Children.Add(line);
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

  /// <summary>
  /// Brief red flash over the overlay to indicate an invalid key press.
  /// Non-blocking — uses a WPF animation that removes itself on completion.
  /// </summary>
  public void FlashInvalidKey()
  {
    var flash = new Rectangle
    {
      Width = _canvas.ActualWidth,
      Height = _canvas.ActualHeight,
      Fill = new SolidColorBrush(Color.FromArgb(80, 255, 0, 0)),
      IsHitTestVisible = false,
    };
    Canvas.SetLeft(flash, 0);
    Canvas.SetTop(flash, 0);
    _canvas.Children.Add(flash);

    var animation = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(200))
    {
      EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
    };
    animation.Completed += (_, _) => _canvas.Children.Remove(flash);
    flash.BeginAnimation(UIElement.OpacityProperty, animation);
  }

    /// <summary>
    /// Determines whether external labels should be used based on cell DIP height
    /// and minimum label font size. The 1.8 multiplier accounts for line height.
    /// </summary>
    internal static bool ShouldUseExternalLabels(double cellDipHeight, double minLabelFontSize)
        => cellDipHeight < minLabelFontSize * 1.8;
}
