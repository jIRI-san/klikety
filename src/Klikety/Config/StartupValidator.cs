using System.Runtime.InteropServices;
using Klikety.Input;

namespace Klikety.Config;

/// <summary>
/// Probes whether the configured global hotkey can be registered.
/// Uses RegisterHotKey/UnregisterHotKey to detect conflicts without
/// permanently claiming the hotkey slot.
/// </summary>
public static class StartupValidator
{
    private const int ProbeHotKeyId = 0x7FFF; // arbitrary unique ID for the probe

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    /// <summary>
    /// Attempts to register the configured hotkey, then immediately unregisters it.
    /// Returns a validation error string if registration fails (conflict), null otherwise.
    /// </summary>
    public static string? ProbeHotKey(HotKeyConfig hotKey)
    {
        bool registered = RegisterHotKey(nint.Zero, ProbeHotKeyId, (uint)hotKey.Modifiers, (uint)hotKey.Key);
        if (!registered)
        {
            int error = Marshal.GetLastWin32Error();
            return $"Global hotkey {hotKey.Modifiers}+{hotKey.Key} is already registered by another application (Win32 error {error}).";
        }

        UnregisterHotKey(nint.Zero, ProbeHotKeyId);
        return null;
    }
}
