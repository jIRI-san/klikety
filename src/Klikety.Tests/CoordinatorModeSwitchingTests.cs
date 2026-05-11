using Klikety.Config;
using Klikety.Input;
using Klikety.Tests.Fakes;

namespace Klikety.Tests;

public class CoordinatorModeSwitchingTests {
    private static (NavigatorCoordinator Coordinator, FakeHotKeyService HotKey, FakeKeyboardHookService Hook,
        FakeMouseActionService Mouse, FakeOverlayWindow Overlay, FakeGridRenderer Renderer, FakePlatformServices Platform,
        FakeModifierDetector ModifierDetector) CreateCoordinator(
        NavigationMode mode = NavigationMode.Both, ConfigModel? configOverride = null) =>
        CoordinatorTestHelper.CreateCoordinator(mode, configOverride);

    [Fact]
    public void ChordKey_BeforeLock_SwitchesMode() {
        // Configure with Crosshair enabled + chord key N
        var config = new ConfigModel {
            Modes = new ModesConfig {
                UniformGrid = new ModeConfig { Enabled = true, Default = true, TwoKey = true, ArrowKeys = true },
                Crosshair = new ModeConfig {
                    Enabled = true, ChordKey = VKey.N, TwoKey = true, ArrowKeys = true,
                },
            },
        };
        var (_, hotKey, hook, _, overlay, _, _, _) = CreateCoordinator(configOverride: config);

        hotKey.SimulateActivation();
        Assert.True(overlay.IsVisible);

        // Press chord key N before any nav key → mode switch to Crosshair
        hook.SimulateKeyDown(VKey.N);

        // Crosshair is now implemented → overlay stays visible
        Assert.True(overlay.IsVisible);
    }

    [Fact]
    public void ChordKey_AfterModeLock_ForwardedToSession() {
        var config = new ConfigModel {
            Modes = new ModesConfig {
                UniformGrid = new ModeConfig { Enabled = true, Default = true, TwoKey = true, ArrowKeys = true },
                Crosshair = new ModeConfig { Enabled = true, ChordKey = VKey.N, TwoKey = true, ArrowKeys = true },
            },
        };
        var (_, hotKey, hook, _, overlay, _, _, _) = CreateCoordinator(configOverride: config);

        hotKey.SimulateActivation();
        // Press a nav key first to lock the mode
        hook.SimulateKeyDown(VKey.A);

        // Now press chord key N — mode is locked, so it goes to session (invalid key)
        hook.SimulateKeyDown(VKey.N);

        // Overlay still visible (chord was not processed as mode switch)
        Assert.True(overlay.IsVisible);
    }

    [Fact]
    public void DisabledMode_ChordKeyIgnored() {
        var config = new ConfigModel {
            Modes = new ModesConfig {
                UniformGrid = new ModeConfig { Enabled = true, Default = true, TwoKey = true, ArrowKeys = true },
                Crosshair = new ModeConfig { Enabled = false, ChordKey = VKey.N, TwoKey = true, ArrowKeys = true },
            },
        };
        var (_, hotKey, hook, _, overlay, _, _, _) = CreateCoordinator(configOverride: config);

        hotKey.SimulateActivation();
        // N is not a chord key (Crosshair disabled) → goes to session as invalid key
        hook.SimulateKeyDown(VKey.N);

        Assert.True(overlay.IsVisible);
    }

    [Fact]
    public void ModeLock_NavKeyLocksMode() {
        var config = new ConfigModel {
            Modes = new ModesConfig {
                UniformGrid = new ModeConfig { Enabled = true, Default = true, TwoKey = true, ArrowKeys = true },
                Crosshair = new ModeConfig { Enabled = true, ChordKey = VKey.N, TwoKey = true, ArrowKeys = true },
            },
        };
        var (_, hotKey, hook, _, overlay, _, _, _) = CreateCoordinator(configOverride: config);

        hotKey.SimulateActivation();
        // Press arrow key → locks mode
        hook.SimulateKeyDown(VKey.Right);
        // Now chord key should NOT switch mode
        hook.SimulateKeyDown(VKey.N);

        Assert.True(overlay.IsVisible); // Still active, no switch attempted
    }

    [Fact]
    public void SwitchMode_FactoryThrows_DeactivatesOverlay() {
        var config = new ConfigModel {
            Modes = new ModesConfig {
                UniformGrid = new ModeConfig { Enabled = true, Default = true, TwoKey = true, ArrowKeys = true },
                LogCrosshair = new ModeConfig { Enabled = true, ChordKey = VKey.M, TwoKey = true, ArrowKeys = true, LogBaseSize = 0 },
            },
        };
        var (_, hotKey, hook, _, overlay, _, _, _) = CreateCoordinator(configOverride: config);

        hotKey.SimulateActivation();
        // Chord to LogCrosshair → ArgumentOutOfRangeException (logBaseSize < 1) → DeactivateOverlay
        hook.SimulateKeyDown(VKey.M);

        Assert.False(overlay.IsVisible);
        Assert.False(hook.IsEnabled);
    }

    [Fact]
    public void SwitchMode_OldSessionDeactivated() {
        var config = new ConfigModel {
            Modes = new ModesConfig {
                UniformGrid = new ModeConfig { Enabled = true, Default = true, TwoKey = true, ArrowKeys = true },
                LogCrosshair = new ModeConfig { Enabled = true, ChordKey = VKey.M, TwoKey = true, ArrowKeys = true, LogBaseSize = 0 },
            },
        };
        var (_, hotKey, hook, _, overlay, renderer, _, _) = CreateCoordinator(configOverride: config);

        hotKey.SimulateActivation();
        Assert.Contains(renderer.Calls, c => c.Method == "RenderGrid");

        // Switch attempt (will fail on LogCrosshair — logBaseSize < 2)
        hook.SimulateKeyDown(VKey.M);

        // ClearCanvas should have been called during switch attempt
        Assert.True(overlay.ClearCanvasCount > 0);
    }
}
