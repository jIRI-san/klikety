using System.Windows;

namespace Klikety.Services;

/// <summary>
/// Provides the work area of the monitor containing the active foreground window.
/// Returns coordinates in DIPs for direct use with WPF positioning.
/// </summary>
public interface IMonitorService {
    Rect GetActiveMonitorWorkArea();
}
