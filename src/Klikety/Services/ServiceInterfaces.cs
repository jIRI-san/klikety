using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;

namespace Klikety.Services;

/// <summary>
/// Abstracts global hotkey registration and activation.
/// </summary>
public interface IHotKeyService : IDisposable {
    event EventHandler? Activated;
    bool Register(HotKeyConfig config);
    void Unregister();
}

/// <summary>
/// Event data for keyboard hook events, carrying both the key and direction.
/// </summary>
public record KeyHookEventArgs(VKey Key, bool IsDown);

/// <summary>
/// Abstracts low-level keyboard hook for overlay key capture.
/// </summary>
public interface IKeyboardHookService {
    event EventHandler<KeyHookEventArgs>? KeyEvent;
    bool Enable();
    void Disable();
}

/// <summary>
/// Abstracts mouse cursor movement and click actions.
/// </summary>
public interface IMouseActionService {
    void MoveTo(System.Drawing.Point physicalPoint);
    void SendAction(System.Drawing.Point physicalPoint, MouseAction action);
}

/// <summary>
/// Abstracts the overlay window for testability.
/// </summary>
public interface IOverlayWindow {
    event EventHandler? FocusLost;
    void Show();
    void Hide();
    void ClearCanvas();
    bool IsVisible { get; }
}

/// <summary>
/// Abstracts grid rendering for testability.
/// </summary>
public interface IGridRenderer {
    void SetTransform(System.Windows.Media.Matrix transformFromDevice);
    void SetLabelOffset(int colOffset, int rowOffset);
    void RenderGrid(IReadOnlyList<GridCell> cells);
    void HighlightColumn(IReadOnlyList<GridCell> cells, int col);
    void HighlightCell(IReadOnlyList<GridCell> cells, GridCell highlightedCell);
    void RenderSubgridOverGrid(IReadOnlyList<GridCell> backgroundCells, IReadOnlyList<GridCell> subgridCells);
    void HighlightColumnOverGrid(IReadOnlyList<GridCell> backgroundCells, IReadOnlyList<GridCell> subgridCells, int col);
    void HighlightCellOverGrid(IReadOnlyList<GridCell> backgroundCells, IReadOnlyList<GridCell> subgridCells, GridCell highlightedCell);
    void FlashInvalidKey();
}

/// <summary>
/// Abstracts crosshair-mode rendering for testability.
/// </summary>
public interface ICrosshairRenderer {
    void SetTransform(System.Windows.Media.Matrix transformFromDevice);
    void SetLabelOffset(int horizOffset, int vertOffset);
    void RenderCross(CrosshairGrid grid);
    void HighlightColumn(CrosshairGrid grid, int col);
    void HighlightRow(CrosshairGrid grid, int row);
    void HighlightCell(CrosshairGrid grid, GridCell cell);
    void RenderSubgridCross(CrosshairGrid parentGrid, CrosshairGrid subgrid, GridCell parentCell);
    void FlashInvalidKey();
}

/// <summary>
/// Abstracts log-crosshair-mode rendering for testability.
/// No subgrid support (flat, no L2/L3). Designed for frequent re-renders
/// with element pooling.
/// </summary>
public interface ILogCrosshairRenderer {
    void SetTransform(System.Windows.Media.Matrix transformFromDevice);
    void RenderCross(LogCrosshairGrid grid);
    void HighlightColumn(LogCrosshairGrid grid, int col);
    void HighlightRow(LogCrosshairGrid grid, int row);
    void HighlightCell(LogCrosshairGrid grid, GridCell cell);
    void FlashInvalidKey();
}
