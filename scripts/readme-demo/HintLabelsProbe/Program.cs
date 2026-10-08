using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

using Klikety.Automation;
using Klikety.Navigation;

if (Environment.UserName != "WDAGUtilityAccount") {
    throw new InvalidOperationException("The screenshot probe runs only inside Windows Sandbox.");
}
if (args.Length != 4) { throw new ArgumentException("Expected worker path, fixture HWND, app PID, output file."); }
if (!Native.SetProcessDpiAwarenessContext((nint)(-4))) {
    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
}
long hwnd = long.Parse(args[1]);
if (!Native.GetWindowRect((nint)hwnd, out var rect) ||
    Native.GetWindowThreadProcessId((nint)hwnd, out uint pid) == 0) {
    throw new InvalidOperationException("Demo window is unavailable.");
}
using var fixture = Process.GetProcessById((int)pid);
if (fixture.ProcessName != "powershell") { throw new InvalidOperationException("Unexpected demo process."); }
var worker = new UiaWorkerSupervisor(args[0], discoveryTimeoutMs: 10000);
try {
    var response = await worker.DiscoverAsync(new((nint)hwnd, int.Parse(args[2]), (int)pid,
        fixture.StartTime.ToUniversalTime().Ticks),
        new HintRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top)
            .Clip(new(0, 0, Native.GetSystemMetrics(0), Native.GetSystemMetrics(1))), CancellationToken.None);
    if (response.Outcome is not (HintOutcome.Success or HintOutcome.Partial) || response.Targets.Length == 0) {
        throw new InvalidOperationException($"Demo discovery failed: {response.Outcome}, {response.Reason}");
    }
    var entries = new ElementHintHierarchy(response.Targets, response.Containers ?? []).Build(response.Targets, 100);
    int groupIndex = entries.ToList().FindIndex(e => e.Compact && e.Description.StartsWith("Combo box"));
    if (groupIndex < 0) { groupIndex = entries.ToList().FindIndex(e => e.Compact && e.Preview.Y > rect.Top + 180); }
    if (groupIndex < 0) { throw new InvalidOperationException("Demo exposes no verified compound group."); }
    int[] horizontal = [0x41, 0x53, 0x44, 0x46, 0x47, 0x48, 0x4A, 0x4B, 0x4C, 0xBA];
    int[] vertical = [0x51, 0x57, 0x45, 0x52, 0x54, 0x59, 0x55, 0x49, 0x4F, 0x50];
    int[] keys = entries.Count <= horizontal.Length ? [horizontal[groupIndex]] :
        [horizontal[groupIndex / vertical.Length], vertical[groupIndex % vertical.Length]];
    File.WriteAllText(args[3], JsonSerializer.Serialize(new {
        response.Outcome, Targets = response.Targets.Length, Containers = response.Containers?.Length ?? 0,
        Entries = entries.Count, GroupKeys = keys, Children = entries[groupIndex].Children.Count,
        GroupRole = entries[groupIndex].Description,
        Groups = entries.Where(e => e.IsGroup).Select(e => new { e.Description, e.Preview, e.Compact })
    }));
} finally {
    if (!await worker.RetireAsync()) { throw new InvalidOperationException("Demo worker cleanup failed."); }
}

internal static class Native {
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetProcessDpiAwarenessContext(nint context);
    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int index);
}
