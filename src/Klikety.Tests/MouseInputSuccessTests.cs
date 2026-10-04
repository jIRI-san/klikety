using System.Drawing;
using System.Runtime.InteropServices;

using Klikety.Config;
using Klikety.Services;
using Klikety.Tests.Fakes;

namespace Klikety.Tests;

public class MouseInputSuccessTests {
    public static IEnumerable<object[]> ClickCases() {
        foreach (var action in new[] { MouseAction.LeftClick, MouseAction.RightClick, MouseAction.MiddleClick, MouseAction.DoubleClick }) {
            for (var modifiers = 0; modifiers < 8; modifiers++) {
                yield return [action, (ActionModifiers)modifiers];
            }
        }
    }

    internal static ushort[] ModifierKeys(ActionModifiers modifiers) =>
        new[] { (ActionModifiers.Shift, (ushort)0x10), (ActionModifiers.Ctrl, (ushort)0x11), (ActionModifiers.Alt, (ushort)0x12) }
            .Where(pair => modifiers.HasFlag(pair.Item1)).Select(pair => pair.Item2).ToArray();

    internal static (uint Down, uint Up) ButtonFlags(MouseAction action) => action switch {
        MouseAction.RightClick => (8, 16),
        MouseAction.MiddleClick => (32, 64),
        _ => (2, 4),
    };

    internal static void AssertModifiers(MouseActionService.INPUT[] batch, ushort[] keys, bool up) {
        Assert.Equal(keys.Length, batch.Length);
        for (var i = 0; i < keys.Length; i++) {
            Assert.Equal(1u, batch[i].type);
            Assert.Equal(keys[i], batch[i].union.ki.wVk);
            Assert.Equal(up ? 2u : 0u, batch[i].union.ki.dwFlags);
        }
    }

    [Theory]
    [MemberData(nameof(ClickCases))]
    public void Click_PreservesMovementAndModifierButtonOrder(MouseAction action, ActionModifiers modifiers) {
        var sender = new FakeInputSender();
        var result = sender.CreateService().SendAction(Point.Empty, action, modifiers);
        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Sends.Count);
        Assert.All(result.Sends, send => Assert.Equal((uint)send.Requested, send.Sent));
        var movement = Assert.Single(sender.Batches[0]);
        Assert.Equal(0xC001u, movement.union.mi.dwFlags);
        Assert.Equal(MouseActionService.NormalizeAbsolute(Point.Empty, new Rectangle(-1920, -1080, 3840, 2160)),
            (movement.union.mi.dx, movement.union.mi.dy));
        var keys = ModifierKeys(modifiers);
        var batch = sender.Batches[1];
        AssertModifiers(batch[..keys.Length], keys, false);
        AssertModifiers(batch[^keys.Length..], keys, true);
        var buttons = batch[keys.Length..(batch.Length - keys.Length)];
        var (down, up) = ButtonFlags(action);
        Assert.Equal(action == MouseAction.DoubleClick ? 4 : 2, buttons.Length);
        for (var i = 0; i < buttons.Length; i++) {
            Assert.Equal(0u, buttons[i].type);
            Assert.Equal(i % 2 == 0 ? down : up, buttons[i].union.mi.dwFlags);
        }
        Assert.Null(result.Cleanup);
    }

    [Theory]
    [InlineData(MouseAction.MoveOnly)]
    [InlineData(MouseAction.DragDrop)]
    public void MovementOnly_SendsNoButtons(MouseAction action) {
        var sender = new FakeInputSender();
        Assert.True(sender.CreateService().SendAction(Point.Empty, action).Succeeded);
        Assert.Single(sender.Batches);
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
    public void Scroll_PreservesWheelDataAndModifierOrder(int modifierValue) {
        var modifiers = (ActionModifiers)modifierValue;
        var sender = new FakeInputSender();
        Assert.True(sender.CreateService().SendScroll(-240, modifiers).Succeeded);
        var batch = Assert.Single(sender.Batches);
        var keys = ModifierKeys(modifiers);
        AssertModifiers(batch[..keys.Length], keys, false);
        AssertModifiers(batch[(keys.Length + 1)..], keys, true);
        Assert.Equal(0x0800u, batch[keys.Length].union.mi.dwFlags);
        Assert.Equal(unchecked((uint)-240), batch[keys.Length].union.mi.mouseData);
    }

    private static readonly int[] expected = new[] { 100, 50 };

    [Theory]
    [MemberData(nameof(ClickCases))]
    public async Task Drag_PreservesThreePhasesGeometryAndDelays(MouseAction button, ActionModifiers modifiers) {
        var sender = new FakeInputSender();
        var delay = new FakeDelayProvider();
        var result = await sender.CreateService(delay).SendDrag(new Point(-1920, -1080), new Point(1919, 1079), button, modifiers);
        Assert.True(result.Succeeded);
        Assert.Equal(expected, delay.RecordedDelays);
        Assert.Equal(new[] { InputStage.DragStart, InputStage.DragNudge, InputStage.DragEnd }, result.Sends.Select(send => send.Stage));
        Assert.Equal(3, sender.Batches.Count);
        var keys = ModifierKeys(modifiers);
        var (down, up) = ButtonFlags(button);
        var first = sender.Batches[0];
        AssertModifiers(first[..keys.Length], keys, false);
        Assert.Equal((0, 0, 0xC001u), (first[^2].union.mi.dx, first[^2].union.mi.dy, first[^2].union.mi.dwFlags));
        Assert.Equal(down, first[^1].union.mi.dwFlags);
        var nudge = Assert.Single(sender.Batches[1]);
        Assert.Equal((65535 / 500, 65535 / 500, 0xC001u), (nudge.union.mi.dx, nudge.union.mi.dy, nudge.union.mi.dwFlags));
        var last = sender.Batches[2];
        Assert.Equal((65535, 65535, 0xC001u), (last[0].union.mi.dx, last[0].union.mi.dy, last[0].union.mi.dwFlags));
        Assert.Equal(up, last[1].union.mi.dwFlags);
        AssertModifiers(last[2..], keys, true);
    }

    [Fact]
    public void ClearModifiers_PreservesAltCtrlShiftReleasesAndNativeLayout() {
        var sender = new FakeInputSender();
        Assert.True(sender.CreateService().ClearStuckModifiers().Succeeded);
        AssertModifiers(Assert.Single(sender.Batches), [0x12, 0x11, 0x10], true);
        Assert.Equal(IntPtr.Size == 8 ? 40 : 28, Marshal.SizeOf<MouseActionService.INPUT>());
        Assert.Equal(IntPtr.Size == 8 ? 8 : 4, Marshal.OffsetOf<MouseActionService.INPUT>("union").ToInt32());
    }
}
