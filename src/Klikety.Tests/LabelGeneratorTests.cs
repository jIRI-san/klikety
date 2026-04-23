using Klikety.Grid;
using Klikety.Input;

namespace Klikety.Tests;

public class LabelGeneratorTests
{
    private static readonly VKey[] DefaultFirstKeys = [VKey.A, VKey.S, VKey.D, VKey.F, VKey.G, VKey.H, VKey.J, VKey.K, VKey.L];
    private static readonly VKey[] DefaultSecondKeys = [VKey.Q, VKey.W, VKey.E, VKey.R, VKey.T, VKey.Y, VKey.U, VKey.I];

    [Fact]
    public void LabelCount_MatchesRowsTimesCols()
    {
        var gen = new LabelGenerator(DefaultFirstKeys, DefaultSecondKeys);
        Assert.Equal(DefaultFirstKeys.Length, gen.Cols);
        Assert.Equal(DefaultSecondKeys.Length, gen.Rows);
    }

    [Fact]
    public void AllLabelsUnique()
    {
        var gen = new LabelGenerator(DefaultFirstKeys, DefaultSecondKeys);
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int r = 0; r < gen.Rows; r++)
            for (int c = 0; c < gen.Cols; c++)
                Assert.True(labels.Add(gen.LabelFor(r, c).ToString()), $"Duplicate label at ({r},{c}): {gen.LabelFor(r, c)}");
    }

    [Fact]
    public void CellFor_RoundTrips()
    {
        var gen = new LabelGenerator(DefaultFirstKeys, DefaultSecondKeys);
        for (int r = 0; r < gen.Rows; r++)
        {
            for (int c = 0; c < gen.Cols; c++)
            {
                var label = gen.LabelFor(r, c);
                var cell = gen.CellFor(label.ToString());
                Assert.NotNull(cell);
                Assert.Equal(r, cell.Value.Row);
                Assert.Equal(c, cell.Value.Col);
            }
        }
    }

    [Fact]
    public void CellFor_IsCaseInsensitive()
    {
        var gen = new LabelGenerator(DefaultFirstKeys, DefaultSecondKeys);
        var label = gen.LabelFor(0, 0);
        var lower = gen.CellFor(label.ToString().ToLowerInvariant());
        var upper = gen.CellFor(label.ToString().ToUpperInvariant());
        Assert.Equal(lower, upper);
    }

    [Fact]
    public void CellFor_InvalidLabel_ReturnsNull()
    {
        var gen = new LabelGenerator(DefaultFirstKeys, DefaultSecondKeys);
        Assert.Null(gen.CellFor("ZZ"));
    }

    [Fact]
    public void Label_HasFirstAndSecondParts()
    {
        var gen = new LabelGenerator(DefaultFirstKeys, DefaultSecondKeys);
        var label = gen.LabelFor(0, 0);
        Assert.False(string.IsNullOrEmpty(label.First));
        Assert.False(string.IsNullOrEmpty(label.Second));
        Assert.Equal(label.First + label.Second, label.ToString());
    }

    [Fact]
    public void SmallGrid_3x2()
    {
        VKey[] first = [VKey.A, VKey.S, VKey.D];
        VKey[] second = [VKey.W, VKey.E];
        var gen = new LabelGenerator(first, second);
        Assert.Equal(3, gen.Cols);
        Assert.Equal(2, gen.Rows);

        // All 6 labels unique
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int r = 0; r < gen.Rows; r++)
            for (int c = 0; c < gen.Cols; c++)
                Assert.True(labels.Add(gen.LabelFor(r, c).ToString()));
        Assert.Equal(6, labels.Count);
    }
}
