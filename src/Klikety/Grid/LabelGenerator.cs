using Klikety.Input;

namespace Klikety.Grid;

/// <summary>
/// A two-part display label for a grid cell, keeping the first-key and
/// second-key characters separate for independent rendering control.
/// </summary>
public readonly record struct CellLabel(string First, string Second) {
    public override string ToString() => $"{First}{Second}";
}

/// <summary>
/// Generates display labels for grid cells by translating VKey codes
/// to characters using an <see cref="IKeyLabelResolver"/>.
/// Provides bidirectional lookup: (row, col) → label and label → (row, col).
/// </summary>
public sealed class LabelGenerator {
    private readonly CellLabel[,] _labels;
    private readonly Dictionary<string, (int Row, int Col)> _labelToCell;
    private readonly int _rows;
    private readonly int _cols;

    /// <summary>
    /// Creates a label generator for the given key sets.
    /// </summary>
    /// <param name="firstKeys">Column keys (first-key set).</param>
    /// <param name="secondKeys">Row keys (second-key set).</param>
    /// <param name="resolver">Resolves VKey to display character.</param>
    public LabelGenerator(VKey[] firstKeys, VKey[] secondKeys, IKeyLabelResolver resolver) {
        _cols = firstKeys.Length;
        _rows = secondKeys.Length;
        _labels = new CellLabel[_rows, _cols];
        _labelToCell = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase);

        for (int row = 0; row < _rows; row++) {
            for (int col = 0; col < _cols; col++) {
                string first = resolver.Resolve(firstKeys[col]);
                string second = resolver.Resolve(secondKeys[row]);

                var label = new CellLabel(first, second);
                _labels[row, col] = label;
                _labelToCell[label.ToString()] = (row, col);
            }
        }
    }

    /// <summary>
    /// Gets the display label for the cell at (row, col).
    /// </summary>
    public CellLabel LabelFor(int row, int col) => _labels[row, col];

    /// <summary>
    /// Looks up the (row, col) for a display label. Returns null if not found.
    /// </summary>
    public (int Row, int Col)? CellFor(string label) {
        return _labelToCell.TryGetValue(label, out var cell) ? cell : null;
    }

    public int Rows => _rows;
    public int Cols => _cols;
}
