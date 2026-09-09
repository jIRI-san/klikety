using System.Drawing;

using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Navigation;
using Klikety.Overlay;
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
    public double DpiScale { get; set; } = 1.0;
    public Rectangle GetPrimaryScreenBounds() => Bounds;
    public double GetDpiScale() => DpiScale;
}

public sealed class FakeKeyboardLayoutProvider : IKeyboardLayoutProvider {
    /// <summary>
    /// US English QWERTY layout handle (0x04090409).
    /// </summary>
    public nint Layout { get; set; } = 0x04090409;
    public bool Qwerty { get; set; } = true;
    public int GetActiveKeyboardLayoutCalls { get; private set; }
    public nint GetActiveKeyboardLayout() {
        GetActiveKeyboardLayoutCalls++;
        return Layout;
    }
    public bool IsQwertyCompatible() => Qwerty;
}

public sealed class FakeForegroundWindowProvider : IForegroundWindowProvider {
    public nint Handle { get; set; }
    public nint LastSetForegroundHwnd { get; private set; }
    public Rectangle Bounds { get; set; } = Rectangle.Empty;
    public string Title { get; set; } = string.Empty;
    public nint GetForegroundWindowHandle() => Handle;
    public void SetForegroundWindow(nint hwnd) => LastSetForegroundHwnd = hwnd;
    public Rectangle GetWindowBounds(nint hwnd) => hwnd == Handle && Handle != 0 ? Bounds : Rectangle.Empty;
    public string GetWindowTitle(nint hwnd) => hwnd == Handle && Handle != 0 ? Title : string.Empty;
}

public sealed class FakeDisplayCatalog : IDisplayCatalog {
    public DisplayCatalogResult Result { get; set; } = DisplayCatalogResult.Ok(
        new DisplaySnapshot(
            [new DisplayInfo(new Rectangle(0, 0, 1920, 1080), 1.0, @"\\.\DISPLAY1", @"\\?\FAKE#PRIMARY")],
            new Rectangle(0, 0, 1920, 1080)));

    public DisplayCatalogResult GetSnapshot() => Result;
}

public sealed class FakePlatformServices : IPlatformServices {
    public FakeKeyStateProvider KeyState { get; } = new();
    public FakeTimerFactory Timers { get; } = new();
    public FakeCursorPositionProvider Cursor { get; } = new();
    public FakeScreenBoundsProvider Screen { get; } = new();
    public FakeKeyboardLayoutProvider KeyboardLayout { get; } = new();
    public FakeForegroundWindowProvider ForegroundWindow { get; } = new();
    public FakeDisplayCatalog DisplayCatalog { get; } = new();

    IKeyStateProvider IPlatformServices.KeyState => KeyState;
    ITimerFactory IPlatformServices.Timers => Timers;
    ICursorPositionProvider IPlatformServices.Cursor => Cursor;
    IScreenBoundsProvider IPlatformServices.Screen => Screen;
    IKeyboardLayoutProvider IPlatformServices.KeyboardLayout => KeyboardLayout;
    IForegroundWindowProvider IPlatformServices.ForegroundWindow => ForegroundWindow;
    IDisplayCatalog IPlatformServices.DisplayCatalog => DisplayCatalog;
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

    public void DrainAndDisable() {
        IsEnabled = false;
    }

    public void Dispose() {
        Disable();
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
    public List<(Point Point, MouseAction? Action, ActionModifiers Modifiers)> Calls { get; } = [];
    public List<(int WheelDelta, ActionModifiers Modifiers)> ScrollCalls { get; } = [];
    public List<(Point Start, Point End, MouseAction Button, ActionModifiers Modifiers)> DragCalls { get; } = [];

    public void MoveTo(Point physicalPoint) {
        Calls.Add((physicalPoint, null, ActionModifiers.None));
    }

    public void SendAction(Point physicalPoint, MouseAction action, ActionModifiers modifiers = ActionModifiers.None) {
        Calls.Add((physicalPoint, action, modifiers));
    }

    public void SendScroll(int wheelDelta, ActionModifiers modifiers = ActionModifiers.None) {
        ScrollCalls.Add((wheelDelta, modifiers));
    }

    public void SendDrag(Point start, Point end, MouseAction button, ActionModifiers modifiers = ActionModifiers.None) {
        DragCalls.Add((start, end, button, modifiers));
    }

    public int ClearStuckModifiersCalls { get; private set; }
    public void ClearStuckModifiers() => ClearStuckModifiersCalls++;
}

public sealed class FakeModifierDetector : IModifierDetector {
    public ActionModifiers Modifiers { get; set; } = ActionModifiers.None;
    public ActionModifiers GetCurrentModifiers() => Modifiers;
}

public sealed class FakeSatelliteOverlay : ISatelliteOverlay {
    public Rectangle LastBounds { get; private set; }
    public int? LastNumber { get; private set; }
    public int ShowCount { get; private set; }
    public int HideCount { get; private set; }

    public void Show(Rectangle physicalBounds, int? number) {
        LastBounds = physicalBounds;
        LastNumber = number;
        ShowCount++;
    }

    public void Hide() => HideCount++;

    public void Dispose() { }
}

public sealed class FakeOverlayWindow : IOverlayWindow {
    public event EventHandler? FocusLost;
    public event EventHandler? DisplayChanged;
    public event EventHandler? KeyboardLayoutChanged;
    public bool IsVisible { get; private set; }
    public int ShowCount { get; private set; }
    public int HideCount { get; private set; }
    public Rectangle LastShowBounds { get; private set; }
    public bool RaiseFocusLostOnHide { get; set; }

    public void Show() {
        Show(new Rectangle(0, 0, 1920, 1080));
    }

    public void Show(Rectangle bounds) {
        IsVisible = true;
        ShowCount++;
        LastShowBounds = bounds;
    }

    public void Hide() {
        // Mirror real OverlayWindow.Hide(): clear all content on hide
        ClearCanvasCount++;
        StatusText = null;
        ClearStatusTextCount++;
        RecordingBorderVisible = false;
        AppScopeBorderVisible = false;
        IsVisible = false;
        HideCount++;
        if (RaiseFocusLostOnHide) {
            FocusLost?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ClearCanvas() {
        ClearCanvasCount++;
    }

    public void Close() {
        IsVisible = false;
    }

    public int ClearCanvasCount { get; private set; }
    public string? StatusText { get; private set; }
    public int ShowStatusTextCount { get; private set; }
    public int ClearStatusTextCount { get; private set; }
    public bool RecordingBorderVisible { get; private set; }
    public bool AppScopeBorderVisible { get; private set; }

    public void ShowStatusText(string text) {
        StatusText = text;
        ShowStatusTextCount++;
    }

    public void ClearStatusText() {
        StatusText = null;
        ClearStatusTextCount++;
    }

    public void SetRecordingBorder(bool visible) {
        RecordingBorderVisible = visible;
    }

    public void SetAppScopeBorder(bool visible, System.Drawing.Rectangle bounds = default) {
        AppScopeBorderVisible = visible;
        AppScopeBorderBounds = bounds;
    }

    public Rectangle AppScopeBorderBounds { get; private set; }

    public void SimulateFocusLoss() {
        FocusLost?.Invoke(this, EventArgs.Empty);
    }

    public void SimulateDisplayChange() {
        DisplayChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SimulateKeyboardLayoutChange() {
        KeyboardLayoutChanged?.Invoke(this, EventArgs.Empty);
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
    public int RebuildLabelsCalls { get; private set; }

    public void RebuildLabels(IKeyLabelResolver resolver) => RebuildLabelsCalls++;
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
    public int RebuildLabelsCalls { get; private set; }

    public void RebuildLabels(IKeyLabelResolver resolver) => RebuildLabelsCalls++;
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
    public int RebuildLabelsCalls { get; private set; }

    public void RebuildLabels(IKeyLabelResolver resolver) => RebuildLabelsCalls++;
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
    public int RebuildLabelsCalls { get; private set; }

    public void RebuildLabels(IKeyLabelResolver resolver) => RebuildLabelsCalls++;
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

    public void RenderFirstKeyIndicator(LogGrid grid, int column, System.Drawing.Rectangle screenBounds)
        => Calls.Add(new("RenderFirstKeyIndicator", grid, Col: column, ScreenBounds: screenBounds));

    public void HideFirstKeyIndicator()
        => Calls.Add(new("HideFirstKeyIndicator"));
}

/// <summary>
/// Fake TimeProvider for testing KeyPressProcessor. Timestamp advances manually.
/// </summary>
public sealed class FakeTimeProvider : TimeProvider {
    private long _timestamp;

    public override long TimestampFrequency => 1000; // 1 tick = 1ms for simplicity

    public override long GetTimestamp() => _timestamp;

    public void Advance(TimeSpan duration) {
        _timestamp += (long)(duration.TotalMilliseconds * TimestampFrequency / 1000);
    }

    public void SetTimestamp(long ticks) => _timestamp = ticks;
}

public sealed class FakeMacroStore : IMacroStore {
    public MacrosFile LastSavedFile { get; private set; } = new();
    public int SaveCount { get; private set; }
    public bool ShouldFailSave { get; set; }
    public MacrosFile FileToLoad { get; set; } = new();
    public IReadOnlyList<string> LoadErrors { get; set; } = [];

    public MacroLoadResult Load() => new() { File = FileToLoad, Errors = LoadErrors };

    public MacroSaveResult Save(MacrosFile file) {
        SaveCount++;
        LastSavedFile = file;
        return ShouldFailSave
            ? new MacroSaveResult { Success = false, Error = "Fake save failure" }
            : new MacroSaveResult { Success = true };
    }
}

public sealed class FakeMacroPickerWindow : IMacroPickerWindow {
    public event Action<int>? SlotSelected;
    public event Action? PickerClosed;

    public bool IsShown { get; private set; }
    public int ShowCount { get; private set; }
    public MacroDefinition?[]? LastMacros { get; private set; }
    public VKey[]? LastSlotKeys { get; private set; }

    public void Show(MacroDefinition?[] macros, VKey[] slotKeys) {
        IsShown = true;
        ShowCount++;
        LastMacros = macros;
        LastSlotKeys = slotKeys;
    }

    public void Close() => IsShown = false;

    public void SimulateSlotSelected(int slot) => SlotSelected?.Invoke(slot);
    public void SimulatePickerClosed() => PickerClosed?.Invoke();
}

public sealed class FakeDelayProvider : IDelayProvider {
    public List<int> RecordedDelays { get; } = [];

    public Task Delay(int milliseconds, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        RecordedDelays.Add(milliseconds);
        return Task.CompletedTask;
    }
}

public sealed class FakeMacroPlaybackWindow : IMacroPlaybackWindow {
    public bool IsShown { get; private set; }
    public string? LastMacroName { get; private set; }
    public int LastTotalSteps { get; private set; }
    public int LastCompletedSteps { get; private set; }

    public void Show(string macroName, int totalSteps, string? windowContext = null) {
        IsShown = true;
        LastMacroName = macroName;
        LastTotalSteps = totalSteps;
    }

    public void UpdateProgress(int completedSteps, int totalSteps) {
        LastCompletedSteps = completedSteps;
        LastTotalSteps = totalSteps;
    }

    public int LastDelayRemainingMs { get; private set; }
    public string? LastDelayActionType { get; private set; }
    public void UpdateDelay(int remainingMs, string actionType) {
        LastDelayRemainingMs = remainingMs;
        LastDelayActionType = actionType;
    }

    public void Close() => IsShown = false;
}

public sealed class FakeClickIndicator : IClickIndicator {
    public List<(double X, double Y)> ShownPositions { get; } = [];

    public Task ShowAndWait(double screenX, double screenY) {
        ShownPositions.Add((screenX, screenY));
        return Task.CompletedTask;
    }
}
