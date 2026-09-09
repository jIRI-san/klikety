namespace Klikety.Services;

/// <summary>
/// Spatial 1..N assignment and fingerprint lookup for display numbers.
/// Fingerprint is ordinal-sorted unique DevicePaths. Navigation-host display is not part of it.
/// </summary>
public static class DisplayNumbering {
    public const int MaxNumbered = 9;

    public static string[] Fingerprint(IReadOnlyList<DisplayInfo> displays) =>
        displays.Select(d => d.DevicePath)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyDictionary<string, int> AssignSpatially(IReadOnlyList<DisplayInfo> displays) {
        var ordered = displays
            .OrderBy(d => d.MonitorBounds.Left)
            .ThenBy(d => d.MonitorBounds.Top)
            .ThenBy(d => d.DevicePath, StringComparer.Ordinal)
            .ToArray();

        int count = Math.Min(ordered.Length, MaxNumbered);
        var numbers = new Dictionary<string, int>(count, StringComparer.Ordinal);
        for (int i = 0; i < count; i++) {
            numbers[ordered[i].DevicePath] = i + 1;
        }

        return numbers;
    }

    public static IReadOnlyDictionary<string, int> Resolve(
        IReadOnlyList<DisplayInfo> displays,
        IEnumerable<(string[] Fingerprint, IReadOnlyDictionary<string, int> Numbers)> known,
        out bool isNew) {
        var fingerprint = Fingerprint(displays);
        foreach (var entry in known) {
            if (SameFingerprint(fingerprint, entry.Fingerprint)) {
                isNew = false;
                return RestrictToCurrent(entry.Numbers, displays);
            }
        }

        isNew = true;
        return AssignSpatially(displays);
    }

    internal static bool SameFingerprint(string[] left, string[] right) {
        if (left.Length != right.Length) {
            return false;
        }

        var a = left.OrderBy(p => p, StringComparer.Ordinal).ToArray();
        var b = right.OrderBy(p => p, StringComparer.Ordinal).ToArray();
        return a.SequenceEqual(b, StringComparer.Ordinal);
    }

    private static Dictionary<string, int> RestrictToCurrent(
        IReadOnlyDictionary<string, int> stored,
        IReadOnlyList<DisplayInfo> displays) {
        var current = new HashSet<string>(displays.Select(d => d.DevicePath), StringComparer.Ordinal);
        var numbers = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pair in stored) {
            if (current.Contains(pair.Key) && pair.Value is >= 1 and <= MaxNumbered) {
                numbers[pair.Key] = pair.Value;
            }
        }

        return numbers;
    }
}
