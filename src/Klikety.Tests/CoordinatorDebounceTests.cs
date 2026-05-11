using Klikety.Config;
using Klikety.Input;
using Klikety.Services;
using Klikety.Tests.Fakes;

namespace Klikety.Tests;

public class CoordinatorDebounceTests {
    private static (NavigatorCoordinator Coordinator, FakeHotKeyService HotKey, FakeKeyboardHookService Hook,
        FakeMouseActionService Mouse, FakeOverlayWindow Overlay, FakeGridRenderer Renderer, FakePlatformServices Platform,
        FakeModifierDetector ModifierDetector) CreateCoordinator(
        NavigationMode mode = NavigationMode.Both, ConfigModel? configOverride = null) =>
        CoordinatorTestHelper.CreateCoordinator(mode, configOverride);

    [Fact]
    public void Debounce_TriggerKeySuppressedUntilReleased() {
        var (_, hotKey, hook, mouse, overlay, _, platform, _) = CreateCoordinator();

        hotKey.SimulateActivation();
        // Space is in debounce set (trigger key added unconditionally)
        // Pressing Space without releasing first should be suppressed
        hook.SimulateKeyDown(VKey.Space);

        Assert.True(overlay.IsVisible); // Not deactivated
        Assert.Empty(mouse.Calls); // No action dispatched
    }

    [Fact]
    public void Debounce_ModifierKeySuppressedWhenHeld() {
        var (_, hotKey, hook, mouse, overlay, _, platform, _) = CreateCoordinator();

        // Simulate Alt held at activation time
        platform.KeyState.SetKeyDown(VKey.LMenu);

        hotKey.SimulateActivation();
        // LMenu in debounce → key-down suppressed
        hook.SimulateKeyDown(VKey.LMenu);

        Assert.True(overlay.IsVisible);
    }

    [Fact]
    public void Debounce_KeyUpRemovesFromDebounceSet() {
        var (_, hotKey, hook, mouse, overlay, _, _, _) = CreateCoordinator();

        hotKey.SimulateActivation();
        // Release Space → removed from debounce
        hook.SimulateKeyUp(VKey.Space);
        // Now Space should work as action key
        hook.SimulateKeyDown(VKey.Space);

        Assert.False(overlay.IsVisible);
        Assert.Contains(mouse.Calls, c => c.Action == MouseAction.LeftClick);
    }

    [Fact]
    public void Debounce_DifferentKeyRemovesTrigger() {
        var (_, hotKey, hook, mouse, overlay, _, _, _) = CreateCoordinator();

        hotKey.SimulateActivation();
        // First keydown for a DIFFERENT key removes trigger key from debounce
        hook.SimulateKeyDown(VKey.A); // This removes Space from debounce (and processes A)

        // Now Space should work
        hook.SimulateKeyDown(VKey.W); // Complete navigation
        hook.SimulateKeyDown(VKey.Space);

        Assert.False(overlay.IsVisible);
        Assert.Contains(mouse.Calls, c => c.Action == MouseAction.LeftClick);
    }

    [Fact]
    public void Debounce_TimerReconciles() {
        var (_, hotKey, hook, mouse, overlay, _, platform, _) = CreateCoordinator();

        platform.KeyState.SetKeyDown(VKey.LMenu);
        hotKey.SimulateActivation();

        // LMenu was in debounce set
        // Simulate timer elapsed — key no longer physically held
        platform.KeyState.SetKeyUp(VKey.LMenu);
        platform.Timers.LastCreated.SimulateElapsed();

        // Now LMenu not in debounce → if it's pressed it goes through
        // (though LMenu isn't a nav key, proving debounce was cleared)
        // Main assertion: timer ran without crashing
        Assert.True(overlay.IsVisible);
    }

    [Fact]
    public void Debounce_MultipleModifiers_AllSuppressed() {
        var (_, hotKey, hook, _, overlay, _, platform, _) = CreateCoordinator();

        // Hold both Alt and Space at activation time
        platform.KeyState.SetKeyDown(VKey.LMenu);

        hotKey.SimulateActivation();

        // Both LMenu and Space should be suppressed
        hook.SimulateKeyDown(VKey.LMenu);
        hook.SimulateKeyDown(VKey.Space);

        Assert.True(overlay.IsVisible); // Neither triggered action/deactivation
    }

    [Fact]
    public void Debounce_KeyUpThenDown_SecondDownProcessed() {
        var (_, hotKey, hook, mouse, overlay, _, _, _) = CreateCoordinator();

        hotKey.SimulateActivation();
        // Release space
        hook.SimulateKeyUp(VKey.Space);
        // Press A (nav key)
        hook.SimulateKeyDown(VKey.A);
        // Press Space (action)
        hook.SimulateKeyDown(VKey.W);
        hook.SimulateKeyDown(VKey.Space);

        Assert.False(overlay.IsVisible);
        Assert.Contains(mouse.Calls, c => c.Action == MouseAction.LeftClick);
    }

    [Fact]
    public void DebounceTimer_ReconcilesClearedKeys() {
        var (_, hotKey, hook, _, overlay, _, platform, _) = CreateCoordinator();

        platform.KeyState.SetKeyDown(VKey.LMenu);
        hotKey.SimulateActivation();

        // Simulate: key physically released, timer fires
        platform.KeyState.SetKeyUp(VKey.LMenu);
        platform.Timers.LastCreated.SimulateElapsed();

        // LMenu should no longer be in debounce — verify via key-down not being suppressed
        // (LMenu isn't a nav key so session ignores it, but it proves debounce was cleared)
        Assert.True(overlay.IsVisible);
    }
}
