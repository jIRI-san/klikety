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
        _cellWidth = measured.Max(t => t.WidthIncludingTrailingWhitespace) + 16 + _theme.LabelOutlineThickness * 2;
        _cellHeight = measured.Max(t => t.Height) + 12 + _theme.LabelOutlineThickness * 2;
        var scale = Coordinates().Scale;
        _width = _canvas.ActualWidth > 0 ? _canvas.ActualWidth : region.Width * scale.M11;
        _height = _canvas.ActualHeight > 0 ? _canvas.ActualHeight : region.Height * scale.M22;
        var areas = Areas(_width, _height);
        _columns = Math.Max(1, (int)Math.Floor(areas.Width / _cellWidth));
        _fitCapacity = Math.Clamp(_columns * Math.Max(1, (int)Math.Floor(areas.Height / _cellHeight)), 1, keyCapacity);
        return _fitCapacity;
    }

    public void Render(IReadOnlyList<HintTarget> targets, int page, int pageCount, int? prefix,
        int? selectedToken, string status) {
        _canvas.Children.Clear();
        var (origin, scale) = Coordinates();
        double width = _width, height = _height;
        var areas = Areas(width, height);
        bool scroll = _cellWidth > areas.Width || _cellHeight > areas.Height || targets.Count > _fitCapacity;
        Canvas? listCanvas = scroll ? new Canvas {
            Width = Math.Max(areas.Width, _cellWidth),
            Height = Math.Max(_cellHeight, Math.Ceiling((double)targets.Count / _columns) * _cellHeight)
        } : null;
        var placed = new List<Rect>();
        var inline = new List<Rect>();
        bool list = scroll;
        foreach (var target in targets) {
            var point = OverlayDip.ToCanvasPoint(target.Preview.X, target.Preview.Y, origin, scale);
            var rect = new Rect(point.X - _cellWidth / 2, point.Y - _cellHeight / 2, _cellWidth, _cellHeight);
            if (rect.X < 0 || rect.Y < areas.ListTop || rect.Right > width ||
                rect.Bottom > height || placed.Any(r => r.IntersectsWith(rect))) { list = true; }
            placed.Add(rect); inline.Add(rect);
        }
        for (int index = 0; index < targets.Count; index++) {
            var target = targets[index];
            var rect = list ? new Rect((scroll ? 0 : areas.Inset) + index % _columns * _cellWidth,
                (scroll ? 0 : areas.ListTop) + index / _columns * _cellHeight, _cellWidth - 4, _cellHeight - 4) : inline[index];
            bool selected = target.Token == selectedToken;
            double opacity = prefix is null || index / _vertical.Length == prefix ? 1 : .2;
            var targetRect = new Rect((target.VisibleBounds.X - origin.X) * scale.M11,
                (target.VisibleBounds.Y - origin.Y) * scale.M22,
                target.VisibleBounds.Width * scale.M11, target.VisibleBounds.Height * scale.M22);
            Add(new Rectangle {
                Width = targetRect.Width, Height = targetRect.Height,
                Stroke = Brush(_theme.LabelColor), StrokeThickness = selected ? 3 : 1, Opacity = opacity * .7
            },
                targetRect.X, targetRect.Y);
            if (list && !scroll) {
                var point = OverlayDip.ToCanvasPoint(target.Preview.X, target.Preview.Y, origin, scale);
                _canvas.Children.Add(new Line {
                    X1 = rect.Right, Y1 = rect.Top + rect.Height / 2,
                    X2 = point.X, Y2 = point.Y, Stroke = Brush(_theme.ConnectorLineColor), StrokeThickness = 1,
                    Opacity = selected ? 1 : .25
                });
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
            Add(new ScrollViewer {
                Content = listCanvas, Width = areas.Width, Height = areas.Height,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            }, areas.Inset, areas.ListTop);
        }
        var statusText = new TextBlock {
            Text = $"{status}\nPage {page + 1}/{pageCount}; Left/Right: pages" +
            (list ? "; list labels point to controls" : ""), FontSize = Math.Max(14, Math.Min(_fontSize, 20)),
            Foreground = Brush(_theme.LabelColor), Background = Brush(_theme.LabelOutlineColor),
            TextWrapping = TextWrapping.Wrap, MaxWidth = areas.Width
        };
        Add(new ScrollViewer {
            Content = statusText, Width = areas.Width,
            Height = areas.StatusHeight, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        }, areas.Inset, areas.StatusY);
    }
    private static (double Inset, double StatusY, double StatusHeight, double ListTop, double Width, double Height) Areas(double width, double height) {
        double inset = Math.Min(8, width / 4), gap = Math.Min(8, height / 8);
        double statusHeight = Math.Min(64, height / 3), listTop = gap + statusHeight + gap;
        return (inset, gap, statusHeight, listTop, width - inset * 2, height - listTop - gap);
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
