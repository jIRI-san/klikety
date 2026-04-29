using System.Windows;
using System.Windows.Input;

using Klikety.Interop;
using Klikety.Services;

namespace Klikety.Overlay;

public partial class OverlayWindow : Window, IOverlayWindow {
    public event EventHandler? FocusLost;

    public OverlayWindow() {
        InitializeComponent();
        Deactivated += (_, _) => FocusLost?.Invoke(this, EventArgs.Empty);
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

    void IOverlayWindow.ClearCanvas() {
        RootCanvas.Children.Clear();
    }

    /// <summary>
    /// Returns the root Canvas for the GridRenderer to draw on.
    /// </summary>
    internal System.Windows.Controls.Canvas Canvas => RootCanvas;
}
