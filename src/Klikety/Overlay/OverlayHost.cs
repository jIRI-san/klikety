using System.Drawing;

using Klikety.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Klikety.Overlay;

/// <summary>
/// One navigation overlay plus N−1 satellites. Satellites are shown first and never activated.
/// </summary>
public sealed partial class OverlayHost : IDisposable {
    private readonly IOverlayWindow _nav;
    private readonly Func<ISatelliteOverlay> _createSatellite;
    private readonly ILogger _logger;
    private readonly List<ISatelliteOverlay> _satellites = [];

    public OverlayHost(
        IOverlayWindow nav,
        Func<ISatelliteOverlay> createSatellite,
        ILogger? logger = null) {
        _nav = nav;
        _createSatellite = createSatellite;
        _logger = logger ?? NullLogger.Instance;
    }

    public int WindowCount => 1 + _satellites.Count;

    public IReadOnlyDictionary<string, int> LastNumbers { get; private set; } =
        new Dictionary<string, int>(StringComparer.Ordinal);

    private IReadOnlyList<DisplayInfo> _lastDisplays = [];
    private DisplayInfo? _lastNav;
    private bool _displayNumbersEnabled = true;

    public void Show(
        IReadOnlyList<DisplayInfo> displays,
        DisplayInfo navDisplay,
        IReadOnlyDictionary<string, int> numbers, bool displayNumbersEnabled = true) {
        HideSatellites();
        _lastDisplays = displays;
        _lastNav = navDisplay;
        _displayNumbersEnabled = displayNumbersEnabled;
        LastNumbers = new Dictionary<string, int>(numbers, StringComparer.Ordinal);
        LogHostShow(
            navDisplay.GdiName,
            navDisplay.MonitorBounds.X, navDisplay.MonitorBounds.Y,
            navDisplay.MonitorBounds.Width, navDisplay.MonitorBounds.Height,
            displays.Count);

        ShowSatellites();
        _nav.Show(navDisplay.MonitorBounds);
    }

    public void SetDisplayNumbersEnabled(bool enabled) {
        if (_displayNumbersEnabled == enabled) { return; }
        _displayNumbersEnabled = enabled;
        HideSatellites();
        if (_nav.IsVisible) { ShowSatellites(); }
    }

    private void ShowSatellites() {
        foreach (var display in _lastDisplays) {
            if (string.Equals(display.DevicePath, _lastNav?.DevicePath, StringComparison.Ordinal)) {
                continue;
            }

            int? number = _displayNumbersEnabled && LastNumbers.TryGetValue(display.DevicePath, out int n) && n is >= 1 and <= 9
                ? n
                : null;
            var satellite = _createSatellite();
            satellite.Show(display.MonitorBounds, number);
            _satellites.Add(satellite);
        }
    }

    public void ShowLast() {
        if (_lastNav is null || _lastDisplays.Count == 0) {
            _nav.Show();
            return;
        }

        Show(_lastDisplays, _lastNav, LastNumbers, _displayNumbersEnabled);
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

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Host Show nav={Gdi} {X},{Y} {W}x{H} displays={Count}")]
    private partial void LogHostShow(string gdi, int x, int y, int w, int h, int count);
}

internal sealed class NullSatelliteOverlay : ISatelliteOverlay {
    public void Show(Rectangle physicalBounds, int? number) { }
    public void Hide() { }
    public void Dispose() { }
}
