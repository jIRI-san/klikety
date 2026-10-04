using Klikety.Config;
using Klikety.Input;
using Klikety.Services;
using Klikety.Tests.Fakes;

namespace Klikety.Tests;

public class HelpOverlayCoordinatorTests {
    [Fact]
    public void Toggle_PausesCommandsAndResumesTheSameSelection() {
        var (coordinator, hotKey, hook, mouse, overlay, renderer, _, _) =
            CoordinatorTestHelper.CreateCoordinator();
        hotKey.SimulateActivation();
        hook.SimulateKeyDown(VKey.A);

        var callsAtSelection = renderer.Calls.Count;
        var mouseCallsAtSelection = mouse.Calls.Count;
        hook.SimulateKeyDown(VKey.OemQuestion);
        Assert.NotNull(overlay.CurrentHelp);
        Assert.Equal(1, overlay.ShowHelpCount);

        hook.SimulateKeyDown(VKey.W);
        hook.SimulateKeyDown(VKey.N);
        hook.SimulateKeyDown(VKey.F1);
        hook.SimulateKeyDown(VKey.Space);
        Assert.Equal(callsAtSelection, renderer.Calls.Count);
        Assert.Equal(mouseCallsAtSelection, mouse.Calls.Count);

        hook.SimulateKeyUp(VKey.OemQuestion);
        hook.SimulateKeyDown(VKey.OemQuestion);
        Assert.Null(overlay.CurrentHelp);
        hook.SimulateKeyUp(VKey.OemQuestion);

        hook.SimulateKeyDown(VKey.W);
        Assert.True(renderer.Calls.Count > callsAtSelection);
        Assert.Equal(mouseCallsAtSelection + 1, mouse.Calls.Count);

        coordinator.Dispose();
    }

    [Fact]
    public void Toggle_LatchesHeldKeyAndAcceptsEitherShiftState() {
        var (coordinator, hotKey, hook, _, overlay, _, _, _) =
            CoordinatorTestHelper.CreateCoordinator();
        hotKey.SimulateActivation();

        hook.SimulateKeyDown(VKey.OemQuestion, HookModifierFlags.Shift);
        hook.SimulateKeyDown(VKey.OemQuestion, HookModifierFlags.Shift);
        Assert.Equal(1, overlay.ShowHelpCount);

        hook.SimulateKeyUp(VKey.OemQuestion);
        hook.SimulateKeyDown(VKey.OemQuestion, HookModifierFlags.None);
        Assert.Null(overlay.CurrentHelp);
        hook.SimulateKeyUp(VKey.OemQuestion);

        hook.SimulateKeyDown(VKey.OemQuestion, HookModifierFlags.Control);
        Assert.Null(overlay.CurrentHelp);
        hook.SimulateKeyUp(VKey.OemQuestion);

        coordinator.Dispose();
    }

    [Fact]
    public void Escape_ClosesHelpWithoutCancellingNavigation() {
        var (coordinator, hotKey, hook, _, overlay, _, _, _) =
            CoordinatorTestHelper.CreateCoordinator();
        hotKey.SimulateActivation();
        hook.SimulateKeyDown(VKey.A);
        hook.SimulateKeyDown(VKey.OemQuestion);
        hook.SimulateKeyUp(VKey.OemQuestion);

        hook.SimulateKeyDown(VKey.Escape);
        Assert.Null(overlay.CurrentHelp);
        Assert.True(overlay.IsVisible);
        hook.SimulateKeyUp(VKey.Escape);

        hook.SimulateKeyDown(VKey.OemQuestion);
        Assert.NotNull(overlay.CurrentHelp);

        coordinator.Dispose();
    }

    [Fact]
    public void DeactivationAndGlobalPickerClearHelpBeforeHiding() {
        var (coordinator, hotKey, hook, _, overlay, _, _, _) =
            CoordinatorTestHelper.CreateCoordinator();
        var picker = new FakeMacroPickerWindow();
        var macroHotKey = new FakeMacroHotKeyService();
        coordinator.MacroPickerWindow = picker;
        coordinator.MacroHotKeyService = macroHotKey;
        hotKey.SimulateActivation();

        hook.SimulateKeyDown(VKey.OemQuestion);
        hook.SimulateKeyUp(VKey.OemQuestion);
        macroHotKey.SimulateActivated();

        Assert.True(picker.IsShown);
        Assert.Null(overlay.CurrentHelp);
        Assert.False(overlay.HelpVisibleAtLastHide);

        picker.SimulatePickerClosed();
        Assert.True(overlay.IsVisible);
        Assert.True(hook.IsEnabled);

        hook.SimulateKeyDown(VKey.OemQuestion);
        Assert.NotNull(overlay.CurrentHelp);
        coordinator.DeactivateOverlay();
        Assert.Null(overlay.CurrentHelp);
        Assert.False(overlay.HelpVisibleAtLastHide);

        coordinator.Dispose();
    }

    [Fact]
    public void InvalidHelpBinding_DoesNotStealConflictingAction() {
        var config = new ConfigModel {
            ActionBindings = new Dictionary<string, MouseAction> {
                ["OemQuestion"] = MouseAction.LeftClick,
            },
        };
        var (coordinator, hotKey, hook, mouse, overlay, _, _, _) =
            CoordinatorTestHelper.CreateCoordinator(configOverride: config);
        hotKey.SimulateActivation();

        hook.SimulateKeyDown(VKey.OemQuestion);

        Assert.Null(overlay.CurrentHelp);
        Assert.Contains(mouse.Calls, call => call.Action == MouseAction.LeftClick);
        coordinator.Dispose();
    }

    [Fact]
    public void ViewportAndKeyboardLayoutChangesRefreshHelpWithoutSessionMutation() {
        var (coordinator, hotKey, hook, _, overlay, renderer, platform, _) =
            CoordinatorTestHelper.CreateCoordinator();
        hotKey.SimulateActivation();
        hook.SimulateKeyDown(VKey.OemQuestion);
        var renderCalls = renderer.Calls.Count;

        overlay.SimulateViewportChange(700, 500);
        Assert.Equal(renderCalls, renderer.Calls.Count);
        platform.KeyboardLayout.Layout = 0x04070407;
        overlay.SimulateKeyboardLayoutChange();

        Assert.Equal(1, overlay.RelayoutHelpCount);
        Assert.Equal(1, overlay.UpdateHelpCount);
        Assert.Equal(renderCalls + 1, renderer.Calls.Count);

        coordinator.Dispose();
    }
}
