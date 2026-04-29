using Klikety.Input;
using Klikety.Interop;

namespace Klikety.Grid;

/// <summary>
/// Production resolver that uses Win32 ToUnicode / MapVirtualKey via NativeMethods.
/// </summary>
public sealed class Win32KeyLabelResolver : IKeyLabelResolver {
    private readonly nint _hkl;

    public Win32KeyLabelResolver(nint hkl = 0) {
        _hkl = hkl == 0 ? NativeMethods.GetActiveKeyboardLayout() : hkl;
    }

    public string Resolve(VKey key) {
        var ch = NativeMethods.VKeyToChar((uint)key, _hkl);
        return ch.HasValue ? char.ToUpper(ch.Value).ToString() : key.ToString();
    }
}
