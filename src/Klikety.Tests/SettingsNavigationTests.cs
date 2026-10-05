using Klikety.Config;
using Klikety.Input;
using Klikety.Navigation;
using Klikety.Services;
using Klikety.Tests.Fakes;

using Microsoft.Extensions.Logging;

namespace Klikety.Tests;

public sealed class SettingsNavigationTests {
    [Fact]
    public void NonQwertyDefaultAndNonUniformChordsUseUniformGridWithoutChangingConfigOrRepeatingWarning() {
        var config = new ConfigModel {
            Modes = new() {
                UniformGrid = new() { Enabled = true, TwoKey = true, ArrowKeys = true, ChordKey = VKey.OemPlus },
                Crosshair = new() { Enabled = true, Default = true, TwoKey = true, ArrowKeys = true, ChordKey = VKey.N },
            },
        };
        var baseline = SettingsFieldCases.Serialize(config).ToJsonString();
        var hotkey = new FakeHotKeyService();
        var hook = new FakeKeyboardHookService();
        var overlay = new FakeOverlayWindow();
        var renderer = new FakeGridRenderer();
        var platform = new FakePlatformServices();
        platform.KeyboardLayout.Qwerty = false;
        var logger = new RecordingLogger();
        var factory = new ModeSessionFactory(config, new ActionMapper(config.ActionBindings), renderer);
        using var coordinator = new NavigatorCoordinator(hotkey, hook, new FakeMouseActionService(), overlay,
            factory, platform, new FakeModifierDetector(), config, logger);
        hotkey.SimulateActivation();
        Assert.True(overlay.IsVisible);
        var rendered = renderer.Calls.Count(call => call.Method == "RenderGrid");
        Assert.True(rendered > 0);
        hook.SimulateKeyDown(VKey.N);
        hook.SimulateKeyUp(VKey.N);
        hook.SimulateKeyDown(VKey.M);
        Assert.Equal(rendered, renderer.Calls.Count(call => call.Method == "RenderGrid"));
        Assert.True(overlay.IsVisible);
        Assert.Single(logger.Warnings, message => message.Contains("Non-QWERTY", StringComparison.Ordinal));
        Assert.Equal(baseline, SettingsFieldCases.Serialize(config).ToJsonString());
    }

    [Fact]
    public void IdleGuardIncludesNavigationAndMacroPickerEvenWhenOverlayIsHidden() {
        var (coordinator, hotkey, hook, _, overlay, _, _, _) = CoordinatorTestHelper.CreateCoordinator(
            macroStore: new FakeMacroStore(), macrosFile: new MacrosFile());
        using (coordinator) {
            var picker = new FakeMacroPickerWindow();
            coordinator.MacroPickerWindow = picker;
            Assert.True(coordinator.IsIdle);
            hotkey.SimulateActivation();
            Assert.False(coordinator.IsIdle);
            hook.SimulateKeyDown(VKey.OemTilde);
            Assert.True(picker.IsShown);
            Assert.False(overlay.IsVisible);
            Assert.False(coordinator.IsIdle);
            picker.SimulatePickerClosed();
            hook.SimulateKeyDown(VKey.Escape);
            Assert.True(coordinator.IsIdle);
        }
    }

    [Fact]
    public void IdleGateRejectsRecordingWhileOverlayIsSuspendedWithoutSavingMacroData() {
        var store = new FakeMacroStore();
        var (coordinator, hotkey, hook, _, overlay, _, _, _) = CoordinatorTestHelper.CreateCoordinator(
            macroStore: store, macrosFile: new MacrosFile());
        using (coordinator) {
            var gate = new SettingsOperationGate(() => coordinator.IsIdle);
            hotkey.SimulateActivation();
            hook.SimulateKey(VKey.OemPipe);
            hook.SimulateKey(VKey.F1);
            hook.SimulateKey(VKey.A);
            hook.SimulateKey(VKey.W);
            hook.SimulateKey(VKey.Space);
            Assert.False(overlay.IsVisible);
            Assert.False(coordinator.IsIdle);
            Assert.Throws<InvalidOperationException>(() => gate.Enter());
            Assert.Equal(0, store.SaveCount);
            hook.SimulateKey(VKey.Escape);
            Assert.True(coordinator.IsIdle);
            Assert.Equal(0, store.SaveCount);
        }
    }

    [Fact]
    public Task IdleGateRejectsPlaybackWithoutANativeOverlayOrTimingWait() => Task.Run(() => {
        var file = new MacrosFile();
        file.Macros[0] = new MacroDefinition {
            Name = "Fixture", ScreenWidth = 1920, ScreenHeight = 1080, DpiScale = 1,
            Steps = [new MacroStep { ActionType = MacroActionType.LeftClick, X = 100, Y = 100, RelativeTimeMs = 100 }],
        };
        var (coordinator, _, _, _, overlay, _, _, _) = CoordinatorTestHelper.CreateCoordinator(
            macroStore: new FakeMacroStore(), macrosFile: file);
        using (coordinator) {
            var hotkey = new FakeMacroHotKeyService();
            var picker = new FakeMacroPickerWindow();
            var delay = new ControlledDelay();
            coordinator.MacroHotKeyService = hotkey;
            coordinator.MacroPickerWindow = picker;
            coordinator.MacroPlaybackWindow = new FakeMacroPlaybackWindow();
            coordinator.DelayProvider = delay;
            hotkey.SimulateActivated();
            picker.SimulateSlotSelected(0);
            try {
                Assert.True(delay.Entered);
                Assert.False(overlay.IsVisible);
                Assert.False(coordinator.IsIdle);
                Assert.Throws<InvalidOperationException>(() => new SettingsOperationGate(() => coordinator.IsIdle).Enter());
            } finally { delay.Complete(); }
            Assert.True(coordinator.IsIdle);
        }
    });

    private sealed class ControlledDelay : IDelayProvider {
        private readonly TaskCompletionSource _pending = new();
        public bool Entered { get; private set; }
        public Task Delay(int milliseconds, CancellationToken ct) {
            ct.ThrowIfCancellationRequested();
            Entered = true;
            return _pending.Task;
        }
        public void Complete() => _pending.TrySetResult();
    }

    private sealed class RecordingLogger : ILogger {
        public List<string> Warnings { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
            if (level == LogLevel.Warning) { Warnings.Add(formatter(state, exception)); }
        }
    }
}
