using System.Runtime.InteropServices;

using Klikety.Config;

namespace Klikety.Services;

/// <summary>
/// Production implementation that reads physical modifier key state via GetAsyncKeyState.
/// </summary>
public sealed partial class ModifierDetector : IModifierDetector {
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int vKey);

    public ActionModifiers GetCurrentModifiers() {
        var mods = ActionModifiers.None;
        if ((GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0) {
            mods |= ActionModifiers.Shift;
        }

        if ((GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0) {
            mods |= ActionModifiers.Ctrl;
        }

        if ((GetAsyncKeyState(VK_MENU) & 0x8000) != 0) {
            mods |= ActionModifiers.Alt;
        }

        return mods;
    }
}
