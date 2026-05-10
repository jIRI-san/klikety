using System.Drawing;

using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Navigation;
using Klikety.Services;
using Klikety.Tests.Fakes;

using Microsoft.Extensions.Logging.Abstractions;

namespace Klikety.Tests;

public class MacroRecordingIntegrationTests {
    private static (NavigatorCoordinator Coordinator, FakeHotKeyService HotKey, FakeKeyboardHookService Hook,
        FakeMouseActionService Mouse, FakeOverlayWindow Overlay, FakePlatformServices Platform,
        FakeModifierDetector ModifierDetector, FakeMacroStore MacroStore) CreateMacroCoordinator(
        MacrosConfig? macrosConfig = null) {
        var macros = macrosConfig ?? new MacrosConfig();
        var config = new ConfigModel {
            Modes = new ModesConfig {
                UniformGrid = new ModeConfig { Enabled = true, Default = true, TwoKey = true, ArrowKeys = true },
            },
            Macros = macros,
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
        var macroStore = new FakeMacroStore();
        var macrosFile = new MacrosFile();

        var coordinator = new NavigatorCoordinator(
            hotKey, hook, mouse, overlay, sessionFactory, platform, modifierDetector, config,
            NullLogger.Instance, macroStore, macrosFile);

        return (coordinator, hotKey, hook, mouse, overlay, platform, modifierDetector, macroStore);
    }

    [Fact]
    public void RecordKey_WhenOverlayOpen_ShowsSlotSelection() {
        var (_, hotKey, hook, _, overlay, _, _, _) = CreateMacroCoordinator();

        hotKey.SimulateActivation();
        Assert.True(overlay.IsVisible);

        hook.SimulateKey(VKey.OemPipe); // record key
        Assert.Contains("slot", overlay.StatusText ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SlotKey_EmptySlot_StartsRecording_ShowsBorder() {
        var (_, hotKey, hook, _, overlay, _, _, _) = CreateMacroCoordinator();

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.OemPipe); // record key
        hook.SimulateKey(VKey.D0); // slot 0

        Assert.True(overlay.RecordingBorderVisible);
    }

    [Fact]
    public void RecordKey_StopsRecording_SavesMacro() {
        var (_, hotKey, hook, mouse, overlay, _, _, store) = CreateMacroCoordinator();

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.OemPipe); // start
        hook.SimulateKey(VKey.D0); // slot 0
        Assert.True(overlay.RecordingBorderVisible);

        // Navigate and action: A, W, Space = L1 navigation
        hook.SimulateKey(VKey.A);
        hook.SimulateKey(VKey.W);
        hook.SimulateKey(VKey.Space);

        // After action fires, overlay was suspended. Simulate resume timer.
        // The action should be recorded and saved.
        // Press record key to stop
        hook.SimulateKey(VKey.OemPipe);

        Assert.False(overlay.RecordingBorderVisible);
        Assert.Equal(1, store.SaveCount);
        Assert.NotNull(store.LastSavedFile.Macros[0]);
        Assert.Equal("Macro 0", store.LastSavedFile.Macros[0]!.Name);
    }

    [Fact]
    public void EscapeDuringRecording_CancelsRecording() {
        var (_, hotKey, hook, _, overlay, _, _, store) = CreateMacroCoordinator();

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.OemPipe); // start
        hook.SimulateKey(VKey.D5); // slot 5

        Assert.True(overlay.RecordingBorderVisible);

        hook.SimulateKey(VKey.Escape);

        Assert.False(overlay.RecordingBorderVisible);
        Assert.Equal(0, store.SaveCount); // not saved
    }

    [Fact]
    public void EscapeDuringSlotSelection_CancelsRecording() {
        var (_, hotKey, hook, _, overlay, _, _, _) = CreateMacroCoordinator();

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.OemPipe); // start
        Assert.Contains("slot", overlay.StatusText ?? "", StringComparison.OrdinalIgnoreCase);

        hook.SimulateKey(VKey.Escape);

        Assert.Null(overlay.StatusText);
        Assert.False(overlay.RecordingBorderVisible);
    }

    [Fact]
    public void OverwriteConfirm_OccupiedSlot_ShowsPrompt() {
        var macroStore = new FakeMacroStore();
        var macros = new MacrosConfig();
        var config = new ConfigModel {
            Modes = new ModesConfig {
                UniformGrid = new ModeConfig { Enabled = true, Default = true, TwoKey = true, ArrowKeys = true },
            },
            Macros = macros,
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

        // Pre-populate slot 0
        var macrosFile = new MacrosFile();
        macrosFile.Macros[0] = new MacroDefinition { Name = "Existing Macro", ScreenWidth = 1920, ScreenHeight = 1080, DpiScale = 1.0 };

        var coordinator = new NavigatorCoordinator(
            hotKey, hook, mouse, overlay, sessionFactory, platform, modifierDetector, config,
            NullLogger.Instance, macroStore, macrosFile);

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.OemPipe); // start
        hook.SimulateKey(VKey.D0); // slot 0 (occupied)

        Assert.Contains("Overwrite", overlay.StatusText ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OverwriteDenied_CancelsRecording() {
        var macroStore = new FakeMacroStore();
        var macros = new MacrosConfig();
        var config = new ConfigModel {
            Modes = new ModesConfig {
                UniformGrid = new ModeConfig { Enabled = true, Default = true, TwoKey = true, ArrowKeys = true },
            },
            Macros = macros,
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

        var macrosFile = new MacrosFile();
        macrosFile.Macros[0] = new MacroDefinition { Name = "Existing", ScreenWidth = 1920, ScreenHeight = 1080, DpiScale = 1.0 };

        var coordinator = new NavigatorCoordinator(
            hotKey, hook, mouse, overlay, sessionFactory, platform, modifierDetector, config,
            NullLogger.Instance, macroStore, macrosFile);

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.OemPipe);
        hook.SimulateKey(VKey.D0);
        hook.SimulateKey(VKey.N); // deny

        Assert.False(overlay.RecordingBorderVisible);
        Assert.Equal(0, macroStore.SaveCount);
    }

    [Fact]
    public void OverwriteConfirmed_StartsRecording() {
        var macroStore = new FakeMacroStore();
        var macros = new MacrosConfig();
        var config = new ConfigModel {
            Modes = new ModesConfig {
                UniformGrid = new ModeConfig { Enabled = true, Default = true, TwoKey = true, ArrowKeys = true },
            },
            Macros = macros,
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

        var macrosFile = new MacrosFile();
        macrosFile.Macros[3] = new MacroDefinition { Name = "Old", ScreenWidth = 1920, ScreenHeight = 1080, DpiScale = 1.0 };

        var coordinator = new NavigatorCoordinator(
            hotKey, hook, mouse, overlay, sessionFactory, platform, modifierDetector, config,
            NullLogger.Instance, macroStore, macrosFile);

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.OemPipe);
        hook.SimulateKey(VKey.D3);
        hook.SimulateKey(VKey.Y); // confirm

        Assert.True(overlay.RecordingBorderVisible);
    }

    [Fact]
    public void FocusLoss_DuringRecording_DoesNotDeactivateOverlay() {
        var (_, hotKey, hook, _, overlay, _, _, _) = CreateMacroCoordinator();

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.OemPipe);
        hook.SimulateKey(VKey.D0);

        Assert.True(overlay.RecordingBorderVisible);

        overlay.SimulateFocusLoss();

        // Overlay should still be visible (focus loss suppressed during recording)
        Assert.True(overlay.IsVisible);
    }

    [Fact]
    public void MacroState_PreventsDoubleRecording() {
        var (_, hotKey, hook, _, overlay, _, _, _) = CreateMacroCoordinator();

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.OemPipe); // start recording
        hook.SimulateKey(VKey.D0); // slot 0
        Assert.True(overlay.RecordingBorderVisible);

        // Second record key stops recording (not starts new one)
        hook.SimulateKey(VKey.OemPipe);
        Assert.False(overlay.RecordingBorderVisible);
    }

    [Fact]
    public void RecordKey_WithoutOverlay_Ignored() {
        var (_, _, hook, _, overlay, _, _, _) = CreateMacroCoordinator();

        // Don't activate hotkey (no overlay)
        hook.SimulateKey(VKey.OemPipe);

        Assert.False(overlay.IsVisible);
        Assert.False(overlay.RecordingBorderVisible);
    }

    [Fact]
    public void SaveFailure_RetainsMacroInMemory() {
        var (_, hotKey, hook, _, _, _, _, store) = CreateMacroCoordinator();
        store.ShouldFailSave = true;

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.OemPipe);
        hook.SimulateKey(VKey.D0);

        // Just stop immediately (empty macro is fine for this test)
        hook.SimulateKey(VKey.OemPipe);

        Assert.Equal(1, store.SaveCount);
        // Macro retained in-memory despite save failure
        Assert.NotNull(store.LastSavedFile.Macros[0]);
    }

    [Fact]
    public void ModifierKeys_CapturedDuringRecording() {
        var (_, hotKey, hook, mouse, _, _, modifierDetector, store) = CreateMacroCoordinator();
        modifierDetector.Modifiers = ActionModifiers.Ctrl;

        hotKey.SimulateActivation();
        hook.SimulateKey(VKey.OemPipe); // start
        hook.SimulateKey(VKey.D0); // slot 0

        // Navigate and action
        hook.SimulateKey(VKey.A);
        hook.SimulateKey(VKey.W);
        hook.SimulateKey(VKey.Space);

        // Stop recording
        hook.SimulateKey(VKey.OemPipe);

        Assert.Equal(1, store.SaveCount);
        var macro = store.LastSavedFile.Macros[0];
        Assert.NotNull(macro);
        Assert.True(macro.Steps.Count > 0);
        Assert.Equal(ActionModifiers.Ctrl, macro.Steps[0].Modifiers);
    }

    [Fact]
    public void AppScoped_Recording_ProducesWindowRelativeMacro() {
        var (_, hotKey, hook, mouse, overlay, platform, _, store) = CreateMacroCoordinator();
        platform.ForegroundWindow.Handle = 0x1234;
        platform.ForegroundWindow.Bounds = new Rectangle(100, 100, 800, 600);
        platform.ForegroundWindow.Title = "Inbox - user@example.com - Outlook";

        // Activate overlay + enter app-scope
        hotKey.SimulateActivation();
        hook.SimulateKeyDown(VKey.OemPeriod); // app-scope chord

        // Start recording
        hook.SimulateKey(VKey.OemPipe);
        hook.SimulateKey(VKey.D0);

        // Navigate and action
        hook.SimulateKey(VKey.A);
        hook.SimulateKey(VKey.W);
        hook.SimulateKey(VKey.Space);

        // Stop recording
        hook.SimulateKey(VKey.OemPipe);

        Assert.Equal(1, store.SaveCount);
        var macro = store.LastSavedFile.Macros[0];
        Assert.NotNull(macro);
        Assert.Equal(MacroPositionMode.WindowRelative, macro.PositionMode);
        Assert.Equal(800, macro.WindowWidth);
        Assert.Equal(600, macro.WindowHeight);
        Assert.Equal("Outlook", macro.WindowTitlePattern);
    }

    [Fact]
    public void AppScoped_Recording_EmptyTitle_Rejected() {
        var (_, hotKey, hook, _, overlay, platform, _, store) = CreateMacroCoordinator();
        platform.ForegroundWindow.Handle = 0x1234;
        platform.ForegroundWindow.Bounds = new Rectangle(100, 100, 800, 600);
        platform.ForegroundWindow.Title = "";

        hotKey.SimulateActivation();
        hook.SimulateKeyDown(VKey.OemPeriod);

        hook.SimulateKey(VKey.OemPipe);

        // Status text should show error
        Assert.Contains("no title", overlay.StatusText ?? "", StringComparison.OrdinalIgnoreCase);
        // Recording should not have started
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public void AppScoped_Recording_CoordinatesAreWindowRelative() {
        var (_, hotKey, hook, mouse, overlay, platform, _, store) = CreateMacroCoordinator();
        platform.ForegroundWindow.Handle = 0x1234;
        platform.ForegroundWindow.Bounds = new Rectangle(100, 50, 800, 600);
        platform.ForegroundWindow.Title = "TestApp";

        hotKey.SimulateActivation();
        hook.SimulateKeyDown(VKey.OemPeriod);

        hook.SimulateKey(VKey.OemPipe);
        hook.SimulateKey(VKey.D0);

        // Navigate and action
        hook.SimulateKey(VKey.A);
        hook.SimulateKey(VKey.W);
        hook.SimulateKey(VKey.Space);

        hook.SimulateKey(VKey.OemPipe);

        var macro = store.LastSavedFile.Macros[0];
        Assert.NotNull(macro);
        // Coordinates should be window-relative (offset from window top-left at 100,50)
        // Exact values depend on grid calculation, but they should be less than window dimensions
        Assert.True(macro.Steps[0].X < 800, $"X={macro.Steps[0].X} should be < WindowWidth=800");
        Assert.True(macro.Steps[0].Y < 600, $"Y={macro.Steps[0].Y} should be < WindowHeight=600");
    }

    [Fact]
    public void FullScreen_Recording_ProducesAbsoluteMacro() {
        var (_, hotKey, hook, mouse, _, _, _, store) = CreateMacroCoordinator();

        hotKey.SimulateActivation();

        // Start recording (not in app-scope)
        hook.SimulateKey(VKey.OemPipe);
        hook.SimulateKey(VKey.D0);

        hook.SimulateKey(VKey.A);
        hook.SimulateKey(VKey.W);
        hook.SimulateKey(VKey.Space);

        hook.SimulateKey(VKey.OemPipe);

        Assert.Equal(1, store.SaveCount);
        var macro = store.LastSavedFile.Macros[0];
        Assert.NotNull(macro);
        Assert.Equal(MacroPositionMode.Absolute, macro.PositionMode);
    }
}
