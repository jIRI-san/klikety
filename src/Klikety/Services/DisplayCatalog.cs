using System.Drawing;

using Klikety.Interop;

namespace Klikety.Services;

/// <summary>
/// Production catalog: <c>EnumDisplayMonitors</c> + CCD DevicePath. Match by GDI name. Fail closed.
/// </summary>
public sealed class DisplayCatalog : IDisplayCatalog {
    public static DisplayCatalog Instance { get; } = new();

    public DisplayCatalogResult GetSnapshot() {
        if (!NativeMethods.TryEnumerateMonitors(out var monitors)) {
            return DisplayCatalogResult.Fail("EnumDisplayMonitors failed or returned no monitors");
        }

        if (!NativeMethods.TryQueryActiveCcdPaths(out var ccdPaths, out string ccdFailure)) {
            return DisplayCatalogResult.Fail(ccdFailure);
        }

        var enumerated = new EnumeratedMonitor[monitors.Length];
        for (int i = 0; i < monitors.Length; i++) {
            enumerated[i] = new EnumeratedMonitor(
                monitors[i].GdiName,
                monitors[i].MonitorBounds,
                monitors[i].DpiScale);
        }

        var ccd = new CcdTarget[ccdPaths.Length];
        for (int i = 0; i < ccdPaths.Length; i++) {
            ccd[i] = new CcdTarget(ccdPaths[i].GdiName, ccdPaths[i].DevicePath);
        }

        return TryMatch(enumerated, ccd, NativeMethods.GetVirtualScreenBounds());
    }

    internal readonly record struct EnumeratedMonitor(
        string GdiName,
        Rectangle MonitorBounds,
        double DpiScale);

    internal readonly record struct CcdTarget(string GdiName, string DevicePath);

    /// <summary>
    /// Pair enumerated monitors to CCD targets by GDI name. No guessed pairings.
    /// </summary>
    internal static DisplayCatalogResult TryMatch(
        IReadOnlyList<EnumeratedMonitor> monitors,
        IReadOnlyList<CcdTarget> ccdTargets,
        Rectangle virtualScreen) {
        if (monitors.Count != ccdTargets.Count) {
            return DisplayCatalogResult.Fail(
                $"CCD active count ({ccdTargets.Count}) does not match EnumDisplayMonitors count ({monitors.Count})");
        }

        var devicePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in ccdTargets) {
            if (string.IsNullOrWhiteSpace(target.DevicePath)) {
                return DisplayCatalogResult.Fail("CCD DevicePath is empty");
            }

            if (!devicePaths.Add(target.DevicePath)) {
                return DisplayCatalogResult.Fail("CCD DevicePath is duplicated");
            }
        }

        var byGdiName = new Dictionary<string, CcdTarget>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in ccdTargets) {
            if (string.IsNullOrWhiteSpace(target.GdiName)) {
                return DisplayCatalogResult.Fail("CCD source GDI name is empty");
            }

            if (!byGdiName.TryAdd(target.GdiName, target)) {
                return DisplayCatalogResult.Fail("CCD source GDI name is duplicated");
            }
        }

        var displays = new DisplayInfo[monitors.Count];
        for (int i = 0; i < monitors.Count; i++) {
            var monitor = monitors[i];
            if (string.IsNullOrWhiteSpace(monitor.GdiName)) {
                return DisplayCatalogResult.Fail("Monitor GDI name is empty");
            }

            if (!byGdiName.TryGetValue(monitor.GdiName, out var target)) {
                return DisplayCatalogResult.Fail($"No CCD path for GDI name '{monitor.GdiName}'");
            }

            displays[i] = new DisplayInfo(
                monitor.MonitorBounds,
                monitor.DpiScale,
                monitor.GdiName,
                target.DevicePath);
        }

        return DisplayCatalogResult.Ok(new DisplaySnapshot(displays, virtualScreen));
    }
}
