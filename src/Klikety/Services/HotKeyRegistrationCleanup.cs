using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Klikety.Services;

internal static class HotKeyRegistrationCleanup {
    public static string? Release(ref bool registered, Func<bool> unregister) {
        if (!registered) { return null; }
        if (!unregister()) { return new Win32Exception(Marshal.GetLastPInvokeError()).Message; }
        registered = false;
        return null;
    }
}
