using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

using Klikety.Config;
using Klikety.Interop;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Klikety.Overlay;

/// <summary>
/// Non-activating satellite overlay. Centered outlined digit; unnumbered displays show no digit.
/// </summary>
public sealed class SatelliteWindow : Window, ISatelliteOverlay {
    private readonly Canvas _canvas = new();
    private readonly ThemeModel _theme;
    private readonly ILogger _logger;
    private bool _exStyleApplied;
    private int? _number;

    public SatelliteWindow(ThemeModel theme, ILogger? logger = null) {
        _theme = theme;
        _logger = logger ?? NullLogger.Instance;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = DimBrush(theme);
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = _canvas;
        SizeChanged += (_, _) => RenderDigit();
    }

    public static double DigitFontSize(double dipWidth, double dipHeight) =>
        Math.Clamp(0.4 * Math.Min(dipWidth, dipHeight), 96, 400);

    public void Show(System.Drawing.Rectangle physicalBounds, int? number) {
        _number = number;
        OverlayPlacement.Place(this, physicalBounds, activate: false, _logger);
        if (!_exStyleApplied) {
            NativeMethods.SetClickThroughExStyle(
                new System.Windows.Interop.WindowInteropHelper(this).Handle);
            _exStyleApplied = true;
        }

        UpdateLayout();
        RenderDigit();
    }

    void ISatelliteOverlay.Hide() => Hide();

    public void Dispose() => Close();

    private void RenderDigit() {
        _canvas.Children.Clear();
        if (_number is not int digit) {
            return;
        }

        double dipWidth = ActualWidth > 1 ? ActualWidth : Width;
        double dipHeight = ActualHeight > 1 ? ActualHeight : Height;
        AddDigit(digit.ToString(CultureInfo.InvariantCulture), dipWidth, dipHeight);
    }

    private static SolidColorBrush DimBrush(ThemeModel theme) {
        var color = (Color)ColorConverter.ConvertFromString(theme.CellBackgroundColor)!;
        color.A = (byte)Math.Clamp((int)(Math.Min(theme.CellBackgroundOpacity, 0.35) * 255), 20, 90);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void AddDigit(string text, double dipWidth, double dipHeight) {
        double fontSize = DigitFontSize(dipWidth, dipHeight);
        var typeface = new Typeface(new FontFamily(_theme.LabelFontFamily), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var fill = BrushFromHex(_theme.LabelColor);
        var outline = BrushFromHex(_theme.LabelOutlineColor);
        var ft = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            fill,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var geometry = ft.BuildGeometry(new System.Windows.Point(0, 0));
        var bounds = geometry.Bounds;
        double offsetX = (dipWidth - bounds.Width) / 2 - bounds.X;
        double offsetY = (dipHeight - bounds.Height) / 2 - bounds.Y;

        var outlinePath = new Path {
            Data = geometry,
            Fill = Brushes.Transparent,
            Stroke = outline,
            StrokeThickness = _theme.LabelOutlineThickness * 2,
            StrokeLineJoin = PenLineJoin.Round,
        };
        Canvas.SetLeft(outlinePath, offsetX);
        Canvas.SetTop(outlinePath, offsetY);
        _canvas.Children.Add(outlinePath);

        var fillPath = new Path {
            Data = geometry,
            Fill = fill,
        };
        Canvas.SetLeft(fillPath, offsetX);
        Canvas.SetTop(fillPath, offsetY);
        _canvas.Children.Add(fillPath);
    }

    private static SolidColorBrush BrushFromHex(string hex) {
        var color = (Color)ColorConverter.ConvertFromString(hex)!;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
