using System.Drawing;

using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Services;

namespace Klikety.Navigation;

/// <summary>
/// UniformGrid mode session. Wraps <see cref="NavigatorStateMachine"/> and
/// <see cref="IGridRenderer"/>, converting <see cref="ModeConfig"/> booleans
/// to the legacy <see cref="NavigationMode"/> enum internally.
/// </summary>
public sealed class UniformGridSession : IModeSession {
    private readonly VKey[] _firstKeys;
    private readonly VKey[] _secondKeys;
    private readonly ActionMapper _actionMapper;
    private readonly NavigationMode _navigationMode;
    private readonly int _level3Threshold;
    private readonly int _minCellPx;
    private readonly int _baseLabelColOffset;
    private readonly int _baseLabelRowOffset;
    private readonly IGridRenderer? _gridRenderer;

    private NavigatorStateMachine? _stateMachine;
    private Point _origin;

    // Cell lists per level for rendering context
    private IReadOnlyList<GridCell> _l1Cells = [];
    private IReadOnlyList<GridCell>? _subgridCells;
    private IReadOnlyList<GridCell>? _l2SubgridCells;

    public event Action<Point, MouseAction>? ActionRequested;
    public event Action? Cancelled;
    public event Action<Point>? CursorMoveRequested;

    public UniformGridSession(
        VKey[] firstKeys,
        VKey[] secondKeys,
        ActionMapper actionMapper,
        ModeConfig modeConfig,
        int level3Threshold,
        IGridRenderer? gridRenderer,
        int minCellPx = 10,
        int baseLabelColOffset = 0,
        int baseLabelRowOffset = 0) {
        _firstKeys = firstKeys;
        _secondKeys = secondKeys;
        _actionMapper = actionMapper;
        _level3Threshold = level3Threshold;
        _minCellPx = minCellPx;
        _baseLabelColOffset = baseLabelColOffset;
        _baseLabelRowOffset = baseLabelRowOffset;
        _gridRenderer = gridRenderer;

        // Convert ModeConfig booleans to legacy NavigationMode enum
        _navigationMode = (modeConfig.TwoKey, modeConfig.ArrowKeys) switch {
            (true, true) => NavigationMode.Both,
            (true, false) => NavigationMode.TwoKey,
            (false, true) => NavigationMode.Arrow,
            _ => NavigationMode.Both, // fallback
        };
    }

    public void Activate(Rectangle screenBounds, Point origin) {
        _origin = origin;
        _l1Cells = GridCalculator.Calculate(
            screenBounds,
            _firstKeys.Length,
            _secondKeys.Length);
        _subgridCells = null;
        _l2SubgridCells = null;

        _stateMachine = new NavigatorStateMachine(
            _firstKeys, _secondKeys, _actionMapper, _navigationMode, _level3Threshold, _minCellPx);

        _stateMachine.ColumnHighlighted += OnColumnHighlighted;
        _stateMachine.CellHighlighted += OnCellHighlighted;
        _stateMachine.CellEntered += OnCellEntered;
        _stateMachine.ActionRequested += OnActionRequested;
        _stateMachine.Cancelled += OnCancelled;
        _stateMachine.InvalidKeyPressed += OnInvalidKeyPressed;
        _stateMachine.LevelExited += OnLevelExited;
        _stateMachine.ColumnUnhighlighted += OnColumnUnhighlighted;

        _stateMachine.Activate(_l1Cells, origin);
        _gridRenderer?.SetLabelOffset(_baseLabelColOffset, _baseLabelRowOffset);
        _gridRenderer?.RenderGrid(_l1Cells);
    }

    public void OnKey(VKey key) {
        _stateMachine?.OnKey(key);
    }

    public void Deactivate() {
        if (_stateMachine is not null) {
            _stateMachine.ColumnHighlighted -= OnColumnHighlighted;
            _stateMachine.CellHighlighted -= OnCellHighlighted;
            _stateMachine.CellEntered -= OnCellEntered;
            _stateMachine.ActionRequested -= OnActionRequested;
            _stateMachine.Cancelled -= OnCancelled;
            _stateMachine.InvalidKeyPressed -= OnInvalidKeyPressed;
            _stateMachine.LevelExited -= OnLevelExited;
            _stateMachine.ColumnUnhighlighted -= OnColumnUnhighlighted;
            _stateMachine.Reset();
            _stateMachine = null;
        }

        _l1Cells = [];
        _subgridCells = null;
        _l2SubgridCells = null;
    }

    // --- SM event handlers (mirror coordinator logic) ---

    private void OnColumnHighlighted(int col, IReadOnlyList<GridCell> cells, int level) {
        if (level == 1) {
            _gridRenderer?.SetLabelOffset(_baseLabelColOffset, _baseLabelRowOffset);
            _gridRenderer?.HighlightColumn(_l1Cells, col);
        } else {
            SetLabelOffsetForLevel(level);
            _gridRenderer?.HighlightColumnOverGrid(_l1Cells, cells, col);
        }
    }

    private void OnCellHighlighted(GridCell cell) {
        if (_subgridCells == null) {
            _gridRenderer?.HighlightCell(_l1Cells, cell);
        } else {
            _gridRenderer?.HighlightCellOverGrid(_l1Cells, _subgridCells, cell);
        }
    }

    private void OnCellEntered(GridCell cell, IReadOnlyList<GridCell> subgridCells, int level) {
        var center = GridCalculator.CenterOf(cell);
        CursorMoveRequested?.Invoke(center);

        if (subgridCells.Count > 0) {
            if (level == 1) {
                _subgridCells = subgridCells;
                _l2SubgridCells = subgridCells;
            } else if (level == 2) {
                _subgridCells = subgridCells;
            }
            SetLabelOffsetForLevel(level + 1);
            _gridRenderer?.RenderSubgridOverGrid(_l1Cells, subgridCells);
        } else if (_subgridCells != null) {
            _gridRenderer?.HighlightCellOverGrid(_l1Cells, _subgridCells, cell);
        } else {
            _gridRenderer?.HighlightCell(_l1Cells, cell);
        }
    }

    private void OnActionRequested(Point point, MouseAction action) {
        ActionRequested?.Invoke(point, action);
    }

    private void OnCancelled(Point _) {
        // Restore cursor to origin, then signal coordinator
        CursorMoveRequested?.Invoke(_origin);
        Cancelled?.Invoke();
    }

    private void OnInvalidKeyPressed() {
        _gridRenderer?.FlashInvalidKey();
    }

    private void OnLevelExited(GridCell parentCell, IReadOnlyList<GridCell> cells, int level) {
        var center = GridCalculator.CenterOf(parentCell);
        CursorMoveRequested?.Invoke(center);

        _subgridCells = _l2SubgridCells;
        SetLabelOffsetForLevel(level - 1);
        _gridRenderer?.RenderSubgridOverGrid(_l1Cells, cells);
    }

    private void OnColumnUnhighlighted(int level) {
        if (level == 1) {
            CursorMoveRequested?.Invoke(_origin);
            _subgridCells = null;
            _l2SubgridCells = null;
            _gridRenderer?.SetLabelOffset(_baseLabelColOffset, _baseLabelRowOffset);
            _gridRenderer?.RenderGrid(_l1Cells);
        } else {
            _subgridCells = _l2SubgridCells;
            SetLabelOffsetForLevel(level);
            if (_subgridCells != null) {
                _gridRenderer?.RenderSubgridOverGrid(_l1Cells, _subgridCells);
            }
        }
    }

    private void SetLabelOffsetForLevel(int level) {
        if (_stateMachine is not null) {
            var (col, row) = _stateMachine.GetLabelOffsetForLevel(level);
            _gridRenderer?.SetLabelOffset(col + _baseLabelColOffset, row + _baseLabelRowOffset);
        }
    }
}
