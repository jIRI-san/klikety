using Klikety.Config;
using Klikety.Input;

namespace Klikety.Services;

/// <summary>
/// Abstracts global hotkey registration and activation.
/// </summary>
public interface IHotKeyService : IDisposable
{
    event EventHandler? Activated;
    bool Register(HotKeyConfig config);
    void Unregister();
}

/// <summary>
/// Abstracts low-level keyboard hook for overlay key capture.
/// </summary>
public interface IKeyboardHookService
{
    event EventHandler<VKey>? KeyPressed;
    bool Enable();
    void Disable();
}

/// <summary>
/// Abstracts mouse cursor movement and click actions.
/// </summary>
public interface IMouseActionService
{
    void MoveTo(System.Drawing.Point physicalPoint);
    void SendAction(System.Drawing.Point physicalPoint, MouseAction action);
}

/// <summary>
/// Abstracts the overlay window for testability.
/// </summary>
public interface IOverlayWindow
{
    event EventHandler? FocusLost;
    void Show();
    void Hide();
    bool IsVisible { get; }
}
