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
    public event EventHandler? KeyboardLayoutChanged;
    public event EventHandler<OverlayViewportChangedEventArgs>? ViewportChanged;
    private ThemeModel? _theme;
    private HelpOverlayContent? _helpContent;
    private bool _displayHookAdded;
    private HwndSource? _keyboardLayoutSource;
    private nint _lastKeyboardLayout;
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

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) {
        LogOverlayState("SizeChanged");
        RaiseViewportChanged();
    }

    private void OnLocationChanged(object? sender, EventArgs e) =>
        LogOverlayState("LocationChanged");

    private void OnDpiChanged(object sender, DpiChangedEventArgs e) {
        LogOverlayState("DpiChanged");
        RaiseViewportChanged();
    }

    private void RaiseViewportChanged() {
        ViewportChanged?.Invoke(
            this,
            new OverlayViewportChangedEventArgs(
                ActualWidth > 0 ? ActualWidth : Width,
                ActualHeight > 0 ? ActualHeight : Height));
    }

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
        EnsureKeyboardLayoutHook(source);
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

    private void EnsureKeyboardLayoutHook(PresentationSource source) {
        if (_keyboardLayoutSource is not null || source is not HwndSource hwndSource) {
            return;
        }

        hwndSource.AddHook(KeyboardLayoutHook);
        _keyboardLayoutSource = hwndSource;
    }

    private nint KeyboardLayoutHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled) {
        const int WM_INPUTLANGCHANGE = 0x0051;
        if (msg == WM_INPUTLANGCHANGE && lParam != _lastKeyboardLayout) {
            _lastKeyboardLayout = lParam;
            KeyboardLayoutChanged?.Invoke(this, EventArgs.Empty);
        }

        return nint.Zero;
    }

    private void RemoveKeyboardLayoutHook() {
        if (_keyboardLayoutSource is null) {
            return;
        }

        _keyboardLayoutSource.RemoveHook(KeyboardLayoutHook);
        _keyboardLayoutSource = null;
    }

    void IOverlayWindow.Hide() {
        LogOverlayState("Hide");
        ((IOverlayWindow)this).HideHelp();
        RemoveKeyboardLayoutHook();
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

    void IOverlayWindow.ShowHelp(HelpOverlayContent content) {
        _helpContent = content;
        RenderHelp();
    }

    void IOverlayWindow.UpdateHelp(HelpOverlayContent content) {
        _helpContent = content;
        RenderHelp();
    }

    void IOverlayWindow.HideHelp() {
        _helpContent = null;
        HelpCanvas.Children.Clear();
    }

    void IOverlayWindow.RelayoutHelp() => RenderHelp();

    private void RelayoutHelp() => RenderHelp();

    private void RenderHelp() {
        HelpCanvas.Children.Clear();
        if (_helpContent is null) {
            return;
        }

        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;
        var commandKeys = _helpContent.Entries.Select(entry => entry.Key);
        var baseLayout = HelpKeyboardLayout.Compute(
            width,
            height,
            commandKeys,
            _helpContent.NavigationAnchors);
        var theme = _theme ?? new ThemeModel();
        var footerWidth = Math.Max(0, baseLayout.ContentWidth - 40);
        var promptBlocks = _helpContent.Prompts
            .Select(prompt => CreateHelpTextBlock(prompt, theme.LabelColor, footerWidth))
            .ToArray();
        var promptHeights = promptBlocks.Select(GetHelpTextHeightBudget).ToArray();
        var closeBlock = CreateHelpTextBlock(_helpContent.CloseInstruction, theme.LabelColor, footerWidth);
        var closeHeight = GetHelpTextHeightBudget(closeBlock);
        var promptLines = promptHeights.Sum(height => (int)(height / HelpFooterLineHeight));
        var closeLines = (int)(closeHeight / HelpFooterLineHeight);
        var layout = HelpKeyboardLayout.Compute(
            width,
            height,
            commandKeys,
            _helpContent.NavigationAnchors,
            promptLines + closeLines - 1);
        var viewport = new ScrollViewer {
            Width = width,
            Height = height,
            HorizontalScrollBarVisibility = layout.ScrollViewport
                ? ScrollBarVisibility.Auto
                : ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = layout.ScrollViewport
                ? ScrollBarVisibility.Auto
                : ScrollBarVisibility.Disabled,
            Focusable = false,
            IsTabStop = false,
        };
        var content = new Canvas {
            Width = layout.ContentWidth,
            Height = layout.ContentHeight,
            ClipToBounds = true,
        };
        viewport.Content = content;
        HelpCanvas.Children.Add(viewport);

        content.Children.Add(new System.Windows.Shapes.Rectangle {
            Width = layout.ContentWidth,
            Height = layout.ContentHeight,
            Fill = TryParseBrush(theme.CellBackgroundColor, Brushes.Black),
            Opacity = 0.88,
        });

        var entriesByKey = _helpContent.Entries
            .GroupBy(entry => entry.Key)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var anchors = _helpContent.NavigationAnchors.ToHashSet();

        foreach (var position in layout.Positions) {
            if (entriesByKey.TryGetValue(position.Key, out var entries)) {
                AddHelpEntry(content, position, entries, theme);
            } else if (anchors.Contains(position.Key)) {
                var anchor = new Border {
                    Width = position.Width * 0.72,
                    Height = position.Height * 0.48,
                    BorderBrush = TryParseBrush(theme.CellBorderColor, Brushes.Gray),
                    BorderThickness = new Thickness(1),
                    Background = TryParseBrush(theme.DimmedOverlayColor, Brushes.Black),
                    Opacity = 0.3,
                    CornerRadius = new CornerRadius(3),
                };
                Canvas.SetLeft(anchor, position.X + position.Width * 0.14);
                Canvas.SetTop(anchor, position.Y);
                content.Children.Add(anchor);
            }
        }

        var closeY = layout.ContentHeight - closeHeight - 12;
        var footerY = closeY - 8 - promptHeights.Sum();
        for (var index = 0; index < _helpContent.Prompts.Count; index++) {
            Canvas.SetLeft(promptBlocks[index], 20);
            Canvas.SetTop(promptBlocks[index], footerY);
            content.Children.Add(promptBlocks[index]);
            footerY += promptHeights[index];
        }
        Canvas.SetLeft(closeBlock, 20);
        Canvas.SetTop(closeBlock, closeY);
        content.Children.Add(closeBlock);
    }

    private const double HelpFooterLineHeight = 18;

    private static void AddHelpEntry(
        Canvas canvas,
        HelpKeyPosition position,
        HelpOverlayEntry[] entries,
        ThemeModel theme) {
        var first = entries[0];
        var accent = HelpCategoryBrush(first.Category, theme);
        var command = string.Join(" / ", entries.Select(entry => entry.Command));
        var stack = new StackPanel {
            Width = Math.Max(position.Width, 48),
            Height = position.Height,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var keycap = new Border {
            Height = position.Height * 0.52,
            Background = TryParseBrush(theme.CellBackgroundColor, Brushes.Black),
            BorderBrush = accent,
            BorderThickness = new Thickness(1.25),
            CornerRadius = new CornerRadius(3),
            Child = new TextBlock {
                Text = first.KeyLabel,
                Foreground = TryParseBrush(theme.LabelColor, Brushes.White),
                FontFamily = new FontFamily(theme.LabelFontFamily),
                FontSize = Math.Max(12, Math.Min(theme.LabelFontSize, 15)),
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
        };
        stack.Children.Add(keycap);
        stack.Children.Add(new TextBlock {
            Text = command,
            Foreground = accent,
            FontFamily = new FontFamily(theme.LabelFontFamily),
            FontSize = HelpKeyboardLayout.MinimumCommandFontSize,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = position.Height * 0.48,
            Opacity = entries.All(entry => entry.IsAvailable) ? 1 : 0.55,
        });

        var card = new Border {
            Width = Math.Max(position.Width, 48),
            Height = position.Height,
            Child = stack,
            Opacity = entries.All(entry => entry.IsAvailable) ? 1 : 0.65,
        };
        Canvas.SetLeft(card, position.X);
        Canvas.SetTop(card, position.Y);
        canvas.Children.Add(card);
    }

    private static TextBlock CreateHelpTextBlock(string text, string color, double width) {
        var label = new TextBlock {
            Text = text,
            Width = width,
            Foreground = TryParseBrush(color, Brushes.White),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        };
        label.Measure(new System.Windows.Size(width, double.PositiveInfinity));
        return label;
    }

    private static double GetHelpTextHeightBudget(TextBlock label) =>
        Math.Max(HelpFooterLineHeight, Math.Ceiling(label.DesiredSize.Height / HelpFooterLineHeight) * HelpFooterLineHeight);

    private static SolidColorBrush HelpCategoryBrush(HelpEntryCategory category, ThemeModel theme) =>
        category switch {
            HelpEntryCategory.Action => TryParseBrush(theme.HighlightedColumnBackground, Brushes.Gold),
            HelpEntryCategory.Mode => TryParseBrush(theme.SubgridLabelColor, Brushes.DeepSkyBlue),
            HelpEntryCategory.Scope => TryParseBrush(theme.AppScopeBorderColor, Brushes.DodgerBlue),
            HelpEntryCategory.Macro => TryParseBrush(theme.ExternalRowLabelColor, Brushes.LightSkyBlue),
            HelpEntryCategory.Display => TryParseBrush(theme.ExternalColLabelColor, Brushes.Orange),
            _ => TryParseBrush(theme.LabelColor, Brushes.White),
        };

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
