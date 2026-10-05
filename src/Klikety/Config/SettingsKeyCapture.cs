using System.Windows.Input;

using Klikety.Input;

namespace Klikety.Config;

internal enum SettingsCaptureKind { Inactive, Ignored, Cancelled, Unsupported, Captured }
internal sealed record SettingsCaptureResult(SettingsCaptureKind Kind, VKey? Key = null, HotKeyModifiers Modifiers = HotKeyModifiers.None);

internal sealed class SettingsKeyCapture {
    public bool IsActive { get; private set; }
    public void Begin() => IsActive = true;
    public void Cancel() => IsActive = false;

    public SettingsCaptureResult Process(Key key, bool repeat, ModifierKeys modifiers) {
        if (!IsActive) { return new(SettingsCaptureKind.Inactive); }
        if (key == System.Windows.Input.Key.Escape) {
            Cancel();
            return new(SettingsCaptureKind.Cancelled);
        }
        if (repeat || key is System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl or
            System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt or System.Windows.Input.Key.LeftShift or
            System.Windows.Input.Key.RightShift or System.Windows.Input.Key.LWin or System.Windows.Input.Key.RWin) {
            return new(SettingsCaptureKind.Ignored);
        }
        var vk = KeyInterop.VirtualKeyFromKey(key);
        if (!Enum.IsDefined(typeof(VKey), vk)) { return new(SettingsCaptureKind.Unsupported); }
        Cancel();
        var flags = HotKeyModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Control)) { flags |= HotKeyModifiers.Control; }
        if (modifiers.HasFlag(ModifierKeys.Alt)) { flags |= HotKeyModifiers.Alt; }
        if (modifiers.HasFlag(ModifierKeys.Shift)) { flags |= HotKeyModifiers.Shift; }
        if (modifiers.HasFlag(ModifierKeys.Windows)) { flags |= HotKeyModifiers.Win; }
        return new(SettingsCaptureKind.Captured, (VKey)vk, flags);
    }
}
