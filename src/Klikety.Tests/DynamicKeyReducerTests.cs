using Klikety.Grid;
using Klikety.Input;

namespace Klikety.Tests;

public class DynamicKeyReducerTests {
    private static readonly VKey[] TenKeys = [
        VKey.A, VKey.S, VKey.D, VKey.F, VKey.G,
        VKey.H, VKey.J, VKey.K, VKey.L, VKey.OemSemicolon,
    ];

    private static readonly VKey[] FourKeys = [VKey.A, VKey.S, VKey.D, VKey.F];

    // --- No reduction needed ---

    [Fact]
    public void AllKeysFit_ReturnsFullArray() {
        // 10 keys, 1000px / 50px = 20 cells, UniformGrid: maxKeys = 20 ≥ 10
        var result = DynamicKeyReducer.ComputeActiveKeys(TenKeys, 1000, 50, hasCenterCell: false);

        Assert.Equal(TenKeys, result.ActiveKeys);
        Assert.Equal(0, result.OriginalStartIndex);
        Assert.False(result.IsDisabled);
    }

    [Fact]
    public void AllKeysFit_Crosshair_ReturnsFullArray() {
        // 10 keys, Crosshair: cells = 11, 1000/50 = 20 cells fit, maxKeys = 19 ≥ 10
        var result = DynamicKeyReducer.ComputeActiveKeys(TenKeys, 1000, 50, hasCenterCell: true);

        Assert.Equal(TenKeys, result.ActiveKeys);
        Assert.Equal(0, result.OriginalStartIndex);
    }

    // --- Reduction (UniformGrid) ---

    [Fact]
    public void UniformGrid_ReducesToMaxKeys() {
        // 10 keys, 40px / 5px = 8 cells → maxKeys = 8
        // Drop 2 total, 1 from each side → start at index 1, take 8
        var result = DynamicKeyReducer.ComputeActiveKeys(TenKeys, 40, 5, hasCenterCell: false);

        Assert.Equal(8, result.ActiveKeys.Length);
        Assert.Equal(1, result.OriginalStartIndex);
        Assert.Equal(TenKeys[1..9], result.ActiveKeys);
    }

    [Fact]
    public void UniformGrid_OddDrop_LeftBiased() {
        // 10 keys, 35px / 5px = 7 cells → maxKeys = 7
        // Drop 3: dropLeft = 1, dropRight = 2 → start at 1, take 7
        var result = DynamicKeyReducer.ComputeActiveKeys(TenKeys, 35, 5, hasCenterCell: false);

        Assert.Equal(7, result.ActiveKeys.Length);
        Assert.Equal(1, result.OriginalStartIndex);
        Assert.Equal(TenKeys[1..8], result.ActiveKeys);
    }

    // --- Reduction (Crosshair with center cell) ---

    [Fact]
    public void Crosshair_RoundsToEven() {
        // 10 keys, 45px / 5px = 9 cells → maxKeys = 9 - 1 = 8 (already even)
        var result = DynamicKeyReducer.ComputeActiveKeys(TenKeys, 45, 5, hasCenterCell: true);

        Assert.Equal(8, result.ActiveKeys.Length);
        Assert.Equal(1, result.OriginalStartIndex);
    }

    [Fact]
    public void Crosshair_OddMaxKeys_RoundedDown() {
        // 10 keys, 40px / 5px = 8 cells → maxKeys = 8 - 1 = 7 → round to 6 (even)
        var result = DynamicKeyReducer.ComputeActiveKeys(TenKeys, 40, 5, hasCenterCell: true);

        Assert.Equal(6, result.ActiveKeys.Length);
        Assert.Equal(0, result.ActiveKeys.Length % 2); // Even
        Assert.Equal(2, result.OriginalStartIndex); // Centered
        Assert.Equal(TenKeys[2..8], result.ActiveKeys);
    }

    // --- Level disabled ---

    [Fact]
    public void TooSmall_ReturnsDisabled() {
        // 10 keys, 5px / 5px = 1 cell → maxKeys = 1 < 2 → disabled
        var result = DynamicKeyReducer.ComputeActiveKeys(TenKeys, 5, 5, hasCenterCell: false);

        Assert.True(result.IsDisabled);
        Assert.Empty(result.ActiveKeys);
    }

    [Fact]
    public void Crosshair_TooSmall_ReturnsDisabled() {
        // 10 keys, 10px / 5px = 2 cells → maxKeys = 2-1 = 1 → round to 0 → disabled
        var result = DynamicKeyReducer.ComputeActiveKeys(TenKeys, 10, 5, hasCenterCell: true);

        Assert.True(result.IsDisabled);
    }

    [Fact]
    public void ZeroExtent_ReturnsDisabled() {
        var result = DynamicKeyReducer.ComputeActiveKeys(TenKeys, 0, 5, hasCenterCell: false);
        Assert.True(result.IsDisabled);
    }

    [Fact]
    public void EmptyKeys_ReturnsDisabled() {
        var result = DynamicKeyReducer.ComputeActiveKeys([], 1000, 5, hasCenterCell: false);
        Assert.True(result.IsDisabled);
    }

    // --- Minimum viable ---

    [Fact]
    public void ExactlyTwoKeys_NotDisabled() {
        // 4 keys, 10px / 5px = 2 → maxKeys = 2 → just enough
        var result = DynamicKeyReducer.ComputeActiveKeys(FourKeys, 10, 5, hasCenterCell: false);

        Assert.False(result.IsDisabled);
        Assert.Equal(2, result.ActiveKeys.Length);
        Assert.Equal(1, result.OriginalStartIndex);
    }

    [Fact]
    public void Crosshair_ExactlyTwoKeys() {
        // 4 keys, 15px / 5px = 3 cells → maxKeys = 3-1 = 2 (even) → take 2
        var result = DynamicKeyReducer.ComputeActiveKeys(FourKeys, 15, 5, hasCenterCell: true);

        Assert.False(result.IsDisabled);
        Assert.Equal(2, result.ActiveKeys.Length);
        Assert.Equal(1, result.OriginalStartIndex);
    }

    // --- Per-axis asymmetry ---

    [Fact]
    public void DifferentAxes_IndependentReduction() {
        // Horizontal: 10 keys, 1920px wide parent → no reduction
        var hResult = DynamicKeyReducer.ComputeActiveKeys(TenKeys, 1920, 5, hasCenterCell: false);
        Assert.Equal(10, hResult.ActiveKeys.Length);

        // Vertical: 10 keys, 30px tall parent → reduced
        var vResult = DynamicKeyReducer.ComputeActiveKeys(TenKeys, 30, 5, hasCenterCell: false);
        Assert.Equal(6, vResult.ActiveKeys.Length);
        Assert.Equal(2, vResult.OriginalStartIndex);
    }

    // --- Resolution integration ---

    [Fact]
    public void TypicalL2_1080p_NoReduction() {
        // L2 cell on 1080p: ~192px (1920/10), 10 keys, 5px min → 192/5 = 38 → all fit
        var result = DynamicKeyReducer.ComputeActiveKeys(TenKeys, 192, 5, hasCenterCell: false);

        Assert.Equal(10, result.ActiveKeys.Length);
    }

    [Fact]
    public void TypicalL3_1080p_ReducesModestly() {
        // L3 cell on 1080p: ~19px (192/10), 10 keys, 5px min → 19/5 = 3 → maxKeys = 3
        var result = DynamicKeyReducer.ComputeActiveKeys(TenKeys, 19, 5, hasCenterCell: false);

        Assert.Equal(3, result.ActiveKeys.Length);
        Assert.Equal(3, result.OriginalStartIndex); // Centered: drop 3.5 → 3 left
    }
}
