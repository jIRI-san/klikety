using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

using Klikety.Interop;

using Microsoft.Extensions.Logging;

namespace Klikety.Overlay;

internal static partial class OverlayPlacement {
    public static void Place(
        Window window,
        System.Drawing.Rectangle physicalBounds,
        bool activate,
        ILogger? logger = null) {
        if (physicalBounds.Width <= 0 || physicalBounds.Height <= 0) {
            return;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;

        if (!window.IsVisible) {
            window.Show();
        }

        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        NativeMethods.SetWindowPhysicalBounds(hwnd, physicalBounds, activate);
        ApplyDipSizeOnly(window, physicalBounds);
        NativeMethods.SetWindowPhysicalBounds(hwnd, physicalBounds, activate);
        NativeMethods.TryGetWindowRect(hwnd, out var wr);
        if (logger is not null) {
            LogPlace(
                logger, window.GetType().Name,
                physicalBounds.X, physicalBounds.Y, physicalBounds.Width, physicalBounds.Height,
                window.Left, window.Top, window.Width, window.Height,
                wr.X, wr.Y, wr.Width, wr.Height, activate);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Place {Window} physical={PX},{PY} {PW}x{PH} dip={Left},{Top} {DW}x{DH} hwnd={HX},{HY} {HW}x{HH} activate={Activate}")]
    private static partial void LogPlace(
        ILogger logger, string window, int px, int py, int pw, int ph,
        double left, double top, double dw, double dh,
        int hx, int hy, int hw, int hh, bool activate);

    private static void ApplyDipSizeOnly(Window window, System.Drawing.Rectangle physicalBounds) {
        var scale = OverlayDip.ScaleOf(window);
        window.Width = Math.Max(physicalBounds.Width * scale.M11, 1);
        window.Height = Math.Max(physicalBounds.Height * scale.M22, 1);
    }
}
