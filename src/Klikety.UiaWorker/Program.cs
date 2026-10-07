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
        _root = AutomationElement.FromHandle((nint)r.Hwnd);
        using var process = Process.GetProcessById((int)pid);
        _processStart = process.StartTime.ToUniversalTime().Ticks;
        if (_processStart != r.ExpectedProcessStart) {
            return Program.Reply(r, HintOutcome.InvalidRoot, "Application process changed");
        }
        _rootBounds = RectOf(_root.Current.BoundingRectangle);
        _rootId = _root.GetRuntimeId();
        if (!ValidId(_rootId)) { return Program.Reply(r, HintOutcome.InvalidRoot, "Missing root identity"); }
        _rootPid = (int)pid;
        _hwnd = r.Hwnd;
        _session = r.SessionId;
        _region = r.Region;
        var cache = new CacheRequest { TreeScope = TreeScope.Element };
        foreach (var property in new[] {
            AutomationElement.ControlTypeProperty, AutomationElement.BoundingRectangleProperty,
            AutomationElement.IsEnabledProperty, AutomationElement.IsOffscreenProperty,
            AutomationElement.ProcessIdProperty, AutomationElement.IsInvokePatternAvailableProperty,
            AutomationElement.IsTogglePatternAvailableProperty, AutomationElement.IsSelectionItemPatternAvailableProperty,
            AutomationElement.IsExpandCollapsePatternAvailableProperty, AutomationElement.IsValuePatternAvailableProperty,
        }) { cache.Add(property); }
        var pending = new Stack<(AutomationElement Element, int Depth)>();
        pending.Push((_root, 0));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int visited = 0, omitted = 0;
        string? reason = null;
        var walker = TreeWalker.ControlViewWalker;
        while (pending.Count > 0) {
            if (visited == ElementHintProtocol.MaxNodes) { reason = "Node limit reached"; omitted++; break; }
            var (element, depth) = pending.Pop();
            visited++;
            try {
                var cachedElement = element.GetUpdatedCache(cache);
                var data = cachedElement.Cached;
                var capabilities = Capabilities(cachedElement);
                int[] id = element.GetRuntimeId();
                var bounds = RectOf(data.BoundingRectangle);
                var clipped = bounds.Clip(r.Region);
                if (data.ProcessId != r.OwnerProcessId && data.ProcessId != Environment.ProcessId &&
                    data.IsEnabled && !data.IsOffscreen && clipped.IsValid &&
                    ElementHintCandidatePolicy.IsInteractive(data.ControlType.Id, capabilities)) {
                    if (!ValidId(id)) { omitted++; reason ??= "Missing target identity"; } else if (seen.Add(string.Join(",", id))) {
                        if (_targets.Count == ElementHintProtocol.MaxTargets) { omitted++; reason = "Target limit reached"; break; }
                        int token = _targets.Count + 1;
                        var target = new HintTarget(token, id, data.ProcessId, data.ControlType.Id,
                            capabilities, bounds, clipped, clipped.Center);
                        _targets.Add(token, (element, target));
                    }
                }
                // Siblings are pushed incrementally: a wide provider cannot allocate an unbounded child list.
                var sibling = depth == 0 ? null : walker.GetNextSibling(element);
                if (sibling is not null) { pending.Push((sibling, depth)); }
                if (depth < ElementHintProtocol.MaxDepth) {
                    var child = walker.GetFirstChild(element);
                    if (child is not null) { pending.Push((child, depth + 1)); }
                } else if (walker.GetFirstChild(element) is not null) { omitted++; reason ??= "Depth limit reached"; }
            } catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException) {
                if (depth == 0) { throw; }
                omitted++; reason ??= "Provider branch unavailable";
                // The unread sibling branch is deliberately reported as omitted.
            }
        }
        var targets = _targets.Values.Select(item => item.Target).OrderBy(t => t.Bounds.Y)
            .ThenBy(t => t.Bounds.X).ThenBy(t => t.Token).ToArray();
        return Program.Reply(r, reason is not null ? HintOutcome.Partial :
            targets.Length == 0 ? HintOutcome.NoTargets : HintOutcome.Success,
            reason, targets, rootPid: _rootPid, visited: visited, omitted: omitted);
    }

    private static HintCapabilities Capabilities(AutomationElement element) {
        object Read(AutomationProperty p) => element.GetCachedPropertyValue(p);
        HintCapabilities result = HintCapabilities.None;
        if (Read(AutomationElement.IsInvokePatternAvailableProperty) is true) { result |= HintCapabilities.Invoke; }
        if (Read(AutomationElement.IsTogglePatternAvailableProperty) is true) { result |= HintCapabilities.Toggle; }
        if (Read(AutomationElement.IsSelectionItemPatternAvailableProperty) is true) { result |= HintCapabilities.Selection; }
        if (Read(AutomationElement.IsExpandCollapsePatternAvailableProperty) is true) { result |= HintCapabilities.Expand; }
        if (Read(AutomationElement.IsValuePatternAvailableProperty) is true) { result |= HintCapabilities.Value; }
        return result;
    }

    private HintResponse Validate(HintRequest r) {
        HintResponse Reject(HintOutcome outcome, string reason) => Program.Reply(r, outcome, reason);
        if (_root is null || r.SessionId != _session || r.Hwnd != _hwnd || r.Region != _region ||
            !_targets.TryGetValue(r.Token, out var entry) || !Native.IsWindow((nint)_hwnd) ||
            Native.IsIconic((nint)_hwnd)) { return Reject(HintOutcome.StaleTarget, "Application or snapshot changed"); }
        if (Native.GetWindowThreadProcessId((nint)_hwnd, out uint pid) == 0) {
            return Reject(HintOutcome.StaleTarget, "Window destroyed");
        }
        var freshRoot = AutomationElement.FromHandle((nint)_hwnd);
        using var process = Process.GetProcessById((int)pid);
        if (pid != _rootPid || !freshRoot.GetRuntimeId().SequenceEqual(_rootId) ||
            process.StartTime.ToUniversalTime().Ticks != _processStart) {
            return Reject(HintOutcome.StaleTarget, "Application identity changed");
        }
        if (RectOf(freshRoot.Current.BoundingRectangle) != _rootBounds) {
            return Reject(HintOutcome.StaleTarget, "Application bounds changed");
        }
        if (Native.GetForegroundWindow() != (nint)_hwnd) {
            return Reject(HintOutcome.StaleTarget, "Application lost foreground");
        }
        var element = entry.Element;
        var current = element.Current;
        if (!current.IsEnabled || current.IsOffscreen || current.ProcessId != entry.Target.ProcessId ||
            !element.GetRuntimeId().SequenceEqual(entry.Target.RuntimeId) ||
            RectOf(current.BoundingRectangle) != entry.Target.Bounds || !DescendsFrom(element, _rootId)) {
            return Reject(HintOutcome.StaleTarget, "Control moved, disappeared, or became unavailable");
        }
        var points = new List<HintPoint>();
        if (element.TryGetClickablePoint(out var clickable) && double.IsFinite(clickable.X) && double.IsFinite(clickable.Y) &&
            clickable.X >= int.MinValue && clickable.X <= int.MaxValue && clickable.Y >= int.MinValue && clickable.Y <= int.MaxValue) {
            points.Add(new((int)Math.Floor(clickable.X), (int)Math.Floor(clickable.Y)));
        }
        var area = entry.Target.VisibleBounds;
        points.Add(area.Center);
        foreach (double x in new[] { .25, .75 }) {
            foreach (double y in new[] { .25, .75 }) { points.Add(new((int)Math.Floor(area.X + area.Width * x), (int)Math.Floor(area.Y + area.Height * y))); }
        }
        foreach (var point in points.Distinct()) {
            if (!area.Contains(point) || !Native.OwnsPoint((nint)_hwnd, point)) { continue; }
            var hit = AutomationElement.FromPoint(new Point(point.X, point.Y));
            if (OwnsHit(hit, entry.Target.RuntimeId)) {
                return Program.Reply(r, HintOutcome.Success, point: point, rootPid: _rootPid);
            }
        }
        return Reject(HintOutcome.NoSafePoint, "Control is covered or has no verified interior point");
    }

    private static bool OwnsHit(AutomationElement element, int[] selectedId) {
        for (int depth = 0; depth <= ElementHintProtocol.MaxDepth; depth++) {
            if (element.GetRuntimeId().SequenceEqual(selectedId)) { return true; }
            var current = element.Current;
            HintCapabilities capabilities = HintCapabilities.None;
            foreach (var property in new[] { AutomationElement.IsInvokePatternAvailableProperty,
                AutomationElement.IsTogglePatternAvailableProperty, AutomationElement.IsSelectionItemPatternAvailableProperty,
                AutomationElement.IsExpandCollapsePatternAvailableProperty, AutomationElement.IsValuePatternAvailableProperty }) {
                if (element.GetCurrentPropertyValue(property) is true) { capabilities = HintCapabilities.Invoke; break; }
            }
            if (ElementHintCandidatePolicy.IsInteractive(current.ControlType.Id, capabilities)) { return false; }
            element = TreeWalker.RawViewWalker.GetParent(element);
            if (element is null) { return false; }
        }
        return false;
    }

    private static bool DescendsFrom(AutomationElement element, int[] id) {
        for (int depth = 0; depth <= ElementHintProtocol.MaxDepth; depth++) {
            if (element.GetRuntimeId().SequenceEqual(id)) { return true; }
            element = TreeWalker.RawViewWalker.GetParent(element);
            if (element is null) { return false; }
        }
        return false;
    }
    private static bool ValidId(int[] id) => id is { Length: > 0 and <= ElementHintProtocol.MaxRuntimeId };
    private static HintRect RectOf(Rect r) => new(r.X, r.Y, r.Width, r.Height);
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
