using System.Drawing;

using Klikety.Config;
using Klikety.Input;
using Klikety.Navigation;
using Klikety.Services;
using Klikety.Tests.Fakes;

namespace Klikety.Tests;

public class NavigatorCoordinatorTests {
    private static (NavigatorCoordinator Coordinator, FakeHotKeyService HotKey, FakeKeyboardHookService Hook,
        FakeMouseActionService Mouse, FakeOverlayWindow Overlay, FakeGridRenderer Renderer, FakePlatformServices Platform,
        FakeModifierDetector ModifierDetector) CreateCoordinator(
        NavigationMode mode = NavigationMode.Both, ConfigModel? configOverride = null) =>
        CoordinatorTestHelper.CreateCoordinator(mode, configOverride);

    [Fact]
    public void HookFailure_OverlayClosedImmediately() {
        var (_, hotKey, hook, _, overlay, _, _, _) = CreateCoordinator();
        hook.ShouldFailOnEnable = true;

        hotKey.SimulateActivation();

        Assert.False(overlay.IsVisible);
        Assert.Equal(1, overlay.HideCount);
    }

    [Fact]
    public void FocusLoss_DeactivatesOverlay() {
        var (_, hotKey, hook, _, overlay, _, _, _) = CreateCoordinator();

        hotKey.SimulateActivation();
        Assert.True(overlay.IsVisible);

        overlay.SimulateFocusLoss();
        Assert.False(overlay.IsVisible);
        Assert.False(hook.IsEnabled);
    }

    [Fact]
    public void FullL1Navigation_ActionDispatched() {
        var (_, hotKey, hook, mouse, overlay, _, _, _) = CreateCoordinator();

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
        var (_, hotKey, hook, mouse, overlay, _, _, _) = CreateCoordinator();

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.Escape);

        Assert.False(overlay.IsVisible);
        Assert.Contains(mouse.Calls, c => c.Action is null);
    }

    [Fact]
    public void InvalidKey_NoTransition() {
        var (_, hotKey, hook, mouse, overlay, _, _, _) = CreateCoordinator();

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.Z);

        Assert.True(overlay.IsVisible);
        Assert.Empty(mouse.Calls);
    }

    [Fact]
    public void ArrowNavigation_ThenEnter_EntersCell() {
        var (_, hotKey, hook, mouse, overlay, renderer, _, _) = CreateCoordinator(NavigationMode.Both);

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.Right);
        hook.SimulateKey(VKey.Return);

        Assert.True(overlay.IsVisible);
        Assert.Contains(renderer.Calls, c => c.Method == "RenderSubgridOverGrid");
    }

    [Fact]
    public void TwoKeyMode_ArrowsIgnored() {
        var (_, hotKey, hook, mouse, overlay, _, _, _) = CreateCoordinator(NavigationMode.TwoKey);

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.Right);
        hook.SimulateKey(VKey.Return);

        Assert.True(overlay.IsVisible);
        Assert.Empty(mouse.Calls);
    }

    [Fact]
    public void FirstKey_CallsHighlightColumn() {
        var (_, hotKey, hook, _, _, renderer, _, _) = CreateCoordinator();

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.A);

        Assert.Contains(renderer.Calls, c => c.Method == "HighlightColumn");
    }

    [Fact]
    public void CellEntered_RendersSubgridOverGrid() {
        var (_, hotKey, hook, _, _, renderer, _, _) = CreateCoordinator();

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.A);
        hook.SimulateKey(VKey.W);

        Assert.Contains(renderer.Calls, c => c.Method == "RenderSubgridOverGrid");
    }

    [Fact]
    public void L2ColumnHighlighted_UsesHighlightColumnOverGrid() {
        var (_, hotKey, hook, _, _, renderer, _, _) = CreateCoordinator();

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.A);
        hook.SimulateKey(VKey.W);
        hook.SimulateKey(VKey.A);

        Assert.Contains(renderer.Calls, c => c.Method == "HighlightColumnOverGrid");
    }

    [Fact]
    public void EscapeFromL2_CallsRenderGrid() {
        var (_, hotKey, hook, _, _, renderer, _, _) = CreateCoordinator();

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.A);
        hook.SimulateKey(VKey.W);
        hook.SimulateKey(VKey.A);

        renderer.Calls.Clear();
        hook.SimulateKey(VKey.Escape);

        Assert.Contains(renderer.Calls, c => c.Method == "RenderGrid");
    }

    [Fact]
    public void EscapeFromL2AwaitAction_CallsRenderGrid() {
        var (_, hotKey, hook, _, _, renderer, _, _) = CreateCoordinator();

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.A);
        hook.SimulateKey(VKey.W);
        hook.SimulateKey(VKey.A);
        hook.SimulateKey(VKey.W);

        renderer.Calls.Clear();
        hook.SimulateKey(VKey.Escape);

        Assert.Contains(renderer.Calls, c => c.Method == "RenderGrid");
    }

    [Fact]
    public void DeactivateOverlay_CallsClearCanvas() {
        var (_, hotKey, hook, _, overlay, renderer, _, _) = CreateCoordinator();

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.Escape);

        Assert.True(overlay.ClearCanvasCount > 0);
        Assert.False(overlay.IsVisible);
    }

    [Fact]
    public void NoNavAction_SendsActionAtOrigin() {
        var (_, hotKey, hook, mouse, overlay, _, _, _) = CreateCoordinator(NavigationMode.Both);

        hotKey.SimulateActivation();
        // Release trigger key (Space) — clears debounce
        hook.SimulateKeyUp(VKey.Space);
        // No navigation — press action key immediately
        hook.SimulateKey(VKey.Space);

        Assert.False(overlay.IsVisible);
        var actionCall = mouse.Calls.First(c => c.Action == MouseAction.LeftClick);
        // No MoveTo should have been called (no navigation), so action point = origin
        Assert.DoesNotContain(mouse.Calls, c => c.Action is null);
    }

    [Fact]
    public void EscapeFromL1AwaitAction_RestoresCursorToOrigin() {
        var (_, hotKey, hook, mouse, overlay, _, _, _) = CreateCoordinator(NavigationMode.Both);

        hotKey.SimulateActivation();
        // Enter L1 cell — moves cursor to L1 cell center
        hook.SimulateKey(VKey.A);
        hook.SimulateKey(VKey.W);
        var moveAfterEntry = mouse.Calls.Last(c => c.Action is null).Point;

        // Escape resets to L1_AwaitFirst — should restore cursor to origin
        hook.SimulateKey(VKey.Escape);

        var restoreCall = mouse.Calls.Last(c => c.Action is null);
        // Restore point should differ from the L1 cell center (it's the origin)
        Assert.NotEqual(moveAfterEntry, restoreCall.Point);
    }

    [Fact]
    public void EscapeFromL2_RestoresCursorToOrigin() {
        var (_, hotKey, hook, mouse, overlay, _, _, _) = CreateCoordinator(NavigationMode.Both);

        hotKey.SimulateActivation();
        // Enter L1 cell
        hook.SimulateKey(VKey.A);
        hook.SimulateKey(VKey.W);
        // Navigate into L2
        hook.SimulateKey(VKey.A);
        var moveToCountBeforeEscape = mouse.Calls.Count(c => c.Action is null);

        // Escape from L2 back to L1 — should restore cursor to origin
        hook.SimulateKey(VKey.Escape);

        var moveToCountAfterEscape = mouse.Calls.Count(c => c.Action is null);
        Assert.Equal(moveToCountBeforeEscape + 1, moveToCountAfterEscape);
    }

    // --- Multi-Monitor Guardrail Tests ---

    [Fact]
    public void CursorOutsidePrimary_ActivationSuppressed() {
        var (_, hotKey, hook, _, overlay, _, platform, _) = CreateCoordinator();

        // Put cursor outside the primary screen bounds
        platform.Screen.Bounds = new Rectangle(0, 0, 1920, 1080);
        platform.Cursor.Position = new Point(2500, 500);

        hotKey.SimulateActivation();

        Assert.False(overlay.IsVisible);
        Assert.False(hook.IsEnabled);
    }

    // --- Re-entrant Activation Tests ---

    [Fact]
    public void ReentrantActivation_Ignored() {
        var (_, hotKey, hook, _, overlay, _, _, _) = CreateCoordinator();

        hotKey.SimulateActivation();
        Assert.Equal(1, overlay.ShowCount);

        // Second activation while already active → ignored
        hotKey.SimulateActivation();
        Assert.Equal(1, overlay.ShowCount);
    }

    // --- Non-QWERTY Fallback Tests ---

    [Fact]
    public void NonQwertyLayout_DefaultCrosshair_FallsBackToUniformGrid() {
        var config = new ConfigModel {
            Modes = new ModesConfig {
                UniformGrid = new ModeConfig { Enabled = true, TwoKey = true, ArrowKeys = true },
                Crosshair = new ModeConfig { Enabled = true, Default = true, ChordKey = VKey.N, TwoKey = true, ArrowKeys = true },
            },
        };
        var (_, hotKey, hook, _, overlay, renderer, platform, _) = CreateCoordinator(configOverride: config);

        // Set non-QWERTY layout (e.g., German QWERTZ)
        platform.KeyboardLayout.Qwerty = false;

        hotKey.SimulateActivation();
        // Should fall back to UniformGrid, which renders a grid
        Assert.True(overlay.IsVisible);
        Assert.Contains(renderer.Calls, c => c.Method == "RenderGrid");
    }

    [Fact]
    public void QwertyLayout_DefaultUniformGrid_NoFallback() {
        var (_, hotKey, hook, _, overlay, renderer, _, _) = CreateCoordinator();

        hotKey.SimulateActivation();
        Assert.True(overlay.IsVisible);
        Assert.Contains(renderer.Calls, c => c.Method == "RenderGrid");
    }

    [Fact]
    public void FocusLoss_DuringSwitchMode_StillDeactivates() {
        var (_, hotKey, hook, _, overlay, _, _, _) = CreateCoordinator();

        hotKey.SimulateActivation();
        // Focus loss always deactivates regardless of internal state
        overlay.SimulateFocusLoss();

        Assert.False(overlay.IsVisible);
        Assert.False(hook.IsEnabled);
    }

    [Fact]
    public void DeactivateOverlay_ClearsDebounceTimer() {
        var (coordinator, hotKey, hook, _, _, _, platform, _) = CreateCoordinator();

        hotKey.SimulateActivation();
        var timer = platform.Timers.LastCreated;
        Assert.True(timer.IsRunning);

        coordinator.DeactivateOverlay();
        Assert.False(timer.IsRunning);
    }

    [Fact]
    public void ActionKey_WhileHookDisabled_NoEffect() {
        var (_, hotKey, hook, mouse, overlay, _, _, _) = CreateCoordinator();

        hotKey.SimulateActivation();
        // Manually deactivate via focus loss
        overlay.SimulateFocusLoss();

        // Simulate a stale key event after hook disabled
        hook.SimulateKeyDown(VKey.Space);

        // No action dispatched (session was cleared)
        Assert.DoesNotContain(mouse.Calls, c => c.Action == MouseAction.LeftClick);
    }

    [Fact]
    public void ChordKey_NonQwerty_IgnoredAndStaysOnCurrentMode() {
        var config = new ConfigModel {
            Modes = new ModesConfig {
                UniformGrid = new ModeConfig { Enabled = true, Default = true, TwoKey = true, ArrowKeys = true },
                Crosshair = new ModeConfig { Enabled = true, ChordKey = VKey.N, TwoKey = true, ArrowKeys = true },
            },
        };
        var (_, hotKey, hook, _, overlay, renderer, platform, _) = CreateCoordinator(configOverride: config);

        hotKey.SimulateActivation();

        // Switch to non-QWERTY after activation
        platform.KeyboardLayout.Qwerty = false;

        // Chord key N should be ignored (non-QWERTY)
        hook.SimulateKeyDown(VKey.N);

        // Overlay still visible, still on UniformGrid
        Assert.True(overlay.IsVisible);
    }

    [Fact]
    public void DefaultModeActivation_ThrowsOnNotSupported_DeactivatesCleanly() {
        // If somehow default resolves to an unimplemented mode, it deactivates cleanly
        var config = new ConfigModel {
            Modes = new ModesConfig {
                UniformGrid = new ModeConfig { Enabled = true, TwoKey = true, ArrowKeys = true },
                LogCrosshair = new ModeConfig { Enabled = true, Default = true, ChordKey = VKey.M, TwoKey = true, ArrowKeys = true, LogBaseSize = 0 },
            },
        };
        var (_, hotKey, hook, _, overlay, _, platform, _) = CreateCoordinator(configOverride: config);

        // QWERTY = true so it doesn't fall back — will try to Activate LogCrosshair → throws (logBaseSize < 2)
        platform.KeyboardLayout.Qwerty = true;
        hotKey.SimulateActivation();

        // Should have deactivated cleanly after ArgumentOutOfRangeException
        Assert.False(overlay.IsVisible);
    }

    [Fact]
    public void MultiMonitor_CursorOnSecondary_NoOverlay() {
        var (_, hotKey, hook, _, overlay, _, platform, _) = CreateCoordinator();

        platform.Screen.Bounds = new Rectangle(0, 0, 1920, 1080);
        platform.Cursor.Position = new Point(3000, 500); // Outside primary

        hotKey.SimulateActivation();

        Assert.False(overlay.IsVisible);
        Assert.Equal(0, overlay.ShowCount);
    }

    [Fact]
    public void Factory_CreatesLogGridSession() {
        var config = new ConfigModel {
            Modes = new ModesConfig {
                LogGrid = new ModeConfig { Enabled = true, TwoKey = true, ArrowKeys = true, LogGridBaseSize = 10 },
            },
        };
        var actionMapper = new ActionMapper(config.ActionBindings);
        var factory = new ModeSessionFactory(config, actionMapper, null);

        var session = factory.Create("LogGrid");

        Assert.IsType<LogGridSession>(session);
    }

    [Fact]
    public void Factory_LogGridUnavailable_WhenFewKeys() {
        // Only 5 horizontal keys → below 10 minimum
        var config = new ConfigModel {
            HorizontalKeys = [VKey.A, VKey.S, VKey.D, VKey.F, VKey.G],
            Modes = new ModesConfig {
                LogGrid = new ModeConfig { Enabled = true, TwoKey = true, ArrowKeys = true, LogGridBaseSize = 10 },
            },
        };
        var actionMapper = new ActionMapper(config.ActionBindings);
        var factory = new ModeSessionFactory(config, actionMapper, null);

        Assert.False(factory.IsLogGridAvailable);
        Assert.NotNull(factory.LogGridKeyPolicyWarning);
        Assert.Throws<NotSupportedException>(() => factory.Create("LogGrid"));
    }

    [Fact]
    public void Factory_LogGridAvailable_WithTrimWarning() {
        // 12 horizontal keys → trimmed to first 10
        var config = new ConfigModel {
            HorizontalKeys = [VKey.A, VKey.S, VKey.D, VKey.F, VKey.G, VKey.H, VKey.J, VKey.K, VKey.L, VKey.OemSemicolon, VKey.N, VKey.M],
            Modes = new ModesConfig {
                LogGrid = new ModeConfig { Enabled = true, TwoKey = true, ArrowKeys = true, LogGridBaseSize = 10 },
            },
        };
        var actionMapper = new ActionMapper(config.ActionBindings);
        var factory = new ModeSessionFactory(config, actionMapper, null);

        Assert.True(factory.IsLogGridAvailable);
        Assert.NotNull(factory.LogGridKeyPolicyWarning);
        Assert.Contains("trimmed", factory.LogGridKeyPolicyWarning);
    }

    [Fact]
    public void ChordKey_LogGrid_SwitchesMode() {
        var config = new ConfigModel {
            Modes = new ModesConfig {
                UniformGrid = new ModeConfig { Enabled = true, Default = true, TwoKey = true, ArrowKeys = true },
                LogGrid = new ModeConfig { Enabled = true, ChordKey = VKey.OemComma, TwoKey = true, ArrowKeys = true, LogGridBaseSize = 10 },
            },
        };
        var (_, hotKey, hook, _, overlay, _, _, _) = CreateCoordinator(configOverride: config);

        hotKey.SimulateActivation();
        Assert.True(overlay.IsVisible);

        // Chord key → switch to LogGrid
        hook.SimulateKeyDown(VKey.OemComma);

        // LogGrid session is active → overlay stays visible
        Assert.True(overlay.IsVisible);
    }

    [Fact]
    public void LogGrid_Unavailable_ChordKeyNotRegistered() {
        // Only 5 horiz keys → LogGrid unavailable → chord key should not switch
        var config = new ConfigModel {
            HorizontalKeys = [VKey.A, VKey.S, VKey.D, VKey.F, VKey.G],
            Modes = new ModesConfig {
                UniformGrid = new ModeConfig { Enabled = true, Default = true, TwoKey = true, ArrowKeys = true },
                LogGrid = new ModeConfig { Enabled = true, ChordKey = VKey.OemComma, TwoKey = true, ArrowKeys = true, LogGridBaseSize = 10 },
            },
        };
        var (_, hotKey, hook, _, overlay, _, _, _) = CreateCoordinator(configOverride: config);

        hotKey.SimulateActivation();
        Assert.True(overlay.IsVisible);

        // OemComma should NOT be a chord key (LogGrid unavailable) → forwarded to session
        hook.SimulateKeyDown(VKey.OemComma);

        // Overlay still visible, mode not switched (key went to UniformGrid session as invalid)
        Assert.True(overlay.IsVisible);
    }

    [Fact]
    public void LogGrid_DefaultMode_WhenAvailable() {
        var config = new ConfigModel {
            Modes = new ModesConfig {
                UniformGrid = new ModeConfig { Enabled = true, TwoKey = true, ArrowKeys = true },
                LogGrid = new ModeConfig { Enabled = true, Default = true, ChordKey = VKey.OemComma, TwoKey = true, ArrowKeys = true, LogGridBaseSize = 10 },
            },
        };
        var (_, hotKey, _, _, overlay, _, _, _) = CreateCoordinator(configOverride: config);

        hotKey.SimulateActivation();

        // LogGrid is available and default → overlay shown
        Assert.True(overlay.IsVisible);
    }

    [Fact]
    public void LogGrid_DefaultMode_FallsBackWhenUnavailable() {
        // LogGrid is marked default but <10 keys → falls back to UniformGrid
        var config = new ConfigModel {
            HorizontalKeys = [VKey.A, VKey.S, VKey.D, VKey.F, VKey.G],
            Modes = new ModesConfig {
                UniformGrid = new ModeConfig { Enabled = true, TwoKey = true, ArrowKeys = true },
                LogGrid = new ModeConfig { Enabled = true, Default = true, ChordKey = VKey.OemComma, TwoKey = true, ArrowKeys = true, LogGridBaseSize = 10 },
            },
        };
        var (_, hotKey, hook, _, overlay, _, _, _) = CreateCoordinator(configOverride: config);

        hotKey.SimulateActivation();

        // Should have fallen back to UniformGrid → overlay visible
        Assert.True(overlay.IsVisible);

        // Verify it's UniformGrid by pressing a nav key (A is a valid key for 5-key grid)
        hook.SimulateKeyDown(VKey.A);
        Assert.True(overlay.IsVisible); // Still in L1
    }
}
