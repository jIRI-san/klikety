using Klikety.Input;

namespace Klikety.Grid;

/// <summary>
/// Generates single-character display labels for an axis key array.
/// Used by Crosshair/LogCrosshair modes for axis annotations.
/// </summary>
public sealed class AxisLabelGenerator {
    private readonly string[] _labels;
    private readonly Dictionary<string, int> _labelToIndex;

    public AxisLabelGenerator(VKey[] keys, IKeyLabelResolver resolver) {
        _labels = new string[keys.Length];
        _labelToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < keys.Length; i++) {
            _labels[i] = resolver.Resolve(keys[i]);
            _labelToIndex[_labels[i]] = i;
        }
    }

    public int Count => _labels.Length;

    public string LabelFor(int index) => _labels[index];

    public int? IndexFor(string label) =>
        _labelToIndex.TryGetValue(label, out var idx) ? idx : null;
}
