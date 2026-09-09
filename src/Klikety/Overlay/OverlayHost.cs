using System.Drawing;

using Klikety.Services;

namespace Klikety.Overlay;

/// <summary>
/// One navigation overlay plus N−1 satellites. Satellites are shown first and never activated.
/// </summary>
public sealed class OverlayHost : IDisposable {
    private readonly IOverlayWindow _nav;
    private readonly Func<ISatelliteOverlay> _createSatellite;
    private readonly List<ISatelliteOverlay> _satellites = [];

    public OverlayHost(IOverlayWindow nav, Func<ISatelliteOverlay> createSatellite) {
        _nav = nav;
        _createSatellite = createSatellite;
    }

    public int WindowCount => 1 + _satellites.Count;

    public IReadOnlyDictionary<string, int> LastNumbers { get; private set; } =
        new Dictionary<string, int>(StringComparer.Ordinal);

    private IReadOnlyList<DisplayInfo> _lastDisplays = [];
    private DisplayInfo? _lastNav;

    public void Show(
        IReadOnlyList<DisplayInfo> displays,
        DisplayInfo navDisplay,
        IReadOnlyDictionary<string, int> numbers) {
        HideSatellites();
        _lastDisplays = displays;
        _lastNav = navDisplay;
        LastNumbers = new Dictionary<string, int>(numbers, StringComparer.Ordinal);

        foreach (var display in displays) {
            if (string.Equals(display.DevicePath, navDisplay.DevicePath, StringComparison.Ordinal)) {
                continue;
            }

            int? number = numbers.TryGetValue(display.DevicePath, out int n) && n is >= 1 and <= 9
                ? n
                : null;
            var satellite = _createSatellite();
            satellite.Show(display.MonitorBounds, number);
            _satellites.Add(satellite);
        }

        _nav.Show(navDisplay.MonitorBounds);
    }

    public void ShowLast() {
        if (_lastNav is null || _lastDisplays.Count == 0) {
            _nav.Show();
            return;
        }

        Show(_lastDisplays, _lastNav, LastNumbers);
    }

    public void Hide() {
        HideSatellites();
        _nav.Hide();
    }

    public void Dispose() {
        HideSatellites();
    }

    private void HideSatellites() {
        foreach (var satellite in _satellites) {
            satellite.Hide();
            satellite.Dispose();
        }

        _satellites.Clear();
    }
}

internal sealed class NullSatelliteOverlay : ISatelliteOverlay {
    public void Show(Rectangle physicalBounds, int? number) { }
    public void Hide() { }
    public void Dispose() { }
}
