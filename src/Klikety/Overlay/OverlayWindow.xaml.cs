using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

using Klikety.Config;
using Klikety.Interop;
using Klikety.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Klikety.Overlay;

public partial class OverlayWindow : Window, IOverlayWindow {
    public event EventHandler? FocusLost;
    public event EventHandler? DisplayChanged;
    private ThemeModel? _theme;
    private bool _displayHookAdded;
    private ILogger _logger = NullLogger.Instance;

    public OverlayWindow() {
        InitializeComponent();
        if (Debugger.IsAttached) {
            Topmost = false;
        }
        Deactivated += OnDeactivated;
        SizeChanged += OnSizeChanged;
        LocationChanged += OnLocationChanged;
        DpiChanged += OnDpiChanged;
    }

    internal void SetTheme(ThemeModel theme) {
        _theme = theme;
    }

    internal void SetLogger(ILogger logger) {
        _logger = logger;
    }

    private void OnDeactivated(object? sender, EventArgs e) {
        LogOverlayState("Deactivated");
        FocusLost?.Invoke(this, EventArgs.Empty);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) =>
        LogOverlayState("SizeChanged");

    private void OnLocationChanged(object? sender, EventArgs e) =>
        LogOverlayState("LocationChanged");

    private void OnDpiChanged(object sender, DpiChangedEventArgs e) =>
        LogOverlayState("DpiChanged");

    private void LogOverlayState(string reason) {
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.TryGetWindowRect(hwnd, out var wr);
        LogOverlayStateCore(
            reason, IsVisible, Left, Top, Width, Height, ActualWidth, ActualHeight,
            wr.X, wr.Y, wr.Width, wr.Height, RootCanvas.Children.Count);
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Overlay {Reason}: vis={Visible} Left={Left} Top={Top} W={Width} H={Height} Actual={ActualW}x{ActualH} hwndRect={HX},{HY} {HW}x{HH} canvas={Canvas}")]
    private partial void LogOverlayStateCore(
        string reason, bool visible, double left, double top, double width, double height,
        double actualW, double actualH, int hx, int hy, int hw, int hh, int canvas);

    [LoggerMessage(Level = LogLevel.Information, Message = "Overlay Show requested physical={X},{Y} {W}x{H}")]
    private partial void LogShowRequested(int x, int y, int w, int h);

    bool IOverlayWindow.IsVisible => IsVisible;

    void IOverlayWindow.Show() {
        ((IOverlayWindow)this).Show(NativeMethods.GetPrimaryScreenBounds());
    }

    void IOverlayWindow.Show(System.Drawing.Rectangle bounds) {
        if (bounds.Width <= 0 || bounds.Height <= 0) {
            bounds = NativeMethods.GetPrimaryScreenBounds();
        }

        LogShowRequested(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        OverlayPlacement.Place(this, bounds, activate: true, _logger);
        var source = PresentationSource.FromVisual(this)
                     ?? throw new InvalidOperationException("No PresentationSource available.");
        EnsureDisplayChangeHook(source);
        Activate();
        Keyboard.Focus(this);
        LogOverlayState("Show");
    }

    private void EnsureDisplayChangeHook(PresentationSource source) {
        if (_displayHookAdded || source is not HwndSource hwndSource) {
            return;
        }

        hwndSource.AddHook(DisplayChangeHook);
        _displayHookAdded = true;
    }

    private nint DisplayChangeHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled) {
        const int WM_DISPLAYCHANGE = 0x007E;
        if (msg == WM_DISPLAYCHANGE) {
            DisplayChanged?.Invoke(this, EventArgs.Empty);
        }

        return nint.Zero;
    }

    void IOverlayWindow.Hide() {
        LogOverlayState("Hide");
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

        var dip = OverlayDip.ToCanvas(bounds, OverlayDip.WindowOrigin(this), OverlayDip.ScaleOf(this));

        var border = new System.Windows.Shapes.Rectangle {
            Width = dip.Width,
            Height = dip.Height,
            Stroke = brush,
            StrokeThickness = 2,
            Fill = Brushes.Transparent,
            Tag = "AppScopeBorder",
        };
        Canvas.SetLeft(border, dip.X);
        Canvas.SetTop(border, dip.Y);
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
