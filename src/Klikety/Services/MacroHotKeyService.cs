using System.Runtime.InteropServices;
using System.Windows.Interop;

using Klikety.Config;

using Microsoft.Extensions.Logging;

namespace Klikety.Services;

public sealed partial class MacroHotKeyService : IMacroHotKeyService {
    private const int WM_HOTKEY = 0x0312;
    private const int MacroHotKeyId = 0x3000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    private readonly HotKeyConfig _hotKey;
    private readonly ILogger _logger;

    private HwndSource? _hwndSource;
    private bool _registered;

    public event Action? Activated;

    public MacroHotKeyService(HotKeyConfig hotKey, ILogger logger) {
        _hotKey = hotKey;
        _logger = logger;
    }

    public bool IsRegistered => _registered;

    public string? Register() {
        EnsureHwndSource();

        _registered = RegisterHotKey(
            _hwndSource!.Handle, MacroHotKeyId,
            (uint)_hotKey.Modifiers, (uint)_hotKey.Key);

        if (!_registered) {
            var desc = $"{_hotKey.Modifiers}+{_hotKey.Key}";
            LogRegisterFailed(desc);
            return $"Macro picker ({desc})";
        }

        return null;
    }

    public void Unregister() {
        if (_hwndSource is null) {
            return;
        }

        if (_registered) {
            UnregisterHotKey(_hwndSource.Handle, MacroHotKeyId);
            _registered = false;
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

        var parameters = new HwndSourceParameters("KliketyMacroHotKeyWindow") {
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

        if (wParam == MacroHotKeyId) {
            Activated?.Invoke();
            handled = true;
        }

        return nint.Zero;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to register macro hotkey: {Description}")]
    private partial void LogRegisterFailed(string description);
}
