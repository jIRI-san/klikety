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
  private readonly LabelGenerator _leftLabelGenerator;
  private readonly LabelGenerator _rightLabelGenerator;
  private readonly double _minLabelFontSize;

  // Which label generator to use for current render operations
  private LabelGenerator _activeLabelGenerator;

  // DIP transform — auto-initialized from canvas PresentationSource on first render
  private Matrix _transformFromDevice = Matrix.Identity;
  private bool _transformInitialized;

  public GridRenderer(Canvas canvas, ThemeModel theme, LabelGenerator leftLabelGenerator, LabelGenerator rightLabelGenerator, double minLabelFontSize = 10.0)
  {
        _canvas = canvas;
        _theme = theme;
    _leftLabelGenerator = leftLabelGenerator;
    _rightLabelGenerator = rightLabelGenerator;
    _activeLabelGenerator = leftLabelGenerator;
    _minLabelFontSize = minLabelFontSize;
    }

  /// <summary>
  /// Sets the device→DIP transform matrix. Call once when overlay is shown.
  /// If never called, auto-initialized from the canvas PresentationSource.
  /// </summary>
  public void SetTransform(Matrix transformFromDevice)
    {
        _transformFromDevice = transformFromDevice;
    _transformInitialized = true;
  }

  /// <summary>
  /// Auto-initializes transform from the canvas's PresentationSource if not set.
  /// </summary>
  private void EnsureTransform()
  {
    if (_transformInitialized) return;
    var source = PresentationSource.FromVisual(_canvas);
    if (source?.CompositionTarget != null)
    {
      _transformFromDevice = source.CompositionTarget.TransformFromDevice;
      _transformInitialized = true;
    }
  }

  /// <summary>
  /// Computes the DIP region covered by a set of cells, accounting for DPI scaling.
  /// Transforms only the corners of the full cell range to DIP, then uses that
  /// as the basis for even subdivision — no per-cell int rounding.
  /// </summary>
  private Rect ComputeRegionFromCells(IReadOnlyList<GridCell> cells)
  {
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
  private static Rect DipRectForCell(int row, int col, Rect region, int totalCols, int totalRows)
  {
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
  private double ComputeAutoFontSize(double cellHalfWidth, double cellHeight, double heightFraction = 0.8)
  {
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
  /// Adds two TextBlocks for a cell label: First centered in the left half,
  /// Second centered in the right half. Font auto-scaled to 80% of cell half-width.
  /// </summary>
  private void AddLabel(Rect dipRect, int row, int col, Brush foreground, double opacity = 1.0, double heightFraction = 0.8)
  {
    var cellLabel = _activeLabelGenerator.LabelFor(row, col);
    double halfWidth = dipRect.Width / 2;
    var fontFamily = new FontFamily(_theme.LabelFontFamily);
    var fontWeight = ParseFontWeight(_theme.LabelFontWeight);
    double fontSize = ComputeAutoFontSize(halfWidth, dipRect.Height, heightFraction);

    var first = new TextBlock
    {
      Text = cellLabel.First,
      Foreground = foreground,
      FontFamily = fontFamily,
      FontSize = fontSize,
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
      FontSize = fontSize,
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
  /// Sets the active label generator for subsequent render calls.
  /// </summary>
  public void SetActiveHalf(Navigation.ScreenHalf half)
  {
    _activeLabelGenerator = half == Navigation.ScreenHalf.Left ? _leftLabelGenerator : _rightLabelGenerator;
  }

  /// <summary>
  /// Clears all canvas children. Called during deactivation to prevent stale frame flash.
  /// </summary>
  public void ClearCanvas() => _canvas.Children.Clear();

  private void AddCellRect(Rect dipRect, Brush fill, Brush stroke, double strokeThickness = -1) {
    if (strokeThickness < 0) strokeThickness = _theme.CellBorderThickness;
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
  /// Renders both halves of the split-screen grid with labels.
  /// </summary>
  public void RenderBothHalves(IReadOnlyList<GridCell> leftCells, IReadOnlyList<GridCell> rightCells)
  {
    _canvas.Children.Clear();
    EnsureTransform();

    var borderBrush = BrushFromHex(_theme.CellBorderColor);
    var bgBrush = BrushFromHex(_theme.CellBackgroundColor, _theme.CellBackgroundOpacity);
    var labelBrush = BrushFromHex(_theme.LabelColor);

    // Render left half
    if (leftCells.Count > 0)
    {
      _activeLabelGenerator = _leftLabelGenerator;
      var region = ComputeRegionFromCells(leftCells);
      int cols = _leftLabelGenerator.Cols;
      int rows = _leftLabelGenerator.Rows;
      RenderCellsInRegion(leftCells, region, cols, rows, borderBrush, bgBrush, labelBrush);
    }

    // Render right half
    if (rightCells.Count > 0)
    {
      _activeLabelGenerator = _rightLabelGenerator;
      var region = ComputeRegionFromCells(rightCells);
      int cols = _rightLabelGenerator.Cols;
      int rows = _rightLabelGenerator.Rows;
      RenderCellsInRegion(rightCells, region, cols, rows, borderBrush, bgBrush, labelBrush);
    }
  }

  private void RenderCellsInRegion(IReadOnlyList<GridCell> cells, Rect region, int cols, int rows,
      Brush borderBrush, Brush bgBrush, Brush labelBrush)
  {
    foreach (var cell in cells)
    {
      var dipRect = DipRectForCell(cell.Row, cell.Col, region, cols, rows);
      AddCellRect(dipRect, bgBrush, borderBrush);
      AddLabel(dipRect, cell.Row, cell.Col, labelBrush);
    }
  }

  /// <summary>
  /// Renders the full grid (all cells with borders and labels).
  /// Used for single-half rendering after column highlight.
  /// </summary>
  public void RenderGrid(IReadOnlyList<GridCell> cells)
    {
        _canvas.Children.Clear();
    EnsureTransform();

    var region = ComputeRegionFromCells(cells);
    int cols = _activeLabelGenerator.Cols;
    int rows = _activeLabelGenerator.Rows;

    var borderBrush = BrushFromHex(_theme.CellBorderColor);
        var bgBrush = BrushFromHex(_theme.CellBackgroundColor, _theme.CellBackgroundOpacity);
        var labelBrush = BrushFromHex(_theme.LabelColor);

        foreach (var cell in cells)
        {
      var dipRect = DipRectForCell(cell.Row, cell.Col, region, cols, rows);
      AddCellRect(dipRect, bgBrush, borderBrush);
      AddLabel(dipRect, cell.Row, cell.Col, labelBrush);
    }
    }

    /// <summary>
    /// Highlights a column and dims non-matching cells.
    /// </summary>
    public void HighlightColumn(IReadOnlyList<GridCell> cells, int col)
    {
        _canvas.Children.Clear();
    EnsureTransform();

    var region = ComputeRegionFromCells(cells);
    int cols = _activeLabelGenerator.Cols;
    int rows = _activeLabelGenerator.Rows;

    var borderBrush = BrushFromHex(_theme.CellBorderColor);
        var normalBg = BrushFromHex(_theme.CellBackgroundColor, _theme.CellBackgroundOpacity);
        var dimBrush = BrushFromHex(_theme.DimmedOverlayColor, _theme.DimmedOverlayOpacity);
        var highlightBg = BrushFromHex(_theme.HighlightedColumnBackground, 0.5);
        var highlightBorder = BrushFromHex(_theme.HighlightedColumnBorderColor);
        var labelBrush = BrushFromHex(_theme.LabelColor);

        foreach (var cell in cells)
        {
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
    public void HighlightCell(IReadOnlyList<GridCell> cells, GridCell highlightedCell)
    {
        _canvas.Children.Clear();
    EnsureTransform();

    var region = ComputeRegionFromCells(cells);
    int cols = _activeLabelGenerator.Cols;
    int rows = _activeLabelGenerator.Rows;

    var borderBrush = BrushFromHex(_theme.CellBorderColor);
        var bgBrush = BrushFromHex(_theme.CellBackgroundColor, _theme.CellBackgroundOpacity);
        var highlightBg = BrushFromHex(_theme.HighlightedColumnBackground, 0.5);
        var highlightBorder = BrushFromHex(_theme.HighlightedColumnBorderColor);
        var labelBrush = BrushFromHex(_theme.LabelColor);

        foreach (var cell in cells)
        {
      var dipRect = DipRectForCell(cell.Row, cell.Col, region, cols, rows);
      bool isHighlighted = cell.Row == highlightedCell.Row && cell.Col == highlightedCell.Col;
      AddCellRect(dipRect,
          isHighlighted ? highlightBg : bgBrush,
          isHighlighted ? highlightBorder : borderBrush,
          isHighlighted ? _theme.CellBorderThickness * 2 : _theme.CellBorderThickness);
      AddLabel(dipRect, cell.Row, cell.Col, labelBrush);
    }
  }

  /// <summary>
  /// Highlights a column in split-screen mode: active half shows column highlight + dimmed,
  /// inactive half fully dimmed with low-opacity labels.
  /// </summary>
  public void HighlightColumnSplitScreen(
      IReadOnlyList<GridCell> leftCells,
      IReadOnlyList<GridCell> rightCells,
      Navigation.ScreenHalf activeHalf,
      int col) {
    _canvas.Children.Clear();
    EnsureTransform();

    var borderBrush = BrushFromHex(_theme.CellBorderColor);
    var dimBrush = BrushFromHex(_theme.DimmedOverlayColor, _theme.DimmedOverlayOpacity);
    var highlightBg = BrushFromHex(_theme.HighlightedColumnBackground, 0.5);
    var highlightBorder = BrushFromHex(_theme.HighlightedColumnBorderColor);
    var labelBrush = BrushFromHex(_theme.LabelColor);

    // Render inactive half (dimmed)
    var inactiveCells = activeHalf == Navigation.ScreenHalf.Left ? rightCells : leftCells;
    var inactiveGen = activeHalf == Navigation.ScreenHalf.Left ? _rightLabelGenerator : _leftLabelGenerator;
    if (inactiveCells.Count > 0) {
      _activeLabelGenerator = inactiveGen;
      var region = ComputeRegionFromCells(inactiveCells);
      foreach (var cell in inactiveCells) {
        var dipRect = DipRectForCell(cell.Row, cell.Col, region, inactiveGen.Cols, inactiveGen.Rows);
        AddCellRect(dipRect, dimBrush, borderBrush);
        AddLabel(dipRect, cell.Row, cell.Col, labelBrush, 0.3);
      }
    }

    // Render active half (column highlight + dimmed non-highlighted)
    var activeCells = activeHalf == Navigation.ScreenHalf.Left ? leftCells : rightCells;
    var activeGen = activeHalf == Navigation.ScreenHalf.Left ? _leftLabelGenerator : _rightLabelGenerator;
    if (activeCells.Count > 0) {
      _activeLabelGenerator = activeGen;
      var region = ComputeRegionFromCells(activeCells);
      foreach (var cell in activeCells) {
        var dipRect = DipRectForCell(cell.Row, cell.Col, region, activeGen.Cols, activeGen.Rows);
        bool isHighlighted = cell.Col == col;
        AddCellRect(dipRect,
            isHighlighted ? highlightBg : dimBrush,
            isHighlighted ? highlightBorder : borderBrush);
        AddLabel(dipRect, cell.Row, cell.Col, labelBrush, isHighlighted ? 1.0 : 0.3);
      }
    }
  }

  /// <summary>
  /// Highlights a single cell in split-screen mode: active half shows cell highlight
  /// with dimmed non-highlighted cells, inactive half fully dimmed.
  /// </summary>
  public void HighlightCellSplitScreen(
      IReadOnlyList<GridCell> leftCells,
      IReadOnlyList<GridCell> rightCells,
      Navigation.ScreenHalf activeHalf,
      GridCell highlightedCell) {
    _canvas.Children.Clear();
    EnsureTransform();

    var borderBrush = BrushFromHex(_theme.CellBorderColor);
    var dimBrush = BrushFromHex(_theme.DimmedOverlayColor, _theme.DimmedOverlayOpacity);
    var highlightBg = BrushFromHex(_theme.HighlightedColumnBackground, 0.5);
    var highlightBorder = BrushFromHex(_theme.HighlightedColumnBorderColor);
    var labelBrush = BrushFromHex(_theme.LabelColor);

    // Inactive half (dimmed)
    var inactiveCells = activeHalf == Navigation.ScreenHalf.Left ? rightCells : leftCells;
    var inactiveGen = activeHalf == Navigation.ScreenHalf.Left ? _rightLabelGenerator : _leftLabelGenerator;
    if (inactiveCells.Count > 0) {
      _activeLabelGenerator = inactiveGen;
      var region = ComputeRegionFromCells(inactiveCells);
      foreach (var cell in inactiveCells) {
        var dipRect = DipRectForCell(cell.Row, cell.Col, region, inactiveGen.Cols, inactiveGen.Rows);
        AddCellRect(dipRect, dimBrush, borderBrush);
        AddLabel(dipRect, cell.Row, cell.Col, labelBrush, 0.3);
      }
    }

    // Active half (cell highlight, non-highlighted use dimBrush)
    var activeCells = activeHalf == Navigation.ScreenHalf.Left ? leftCells : rightCells;
    var activeGen = activeHalf == Navigation.ScreenHalf.Left ? _leftLabelGenerator : _rightLabelGenerator;
    if (activeCells.Count > 0) {
      _activeLabelGenerator = activeGen;
      var region = ComputeRegionFromCells(activeCells);
      foreach (var cell in activeCells) {
        var dipRect = DipRectForCell(cell.Row, cell.Col, region, activeGen.Cols, activeGen.Rows);
        bool isHighlighted = cell.Row == highlightedCell.Row && cell.Col == highlightedCell.Col;
        AddCellRect(dipRect,
            isHighlighted ? highlightBg : dimBrush,
            isHighlighted ? highlightBorder : borderBrush,
            isHighlighted ? _theme.CellBorderThickness * 2 : _theme.CellBorderThickness);
        AddLabel(dipRect, cell.Row, cell.Col, labelBrush, isHighlighted ? 1.0 : 0.3);
      }
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
    EnsureTransform();

    var region = ComputeRegionFromCells(cells);
    int cols = _activeLabelGenerator.Cols;
    int rows = _activeLabelGenerator.Rows;

    var firstDip = DipRectForCell(0, 0, region, cols, rows);
    bool useExternalLabels = ShouldUseExternalLabels(firstDip.Height, _minLabelFontSize);

        var borderBrush = BrushFromHex(_theme.SubgridBorderColor);
        var bgBrush = BrushFromHex(_theme.CellBackgroundColor, _theme.CellBackgroundOpacity);

        // Draw cell backgrounds
        foreach (var cell in cells)
        {
      var dipRect = DipRectForCell(cell.Row, cell.Col, region, cols, rows);
      AddCellRect(dipRect, bgBrush, borderBrush);
    }

        if (useExternalLabels)
        {
      RenderExternalLabels(cells, region);
    }
        else
        {
      // Subgrids use 90% height fill for tighter packing before going external
      var labelBrush = BrushFromHex(_theme.SubgridLabelColor);
            foreach (var cell in cells)
            {
        AddLabel(DipRectForCell(cell.Row, cell.Col, region, cols, rows), cell.Row, cell.Col, labelBrush, heightFraction: 0.9);
      }
        }
    }

  /// <summary>
  /// Renders labels outside the subgrid: column keys along the top, row keys along
  /// the left side, with connector lines linking labels to their grid column/row.
  /// </summary>
  private void RenderExternalLabels(IReadOnlyList<GridCell> cells, Rect region)
  {
        var extLabelBrush = BrushFromHex(_theme.ExternalLabelColor);
        var connectorBrush = BrushFromHex(_theme.ConnectorLineColor);
        var fontFamily = new FontFamily(_theme.LabelFontFamily);
        var fontWeight = ParseFontWeight(_theme.LabelFontWeight);
        double fontSize = Math.Max(_minLabelFontSize, _theme.LabelFontSize * 0.8);

    int cols = _activeLabelGenerator.Cols;
    int rows = _activeLabelGenerator.Rows;

    double gridLeft = region.X;
    double gridTop = region.Y;

    // External column labels (first keys) above the grid
    double labelMargin = fontSize * 1.5;
        for (int c = 0; c < cols && c < cells.Count; c++)
        {
      var cellDip = DipRectForCell(0, c, region, cols, rows);
      var cellLabel = _activeLabelGenerator.LabelFor(0, c);
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

      var cellDip = DipRectForCell(r, 0, region, cols, rows);
      var cellLabel = _activeLabelGenerator.LabelFor(r, 0);
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
