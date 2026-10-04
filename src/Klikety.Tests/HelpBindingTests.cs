using Klikety.Config;
using Klikety.Input;
using Klikety.Services;

namespace Klikety.Tests;

public class HelpBindingTests {
    [Theory]
    [InlineData(HookModifierFlags.None, true)]
    [InlineData(HookModifierFlags.Shift, true)]
    [InlineData(HookModifierFlags.Control, false)]
    [InlineData(HookModifierFlags.Alt, false)]
    [InlineData(HookModifierFlags.Win, false)]
    [InlineData(HookModifierFlags.Shift | HookModifierFlags.Control, false)]
    public void Matches_DefaultHelpKeyAllowsOptionalShiftOnly(
        HookModifierFlags modifiers,
        bool expected) {
        var result = HelpBindingPolicy.Matches(
            new HelpBindingConfig(),
            VKey.OemQuestion,
            modifiers);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Matches_CustomKeyCanRequireShift() {
        var binding = new HelpBindingConfig {
            Key = VKey.OemTilde,
            RequireShift = true,
        };

        Assert.False(HelpBindingPolicy.Matches(binding, VKey.OemTilde, HookModifierFlags.None));
        Assert.True(HelpBindingPolicy.Matches(binding, VKey.OemTilde, HookModifierFlags.Shift));
        Assert.False(HelpBindingPolicy.Matches(binding, VKey.OemQuestion, HookModifierFlags.Shift));
    }

    [Fact]
    public void Matches_DisabledBindingNeverMatches() {
        var binding = new HelpBindingConfig { Enabled = false };

        Assert.False(HelpBindingPolicy.Matches(binding, VKey.OemQuestion, HookModifierFlags.None));
    }

    [Fact]
    public void GetInvalidReason_RejectsConflictingExplicitBinding() {
        var config = new ConfigModel {
            HelpBinding = new HelpBindingConfig { Key = VKey.OemTilde },
            ActionBindings = new Dictionary<string, MouseAction> {
                ["OemTilde"] = MouseAction.LeftClick,
            },
        };

        Assert.Contains("action binding", HelpBindingPolicy.GetInvalidReason(config));
    }

    [Fact]
    public void GetInvalidReason_RejectsImplicitSpaceActionAndOverlappingGlobalHotKey() {
        var implicitSpace = new ConfigModel {
            HelpBinding = new HelpBindingConfig { Key = VKey.Space },
        };
        Assert.Contains("implicit left-click", HelpBindingPolicy.GetInvalidReason(implicitSpace));

        var globalHotKey = new ConfigModel {
            HelpBinding = new HelpBindingConfig { Key = VKey.OemQuestion },
            HotKey = new HotKeyConfig {
                Modifiers = HotKeyModifiers.Shift,
                Key = VKey.OemQuestion,
            },
        };
        Assert.Contains("global hotkey", HelpBindingPolicy.GetInvalidReason(globalHotKey));
    }
}
