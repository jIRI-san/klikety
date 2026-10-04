using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

using Klikety.Config;
using Klikety.Interop;
using Klikety.Services;

namespace Klikety.Overlay;

/// <summary>
/// Non-activating transparent window that shows a shrinking circle at a screen position
/// before a macro playback click executes. The circle animates from initialRadius to
/// finalRadius, then fires Completed and hides itself.
/// </summary>
public sealed class ClickIndicatorWindow : Window, IClickIndicatorView {
    private readonly Ellipse _circle;
    private readonly PlaybackIndicatorConfig _config;
    private bool _exStyleApplied;

    private DoubleAnimation? _widthAnimation;
    private EventHandler? _completionHandler;

    public ClickIndicatorWindow(PlaybackIndicatorConfig config) {
        _config = config;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;

        var diameter = config.InitialRadius * 2;
        Width = diameter;
        Height = diameter;

        _circle = new Ellipse {
            Fill = BrushFromHex(config.FillColor),
            Stroke = BrushFromHex(config.StrokeColor),
            StrokeThickness = config.StrokeThickness,
            Width = diameter,
            Height = diameter,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Content = _circle;
    }

    /// <summary>
    /// Shows the indicator centered at the given physical screen coordinates,
    /// animates the shrink, then fires Completed and hides.
    /// </summary>
    void IClickIndicatorView.Start(double screenX, double screenY, Action completed) {
        // Apply non-activating style on first show (needs HWND)
        if (!_exStyleApplied) {
            Show();
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            NativeMethods.SetClickThroughExStyle(hwnd);
            _exStyleApplied = true;
        }

        // Convert physical pixels to DIPs
        var source = PresentationSource.FromVisual(this);
        var dpiScaleX = source?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
        var dpiScaleY = source?.CompositionTarget?.TransformFromDevice.M22 ?? 1.0;

        var dipX = screenX * dpiScaleX;
        var dipY = screenY * dpiScaleY;

        var initialDiameter = _config.InitialRadius * 2;

        // Window stays at full size so the circle + stroke is never clipped.
        // The ellipse is centered inside via layout alignment.
        Left = dipX - _config.InitialRadius;
        Top = dipY - _config.InitialRadius;
        Width = initialDiameter;
        Height = initialDiameter;

        // Clear any leftover animations from previous ShowAt calls
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        _circle.BeginAnimation(WidthProperty, null);
        _circle.BeginAnimation(HeightProperty, null);

        _circle.Width = initialDiameter;
        _circle.Height = initialDiameter;

        Show();

        var duration = new Duration(TimeSpan.FromMilliseconds(_config.AnimationDurationMs));
        var finalDiameter = _config.FinalRadius * 2;

        var widthAnim = new DoubleAnimation(initialDiameter, finalDiameter, duration);
        var heightAnim = new DoubleAnimation(initialDiameter, finalDiameter, duration);

        _widthAnimation = widthAnim;
        _completionHandler = (_, _) => completed();
        widthAnim.Completed += _completionHandler;

        _circle.BeginAnimation(WidthProperty, widthAnim);
        _circle.BeginAnimation(HeightProperty, heightAnim);
    }

    void IClickIndicatorView.StopAndHide() {
        if (_widthAnimation is not null && _completionHandler is not null) {
            _widthAnimation.Completed -= _completionHandler;
        }
        _widthAnimation = null;
        _completionHandler = null;
        _circle.BeginAnimation(WidthProperty, null);
        _circle.BeginAnimation(HeightProperty, null);
        Hide();
    }

    private static SolidColorBrush BrushFromHex(string hex) {
        try {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        } catch {
            return Brushes.Red;
        }
    }
}

/// <summary>
/// Bridges <see cref="ClickIndicatorWindow"/> to <see cref="IClickIndicator"/>
/// by dispatching to the UI thread and awaiting animation completion.
/// </summary>
public sealed class ClickIndicatorAdapter : IClickIndicator {
    private sealed class WindowDispatcher(ClickIndicatorWindow window) : IIndicatorDispatcher {
        public void Post(Action action) => window.Dispatcher.BeginInvoke(action);
    }

    private readonly ClickIndicatorLifecycle _lifecycle;

    public ClickIndicatorAdapter(ClickIndicatorWindow window) =>
        _lifecycle = new ClickIndicatorLifecycle(new WindowDispatcher(window), window);

    public Task ShowAndWait(double screenX, double screenY, CancellationToken ct) =>
        _lifecycle.ShowAndWait(screenX, screenY, ct);

    public void Dispose() => _lifecycle.Dispose();
}
