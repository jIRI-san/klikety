using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

using Klikety.Input;

namespace Klikety.Services;

/// <summary>
/// Low-level keyboard hook (WH_KEYBOARD_LL) active only while overlay is visible.
/// Hook callback does minimal work — reads VKey, calls CallNextHookEx,
/// then dispatches to UI thread via Dispatcher.InvokeAsync.
/// </summary>
public sealed class KeyboardHookService : IKeyboardHookService {
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;

    private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? lpModuleName);

    private nint _hookId;
    private LowLevelKeyboardProc? _hookProc; // prevent GC
    private readonly Dispatcher _dispatcher;

    public event EventHandler<KeyHookEventArgs>? KeyEvent;

    public KeyboardHookService() {
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    public bool Enable() {
        if (_hookId != 0) {
            return true; // already hooked
        }

        _hookProc = HookCallback;
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(module.ModuleName), 0);

        return _hookId != 0;
    }

    public void Disable() {
        if (_hookId != 0) {
            UnhookWindowsHookEx(_hookId);
            _hookId = 0;
        }
        _hookProc = null;
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam) {
        if (nCode >= 0) {
            bool isDown = wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN;
            bool isUp = wParam == WM_KEYUP || wParam == WM_SYSKEYUP;

            if (isDown || isUp) {
                var vkey = (VKey)(uint)Marshal.ReadInt32(lParam);
                var args = new KeyHookEventArgs(vkey, isDown);
                _dispatcher.InvokeAsync(() => KeyEvent?.Invoke(this, args));
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }
}
