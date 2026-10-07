using Klikety.Config;
using Klikety.Input;
using Klikety.Services;
using Klikety.Tests.Fakes;

namespace Klikety.Tests;

public class HelpOverlayCoordinatorTests {
    [Fact]
    public void Toggle_ResumesTheSameSelectionWithoutDispatchingCloseKey() {
        var (coordinator, hotKey, hook, mouse, overlay, renderer, _, _) =
            CoordinatorTestHelper.CreateCoordinator();
        hotKey.SimulateActivation();
        hook.SimulateKeyDown(VKey.A);

        var callsAtSelection = renderer.Calls.Count;
        var mouseCallsAtSelection = mouse.Calls.Count;
        hook.SimulateKeyDown(VKey.OemQuestion);
        Assert.NotNull(overlay.CurrentHelp);
        Assert.Equal(1, overlay.ShowHelpCount);

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
    public void NavigationKey_ClosesHelpAndContinuesTheExistingSelection() {
        var (coordinator, hotKey, hook, mouse, overlay, renderer, _, _) =
            CoordinatorTestHelper.CreateCoordinator();
        using (coordinator) {
            hotKey.SimulateActivation();
            hook.SimulateKey(VKey.A);
            var callsAtSelection = renderer.Calls.Count;
            var mouseCallsAtSelection = mouse.Calls.Count;
            hook.SimulateKey(VKey.OemQuestion);

            hook.SimulateKey(VKey.W);

            Assert.Null(overlay.CurrentHelp);
            Assert.True(overlay.IsVisible);
            Assert.True(renderer.Calls.Count > callsAtSelection);
            Assert.Equal(mouseCallsAtSelection + 1, mouse.Calls.Count);
            Assert.DoesNotContain(mouse.Calls, call => call.Action is not null);
        }
    }

    [Theory]
    [InlineData(ActionModifiers.None)]
    [InlineData(ActionModifiers.Shift)]
    [InlineData(ActionModifiers.Ctrl | ActionModifiers.Alt)]
    public void ActionKey_ClosesHelpAndDispatchesOnceWithModifiers(ActionModifiers modifiers) {
        var (coordinator, hotKey, hook, mouse, overlay, _, _, modifierDetector) =
            CoordinatorTestHelper.CreateCoordinator();
        using (coordinator) {
            hotKey.SimulateActivation();
            hook.SimulateKey(VKey.OemQuestion);
            modifierDetector.Modifiers = modifiers;

            hook.SimulateKeyDown(VKey.Space);
            hook.SimulateKeyDown(VKey.Space);
            hook.SimulateKeyUp(VKey.Space);

            Assert.Null(overlay.CurrentHelp);
            Assert.False(overlay.IsVisible);
            var action = Assert.Single(mouse.Calls, call => call.Action is not null);
            Assert.Equal(MouseAction.LeftClick, action.Action);
            Assert.Equal(modifiers, action.Modifiers);
            Assert.False(overlay.HelpVisibleAtLastHide);
        }
    }

    [Theory]
    [InlineData(VKey.Shift)]
    [InlineData(VKey.LShift)]
    [InlineData(VKey.RShift)]
    [InlineData(VKey.Control)]
    [InlineData(VKey.LControl)]
    [InlineData(VKey.RControl)]
    [InlineData(VKey.Menu)]
    [InlineData(VKey.LMenu)]
    [InlineData(VKey.RMenu)]
    [InlineData(VKey.LWin)]
    [InlineData(VKey.RWin)]
    public void ModifierAlone_DoesNotDismissHelpOrDispatch(VKey key) {
        var (coordinator, hotKey, hook, mouse, overlay, renderer, _, _) =
            CoordinatorTestHelper.CreateCoordinator();
        using (coordinator) {
            hotKey.SimulateActivation();
            hook.SimulateKey(VKey.OemQuestion);
            var renderCalls = renderer.Calls.Count;
            var mouseCalls = mouse.Calls.Count;

            hook.SimulateKeyDown(key);
            hook.SimulateKeyUp(key);

            Assert.NotNull(overlay.CurrentHelp);
            Assert.Equal(renderCalls, renderer.Calls.Count);
            Assert.Equal(mouseCalls, mouse.Calls.Count);
        }
    }

    [Fact]
    public void UnmappedKey_ClosesHelpWithoutInventingAnAction() {
        var (coordinator, hotKey, hook, mouse, overlay, _, _, _) =
            CoordinatorTestHelper.CreateCoordinator();
        using (coordinator) {
            hotKey.SimulateActivation();
            hook.SimulateKey(VKey.OemQuestion);

            hook.SimulateKey(VKey.Pause);

            Assert.Null(overlay.CurrentHelp);
            Assert.True(overlay.IsVisible);
            Assert.Empty(mouse.Calls);
        }
    }

    [Fact]
    public void HelperKey_ClosesHelpBeforeNormalPickerHandoff() {
        var (coordinator, hotKey, hook, _, overlay, _, _, _) =
            CoordinatorTestHelper.CreateCoordinator();
        using (coordinator) {
            var picker = new FakeMacroPickerWindow();
            coordinator.MacroPickerWindow = picker;
            hotKey.SimulateActivation();
            hook.SimulateKey(VKey.OemQuestion);

            hook.SimulateKey(VKey.OemTilde);

            Assert.Null(overlay.CurrentHelp);
            Assert.True(picker.IsShown);
            Assert.Equal(1, picker.ShowCount);
            Assert.False(overlay.IsVisible);
            Assert.False(overlay.HelpVisibleAtLastHide);
        }
    }

    [Fact]
    public void RecordingSlotKey_ClosesHelpAndSelectsTheSlotNormally() {
        var store = new FakeMacroStore();
        var (coordinator, hotKey, hook, _, overlay, _, _, _) =
            CoordinatorTestHelper.CreateCoordinator(macroStore: store);
        using (coordinator) {
            hotKey.SimulateActivation();
            hook.SimulateKey(VKey.OemQuestion);
            hook.SimulateKeyUp(VKey.OemQuestion);
            hook.SimulateKey(VKey.OemPipe);
            Assert.Null(overlay.CurrentHelp);
            Assert.Contains("slot", overlay.StatusText ?? "", StringComparison.OrdinalIgnoreCase);

            hook.SimulateKey(VKey.OemQuestion);
            Assert.NotNull(overlay.CurrentHelp);
            hook.SimulateKey(VKey.F1);

            Assert.Null(overlay.CurrentHelp);
            Assert.True(overlay.RecordingBorderVisible);
            Assert.Equal(0, store.SaveCount);
        }
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
        hook.SimulateKeyDown(VKey.OemQuestion, HookModifierFlags.None);
        Assert.Equal(1, overlay.ShowHelpCount);
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
        hook.SimulateKeyDown(VKey.Escape);
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
