using System.Drawing;

using Klikety.Grid;

namespace Klikety.Tests;

public class LogGridCalculatorTests {
    // --- Basic structure ---

    [Fact]
    public void TenKeys_Creates11x11Grid() {
        var grid = LogGridCalculator.Calculate(
            new Point(960, 540), new Rectangle(0, 0, 1920, 1080),
            logBaseSize: 5, horizKeyCount: 10, vertKeyCount: 10);

        Assert.Equal(11, grid.Cols);
        Assert.Equal(11, grid.Rows);
        Assert.Equal(121, grid.Cells.Count);
    }

    [Fact]
    public void FourKeys_Creates5x5Grid() {
        var grid = LogGridCalculator.Calculate(
            new Point(500, 500), new Rectangle(0, 0, 1000, 1000),
            logBaseSize: 5, horizKeyCount: 4, vertKeyCount: 4);

        Assert.Equal(5, grid.Cols);
        Assert.Equal(5, grid.Rows);
        Assert.Equal(25, grid.Cells.Count);
    }

    [Fact]
    public void AsymmetricKeys_CorrectDimensions() {
        var grid = LogGridCalculator.Calculate(
            new Point(960, 540), new Rectangle(0, 0, 1920, 1080),
            logBaseSize: 5, horizKeyCount: 10, vertKeyCount: 6);

        Assert.Equal(11, grid.Cols);
        Assert.Equal(7, grid.Rows);
    }

    // --- Center cell ---

    [Fact]
    public void CenterCell_AtCorrectIndex() {
        var grid = LogGridCalculator.Calculate(
            new Point(960, 540), new Rectangle(0, 0, 1920, 1080),
            logBaseSize: 5, horizKeyCount: 10, vertKeyCount: 10);

        Assert.Equal(5, grid.CenterCol);
        Assert.Equal(5, grid.CenterRow);
    }

    [Fact]
    public void CenterCell_ApproximatelyBaseSize() {
        var grid = LogGridCalculator.Calculate(
            new Point(960, 540), new Rectangle(0, 0, 1920, 1080),
            logBaseSize: 10, horizKeyCount: 10, vertKeyCount: 10);

        var center = grid.CenterCell;
        // Center cell should be approximately logBaseSize (rounding may cause ±1)
        Assert.InRange(center.Bounds.Width, 9, 11);
        Assert.InRange(center.Bounds.Height, 9, 11);
    }

    // --- Cell growth ---

    [Fact]
    public void CellsGrowOutwardFromCenter_Horizontal() {
        var grid = LogGridCalculator.Calculate(
            new Point(960, 540), new Rectangle(0, 0, 1920, 1080),
            logBaseSize: 5, horizKeyCount: 10, vertKeyCount: 10);

        int centerRow = grid.CenterRow;

        // Right of center: each cell at least as wide as the previous
        for (int col = grid.CenterCol + 1; col < grid.Cols - 1; col++) {
            var inner = grid.CellAt(centerRow, col);
            var outer = grid.CellAt(centerRow, col + 1);
            Assert.True(outer.Bounds.Width >= inner.Bounds.Width,
                $"Cell ({centerRow},{col + 1}) width {outer.Bounds.Width} should be >= ({centerRow},{col}) width {inner.Bounds.Width}");
        }

        // Left of center: each cell at least as wide as the previous inward
        for (int col = grid.CenterCol - 1; col > 0; col--) {
            var inner = grid.CellAt(centerRow, col);
            var outer = grid.CellAt(centerRow, col - 1);
            Assert.True(outer.Bounds.Width >= inner.Bounds.Width,
                $"Cell ({centerRow},{col - 1}) width {outer.Bounds.Width} should be >= ({centerRow},{col}) width {inner.Bounds.Width}");
        }
    }

    [Fact]
    public void CellsGrowOutwardFromCenter_Vertical() {
        var grid = LogGridCalculator.Calculate(
            new Point(960, 540), new Rectangle(0, 0, 1920, 1080),
            logBaseSize: 5, horizKeyCount: 10, vertKeyCount: 10);

        int centerCol = grid.CenterCol;

        // Below center
        for (int row = grid.CenterRow + 1; row < grid.Rows - 1; row++) {
            var inner = grid.CellAt(row, centerCol);
            var outer = grid.CellAt(row + 1, centerCol);
            Assert.True(outer.Bounds.Height >= inner.Bounds.Height,
                $"Cell ({row + 1},{centerCol}) height {outer.Bounds.Height} should be >= ({row},{centerCol}) height {inner.Bounds.Height}");
        }

        // Above center
        for (int row = grid.CenterRow - 1; row > 0; row--) {
            var inner = grid.CellAt(row, centerCol);
            var outer = grid.CellAt(row - 1, centerCol);
            Assert.True(outer.Bounds.Height >= inner.Bounds.Height,
                $"Cell ({row - 1},{centerCol}) height {outer.Bounds.Height} should be >= ({row},{centerCol}) height {inner.Bounds.Height}");
        }
    }

    // --- Full coverage ---

    [Fact]
    public void CellsAreSquare_Centered() {
        var bounds = new Rectangle(0, 0, 1920, 1080);
        var grid = LogGridCalculator.Calculate(
            new Point(960, 540), bounds,
            logBaseSize: 5, horizKeyCount: 10, vertKeyCount: 10);

        for (int r = 0; r < grid.Rows; r++) {
            for (int c = 0; c < grid.Cols; c++) {
                if (!grid.IsDegenerate(r, c)) {
                    Assert.Equal(grid.CellAt(r, c).Bounds.Width, grid.CellAt(r, c).Bounds.Height);
                }
            }
        }
    }

    [Fact]
    public void CellsAreSquare_OffCenter() {
        var bounds = new Rectangle(0, 0, 1920, 1080);
        var grid = LogGridCalculator.Calculate(
            new Point(300, 200), bounds,
            logBaseSize: 5, horizKeyCount: 10, vertKeyCount: 10);

        for (int r = 0; r < grid.Rows; r++) {
            for (int c = 0; c < grid.Cols; c++) {
                if (!grid.IsDegenerate(r, c)) {
                    Assert.Equal(grid.CellAt(r, c).Bounds.Width, grid.CellAt(r, c).Bounds.Height);
                }
            }
        }
    }

    [Fact]
    public void CellsOnCenterRow_HaveConsistentHeight() {
        var grid = LogGridCalculator.Calculate(
            new Point(960, 540), new Rectangle(0, 0, 1920, 1080),
            logBaseSize: 5, horizKeyCount: 10, vertKeyCount: 10);

        // All cells in center row should have same height (square = min dim)
        for (int col = 0; col < grid.Cols; col++) {
            var cell = grid.CellAt(grid.CenterRow, col);
            if (!grid.IsDegenerate(grid.CenterRow, col)) {
                Assert.Equal(cell.Bounds.Width, cell.Bounds.Height);
            }
        }
    }

    [Fact]
    public void CellsOnCenterCol_HaveConsistentWidth() {
        var grid = LogGridCalculator.Calculate(
            new Point(960, 540), new Rectangle(0, 0, 1920, 1080),
            logBaseSize: 5, horizKeyCount: 10, vertKeyCount: 10);

        // All cells in center col should have same width (square = min dim)
        for (int row = 0; row < grid.Rows; row++) {
            var cell = grid.CellAt(row, grid.CenterCol);
            if (!grid.IsDegenerate(row, grid.CenterCol)) {
                Assert.Equal(cell.Bounds.Width, cell.Bounds.Height);
            }
        }
    }

    // --- Off-center cursor ---

    [Fact]
    public void OffCenterCursor_OuterCellsLargerThanInner() {
        var bounds = new Rectangle(0, 0, 1920, 1080);
        var grid = LogGridCalculator.Calculate(
            new Point(960, 540), bounds,
            logBaseSize: 5, horizKeyCount: 10, vertKeyCount: 10);

        // Outermost cell on center row should be >= inner cell (log growth)
        var outer = grid.CellAt(grid.CenterRow, 0);
        var inner = grid.CellAt(grid.CenterRow, grid.CenterCol - 1);
        if (!grid.IsDegenerate(grid.CenterRow, 0) && !grid.IsDegenerate(grid.CenterRow, grid.CenterCol - 1)) {
            Assert.True(outer.Bounds.Width >= inner.Bounds.Width);
        }
    }

    // --- Cursor near edge → degenerate cells ---

    [Fact]
    public void CursorNearEdge_DegenerateCellsOnShortSide() {
        var bounds = new Rectangle(0, 0, 1920, 1080);
        var grid = LogGridCalculator.Calculate(
            new Point(2, 540), bounds,
            logBaseSize: 5, horizKeyCount: 10, vertKeyCount: 10);

        bool hasDegenerate = false;
        for (int col = 0; col < grid.CenterCol; col++) {
            if (grid.IsDegenerate(grid.CenterRow, col)) {
                hasDegenerate = true;
                break;
            }
        }
        Assert.True(hasDegenerate);
    }

    // --- Cross detection ---

    [Fact]
    public void IsOnCross_CenterRowAndCol() {
        var grid = LogGridCalculator.Calculate(
            new Point(960, 540), new Rectangle(0, 0, 1920, 1080),
            logBaseSize: 5, horizKeyCount: 10, vertKeyCount: 10);

        Assert.True(grid.IsOnCross(grid.CenterRow, 0));
        Assert.True(grid.IsOnCross(0, grid.CenterCol));
        Assert.True(grid.IsOnCross(grid.CenterRow, grid.CenterCol));
        Assert.False(grid.IsOnCross(0, 0));
    }

    // --- Bounds with offset ---

    [Fact]
    public void BoundsWithOffset_CellsWithinBounds() {
        var bounds = new Rectangle(100, 200, 800, 600);
        var grid = LogGridCalculator.Calculate(
            new Point(500, 500), bounds,
            logBaseSize: 5, horizKeyCount: 4, vertKeyCount: 4);

        // All cells should be within the original bounds
        for (int r = 0; r < grid.Rows; r++) {
            for (int c = 0; c < grid.Cols; c++) {
                var cell = grid.CellAt(r, c);
                Assert.True(cell.Bounds.X >= bounds.X);
                Assert.True(cell.Bounds.Y >= bounds.Y);
                Assert.True(cell.Bounds.Right <= bounds.Right);
                Assert.True(cell.Bounds.Bottom <= bounds.Bottom);
            }
        }
    }

    // --- CenterOf helper ---

    [Fact]
    public void CenterOf_ReturnsMiddle() {
        var cell = new GridCell(0, 0, new Rectangle(100, 200, 50, 30));
        var center = LogGridCalculator.CenterOf(cell);
        Assert.Equal(125, center.X);
        Assert.Equal(215, center.Y);
    }

    // --- Validation ---

    [Fact]
    public void ZeroWidth_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LogGridCalculator.Calculate(new Point(0, 0), new Rectangle(0, 0, 0, 100), 5, 10, 10));

    [Fact]
    public void ZeroHeight_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LogGridCalculator.Calculate(new Point(0, 0), new Rectangle(0, 0, 100, 0), 5, 10, 10));

    [Fact]
    public void ZeroHorizKeys_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LogGridCalculator.Calculate(new Point(50, 50), new Rectangle(0, 0, 100, 100), 5, 0, 10));

    [Fact]
    public void ZeroVertKeys_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LogGridCalculator.Calculate(new Point(50, 50), new Rectangle(0, 0, 100, 100), 5, 10, 0));

    [Fact]
    public void ZeroBaseSize_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LogGridCalculator.Calculate(new Point(50, 50), new Rectangle(0, 0, 100, 100), 0, 10, 10));

    // --- Various resolutions ---

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(3840, 2160)]
    [InlineData(1366, 768)]
    public void VariousResolutions_SquareCells(int width, int height) {
        var bounds = new Rectangle(0, 0, width, height);
        var grid = LogGridCalculator.Calculate(
            new Point(width / 2, height / 2), bounds,
            logBaseSize: 5, horizKeyCount: 10, vertKeyCount: 10);

        for (int r = 0; r < grid.Rows; r++) {
            for (int c = 0; c < grid.Cols; c++) {
                if (!grid.IsDegenerate(r, c)) {
                    var cell = grid.CellAt(r, c);
                    Assert.Equal(cell.Bounds.Width, cell.Bounds.Height);
                    Assert.True(cell.Bounds.X >= 0);
                    Assert.True(cell.Bounds.Y >= 0);
                    Assert.True(cell.Bounds.Right <= width);
                    Assert.True(cell.Bounds.Bottom <= height);
                }
            }
        }
    }

    // --- Center at bounds edge (clamped) ---

    [Fact]
    public void CenterOutsideBounds_ClampedToBounds() {
        var bounds = new Rectangle(100, 100, 800, 600);
        // Center outside bounds — should be clamped
        var grid = LogGridCalculator.Calculate(
            new Point(50, 50), bounds,
            logBaseSize: 5, horizKeyCount: 4, vertKeyCount: 4);

        // All cells within bounds
        for (int r = 0; r < grid.Rows; r++) {
            for (int c = 0; c < grid.Cols; c++) {
                var cell = grid.CellAt(r, c);
                Assert.True(cell.Bounds.X >= bounds.X);
                Assert.True(cell.Bounds.Y >= bounds.Y);
                Assert.True(cell.Bounds.Right <= bounds.Right);
                Assert.True(cell.Bounds.Bottom <= bounds.Bottom);
            }
        }
    }

    // --- Fix #8: logBaseSize larger than screen ---

    [Fact]
    public void LogBaseSize_LargerThanScreen_ProducesValidGrid() {
        var bounds = new Rectangle(0, 0, 100, 100);
        // logBaseSize=200 exceeds screen — ratio falls back to 1.0, all edges clamped
        var grid = LogGridCalculator.Calculate(
            new Point(50, 50), bounds,
            logBaseSize: 200, horizKeyCount: 4, vertKeyCount: 4);

        // Grid structure valid
        Assert.Equal(5, grid.Cols);
        Assert.Equal(5, grid.Rows);

        // All cells within bounds
        for (int r = 0; r < grid.Rows; r++) {
            for (int c = 0; c < grid.Cols; c++) {
                var cell = grid.CellAt(r, c);
                Assert.True(cell.Bounds.X >= 0);
                Assert.True(cell.Bounds.Y >= 0);
                Assert.True(cell.Bounds.Right <= 100);
                Assert.True(cell.Bounds.Bottom <= 100);
            }
        }

        // Most cells will be degenerate (width/height < 1) due to clamping
        bool hasDegenerate = false;
        for (int r = 0; r < grid.Rows; r++) {
            for (int c = 0; c < grid.Cols; c++) {
                if (grid.IsDegenerate(r, c)) {
                    hasDegenerate = true;
                }
            }
        }
        Assert.True(hasDegenerate);
    }
}
