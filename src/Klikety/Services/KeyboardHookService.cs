using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

using Klikety.Input;

using Microsoft.Extensions.Logging;

namespace Klikety.Services;

/// <summary>
/// Low-level keyboard hook (WH_KEYBOARD_LL) active only while overlay is visible.
/// Hook callback suppresses regular key messages (returns 1) so they never reach
/// any window, and dispatches to UI thread via Dispatcher.InvokeAsync.
/// System keys (Alt combos) pass through for Alt+Tab etc.
/// </summary>
public sealed partial class KeyboardHookService : IKeyboardHookService {
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;
    private const int LLKHF_INJECTED = 0x10;
    private const int FLAGS_OFFSET = 8; // offset of 'flags' in KBDLLHOOKSTRUCT
    private bool _disposed;

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
    private readonly ILogger? _logger;
    private int _generation; // incremented on Enable/Disable to discard stale events
    private bool _draining; // drain mode: suppress all keys without dispatching, auto-disable on keyup

    public event EventHandler<KeyHookEventArgs>? KeyEvent;

    public KeyboardHookService(ILogger? logger = null) {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _logger = logger;
    }

    public bool Enable() {
        _draining = false;
        if (_hookId != 0) {
            return true; // already hooked
        }

        _hookProc = HookCallback;
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(module.ModuleName), 0);

        if (_hookId != 0) {
            if (_logger is not null) {
                LogHookEnabled(_logger);
            }

            return true;
        }

        if (_logger is not null) {
            LogHookEnableFailed(_logger);
        }

        return false;
    }

    public void Disable() {
        _draining = false;
        _generation++;
        if (_hookId != 0) {
            if (UnhookWindowsHookEx(_hookId)) {
                _hookId = 0;
                _hookProc = null;
                if (_logger is not null) {
                    LogHookDisabled(_logger);
                }
            }
        }
    }

    public void DrainAndDisable() {
        _draining = true;
        _generation++; // stop dispatching events
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam) {
        if (nCode >= 0) {
            bool isDown = wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN;
            bool isUp = wParam == WM_KEYUP || wParam == WM_SYSKEYUP;

            if (isDown || isUp) {
                var flags = (uint)Marshal.ReadInt32(lParam, FLAGS_OFFSET);
                bool isInjected = (flags & LLKHF_INJECTED) != 0;

                // Drain mode: suppress physical keys, auto-disable on physical keyup.
                // Injected events (e.g. ClearStuckModifiers via SendInput) pass through.
                if (_draining) {
                    if (isInjected) {
                        return CallNextHookEx(_hookId, nCode, wParam, lParam);
                    }

                    if (isUp) {
                        _draining = false;
                        Disable();
                    }

                    return (nint)1;
                }

                var vkey = (VKey)(uint)Marshal.ReadInt32(lParam);
                var args = new KeyHookEventArgs(vkey, isDown);
                var gen = _generation;
                _dispatcher.InvokeAsync(() => {
                    if (gen == _generation) {
                        KeyEvent?.Invoke(this, args);
                    }
                });

                // Suppress regular keys so they never reach the focused window.
                // System keys (WM_SYSKEYDOWN/UP) pass through for Alt+Tab etc.
                if (wParam == WM_KEYDOWN || wParam == WM_KEYUP) {
                    return (nint)1;
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose() {
        if (!_disposed) {
            _disposed = true;
            Disable();
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Keyboard hook enabled")]
    private static partial void LogHookEnabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Keyboard hook enable failed")]
    private static partial void LogHookEnableFailed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Keyboard hook disabled")]
    private static partial void LogHookDisabled(ILogger logger);
}
