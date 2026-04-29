using Klikety.Input;

namespace Klikety.Grid;

/// <summary>
/// Result of dynamic key reduction: the subset of keys to use and
/// where they start in the original array (for absolute offset mapping).
/// </summary>
public sealed record DynamicKeyReduction(VKey[] ActiveKeys, int OriginalStartIndex) {
    /// <summary>True when fewer than 2 keys remain — level should be disabled.</summary>
    public bool IsDisabled => ActiveKeys.Length < 2;
}

/// <summary>
/// At L2/L3, outermost keys are dropped from both sides (centered) to keep
/// cells ≥ minCellPx. Formula-driven per axis independently.
/// </summary>
public static class DynamicKeyReducer {
    /// <summary>
    /// Computes the centered subset of keys that fits within the parent extent
    /// while keeping each cell at least <paramref name="minCellPx"/> pixels.
    /// </summary>
    /// <param name="fullKeys">Full key array for this axis.</param>
    /// <param name="parentExtentPx">Parent cell extent in pixels for this axis.</param>
    /// <param name="minCellPx">Minimum cell size in pixels.</param>
    /// <param name="hasCenterCell">
    /// True for Crosshair grids (center cell exists → cells = keys + 1).
    /// False for UniformGrid (cells = keys).
    /// </param>
    public static DynamicKeyReduction ComputeActiveKeys(
        VKey[] fullKeys, int parentExtentPx, int minCellPx, bool hasCenterCell) {
        if (fullKeys.Length == 0 || parentExtentPx <= 0 || minCellPx <= 0) {
            return new DynamicKeyReduction([], 0);
        }

        // How many cells can fit?
        int maxCells = parentExtentPx / minCellPx;

        // For Crosshair: cells = keys + 1 (center cell). So maxKeys = maxCells - 1.
        // For UniformGrid: cells = keys. So maxKeys = maxCells.
        int maxKeys = hasCenterCell ? maxCells - 1 : maxCells;

        // Crosshair: round to even for center stability
        if (hasCenterCell && maxKeys > 0 && maxKeys % 2 != 0) {
            maxKeys--;
        }

        // Clamp to available keys
        if (maxKeys >= fullKeys.Length) {
            return new DynamicKeyReduction(fullKeys, 0);
        }

        // Level disabled
        if (maxKeys < 2) {
            return new DynamicKeyReduction([], 0);
        }

        // Take centered subset: drop equal from both sides
        int drop = fullKeys.Length - maxKeys;
        int dropLeft = drop / 2;
        int startIndex = dropLeft;

        var activeKeys = fullKeys[startIndex..(startIndex + maxKeys)];
        return new DynamicKeyReduction(activeKeys, startIndex);
    }
}
