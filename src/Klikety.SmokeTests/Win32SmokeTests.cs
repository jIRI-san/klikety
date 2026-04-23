using System.Drawing;
using Klikety.Config;
using Klikety.Interop;
using Klikety.Services;

namespace Klikety.SmokeTests;

/// <summary>
/// Smoke tests that exercise real Win32 services on a live display.
/// Excluded from CI — run manually with: dotnet test --filter "Category=Smoke"
/// </summary>
[Trait("Category", "Smoke")]
public class Win32SmokeTests
{
    [Fact]
    public void GetPrimaryScreenBounds_ReturnsNonZero()
    {
        var bounds = NativeMethods.GetPrimaryScreenBounds();
        Assert.True(bounds.Width > 0, $"Screen width was {bounds.Width}");
        Assert.True(bounds.Height > 0, $"Screen height was {bounds.Height}");
    }

    [Fact]
    public void GetCursorPosition_ReturnsWithinScreenBounds()
    {
        var bounds = NativeMethods.GetPrimaryScreenBounds();
        var pos = NativeMethods.GetCursorPosition();
        Assert.InRange(pos.X, bounds.Left, bounds.Right);
        Assert.InRange(pos.Y, bounds.Top, bounds.Bottom);
    }

    [Fact]
    public void GetActiveKeyboardLayout_ReturnsNonZero()
    {
        var hkl = NativeMethods.GetActiveKeyboardLayout();
        Assert.NotEqual(nint.Zero, hkl);
    }

    [Fact]
    public void VKeyToChar_LetterA_ReturnsA()
    {
        var hkl = NativeMethods.GetActiveKeyboardLayout();
        var ch = NativeMethods.VKeyToChar((uint)Input.VKey.A, hkl);
        Assert.NotNull(ch);
        Assert.Equal('a', char.ToLowerInvariant(ch.Value));
    }

    [Fact]
    public void MouseActionService_MoveTo_DoesNotThrow()
    {
        var service = new MouseActionService();
        var pos = NativeMethods.GetCursorPosition();
        // Move to current position (no visible effect, just verify no exception)
        service.MoveTo(pos);
    }

    [Fact(Skip = "Requires WPF message loop (HwndSource) — run manually in a WPF host")]
    public void HotKeyService_RegisterUnregister_Succeeds()
    {
        using var service = new HotKeyService();
        var config = new HotKeyConfig
        {
            Modifiers = HotKeyModifiers.Control | HotKeyModifiers.Alt | HotKeyModifiers.Shift,
            Key = Input.VKey.Pause, // unlikely to conflict
        };
        bool registered = service.Register(config);
        Assert.True(registered, "Failed to register hotkey");
        service.Unregister();
    }

    [Fact]
    public void StartupValidator_Probe_ReturnsResult()
    {
        var config = new HotKeyConfig
        {
            Modifiers = HotKeyModifiers.Control | HotKeyModifiers.Alt | HotKeyModifiers.Shift,
            Key = Input.VKey.Pause,
        };
        string? error = StartupValidator.ProbeHotKey(config);
        Assert.Null(error);
    }
}
