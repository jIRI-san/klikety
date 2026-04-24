using System.Drawing;

using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Navigation;
using Klikety.Tests.Fakes;

using Microsoft.Extensions.Logging.Abstractions;

namespace Klikety.Tests;

public class NavigatorCoordinatorTests {
    private static (NavigatorCoordinator Coordinator, FakeHotKeyService HotKey, FakeKeyboardHookService Hook,
        FakeMouseActionService Mouse, FakeOverlayWindow Overlay, FakeGridRenderer Renderer) CreateCoordinator(
        NavigationMode mode = NavigationMode.Both) {
        var config = new ConfigModel { NavigationMode = mode };
        var hotKey = new FakeHotKeyService();
        var hook = new FakeKeyboardHookService();
        var mouse = new FakeMouseActionService();
        var overlay = new FakeOverlayWindow();
        var actionMapper = new ActionMapper(config.ActionBindings);
        var sm = new NavigatorStateMachine(
            config.KeySets.Left, config.KeySets.Right,
            actionMapper, config.NavigationMode, config.Level3CellSizeThreshold);
        var renderer = new FakeGridRenderer();

        var coordinator = new NavigatorCoordinator(
            hotKey, hook, mouse, overlay, sm, renderer, config,
            NullLogger.Instance);

        return (coordinator, hotKey, hook, mouse, overlay, renderer);
    }

    [Fact]
    public void HookFailure_OverlayClosedImmediately() {
        var (_, hotKey, hook, _, overlay, _) = CreateCoordinator();
        hook.ShouldFailOnEnable = true;

        hotKey.SimulateActivation();

        Assert.False(overlay.IsVisible);
        Assert.Equal(1, overlay.HideCount);
    }

    [Fact]
    public void FocusLoss_DeactivatesOverlay() {
        var (_, hotKey, hook, _, overlay, _) = CreateCoordinator();

        hotKey.SimulateActivation();
        Assert.True(overlay.IsVisible);

        overlay.SimulateFocusLoss();
        Assert.False(overlay.IsVisible);
        Assert.False(hook.IsEnabled);
    }

    [Fact]
    public void FullL1Navigation_ActionDispatched() {
        var (_, hotKey, hook, mouse, overlay, _) = CreateCoordinator();

        hotKey.SimulateActivation();
        Assert.True(overlay.IsVisible);

        // L1: first key A (col 0), second key W (row 0)
        hook.SimulateKey(VKey.A);
        hook.SimulateKey(VKey.W);

        // Action key Space → LeftClick
        hook.SimulateKey(VKey.Space);

        Assert.False(overlay.IsVisible);
        Assert.Contains(mouse.Calls, c => c.Action == MouseAction.LeftClick);
    }

    [Fact]
    public void EscapeAtL1_RestoresCursor() {
        var (_, hotKey, hook, mouse, overlay, _) = CreateCoordinator();

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.Escape);

        Assert.False(overlay.IsVisible);
        // Should have a MoveTo call for cursor restore
        Assert.Contains(mouse.Calls, c => c.Action is null); // MoveTo has null action
    }

    [Fact]
    public void InvalidKey_NoTransition() {
        var (_, hotKey, hook, mouse, overlay, _) = CreateCoordinator();

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.Z); // not in firstKeys

        Assert.True(overlay.IsVisible); // still showing
        Assert.Empty(mouse.Calls);
    }

    [Fact]
    public void ArrowNavigation_ThenEnter() {
        var (_, hotKey, hook, mouse, overlay, _) = CreateCoordinator(NavigationMode.Both);

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.Right);
        hook.SimulateKey(VKey.Return);

        Assert.False(overlay.IsVisible);
        Assert.Contains(mouse.Calls, c => c.Action == MouseAction.LeftClick);
    }

    [Fact]
    public void TwoKeyMode_ArrowsIgnored() {
        var (_, hotKey, hook, mouse, overlay, _) = CreateCoordinator(NavigationMode.TwoKey);

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.Right);
        hook.SimulateKey(VKey.Return);

        Assert.True(overlay.IsVisible); // should still be showing — arrows did nothing
        Assert.Empty(mouse.Calls);
    }

    [Fact]
    public void LeftFirstKey_CallsSetActiveHalf_And_HighlightColumnSplitScreen() {
        var (_, hotKey, hook, _, _, renderer) = CreateCoordinator();

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.A);

        Assert.Contains(renderer.Calls, c => c.Method == "SetActiveHalf" && c.Half == ScreenHalf.Left);
        Assert.Contains(renderer.Calls, c => c.Method == "HighlightColumnSplitScreen" && c.Half == ScreenHalf.Left);
    }

    [Fact]
    public void RightFirstKey_DispatchesToRightHalf() {
        var (_, hotKey, hook, mouse, _, _) = CreateCoordinator();

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.J); // right first key
        hook.SimulateKey(VKey.U); // right second key
        hook.SimulateKey(VKey.Space); // action

        Assert.Contains(mouse.Calls, c => c.Action == MouseAction.LeftClick);
    }

    [Fact]
    public void CellEntered_DoesNotRenderSubgrid() {
        var (_, hotKey, hook, _, _, renderer) = CreateCoordinator();

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.A); // L1 first
        hook.SimulateKey(VKey.W); // L1 second → CellEntered

        Assert.DoesNotContain(renderer.Calls, c => c.Method == "RenderSubgrid");
    }

    [Fact]
    public void EscapeFromL2_CallsRenderBothHalves() {
        var (_, hotKey, hook, _, _, renderer) = CreateCoordinator();

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.A); // L1 first
        hook.SimulateKey(VKey.W); // L1 second → AwaitAction
        hook.SimulateKey(VKey.A); // Navigate into L2

        renderer.Calls.Clear();
        hook.SimulateKey(VKey.Escape); // Escape from L2 → back to L1

        Assert.Contains(renderer.Calls, c => c.Method == "RenderBothHalves");
    }

    [Fact]
    public void Backspace_CallsSetActiveHalf_And_RenderBothHalves() {
        var (_, hotKey, hook, _, _, renderer) = CreateCoordinator();

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.A); // L1 first → AwaitSecond

        renderer.Calls.Clear();
        hook.SimulateKey(VKey.Back); // Backspace → back to AwaitFirst

        Assert.Contains(renderer.Calls, c => c.Method == "SetActiveHalf" && c.Half == ScreenHalf.Left);
        Assert.Contains(renderer.Calls, c => c.Method == "RenderBothHalves");
    }

    [Fact]
    public void Backspace_FromRightHalf_ResetsToLeft() {
        var (_, hotKey, hook, _, _, renderer) = CreateCoordinator();

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.J); // right half → AwaitSecond

        renderer.Calls.Clear();
        hook.SimulateKey(VKey.Back); // Backspace → back to AwaitFirst

        Assert.Contains(renderer.Calls, c => c.Method == "SetActiveHalf" && c.Half == ScreenHalf.Left);
        Assert.Contains(renderer.Calls, c => c.Method == "RenderBothHalves");
    }

    [Fact]
    public void DeactivateOverlay_CallsClearCanvas() {
        var (_, hotKey, hook, _, overlay, renderer) = CreateCoordinator();

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.Escape); // cancel → deactivate

        Assert.Contains(renderer.Calls, c => c.Method == "ClearCanvas");
        Assert.False(overlay.IsVisible);
    }
}
