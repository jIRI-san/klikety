using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;

using Klikety.Automation;

namespace Klikety.UiaWorker;

internal static class Program {
    // Main remains a windowless MTA; async pipe continuations never own UIA objects.
    [MTAThread]
    private static int Main() {
        using var input = Console.OpenStandardInput();
        using var output = Console.OpenStandardOutput();
        var worker = new UiaSnapshot();
        using (var process = Process.GetCurrentProcess()) {
            Console.Error.WriteLine($"uia-worker-ready startupMs={Math.Max(0, (long)(DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalMilliseconds)}");
        }
        while (true) {
            HintRequest request;
            try {
                request = ElementHintProtocol.ReadAsync<HintRequest>(input,
                    ElementHintProtocol.MaxRequestBytes, CancellationToken.None).GetAwaiter().GetResult();
            } catch (EndOfStreamException) { return 0; } catch (Exception ex) when (
                ex is IOException or InvalidDataException or System.Text.Json.JsonException) { return 2; }
            if (request.Version != ElementHintProtocol.Version || !Enum.IsDefined(request.Command)) { return 2; }
            HintResponse response;
            try {
                response = worker.Handle(request);
            } catch (UnauthorizedAccessException) {
                response = Reply(request, HintOutcome.AccessDenied, "Access denied");
            } catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException or ArgumentException or Win32Exception) {
                response = Reply(request, ex.HResult == unchecked((int)0x80070005) || ex is Win32Exception { NativeErrorCode: 5 }
                    ? HintOutcome.AccessDenied : HintOutcome.ProviderError, $"Provider error 0x{ex.HResult:X8}");
            }
            try {
                response = ElementHintProtocol.BoundSnapshotResponse(response);
                ElementHintProtocol.WriteAsync(output, response, ElementHintProtocol.MaxResponseBytes,
                    CancellationToken.None).GetAwaiter().GetResult();
            } catch (IOException) { return 2; }
        }
    }

    internal static HintResponse Reply(HintRequest r, HintOutcome outcome, string? reason = null,
        HintTarget[]? targets = null, HintPoint? point = null, int rootPid = 0,
        int visited = 0, int omitted = 0) =>
        new(ElementHintProtocol.Version, r.SessionId, r.RequestId, outcome, targets ?? [],
            visited, omitted, reason, point, rootPid);
}

internal sealed class UiaSnapshot {
    private readonly Dictionary<int, (AutomationElement Element, HintTarget Target)> _targets = [];
    private AutomationElement? _root;
    private int[] _rootId = [];
    private int _rootPid;
    private long _processStart;
    private HintRect _rootBounds;
    private long _hwnd;
    private Guid _session;
    private HintRect _region;

    public HintResponse Handle(HintRequest r) => r.Command switch {
        HintCommand.Hello => Program.Reply(r, HintOutcome.Success),
        HintCommand.Discover => Discover(r),
        HintCommand.Validate => Validate(r),
        _ => Program.Reply(r, HintOutcome.ProtocolError),
    };

    private HintResponse Discover(HintRequest r) {
        _targets.Clear();
        _root = null;
        if (!r.Region.IsValid || r.Hwnd == 0 || !Native.IsWindow((nint)r.Hwnd) ||
            Native.IsIconic((nint)r.Hwnd)) { return Program.Reply(r, HintOutcome.InvalidRoot, "Invalid or minimized window"); }
        if (Native.GetWindowThreadProcessId((nint)r.Hwnd, out uint pid) == 0 ||
            pid == 0 || pid == r.OwnerProcessId || pid == Environment.ProcessId ||
            pid != r.ExpectedProcessId || r.ExpectedProcessStart <= 0) {
            return Program.Reply(r, HintOutcome.InvalidRoot, "Invalid application identity");
        }
        using var process = Process.GetProcessById((int)pid);
        _processStart = process.StartTime.ToUniversalTime().Ticks;
        if (_processStart != r.ExpectedProcessStart) {
            return Program.Reply(r, HintOutcome.InvalidRoot, "Application process changed");
        }
        _root = AutomationElement.FromHandle((nint)r.Hwnd);
        _rootBounds = RectOf(_root.Current.BoundingRectangle);
        _rootId = _root.GetRuntimeId();
        if (!UiaTreeAlgorithms.ValidId(_rootId)) { return Program.Reply(r, HintOutcome.InvalidRoot, "Missing root identity"); }
        if (!_rootBounds.IsValid) { return Program.Reply(r, HintOutcome.InvalidRoot, "Invalid root geometry"); }
        _rootPid = (int)pid;
        _hwnd = r.Hwnd;
        _session = r.SessionId;
        _region = r.Region;
        var discovery = UiaTreeAlgorithms.Discover(_root, new AutomationTree(), r.Region,
            r.OwnerProcessId, Environment.ProcessId);
        foreach (var entry in discovery.Entries) { _targets.Add(entry.Target.Token, entry); }
        var targets = discovery.Entries.Select(item => item.Target).ToArray();
        return Program.Reply(r, discovery.Reason is not null ? HintOutcome.Partial :
            targets.Length == 0 ? HintOutcome.NoTargets : HintOutcome.Success,
            discovery.Reason, targets, rootPid: _rootPid, visited: discovery.Visited, omitted: discovery.Omitted);
    }

    private HintResponse Validate(HintRequest r) {
        HintResponse Reject(HintOutcome outcome, string reason) => Program.Reply(r, outcome, reason);
        if (_root is null || r.SessionId != _session || r.Hwnd != _hwnd || r.Region != _region ||
            !_targets.TryGetValue(r.Token, out var entry) || !Native.IsWindow((nint)_hwnd) ||
            Native.IsIconic((nint)_hwnd)) { return Reject(HintOutcome.StaleTarget, "Application or snapshot changed"); }
        if (Native.GetWindowThreadProcessId((nint)_hwnd, out uint pid) == 0) {
            return Reject(HintOutcome.StaleTarget, "Window destroyed");
        }
        if (pid != _rootPid) { return Reject(HintOutcome.StaleTarget, "Application identity changed"); }
        using var process = Process.GetProcessById((int)pid);
        if (process.StartTime.ToUniversalTime().Ticks != _processStart) {
            return Reject(HintOutcome.StaleTarget, "Application identity changed");
        }
        var freshRoot = AutomationElement.FromHandle((nint)_hwnd);
        var currentRoot = new HintRootIdentity((int)pid, process.StartTime.ToUniversalTime().Ticks,
            freshRoot.GetRuntimeId(), RectOf(freshRoot.Current.BoundingRectangle));
        var foreground = Native.GetForegroundWindow().ToInt64();
        var rootChange = UiaTreeAlgorithms.RootChanged(new(_rootPid, _processStart, _rootId, _rootBounds),
            currentRoot, _hwnd, foreground);
        if (rootChange is not null) {
            return Reject(HintOutcome.StaleTarget, rootChange == "Application lost foreground"
                ? $"{rootChange} (expected={_hwnd}, actual={foreground})" : rootChange);
        }
        var tree = new AutomationTree();
        var element = entry.Element;
        if (UiaTreeAlgorithms.TargetChanged(entry.Target, tree.Read(element)) ||
            !UiaTreeAlgorithms.DescendsFrom(element, _rootId, tree)) {
            return Reject(HintOutcome.StaleTarget, "Control moved, disappeared, or became unavailable");
        }
        HintPoint? preferred = null;
        if (element.TryGetClickablePoint(out var clickable) && double.IsFinite(clickable.X) && double.IsFinite(clickable.Y) &&
            clickable.X >= int.MinValue && clickable.X <= int.MaxValue && clickable.Y >= int.MinValue && clickable.Y <= int.MaxValue) {
            preferred = new((int)Math.Floor(clickable.X), (int)Math.Floor(clickable.Y));
        }
        var verified = UiaTreeAlgorithms.VerifiedPoint(entry.Target.VisibleBounds, preferred,
            point => Native.OwnsPoint((nint)_hwnd, point),
            point => AutomationElement.FromPoint(new Point(point.X, point.Y)), entry.Target.RuntimeId, tree);
        if (verified is { } safePoint) {
            return Program.Reply(r, HintOutcome.Success, point: safePoint, rootPid: _rootPid);
        }
        return Reject(HintOutcome.NoSafePoint, "Control is covered or has no verified interior point");
    }

    private static HintRect RectOf(Rect r) => new(r.X, r.Y, r.Width, r.Height);
}

internal sealed class AutomationTree : IHintTree<AutomationElement> {
    private readonly CacheRequest _cache = new() { TreeScope = TreeScope.Element };
    public AutomationTree() {
        foreach (var property in new[] {
            AutomationElement.ControlTypeProperty, AutomationElement.BoundingRectangleProperty,
            AutomationElement.IsEnabledProperty, AutomationElement.IsOffscreenProperty,
            AutomationElement.ProcessIdProperty, AutomationElement.IsInvokePatternAvailableProperty,
            AutomationElement.IsTogglePatternAvailableProperty, AutomationElement.IsSelectionItemPatternAvailableProperty,
            AutomationElement.IsExpandCollapsePatternAvailableProperty, AutomationElement.IsValuePatternAvailableProperty,
        }) { _cache.Add(property); }
    }
    public HintNode Read(AutomationElement element) {
        var cached = element.GetUpdatedCache(_cache);
        var data = cached.Cached;
        HintCapabilities capabilities = HintCapabilities.None;
        if (cached.GetCachedPropertyValue(AutomationElement.IsInvokePatternAvailableProperty) is true) { capabilities |= HintCapabilities.Invoke; }
        if (cached.GetCachedPropertyValue(AutomationElement.IsTogglePatternAvailableProperty) is true) { capabilities |= HintCapabilities.Toggle; }
        if (cached.GetCachedPropertyValue(AutomationElement.IsSelectionItemPatternAvailableProperty) is true) { capabilities |= HintCapabilities.Selection; }
        if (cached.GetCachedPropertyValue(AutomationElement.IsExpandCollapsePatternAvailableProperty) is true) { capabilities |= HintCapabilities.Expand; }
        if (cached.GetCachedPropertyValue(AutomationElement.IsValuePatternAvailableProperty) is true) { capabilities |= HintCapabilities.Value; }
        var bounds = data.BoundingRectangle;
        return new(element.GetRuntimeId(), data.ProcessId, data.ControlType.Id, capabilities,
            new(bounds.X, bounds.Y, bounds.Width, bounds.Height), data.IsEnabled, data.IsOffscreen);
    }
    public int[] RuntimeId(AutomationElement element) => element.GetRuntimeId();
    public AutomationElement? FirstChild(AutomationElement element) => TreeWalker.ControlViewWalker.GetFirstChild(element);
    public AutomationElement? NextSibling(AutomationElement element) => TreeWalker.ControlViewWalker.GetNextSibling(element);
    public AutomationElement? Parent(AutomationElement element) => TreeWalker.RawViewWalker.GetParent(element);
    public bool IsBranchFailure(Exception exception) => exception is ElementNotAvailableException or InvalidOperationException or COMException;
}

internal static class Native {
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(HintPoint point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
    internal static bool OwnsPoint(nint root, HintPoint point) =>
        GetAncestor(WindowFromPoint(point), 2) == root;
}
