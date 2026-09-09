using Klikety.Grid;
using Klikety.Input;
using Klikety.Services;
using Klikety.Tests.Fakes;

namespace Klikety.Tests;

public sealed class KeyPressProcessorTests {
    private readonly FakeKeyLabelResolver _resolver = new();
    private readonly FakeKeyStateProvider _keyState = new();
    private readonly FakeKeyboardLayoutProvider _keyboardLayout = new();
    private readonly FakeTimeProvider _time = new();
    private readonly KeyPressProcessor _processor;

    public KeyPressProcessorTests() {
        _processor = new KeyPressProcessor(_keyState, _time, _keyboardLayout, _ => _resolver);
    }

    [Fact]
    public void PlainLetter_ReturnsLabelEntry() {
        var entry = _processor.ProcessKeyDown(VKey.A);

        Assert.NotNull(entry);
        Assert.Equal("A", entry.Label);
    }

    [Fact]
    public void ModifierOnly_ReturnsNull() {
        var entry = _processor.ProcessKeyDown(VKey.LControl);

        Assert.Null(entry);
    }

    [Theory]
    [InlineData(VKey.LControl)]
    [InlineData(VKey.RControl)]
    [InlineData(VKey.LShift)]
    [InlineData(VKey.RShift)]
    [InlineData(VKey.LMenu)]
    [InlineData(VKey.RMenu)]
    [InlineData(VKey.LWin)]
    [InlineData(VKey.RWin)]
    [InlineData(VKey.Shift)]
    [InlineData(VKey.Control)]
    [InlineData(VKey.Menu)]
    public void AllModifierKeys_ReturnNull(VKey modKey) {
        Assert.Null(_processor.ProcessKeyDown(modKey));
    }

    [Fact]
    public void ModifierPlusLetter_ReturnsCombinedLabel() {
        _keyState.SetKeyDown(VKey.LControl);
        _processor.ProcessKeyDown(VKey.LControl);

        var entry = _processor.ProcessKeyDown(VKey.C);

        Assert.NotNull(entry);
        Assert.Equal("Ctrl+C", entry.Label);
    }

    [Fact]
    public void MultiModifier_CanonicalOrder() {
        _keyState.SetKeyDown(VKey.LControl);
        _keyState.SetKeyDown(VKey.LShift);
        _keyState.SetKeyDown(VKey.LMenu);
        _processor.ProcessKeyDown(VKey.LControl);
        _processor.ProcessKeyDown(VKey.LShift);
        _processor.ProcessKeyDown(VKey.LMenu);

        var entry = _processor.ProcessKeyDown(VKey.A);

        Assert.NotNull(entry);
        Assert.Equal("Ctrl+Shift+Alt+A", entry.Label);
    }

    [Fact]
    public void LeftRightModifiers_Deduplicated() {
        _keyState.SetKeyDown(VKey.LControl);
        _keyState.SetKeyDown(VKey.RControl);
        _processor.ProcessKeyDown(VKey.LControl);
        _processor.ProcessKeyDown(VKey.RControl);

        var entry = _processor.ProcessKeyDown(VKey.A);

        Assert.NotNull(entry);
        Assert.Equal("Ctrl+A", entry.Label);
    }

    [Fact]
    public void SpecialKey_ReturnsSpecialLabel() {
        var entry = _processor.ProcessKeyDown(VKey.Return);

        Assert.NotNull(entry);
        Assert.Equal("Enter", entry.Label);
    }

    [Fact]
    public void ArrowKeys_ReturnArrowSymbols() {
        Assert.Equal("←", _processor.ProcessKeyDown(VKey.Left)!.Label);
        Assert.Equal("→", _processor.ProcessKeyDown(VKey.Right)!.Label);
        Assert.Equal("↑", _processor.ProcessKeyDown(VKey.Up)!.Label);
        Assert.Equal("↓", _processor.ProcessKeyDown(VKey.Down)!.Label);
    }

    [Fact]
    public void FunctionKey_ReturnsFLabel() {
        var entry = _processor.ProcessKeyDown((VKey)0x70);

        Assert.NotNull(entry);
        Assert.Equal("F1", entry.Label);
    }

    [Fact]
    public void SpaceKey_ReturnsSpaceLabel() {
        var entry = _processor.ProcessKeyDown(VKey.Space);

        Assert.NotNull(entry);
        Assert.Equal("Space", entry.Label);
    }

    [Fact]
    public void UnknownVKey_FallsBackToToString() {
        // VKey value not in enum and not in special keys
        var entry = _processor.ProcessKeyDown((VKey)0xFF);

        Assert.NotNull(entry);
        // ToString on undefined enum value returns the numeric string
        Assert.Equal("255", entry.Label);
    }

    [Fact]
    public void RepeatDetection_WithinWindow_ReturnsTrue() {
        _time.SetTimestamp(0);
        var first = _processor.ProcessKeyDown(VKey.A)!;

        _time.Advance(TimeSpan.FromMilliseconds(100));
        var second = _processor.ProcessKeyDown(VKey.A)!;

        Assert.True(_processor.IsRepeat(second, first, 150));
    }

    [Fact]
    public void RepeatDetection_OutsideWindow_ReturnsFalse() {
        _time.SetTimestamp(0);
        var first = _processor.ProcessKeyDown(VKey.A)!;

        _time.Advance(TimeSpan.FromMilliseconds(200));
        var second = _processor.ProcessKeyDown(VKey.A)!;

        Assert.False(_processor.IsRepeat(second, first, 150));
    }

    [Fact]
    public void RepeatDetection_DifferentLabels_ReturnsFalse() {
        _time.SetTimestamp(0);
        var first = _processor.ProcessKeyDown(VKey.A)!;

        _time.Advance(TimeSpan.FromMilliseconds(50));
        var second = _processor.ProcessKeyDown(VKey.B)!;

        Assert.False(_processor.IsRepeat(second, first, 150));
    }

    [Fact]
    public void RepeatDetection_NullPrevious_ReturnsFalse() {
        var current = _processor.ProcessKeyDown(VKey.A)!;

        Assert.False(_processor.IsRepeat(current, null, 150));
    }

    [Fact]
    public void StaleModifier_ReconciledOnKeyDown() {
        // Simulate: LControl was held (tracked), but physically released (missed key-up)
        _keyState.SetKeyDown(VKey.LControl);
        _processor.ProcessKeyDown(VKey.LControl);

        // Now LControl is no longer physically held
        _keyState.SetKeyUp(VKey.LControl);

        // Next key-down should reconcile and NOT include Ctrl prefix
        var entry = _processor.ProcessKeyDown(VKey.A);

        Assert.NotNull(entry);
        Assert.Equal("A", entry.Label);
    }

    [Fact]
    public void Reset_ClearsModifierState() {
        _keyState.SetKeyDown(VKey.LShift);
        _processor.ProcessKeyDown(VKey.LShift);

        _processor.ResetModifierState();
        // Even though keyState still reports key down, held set is cleared
        // Reconcile will re-add it if still physically held
        _keyState.SetKeyUp(VKey.LShift);

        var entry = _processor.ProcessKeyDown(VKey.A);

        Assert.NotNull(entry);
        Assert.Equal("A", entry.Label);
    }

    [Fact]
    public void KeyUp_RemovesModifierFromHeld() {
        _keyState.SetKeyDown(VKey.LControl);
        _processor.ProcessKeyDown(VKey.LControl);

        _keyState.SetKeyUp(VKey.LControl);
        _processor.ProcessKeyUp(VKey.LControl);

        var entry = _processor.ProcessKeyDown(VKey.A);

        Assert.NotNull(entry);
        Assert.Equal("A", entry.Label);
    }

    [Fact]
    public void WinModifier_IncludedInPrefix() {
        _keyState.SetKeyDown(VKey.LWin);
        _processor.ProcessKeyDown(VKey.LWin);

        var entry = _processor.ProcessKeyDown(VKey.L);

        Assert.NotNull(entry);
        Assert.Equal("Win+L", entry.Label);
    }

    [Fact]
    public void KeyPressEntry_ToString_HidesLabel() {
        var entry = new KeyPressEntry("SensitiveData", 0);

        Assert.Equal("[KeyPressEntry]", entry.ToString());
    }

    [Fact]
    public void RebuildCache_UsesNewResolver() {
        // Initial resolver returns uppercase VKey names
        var entry1 = _processor.ProcessKeyDown(VKey.A);
        Assert.Equal("A", entry1!.Label);

        // Rebuild with a different resolver
        var customResolver = new CustomLabelResolver("x");
        _processor.RebuildCache(customResolver);

        var entry2 = _processor.ProcessKeyDown(VKey.A);
        Assert.Equal("x", entry2!.Label);
    }

    [Fact]
    public void LayoutChange_RebuildsCacheBeforeResolvingKey() {
        var processor = new KeyPressProcessor(
            _keyState,
            _time,
            _keyboardLayout,
            hkl => hkl == 0x04090409 ? _resolver : new CustomLabelResolver("x"));

        _keyboardLayout.Layout = 0x04050405;
        int readsBeforeKey = _keyboardLayout.GetActiveKeyboardLayoutCalls;

        var entry = processor.ProcessKeyDown(VKey.A);

        Assert.Equal("x", entry!.Label);
        Assert.Equal(readsBeforeKey + 1, _keyboardLayout.GetActiveKeyboardLayoutCalls);
    }

    private sealed class CustomLabelResolver : IKeyLabelResolver {
        private readonly string _label;
        public CustomLabelResolver(string label) => _label = label;
        public string Resolve(VKey key) => _label;
    }
}
