using System.Drawing;

using Klikety.Config;
using Klikety.Navigation;
using Klikety.Tests.Fakes;

using Microsoft.Extensions.Logging.Abstractions;

namespace Klikety.Tests;

public class MacroRecorderTests {
    private readonly FakeScreenBoundsProvider _screen = new() {
        Bounds = new Rectangle(0, 0, 1920, 1080),
        DpiScale = 1.5,
    };

    private readonly MacroRecorder _recorder;

    public MacroRecorderTests() {
        _recorder = new MacroRecorder(_screen, NullLogger.Instance);
    }

    private static MacroDefinition?[] EmptySlots() => new MacroDefinition?[10];

    private static MacroDefinition?[] SlotsWithMacroAt(int slot) {
        var macros = new MacroDefinition?[10];
        macros[slot] = new MacroDefinition { Name = $"Existing {slot}", ScreenWidth = 1920, ScreenHeight = 1080, DpiScale = 1.0 };
        return macros;
    }

    [Fact]
    public void StartRecording_TransitionsToAwaitSlot() {
        _recorder.StartRecording();

        Assert.Equal(MacroRecorderState.AwaitSlot, _recorder.State);
    }

    [Fact]
    public void StartRecording_FiresSlotSelectionRequested() {
        var fired = false;
        _recorder.SlotSelectionRequested += () => fired = true;

        _recorder.StartRecording();

        Assert.True(fired);
    }

    [Fact]
    public void StartRecording_WhileNotIdle_Ignored() {
        _recorder.StartRecording();
        Assert.Equal(MacroRecorderState.AwaitSlot, _recorder.State);

        _recorder.StartRecording(); // second call
        Assert.Equal(MacroRecorderState.AwaitSlot, _recorder.State); // unchanged
    }

    [Fact]
    public void OnSlotKey_EmptySlot_TransitionsToRecording() {
        _recorder.StartRecording();

        _recorder.OnSlotKey(3, EmptySlots());

        Assert.Equal(MacroRecorderState.Recording, _recorder.State);
    }

    [Fact]
    public void OnSlotKey_EmptySlot_FiresRecordingStarted() {
        var fired = false;
        _recorder.RecordingStarted += () => fired = true;
        _recorder.StartRecording();

        _recorder.OnSlotKey(0, EmptySlots());

        Assert.True(fired);
    }

    [Fact]
    public void OnSlotKey_OccupiedSlot_TransitionsToAwaitOverwrite() {
        _recorder.StartRecording();

        _recorder.OnSlotKey(2, SlotsWithMacroAt(2));

        Assert.Equal(MacroRecorderState.AwaitOverwrite, _recorder.State);
    }

    [Fact]
    public void OnSlotKey_OccupiedSlot_FiresOverwriteConfirm() {
        int? firedSlot = null;
        string? firedName = null;
        _recorder.OverwriteConfirmRequested += (slot, name) => {
            firedSlot = slot;
            firedName = name;
        };
        _recorder.StartRecording();

        _recorder.OnSlotKey(2, SlotsWithMacroAt(2));

        Assert.Equal(2, firedSlot);
        Assert.Equal("Existing 2", firedName);
    }

    [Fact]
    public void OnOverwriteResponse_Confirmed_TransitionsToRecording() {
        _recorder.StartRecording();
        _recorder.OnSlotKey(5, SlotsWithMacroAt(5));

        _recorder.OnOverwriteResponse(true);

        Assert.Equal(MacroRecorderState.Recording, _recorder.State);
    }

    [Fact]
    public void OnOverwriteResponse_Denied_CancelsRecording() {
        var cancelled = false;
        _recorder.RecordingCancelled += () => cancelled = true;
        _recorder.StartRecording();
        _recorder.OnSlotKey(5, SlotsWithMacroAt(5));

        _recorder.OnOverwriteResponse(false);

        Assert.Equal(MacroRecorderState.Idle, _recorder.State);
        Assert.True(cancelled);
    }

    [Fact]
    public void RecordAction_CapturesStep() {
        _recorder.StartRecording();
        _recorder.OnSlotKey(0, EmptySlots());

        _recorder.RecordAction(new Point(100, 200), MouseAction.LeftClick, ActionModifiers.Shift);

        MacroDefinition? result = null;
        _recorder.RecordingComplete += (_, macro) => result = macro;
        _recorder.StopRecording();

        Assert.NotNull(result);
        Assert.Single(result.Steps);
        Assert.Equal(MacroActionType.LeftClick, result.Steps[0].ActionType);
        Assert.Equal(100, result.Steps[0].X);
        Assert.Equal(200, result.Steps[0].Y);
        Assert.Equal(ActionModifiers.Shift, result.Steps[0].Modifiers);
    }

    [Fact]
    public void RecordAction_FirstStep_HasZeroRelativeTime() {
        _recorder.StartRecording();
        _recorder.OnSlotKey(0, EmptySlots());

        _recorder.RecordAction(new Point(100, 200), MouseAction.LeftClick, ActionModifiers.None);

        MacroDefinition? result = null;
        _recorder.RecordingComplete += (_, macro) => result = macro;
        _recorder.StopRecording();

        // First step should have very small elapsed (< 100ms in test context)
        Assert.NotNull(result);
        Assert.True(result.Steps[0].RelativeTimeMs < 100);
    }

    [Fact]
    public void RecordAction_AllActionTypes_MappedCorrectly() {
        _recorder.StartRecording();
        _recorder.OnSlotKey(0, EmptySlots());

        _recorder.RecordAction(new Point(10, 10), MouseAction.LeftClick, ActionModifiers.None);
        _recorder.RecordAction(new Point(20, 20), MouseAction.RightClick, ActionModifiers.None);
        _recorder.RecordAction(new Point(30, 30), MouseAction.MiddleClick, ActionModifiers.None);
        _recorder.RecordAction(new Point(40, 40), MouseAction.DoubleClick, ActionModifiers.None);
        _recorder.RecordAction(new Point(50, 50), MouseAction.MoveOnly, ActionModifiers.None);

        MacroDefinition? result = null;
        _recorder.RecordingComplete += (_, macro) => result = macro;
        _recorder.StopRecording();

        Assert.NotNull(result);
        Assert.Equal(5, result.Steps.Count);
        Assert.Equal(MacroActionType.LeftClick, result.Steps[0].ActionType);
        Assert.Equal(MacroActionType.RightClick, result.Steps[1].ActionType);
        Assert.Equal(MacroActionType.MiddleClick, result.Steps[2].ActionType);
        Assert.Equal(MacroActionType.DoubleClick, result.Steps[3].ActionType);
        Assert.Equal(MacroActionType.MoveOnly, result.Steps[4].ActionType);
    }

    [Fact]
    public void RecordScroll_CapturesScrollStep() {
        _recorder.StartRecording();
        _recorder.OnSlotKey(0, EmptySlots());

        _recorder.RecordScroll(new Point(300, 400), 120, ActionModifiers.Ctrl);

        MacroDefinition? result = null;
        _recorder.RecordingComplete += (_, macro) => result = macro;
        _recorder.StopRecording();

        Assert.NotNull(result);
        Assert.Single(result.Steps);
        Assert.Equal(MacroActionType.Scroll, result.Steps[0].ActionType);
        Assert.Equal(120, result.Steps[0].ScrollDelta);
        Assert.Equal(ActionModifiers.Ctrl, result.Steps[0].Modifiers);
    }

    [Fact]
    public void DragPairing_DragDrop_ThenButton_CreatesSingleStep() {
        _recorder.StartRecording();
        _recorder.OnSlotKey(0, EmptySlots());

        _recorder.RecordAction(new Point(100, 200), MouseAction.DragDrop, ActionModifiers.Shift);
        _recorder.RecordAction(new Point(500, 600), MouseAction.LeftClick, ActionModifiers.None);

        MacroDefinition? result = null;
        _recorder.RecordingComplete += (_, macro) => result = macro;
        _recorder.StopRecording();

        Assert.NotNull(result);
        Assert.Single(result.Steps);
        var step = result.Steps[0];
        Assert.Equal(MacroActionType.DragDrop, step.ActionType);
        Assert.Equal(100, step.X);
        Assert.Equal(200, step.Y);
        Assert.Equal(500, step.EndX);
        Assert.Equal(600, step.EndY);
        Assert.Equal(MouseAction.LeftClick, step.DragButton);
        Assert.Equal(ActionModifiers.Shift, step.Modifiers);
    }

    [Fact]
    public void DragPairing_DragDrop_ThenInvalidButton_DiscardsPartialDrag() {
        _recorder.StartRecording();
        _recorder.OnSlotKey(0, EmptySlots());

        _recorder.RecordAction(new Point(100, 200), MouseAction.DragDrop, ActionModifiers.None);
        _recorder.RecordAction(new Point(500, 600), MouseAction.MoveOnly, ActionModifiers.None);

        MacroDefinition? result = null;
        _recorder.RecordingComplete += (_, macro) => result = macro;
        _recorder.StopRecording();

        Assert.NotNull(result);
        Assert.Empty(result.Steps);
    }

    [Fact]
    public void DragPairing_CancelDuringPendingDrag_ClearsPendingDrag() {
        var cancelled = false;
        _recorder.RecordingCancelled += () => cancelled = true;
        _recorder.StartRecording();
        _recorder.OnSlotKey(0, EmptySlots());

        _recorder.RecordAction(new Point(100, 200), MouseAction.DragDrop, ActionModifiers.None);
        _recorder.Cancel();

        Assert.True(cancelled);
        Assert.Equal(MacroRecorderState.Idle, _recorder.State);
    }

    [Fact]
    public void StopRecording_WithPendingDrag_DiscardsDrag_KeepsOtherSteps() {
        _recorder.StartRecording();
        _recorder.OnSlotKey(0, EmptySlots());

        _recorder.RecordAction(new Point(10, 20), MouseAction.LeftClick, ActionModifiers.None);
        _recorder.RecordAction(new Point(100, 200), MouseAction.DragDrop, ActionModifiers.None);

        MacroDefinition? result = null;
        _recorder.RecordingComplete += (_, macro) => result = macro;
        _recorder.StopRecording();

        Assert.NotNull(result);
        Assert.Single(result.Steps); // only the LeftClick, pending drag discarded
        Assert.Equal(MacroActionType.LeftClick, result.Steps[0].ActionType);
    }

    [Fact]
    public void StopRecording_CapturesScreenDimensions() {
        _recorder.StartRecording();
        _recorder.OnSlotKey(0, EmptySlots());

        MacroDefinition? result = null;
        _recorder.RecordingComplete += (_, macro) => result = macro;
        _recorder.StopRecording();

        Assert.NotNull(result);
        Assert.Equal(1920, result.ScreenWidth);
        Assert.Equal(1080, result.ScreenHeight);
        Assert.Equal(1.5, result.DpiScale);
    }

    [Fact]
    public void StopRecording_AutoNames_MacroN() {
        _recorder.StartRecording();
        _recorder.OnSlotKey(7, EmptySlots());

        MacroDefinition? result = null;
        _recorder.RecordingComplete += (_, macro) => result = macro;
        _recorder.StopRecording();

        Assert.NotNull(result);
        Assert.Equal("Macro 7", result.Name);
    }

    [Fact]
    public void StopRecording_EmitsCorrectSlot() {
        _recorder.StartRecording();
        _recorder.OnSlotKey(4, EmptySlots());

        int? emittedSlot = null;
        _recorder.RecordingComplete += (slot, _) => emittedSlot = slot;
        _recorder.StopRecording();

        Assert.Equal(4, emittedSlot);
    }

    [Fact]
    public void Cancel_FromAwaitSlot_ReturnsToIdle() {
        _recorder.StartRecording();
        Assert.Equal(MacroRecorderState.AwaitSlot, _recorder.State);

        _recorder.Cancel();

        Assert.Equal(MacroRecorderState.Idle, _recorder.State);
    }

    [Fact]
    public void Cancel_FromAwaitOverwrite_ReturnsToIdle() {
        _recorder.StartRecording();
        _recorder.OnSlotKey(0, SlotsWithMacroAt(0));
        Assert.Equal(MacroRecorderState.AwaitOverwrite, _recorder.State);

        _recorder.Cancel();

        Assert.Equal(MacroRecorderState.Idle, _recorder.State);
    }

    [Fact]
    public void Cancel_FromRecording_ReturnsToIdle_FiresEvent() {
        var cancelled = false;
        _recorder.RecordingCancelled += () => cancelled = true;
        _recorder.StartRecording();
        _recorder.OnSlotKey(0, EmptySlots());
        Assert.Equal(MacroRecorderState.Recording, _recorder.State);

        _recorder.Cancel();

        Assert.Equal(MacroRecorderState.Idle, _recorder.State);
        Assert.True(cancelled);
    }

    [Fact]
    public void Cancel_FromIdle_Ignored() {
        var cancelled = false;
        _recorder.RecordingCancelled += () => cancelled = true;

        _recorder.Cancel();

        Assert.False(cancelled);
    }

    [Fact]
    public void RecordAction_WhileNotRecording_Ignored() {
        // In AwaitSlot state
        _recorder.StartRecording();

        _recorder.RecordAction(new Point(100, 200), MouseAction.LeftClick, ActionModifiers.None);

        // Force into Recording to verify no steps were captured
        _recorder.OnSlotKey(0, EmptySlots());
        MacroDefinition? result = null;
        _recorder.RecordingComplete += (_, macro) => result = macro;
        _recorder.StopRecording();

        Assert.NotNull(result);
        Assert.Empty(result.Steps);
    }

    [Fact]
    public void OnSlotKey_OutOfRange_Ignored() {
        _recorder.StartRecording();

        _recorder.OnSlotKey(-1, EmptySlots());
        Assert.Equal(MacroRecorderState.AwaitSlot, _recorder.State);

        _recorder.OnSlotKey(10, EmptySlots());
        Assert.Equal(MacroRecorderState.AwaitSlot, _recorder.State);
    }

    [Fact]
    public void OnSlotKey_NotInAwaitSlot_Ignored() {
        // Idle state
        _recorder.OnSlotKey(0, EmptySlots());
        Assert.Equal(MacroRecorderState.Idle, _recorder.State);
    }

    [Fact]
    public void MultipleSteps_CapturedInOrder() {
        _recorder.StartRecording();
        _recorder.OnSlotKey(0, EmptySlots());

        _recorder.RecordAction(new Point(10, 20), MouseAction.LeftClick, ActionModifiers.None);
        _recorder.RecordAction(new Point(30, 40), MouseAction.RightClick, ActionModifiers.Ctrl);
        _recorder.RecordScroll(new Point(50, 60), -120, ActionModifiers.None);

        MacroDefinition? result = null;
        _recorder.RecordingComplete += (_, macro) => result = macro;
        _recorder.StopRecording();

        Assert.NotNull(result);
        Assert.Equal(3, result.Steps.Count);
        Assert.Equal(MacroActionType.LeftClick, result.Steps[0].ActionType);
        Assert.Equal(MacroActionType.RightClick, result.Steps[1].ActionType);
        Assert.Equal(MacroActionType.Scroll, result.Steps[2].ActionType);
    }

    // --- Window-relative context tests ---

    private static MacroRecordingContext WindowRelativeContext(int w = 800, int h = 600, string title = "Outlook") =>
        new(IsWindowRelative: true, WindowWidth: w, WindowHeight: h, WindowTitlePattern: title, DpiScale: 1.5);

    [Fact]
    public void StopRecording_WindowRelativeContext_ProducesWindowRelativeMacro() {
        _recorder.StartRecording(WindowRelativeContext());
        _recorder.OnSlotKey(0, EmptySlots());
        _recorder.RecordAction(new Point(100, 200), MouseAction.LeftClick, ActionModifiers.None);

        MacroDefinition? result = null;
        _recorder.RecordingComplete += (_, macro) => result = macro;
        _recorder.StopRecording();

        Assert.NotNull(result);
        Assert.Equal(MacroPositionMode.WindowRelative, result.PositionMode);
        Assert.Equal(800, result.WindowWidth);
        Assert.Equal(600, result.WindowHeight);
        Assert.Equal("Outlook", result.WindowTitlePattern);
        Assert.Equal(0, result.ScreenWidth);
        Assert.Equal(0, result.ScreenHeight);
    }

    [Fact]
    public void StopRecording_NoContext_ProducesAbsoluteMacro() {
        _recorder.StartRecording();
        _recorder.OnSlotKey(0, EmptySlots());
        _recorder.RecordAction(new Point(100, 200), MouseAction.LeftClick, ActionModifiers.None);

        MacroDefinition? result = null;
        _recorder.RecordingComplete += (_, macro) => result = macro;
        _recorder.StopRecording();

        Assert.NotNull(result);
        Assert.Equal(MacroPositionMode.Absolute, result.PositionMode);
        Assert.Equal(1920, result.ScreenWidth);
        Assert.Equal(1080, result.ScreenHeight);
    }

    [Fact]
    public void DragDrop_WindowRelative_FiresStartFromCursorRequested() {
        var fired = false;
        _recorder.StartFromCursorRequested += () => fired = true;

        _recorder.StartRecording(WindowRelativeContext());
        _recorder.OnSlotKey(0, EmptySlots());

        // Drag phase 1 (start) + phase 2 (end with button)
        _recorder.RecordAction(new Point(10, 20), MouseAction.DragDrop, ActionModifiers.None);
        _recorder.RecordAction(new Point(50, 60), MouseAction.LeftClick, ActionModifiers.None);

        Assert.True(fired);
        Assert.Equal(MacroRecorderState.AwaitStartFromCursorConfirm, _recorder.State);
    }

    [Fact]
    public void DragDrop_Absolute_DoesNotFireStartFromCursorRequested() {
        var fired = false;
        _recorder.StartFromCursorRequested += () => fired = true;

        _recorder.StartRecording();
        _recorder.OnSlotKey(0, EmptySlots());

        _recorder.RecordAction(new Point(10, 20), MouseAction.DragDrop, ActionModifiers.None);
        _recorder.RecordAction(new Point(50, 60), MouseAction.LeftClick, ActionModifiers.None);

        Assert.False(fired);
        Assert.Equal(MacroRecorderState.Recording, _recorder.State);
    }

    [Fact]
    public void StartFromCursor_Y_SetsStartFromCursorTrue() {
        _recorder.StartRecording(WindowRelativeContext());
        _recorder.OnSlotKey(0, EmptySlots());
        _recorder.RecordAction(new Point(10, 20), MouseAction.DragDrop, ActionModifiers.None);
        _recorder.RecordAction(new Point(50, 60), MouseAction.LeftClick, ActionModifiers.None);

        _recorder.OnStartFromCursorResponse(true);

        MacroDefinition? result = null;
        _recorder.RecordingComplete += (_, macro) => result = macro;
        _recorder.StopRecording();

        Assert.NotNull(result);
        Assert.Single(result.Steps);
        Assert.True(result.Steps[0].StartFromCursor);
        Assert.Equal(MacroRecorderState.Idle, _recorder.State);
    }

    [Fact]
    public void StartFromCursor_N_SetsStartFromCursorFalse() {
        _recorder.StartRecording(WindowRelativeContext());
        _recorder.OnSlotKey(0, EmptySlots());
        _recorder.RecordAction(new Point(10, 20), MouseAction.DragDrop, ActionModifiers.None);
        _recorder.RecordAction(new Point(50, 60), MouseAction.LeftClick, ActionModifiers.None);

        _recorder.OnStartFromCursorResponse(false);

        MacroDefinition? result = null;
        _recorder.RecordingComplete += (_, macro) => result = macro;
        _recorder.StopRecording();

        Assert.NotNull(result);
        Assert.Single(result.Steps);
        Assert.False(result.Steps[0].StartFromCursor);
    }

    [Fact]
    public void AwaitStartFromCursorConfirm_Escape_CancelsRecording() {
        _recorder.StartRecording(WindowRelativeContext());
        _recorder.OnSlotKey(0, EmptySlots());
        _recorder.RecordAction(new Point(10, 20), MouseAction.DragDrop, ActionModifiers.None);
        _recorder.RecordAction(new Point(50, 60), MouseAction.LeftClick, ActionModifiers.None);

        Assert.Equal(MacroRecorderState.AwaitStartFromCursorConfirm, _recorder.State);

        var cancelled = false;
        _recorder.RecordingCancelled += () => cancelled = true;
        _recorder.Cancel();

        Assert.True(cancelled);
        Assert.Equal(MacroRecorderState.Idle, _recorder.State);
    }

    [Fact]
    public void StopRecording_FromAwaitStartFromCursorConfirm_FinalizesStep() {
        _recorder.StartRecording(WindowRelativeContext());
        _recorder.OnSlotKey(0, EmptySlots());
        _recorder.RecordAction(new Point(10, 20), MouseAction.DragDrop, ActionModifiers.None);
        _recorder.RecordAction(new Point(50, 60), MouseAction.LeftClick, ActionModifiers.None);

        MacroDefinition? result = null;
        _recorder.RecordingComplete += (_, macro) => result = macro;
        _recorder.StopRecording();

        Assert.NotNull(result);
        Assert.Single(result.Steps);
        Assert.False(result.Steps[0].StartFromCursor); // default decline
    }
}
