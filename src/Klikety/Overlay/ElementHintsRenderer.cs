using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

using Klikety.Automation;
using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Navigation;

namespace Klikety.Overlay;

public sealed class ElementHintsRenderer : IElementHintsRenderer {
    private readonly Canvas _canvas;
    private readonly ThemeModel _theme;
    private readonly double _fontSize;
    private readonly VKey[] _horizontal, _vertical;
    private IKeyLabelResolver _labels;
    private double _cellWidth, _cellHeight;
    private double _width, _height;
    private int _columns;
    private int _fitCapacity;
    private readonly Func<(System.Drawing.Point Origin, Matrix Scale)>? _coordinateSpace;

    public ElementHintsRenderer(Canvas canvas, ThemeModel theme, double minFontSize,
        VKey[] horizontal, VKey[] vertical, IKeyLabelResolver labels,
        Func<(System.Drawing.Point Origin, Matrix Scale)>? coordinateSpace = null) {
        _canvas = canvas; _theme = theme; _fontSize = Math.Max(minFontSize, theme.LabelFontSize);
        _horizontal = horizontal; _vertical = vertical; _labels = labels;
        _coordinateSpace = coordinateSpace;
    }
    public void RebuildLabels(IKeyLabelResolver resolver) => _labels = resolver;
    private string Label(int index) => _labels.Resolve(_horizontal[index / _vertical.Length]) +
        _labels.Resolve(_vertical[index % _vertical.Length]);
    private FormattedText Text(string text) => new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new Typeface(_theme.LabelFontFamily), _fontSize, Brush(_theme.LabelColor), VisualTreeHelper.GetDpi(_canvas).PixelsPerDip);
    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    public int GetPageCapacity(System.Drawing.Rectangle region, int keyCapacity) {
        var measured = Enumerable.Range(0, keyCapacity).Select(i => Text(Label(i))).ToArray();
        _cellWidth = measured.Max(t => t.WidthIncludingTrailingWhitespace) + 12 + _theme.LabelOutlineThickness * 2;
        _cellHeight = measured.Max(t => t.Height) + 8 + _theme.LabelOutlineThickness * 2;
        var scale = Coordinates().Scale;
        _width = _canvas.ActualWidth > 0 ? _canvas.ActualWidth : region.Width * scale.M11;
        _height = _canvas.ActualHeight > 0 ? _canvas.ActualHeight : region.Height * scale.M22;
        var areas = Areas(_width, _height);
        double listWidth = Math.Min(areas.Width, Math.Max(_cellWidth, 360));
        _columns = Math.Max(1, (int)Math.Floor(listWidth / _cellWidth));
        _fitCapacity = Math.Clamp(_columns * Math.Max(1, (int)Math.Floor(areas.Height / _cellHeight)), 1, keyCapacity);
        return _fitCapacity;
    }

    public void Render(IReadOnlyList<HintTarget> targets, int page, int pageCount, int? prefix,
        int? selectedToken, string status) {
        _canvas.Children.Clear();
        var (origin, scale) = Coordinates();
        double width = _width, height = _height;
        var areas = Areas(width, height);
        var viewport = new Rect(areas.Inset, areas.ListTop, areas.Width, areas.Height);
        var placed = new List<Rect>();
        bool list = _cellWidth > areas.Width || _cellHeight > areas.Height || targets.Count > _fitCapacity;
        foreach (var target in targets) {
            var point = OverlayDip.ToCanvasPoint(target.Preview.X, target.Preview.Y, origin, scale);
            if (list || PlaceNear(point, viewport, placed) is not { } rect) { list = true; break; }
            placed.Add(rect);
        }
        double listWidth = Math.Min(areas.Width, Math.Max(_cellWidth, 360));
        int listColumns = Math.Max(1, (int)Math.Floor(listWidth / _cellWidth));
        Canvas? listCanvas = list && targets.Count > 0 ? new Canvas {
            Width = Math.Max(listWidth, _cellWidth),
            Height = Math.Max(_cellHeight, Math.Ceiling((double)targets.Count / listColumns) * _cellHeight)
        } : null;
        double listLeft = width - areas.Inset - listWidth;
        int previewIndex = selectedToken is { } token ? targets.ToList().FindIndex(t => t.Token == token) :
            prefix is { } column ? column * _vertical.Length : 0;
        double scrollOffset = listCanvas is not null && previewIndex >= 0 && previewIndex < targets.Count ?
            previewIndex / listColumns * _cellHeight : 0;
        Line? selectedLine = null;
        Rect selectedLabel = default;
        for (int index = 0; index < targets.Count; index++) {
            var target = targets[index];
            var slot = list ? new Rect(index % listColumns * _cellWidth,
                index / listColumns * _cellHeight, _cellWidth, _cellHeight) : placed[index];
            var rect = new Rect(slot.X, slot.Y, slot.Width - 4, slot.Height - 4);
            bool selected = target.Token == selectedToken;
            bool matching = prefix is not null && index / _vertical.Length == prefix;
            double opacity = prefix is not null ? matching ? 1 : .2 : selectedToken is null || selected ? 1 : .35;
            var targetRect = new Rect((target.VisibleBounds.X - origin.X) * scale.M11,
                (target.VisibleBounds.Y - origin.Y) * scale.M22,
                target.VisibleBounds.Width * scale.M11, target.VisibleBounds.Height * scale.M22);
            if (selected || matching) {
                Add(new Rectangle {
                    Width = targetRect.Width, Height = targetRect.Height,
                    Stroke = Brush(_theme.ConnectorLineColor), StrokeThickness = selected ? 3 : 1, Opacity = .8
                }, targetRect.X, targetRect.Y);
            }
            if (selected) {
                var point = OverlayDip.ToCanvasPoint(target.Preview.X, target.Preview.Y, origin, scale);
                var start = list ? new Point(listLeft + Math.Min(listWidth, rect.Right),
                    areas.ListTop + Math.Clamp(rect.Top - scrollOffset + rect.Height / 2, 0, areas.Height)) :
                    new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
                if (list || !rect.Contains(point)) {
                    selectedLabel = rect;
                    selectedLine = new Line {
                        X1 = start.X, Y1 = start.Y, X2 = point.X, Y2 = point.Y,
                        Stroke = Brush(_theme.ConnectorLineColor), StrokeThickness = 2
                    };
                    _canvas.Children.Add(selectedLine);
                }
            }
            var cardCanvas = listCanvas ?? _canvas;
            AddTo(cardCanvas, new Border {
                Width = rect.Width, Height = rect.Height, Background = Brush(_theme.LabelOutlineColor),
                BorderBrush = Brush(_theme.LabelColor), BorderThickness = new Thickness(selected ? 3 : 1), Opacity = opacity
            },
                rect.X, rect.Y);
            var text = Text(Label(index));
            var geometry = text.BuildGeometry(new Point(rect.X + (rect.Width - text.WidthIncludingTrailingWhitespace) / 2,
                rect.Y + (rect.Height - text.Height) / 2));
            cardCanvas.Children.Add(new Path {
                Data = geometry, Fill = Brushes.Transparent,
                Stroke = Brush(_theme.LabelOutlineColor), StrokeThickness = _theme.LabelOutlineThickness * 2, Opacity = opacity
            });
            cardCanvas.Children.Add(new Path { Data = geometry, Fill = Brush(_theme.LabelColor), Opacity = opacity });
        }
        if (listCanvas is not null) {
            var scroll = new ScrollViewer {
                Content = listCanvas, Width = listWidth, Height = Math.Min(areas.Height, listCanvas.Height + 2),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            if (selectedLine is not null) {
                scroll.ScrollChanged += (_, _) => {
                    selectedLine.X1 = listLeft + Math.Clamp(selectedLabel.Right - scroll.HorizontalOffset, 0, scroll.ViewportWidth);
                    selectedLine.Y1 = areas.ListTop + Math.Clamp(selectedLabel.Top - scroll.VerticalOffset +
                        selectedLabel.Height / 2, 0, scroll.ViewportHeight);
                };
            }
            scroll.ScrollToVerticalOffset(scrollOffset);
            Add(scroll, listLeft, areas.ListTop);
        }
        var messages = new List<string>();
        if (!string.IsNullOrEmpty(status)) { messages.Add(status); }
        if (selectedToken is not null) {
            messages.Add("Selected: action key to act");
        } else if (prefix is not null) {
            messages.Add("Type the second label key");
        } else {
            messages.Add(list ? "Crowded controls: label keys preview a target" : "Two label keys select; action key acts");
        }
        if (pageCount > 1) { messages.Add($"Page {page + 1}/{pageCount}: Left/Right"); }
        messages.Add("Enter: grid; Esc: cancel");
        var statusText = new TextBlock {
            Text = string.Join(" | ", messages), FontSize = Math.Max(14, Math.Min(_fontSize, 20)),
            Foreground = Brush(_theme.LabelColor), TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8, 4, 8, 4), MaxWidth = areas.Width
        };
        statusText.Measure(new Size(areas.Width, double.PositiveInfinity));
        double statusWidth = Math.Min(areas.Width, statusText.DesiredSize.Width);
        double statusHeight = Math.Min(areas.StatusHeight, statusText.DesiredSize.Height);
        Add(new ScrollViewer {
            Content = new Border { Child = statusText, Background = Brush(_theme.LabelOutlineColor), CornerRadius = new CornerRadius(4) },
            Width = statusWidth, Height = statusHeight, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        }, (width - statusWidth) / 2, height - areas.StatusY - statusHeight);
    }
    private Rect? PlaceNear(Point point, Rect viewport, List<Rect> placed) {
        if (_cellWidth > viewport.Width || _cellHeight > viewport.Height) { return null; }
        var center = new Point(Math.Clamp(point.X - _cellWidth / 2, viewport.Left, viewport.Right - _cellWidth),
            Math.Clamp(point.Y - _cellHeight / 2, viewport.Top, viewport.Bottom - _cellHeight));
        for (int ring = 0; ring <= 4; ring++) {
            foreach (var (x, y) in new[] { (0, -1), (1, 0), (0, 1), (-1, 0), (-1, -1), (1, -1), (1, 1), (-1, 1) }) {
                var rect = new Rect(center.X + x * ring * (_cellWidth + 2), center.Y + y * ring * (_cellHeight + 2), _cellWidth, _cellHeight);
                if (viewport.Contains(rect) && !placed.Any(r => r.IntersectsWith(rect))) { return rect; }
            }
        }
        return null;
    }
    private static (double Inset, double StatusY, double StatusHeight, double ListTop, double Width, double Height) Areas(double width, double height) {
        double inset = Math.Min(8, width / 4), gap = Math.Min(8, height / 8);
        double statusHeight = Math.Min(48, height / 3);
        return (inset, gap, statusHeight, gap, width - inset * 2, height - statusHeight - gap * 3);
    }
    private (System.Drawing.Point Origin, Matrix Scale) Coordinates() =>
        _coordinateSpace?.Invoke() ?? (OverlayDip.WindowOrigin(_canvas), OverlayDip.ScaleOf(_canvas));
    private void Add(UIElement element, double x, double y) {
        AddTo(_canvas, element, x, y);
    }
    private static void AddTo(Canvas canvas, UIElement element, double x, double y) {
        Canvas.SetLeft(element, x); Canvas.SetTop(element, y); canvas.Children.Add(element);
    }
    public void FlashInvalidKey() {
        _canvas.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(.5, 1, TimeSpan.FromMilliseconds(120)));
    }
}
