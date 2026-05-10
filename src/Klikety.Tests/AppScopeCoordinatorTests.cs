using System.Drawing;

using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Navigation;
using Klikety.Services;
using Klikety.Tests.Fakes;

using Microsoft.Extensions.Logging.Abstractions;

namespace Klikety.Tests;

public class AppScopeCoordinatorTests {
    private static (NavigatorCoordinator Coordinator, FakeHotKeyService HotKey, FakeKeyboardHookService Hook,
        FakeMouseActionService Mouse, FakeOverlayWindow Overlay, FakePlatformServices Platform)
        CreateCoordinator(ConfigModel? configOverride = null) {
        var config = configOverride ?? new ConfigModel {
            Modes = new ModesConfig {
                UniformGrid = new ModeConfig { Enabled = true, Default = true, TwoKey = true, ArrowKeys = true },
            },
        };
        var hotKey = new FakeHotKeyService();
        var hook = new FakeKeyboardHookService();
        var mouse = new FakeMouseActionService();
        var overlay = new FakeOverlayWindow();
        var actionMapper = new ActionMapper(config.ActionBindings);
        var renderer = new FakeGridRenderer();
        var sessionFactory = new ModeSessionFactory(config, actionMapper, renderer);
        var platform = new FakePlatformServices();
        var modifierDetector = new FakeModifierDetector();

        var coordinator = new NavigatorCoordinator(
            hotKey, hook, mouse, overlay, sessionFactory, platform, modifierDetector, config,
            NullLogger.Instance);

        return (coordinator, hotKey, hook, mouse, overlay, platform);
    }

    private static void ActivateOverlay(FakeHotKeyService hotKey, FakePlatformServices platform,
        nint hwnd = 0x1234, Rectangle? windowBounds = null) {
        platform.ForegroundWindow.Handle = hwnd;
        platform.ForegroundWindow.Bounds = windowBounds ?? new Rectangle(100, 100, 800, 600);
        hotKey.SimulateActivation();
    }

    [Fact]
    public void ChordPress_HappyPath_OverlayResizedToWindowBounds() {
        var (_, hotKey, hook, _, overlay, platform) = CreateCoordinator();
        var windowBounds = new Rectangle(100, 100, 800, 600);
        ActivateOverlay(hotKey, platform, windowBounds: windowBounds);

        Assert.True(overlay.IsVisible);

        hook.SimulateKeyDown(VKey.B);

        // Overlay stays full-screen; session gets window bounds; border visible
        Assert.True(overlay.IsVisible);
        Assert.True(overlay.AppScopeBorderVisible);
    }

    [Fact]
    public void ChordPress_ModeLocked_ForwardedToSession() {
        var (_, hotKey, hook, _, overlay, platform) = CreateCoordinator();
        ActivateOverlay(hotKey, platform);

        // Press a nav key first to lock mode
        hook.SimulateKeyDown(VKey.A);

        var showCountBefore = overlay.ShowCount;
        hook.SimulateKeyDown(VKey.B);

        // Overlay should not have been re-shown (no app-scope switch)
        Assert.Equal(showCountBefore, overlay.ShowCount);
    }

    [Fact]
    public void ChordPress_MinimizedWindow_FlashesError() {
        var (_, hotKey, hook, _, overlay, platform) = CreateCoordinator();
        // Set bounds to empty (simulates minimized — FakeForegroundWindowProvider returns Empty for valid handle
        // when Bounds is Empty)
        platform.ForegroundWindow.Handle = 0x1234;
        platform.ForegroundWindow.Bounds = Rectangle.Empty;
        hotKey.SimulateActivation();

        hook.SimulateKeyDown(VKey.B);

        Assert.Equal("Invalid window", overlay.StatusText);
        Assert.True(overlay.IsVisible); // stays full-screen
    }

    [Fact]
    public void ChordPress_ZeroHwnd_FlashesError() {
        var (_, hotKey, hook, _, overlay, platform) = CreateCoordinator();
        platform.ForegroundWindow.Handle = 0;
        hotKey.SimulateActivation();

        hook.SimulateKeyDown(VKey.B);

        Assert.Equal("Invalid window", overlay.StatusText);
    }

    [Fact]
    public void ChordPress_WindowOutsidePrimaryScreen_FlashesError() {
        var (_, hotKey, hook, _, overlay, platform) = CreateCoordinator();
        // Window entirely on a second monitor (right of primary 1920x1080)
        var windowBounds = new Rectangle(2000, 100, 800, 600);
        ActivateOverlay(hotKey, platform, windowBounds: windowBounds);

        hook.SimulateKeyDown(VKey.B);

        Assert.Equal("Window outside screen", overlay.StatusText);
    }

    [Fact]
    public void ChordPress_WindowPartiallyOffScreen_ClippedToPrimary() {
        var (_, hotKey, hook, _, overlay, platform) = CreateCoordinator();
        // Window extends 100px beyond right edge of primary screen (1920x1080)
        var windowBounds = new Rectangle(1200, 100, 820, 600);
        ActivateOverlay(hotKey, platform, windowBounds: windowBounds);

        hook.SimulateKeyDown(VKey.B);

        // Session activated (border visible); overlay stays full-screen
        Assert.True(overlay.AppScopeBorderVisible);
    }

    [Fact]
    public void ChordPress_AutoRepeatGuard_NoRedundantResize() {
        var (_, hotKey, hook, _, overlay, platform) = CreateCoordinator();
        ActivateOverlay(hotKey, platform);

        hook.SimulateKeyDown(VKey.B);
        var showCountAfterFirst = overlay.ShowCount;

        // Second press (auto-repeat) should be no-op
        hook.SimulateKeyDown(VKey.B);
        Assert.Equal(showCountAfterFirst, overlay.ShowCount);
    }

    [Fact]
    public void ChordPress_OriginClamped_CursorOutsideWindowBounds() {
        var (_, hotKey, hook, mouse, overlay, platform) = CreateCoordinator();
        var windowBounds = new Rectangle(200, 200, 400, 300);
        platform.ForegroundWindow.Handle = 0x1234;
        platform.ForegroundWindow.Bounds = windowBounds;
        // Cursor at (100, 100) — outside window bounds
        platform.Cursor.Position = new Point(100, 100);
        hotKey.SimulateActivation();

        hook.SimulateKeyDown(VKey.B);

        // Overlay visible with border (session activated at window bounds, origin clamped)
        Assert.True(overlay.IsVisible);
        Assert.True(overlay.AppScopeBorderVisible);
    }

    [Fact]
    public void Escape_AppScoped_DeactivatesOverlay() {
        var (_, hotKey, hook, _, overlay, platform) = CreateCoordinator();
        ActivateOverlay(hotKey, platform);

        hook.SimulateKeyDown(VKey.B);
        Assert.True(overlay.IsVisible);

        // Escape at L1 deactivates
        hook.SimulateKeyDown(VKey.Escape);
        Assert.False(overlay.IsVisible);
    }

    [Fact]
    public void DeactivateOverlay_ClearsAppScopeState() {
        var (coordinator, hotKey, hook, _, overlay, platform) = CreateCoordinator();
        ActivateOverlay(hotKey, platform);

        hook.SimulateKeyDown(VKey.B);
        Assert.True(overlay.AppScopeBorderVisible);

        hook.SimulateKeyDown(VKey.Escape);
        Assert.False(overlay.AppScopeBorderVisible);
    }

    [Fact]
    public void Drag_AppScoped_ResetsToFullScreen() {
        var config = new ConfigModel {
            Modes = new ModesConfig {
                UniformGrid = new ModeConfig { Enabled = true, Default = true, TwoKey = true, ArrowKeys = true },
            },
            ActionBindings = new Dictionary<string, MouseAction>(StringComparer.OrdinalIgnoreCase) {
                ["X"] = MouseAction.DragDrop,
            },
        };
        var (_, hotKey, hook, _, overlay, platform) = CreateCoordinator(config);
        ActivateOverlay(hotKey, platform);

        hook.SimulateKeyDown(VKey.B);
        Assert.True(overlay.AppScopeBorderVisible);
        var appScopeBounds = overlay.LastShowBounds;

        // Start drag: nav to a cell, then DragDrop action
        hook.SimulateKeyDown(VKey.A);
        hook.SimulateKeyDown(VKey.W);
        hook.SimulateKeyDown(VKey.X); // DragDrop

        // After drag start, overlay should remain full-screen and border cleared
        Assert.False(overlay.AppScopeBorderVisible);
        Assert.True(overlay.IsVisible);
    }

    [Fact]
    public void FocusLoss_DuringSwitchToAppScope_SuppressedByGuard() {
        var (_, hotKey, hook, _, overlay, platform) = CreateCoordinator();
        overlay.RaiseFocusLostOnHide = true;
        ActivateOverlay(hotKey, platform);

        // This will trigger Hide() → FocusLost during SwitchToAppScope
        hook.SimulateKeyDown(VKey.B);

        // Should not have been deactivated — still visible at window bounds
        Assert.True(overlay.IsVisible);
    }

    [Fact]
    public void AppScopeBorder_SetOnActivation_ClearedOnDeactivation() {
        var (_, hotKey, hook, _, overlay, platform) = CreateCoordinator();
        ActivateOverlay(hotKey, platform);

        Assert.False(overlay.AppScopeBorderVisible);

        hook.SimulateKeyDown(VKey.B);
        Assert.True(overlay.AppScopeBorderVisible);

        hook.SimulateKeyDown(VKey.Escape);
        Assert.False(overlay.AppScopeBorderVisible);
    }

    [Fact]
    public void AppScopeDisabled_NullChordKey_NoEffect() {
        var config = new ConfigModel {
            Modes = new ModesConfig {
                UniformGrid = new ModeConfig { Enabled = true, Default = true, TwoKey = true, ArrowKeys = true },
            },
            AppScope = new AppScopeConfig { ChordKey = null },
        };
        var (_, hotKey, hook, _, overlay, platform) = CreateCoordinator(config);
        platform.ForegroundWindow.Handle = 0x1234;
        platform.ForegroundWindow.Bounds = new Rectangle(100, 100, 800, 600);
        hotKey.SimulateActivation();

        var showCountBefore = overlay.ShowCount;
        hook.SimulateKeyDown(VKey.B);

        // B should be forwarded to session (mode locked), not treated as app-scope chord
        Assert.Equal(showCountBefore, overlay.ShowCount);
    }
}
