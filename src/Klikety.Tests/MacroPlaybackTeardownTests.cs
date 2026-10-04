using Klikety.Config;
using Klikety.Navigation;
using Klikety.Services;
using Klikety.Tests.Fakes;

using Microsoft.Extensions.Logging.Abstractions;

namespace Klikety.Tests;

public class MacroPlaybackTeardownTests {
    internal static MacroHandler CreateHandler(FakeMouseActionService mouse, IMacroPlaybackWindow window, IClickIndicator indicator) {
        var config = new ConfigModel();
        var factory = new ModeSessionFactory(config, new ActionMapper(config.ActionBindings), new FakeGridRenderer());
        var sessions = new SessionManager(factory, new FakeOverlayWindow(), NullLogger.Instance);
        var file = new MacrosFile();
        file.Macros[0] = MacroCancellationTests.Macro(MacroActionType.LeftClick);
        var handler = new MacroHandler(config, new FakePlatformServices(), new FakeKeyboardHookService(), mouse,
            sessions, NullLogger.Instance, new FakeMacroStore(), file) {
            MacroPlaybackWindow = window,
            ClickIndicator = indicator,
            DelayProvider = new FakeDelayProvider(),
            MacroHotKeyService = new FakeMacroHotKeyService(),
            MacroPickerWindow = new FakeMacroPickerWindow(),
        };
        return handler;
    }

    internal static void Start(MacroHandler handler) {
        ((FakeMacroHotKeyService)handler.MacroHotKeyService!).SimulateActivated();
        ((FakeMacroPickerWindow)handler.MacroPickerWindow!).SimulateSlotSelected(0);
    }

    private sealed class DeferredIndicator : IClickIndicator {
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public bool Disposed { get; private set; }
        public Task ShowAndWait(double screenX, double screenY, CancellationToken ct) {
            Token = ct;
            return Completion.Task;
        }
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public async Task Dispose_DoesNotDrain_OperationEventuallyReleasesState() {
        var indicator = new DeferredIndicator();
        var window = new FakeMacroPlaybackWindow();
        var mouse = new FakeMouseActionService();
        var handler = CreateHandler(mouse, window, indicator);
        Start(handler);
        var task = handler.PlaybackTask!;
        Assert.False(task.IsCompleted);

        handler.Dispose();

        Assert.True(indicator.Token.IsCancellationRequested);
        Assert.True(indicator.Disposed);
        Assert.False(task.IsCompleted);
        Assert.False(window.IsShown);
        indicator.Completion.SetResult();
        await task;
        Assert.Null(handler.PlaybackTask);
        Assert.Empty(mouse.Calls);
        handler.Dispose();
    }

    [Fact]
    public async Task Reload_LateOldCompletionCannotCloseReplacementProgress() {
        var window = new FakeMacroPlaybackWindow();
        var oldIndicator = new DeferredIndicator();
        var oldMouse = new FakeMouseActionService();
        var oldHandler = CreateHandler(oldMouse, window, oldIndicator);
        Start(oldHandler);
        var oldTask = oldHandler.PlaybackTask!;
        oldHandler.Dispose();

        var nextIndicator = new DeferredIndicator();
        var nextHandler = CreateHandler(new FakeMouseActionService(), window, nextIndicator);
        Start(nextHandler);
        var nextTask = nextHandler.PlaybackTask!;
        oldIndicator.Completion.SetResult();
        await oldTask;
        Assert.True(window.IsShown);
        Assert.Equal(0, window.LastCompletedSteps);
        Assert.Equal(MacroState.Playing, nextHandler.State);
        Assert.Empty(oldMouse.Calls);

        nextIndicator.Completion.SetResult();
        await nextTask;
        Assert.Equal(MacroState.Idle, nextHandler.State);
        Assert.False(window.IsShown);
        nextHandler.Dispose();
    }
}
