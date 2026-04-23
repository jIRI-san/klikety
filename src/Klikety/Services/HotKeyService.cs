using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Klikety.Config;
using Klikety.Input;

namespace Klikety.Services;

/// <summary>
/// Registers a global hotkey via Win32 RegisterHotKey, raises Activated on WM_HOTKEY.
/// </summary>
public sealed class HotKeyService : IHotKeyService
{
    private const int WM_HOTKEY = 0x0312;
    private const int HotKeyId = 0x1000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    private HwndSource? _hwndSource;
    private bool _registered;

    public event EventHandler? Activated;

    public bool Register(HotKeyConfig config)
    {
        EnsureHwndSource();
        _registered = RegisterHotKey(_hwndSource!.Handle, HotKeyId, (uint)config.Modifiers, (uint)config.Key);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered && _hwndSource is not null)
        {
            UnregisterHotKey(_hwndSource.Handle, HotKeyId);
            _registered = false;
        }
    }

    public void Dispose()
    {
        Unregister();
        if (_hwndSource is not null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource.Dispose();
            _hwndSource = null;
        }
    }

    private void EnsureHwndSource()
    {
        if (_hwndSource is not null) return;

        var parameters = new HwndSourceParameters("KliketyHotKeyWindow")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
        };
        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam == HotKeyId)
        {
            Activated?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        return nint.Zero;
    }
}
