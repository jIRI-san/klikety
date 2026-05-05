using System.Drawing;

using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Navigation;
using Klikety.Services;

namespace Klikety.Tests.Fakes;

// --- Platform Services Fakes ---

public sealed class FakeKeyStateProvider : IKeyStateProvider {
    private readonly HashSet<VKey> _downKeys = [];

    public void SetKeyDown(VKey key) => _downKeys.Add(key);
    public void SetKeyUp(VKey key) => _downKeys.Remove(key);
    public bool IsKeyDown(VKey key) => _downKeys.Contains(key);
}

public sealed class FakeTimer : IDebounceTimer {
    public event Action? Elapsed;
    public bool IsRunning { get; private set; }
    public TimeSpan? LastInterval { get; private set; }

    public void Start(TimeSpan interval) {
        LastInterval = interval;
        IsRunning = true;
    }

    public void Stop() {
        IsRunning = false;
    }

    public void SimulateElapsed() => Elapsed?.Invoke();

    public void Dispose() => Stop();
}

public sealed class FakeTimerFactory : ITimerFactory {
    public FakeTimer LastCreated { get; private set; } = null!;

    public IDebounceTimer Create() {
        LastCreated = new FakeTimer();
        return LastCreated;
    }
}

public sealed class FakeCursorPositionProvider : ICursorPositionProvider {
    public Point Position { get; set; } = new(500, 500);
    public Point GetCursorPosition() => Position;
}

public sealed class FakeScreenBoundsProvider : IScreenBoundsProvider {
    public Rectangle Bounds { get; set; } = new(0, 0, 1920, 1080);
    public Rectangle GetPrimaryScreenBounds() => Bounds;
}

public sealed class FakeKeyboardLayoutProvider : IKeyboardLayoutProvider {
    /// <summary>
    /// US English QWERTY layout handle (0x04090409).
    /// </summary>
    public nint Layout { get; set; } = 0x04090409;
    public bool Qwerty { get; set; } = true;
    public nint GetActiveKeyboardLayout() => Layout;
    public bool IsQwertyCompatible() => Qwerty;
}

public sealed class FakePlatformServices : IPlatformServices {
    public FakeKeyStateProvider KeyState { get; } = new();
    public FakeTimerFactory Timers { get; } = new();
    public FakeCursorPositionProvider Cursor { get; } = new();
    public FakeScreenBoundsProvider Screen { get; } = new();
    public FakeKeyboardLayoutProvider KeyboardLayout { get; } = new();

    IKeyStateProvider IPlatformServices.KeyState => KeyState;
    ITimerFactory IPlatformServices.Timers => Timers;
    ICursorPositionProvider IPlatformServices.Cursor => Cursor;
    IScreenBoundsProvider IPlatformServices.Screen => Screen;
    IKeyboardLayoutProvider IPlatformServices.KeyboardLayout => KeyboardLayout;
}

// --- End Platform Services Fakes ---

/// <summary>
/// Test resolver that returns uppercase VKey name as the label.
/// Deterministic, no Win32 dependency.
/// </summary>
public sealed class FakeKeyLabelResolver : IKeyLabelResolver {
    public string Resolve(VKey key) => key.ToString().ToUpperInvariant();
}

public sealed class FakeKeyboardHookService : IKeyboardHookService {
    public event EventHandler<KeyHookEventArgs>? KeyEvent;
    public bool IsEnabled { get; private set; }
    public bool ShouldFailOnEnable { get; set; }

    public bool Enable() {
        if (ShouldFailOnEnable) {
            return false;
        }

        IsEnabled = true;
        return true;
    }

    public void Disable() {
        IsEnabled = false;
    }

    public void SimulateKeyDown(VKey vkey) {
        KeyEvent?.Invoke(this, new KeyHookEventArgs(vkey, true));
    }

    public void SimulateKeyUp(VKey vkey) {
        KeyEvent?.Invoke(this, new KeyHookEventArgs(vkey, false));
    }

    /// <summary>Convenience: simulates key-down (backward compat for existing tests).</summary>
    public void SimulateKey(VKey vkey) => SimulateKeyDown(vkey);
}

public sealed class FakeMouseActionService : IMouseActionService {
    public List<(Point Point, MouseAction? Action)> Calls { get; } = [];

    public void MoveTo(Point physicalPoint) {
        Calls.Add((physicalPoint, null));
    }

    public void SendAction(Point physicalPoint, MouseAction action) {
        Calls.Add((physicalPoint, action));
    }
}

public sealed class FakeOverlayWindow : IOverlayWindow {
    public event EventHandler? FocusLost;
    public bool IsVisible { get; private set; }
    public int ShowCount { get; private set; }
    public int HideCount { get; private set; }

    public void Show() {
        IsVisible = true;
        ShowCount++;
    }

    public void Hide() {
        IsVisible = false;
        HideCount++;
    }

    public void ClearCanvas() {
        ClearCanvasCount++;
    }

    public void Close() {
        IsVisible = false;
    }

    public int ClearCanvasCount { get; private set; }

    public void SimulateFocusLoss() {
        FocusLost?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class FakeHotKeyService : IHotKeyService {
    public event EventHandler? Activated;
    public bool IsRegistered { get; private set; }

    public bool Register(HotKeyConfig config) {
        IsRegistered = true;
        return true;
    }

    public void Unregister() {
        IsRegistered = false;
    }

    public void Dispose() {
        Unregister();
    }

    public void SimulateActivation() {
        Activated?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class FakeGridRenderer : IGridRenderer {
    public record RenderCall(string Method, IReadOnlyList<GridCell>? Cells = null,
        int? Col = null);

    public List<RenderCall> Calls { get; } = [];

    public void SetTransform(System.Windows.Media.Matrix m) { }

    public void SetLabelOffset(int colOffset, int rowOffset) { }

    public void RenderGrid(IReadOnlyList<GridCell> cells)
        => Calls.Add(new("RenderGrid", cells));

    public void HighlightColumn(IReadOnlyList<GridCell> cells, int col)
        => Calls.Add(new("HighlightColumn", cells, Col: col));

    public void HighlightCell(IReadOnlyList<GridCell> cells, GridCell cell)
        => Calls.Add(new("HighlightCell", cells));

    public void RenderSubgridOverGrid(IReadOnlyList<GridCell> bg, IReadOnlyList<GridCell> sub)
        => Calls.Add(new("RenderSubgridOverGrid", sub));

    public void HighlightColumnOverGrid(IReadOnlyList<GridCell> bg, IReadOnlyList<GridCell> sub, int col)
        => Calls.Add(new("HighlightColumnOverGrid", sub, Col: col));

    public void HighlightCellOverGrid(IReadOnlyList<GridCell> bg, IReadOnlyList<GridCell> sub, GridCell cell)
        => Calls.Add(new("HighlightCellOverGrid", sub));

    public void FlashInvalidKey()
        => Calls.Add(new("FlashInvalidKey"));
}

public sealed class FakeCrosshairRenderer : ICrosshairRenderer {
    public record RenderCall(string Method, CrosshairGrid? Grid = null,
        int? Col = null, int? Row = null, GridCell? Cell = null);

    public List<RenderCall> Calls { get; } = [];

    public void SetTransform(System.Windows.Media.Matrix m) { }

    public void SetLabelOffset(int horizOffset, int vertOffset) { }

    public void RenderCross(CrosshairGrid grid)
        => Calls.Add(new("RenderCross", grid));

    public void HighlightColumn(CrosshairGrid grid, int col)
        => Calls.Add(new("HighlightColumn", grid, Col: col));

    public void HighlightRow(CrosshairGrid grid, int row)
        => Calls.Add(new("HighlightRow", grid, Row: row));

    public void HighlightCell(CrosshairGrid grid, GridCell cell)
        => Calls.Add(new("HighlightCell", grid, Cell: cell));

    public void RenderSubgridCross(CrosshairGrid parentGrid, CrosshairGrid subgrid, GridCell parentCell)
        => Calls.Add(new("RenderSubgridCross", subgrid, Cell: parentCell));

    public void FlashInvalidKey()
        => Calls.Add(new("FlashInvalidKey"));
}

public sealed class FakeLogCrosshairRenderer : ILogCrosshairRenderer {
    public record RenderCall(string Method, LogCrosshairGrid? Grid = null,
        int? Col = null, int? Row = null, GridCell? Cell = null);

    public List<RenderCall> Calls { get; } = [];

    public void SetTransform(System.Windows.Media.Matrix m) { }

    public void RenderCross(LogCrosshairGrid grid)
        => Calls.Add(new("RenderCross", grid));

    public void HighlightColumn(LogCrosshairGrid grid, int col)
        => Calls.Add(new("HighlightColumn", grid, Col: col));

    public void HighlightRow(LogCrosshairGrid grid, int row)
        => Calls.Add(new("HighlightRow", grid, Row: row));

    public void HighlightCell(LogCrosshairGrid grid, GridCell cell)
        => Calls.Add(new("HighlightCell", grid, Cell: cell));

    public void FlashInvalidKey()
        => Calls.Add(new("FlashInvalidKey"));
}

public sealed class FakeLogGridRenderer : ILogGridRenderer {
    public record RenderCall(string Method, LogGrid? Grid = null,
        int? Col = null, GridCell? Cell = null, string? Label = null,
        System.Drawing.Rectangle? ScreenBounds = null);

    public List<RenderCall> Calls { get; } = [];

    public void SetTransform(System.Windows.Media.Matrix m) { }

    public void RenderGrid(LogGrid grid)
        => Calls.Add(new("RenderGrid", grid));

    public void HighlightColumn(LogGrid grid, int col)
        => Calls.Add(new("HighlightColumn", grid, Col: col));

    public void HighlightCell(LogGrid grid, GridCell cell)
        => Calls.Add(new("HighlightCell", grid, Cell: cell));

    public void FlashInvalidKey()
        => Calls.Add(new("FlashInvalidKey"));

    public void ClearCanvas()
        => Calls.Add(new("ClearCanvas"));

    public void RenderFirstKeyIndicator(LogGrid grid, string label, System.Drawing.Rectangle screenBounds)
        => Calls.Add(new("RenderFirstKeyIndicator", grid, Label: label, ScreenBounds: screenBounds));

    public void HideFirstKeyIndicator()
        => Calls.Add(new("HideFirstKeyIndicator"));
}
