using System.Drawing;

using Klikety.Grid;

namespace Klikety.Tests;

/// <summary>
/// Property-based tests for <see cref="LogScaleGridCalculator"/>.
/// No exact-pixel assertions — only structural/invariant properties.
/// </summary>
public class LogScaleGridCalculatorTests {
    // -------------------------------------------------------------------------
    // (a) Correct cell count
    // -------------------------------------------------------------------------

    [Fact]
    public void TenByTen_ProducesHundredCells() {
        var grid = LogScaleGridCalculator.Calculate(
            new Point(960, 540), new Rectangle(0, 0, 1920, 1080),
            logBaseSize: 10, cols: 10, rows: 10);

        Assert.Equal(10, grid.Cols);
        Assert.Equal(10, grid.Rows);
        Assert.Equal(100, grid.Cells.Length);
    }

    [Fact]
    public void SixByEight_ProducesCorrectCount() {
        var grid = LogScaleGridCalculator.Calculate(
            new Point(500, 500), new Rectangle(0, 0, 1000, 1000),
            logBaseSize: 10, cols: 6, rows: 8);

        Assert.Equal(6, grid.Cols);
        Assert.Equal(8, grid.Rows);
        Assert.Equal(48, grid.Cells.Length);
    }

    // -------------------------------------------------------------------------
    // (b) Full bounds coverage — no gaps (shared-edge rounding)
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(960, 540, 1920, 1080)]
    [InlineData(960, 540, 1919, 1079)]   // odd dimensions
    [InlineData(540, 960, 1080, 1920)]   // portrait
    [InlineData(960, 540, 3840, 2160)]   // 4K
    [InlineData(300, 200, 1920, 1080)]   // off-center
    public void NoGaps_BetweenAdjacentCells_Horizontal(int cx, int cy, int w, int h) {
        var grid = LogScaleGridCalculator.Calculate(
            new Point(cx, cy), new Rectangle(0, 0, w, h),
            logBaseSize: 10, cols: 10, rows: 10);

        for (int row = 0; row < grid.Rows; row++) {
            for (int col = 0; col < grid.Cols - 1; col++) {
                var left = grid.CellAt(row, col);
                var right = grid.CellAt(row, col + 1);
                Assert.Equal(left.Bounds.Right, right.Bounds.Left);
            }
        }
    }

    [Theory]
    [InlineData(960, 540, 1920, 1080)]
    [InlineData(960, 540, 1919, 1079)]
    [InlineData(540, 960, 1080, 1920)]
    [InlineData(960, 540, 3840, 2160)]
    [InlineData(300, 200, 1920, 1080)]
    public void NoGaps_BetweenAdjacentCells_Vertical(int cx, int cy, int w, int h) {
        var grid = LogScaleGridCalculator.Calculate(
            new Point(cx, cy), new Rectangle(0, 0, w, h),
            logBaseSize: 10, cols: 10, rows: 10);

        for (int col = 0; col < grid.Cols; col++) {
            for (int row = 0; row < grid.Rows - 1; row++) {
                var top = grid.CellAt(row, col);
                var bottom = grid.CellAt(row + 1, col);
                Assert.Equal(top.Bounds.Bottom, bottom.Bounds.Top);
            }
        }
    }

    [Theory]
    [InlineData(960, 540, 1920, 1080)]
    [InlineData(960, 540, 1919, 1079)]
    [InlineData(540, 960, 1080, 1920)]
    [InlineData(960, 540, 3840, 2160)]
    [InlineData(300, 200, 1920, 1080)]
    public void FirstCell_StartsAtBoundsLeft_And_LastCell_EndsAtBoundsRight(int cx, int cy, int w, int h) {
        var bounds = new Rectangle(0, 0, w, h);
        var grid = LogScaleGridCalculator.Calculate(
            new Point(cx, cy), bounds, logBaseSize: 10, cols: 10, rows: 10);

        for (int row = 0; row < grid.Rows; row++) {
            Assert.Equal(bounds.Left, grid.CellAt(row, 0).Bounds.Left);
            Assert.Equal(bounds.Right, grid.CellAt(row, grid.Cols - 1).Bounds.Right);
        }

        for (int col = 0; col < grid.Cols; col++) {
            Assert.Equal(bounds.Top, grid.CellAt(0, col).Bounds.Top);
            Assert.Equal(bounds.Bottom, grid.CellAt(grid.Rows - 1, col).Bounds.Bottom);
        }
    }

    // -------------------------------------------------------------------------
    // (c) Monotonic growth outward from center per half-axis
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(960, 540, 1920, 1080)]
    [InlineData(960, 540, 3840, 2160)]
    [InlineData(300, 200, 1920, 1080)]
    public void ColumnWidths_GrowMonotonically_OutwardFromCenter(int cx, int cy, int w, int h) {
        var grid = LogScaleGridCalculator.Calculate(
            new Point(cx, cy), new Rectangle(0, 0, w, h),
            logBaseSize: 10, cols: 10, rows: 10);

        int midRow = 0; // widths are axis-level — check top row

        // Right half (cols 5..9): each successive column at least as wide
        for (int col = grid.Cols / 2; col < grid.Cols - 1; col++) {
            int innerW = grid.CellAt(midRow, col).Bounds.Width;
            int outerW = grid.CellAt(midRow, col + 1).Bounds.Width;
            Assert.True(outerW >= innerW,
                $"Right half: col {col + 1} width {outerW} < col {col} width {innerW}");
        }

        // Left half (cols 4..0): each successive column (moving left) at least as wide
        for (int col = grid.Cols / 2 - 1; col > 0; col--) {
            int innerW = grid.CellAt(midRow, col).Bounds.Width;
            int outerW = grid.CellAt(midRow, col - 1).Bounds.Width;
            Assert.True(outerW >= innerW,
                $"Left half: col {col - 1} width {outerW} < col {col} width {innerW}");
        }
    }

    [Theory]
    [InlineData(960, 540, 1920, 1080)]
    [InlineData(960, 540, 3840, 2160)]
    [InlineData(300, 200, 1920, 1080)]
    public void RowHeights_GrowMonotonically_OutwardFromCenter(int cx, int cy, int w, int h) {
        var grid = LogScaleGridCalculator.Calculate(
            new Point(cx, cy), new Rectangle(0, 0, w, h),
            logBaseSize: 10, cols: 10, rows: 10);

        int midCol = 0;

        // Bottom half
        for (int row = grid.Rows / 2; row < grid.Rows - 1; row++) {
            int innerH = grid.CellAt(row, midCol).Bounds.Height;
            int outerH = grid.CellAt(row + 1, midCol).Bounds.Height;
            Assert.True(outerH >= innerH,
                $"Bottom half: row {row + 1} height {outerH} < row {row} height {innerH}");
        }

        // Top half
        for (int row = grid.Rows / 2 - 1; row > 0; row--) {
            int innerH = grid.CellAt(row, midCol).Bounds.Height;
            int outerH = grid.CellAt(row - 1, midCol).Bounds.Height;
            Assert.True(outerH >= innerH,
                $"Top half: row {row - 1} height {outerH} < row {row} height {innerH}");
        }
    }

    // -------------------------------------------------------------------------
    // (d) All 4 screen corners as center
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1920, 0)]
    [InlineData(0, 1080)]
    [InlineData(1920, 1080)]
    public void CornerCenter_ProducesValidGrid(int cx, int cy) {
        var bounds = new Rectangle(0, 0, 1920, 1080);
        var grid = LogScaleGridCalculator.Calculate(
            new Point(cx, cy), bounds, logBaseSize: 10, cols: 10, rows: 10);

        Assert.Equal(100, grid.Cells.Length);
        // All cells must have non-negative dimensions (some may be zero-size at extreme corners)
        foreach (var cell in grid.Cells) {
            Assert.True(cell.Bounds.Width >= 0);
            Assert.True(cell.Bounds.Height >= 0);
        }
        // Bounds coverage still holds
        Assert.Equal(bounds.Left, grid.CellAt(0, 0).Bounds.Left);
        Assert.Equal(bounds.Right, grid.CellAt(0, grid.Cols - 1).Bounds.Right);
        Assert.Equal(bounds.Top, grid.CellAt(0, 0).Bounds.Top);
        Assert.Equal(bounds.Bottom, grid.CellAt(grid.Rows - 1, 0).Bounds.Bottom);
    }

    // -------------------------------------------------------------------------
    // (e) Mid-edge positions
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(960, 0)]    // top edge
    [InlineData(960, 1080)] // bottom edge
    [InlineData(0, 540)]    // left edge
    [InlineData(1920, 540)] // right edge
    public void MidEdgeCenter_ProducesValidGrid(int cx, int cy) {
        var bounds = new Rectangle(0, 0, 1920, 1080);
        var grid = LogScaleGridCalculator.Calculate(
            new Point(cx, cy), bounds, logBaseSize: 10, cols: 10, rows: 10);

        Assert.Equal(100, grid.Cells.Length);
        Assert.Equal(bounds.Left, grid.CellAt(0, 0).Bounds.Left);
        Assert.Equal(bounds.Right, grid.CellAt(0, grid.Cols - 1).Bounds.Right);
        Assert.Equal(bounds.Top, grid.CellAt(0, 0).Bounds.Top);
        Assert.Equal(bounds.Bottom, grid.CellAt(grid.Rows - 1, 0).Bounds.Bottom);
    }

    // -------------------------------------------------------------------------
    // (f) Asymmetric screens
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(960, 540, 1920, 1080)]
    [InlineData(540, 960, 1080, 1920)]
    [InlineData(1920, 1080, 3840, 2160)]
    public void AsymmetricScreens_NoGapsAndCorrectCount(int cx, int cy, int w, int h) {
        var bounds = new Rectangle(0, 0, w, h);
        var grid = LogScaleGridCalculator.Calculate(
            new Point(cx, cy), bounds, logBaseSize: 10, cols: 10, rows: 10);

        Assert.Equal(100, grid.Cells.Length);

        for (int row = 0; row < grid.Rows; row++) {
            for (int col = 0; col < grid.Cols - 1; col++) {
                Assert.Equal(grid.CellAt(row, col).Bounds.Right, grid.CellAt(row, col + 1).Bounds.Left);
            }
        }

        for (int col = 0; col < grid.Cols; col++) {
            for (int row = 0; row < grid.Rows - 1; row++) {
                Assert.Equal(grid.CellAt(row, col).Bounds.Bottom, grid.CellAt(row + 1, col).Bounds.Top);
            }
        }
    }

    // -------------------------------------------------------------------------
    // (g) Cursor at exact bounds edge (clamped — same as corner tests)
    // -------------------------------------------------------------------------

    [Fact]
    public void CursorAtBoundsEdge_IsClamped_AndGridIsValid() {
        var bounds = new Rectangle(0, 0, 1920, 1080);
        // Cursor slightly outside bounds — should be clamped
        var grid = LogScaleGridCalculator.Calculate(
            new Point(-50, -50), bounds, logBaseSize: 10, cols: 10, rows: 10);

        Assert.Equal(new Point(0, 0), grid.CenterPoint);
        Assert.Equal(100, grid.Cells.Length);
    }

    // -------------------------------------------------------------------------
    // (h) Very small bounds — uniform fallback
    // -------------------------------------------------------------------------

    [Fact]
    public void SmallBounds_50x50_TriggersUniformFallback_NoGaps() {
        var bounds = new Rectangle(0, 0, 50, 50);
        var grid = LogScaleGridCalculator.Calculate(
            new Point(25, 25), bounds, logBaseSize: 10, cols: 10, rows: 10);

        Assert.Equal(100, grid.Cells.Length);

        // No gaps horizontal
        for (int row = 0; row < grid.Rows; row++) {
            for (int col = 0; col < grid.Cols - 1; col++) {
                Assert.Equal(grid.CellAt(row, col).Bounds.Right, grid.CellAt(row, col + 1).Bounds.Left);
            }
        }

        // No gaps vertical
        for (int col = 0; col < grid.Cols; col++) {
            for (int row = 0; row < grid.Rows - 1; row++) {
                Assert.Equal(grid.CellAt(row, col).Bounds.Bottom, grid.CellAt(row + 1, col).Bounds.Top);
            }
        }

        // Coverage
        Assert.Equal(bounds.Left, grid.CellAt(0, 0).Bounds.Left);
        Assert.Equal(bounds.Right, grid.CellAt(0, grid.Cols - 1).Bounds.Right);
    }

    // -------------------------------------------------------------------------
    // (i) Odd-dimension bounds (1919×1079)
    // -------------------------------------------------------------------------

    [Fact]
    public void OddDimensions_1919x1079_NoGaps() {
        var bounds = new Rectangle(0, 0, 1919, 1079);
        var grid = LogScaleGridCalculator.Calculate(
            new Point(959, 539), bounds, logBaseSize: 10, cols: 10, rows: 10);

        for (int row = 0; row < grid.Rows; row++) {
            for (int col = 0; col < grid.Cols - 1; col++) {
                Assert.Equal(grid.CellAt(row, col).Bounds.Right, grid.CellAt(row, col + 1).Bounds.Left);
            }
        }

        for (int col = 0; col < grid.Cols; col++) {
            for (int row = 0; row < grid.Rows - 1; row++) {
                Assert.Equal(grid.CellAt(row, col).Bounds.Bottom, grid.CellAt(row + 1, col).Bounds.Top);
            }
        }
    }

    // -------------------------------------------------------------------------
    // REQ-3: Off-center cursor — asymmetric halves have independent growth ratios
    // -------------------------------------------------------------------------

    [Fact]
    public void OffCenter_LeftHalf_SmallerCells_ThanRightHalf() {
        // Center is far left — left half has little space, right has lots
        var grid = LogScaleGridCalculator.Calculate(
            new Point(100, 540), new Rectangle(0, 0, 1920, 1080),
            logBaseSize: 10, cols: 10, rows: 10);

        // Left half columns (0..4) should collectively be narrower than right half (5..9)
        int leftTotal = 0;
        int rightTotal = 0;
        for (int col = 0; col < 5; col++) leftTotal += grid.CellAt(0, col).Bounds.Width;
        for (int col = 5; col < 10; col++) rightTotal += grid.CellAt(0, col).Bounds.Width;
        Assert.True(leftTotal < rightTotal, $"Left {leftTotal} should be less than right {rightTotal}");
    }

    // -------------------------------------------------------------------------
    // CenterPoint recorded correctly
    // -------------------------------------------------------------------------

    [Fact]
    public void CenterPoint_RecordsClampedCursorPosition() {
        var bounds = new Rectangle(0, 0, 1920, 1080);
        var grid = LogScaleGridCalculator.Calculate(
            new Point(300, 400), bounds, logBaseSize: 10, cols: 10, rows: 10);

        Assert.Equal(new Point(300, 400), grid.CenterPoint);
    }

    // -------------------------------------------------------------------------
    // ColEdges / RowEdges length
    // -------------------------------------------------------------------------

    [Fact]
    public void EdgeArrays_HaveCorrectLength() {
        var grid = LogScaleGridCalculator.Calculate(
            new Point(960, 540), new Rectangle(0, 0, 1920, 1080),
            logBaseSize: 10, cols: 10, rows: 10);

        Assert.Equal(11, grid.ColEdges.Length); // cols + 1
        Assert.Equal(11, grid.RowEdges.Length); // rows + 1
    }

    // -------------------------------------------------------------------------
    // Argument validation
    // -------------------------------------------------------------------------

    [Fact]
    public void ZeroWidth_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LogScaleGridCalculator.Calculate(
                new Point(0, 0), new Rectangle(0, 0, 0, 100),
                logBaseSize: 10, cols: 10, rows: 10));

    [Fact]
    public void ZeroHeight_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LogScaleGridCalculator.Calculate(
                new Point(0, 0), new Rectangle(0, 0, 100, 0),
                logBaseSize: 10, cols: 10, rows: 10));

    [Fact]
    public void OddCols_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LogScaleGridCalculator.Calculate(
                new Point(960, 540), new Rectangle(0, 0, 1920, 1080),
                logBaseSize: 10, cols: 9, rows: 10));

    [Fact]
    public void OddRows_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LogScaleGridCalculator.Calculate(
                new Point(960, 540), new Rectangle(0, 0, 1920, 1080),
                logBaseSize: 10, cols: 10, rows: 9));

    [Fact]
    public void ZeroBaseSize_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LogScaleGridCalculator.Calculate(
                new Point(960, 540), new Rectangle(0, 0, 1920, 1080),
                logBaseSize: 0, cols: 10, rows: 10));
}
