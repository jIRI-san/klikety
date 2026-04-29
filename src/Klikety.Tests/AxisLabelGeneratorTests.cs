using Klikety.Grid;
using Klikety.Input;
using Klikety.Tests.Fakes;

namespace Klikety.Tests;

public class AxisLabelGeneratorTests {
    private static readonly IKeyLabelResolver Resolver = new FakeKeyLabelResolver();

    [Fact]
    public void Count_MatchesKeyCount() {
        VKey[] keys = [VKey.A, VKey.S, VKey.D, VKey.F, VKey.G];
        var gen = new AxisLabelGenerator(keys, Resolver);
        Assert.Equal(5, gen.Count);
    }

    [Fact]
    public void LabelFor_ReturnsResolvedLabel() {
        VKey[] keys = [VKey.A, VKey.S];
        var gen = new AxisLabelGenerator(keys, Resolver);
        Assert.Equal("A", gen.LabelFor(0));
        Assert.Equal("S", gen.LabelFor(1));
    }

    [Fact]
    public void IndexFor_RoundTrips() {
        VKey[] keys = [VKey.Q, VKey.W, VKey.E, VKey.R, VKey.T];
        var gen = new AxisLabelGenerator(keys, Resolver);
        for (int i = 0; i < keys.Length; i++) {
            var label = gen.LabelFor(i);
            var idx = gen.IndexFor(label);
            Assert.Equal(i, idx);
        }
    }

    [Fact]
    public void IndexFor_IsCaseInsensitive() {
        VKey[] keys = [VKey.A];
        var gen = new AxisLabelGenerator(keys, Resolver);
        Assert.Equal(0, gen.IndexFor("a"));
        Assert.Equal(0, gen.IndexFor("A"));
    }

    [Fact]
    public void IndexFor_InvalidLabel_ReturnsNull() {
        VKey[] keys = [VKey.A, VKey.S];
        var gen = new AxisLabelGenerator(keys, Resolver);
        Assert.Null(gen.IndexFor("Z"));
    }

    [Fact]
    public void TenKeys_AllUnique() {
        VKey[] keys = [VKey.A, VKey.S, VKey.D, VKey.F, VKey.G, VKey.H, VKey.J, VKey.K, VKey.L, VKey.OemSemicolon];
        var gen = new AxisLabelGenerator(keys, Resolver);
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < gen.Count; i++) {
            Assert.True(labels.Add(gen.LabelFor(i)), $"Duplicate at index {i}");
        }
        Assert.Equal(10, labels.Count);
    }
}
