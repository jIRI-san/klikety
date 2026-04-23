using Klikety.Input;
using Klikety.Interop;

namespace Klikety.Grid;

/// <summary>
/// Generates display labels for grid cells by translating VKey codes
/// to characters using the active keyboard layout (HKL).
/// Provides bidirectional lookup: (row, col) → label and label → (row, col).
/// </summary>
public sealed class LabelGenerator
{
    private readonly string[,] _labels;
    private readonly Dictionary<string, (int Row, int Col)> _labelToCell;
    private readonly int _rows;
    private readonly int _cols;

    /// <summary>
    /// Creates a label generator for the given key sets.
    /// </summary>
    /// <param name="firstKeys">Column keys (first-key set).</param>
    /// <param name="secondKeys">Row keys (second-key set).</param>
    /// <param name="hkl">Keyboard layout handle. Pass IntPtr.Zero to use the current thread's HKL.</param>
    public LabelGenerator(VKey[] firstKeys, VKey[] secondKeys, nint hkl = 0)
    {
        if (hkl == 0)
            hkl = NativeMethods.GetActiveKeyboardLayout();

        _cols = firstKeys.Length;
        _rows = secondKeys.Length;
        _labels = new string[_rows, _cols];
        _labelToCell = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase);

        for (int row = 0; row < _rows; row++)
        {
            for (int col = 0; col < _cols; col++)
            {
                var ch1 = NativeMethods.VKeyToChar((uint)firstKeys[col], hkl);
                var ch2 = NativeMethods.VKeyToChar((uint)secondKeys[row], hkl);

                // Fall back to VKey name if ToUnicode fails (dead key, unmapped)
                string label1 = ch1.HasValue ? char.ToUpper(ch1.Value).ToString() : firstKeys[col].ToString();
                string label2 = ch2.HasValue ? char.ToUpper(ch2.Value).ToString() : secondKeys[row].ToString();

                string label = $"{label1}{label2}";
                _labels[row, col] = label;
                _labelToCell[label] = (row, col);
            }
        }
    }

    /// <summary>
    /// Gets the display label for the cell at (row, col).
    /// </summary>
    public string LabelFor(int row, int col) => _labels[row, col];

    /// <summary>
    /// Looks up the (row, col) for a display label. Returns null if not found.
    /// </summary>
    public (int Row, int Col)? CellFor(string label)
    {
        return _labelToCell.TryGetValue(label, out var cell) ? cell : null;
    }

    public int Rows => _rows;
    public int Cols => _cols;
}
