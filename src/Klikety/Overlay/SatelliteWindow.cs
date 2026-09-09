using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

using Klikety.Config;
using Klikety.Interop;

namespace Klikety.Overlay;

/// <summary>
/// Non-activating satellite overlay. Centered outlined digit; unnumbered displays show no digit.
/// </summary>
public sealed class SatelliteWindow : Window, ISatelliteOverlay {
    private readonly Canvas _canvas = new();
    private readonly ThemeModel _theme;
    private bool _exStyleApplied;

    public SatelliteWindow(ThemeModel theme) {
        _theme = theme;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Content = _canvas;
    }

    public static double DigitFontSize(double dipWidth, double dipHeight) =>
        Math.Clamp(0.4 * Math.Min(dipWidth, dipHeight), 96, 400);

    public void Show(System.Drawing.Rectangle physicalBounds, int? number) {
        if (!_exStyleApplied) {
            Show();
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            NativeMethods.SetClickThroughExStyle(hwnd);
            _exStyleApplied = true;
        }

        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformFromDevice
            ?? Matrix.Identity;
        var topLeft = transform.Transform(new Point(physicalBounds.X, physicalBounds.Y));
        var bottomRight = transform.Transform(new Point(
            physicalBounds.X + physicalBounds.Width,
            physicalBounds.Y + physicalBounds.Height));
        Left = topLeft.X;
        Top = topLeft.Y;
        Width = Math.Max(bottomRight.X - topLeft.X, 1);
        Height = Math.Max(bottomRight.Y - topLeft.Y, 1);

        _canvas.Children.Clear();
        if (number is int digit) {
            AddDigit(digit.ToString(CultureInfo.InvariantCulture), Width, Height);
        }

        Show();
    }

    void ISatelliteOverlay.Hide() => Hide();

    public void Dispose() => Close();

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
