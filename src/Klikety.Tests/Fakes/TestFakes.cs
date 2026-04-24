using System.Drawing;
using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Navigation;
using Klikety.Services;

namespace Klikety.Tests.Fakes;

public sealed class FakeKeyboardHookService : IKeyboardHookService
{
    public event EventHandler<VKey>? KeyPressed;
    public bool IsEnabled { get; private set; }
    public bool ShouldFailOnEnable { get; set; }

    public bool Enable()
    {
        if (ShouldFailOnEnable) return false;
        IsEnabled = true;
        return true;
    }

    public void Disable()
    {
        IsEnabled = false;
    }

    public void SimulateKey(VKey vkey)
    {
        KeyPressed?.Invoke(this, vkey);
    }
}

public sealed class FakeMouseActionService : IMouseActionService
{
    public List<(Point Point, MouseAction? Action)> Calls { get; } = [];

    public void MoveTo(Point physicalPoint)
    {
        Calls.Add((physicalPoint, null));
    }

    public void SendAction(Point physicalPoint, MouseAction action)
    {
        Calls.Add((physicalPoint, action));
    }
}

public sealed class FakeOverlayWindow : IOverlayWindow
{
    public event EventHandler? FocusLost;
    public bool IsVisible { get; private set; }
    public int ShowCount { get; private set; }
    public int HideCount { get; private set; }

    public void Show()
    {
        IsVisible = true;
        ShowCount++;
    }

    public void Hide()
    {
        IsVisible = false;
        HideCount++;
    }

    public void SimulateFocusLoss()
    {
        FocusLost?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class FakeHotKeyService : IHotKeyService
{
    public event EventHandler? Activated;
    public bool IsRegistered { get; private set; }

    public bool Register(HotKeyConfig config)
    {
        IsRegistered = true;
        return true;
    }

    public void Unregister()
    {
        IsRegistered = false;
    }

    public void Dispose()
    {
        Unregister();
    }

    public void SimulateActivation()
    {
        Activated?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class FakeGridRenderer : IGridRenderer {
    public record RenderCall(string Method, IReadOnlyList<GridCell>? Cells = null,
        ScreenHalf? Half = null, int? Col = null);

    public List<RenderCall> Calls { get; } = [];
    public ScreenHalf? ActiveHalf { get; private set; }

    public void SetTransform(System.Windows.Media.Matrix m) { }

    public void SetActiveHalf(ScreenHalf half) {
        ActiveHalf = half;
        Calls.Add(new("SetActiveHalf", Half: half));
    }

    public void RenderBothHalves(IReadOnlyList<GridCell> l, IReadOnlyList<GridCell> r)
        => Calls.Add(new("RenderBothHalves"));

    public void RenderGrid(IReadOnlyList<GridCell> cells)
        => Calls.Add(new("RenderGrid", cells));

    public void HighlightColumn(IReadOnlyList<GridCell> cells, int col)
        => Calls.Add(new("HighlightColumn", cells, Col: col));

    public void HighlightCell(IReadOnlyList<GridCell> cells, GridCell cell)
        => Calls.Add(new("HighlightCell", cells));

    public void HighlightColumnSplitScreen(IReadOnlyList<GridCell> l, IReadOnlyList<GridCell> r,
        ScreenHalf half, int col)
        => Calls.Add(new("HighlightColumnSplitScreen", Half: half, Col: col));

    public void HighlightCellSplitScreen(IReadOnlyList<GridCell> l, IReadOnlyList<GridCell> r,
        ScreenHalf half, GridCell cell)
        => Calls.Add(new("HighlightCellSplitScreen", Half: half));

    public void RenderSubgrid(IReadOnlyList<GridCell> cells)
        => Calls.Add(new("RenderSubgrid", cells));

    public void FlashInvalidKey()
        => Calls.Add(new("FlashInvalidKey"));

    public void ClearCanvas()
        => Calls.Add(new("ClearCanvas"));
}
