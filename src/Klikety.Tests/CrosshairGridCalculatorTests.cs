using System.Drawing;

using Klikety.Grid;

namespace Klikety.Tests;

public class CrosshairGridCalculatorTests {
    // --- Basic grid structure ---

    [Fact]
    public void TenKeys_Creates11x11Grid() {
        var grid = CrosshairGridCalculator.Calculate(
            new Rectangle(0, 0, 1920, 1080), horizKeyCount: 10, vertKeyCount: 10);

        Assert.Equal(11, grid.Cols);
        Assert.Equal(11, grid.Rows);
        Assert.Equal(121, grid.Cells.Count);
    }

    [Fact]
    public void FourKeys_Creates5x5Grid() {
        var grid = CrosshairGridCalculator.Calculate(
            new Rectangle(0, 0, 1000, 1000), horizKeyCount: 4, vertKeyCount: 4);

        Assert.Equal(5, grid.Cols);
        Assert.Equal(5, grid.Rows);
        Assert.Equal(25, grid.Cells.Count);
    }

    [Fact]
    public void AsymmetricKeys_CorrectDimensions() {
        var grid = CrosshairGridCalculator.Calculate(
            new Rectangle(0, 0, 1920, 1080), horizKeyCount: 10, vertKeyCount: 6);

        Assert.Equal(11, grid.Cols);
        Assert.Equal(7, grid.Rows);
        Assert.Equal(77, grid.Cells.Count);
    }

    // --- Center cell position ---

    [Fact]
    public void EvenKeys_CenterAtHalfIndex() {
        var grid = CrosshairGridCalculator.Calculate(
            new Rectangle(0, 0, 1920, 1080), horizKeyCount: 10, vertKeyCount: 10);

        // horizKeys/2 = 5, vertKeys/2 = 5
        Assert.Equal(5, grid.CenterCol);
        Assert.Equal(5, grid.CenterRow);
    }

    [Fact]
    public void OddKeys_CenterAtHalfIndex() {
        var grid = CrosshairGridCalculator.Calculate(
            new Rectangle(0, 0, 1000, 1000), horizKeyCount: 5, vertKeyCount: 5);

        // 5/2 = 2
        Assert.Equal(2, grid.CenterCol);
        Assert.Equal(2, grid.CenterRow);
    }

    [Fact]
    public void CenterCell_IsCorrect() {
        var grid = CrosshairGridCalculator.Calculate(
            new Rectangle(0, 0, 1000, 1000), horizKeyCount: 4, vertKeyCount: 4);

        // 5×5 grid, center at (2,2)
        var center = grid.CenterCell;
        Assert.Equal(2, center.Row);
        Assert.Equal(2, center.Col);
    }

    // --- Cell bounds fill screen ---

    [Fact]
    public void CellBounds_CoverEntireScreen() {
        var bounds = new Rectangle(0, 0, 1920, 1080);
        var grid = CrosshairGridCalculator.Calculate(bounds, horizKeyCount: 10, vertKeyCount: 10);

        // First cell starts at origin
        var firstCell = grid.CellAt(0, 0);
        Assert.Equal(0, firstCell.Bounds.X);
        Assert.Equal(0, firstCell.Bounds.Y);

        // Last cell ends at screen edge
        var lastCell = grid.CellAt(grid.Rows - 1, grid.Cols - 1);
        Assert.Equal(bounds.Width, lastCell.Bounds.Right);
        Assert.Equal(bounds.Height, lastCell.Bounds.Bottom);
    }

    [Fact]
    public void CellBounds_WithOffset() {
        var bounds = new Rectangle(100, 200, 800, 600);
        var grid = CrosshairGridCalculator.Calculate(bounds, horizKeyCount: 4, vertKeyCount: 4);

        var firstCell = grid.CellAt(0, 0);
        Assert.Equal(100, firstCell.Bounds.X);
        Assert.Equal(200, firstCell.Bounds.Y);

        var lastCell = grid.CellAt(grid.Rows - 1, grid.Cols - 1);
        Assert.Equal(900, lastCell.Bounds.Right);
        Assert.Equal(800, lastCell.Bounds.Bottom);
    }

    [Fact]
    public void CellWidths_Uniform() {
        var grid = CrosshairGridCalculator.Calculate(
            new Rectangle(0, 0, 1100, 1100), horizKeyCount: 10, vertKeyCount: 10);

        // 1100 / 11 = 100 per cell
        for (int row = 0; row < grid.Rows; row++) {
            for (int col = 0; col < grid.Cols; col++) {
                var cell = grid.CellAt(row, col);
                Assert.Equal(100, cell.Bounds.Width);
                Assert.Equal(100, cell.Bounds.Height);
            }
        }
    }

    // --- Cross detection ---

    [Fact]
    public void IsOnCross_CenterRowAndCol() {
        var grid = CrosshairGridCalculator.Calculate(
            new Rectangle(0, 0, 1000, 1000), horizKeyCount: 4, vertKeyCount: 4);

        // Center is (2,2). Cross = row 2 or col 2.
        Assert.True(grid.IsOnCross(2, 0)); // center row
        Assert.True(grid.IsOnCross(2, 4)); // center row
        Assert.True(grid.IsOnCross(0, 2)); // center col
        Assert.True(grid.IsOnCross(4, 2)); // center col
        Assert.True(grid.IsOnCross(2, 2)); // center cell (both)

        Assert.False(grid.IsOnCross(0, 0));
        Assert.False(grid.IsOnCross(1, 1));
        Assert.False(grid.IsOnCross(3, 3));
        Assert.False(grid.IsOnCross(4, 4));
    }

    // --- CenterOf ---

    [Fact]
    public void CenterOf_ReturnsMiddleOfCell() {
        var grid = CrosshairGridCalculator.Calculate(
            new Rectangle(0, 0, 1000, 1000), horizKeyCount: 4, vertKeyCount: 4);

        // 5×5 grid, each cell 200×200
        var cell = grid.CellAt(0, 0); // (0,0,200,200)
        var center = CrosshairGridCalculator.CenterOf(cell);
        Assert.Equal(100, center.X);
        Assert.Equal(100, center.Y);
    }

    // --- L2/L3 subgrid (parent cell bounds) ---

    [Fact]
    public void Subgrid_ComputedFromParentCellBounds() {
        // Simulate L2: parent cell is a 200×200 square
        var parentBounds = new Rectangle(400, 400, 200, 200);
        var subgrid = CrosshairGridCalculator.Calculate(parentBounds, horizKeyCount: 4, vertKeyCount: 4);

        Assert.Equal(5, subgrid.Cols);
        Assert.Equal(5, subgrid.Rows);

        var first = subgrid.CellAt(0, 0);
        Assert.Equal(400, first.Bounds.X);
        Assert.Equal(400, first.Bounds.Y);
        Assert.Equal(40, first.Bounds.Width);
        Assert.Equal(40, first.Bounds.Height);
    }

    // --- Edge cases ---

    [Fact]
    public void SingleKey_Creates2x2Grid() {
        var grid = CrosshairGridCalculator.Calculate(
            new Rectangle(0, 0, 100, 100), horizKeyCount: 1, vertKeyCount: 1);

        Assert.Equal(2, grid.Cols);
        Assert.Equal(2, grid.Rows);
        Assert.Equal(0, grid.CenterCol); // 1/2 = 0
        Assert.Equal(0, grid.CenterRow);
    }

    [Fact]
    public void TwoKeys_Creates3x3Grid() {
        var grid = CrosshairGridCalculator.Calculate(
            new Rectangle(0, 0, 300, 300), horizKeyCount: 2, vertKeyCount: 2);

        Assert.Equal(3, grid.Cols);
        Assert.Equal(3, grid.Rows);
        Assert.Equal(1, grid.CenterCol); // 2/2 = 1
        Assert.Equal(1, grid.CenterRow);
    }

    [Fact]
    public void InvalidHorizKeys_Throws() {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CrosshairGridCalculator.Calculate(new Rectangle(0, 0, 100, 100), 0, 5));
    }

    [Fact]
    public void InvalidVertKeys_Throws() {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CrosshairGridCalculator.Calculate(new Rectangle(0, 0, 100, 100), 5, 0));
    }
}
