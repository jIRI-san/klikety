using System.Drawing;
using Klikety.Config;
using Klikety.Grid;
using Klikety.Interop;
using Klikety.Navigation;
using Klikety.Overlay;
using Klikety.Services;
using Microsoft.Extensions.Logging;

namespace Klikety;

/// <summary>
/// Wires all services together: hotkey → overlay → hook → state machine → mouse action.
/// Single DeactivateOverlay() method covers all exit paths.
/// </summary>
public sealed class NavigatorCoordinator
{
    private readonly IHotKeyService _hotKeyService;
    private readonly IKeyboardHookService _hookService;
    private readonly IMouseActionService _mouseService;
    private readonly IOverlayWindow _overlayWindow;
    private readonly NavigatorStateMachine _stateMachine;
    private readonly GridRenderer? _gridRenderer;
    private readonly LabelGenerator _labelGenerator;
    private readonly ConfigModel _config;
    private readonly ILogger _logger;

    private IReadOnlyList<GridCell> _currentCells = [];
    private bool _deactivating;

    public NavigatorCoordinator(
        IHotKeyService hotKeyService,
        IKeyboardHookService hookService,
        IMouseActionService mouseService,
        IOverlayWindow overlayWindow,
        NavigatorStateMachine stateMachine,
        GridRenderer? gridRenderer,
        LabelGenerator labelGenerator,
        ConfigModel config,
        ILogger logger)
    {
        _hotKeyService = hotKeyService;
        _hookService = hookService;
        _mouseService = mouseService;
        _overlayWindow = overlayWindow;
        _stateMachine = stateMachine;
        _gridRenderer = gridRenderer;
        _labelGenerator = labelGenerator;
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
    }

    private void OnHotKeyActivated(object? sender, EventArgs e)
    {
        _logger.LogDebug("Hotkey activated");

        var screenBounds = NativeMethods.GetPrimaryScreenBounds();
        _currentCells = GridCalculator.Calculate(
            screenBounds,
            _config.KeySets.FirstKeys.Length,
            _config.KeySets.SecondKeys.Length);

        var origin = NativeMethods.GetCursorPosition();

        _overlayWindow.Show();

        if (!_hookService.Enable())
        {
            _logger.LogError("Failed to install keyboard hook");
            DeactivateOverlay();
            // TODO: show tray notification
            return;
        }

        _stateMachine.Activate(_currentCells, origin);
        _gridRenderer?.RenderGrid(_currentCells);
    }

    private void OnKeyPressed(object? sender, Input.VKey vkey)
    {
        _stateMachine.OnKey(vkey);
    }

    private void OnFocusLost(object? sender, EventArgs e)
    {
        _logger.LogDebug("Overlay focus lost");
        DeactivateOverlay();
    }

    private void OnColumnHighlighted(int col)
    {
        _gridRenderer?.HighlightColumn(_currentCells, col);
    }

    private void OnCellHighlighted(GridCell cell)
    {
        _gridRenderer?.HighlightCell(_currentCells, cell);
    }

    private void OnCellEntered(GridCell cell, int level)
    {
        if (level > 1)
        {
            var subCells = SubgridCalculator.Calculate(
                cell,
                _config.KeySets.FirstKeys.Length,
                _config.KeySets.SecondKeys.Length);
            _currentCells = subCells;
            _gridRenderer?.RenderSubgrid(subCells);
        }
    }

    private void OnActionRequested(Point point, MouseAction action)
    {
        _logger.LogDebug("Action requested: {Action} at ({X}, {Y})", action, point.X, point.Y);
        _mouseService.SendAction(point, action);
        DeactivateOverlay();
    }

    private void OnCancelled(Point originPoint)
    {
        _logger.LogDebug("Navigation cancelled, restoring cursor");
        _mouseService.MoveTo(originPoint);
        DeactivateOverlay();
    }

    /// <summary>
    /// Single idempotent exit method. Called from action, cancel, focus-loss, exception, quit.
    /// </summary>
    public void DeactivateOverlay()
    {
        if (_deactivating) return;
        _deactivating = true;

        try
        {
            _hookService.Disable();
            _overlayWindow.Hide();
            _stateMachine.Reset();
        }
        finally
        {
            _deactivating = false;
        }
    }
}
