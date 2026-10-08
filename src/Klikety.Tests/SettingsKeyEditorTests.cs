using System.Windows.Input;

using Klikety.Config;
using Klikety.Input;

namespace Klikety.Tests;

public sealed class SettingsKeyEditorTests {
    [Theory]
    [InlineData(Key.LeftCtrl)]
    [InlineData(Key.RightCtrl)]
    [InlineData(Key.LeftAlt)]
    [InlineData(Key.RightAlt)]
    [InlineData(Key.LeftShift)]
    [InlineData(Key.RightShift)]
    [InlineData(Key.LWin)]
    [InlineData(Key.RWin)]
    public void ModifierOnlyInputDoesNotCompleteOrChangeTheCapturedKey(Key key) {
        var capture = new SettingsKeyCapture();
        capture.Begin();
        Assert.Equal(SettingsCaptureKind.Ignored, capture.Process(key, false, ModifierKeys.None).Kind);
        Assert.True(capture.IsActive);
    }

    [Fact]
    public void RepeatEscapeFocusCancellationAndUnsupportedKeyAreDistinct() {
        var capture = new SettingsKeyCapture();
        Assert.Equal(SettingsCaptureKind.Inactive, capture.Process(Key.A, false, ModifierKeys.None).Kind);
        capture.Begin();
        Assert.Equal(SettingsCaptureKind.Ignored, capture.Process(Key.A, true, ModifierKeys.None).Kind);
        Assert.Equal(SettingsCaptureKind.Unsupported, capture.Process(Key.None, false, ModifierKeys.None).Kind);
        Assert.True(capture.IsActive);
        Assert.Equal(SettingsCaptureKind.Cancelled, capture.Process(Key.Escape, false, ModifierKeys.None).Kind);
        Assert.False(capture.IsActive);
        capture.Begin();
        capture.Cancel();
        Assert.Equal(SettingsCaptureKind.Inactive, capture.Process(Key.A, false, ModifierKeys.None).Kind);
    }

    [Theory]
    [InlineData(ModifierKeys.Control, HotKeyModifiers.Control)]
    [InlineData(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift, HotKeyModifiers.Control | HotKeyModifiers.Alt | HotKeyModifiers.Shift)]
    [InlineData(ModifierKeys.Windows | ModifierKeys.Shift, HotKeyModifiers.Win | HotKeyModifiers.Shift)]
    public void CapturedShortcutRetainsPhysicalVkeyAndModifierFlags(ModifierKeys input, HotKeyModifiers expected) {
        var capture = new SettingsKeyCapture();
        capture.Begin();
        var result = capture.Process(Key.OemOpenBrackets, false, input);
        Assert.Equal(SettingsCaptureKind.Captured, result.Kind);
        Assert.Equal(VKey.OemOpenBrackets, result.Key);
        Assert.Equal(expected, result.Modifiers);
        Assert.False(capture.IsActive);
    }
}
