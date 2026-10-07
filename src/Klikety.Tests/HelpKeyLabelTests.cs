using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Navigation;
using Klikety.Overlay;

namespace Klikety.Tests;

public class HelpKeyLabelTests {
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

    private static HelpOverlayContent Build(ConfigModel config, IKeyLabelResolver labels) =>
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
            appScoped: false);

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
