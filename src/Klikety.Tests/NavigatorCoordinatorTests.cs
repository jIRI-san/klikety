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
            config.FirstKeys, config.SecondKeys,
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

        hook.SimulateKey(VKey.A);
        hook.SimulateKey(VKey.W);
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
        Assert.Contains(mouse.Calls, c => c.Action is null);
    }

    [Fact]
    public void InvalidKey_NoTransition() {
        var (_, hotKey, hook, mouse, overlay, _) = CreateCoordinator();

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.Z);

        Assert.True(overlay.IsVisible);
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

        Assert.True(overlay.IsVisible);
        Assert.Empty(mouse.Calls);
    }

    [Fact]
    public void FirstKey_CallsHighlightColumn() {
        var (_, hotKey, hook, _, _, renderer) = CreateCoordinator();

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.A);

        Assert.Contains(renderer.Calls, c => c.Method == "HighlightColumn");
    }

    [Fact]
    public void CellEntered_RendersSubgridOverGrid() {
        var (_, hotKey, hook, _, _, renderer) = CreateCoordinator();

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.A);
        hook.SimulateKey(VKey.W);

        Assert.Contains(renderer.Calls, c => c.Method == "RenderSubgridOverGrid");
    }

    [Fact]
    public void L2ColumnHighlighted_UsesHighlightColumnOverGrid() {
        var (_, hotKey, hook, _, _, renderer) = CreateCoordinator();

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.A);
        hook.SimulateKey(VKey.W);
        hook.SimulateKey(VKey.A);

        Assert.Contains(renderer.Calls, c => c.Method == "HighlightColumnOverGrid");
    }

    [Fact]
    public void EscapeFromL2_CallsRenderGrid() {
        var (_, hotKey, hook, _, _, renderer) = CreateCoordinator();

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.A);
        hook.SimulateKey(VKey.W);
        hook.SimulateKey(VKey.A);

        renderer.Calls.Clear();
        hook.SimulateKey(VKey.Escape);

        Assert.Contains(renderer.Calls, c => c.Method == "RenderGrid");
    }

    [Fact]
    public void EscapeFromL2AwaitAction_CallsRenderSubgridOverGrid() {
        var (_, hotKey, hook, _, _, renderer) = CreateCoordinator();

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.A);
        hook.SimulateKey(VKey.W);
        hook.SimulateKey(VKey.A);
        hook.SimulateKey(VKey.W);

        renderer.Calls.Clear();
        hook.SimulateKey(VKey.Escape);

        Assert.Contains(renderer.Calls, c => c.Method == "RenderSubgridOverGrid");
    }

    [Fact]
    public void Backspace_CallsRenderGrid() {
        var (_, hotKey, hook, _, _, renderer) = CreateCoordinator();

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.A);

        renderer.Calls.Clear();
        hook.SimulateKey(VKey.Back);

        Assert.Contains(renderer.Calls, c => c.Method == "RenderGrid");
    }

    [Fact]
    public void DeactivateOverlay_CallsClearCanvas() {
        var (_, hotKey, hook, _, overlay, renderer) = CreateCoordinator();

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.Escape);

        Assert.Contains(renderer.Calls, c => c.Method == "ClearCanvas");
        Assert.False(overlay.IsVisible);
    }
}
