using System.Drawing;
using Klikety.Config;
using Klikety.Input;
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
