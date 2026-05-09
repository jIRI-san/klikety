using System.Windows;
using System.Windows.Media;

using Klikety.Interop;

namespace Klikety.Services;

/// <summary>
/// Production IMonitorService that uses Win32 APIs to determine the active monitor's work area.
/// Returns coordinates in DIPs via PresentationSource device-to-DIP transform.
/// </summary>
public sealed class MonitorService : IMonitorService {
    private readonly Visual _visual;

    /// <param name="visual">A WPF visual used to obtain the DPI transform matrix.</param>
    public MonitorService(Visual visual) {
        _visual = visual;
    }

    public Rect GetActiveMonitorWorkArea() {
        var physicalRect = NativeMethods.GetForegroundMonitorWorkArea();

        var source = PresentationSource.FromVisual(_visual);
        if (source?.CompositionTarget is null) {
            // Fallback: assume 96 DPI (1:1 mapping)
            return new Rect(physicalRect.X, physicalRect.Y, physicalRect.Width, physicalRect.Height);
        }

        var transform = source.CompositionTarget.TransformFromDevice;
        var topLeft = transform.Transform(new Point(physicalRect.X, physicalRect.Y));
        var bottomRight = transform.Transform(new Point(
            physicalRect.X + physicalRect.Width,
            physicalRect.Y + physicalRect.Height));

        return new Rect(topLeft, bottomRight);
    }
}
