using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

using Klikety.Config;
using Klikety.Interop;
using Klikety.Services;

namespace Klikety.Overlay;

public partial class OverlayWindow : Window, IOverlayWindow {
    public event EventHandler? FocusLost;
    private ThemeModel? _theme;

    public OverlayWindow() {
        InitializeComponent();
        if (Debugger.IsAttached) {
            Topmost = false;
        }
        Deactivated += (_, _) => FocusLost?.Invoke(this, EventArgs.Empty);
    }

    internal void SetTheme(ThemeModel theme) {
        _theme = theme;
    }

    bool IOverlayWindow.IsVisible => IsVisible;

    void IOverlayWindow.Show() {
        // Size to primary screen bounds in DIPs
        var screenBounds = NativeMethods.GetPrimaryScreenBounds();

        // Must show first so the HWND exists and PresentationSource is available
        Show();

        var source = PresentationSource.FromVisual(this)
                         ?? throw new InvalidOperationException("No PresentationSource available.");
        var transform = source.CompositionTarget!.TransformFromDevice;
        var topLeft = transform.Transform(new System.Windows.Point(screenBounds.X, screenBounds.Y));
        var bottomRight = transform.Transform(new System.Windows.Point(
            screenBounds.X + screenBounds.Width,
            screenBounds.Y + screenBounds.Height));

        Left = topLeft.X;
        Top = topLeft.Y;
        Width = bottomRight.X - topLeft.X;
        Height = bottomRight.Y - topLeft.Y;

        Activate();
        Keyboard.Focus(this);
    }

    void IOverlayWindow.Hide() {
        Hide();
    }

    void IOverlayWindow.Close() {
        Close();
    }

    void IOverlayWindow.ClearCanvas() {
        RootCanvas.Children.Clear();
    }

    void IOverlayWindow.ShowStatusText(string text) {
        StatusCanvas.Children.Clear();

        var fillBrush = TryParseBrush(_theme?.StatusTextFillColor, Brushes.White);
        var outlineBrush = TryParseBrush(_theme?.StatusTextOutlineColor, Brushes.Black);
        var bgBrush = TryParseBrush(_theme?.StatusTextBackgroundColor, new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)));

        double fontSize = 28;
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        var ft = new FormattedText(text, CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, typeface, fontSize, fillBrush, dpi);
        var geometry = ft.BuildGeometry(new System.Windows.Point(0, 0));
        var bounds = geometry.Bounds;

        double padding = 16;
        double bgWidth = bounds.Width + padding * 2;
        double bgHeight = bounds.Height + padding * 2;
        double canvasWidth = ActualWidth > 0 ? ActualWidth : Width;
        double bgX = (canvasWidth - bgWidth) / 2;
        double bgY = ActualHeight * 0.05;

        // Background rectangle
        var bgRect = new System.Windows.Shapes.Rectangle {
            Width = bgWidth,
            Height = bgHeight,
            Fill = bgBrush,
            Opacity = 0.85,
            RadiusX = 6,
            RadiusY = 6,
        };
        Canvas.SetLeft(bgRect, bgX);
        Canvas.SetTop(bgRect, bgY);
        StatusCanvas.Children.Add(bgRect);

        double textX = bgX + padding - bounds.X;
        double textY = bgY + padding - bounds.Y;

        // Outline layer
        var outline = new Path {
            Data = geometry,
            Fill = Brushes.Transparent,
            Stroke = outlineBrush,
            StrokeThickness = 3,
            StrokeLineJoin = PenLineJoin.Round,
        };
        Canvas.SetLeft(outline, textX);
        Canvas.SetTop(outline, textY);
        StatusCanvas.Children.Add(outline);

        // Fill layer
        var fillPath = new Path {
            Data = geometry,
            Fill = fillBrush,
        };
        Canvas.SetLeft(fillPath, textX);
        Canvas.SetTop(fillPath, textY);
        StatusCanvas.Children.Add(fillPath);
    }

    void IOverlayWindow.ClearStatusText() {
        StatusCanvas.Children.Clear();
    }

    private static SolidColorBrush TryParseBrush(string? colorString, SolidColorBrush fallback) {
        if (colorString is null) {
            return fallback;
        }

        try {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorString));
        } catch (FormatException) {
            return fallback;
        }
    }

    /// <summary>
    /// Returns the root Canvas for the GridRenderer to draw on.
    /// </summary>
    internal System.Windows.Controls.Canvas Canvas => RootCanvas;
}
