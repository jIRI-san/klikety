using Klikety.Config;
using Klikety.Input;

namespace Klikety.Tests;

public class LogGridKeyPolicyTests {
    private static readonly VKey[] TenKeys =
        [VKey.A, VKey.S, VKey.D, VKey.F, VKey.G, VKey.H, VKey.J, VKey.K, VKey.L, VKey.OemSemicolon];

    private static readonly VKey[] TwelveKeys =
        [VKey.A, VKey.S, VKey.D, VKey.F, VKey.G, VKey.H, VKey.J, VKey.K, VKey.L, VKey.OemSemicolon, VKey.Q, VKey.W];

    private static readonly VKey[] EightKeys =
        [VKey.A, VKey.S, VKey.D, VKey.F, VKey.G, VKey.H, VKey.J, VKey.K];

    [Fact]
    public void Evaluate_ExactlyTenKeys_Available_NoWarning() {
        var result = LogGridKeyPolicy.Evaluate(TenKeys, TenKeys);

        Assert.True(result.IsAvailable);
        Assert.Null(result.Warning);
        Assert.Equal(10, result.EffectiveHorizontalKeys.Length);
        Assert.Equal(10, result.EffectiveVerticalKeys.Length);
    }

    [Fact]
    public void Evaluate_MoreThanTenKeys_TrimsToFirstTen() {
        var result = LogGridKeyPolicy.Evaluate(TwelveKeys, TwelveKeys);

        Assert.True(result.IsAvailable);
        Assert.Equal(10, result.EffectiveHorizontalKeys.Length);
        Assert.Equal(10, result.EffectiveVerticalKeys.Length);
        Assert.Equal(TwelveKeys[..10], result.EffectiveHorizontalKeys);
        Assert.Equal(TwelveKeys[..10], result.EffectiveVerticalKeys);
        Assert.NotNull(result.Warning);
        Assert.Contains("trimmed", result.Warning);
    }

    [Fact]
    public void Evaluate_FewerThanTenHorizontal_Unavailable() {
        var result = LogGridKeyPolicy.Evaluate(EightKeys, TenKeys);

        Assert.False(result.IsAvailable);
        Assert.NotNull(result.Warning);
        Assert.Contains("unavailable", result.Warning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("horizontal", result.Warning);
        Assert.Contains("8", result.Warning);
    }

    [Fact]
    public void Evaluate_FewerThanTenVertical_Unavailable() {
        var result = LogGridKeyPolicy.Evaluate(TenKeys, EightKeys);

        Assert.False(result.IsAvailable);
        Assert.NotNull(result.Warning);
        Assert.Contains("unavailable", result.Warning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("vertical", result.Warning);
    }

    [Fact]
    public void Evaluate_BothAxesFewer_ReportsFirstInsufficient() {
        var result = LogGridKeyPolicy.Evaluate(EightKeys, EightKeys);

        Assert.False(result.IsAvailable);
        Assert.NotNull(result.Warning);
        Assert.Contains("horizontal", result.Warning);
    }

    [Fact]
    public void Evaluate_MixedTrimAndExact_OnlyTrimsLongerAxis() {
        var result = LogGridKeyPolicy.Evaluate(TwelveKeys, TenKeys);

        Assert.True(result.IsAvailable);
        Assert.Equal(10, result.EffectiveHorizontalKeys.Length);
        Assert.Equal(10, result.EffectiveVerticalKeys.Length);
        Assert.Same(TenKeys, result.EffectiveVerticalKeys);
        Assert.NotNull(result.Warning);
    }
}
