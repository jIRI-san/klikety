using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Navigation;
using Klikety.Services;
using Klikety.Tests.Fakes;

using Microsoft.Extensions.Logging.Abstractions;

namespace Klikety.Tests;

internal static class CoordinatorTestHelper {
    internal static (NavigatorCoordinator Coordinator, FakeHotKeyService HotKey, FakeKeyboardHookService Hook,
        FakeMouseActionService Mouse, FakeOverlayWindow Overlay, FakeGridRenderer Renderer,
        FakePlatformServices Platform, FakeModifierDetector ModifierDetector) CreateCoordinator(
        NavigationMode mode = NavigationMode.Both, ConfigModel? configOverride = null,
        FakeMacroStore? macroStore = null, MacrosFile? macrosFile = null) {
        var modeConfig = new ModeConfig {
            Enabled = true,
            Default = true,
            TwoKey = mode is NavigationMode.TwoKey or NavigationMode.Both,
            ArrowKeys = mode is NavigationMode.Arrow or NavigationMode.Both,
        };
        var config = configOverride ?? new ConfigModel {
            Modes = new ModesConfig { UniformGrid = modeConfig },
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
            NullLogger.Instance, macroStore, macrosFile);

        return (coordinator, hotKey, hook, mouse, overlay, renderer, platform, modifierDetector);
    }
}
