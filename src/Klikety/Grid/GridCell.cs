using System.Drawing;

namespace Klikety.Grid;

/// <summary>
/// A single cell in the navigation grid. All coordinates in physical pixels.
/// </summary>
public readonly record struct GridCell(int Row, int Col, Rectangle Bounds);
