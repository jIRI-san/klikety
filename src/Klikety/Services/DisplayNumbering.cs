namespace Klikety.Services;

/// <summary>
/// Spatial 1..N assignment for an unknown topology fingerprint.
/// Sort key is (Left, Top, DevicePath). Fingerprint persistence is owned by the topology store.
/// </summary>
public static class DisplayNumbering {
    public static IReadOnlyDictionary<string, int> AssignSpatially(IReadOnlyList<DisplayInfo> displays) {
        var ordered = displays
            .OrderBy(d => d.MonitorBounds.Left)
            .ThenBy(d => d.MonitorBounds.Top)
            .ThenBy(d => d.DevicePath, StringComparer.Ordinal)
            .ToArray();

        var numbers = new Dictionary<string, int>(displays.Count, StringComparer.Ordinal);
        for (int i = 0; i < ordered.Length; i++) {
            numbers[ordered[i].DevicePath] = i + 1;
        }

        return numbers;
    }
}
