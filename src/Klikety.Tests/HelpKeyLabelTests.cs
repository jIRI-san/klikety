using Klikety.Automation;
using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Navigation;
using Klikety.Overlay;

namespace Klikety.Tests;

public class HelpKeyLabelTests {
    [Fact]
    public void Build_HintAxesAndPrefixUseConfiguredOrderAndCurrentLayoutGlyphs() {
        var config = new ConfigModel {
            HorizontalKeys = [VKey.OemTilde, VKey.F1],
            VerticalKeys = [VKey.OemPlus, VKey.R],
        };
        var hints = new ElementHintsHelpState(HintOutcome.Success, "", 0, 1, 4, 0, false);
        var first = Build(config, new LayoutLabels("/", "`"), hints);
        var second = Build(config, new LayoutLabels("?", "~"), hints);
        Assert.Contains("First label key: `, F1. Second label key: OemPlus, R.", first.Prompts);
        Assert.Contains("First label key: ~, F1. Second label key: OemPlus, R.", second.Prompts);
        Assert.Contains(first.Prompts, prompt => prompt.Contains("First key ` entered"));
        Assert.Contains(second.Prompts, prompt => prompt.Contains("First key ~ entered"));
        Assert.Equal(config.HorizontalKeys.Concat(config.VerticalKeys), second.NavigationAnchors);
        Assert.DoesNotContain(second.Entries, entry =>
            entry.Key is VKey.OemPlus or VKey.R && entry.Category == HelpEntryCategory.Mode);
    }

    [Fact]
    public void Build_UsesCurrentLayoutGlyphsAndReadableFallbacks() {
        var config = new ConfigModel {
            ActionBindings = new Dictionary<string, MouseAction> {
                ["OemTilde"] = MouseAction.DoubleClick,
                ["F1"] = MouseAction.RightClick,
            },
            HelpBinding = new HelpBindingConfig { Key = VKey.OemQuestion },
        };

        var firstLayout = Build(config, new LayoutLabels("/", "`"));
        var secondLayout = Build(config, new LayoutLabels("?", "~"));

        Assert.Contains(firstLayout.Entries, entry =>
            entry.Key == VKey.OemQuestion && entry.KeyLabel == "/");
        Assert.Contains(secondLayout.Entries, entry =>
            entry.Key == VKey.OemQuestion && entry.KeyLabel == "?");
        Assert.Contains(firstLayout.Entries, entry =>
            entry.Key == VKey.OemTilde && entry.KeyLabel == "`");
        Assert.Contains(secondLayout.Entries, entry =>
            entry.Key == VKey.OemTilde && entry.KeyLabel == "~");
        Assert.Contains(secondLayout.Entries, entry =>
            entry.Key == VKey.F1 && entry.KeyLabel == "F1");
    }

    [Fact]
    public void Build_UsesVisibleNamesForEscapeAndSpaceEvenWhenResolverReturnsControlCharacters() {
        var content = Build(new ConfigModel(), new NonprintingLabels());

        Assert.Contains(content.Entries, entry =>
            entry.Key == VKey.Escape && entry.KeyLabel == "Esc" && entry.Command == "Close");
        Assert.Contains(content.Entries, entry =>
            entry.Key == VKey.Space && entry.KeyLabel == "Space");
    }

    [Theory]
    [InlineData(VKey.Escape)]
    [InlineData(VKey.Space)]
    [InlineData(VKey.Return)]
    [InlineData(VKey.Tab)]
    [InlineData(VKey.Back)]
    public void Win32Resolver_NonprintingKeysUseReadableNames(VKey key) {
        Assert.Equal(key.ToString(), new Win32KeyLabelResolver().Resolve(key));
    }

    private static HelpOverlayContent Build(ConfigModel config, IKeyLabelResolver labels, ElementHintsHelpState? hints = null) =>
        HelpOverlayContentBuilder.Build(
            config,
            labels,
            logGridAvailable: true,
            isDragMode: false,
            MacroState.Idle,
            recorderState: null,
            selectedMacroSlot: -1,
            recordedStepCount: 0,
            macroSlots: [],
            displayNumbers: new Dictionary<string, int>(),
            activeDisplayPath: null,
            macroPickerAvailable: true,
            isModeLocked: false,
            appScoped: false,
            elementHints: hints);

    private sealed class LayoutLabels(string helpLabel, string customLabel) : IKeyLabelResolver {
        public string Resolve(VKey key) => key switch {
            VKey.OemQuestion => helpLabel,
            VKey.OemTilde => customLabel,
            _ when key is VKey.F1 => key.ToString(),
            _ => key.ToString(),
        };
    }

    private sealed class NonprintingLabels : IKeyLabelResolver {
        public string Resolve(VKey key) => key switch {
            VKey.Escape => "\u001b",
            VKey.Space => " ",
            _ => key.ToString(),
        };
    }
}
