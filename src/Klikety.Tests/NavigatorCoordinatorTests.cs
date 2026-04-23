using System.Drawing;
using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Navigation;
using Klikety.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Klikety.Tests;

public class NavigatorCoordinatorTests
{
    private static (NavigatorCoordinator Coordinator, FakeHotKeyService HotKey, FakeKeyboardHookService Hook,
        FakeMouseActionService Mouse, FakeOverlayWindow Overlay) CreateCoordinator(
        NavigationMode mode = NavigationMode.Both)
    {
        var config = new ConfigModel { NavigationMode = mode };
        var hotKey = new FakeHotKeyService();
        var hook = new FakeKeyboardHookService();
        var mouse = new FakeMouseActionService();
        var overlay = new FakeOverlayWindow();
        var labelGen = new LabelGenerator(config.KeySets.FirstKeys, config.KeySets.SecondKeys);
        var actionMapper = new ActionMapper(config.ActionBindings);
        var sm = new NavigatorStateMachine(
            config.KeySets.FirstKeys, config.KeySets.SecondKeys,
            actionMapper, config.NavigationMode, config.Level3CellSizeThreshold);

        var coordinator = new NavigatorCoordinator(
            hotKey, hook, mouse, overlay, sm, null, labelGen, config,
            NullLogger.Instance);

        return (coordinator, hotKey, hook, mouse, overlay);
    }

    [Fact]
    public void HookFailure_OverlayClosedImmediately()
    {
        var (_, hotKey, hook, _, overlay) = CreateCoordinator();
        hook.ShouldFailOnEnable = true;

        hotKey.SimulateActivation();

        Assert.False(overlay.IsVisible);
        Assert.Equal(1, overlay.HideCount);
    }

    [Fact]
    public void FocusLoss_DeactivatesOverlay()
    {
        var (_, hotKey, hook, _, overlay) = CreateCoordinator();

        hotKey.SimulateActivation();
        Assert.True(overlay.IsVisible);

        overlay.SimulateFocusLoss();
        Assert.False(overlay.IsVisible);
        Assert.False(hook.IsEnabled);
    }

    [Fact]
    public void FullL1Navigation_ActionDispatched()
    {
        var (_, hotKey, hook, mouse, overlay) = CreateCoordinator();

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
    public void EscapeAtL1_RestoresCursor()
    {
        var (_, hotKey, hook, mouse, overlay) = CreateCoordinator();

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.Escape);

        Assert.False(overlay.IsVisible);
        // Should have a MoveTo call for cursor restore
        Assert.Contains(mouse.Calls, c => c.Action is null); // MoveTo has null action
    }

    [Fact]
    public void InvalidKey_NoTransition()
    {
        var (_, hotKey, hook, mouse, overlay) = CreateCoordinator();

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.Z); // not in firstKeys

        Assert.True(overlay.IsVisible); // still showing
        Assert.Empty(mouse.Calls);
    }

    [Fact]
    public void ArrowNavigation_ThenEnter()
    {
        var (_, hotKey, hook, mouse, overlay) = CreateCoordinator(NavigationMode.Both);

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.Right);
        hook.SimulateKey(VKey.Return);

        Assert.False(overlay.IsVisible);
        Assert.Contains(mouse.Calls, c => c.Action == MouseAction.LeftClick);
    }

    [Fact]
    public void TwoKeyMode_ArrowsIgnored()
    {
        var (_, hotKey, hook, mouse, overlay) = CreateCoordinator(NavigationMode.TwoKey);

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.Right);
        hook.SimulateKey(VKey.Return);

        Assert.True(overlay.IsVisible); // should still be showing — arrows did nothing
        Assert.Empty(mouse.Calls);
    }
}
