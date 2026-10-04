using System.Drawing;
using System.Runtime.InteropServices;

using Klikety.Config;
using Klikety.Interop;

using Microsoft.Extensions.Logging;

namespace Klikety.Services;

/// <summary>
/// Moves the mouse cursor and sends click actions via Win32 SendInput.
/// All coordinates are physical pixels; normalized to 0–65535 range for MOUSEEVENTF_ABSOLUTE.
/// </summary>
public sealed partial class MouseActionService : IMouseActionService {
    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_MENU = 0x12;

    private readonly ILogger _logger;
    private readonly IInputSender _sender;
    private readonly Func<Rectangle> _virtualScreen;
    private readonly IDelayProvider _delay;

    internal readonly record struct NativeSendResult(uint Sent, int? Error);

    internal interface IInputSender {
        NativeSendResult Send(INPUT[] inputs);
    }

    private sealed class Win32InputSender : IInputSender {
        public NativeSendResult Send(INPUT[] inputs) {
            var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            var error = Marshal.GetLastPInvokeError();
            return new NativeSendResult(sent, sent != inputs.Length && error != 0 ? error : null);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct INPUT_UNION {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT {
        public uint type;
        public INPUT_UNION union;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public MouseActionService(ILogger logger)
        : this(logger, new Win32InputSender(), NativeMethods.GetVirtualScreenBounds, new TaskDelayProvider()) { }

    internal MouseActionService(ILogger logger, IInputSender sender, Func<Rectangle> virtualScreen, IDelayProvider delay) {
        _logger = logger;
        _sender = sender;
        _virtualScreen = virtualScreen;
        _delay = delay;
    }

    public InputResult MoveTo(Point physicalPoint) {
        var (nx, ny) = NormalizePoint(physicalPoint);

        var input = new INPUT {
            type = INPUT_MOUSE,
            union = new INPUT_UNION {
                mi = new MOUSEINPUT {
                    dx = nx,
                    dy = ny,
                    dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK,
                }
            },
        };

        return SendBatch([input], InputStage.Move, []);
    }

    private (int X, int Y) NormalizePoint(Point physicalPoint) =>
        NormalizeAbsolute(physicalPoint, _virtualScreen());

    /// <summary>
    /// Maps a physical pixel through virtual-desktop metrics to the 0–65535 SendInput range.
    /// </summary>
    internal static (int X, int Y) NormalizeAbsolute(Point physicalPoint, Rectangle virtualScreen) {
        int divisorX = Math.Max(virtualScreen.Width - 1, 1);
        int divisorY = Math.Max(virtualScreen.Height - 1, 1);
        int normalizedX = (int)((physicalPoint.X - virtualScreen.X) * 65535.0 / divisorX);
        int normalizedY = (int)((physicalPoint.Y - virtualScreen.Y) * 65535.0 / divisorY);
        return (normalizedX, normalizedY);
    }

    public InputResult SendAction(Point physicalPoint, MouseAction action, ActionModifiers modifiers = ActionModifiers.None) {
        var movement = MoveTo(physicalPoint);

        if (!movement.Succeeded || action is MouseAction.MoveOnly or MouseAction.DragDrop) {
            return movement;
        }

        var (downFlag, upFlag) = action switch {
            MouseAction.LeftClick or MouseAction.DoubleClick => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP),
            MouseAction.RightClick => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP),
            MouseAction.MiddleClick => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

        int clickCount = action == MouseAction.DoubleClick ? 2 : 1;

        var inputs = new List<INPUT>();
        inputs.AddRange(BuildModifierInputs(modifiers, keyUp: false));
        for (int i = 0; i < clickCount; i++) {
            inputs.Add(MakeMouseInput(downFlag));
            inputs.Add(MakeMouseInput(upFlag));
        }
        inputs.AddRange(BuildModifierInputs(modifiers, keyUp: true));
        var click = SendBatch([.. inputs], InputStage.Click, []);
        return new InputResult([.. movement.Sends, .. click.Sends], click.Cleanup);
    }

    private static INPUT MakeMouseInput(uint dwFlags) => new() {
        type = INPUT_MOUSE,
        union = new INPUT_UNION { mi = new MOUSEINPUT { dwFlags = dwFlags } },
    };

    private static INPUT[] BuildModifierInputs(ActionModifiers modifiers, bool keyUp) {
        var inputs = new List<INPUT>(3);
        uint flags = keyUp ? KEYEVENTF_KEYUP : 0;

        if (modifiers.HasFlag(ActionModifiers.Shift)) {
            inputs.Add(new INPUT { type = INPUT_KEYBOARD, union = new INPUT_UNION { ki = new KEYBDINPUT { wVk = VK_SHIFT, dwFlags = flags } } });
        }
        if (modifiers.HasFlag(ActionModifiers.Ctrl)) {
            inputs.Add(new INPUT { type = INPUT_KEYBOARD, union = new INPUT_UNION { ki = new KEYBDINPUT { wVk = VK_CONTROL, dwFlags = flags } } });
        }
        if (modifiers.HasFlag(ActionModifiers.Alt)) {
            inputs.Add(new INPUT { type = INPUT_KEYBOARD, union = new INPUT_UNION { ki = new KEYBDINPUT { wVk = VK_MENU, dwFlags = flags } } });
        }

        return [.. inputs];
    }

    public InputResult ClearStuckModifiers() {
        INPUT[] keyUps = [
            new() { type = INPUT_KEYBOARD, union = new INPUT_UNION { ki = new KEYBDINPUT { wVk = VK_MENU, dwFlags = KEYEVENTF_KEYUP } } },
            new() { type = INPUT_KEYBOARD, union = new INPUT_UNION { ki = new KEYBDINPUT { wVk = VK_CONTROL, dwFlags = KEYEVENTF_KEYUP } } },
            new() { type = INPUT_KEYBOARD, union = new INPUT_UNION { ki = new KEYBDINPUT { wVk = VK_SHIFT, dwFlags = KEYEVENTF_KEYUP } } },
        ];
        return SendBatch(keyUps, InputStage.ClearModifiers, []);
    }

    public Task<InputResult> SendDrag(Point start, Point end, MouseAction button, ActionModifiers modifiers = ActionModifiers.None) {
        if (button is not (MouseAction.LeftClick or MouseAction.DoubleClick or MouseAction.RightClick or MouseAction.MiddleClick)) {
            throw new ArgumentOutOfRangeException(nameof(button));
        }

        return Task.Run(() => SendDragCore(start, end, button, modifiers));
    }

    private async Task<InputResult> SendDragCore(Point start, Point end, MouseAction button, ActionModifiers modifiers) {
        var (downFlag, upFlag) = button switch {
            MouseAction.RightClick => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP),
            MouseAction.MiddleClick => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP),
            _ => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP),
        };

        var (sx, sy) = NormalizePoint(start);
        var (ex, ey) = NormalizePoint(end);

        var modKeyDowns = BuildModifierInputs(modifiers, keyUp: false);
        var modKeyUps = BuildModifierInputs(modifiers, keyUp: true);

        // Phase 1: [mod-downs] + move-to-start + button-down
        var phase1 = new List<INPUT>(modKeyDowns.Length + 2);
        phase1.AddRange(modKeyDowns);
        phase1.Add(MakeMoveInput(sx, sy));
        phase1.Add(MakeMouseInput(downFlag));

        var held = new List<INPUT>();
        var first = SendBatch([.. phase1], InputStage.DragStart, held);
        if (!first.Succeeded) {
            return first;
        }

        // Phase 2: small intermediate move to cross the OS drag threshold
        // (SM_CXDRAG/SM_CYDRAG, typically 4px). Without this, the app may treat
        // the button-down as a click rather than a drag initiation.
        await _delay.Delay(100, CancellationToken.None).ConfigureAwait(false);
        int nudgeDx = ex - sx;
        int nudgeDy = ey - sy;
        int nudgeX = sx + (nudgeDx != 0 ? Math.Sign(nudgeDx) : 0) * (65535 / 500); // ~3-4px nudge toward end
        int nudgeY = sy + (nudgeDy != 0 ? Math.Sign(nudgeDy) : 0) * (65535 / 500);
        var nudge = SendBatch([MakeMoveInput(nudgeX, nudgeY)], InputStage.DragNudge, held);
        if (!nudge.Succeeded) {
            return new InputResult([.. first.Sends, .. nudge.Sends], nudge.Cleanup);
        }
        await _delay.Delay(50, CancellationToken.None).ConfigureAwait(false);

        // Phase 3: move-to-end + button-up + [mod-ups]
        var phase3 = new List<INPUT>(2 + modKeyUps.Length);
        phase3.Add(MakeMoveInput(ex, ey));
        phase3.Add(MakeMouseInput(upFlag));
        phase3.AddRange(modKeyUps);

        var last = SendBatch([.. phase3], InputStage.DragEnd, held);
        return new InputResult([.. first.Sends, .. nudge.Sends, .. last.Sends], last.Cleanup);
    }

    private static INPUT MakeMoveInput(int nx, int ny) => new() {
        type = INPUT_MOUSE,
        union = new INPUT_UNION {
            mi = new MOUSEINPUT {
                dx = nx,
                dy = ny,
                dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK,
            }
        },
    };

    public InputResult SendScroll(int wheelDelta, ActionModifiers modifiers = ActionModifiers.None) {
        var scrollInput = new INPUT {
            type = INPUT_MOUSE,
            union = new INPUT_UNION {
                mi = new MOUSEINPUT {
                    mouseData = unchecked((uint)wheelDelta),
                    dwFlags = MOUSEEVENTF_WHEEL,
                }
            },
        };

        if (modifiers == ActionModifiers.None) {
            return SendBatch([scrollInput], InputStage.Scroll, []);
        }

        var modKeyDowns = BuildModifierInputs(modifiers, keyUp: false);
        var modKeyUps = BuildModifierInputs(modifiers, keyUp: true);
        var inputs = new INPUT[modKeyDowns.Length + 1 + modKeyUps.Length];
        modKeyDowns.CopyTo(inputs, 0);
        inputs[modKeyDowns.Length] = scrollInput;
        modKeyUps.CopyTo(inputs, modKeyDowns.Length + 1);
        return SendBatch(inputs, InputStage.Scroll, []);
    }

    private InputResult SendBatch(INPUT[] inputs, InputStage stage, List<INPUT> held) {
        var native = _sender.Send(inputs);
        var primary = new InputSendOutcome(stage, inputs.Length, native.Sent, native.Error);
        foreach (var input in inputs.Take((int)Math.Min(native.Sent, (uint)inputs.Length))) {
            TrackHeld(input, held);
        }
        if (primary.Succeeded || held.Count == 0) {
            return new InputResult([primary]);
        }

        var releases = held.OrderBy(input => input.type).Select(MakeRelease).ToArray();
        var cleanupNative = _sender.Send(releases);
        var cleanup = new InputSendOutcome(InputStage.ReleaseCleanup, releases.Length, cleanupNative.Sent, cleanupNative.Error);
        if (!cleanup.Succeeded) {
            LogCleanupFailed(cleanup.ToString());
        }
        return new InputResult([primary], cleanup);
    }

    private static void TrackHeld(INPUT input, List<INPUT> held) {
        if (input.type == INPUT_KEYBOARD) {
            if ((input.union.ki.dwFlags & KEYEVENTF_KEYUP) != 0) {
                held.RemoveAll(item => item.type == INPUT_KEYBOARD && item.union.ki.wVk == input.union.ki.wVk);
            } else {
                held.Add(input);
            }
        } else {
            var flags = input.union.mi.dwFlags;
            if (flags is MOUSEEVENTF_LEFTDOWN or MOUSEEVENTF_RIGHTDOWN or MOUSEEVENTF_MIDDLEDOWN) {
                held.Add(input);
            } else if (flags is MOUSEEVENTF_LEFTUP or MOUSEEVENTF_RIGHTUP or MOUSEEVENTF_MIDDLEUP) {
                held.RemoveAll(item => item.type == INPUT_MOUSE && MakeRelease(item).union.mi.dwFlags == flags);
            }
        }
    }

    private static INPUT MakeRelease(INPUT held) {
        if (held.type == INPUT_KEYBOARD) {
            held.union.ki.dwFlags = KEYEVENTF_KEYUP;
            return held;
        }
        return MakeMouseInput(held.union.mi.dwFlags switch {
            MOUSEEVENTF_LEFTDOWN => MOUSEEVENTF_LEFTUP,
            MOUSEEVENTF_RIGHTDOWN => MOUSEEVENTF_RIGHTUP,
            MOUSEEVENTF_MIDDLEDOWN => MOUSEEVENTF_MIDDLEUP,
            _ => throw new InvalidOperationException("Only held synthetic buttons can be released."),
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "SendInput release cleanup failed: {Details}")]
    private partial void LogCleanupFailed(string details);
}
