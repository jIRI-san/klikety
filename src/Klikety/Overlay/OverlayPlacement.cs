using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

using Klikety.Interop;

namespace Klikety.Overlay;

internal static class OverlayPlacement {
    public static void Place(Window window, System.Drawing.Rectangle physicalBounds, bool activate) {
        if (physicalBounds.Width <= 0 || physicalBounds.Height <= 0) {
            return;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        ApplyDip(window, physicalBounds);

        if (!window.IsVisible) {
            window.Show();
        }

        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        NativeMethods.SetWindowPhysicalBounds(hwnd, physicalBounds, activate);
        ApplyDip(window, physicalBounds);
        NativeMethods.SetWindowPhysicalBounds(hwnd, physicalBounds, activate);
    }

    private static void ApplyDip(Window window, System.Drawing.Rectangle physicalBounds) {
        var source = PresentationSource.FromVisual(window);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = transform.Transform(new Point(physicalBounds.X, physicalBounds.Y));
        var bottomRight = transform.Transform(new Point(
            physicalBounds.X + physicalBounds.Width,
            physicalBounds.Y + physicalBounds.Height));
        window.Left = topLeft.X;
        window.Top = topLeft.Y;
        window.Width = Math.Max(bottomRight.X - topLeft.X, 1);
        window.Height = Math.Max(bottomRight.Y - topLeft.Y, 1);
    }
}
