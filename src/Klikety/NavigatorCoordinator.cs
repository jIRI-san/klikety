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
/// Manages split-screen left/right cell lists.
/// </summary>
public sealed class NavigatorCoordinator
{
    private readonly IHotKeyService _hotKeyService;
    private readonly IKeyboardHookService _hookService;
    private readonly IMouseActionService _mouseService;
    private readonly IOverlayWindow _overlayWindow;
    private readonly NavigatorStateMachine _stateMachine;
  private readonly GridRenderer? _gridRenderer;
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
        GridRenderer? gridRenderer,
        ConfigModel config,
        ILogger logger)
    {
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
  }

  private void OnHotKeyActivated(object? sender, EventArgs e)
    {
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

        if (!_hookService.Enable())
        {
            _logger.LogError("Failed to install keyboard hook");
      DeactivateOverlay();
      return;
        }

    // Pass all cells (left + right merged) to state machine for L1
    var allCells = new List<GridCell>(_leftCells.Count + _rightCells.Count);
    allCells.AddRange(_leftCells);
    allCells.AddRange(_rightCells);
    _currentCells = allCells;

    _stateMachine.Activate(allCells, origin);
    _gridRenderer?.RenderBothHalves(_leftCells, _rightCells);
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

  private void OnColumnHighlighted(ScreenHalf half, int col)
  {
    _activeHalf = half;
    _currentCells = half == ScreenHalf.Left ? _leftCells : _rightCells;
    _gridRenderer?.HighlightColumn(_currentCells, col);
    }

    private void OnCellHighlighted(GridCell cell)
    {
        _gridRenderer?.HighlightCell(_currentCells, cell);
    }

    private void OnCellEntered(GridCell cell, int level)
    {
    var center = GridCalculator.CenterOf(cell);
    _mouseService.MoveTo(center);

    if (level > 1)
        {
      var activeKeys = _activeHalf == ScreenHalf.Left ? _config.KeySets.Left : _config.KeySets.Right;
      var subCells = SubgridCalculator.Calculate(
                cell,
                activeKeys.FirstKeys.Length,
                activeKeys.SecondKeys.Length);
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

  private void OnInvalidKeyPressed()
  {
    _logger.LogDebug("Invalid key pressed");
    _gridRenderer?.FlashInvalidKey();
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
