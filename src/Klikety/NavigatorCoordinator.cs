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
/// Manages split-screen left/right cell lists.
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

    // Split-screen cell lists
    private IReadOnlyList<GridCell> _leftCells = [];
    private IReadOnlyList<GridCell> _rightCells = [];
    private IReadOnlyList<GridCell> _currentCells = [];
    private ScreenHalf _activeHalf;
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

        // Wire events
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
        int halfWidth = screenBounds.Width / 2;

        var leftBounds = new Rectangle(screenBounds.X, screenBounds.Y, halfWidth, screenBounds.Height);
        var rightBounds = new Rectangle(screenBounds.X + halfWidth, screenBounds.Y,
            screenBounds.Width - halfWidth, screenBounds.Height);

        _leftCells = GridCalculator.Calculate(
            leftBounds,
            _config.KeySets.Left.FirstKeys.Length,
            _config.KeySets.Left.SecondKeys.Length);

        _rightCells = GridCalculator.Calculate(
            rightBounds,
            _config.KeySets.Right.FirstKeys.Length,
            _config.KeySets.Right.SecondKeys.Length);

        var origin = NativeMethods.GetCursorPosition();

        _overlayWindow.Show();

        if (!_hookService.Enable()) {
            _logger.LogError("Failed to install keyboard hook");
            DeactivateOverlay();
            return;
        }

        // Pass separate left/right cell lists to state machine
        _currentCells = _leftCells; // default before half is selected
        _activeHalf = ScreenHalf.Left;
        _stateMachine.Activate(_leftCells, _rightCells, origin);
        _gridRenderer?.RenderBothHalves(_leftCells, _rightCells);
    }

    private void OnKeyPressed(object? sender, Input.VKey vkey) {
        _stateMachine.OnKey(vkey);
    }

    private void OnFocusLost(object? sender, EventArgs e) {
        _logger.LogDebug("Overlay focus lost");
        DeactivateOverlay();
    }

    private void OnColumnHighlighted(ScreenHalf half, int col, IReadOnlyList<GridCell> cells, int level) {
        _activeHalf = half;
        _currentCells = cells;
        _gridRenderer?.SetActiveHalf(half);

        if (level == 1) {
            _gridRenderer?.HighlightColumnSplitScreen(_leftCells, _rightCells, half, col);
        } else {
            _gridRenderer?.HighlightColumn(cells, col);
        }
    }

    private void OnCellHighlighted(GridCell cell) {
        if (_stateMachine.State is NavigatorState.L1_AwaitFirst or NavigatorState.L1_AwaitSecond
                                or NavigatorState.L1_AwaitAction) {
            _gridRenderer?.HighlightCellSplitScreen(_leftCells, _rightCells, _activeHalf, cell);
        } else {
            _gridRenderer?.HighlightCell(_currentCells, cell);
        }
    }

    private void OnCellEntered(GridCell cell, int level) {
        var center = GridCalculator.CenterOf(cell);
        _mouseService.MoveTo(center);
        // Subgrid rendering driven by ColumnHighlighted at L2/L3 via HandleActionOrNav.
    }

    private void OnActionRequested(Point point, MouseAction action) {
        _logger.LogDebug("Action requested: {Action} at ({X}, {Y})", action, point.X, point.Y);
        _mouseService.SendAction(point, action);
        DeactivateOverlay();
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
        var center = GridCalculator.CenterOf(parentCell);
        _mouseService.MoveTo(center);
        _currentCells = cells;

        if (level == 1) {
            _gridRenderer?.RenderBothHalves(_leftCells, _rightCells);
        } else {
            _gridRenderer?.SetActiveHalf(_activeHalf);
            _gridRenderer?.RenderSubgrid(cells);
        }
    }

    private void OnColumnUnhighlighted(int level) {
        if (level == 1) {
            _activeHalf = ScreenHalf.Left;
        }

        _gridRenderer?.SetActiveHalf(_activeHalf);
        if (level == 1) {
            _gridRenderer?.RenderBothHalves(_leftCells, _rightCells);
        } else {
            _gridRenderer?.RenderSubgrid(_currentCells);
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
