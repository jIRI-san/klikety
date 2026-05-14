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
        ((IOverlayWindow)this).Show(NativeMethods.GetPrimaryScreenBounds());
    }

    void IOverlayWindow.Show(System.Drawing.Rectangle bounds) {
        if (bounds.Width <= 0 || bounds.Height <= 0) {
            bounds = NativeMethods.GetPrimaryScreenBounds();
        }

        var source = PresentationSource.FromVisual(this);

        if (source is not null) {
            // Window was previously shown — pre-set position/size to prevent flash at old bounds.
            // Hide() shrank the window to 1×1 offscreen, so resizing back forces WPF to
            // allocate a fresh (blank) render target — no stale content to flash.
            var pre = source.CompositionTarget!.TransformFromDevice;
            var preTopLeft = pre.Transform(new System.Windows.Point(bounds.X, bounds.Y));
            var preBottomRight = pre.Transform(new System.Windows.Point(
                bounds.X + bounds.Width, bounds.Y + bounds.Height));
            Left = preTopLeft.X;
            Top = preTopLeft.Y;
            Width = preBottomRight.X - preTopLeft.X;
            Height = preBottomRight.Y - preTopLeft.Y;

            Show();
        } else {
            // First show — need WPF Show() to create PresentationSource
            Show();

            source = PresentationSource.FromVisual(this)
                     ?? throw new InvalidOperationException("No PresentationSource available.");
            var transform = source.CompositionTarget!.TransformFromDevice;
            var topLeft = transform.Transform(new System.Windows.Point(bounds.X, bounds.Y));
            var bottomRight = transform.Transform(new System.Windows.Point(
                bounds.X + bounds.Width, bounds.Y + bounds.Height));

            Left = topLeft.X;
            Top = topLeft.Y;
            Width = bottomRight.X - topLeft.X;
            Height = bottomRight.Y - topLeft.Y;
        }

        Activate();
        Keyboard.Focus(this);
    }

    void IOverlayWindow.Hide() {
        RootCanvas.Children.Clear();
        StatusCanvas.Children.Clear();
        RecordingBorder.Visibility = Visibility.Collapsed;
        // Shrink to 1×1 offscreen before hiding. This forces WPF to discard the
        // full-screen render target. On next Show(), a fresh target is allocated
        // (starting blank), eliminating the DWM stale-surface flash.
        Left = -1;
        Top = -1;
        Width = 1;
        Height = 1;
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

    void IOverlayWindow.SetRecordingBorder(bool visible) {
        RecordingBorder.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    void IOverlayWindow.SetAppScopeBorder(bool visible, System.Drawing.Rectangle bounds) {
        // Remove any existing app-scope border from StatusCanvas
        for (int i = StatusCanvas.Children.Count - 1; i >= 0; i--) {
            if (StatusCanvas.Children[i] is System.Windows.Shapes.Rectangle r && r.Tag is "AppScopeBorder") {
                StatusCanvas.Children.RemoveAt(i);
            }
        }

        if (!visible) {
            return;
        }

        var colorStr = _theme?.AppScopeBorderColor ?? "#4488FF";
        var brush = TryParseBrush(colorStr, new SolidColorBrush(Color.FromRgb(0x44, 0x88, 0xFF)));

        // Convert physical-pixel bounds to DIP coordinates on the full-screen canvas
        var source = PresentationSource.FromVisual(this);
        double bx = bounds.X, by = bounds.Y, bw = bounds.Width, bh = bounds.Height;
        if (source?.CompositionTarget is not null) {
            var t = source.CompositionTarget.TransformFromDevice;
            var tl = t.Transform(new System.Windows.Point(bounds.X, bounds.Y));
            var br = t.Transform(new System.Windows.Point(bounds.Right, bounds.Bottom));
            bx = tl.X;
            by = tl.Y;
            bw = br.X - tl.X;
            bh = br.Y - tl.Y;
        }

        var border = new System.Windows.Shapes.Rectangle {
            Width = bw,
            Height = bh,
            Stroke = brush,
            StrokeThickness = 2,
            Fill = Brushes.Transparent,
            Tag = "AppScopeBorder",
        };
        Canvas.SetLeft(border, bx);
        Canvas.SetTop(border, by);
        StatusCanvas.Children.Add(border);
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
