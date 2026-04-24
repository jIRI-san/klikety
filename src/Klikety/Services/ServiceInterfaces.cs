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
/// Abstracts low-level keyboard hook for overlay key capture.
/// </summary>
public interface IKeyboardHookService {
    event EventHandler<VKey>? KeyPressed;
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
    bool IsVisible { get; }
}

/// <summary>
/// Abstracts grid rendering for testability.
/// </summary>
public interface IGridRenderer {
    void SetTransform(System.Windows.Media.Matrix transformFromDevice);
    void RenderGrid(IReadOnlyList<GridCell> cells);
    void HighlightColumn(IReadOnlyList<GridCell> cells, int col);
    void HighlightCell(IReadOnlyList<GridCell> cells, GridCell highlightedCell);
    void RenderSubgrid(IReadOnlyList<GridCell> cells);
    void RenderSubgridOverGrid(IReadOnlyList<GridCell> backgroundCells, IReadOnlyList<GridCell> subgridCells);
    void HighlightColumnOverGrid(IReadOnlyList<GridCell> backgroundCells, IReadOnlyList<GridCell> subgridCells, int col);
    void FlashInvalidKey();
    void ClearCanvas();
}
