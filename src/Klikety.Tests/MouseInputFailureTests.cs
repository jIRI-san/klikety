using System.Drawing;

using Klikety.Config;
using Klikety.Services;
using Klikety.Tests.Fakes;

using Microsoft.Extensions.Logging;

namespace Klikety.Tests;

public class MouseInputFailureTests {
    [Fact]
    public void FailedMove_SuppressesDependentClick_ReportsMissingNativeError() {
        var sender = new FakeInputSender();
        sender.Results[1] = new(0, null);
        var result = sender.CreateService().SendAction(Point.Empty, MouseAction.LeftClick, ActionModifiers.Ctrl);
        Assert.False(result.Succeeded);
        Assert.Equal(new InputSendOutcome(InputStage.Move, 1, 0), result.Failure);
        Assert.Single(sender.Batches);
        Assert.Null(result.Cleanup);
        Assert.Contains("unavailable", result.ToString());
    }

    [Theory]
    [MemberData(nameof(MouseInputSuccessTests.ClickCases), MemberType = typeof(MouseInputSuccessTests))]
    public void Click_AllIncompletePrefixes_ReleaseOnlySentHeldInputsOnce(MouseAction action, ActionModifiers modifiers) {
        var keys = MouseInputSuccessTests.ModifierKeys(modifiers);
        var clickCount = action == MouseAction.DoubleClick ? 4 : 2;
        var requested = keys.Length * 2 + clickCount;
        for (uint sent = 0; sent < requested; sent++) {
            var sender = new FakeInputSender();
            sender.Results[2] = new(sent, 5);
            var result = sender.CreateService().SendAction(Point.Empty, action, modifiers);
            Assert.False(result.Succeeded);
            Assert.Equal(new InputSendOutcome(InputStage.Click, requested, sent, 5), result.Failure);
            var buttonSent = Math.Clamp((int)sent - keys.Length, 0, clickCount);
            var releasedKeys = Math.Max(0, (int)sent - keys.Length - clickCount);
            var heldKeys = keys.Take(Math.Min((int)sent, keys.Length)).Skip(releasedKeys).ToArray();
            var buttonHeld = buttonSent % 2 != 0;
            AssertCleanup(sender, result, 2, buttonHeld ? MouseInputSuccessTests.ButtonFlags(action).Up : null, heldKeys);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void Scroll_AllIncompletePrefixes_ReleaseOnlyHeldModifiers(int modifierValue) {
        var modifiers = (ActionModifiers)modifierValue;
        var keys = MouseInputSuccessTests.ModifierKeys(modifiers);
        var requested = keys.Length * 2 + 1;
        for (uint sent = 0; sent < requested; sent++) {
            var sender = new FakeInputSender();
            sender.Results[1] = new(sent, 87);
            var result = sender.CreateService().SendScroll(120, modifiers);
            Assert.Equal(new InputSendOutcome(InputStage.Scroll, requested, sent, 87), result.Failure);
            var released = Math.Max(0, (int)sent - keys.Length - 1);
            AssertCleanup(sender, result, 1, null, keys.Take(Math.Min((int)sent, keys.Length)).Skip(released).ToArray());
        }
    }

    [Theory]
    [MemberData(nameof(MouseInputSuccessTests.ClickCases), MemberType = typeof(MouseInputSuccessTests))]
    public async Task Drag_AllPhasePrefixes_StopAndReleaseHeldInputs(MouseAction button, ActionModifiers modifiers) {
        var keys = MouseInputSuccessTests.ModifierKeys(modifiers);
        var up = MouseInputSuccessTests.ButtonFlags(button).Up;
        for (var phase = 1; phase <= 3; phase++) {
            var requested = phase == 2 ? 1 : keys.Length + 2;
            for (uint sent = 0; sent < requested; sent++) {
                var sender = new FakeInputSender();
                var delay = new FakeDelayProvider();
                sender.Results[phase] = new(sent, 5);
                var result = await sender.CreateService(delay).SendDrag(Point.Empty, new Point(100, 100), button, modifiers);
                Assert.False(result.Succeeded);
                Assert.Equal(new InputSendOutcome(phase switch {
                    1 => InputStage.DragStart,
                    2 => InputStage.DragNudge,
                    _ => InputStage.DragEnd,
                }, requested, sent, 5), result.Failure);
                var buttonHeld = phase == 2 || (phase == 3 && sent < 2);
                var heldKeys = phase == 1 ? keys.Take((int)sent).ToArray()
                    : keys.Skip(phase == 3 ? Math.Max(0, (int)sent - 2) : 0).ToArray();
                AssertCleanup(sender, result, phase, buttonHeld ? up : null, heldKeys);
                Assert.Equal(phase - 1, delay.RecordedDelays.Count);
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ClearModifiers_IncompleteRelease_IsExplicitAndNotRetried(uint sent) {
        var sender = new FakeInputSender();
        sender.Results[1] = new(sent, 5);
        var result = sender.CreateService().ClearStuckModifiers();
        Assert.Equal(new InputSendOutcome(InputStage.ClearModifiers, 3, sent, 5), result.Failure);
        Assert.Null(result.Cleanup);
        Assert.Single(sender.Batches);
    }

    [Fact]
    public void FailedCleanup_RetainsPrimaryAndCleanupDiagnostics_LogsOnceWithoutRetry() {
        var sender = new FakeInputSender();
        var logger = new CapturingLogger();
        sender.Results[2] = new(1, 5);
        sender.Results[3] = new(0, 87);
        var result = sender.CreateService(logger: logger).SendAction(Point.Empty, MouseAction.LeftClick);
        Assert.Equal(new InputSendOutcome(InputStage.Click, 2, 1, 5), result.Failure);
        Assert.Equal(new InputSendOutcome(InputStage.ReleaseCleanup, 1, 0, 87), result.Cleanup);
        Assert.Equal(3, sender.Batches.Count);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("release cleanup failed", entry.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScrollAndDrag_FailedCleanupIsReturnedAndNotRetried(bool drag) {
        var sender = new FakeInputSender();
        var logger = new CapturingLogger();
        sender.Results[1] = new(1, 5);
        sender.Results[2] = new(0, 87);
        var service = sender.CreateService(logger: logger);
        var result = drag
            ? await service.SendDrag(Point.Empty, new Point(100, 100), MouseAction.LeftClick, ActionModifiers.Ctrl)
            : service.SendScroll(120, ActionModifiers.Ctrl);
        Assert.False(result.Succeeded);
        Assert.Equal(5, result.Failure!.NativeError);
        Assert.Equal(new InputSendOutcome(InputStage.ReleaseCleanup, 1, 0, 87), result.Cleanup);
        Assert.Equal(2, sender.Batches.Count);
        Assert.Single(logger.Entries);
    }

    private static void AssertCleanup(FakeInputSender sender, InputResult result, int primaryCalls, uint? buttonUp, ushort[] heldKeys) {
        if (buttonUp is null && heldKeys.Length == 0) {
            Assert.Equal(primaryCalls, sender.Batches.Count);
            Assert.Null(result.Cleanup);
            return;
        }
        Assert.Equal(primaryCalls + 1, sender.Batches.Count);
        var cleanup = sender.Batches[^1];
        Assert.True(result.Cleanup!.Succeeded);
        Assert.Equal(heldKeys.Length + (buttonUp.HasValue ? 1 : 0), cleanup.Length);
        var keyOffset = 0;
        if (buttonUp.HasValue) {
            Assert.Equal(0u, cleanup[0].type);
            Assert.Equal(buttonUp.Value, cleanup[0].union.mi.dwFlags);
            keyOffset = 1;
        }
        MouseInputSuccessTests.AssertModifiers(cleanup[keyOffset..], heldKeys, true);
    }
}
