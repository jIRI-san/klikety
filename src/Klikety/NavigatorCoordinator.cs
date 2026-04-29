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
public sealed partial class NavigatorCoordinator {
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
    private Point _origin;

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
        _hookService.KeyEvent += OnKeyEvent;
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
        LogHotkeyActivated();

        var screenBounds = NativeMethods.GetPrimaryScreenBounds();

        _l1Cells = GridCalculator.Calculate(
            screenBounds,
            _config.FirstKeys.Length,
            _config.SecondKeys.Length);

        _subgridCells = null;
        _l2SubgridCells = null;

        _origin = NativeMethods.GetCursorPosition();

        _overlayWindow.Show();

        if (!_hookService.Enable()) {
            LogHookInstallFailed();
            DeactivateOverlay();
            return;
        }

        _stateMachine.Activate(_l1Cells, _origin);
        _gridRenderer?.RenderGrid(_l1Cells);
    }

    private void OnKeyEvent(object? sender, Services.KeyHookEventArgs e) {
        if (!e.IsDown) {
            return; // key-up: no action yet (debounce removal will be added in step 2.3)
        }

        var vkey = e.Key;
        var stateBefore = _stateMachine.State;
        LogKeyPressed(vkey, stateBefore);
        _stateMachine.OnKey(vkey);
        var stateAfter = _stateMachine.State;
        if (stateAfter != stateBefore) {
            LogStateTransition(stateBefore, stateAfter);
        }
    }

    private void OnFocusLost(object? sender, EventArgs e) {
        LogFocusLost();
        DeactivateOverlay();
    }

    private void OnColumnHighlighted(int col, IReadOnlyList<GridCell> cells, int level) {
        LogColumnHighlighted(col, level, cells.Count);
        if (level == 1) {
            _gridRenderer?.HighlightColumn(_l1Cells, col);
        } else {
            _gridRenderer?.HighlightColumnOverGrid(_l1Cells, cells, col);
        }
    }

    private void OnCellHighlighted(GridCell cell) {
        LogCellHighlighted(cell.Row, cell.Col, _subgridCells != null);
        if (_subgridCells == null) {
            _gridRenderer?.HighlightCell(_l1Cells, cell);
        } else {
            _gridRenderer?.HighlightCellOverGrid(_l1Cells, _subgridCells, cell);
        }
    }

    private void OnCellEntered(GridCell cell, IReadOnlyList<GridCell> subgridCells, int level) {
        LogCellEntered(cell.Row, cell.Col, level, subgridCells.Count);
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
        } else if (_subgridCells != null) {
            // Deepest level (no further descent) — show crosshair on selected cell
            _gridRenderer?.HighlightCellOverGrid(_l1Cells, _subgridCells, cell);
        } else {
            _gridRenderer?.HighlightCell(_l1Cells, cell);
        }
    }

    private void OnActionRequested(Point point, MouseAction action) {
        LogActionRequested(action, point.X, point.Y);
        DeactivateOverlay();
        _mouseService.SendAction(point, action);
    }

    private void OnCancelled(Point originPoint) {
        LogCancelled();
        _mouseService.MoveTo(originPoint);
        DeactivateOverlay();
    }

    private void OnInvalidKeyPressed() {
        LogInvalidKey();
        _gridRenderer?.FlashInvalidKey();
    }

    private void OnLevelExited(GridCell parentCell, IReadOnlyList<GridCell> cells, int level) {
        LogLevelExited(level, parentCell.Row, parentCell.Col);
        var center = GridCalculator.CenterOf(parentCell);
        _mouseService.MoveTo(center);

        // L3→L2: restore L2 subgrid view
        _subgridCells = _l2SubgridCells;
        _gridRenderer?.RenderSubgridOverGrid(_l1Cells, cells);
    }

    private void OnColumnUnhighlighted(int level) {
        LogColumnUnhighlighted(level);
        if (level == 1) {
            _mouseService.MoveTo(_origin);
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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Hotkey activated")]
    private partial void LogHotkeyActivated();

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to install keyboard hook")]
    private partial void LogHookInstallFailed();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Key: {Key}  State: {State}")]
    private partial void LogKeyPressed(Input.VKey key, NavigatorState state);

    [LoggerMessage(Level = LogLevel.Debug, Message = "State: {Before} → {After}")]
    private partial void LogStateTransition(NavigatorState before, NavigatorState after);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Overlay focus lost")]
    private partial void LogFocusLost();

    [LoggerMessage(Level = LogLevel.Debug, Message = "ColumnHighlighted: col={Col} level={Level} cells={Count}")]
    private partial void LogColumnHighlighted(int col, int level, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "CellHighlighted: row={Row} col={Col} hasSubgrid={HasSubgrid}")]
    private partial void LogCellHighlighted(int row, int col, bool hasSubgrid);

    [LoggerMessage(Level = LogLevel.Debug, Message = "CellEntered: row={Row} col={Col} level={Level} subgrid={Count}")]
    private partial void LogCellEntered(int row, int col, int level, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Action requested: {Action} at ({X}, {Y})")]
    private partial void LogActionRequested(MouseAction action, int x, int y);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Navigation cancelled, restoring cursor")]
    private partial void LogCancelled();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Invalid key pressed")]
    private partial void LogInvalidKey();

    [LoggerMessage(Level = LogLevel.Debug, Message = "LevelExited: level={Level} parentRow={Row} parentCol={Col}")]
    private partial void LogLevelExited(int level, int row, int col);

    [LoggerMessage(Level = LogLevel.Debug, Message = "ColumnUnhighlighted: level={Level}")]
    private partial void LogColumnUnhighlighted(int level);
}
