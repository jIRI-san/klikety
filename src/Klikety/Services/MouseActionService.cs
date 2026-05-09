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

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT_UNION {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT {
        public uint type;
        public INPUT_UNION union;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public MouseActionService(ILogger logger) {
        _logger = logger;
    }

    public void MoveTo(Point physicalPoint) {
        var (nx, ny) = NormalizePoint(physicalPoint);

        var input = new INPUT {
            type = INPUT_MOUSE,
            union = new INPUT_UNION {
                mi = new MOUSEINPUT {
                    dx = nx,
                    dy = ny,
                    dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE,
                }
            },
        };

        _ = SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    private static (int X, int Y) NormalizePoint(Point physicalPoint) {
        var bounds = NativeMethods.GetPrimaryScreenBounds();
        int divisorX = Math.Max(bounds.Width - 1, 1);
        int divisorY = Math.Max(bounds.Height - 1, 1);
        int normalizedX = (int)((physicalPoint.X - bounds.X) * 65535.0 / divisorX);
        int normalizedY = (int)((physicalPoint.Y - bounds.Y) * 65535.0 / divisorY);
        return (normalizedX, normalizedY);
    }

    public void SendAction(Point physicalPoint, MouseAction action, ActionModifiers modifiers = ActionModifiers.None) {
        MoveTo(physicalPoint);

        if (action is MouseAction.MoveOnly or MouseAction.DragDrop) {
            return;
        }

        var (downFlag, upFlag) = action switch {
            MouseAction.LeftClick or MouseAction.DoubleClick => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP),
            MouseAction.RightClick => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP),
            MouseAction.MiddleClick => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP),
            _ => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP),
        };

        int clickCount = action == MouseAction.DoubleClick ? 2 : 1;

        if (modifiers == ActionModifiers.None) {
            var clickInputs = new INPUT[clickCount * 2];
            for (int i = 0; i < clickCount; i++) {
                clickInputs[i * 2] = MakeMouseInput(downFlag);
                clickInputs[i * 2 + 1] = MakeMouseInput(upFlag);
            }
            _ = SendInput((uint)clickInputs.Length, clickInputs, Marshal.SizeOf<INPUT>());
        } else {
            // Modifiers held physically are already active — skip synthetic injection for those.
            // We inject all requested modifiers to guarantee the target app sees them,
            // since the overlay just closed and key state may be ambiguous.
            var modKeyDowns = BuildModifierInputs(modifiers, keyUp: false);
            var modKeyUps = BuildModifierInputs(modifiers, keyUp: true);

            var inputs = new List<INPUT>(modKeyDowns.Length + clickCount * 2 + modKeyUps.Length);
            inputs.AddRange(modKeyDowns);
            for (int i = 0; i < clickCount; i++) {
                inputs.Add(MakeMouseInput(downFlag));
                inputs.Add(MakeMouseInput(upFlag));
            }
            inputs.AddRange(modKeyUps);

            var inputArray = inputs.ToArray();
            var sent = SendInput((uint)inputArray.Length, inputArray, Marshal.SizeOf<INPUT>());
            if (sent < inputArray.Length) {
                // Compensating KEYUP for any modifiers — extra key-ups for already-up keys are harmless
                LogPartialSend(sent, inputArray.Length);
                _ = SendInput((uint)modKeyUps.Length, modKeyUps, Marshal.SizeOf<INPUT>());
            }
        }
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "SendInput partial send: {Sent}/{Total}. Issuing compensating KEYUP.")]
    private partial void LogPartialSend(uint sent, int total);

    public void SendDrag(Point start, Point end, MouseAction button, ActionModifiers modifiers = ActionModifiers.None) {
        if (button is MouseAction.MoveOnly or MouseAction.DragDrop) {
            return;
        }

        // Run on a background thread to avoid blocking the WPF dispatcher
        // with Thread.Sleep delays needed for drag threshold detection.
        Task.Run(() => SendDragCore(start, end, button, modifiers));
    }

    private void SendDragCore(Point start, Point end, MouseAction button, ActionModifiers modifiers) {
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

        var p1Array = phase1.ToArray();
        var sent1 = SendInput((uint)p1Array.Length, p1Array, Marshal.SizeOf<INPUT>());
        if (sent1 < p1Array.Length) {
            LogPartialSend(sent1, p1Array.Length);
            var compensate = new List<INPUT>(1 + modKeyUps.Length);
            compensate.Add(MakeMouseInput(upFlag));
            compensate.AddRange(modKeyUps);
            _ = SendInput((uint)compensate.Count, [.. compensate], Marshal.SizeOf<INPUT>());
            return;
        }

        // Phase 2: small intermediate move to cross the OS drag threshold
        // (SM_CXDRAG/SM_CYDRAG, typically 4px). Without this, the app may treat
        // the button-down as a click rather than a drag initiation.
        Thread.Sleep(100);
        int nudgeDx = ex - sx;
        int nudgeDy = ey - sy;
        int nudgeX = sx + (nudgeDx != 0 ? Math.Sign(nudgeDx) : 0) * (65535 / 500); // ~3-4px nudge toward end
        int nudgeY = sy + (nudgeDy != 0 ? Math.Sign(nudgeDy) : 0) * (65535 / 500);
        _ = SendInput(1, [MakeMoveInput(nudgeX, nudgeY)], Marshal.SizeOf<INPUT>());

        Thread.Sleep(50);

        // Phase 3: move-to-end + button-up + [mod-ups]
        var phase3 = new List<INPUT>(2 + modKeyUps.Length);
        phase3.Add(MakeMoveInput(ex, ey));
        phase3.Add(MakeMouseInput(upFlag));
        phase3.AddRange(modKeyUps);

        var p3Array = phase3.ToArray();
        var sent3 = SendInput((uint)p3Array.Length, p3Array, Marshal.SizeOf<INPUT>());
        if (sent3 < p3Array.Length) {
            LogPartialSend(sent3, p3Array.Length);
            var compensate = new List<INPUT>(1 + modKeyUps.Length);
            compensate.Add(MakeMouseInput(upFlag));
            compensate.AddRange(modKeyUps);
            _ = SendInput((uint)compensate.Count, [.. compensate], Marshal.SizeOf<INPUT>());
        }
    }

    private static INPUT MakeMoveInput(int nx, int ny) => new() {
        type = INPUT_MOUSE,
        union = new INPUT_UNION {
            mi = new MOUSEINPUT {
                dx = nx,
                dy = ny,
                dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE,
            }
        },
    };

    public void SendScroll(int wheelDelta, ActionModifiers modifiers = ActionModifiers.None) {
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
            _ = SendInput(1, [scrollInput], Marshal.SizeOf<INPUT>());
            return;
        }

        var modKeyDowns = BuildModifierInputs(modifiers, keyUp: false);
        var modKeyUps = BuildModifierInputs(modifiers, keyUp: true);
        var inputs = new INPUT[modKeyDowns.Length + 1 + modKeyUps.Length];
        modKeyDowns.CopyTo(inputs, 0);
        inputs[modKeyDowns.Length] = scrollInput;
        modKeyUps.CopyTo(inputs, modKeyDowns.Length + 1);
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent < inputs.Length) {
            // Compensate: release modifier keys to prevent stuck state
            _ = SendInput((uint)modKeyUps.Length, modKeyUps, Marshal.SizeOf<INPUT>());
        }
    }
}
