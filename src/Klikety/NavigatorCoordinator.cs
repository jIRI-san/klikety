using System.Drawing;

using Klikety.Config;
using Klikety.Grid;
using Klikety.Interop;
using Klikety.Navigation;
using Klikety.Services;

using Microsoft.Extensions.Logging;

namespace Klikety;

/// <summary>
/// Wires all services together: hotkey → overlay → hook → state machine → mouse action.
/// Single DeactivateOverlay() method covers all exit paths.
/// Tracks cell lists per level for rendering context.
/// </summary>
public sealed class NavigatorCoordinator {
    private readonly IHotKeyService _hotKeyService;
    private readonly IKeyboardHookService _hookService;
    private readonly IMouseActionService _mouseService;
    private readonly IOverlayWindow _overlayWindow;
    private readonly NavigatorStateMachine _stateMachine;
    private readonly IGridRenderer? _gridRenderer;
    private readonly ConfigModel _config;
    private readonly ILogger _logger;

    // Cell lists per level
    private IReadOnlyList<GridCell> _l1Cells = [];
    private IReadOnlyList<GridCell>? _subgridCells;
    private IReadOnlyList<GridCell>? _l2SubgridCells;
    private bool _deactivating;

    public NavigatorCoordinator(
        IHotKeyService hotKeyService,
        IKeyboardHookService hookService,
        IMouseActionService mouseService,
        IOverlayWindow overlayWindow,
        NavigatorStateMachine stateMachine,
        IGridRenderer? gridRenderer,
        ConfigModel config,
        ILogger logger) {
        _hotKeyService = hotKeyService;
        _hookService = hookService;
        _mouseService = mouseService;
        _overlayWindow = overlayWindow;
        _stateMachine = stateMachine;
        _gridRenderer = gridRenderer;
        _config = config;
        _logger = logger;

        _hotKeyService.Activated += OnHotKeyActivated;
        _hookService.KeyPressed += OnKeyPressed;
        _overlayWindow.FocusLost += OnFocusLost;

        _stateMachine.ColumnHighlighted += OnColumnHighlighted;
        _stateMachine.CellHighlighted += OnCellHighlighted;
        _stateMachine.CellEntered += OnCellEntered;
        _stateMachine.ActionRequested += OnActionRequested;
        _stateMachine.Cancelled += OnCancelled;
        _stateMachine.InvalidKeyPressed += OnInvalidKeyPressed;
        _stateMachine.LevelExited += OnLevelExited;
        _stateMachine.ColumnUnhighlighted += OnColumnUnhighlighted;
    }

    private void OnHotKeyActivated(object? sender, EventArgs e) {
        _logger.LogDebug("Hotkey activated");

        var screenBounds = NativeMethods.GetPrimaryScreenBounds();

        _l1Cells = GridCalculator.Calculate(
            screenBounds,
            _config.FirstKeys.Length,
            _config.SecondKeys.Length);

        _subgridCells = null;
        _l2SubgridCells = null;

        var origin = NativeMethods.GetCursorPosition();

        _overlayWindow.Show();

        if (!_hookService.Enable()) {
            _logger.LogError("Failed to install keyboard hook");
            DeactivateOverlay();
            return;
        }

        _stateMachine.Activate(_l1Cells, origin);
        _gridRenderer?.RenderGrid(_l1Cells);
    }

    private void OnKeyPressed(object? sender, Input.VKey vkey) {
        var stateBefore = _stateMachine.State;
        _logger.LogDebug("Key: {Key}  State: {State}", vkey, stateBefore);
        _stateMachine.OnKey(vkey);
        var stateAfter = _stateMachine.State;
        if (stateAfter != stateBefore) {
            _logger.LogDebug("State: {Before} → {After}", stateBefore, stateAfter);
        }
    }

    private void OnFocusLost(object? sender, EventArgs e) {
        _logger.LogDebug("Overlay focus lost");
        DeactivateOverlay();
    }

    private void OnColumnHighlighted(int col, IReadOnlyList<GridCell> cells, int level) {
        _logger.LogDebug("ColumnHighlighted: col={Col} level={Level} cells={Count}", col, level, cells.Count);
        if (level == 1) {
            _gridRenderer?.HighlightColumn(_l1Cells, col);
        } else {
            _gridRenderer?.HighlightColumnOverGrid(_l1Cells, cells, col);
        }
    }

    private void OnCellHighlighted(GridCell cell) {
        _logger.LogDebug("CellHighlighted: row={Row} col={Col} hasSubgrid={HasSubgrid}", cell.Row, cell.Col, _subgridCells != null);
        if (_subgridCells == null) {
            _gridRenderer?.HighlightCell(_l1Cells, cell);
        } else {
            _gridRenderer?.HighlightCellOverGrid(_l1Cells, _subgridCells, cell);
        }
    }

    private void OnCellEntered(GridCell cell, IReadOnlyList<GridCell> subgridCells, int level) {
        _logger.LogDebug("CellEntered: row={Row} col={Col} level={Level} subgrid={Count}", cell.Row, cell.Col, level, subgridCells.Count);
        var center = GridCalculator.CenterOf(cell);
        _mouseService.MoveTo(center);

        if (subgridCells.Count > 0) {
            if (level == 1) {
                _subgridCells = subgridCells;
                _l2SubgridCells = subgridCells;
            } else if (level == 2) {
                _subgridCells = subgridCells;
            }
            _gridRenderer?.RenderSubgridOverGrid(_l1Cells, subgridCells);
        }
    }

    private void OnActionRequested(Point point, MouseAction action) {
        _logger.LogDebug("Action requested: {Action} at ({X}, {Y})", action, point.X, point.Y);
        DeactivateOverlay();
        _mouseService.SendAction(point, action);
    }

    private void OnCancelled(Point originPoint) {
        _logger.LogDebug("Navigation cancelled, restoring cursor");
        _mouseService.MoveTo(originPoint);
        DeactivateOverlay();
    }

    private void OnInvalidKeyPressed() {
        _logger.LogDebug("Invalid key pressed");
        _gridRenderer?.FlashInvalidKey();
    }

    private void OnLevelExited(GridCell parentCell, IReadOnlyList<GridCell> cells, int level) {
        _logger.LogDebug("LevelExited: level={Level} parentRow={Row} parentCol={Col}", level, parentCell.Row, parentCell.Col);
        var center = GridCalculator.CenterOf(parentCell);
        _mouseService.MoveTo(center);

        // L3→L2: restore L2 subgrid view
        _subgridCells = _l2SubgridCells;
        _gridRenderer?.RenderSubgridOverGrid(_l1Cells, cells);
    }

    private void OnColumnUnhighlighted(int level) {
        _logger.LogDebug("ColumnUnhighlighted: level={Level}", level);
        if (level == 1) {
            _subgridCells = null;
            _l2SubgridCells = null;
            _gridRenderer?.RenderGrid(_l1Cells);
        } else {
            _subgridCells = _l2SubgridCells;
            if (_subgridCells != null) {
                _gridRenderer?.RenderSubgridOverGrid(_l1Cells, _subgridCells);
            }
        }
    }

    /// <summary>
    /// Single idempotent exit method. Called from action, cancel, focus-loss, exception, quit.
    /// </summary>
    public void DeactivateOverlay() {
        if (_deactivating) {
            return;
        }

        _deactivating = true;

        try {
            _hookService.Disable();
            _gridRenderer?.ClearCanvas();
            _overlayWindow.Hide();
            _stateMachine.Reset();
        } finally {
            _deactivating = false;
        }
    }
}
