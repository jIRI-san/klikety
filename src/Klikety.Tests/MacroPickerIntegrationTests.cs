using Klikety.Config;
using Klikety.Input;
using Klikety.Navigation;
using Klikety.Services;
using Klikety.Tests.Fakes;

namespace Klikety.Tests;

public class MacroPickerIntegrationTests {
    private static (NavigatorCoordinator Coordinator, FakeHotKeyService HotKey, FakeKeyboardHookService Hook,
        FakeMouseActionService Mouse, FakeOverlayWindow Overlay, FakePlatformServices Platform,
        FakeMacroStore MacroStore, FakeMacroPickerWindow Picker) CreatePickerCoordinator(
        MacrosConfig? macrosConfig = null, MacrosFile? macrosFile = null) {
        var macros = macrosConfig ?? new MacrosConfig();
        var config = new ConfigModel {
            Modes = new ModesConfig {
                UniformGrid = new ModeConfig { Enabled = true, Default = true, TwoKey = true, ArrowKeys = true },
            },
            Macros = macros,
        };
        var macroStore = new FakeMacroStore();
        var file = macrosFile ?? new MacrosFile();
        var picker = new FakeMacroPickerWindow();

        var (coordinator, hotKey, hook, mouse, overlay, _, platform, _) =
            CoordinatorTestHelper.CreateCoordinator(configOverride: config, macroStore: macroStore, macrosFile: file);
        coordinator.MacroPickerWindow = picker;

        return (coordinator, hotKey, hook, mouse, overlay, platform, macroStore, picker);
    }

    [Fact]
    public void HelperKey_WhenOverlayOpen_ShowsPicker() {
        var (_, hotKey, hook, _, overlay, _, _, picker) = CreatePickerCoordinator();

        hotKey.SimulateActivation();
        Assert.True(overlay.IsVisible);

        hook.SimulateKeyDown(VKey.OemTilde);

        Assert.True(picker.IsShown);
        Assert.Equal(1, picker.ShowCount);
        Assert.False(overlay.IsVisible);
    }

    [Fact]
    public void HelperKey_WhenOverlayClosed_DoesNothing() {
        var (_, _, hook, _, _, _, _, picker) = CreatePickerCoordinator();

        hook.SimulateKeyDown(VKey.OemTilde);

        Assert.False(picker.IsShown);
        Assert.Equal(0, picker.ShowCount);
    }

    [Fact]
    public void PickerClosed_ResumesOverlay() {
        var (_, hotKey, hook, _, overlay, _, _, picker) = CreatePickerCoordinator();

        hotKey.SimulateActivation();
        hook.SimulateKeyDown(VKey.OemTilde);
        Assert.True(picker.IsShown);

        picker.SimulatePickerClosed();

        Assert.True(overlay.IsVisible);
    }

    [Fact]
    public void PickerSlotSelected_ResumesOverlay() {
        var file = new MacrosFile();
        file.Macros[0] = new MacroDefinition { Name = "Test", Steps = [new MacroStep()] };
        var (_, hotKey, hook, _, overlay, _, _, picker) = CreatePickerCoordinator(macrosFile: file);

        hotKey.SimulateActivation();
        hook.SimulateKeyDown(VKey.OemTilde);

        picker.SimulateSlotSelected(0);

        Assert.True(overlay.IsVisible);
    }

    [Fact]
    public void HelperKey_WhileRecording_DoesNotShowPicker() {
        var (_, hotKey, hook, _, overlay, _, _, picker) = CreatePickerCoordinator();

        hotKey.SimulateActivation();

        // Start recording: press record key → slot 0 → enter recording state
        hook.SimulateKeyDown(VKey.OemPipe);
        hook.SimulateKeyDown(VKey.D0);

        // Now try helper key
        hook.SimulateKeyDown(VKey.OemTilde);

        Assert.False(picker.IsShown);
    }

    [Fact]
    public void HelperKey_WhenMacrosDisabled_DoesNotShowPicker() {
        var config = new MacrosConfig { Enabled = false };
        var (_, hotKey, hook, _, overlay, _, _, picker) = CreatePickerCoordinator(macrosConfig: config);

        hotKey.SimulateActivation();
        hook.SimulateKeyDown(VKey.OemTilde);

        Assert.False(picker.IsShown);
    }
}
