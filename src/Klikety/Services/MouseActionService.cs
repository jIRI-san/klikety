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
    private struct INPUT {
        [FieldOffset(0)] public uint type;
        [FieldOffset(4)] public MOUSEINPUT mi;
        [FieldOffset(4)] public KEYBDINPUT ki;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public MouseActionService(ILogger logger) {
        _logger = logger;
    }

    public void MoveTo(Point physicalPoint) {
        var bounds = NativeMethods.GetPrimaryScreenBounds();
        int divisorX = Math.Max(bounds.Width - 1, 1);
        int divisorY = Math.Max(bounds.Height - 1, 1);
        int normalizedX = (int)((physicalPoint.X - bounds.X) * 65535.0 / divisorX);
        int normalizedY = (int)((physicalPoint.Y - bounds.Y) * 65535.0 / divisorY);

        var input = new INPUT {
            type = INPUT_MOUSE,
            mi = new MOUSEINPUT {
                dx = normalizedX,
                dy = normalizedY,
                dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE,
            },
        };

        _ = SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    public void SendAction(Point physicalPoint, MouseAction action, ActionModifiers modifiers = ActionModifiers.None) {
        MoveTo(physicalPoint);

        if (action == MouseAction.MoveOnly) {
            return;
        }

        var (downFlag, upFlag) = action switch {
            MouseAction.LeftClick or MouseAction.DoubleClick => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP),
            MouseAction.RightClick => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP),
            MouseAction.MiddleClick => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP),
            _ => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP),
        };

        if (modifiers == ActionModifiers.None) {
            var clickInputs = new INPUT[] {
                new() { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = downFlag } },
                new() { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = upFlag } },
            };

            _ = SendInput((uint)clickInputs.Length, clickInputs, Marshal.SizeOf<INPUT>());
        } else {
            var modKeyDowns = BuildModifierInputs(modifiers, keyUp: false);
            var modKeyUps = BuildModifierInputs(modifiers, keyUp: true);

            var inputs = new List<INPUT>(modKeyDowns.Length + 2 + modKeyUps.Length);
            inputs.AddRange(modKeyDowns);
            inputs.Add(new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = downFlag } });
            inputs.Add(new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = upFlag } });
            inputs.AddRange(modKeyUps);

            var inputArray = inputs.ToArray();
            var sent = SendInput((uint)inputArray.Length, inputArray, Marshal.SizeOf<INPUT>());
            if (sent < inputArray.Length) {
                // Compensating KEYUP for any modifiers that were sent down
                LogPartialSend(sent, inputArray.Length);
                _ = SendInput((uint)modKeyUps.Length, modKeyUps, Marshal.SizeOf<INPUT>());
            }
        }

        // Double-click: send a second click pair
        if (action == MouseAction.DoubleClick) {
            var clickInputs = new INPUT[] {
                new() { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = downFlag } },
                new() { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = upFlag } },
            };
            _ = SendInput((uint)clickInputs.Length, clickInputs, Marshal.SizeOf<INPUT>());
        }
    }

    private static INPUT[] BuildModifierInputs(ActionModifiers modifiers, bool keyUp) {
        var inputs = new List<INPUT>(3);
        uint flags = keyUp ? KEYEVENTF_KEYUP : 0;

        if (modifiers.HasFlag(ActionModifiers.Shift)) {
            inputs.Add(new INPUT { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = VK_SHIFT, dwFlags = flags } });
        }
        if (modifiers.HasFlag(ActionModifiers.Ctrl)) {
            inputs.Add(new INPUT { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = VK_CONTROL, dwFlags = flags } });
        }
        if (modifiers.HasFlag(ActionModifiers.Alt)) {
            inputs.Add(new INPUT { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = VK_MENU, dwFlags = flags } });
        }

        return [.. inputs];
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "SendInput partial send: {Sent}/{Total}. Issuing compensating KEYUP.")]
    private partial void LogPartialSend(uint sent, int total);
}
