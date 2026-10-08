using System.Globalization;
using System.Windows;
using System.Windows.Automation;
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
    private RotateTransform? _spinnerRotation;
    private readonly Func<(System.Drawing.Point Origin, Matrix Scale)>? _coordinateSpace;

    public ElementHintsRenderer(Canvas canvas, ThemeModel theme, double minFontSize,
        VKey[] horizontal, VKey[] vertical, IKeyLabelResolver labels,
        Func<(System.Drawing.Point Origin, Matrix Scale)>? coordinateSpace = null) {
        _canvas = canvas; _theme = theme; _fontSize = Math.Max(minFontSize, theme.LabelFontSize);
        _horizontal = horizontal; _vertical = vertical; _labels = labels;
        _coordinateSpace = coordinateSpace;
    }
    public void RebuildLabels(IKeyLabelResolver resolver) => _labels = resolver;
    private string Label(HintLabel label) => _labels.Resolve(label.First) +
        (label.Second is { } second ? _labels.Resolve(second) : "") + (label.Entry.IsGroup ? "+" : "");
    private FormattedText Text(string text) => new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new Typeface(_theme.LabelFontFamily), _fontSize, Brush(_theme.LabelColor), VisualTreeHelper.GetDpi(_canvas).PixelsPerDip);
    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    public int GetPageCapacity(System.Drawing.Rectangle region, int keyCapacity, bool singleKey = false) {
        var measured = Enumerable.Range(0, keyCapacity).Select(i => Text(
            _labels.Resolve(_horizontal[singleKey ? i : i / _vertical.Length]) +
            (singleKey ? "" : _labels.Resolve(_vertical[i % _vertical.Length])) + "+")).ToArray();
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

    public void Render(HintLevelView view) {
        var labels = view.Labels;
        var targets = labels.Select(l => l.Entry).ToArray();
        int? prefix = view.Prefix, selectedToken = view.SelectedToken;
        _spinnerRotation?.BeginAnimation(RotateTransform.AngleProperty, null);
        _spinnerRotation = null;
        _canvas.Children.Clear();
        var (origin, scale) = Coordinates();
        double width = _width, height = _height;
        var areas = Areas(width, height);
        double cellWidth = _cellWidth, cellHeight = _cellHeight;
        bool associated = view.Compact && view.Depth > 1;
        var controlRects = targets.Select(target => new Rect(
            (target.Bounds.X - origin.X) * scale.M11, (target.Bounds.Y - origin.Y) * scale.M22,
            target.Bounds.Width * scale.M11, target.Bounds.Height * scale.M22)).ToArray();
        var viewport = new Rect(areas.Inset, areas.ListTop, areas.Width, areas.Height);
        var outlines = controlRects.Select((rect, index) => {
            var clipped = Rect.Intersect(rect, viewport);
            if (clipped.IsEmpty) { return clipped; }
            // Separate coincident outlines visually; target bounds and action points stay unchanged.
            int coincident = controlRects.Take(index).Count(previous => previous == rect);
            double inset = Math.Min(1.5 + coincident * 3, Math.Min(clipped.Width, clipped.Height) / 4);
            clipped.Inflate(-inset, -inset);
            return clipped;
        }).ToArray();
        var placed = new List<Rect>();
        bool list = cellWidth > areas.Width || cellHeight > areas.Height || targets.Length > _fitCapacity;
        for (int index = 0; index < targets.Length; index++) {
            var target = targets[index];
            var point = OverlayDip.ToCanvasPoint(target.Preview.X, target.Preview.Y, origin, scale);
            var rect = associated ? PlaceAtControl(outlines[index], viewport, placed, index) :
                PlaceNear(point, viewport, placed);
            if (list || rect is null) { list = true; break; }
            placed.Add(rect.Value);
        }
        if (list && view.Compact && labels.Count > 0) {
            cellWidth = labels.Max(l => Text(Label(l) + "  " + l.Entry.Description).WidthIncludingTrailingWhitespace) +
                16 + _theme.LabelOutlineThickness * 2;
        }
        double listWidth = Math.Min(areas.Width, view.Compact ? cellWidth : Math.Max(cellWidth, 360));
        int listColumns = view.Compact ? 1 : Math.Max(1, (int)Math.Floor(listWidth / cellWidth));
        Canvas? listCanvas = list && targets.Length > 0 ? new Canvas {
            Width = Math.Max(listWidth, cellWidth),
            Height = Math.Max(cellHeight, Math.Ceiling((double)targets.Length / listColumns) * cellHeight)
        } : null;
        double listLeft = width - areas.Inset - listWidth;
        double listTop = areas.ListTop;
        if (view.Compact && targets.Length > 0 && listCanvas is not null) {
            var anchor = targets[0].Preview;
            var point = OverlayDip.ToCanvasPoint(anchor.X, anchor.Y, origin, scale);
            listLeft = Math.Clamp(point.X - listWidth / 2, areas.Inset, width - areas.Inset - listWidth);
            listTop = Math.Clamp(point.Y + 16, areas.ListTop,
                areas.ListTop + areas.Height - Math.Min(areas.Height, listCanvas.Height + 2));
        }
        int previewIndex = view.FocusedId is { } focused ? Array.FindIndex(targets, t => t.Id == focused) :
            selectedToken is { } token ? Array.FindIndex(targets, t => t.Target?.Token == token) :
            prefix is { } column ? column * _vertical.Length : 0;
        double scrollOffset = listCanvas is not null && previewIndex >= 0 && previewIndex < targets.Length ?
            previewIndex / listColumns * cellHeight : 0;
        Line? selectedLine = null;
        Rect selectedLabel = default;
        for (int index = 0; index < targets.Length; index++) {
            var target = targets[index];
            var slot = list ? new Rect(index % listColumns * cellWidth,
                index / listColumns * cellHeight, cellWidth, cellHeight) : placed[index];
            var rect = new Rect(slot.X, slot.Y, slot.Width - 4, slot.Height - 4);
            bool selected = target.Target?.Token == selectedToken && selectedToken is not null;
            bool focusedEntry = view.FocusedId == target.Id;
            bool matching = prefix is not null && Array.IndexOf(_horizontal, labels[index].First) == prefix;
            double opacity = prefix is not null ? matching ? 1 : .2 :
                view.FocusedId is null && selectedToken is null || selected || focusedEntry ? 1 : .35;
            var targetRect = new Rect((target.Bounds.X - origin.X) * scale.M11,
                (target.Bounds.Y - origin.Y) * scale.M22,
                target.Bounds.Width * scale.M11, target.Bounds.Height * scale.M22);
            var accent = associated ? Accent(index) : Brush(_theme.LabelColor);
            double associationThickness = selected || focusedEntry ? 3 : 2;
            if (associated && !outlines[index].IsEmpty) {
                AddAssociationRect(_canvas, outlines[index], accent, index, associationThickness,
                    opacity, $"control:{target.Id}");
                if (!list) {
                    var end = OutlinePoint(outlines[index], new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2));
                    if (!rect.Contains(end)) {
                        AddAssociationLine(new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2),
                            end, accent, index, opacity, target.Id);
                    }
                }
            } else if (!associated && (selected || matching || focusedEntry)) {
                Add(new Rectangle {
                    Width = targetRect.Width, Height = targetRect.Height,
                    Stroke = Brush(_theme.ConnectorLineColor), StrokeThickness = selected ? 3 : 1, Opacity = .8
                }, targetRect.X, targetRect.Y);
            }
            if (selected && (!associated || list)) {
                var point = OverlayDip.ToCanvasPoint(target.Preview.X, target.Preview.Y, origin, scale);
                var start = list ? new Point(listLeft + Math.Min(listWidth, rect.Right),
                    listTop + Math.Clamp(rect.Top - scrollOffset + rect.Height / 2, 0, areas.Height)) :
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
            var background = Brush(_theme.LabelOutlineColor);
            background.Opacity = .4;
            AddTo(cardCanvas, new Border {
                Width = rect.Width, Height = rect.Height, Background = background,
                BorderBrush = associated ? Brushes.Transparent : Brush(_theme.LabelColor),
                BorderThickness = new Thickness(associated ? 0 : selected || focusedEntry ? 3 : 1),
                CornerRadius = new CornerRadius(target.IsGroup ? 5 : 0), Opacity = opacity
            },
                rect.X, rect.Y);
            if (associated) {
                AddAssociationRect(cardCanvas, rect, accent, index, associationThickness,
                    opacity, $"badge:{target.Id}", target.IsGroup ? 5 : 0, behind: false);
            }
            var text = Text(Label(labels[index]) + (list && view.Compact ? "  " + target.Description : ""));
            var geometry = text.BuildGeometry(new Point(rect.X + (rect.Width - text.WidthIncludingTrailingWhitespace) / 2,
                rect.Y + (rect.Height - text.Height) / 2));
            cardCanvas.Children.Add(new Path {
                Data = geometry, Fill = Brushes.Transparent,
                Stroke = associated ? Brushes.Black : Brush(_theme.LabelOutlineColor),
                StrokeThickness = associated ? Math.Max(3, _theme.LabelOutlineThickness * 2) :
                    _theme.LabelOutlineThickness * 2, Opacity = opacity
            });
            cardCanvas.Children.Add(new Path { Data = geometry, Fill = accent, Opacity = opacity });
        }
        if (listCanvas is not null) {
            var scroll = new ScrollViewer {
                Content = listCanvas, Width = listWidth, Height = Math.Min(areas.Height, listCanvas.Height + 2),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            if (selectedLine is not null) {
                scroll.ScrollChanged += (_, _) => {
                    selectedLine.X1 = listLeft + Math.Clamp(selectedLabel.Right - scroll.HorizontalOffset, 0, scroll.ViewportWidth);
                    selectedLine.Y1 = listTop + Math.Clamp(selectedLabel.Top - scroll.VerticalOffset +
                        selectedLabel.Height / 2, 0, scroll.ViewportHeight);
                };
            }
            scroll.ScrollToVerticalOffset(scrollOffset);
            Add(scroll, listLeft, listTop);
        }
        var messages = new List<string>();
        if (!string.IsNullOrEmpty(view.Status)) { messages.Add(view.Status); }
        messages.Add($"L{view.Depth}");
        if (selectedToken is not null) {
            messages.Add("Selected: action key to act");
        } else if (prefix is not null) {
            messages.Add("Type the second label key");
        } else {
            messages.Add(view.FocusedId is < 0 ? "Group focused: type its label to open" :
                view.SingleKey ? "One label key selects; + opens a group" : "Two label keys select; + opens a group");
        }
        if (view.ArrowKeys && targets.Length > 0) { messages.Add("Arrows: focus"); }
        if (associated && previewIndex >= 0 && previewIndex < targets.Length &&
            (view.FocusedId is not null || selectedToken is not null)) {
            messages.Add($"{Label(labels[previewIndex])}: {targets[previewIndex].Description}");
        }
        if (view.PageCount > 1) { messages.Add($"Page {view.Page + 1}/{view.PageCount}: PgUp/PgDn"); }
        messages.Add(view.Depth > 1 ? "Enter: grid; Esc: back" : "Enter: grid; Esc: cancel");
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
        if (view.IsDiscovering) { RenderSpinner(width, height); }
    }
    private static SolidColorBrush Accent(int index) => Brush((index % 6) switch {
        0 => "#56B4E9",
        1 => "#E69F00",
        2 => "#009E73",
        3 => "#CC79A7",
        4 => "#F0E442",
        _ => "#D55E00"
    });
    private static DoubleCollection Pattern(int index) => (index % 6) switch {
        0 => [],
        1 => [4, 2],
        2 => [1, 2],
        3 => [4, 2, 1, 2],
        4 => [6, 2],
        _ => [1, 1]
    };
    private static void AddAssociationRect(Canvas canvas, Rect rect, Brush accent, int index,
        double thickness, double opacity, string tag, double radius = 0, bool behind = true) {
        var halo = new Rectangle {
            Width = rect.Width, Height = rect.Height, Stroke = Brushes.Black,
            StrokeThickness = thickness + 2, RadiusX = radius, RadiusY = radius,
            Opacity = opacity, IsHitTestVisible = false
        };
        Panel.SetZIndex(halo, behind ? -2 : 0);
        AddTo(canvas, halo, rect.X, rect.Y);
        var outline = new Rectangle {
            Width = rect.Width, Height = rect.Height, Stroke = accent, StrokeThickness = thickness,
            StrokeDashArray = Pattern(index), RadiusX = radius, RadiusY = radius, Opacity = opacity,
            IsHitTestVisible = false, Tag = tag
        };
        Panel.SetZIndex(outline, behind ? -1 : 0);
        AddTo(canvas, outline, rect.X, rect.Y);
    }
    private void AddAssociationLine(Point start, Point end, Brush accent, int index, double opacity, int id) {
        var halo = new Line {
            X1 = start.X, Y1 = start.Y, X2 = end.X, Y2 = end.Y,
            Stroke = Brushes.Black, StrokeThickness = 4, Opacity = opacity, IsHitTestVisible = false
        };
        Panel.SetZIndex(halo, -2);
        _canvas.Children.Add(halo);
        var line = new Line {
            X1 = start.X, Y1 = start.Y, X2 = end.X, Y2 = end.Y,
            Stroke = accent, StrokeThickness = 2, StrokeDashArray = Pattern(index),
            Opacity = opacity, IsHitTestVisible = false, Tag = $"link:{id}"
        };
        Panel.SetZIndex(line, -1);
        _canvas.Children.Add(line);
    }
    private Rect? PlaceAtControl(Rect control, Rect viewport, List<Rect> placed, int index) {
        if (control.IsEmpty || _cellWidth > viewport.Width || _cellHeight > viewport.Height) { return null; }
        var corners = new[] { control.TopLeft, control.TopRight, control.BottomLeft, control.BottomRight };
        for (int offset = 0; offset < corners.Length; offset++) {
            var point = corners[(index + offset) % corners.Length];
            var rect = new Rect(Math.Clamp(point.X - _cellWidth / 2, viewport.Left, viewport.Right - _cellWidth),
                Math.Clamp(point.Y - _cellHeight / 2, viewport.Top, viewport.Bottom - _cellHeight),
                _cellWidth, _cellHeight);
            if (!placed.Any(previous => previous.IntersectsWith(rect))) { return rect; }
        }
        return PlaceNear(new(control.X + control.Width / 2, control.Top - _cellHeight / 2 - 4), viewport, placed);
    }
    private static Point OutlinePoint(Rect rect, Point point) {
        var clamped = new Point(Math.Clamp(point.X, rect.Left, rect.Right), Math.Clamp(point.Y, rect.Top, rect.Bottom));
        if (!rect.Contains(point)) { return clamped; }
        return new[] { new Point(rect.Left, point.Y), new Point(rect.Right, point.Y),
            new Point(point.X, rect.Top), new Point(point.X, rect.Bottom) }
            .MinBy(candidate => (candidate - point).LengthSquared);
    }
    private void RenderSpinner(double width, double height) {
        var content = new Canvas { Width = 48, Height = 48, IsHitTestVisible = false };
        AddTo(content, new Ellipse {
            Width = 40, Height = 40, Stroke = Brush(_theme.LabelOutlineColor), StrokeThickness = 4, Opacity = .4
        }, 4, 4);
        var rotation = new RotateTransform(0, 24, 24);
        content.Children.Add(new Path {
            Data = Geometry.Parse("M 24,4 A 20,20 0 1 1 4,24"),
            Stroke = Brush(_theme.LabelColor), StrokeThickness = 4,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            RenderTransform = rotation
        });
        rotation.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(900)) { RepeatBehavior = RepeatBehavior.Forever });
        _spinnerRotation = rotation;
        double size = Math.Min(48, Math.Min(width, height));
        var spinner = new Viewbox { Width = size, Height = size, Child = content, IsHitTestVisible = false };
        AutomationProperties.SetName(spinner, "Finding controls");
        spinner.Unloaded += (_, _) => rotation.BeginAnimation(RotateTransform.AngleProperty, null);
        Add(spinner, (width - size) / 2, (height - size) / 2);
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
