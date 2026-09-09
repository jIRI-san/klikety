using Klikety.Input;

namespace Klikety.Grid;

/// <summary>
/// Generates single-character display labels for an axis key array.
/// Used by Crosshair/LogCrosshair modes for axis annotations.
/// </summary>
public sealed class AxisLabelGenerator {
    private readonly string[] _labels;
    private readonly Dictionary<string, int> _labelToIndex;
    private readonly VKey[] _keys;

    public AxisLabelGenerator(VKey[] keys, IKeyLabelResolver resolver) {
        _keys = keys;
        _labels = new string[keys.Length];
        _labelToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        Rebuild(resolver);
    }

    /// <summary>
    /// Re-resolves axis labels while retaining the configured physical key layout.
    /// Must be called on the UI dispatcher with rendering.
    /// </summary>
    public void Rebuild(IKeyLabelResolver resolver) {
        _labelToIndex.Clear();
        for (int i = 0; i < _keys.Length; i++) {
            _labels[i] = resolver.Resolve(_keys[i]);
            _labelToIndex[_labels[i]] = i;
        }
    }

    public int Count => _labels.Length;

    public string LabelFor(int index) => _labels[index];

    public int? IndexFor(string label) =>
        _labelToIndex.TryGetValue(label, out var idx) ? idx : null;
}
