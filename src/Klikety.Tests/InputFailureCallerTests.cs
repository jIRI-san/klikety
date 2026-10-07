using System.Drawing;

using Klikety.Config;
using Klikety.Input;
using Klikety.Navigation;
using Klikety.Services;
using Klikety.Tests.Fakes;

using Microsoft.Extensions.Logging;

namespace Klikety.Tests;

public class InputFailureCallerTests {
    private static (ActionDispatcher Dispatcher, SessionManager Sessions, FakeOverlayWindow Overlay,
        FakeMouseActionService Mouse, CapturingLogger Logger) CreateDispatcher() {
        var config = new ConfigModel();
        var overlay = new FakeOverlayWindow();
        var logger = new CapturingLogger();
        var factory = new ModeSessionFactory(config, new ActionMapper(config.ActionBindings), new FakeGridRenderer());
        var sessions = new SessionManager(factory, overlay, logger) { ScreenBounds = new Rectangle(0, 0, 1920, 1080) };
        var mouse = new FakeMouseActionService { Result = new([new(InputStage.Click, 2, 0, 5)]) };
        var dispatcher = new ActionDispatcher(mouse, new FakeModifierDetector(), overlay, sessions, overlay.Hide, logger);
        return (dispatcher, sessions, overlay, mouse, logger);
    }

    [Fact]
    public void NormalClickAndCursorRestore_ObserveFailuresWithoutChangingDeactivation() {
        var (dispatcher, _, overlay, mouse, logger) = CreateDispatcher();
        overlay.Show();
        Assert.True(dispatcher.HandleAction(new Point(10, 20), MouseAction.LeftClick));
        Assert.False(overlay.IsVisible);
        dispatcher.HandleAction(new Point(-1, -1), MouseAction.LeftClick);
        Assert.Equal(2, logger.Entries.Count(entry => entry.Level == LogLevel.Error));
        Assert.Equal(2, mouse.Calls.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DragResultAndFault_AreObservedWithoutUnobservedTasks(bool throws) {
        var (dispatcher, _, overlay, mouse, logger) = CreateDispatcher();
        var completion = new TaskCompletionSource<InputResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        mouse.DragResult = () => completion.Task;
        Assert.False(dispatcher.HandleAction(new Point(10, 20), MouseAction.DragDrop));
        Assert.True(dispatcher.HandleAction(new Point(30, 40), MouseAction.RightClick));
        Assert.False(overlay.IsVisible);
        Assert.False(dispatcher.PendingDrag.IsCompleted);
        if (throws) {
            completion.SetException(new InvalidOperationException("transport fault"));
        } else {
            completion.SetResult(mouse.Result);
        }
        await dispatcher.PendingDrag;
        Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public void RecordingFailure_PreservesAttemptedStepAndResumePolicy() {
        var (dispatcher, _, _, _, logger) = CreateDispatcher();
        var recorder = new MacroRecorder(new FakeScreenBoundsProvider(), logger);
        recorder.StartRecording();
        recorder.OnSlotKey(0, new MacroDefinition?[10]);
        MacroDefinition? saved = null;
        recorder.RecordingComplete += (_, macro) => saved = macro;
        var result = dispatcher.HandleRecordingAction(new Point(10, 20), MouseAction.LeftClick, recorder, false, Rectangle.Empty);
        Assert.Equal(RecordingActionResult.SuspendAndResume, result);
        recorder.StopRecording();
        var step = Assert.Single(saved!.Steps);
        Assert.Equal(MacroActionType.LeftClick, step.ActionType);
        Assert.Equal((10, 20), (step.X, step.Y));
        Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecordingDragFailure_PreservesPairAndExistingPromptPolicy(bool relative) {
        var (dispatcher, _, _, _, logger) = CreateDispatcher();
        var recorder = new MacroRecorder(new FakeScreenBoundsProvider(), logger);
        recorder.StartRecording(relative ? new MacroRecordingContext(true, 1920, 1080, "Test", 1) : null);
        recorder.OnSlotKey(0, new MacroDefinition?[10]);
        MacroDefinition? saved = null;
        recorder.RecordingComplete += (_, macro) => saved = macro;
        Assert.Equal(RecordingActionResult.ResetForDrag,
            dispatcher.HandleRecordingAction(new Point(10, 20), MouseAction.DragDrop, recorder, relative, Rectangle.Empty));
        Assert.Equal(relative ? RecordingActionResult.Consumed : RecordingActionResult.SuspendAndResume,
            dispatcher.HandleRecordingAction(new Point(30, 40), MouseAction.RightClick, recorder, relative, Rectangle.Empty));
        await dispatcher.PendingDrag;
        recorder.StopRecording();
        var step = Assert.Single(saved!.Steps);
        Assert.Equal(MacroActionType.DragDrop, step.ActionType);
        Assert.Equal(MouseAction.RightClick, step.DragButton);
        Assert.Equal((10, 20, 30, 40), (step.X, step.Y, step.EndX, step.EndY));
        Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public void Coordinator_CursorMoveAndClearModifiers_LogFailures() {
        var logger = new CapturingLogger();
        var (coordinator, hotKey, hook, mouse, _, _, _, _) = CoordinatorTestHelper.CreateCoordinator(logger: logger);
        using (coordinator) {
            mouse.Result = new([new(InputStage.Move, 1, 0)]);
            hotKey.SimulateActivation();
            hook.SimulateKeyDown(VKey.A);
            hook.SimulateKeyDown(VKey.W);
            Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Message.Contains("Move: 0/1"));
            var before = logger.Entries.Count(entry => entry.Level == LogLevel.Error);
            mouse.Result = new([new(InputStage.ClearModifiers, 3, 0, 5)]);
            coordinator.DeactivateOverlay();
            Assert.Equal(before + 1, logger.Entries.Count(entry => entry.Level == LogLevel.Error));
            Assert.Contains(logger.Entries, entry => entry.Message.Contains("ClearModifiers: 0/3"));
        }
    }

    [Fact]
    public void ScrollHotkey_ObservesBothDirectionsWithoutNativeRegistration() {
        var logger = new CapturingLogger();
        var mouse = new FakeMouseActionService { Result = new([new(InputStage.Scroll, 1, 0)]) };
        using var service = new ScrollHotKeyService(new ScrollHotKeyConfig(), mouse, logger);
        service.DispatchScroll(120);
        service.DispatchScroll(-120);
        Assert.Equal(new[] { 120, -120 }, mouse.ScrollCalls.Select(call => call.WheelDelta));
        Assert.Equal(2, logger.Entries.Count(entry => entry.Level == LogLevel.Error));
    }
}
