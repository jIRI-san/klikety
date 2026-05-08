using System.Runtime.InteropServices;
using System.Windows.Interop;

using Klikety.Config;

using Microsoft.Extensions.Logging;

namespace Klikety.Services;

public interface IScrollHotKeyService : IDisposable {
    bool IsRegistered { get; }
    List<string> Register();
    void Unregister();
}

public sealed partial class ScrollHotKeyService : IScrollHotKeyService {
    private const int WM_HOTKEY = 0x0312;
    private const int ScrollUpId = 0x2000;
    private const int ScrollDownId = 0x2001;
    private const int WHEEL_DELTA = 120;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    private readonly ScrollHotKeyConfig _config;
    private readonly IMouseActionService _mouseService;
    private readonly ILogger _logger;
    private readonly int _wheelDelta;

    private HwndSource? _hwndSource;
    private bool _upRegistered;
    private bool _downRegistered;

    public ScrollHotKeyService(ScrollHotKeyConfig config, IMouseActionService mouseService, ILogger logger) {
        _config = config;
        _mouseService = mouseService;
        _logger = logger;
        _wheelDelta = WHEEL_DELTA * Math.Max(config.ScrollAmount, 1);
    }

    public bool IsRegistered => _upRegistered || _downRegistered;

    public List<string> Register() {
        EnsureHwndSource();
        var failures = new List<string>();

        _upRegistered = RegisterHotKey(
            _hwndSource!.Handle, ScrollUpId,
            (uint)_config.ScrollUpKey.Modifiers, (uint)_config.ScrollUpKey.Key);
        if (!_upRegistered) {
            failures.Add($"Scroll up ({_config.ScrollUpKey.Modifiers}+{_config.ScrollUpKey.Key})");
            LogRegisterFailed("scroll up", _config.ScrollUpKey.Modifiers, _config.ScrollUpKey.Key);
        }

        _downRegistered = RegisterHotKey(
            _hwndSource!.Handle, ScrollDownId,
            (uint)_config.ScrollDownKey.Modifiers, (uint)_config.ScrollDownKey.Key);
        if (!_downRegistered) {
            failures.Add($"Scroll down ({_config.ScrollDownKey.Modifiers}+{_config.ScrollDownKey.Key})");
            LogRegisterFailed("scroll down", _config.ScrollDownKey.Modifiers, _config.ScrollDownKey.Key);
        }

        return failures;
    }

    public void Unregister() {
        if (_hwndSource is null) {
            return;
        }

        if (_upRegistered) {
            UnregisterHotKey(_hwndSource.Handle, ScrollUpId);
            _upRegistered = false;
        }
        if (_downRegistered) {
            UnregisterHotKey(_hwndSource.Handle, ScrollDownId);
            _downRegistered = false;
        }
    }

    public void Dispose() {
        Unregister();
        if (_hwndSource is not null) {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource.Dispose();
            _hwndSource = null;
        }
    }

    private void EnsureHwndSource() {
        if (_hwndSource is not null) {
            return;
        }

        var parameters = new HwndSourceParameters("KliketyScrollHotKeyWindow") {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
        };
        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled) {
        if (msg != WM_HOTKEY) {
            return nint.Zero;
        }

        if (wParam == ScrollUpId) {
            _mouseService.SendScroll(_wheelDelta);
            handled = true;
        } else if (wParam == ScrollDownId) {
            _mouseService.SendScroll(-_wheelDelta);
            handled = true;
        }

        return nint.Zero;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to register {Direction} hotkey: {Modifiers}+{Key}")]
    private partial void LogRegisterFailed(string direction, HotKeyModifiers modifiers, Input.VKey key);
}
