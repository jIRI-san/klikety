using System.Drawing;
using System.Runtime.InteropServices;

using Klikety.Config;
using Klikety.Interop;

namespace Klikety.Services;

/// <summary>
/// Moves the mouse cursor and sends click actions via Win32 SendInput.
/// All coordinates are physical pixels; normalized to 0–65535 range for MOUSEEVENTF_ABSOLUTE.
/// </summary>
public sealed class MouseActionService : IMouseActionService {
    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;

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
    private struct INPUT {
        public uint type;
        public MOUSEINPUT mi;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public void MoveTo(Point physicalPoint) {
        var bounds = NativeMethods.GetPrimaryScreenBounds();
        int normalizedX = (int)((physicalPoint.X - bounds.X) * 65535.0 / (bounds.Width - 1));
        int normalizedY = (int)((physicalPoint.Y - bounds.Y) * 65535.0 / (bounds.Height - 1));

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

    public void SendAction(Point physicalPoint, MouseAction action) {
        MoveTo(physicalPoint);

        var (downFlag, upFlag) = action switch {
            MouseAction.LeftClick or MouseAction.DoubleClick => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP),
            MouseAction.RightClick => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP),
            MouseAction.MiddleClick => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP),
            _ => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP),
        };

        var clickInputs = new INPUT[]
        {
            new() { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = downFlag } },
            new() { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = upFlag } },
        };

        _ = SendInput((uint)clickInputs.Length, clickInputs, Marshal.SizeOf<INPUT>());

        // Double-click: send a second click pair
        if (action == MouseAction.DoubleClick) {
            _ = SendInput((uint)clickInputs.Length, clickInputs, Marshal.SizeOf<INPUT>());
        }
    }
}
