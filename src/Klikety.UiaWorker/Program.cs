using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;

using Klikety.Automation;

using UiaAutomation = System.Windows.Automation.Automation;

namespace Klikety.UiaWorker;

internal static class Program {
    // Main remains a windowless MTA; async pipe continuations never own UIA objects.
    [MTAThread]
    private static int Main() {
        using var input = Console.OpenStandardInput();
        using var output = Console.OpenStandardOutput();
        using var worker = new UiaSnapshot();
        HintResponse? published = null;
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
            if (request.Command is HintCommand.Discover or HintCommand.Release) { published = null; }
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
                bool discovery = request.Command is HintCommand.Discover or HintCommand.Continue or HintCommand.Expand;
                response = ElementHintProtocol.BoundSnapshotResponse(response, discovery ? published : null);
                ElementHintProtocol.WriteAsync(output, response, ElementHintProtocol.MaxResponseBytes,
                    CancellationToken.None).GetAwaiter().GetResult();
                if (discovery && response.Outcome is HintOutcome.Success or HintOutcome.Partial or HintOutcome.NoTargets) {
                    published = response;
                }
            } catch (IOException) { return 2; }
        }
    }

    internal static HintResponse Reply(HintRequest r, HintOutcome outcome, string? reason = null,
        HintTarget[]? targets = null, HintPoint? point = null, int rootPid = 0,
        int visited = 0, int omitted = 0, HintContainer[]? containers = null) =>
        new(ElementHintProtocol.Version, r.SessionId, r.RequestId, outcome, targets ?? [],
            visited, omitted, reason, point, rootPid, containers, GroupId: r.GroupId);
}

internal sealed class UiaSnapshot : IDisposable {
    private readonly Dictionary<int, (AutomationElement Element, HintTarget Target)> _targets = [];
    private AutomationElement? _root;
    private int[] _rootId = [];
    private int _rootPid;
    private long _processStart;
    private HintRect _rootBounds;
    private long _hwnd;
    private Guid _session;
    private HintRect _region;
    private HintWindowCache<AutomationElement>? _cache;
    private int _cacheCapacity = -1;
    private HintTreeCache<AutomationElement>? _tree;
    private ProgressiveHintDiscovery<AutomationElement>? _discovery;

    public HintResponse Handle(HintRequest r) => r.Command switch {
        HintCommand.Hello => Program.Reply(r, HintOutcome.Success),
        HintCommand.Discover => Discover(r),
        HintCommand.Validate => Validate(r),
        HintCommand.Continue => Continue(r),
        HintCommand.Expand => Program.Reply(r, HintOutcome.StaleTarget, "Child-count groups are no longer supported") with { GroupId = r.GroupId },
        HintCommand.Release => Release(r),
        _ => Program.Reply(r, HintOutcome.ProtocolError),
    };

    private HintResponse Discover(HintRequest r) {
        if (r.CacheWindowCount is < 0 or > ElementHintProtocol.MaxCacheWindowCount ||
            r.GroupId != 0) { return Program.Reply(r, HintOutcome.ProtocolError, "Invalid discovery settings"); }
        _targets.Clear();
        _discovery = null;
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
        if (r.Incremental) {
            _tree = CachedTree(r);
            _discovery = new(_tree.Root!, _tree, r.Region, r.OwnerProcessId, Environment.ProcessId);
            return Continue(r);
        }
        var discovery = UiaTreeAlgorithms.Discover(_root, new AutomationTree(), r.Region,
            r.OwnerProcessId, Environment.ProcessId);
        foreach (var entry in discovery.Entries) { _targets.Add(entry.Target.Token, entry); }
        var targets = discovery.Entries.Select(item => item.Target).ToArray();
        return Program.Reply(r, discovery.Reason is not null ? HintOutcome.Partial :
            targets.Length == 0 ? HintOutcome.NoTargets : HintOutcome.Success,
            discovery.Reason, targets, rootPid: _rootPid, visited: discovery.Visited, omitted: discovery.Omitted,
            containers: discovery.Containers);
    }

    private HintTreeCache<AutomationElement> CachedTree(HintRequest r) {
        if (_cacheCapacity != r.CacheWindowCount) {
            _cache?.Clear(RetireCache);
            _cache = new(r.CacheWindowCount);
            _cacheCapacity = r.CacheWindowCount;
        }
        var key = new HintWindowKey(r.Hwnd, _rootPid, _processStart);
        _cache!.RemoveWhere(k => !Native.IsWindow((nint)k.Hwnd) ||
            Native.GetWindowThreadProcessId((nint)k.Hwnd, out uint pid) == 0 || pid != k.ProcessId ||
            k.Hwnd == key.Hwnd && k != key, RetireCache);
        HintTreeCache<AutomationElement> Create() {
            var cached = new HintTreeCache<AutomationElement>(new AutomationTree(),
                cachedIdentity: element => element.GetCachedPropertyValue(AutomationElement.RuntimeIdProperty) as int[]) { Root = _root };
            if (r.CacheWindowCount != 0) { Subscribe(cached, _root!); }
            return cached;
        }
        var tree = _cache.Get(key, Create, RetireCache);
        tree.Refresh();
        if (!tree.TryReadCached(tree.Root!, out var old)) {
            tree.Root = _root;
        } else if (!old!.RuntimeId.SequenceEqual(_rootId)) {
            _cache.RemoveWhere(k => k == key, RetireCache);
            tree = _cache.Get(key, Create, RetireCache);
        } else if (old.Bounds != _rootBounds) {
            tree.MarkDirty(null);
            tree.Root = _root;
            tree.Refresh();
        }
        return tree;
    }

    private static void Subscribe(HintTreeCache<AutomationElement> tree, AutomationElement root) {
        var cache = new CacheRequest { TreeScope = TreeScope.Element };
        cache.Add(AutomationElement.RuntimeIdProperty);
        void Dirty(object sender) {
            try {
                tree.MarkDirty((sender as AutomationElement)?.GetCachedPropertyValue(AutomationElement.RuntimeIdProperty) as int[]);
            } catch (Exception ex) when (ex is InvalidOperationException or ElementNotAvailableException) {
                // An unavailable event identity requires conservative window invalidation.
                tree.MarkDirty(null);
            }
        }
        StructureChangedEventHandler structure = (sender, _) => Dirty(sender);
        AutomationPropertyChangedEventHandler properties = (sender, _) => Dirty(sender);
        using (cache.Activate()) {
            UiaAutomation.AddStructureChangedEventHandler(root, TreeScope.Subtree, structure);
            try {
                UiaAutomation.AddAutomationPropertyChangedEventHandler(root, TreeScope.Subtree, properties,
                    AutomationElement.BoundingRectangleProperty, AutomationElement.IsOffscreenProperty,
                    AutomationElement.IsEnabledProperty, AutomationElement.IsInvokePatternAvailableProperty,
                    AutomationElement.IsTogglePatternAvailableProperty, AutomationElement.IsSelectionItemPatternAvailableProperty,
                    AutomationElement.IsExpandCollapsePatternAvailableProperty, AutomationElement.IsValuePatternAvailableProperty);
            } catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException or ArgumentException) {
                UiaAutomation.RemoveStructureChangedEventHandler(root, structure);
                throw;
            }
        }
        tree.Unsubscribe = () => {
            UiaAutomation.RemoveStructureChangedEventHandler(root, structure);
            UiaAutomation.RemoveAutomationPropertyChangedEventHandler(root, properties);
        };
    }

    private static void RetireCache(HintTreeCache<AutomationElement> tree) => tree.Unsubscribe?.Invoke();

    private bool SameSnapshot(HintRequest r) => _root is not null && r.SessionId == _session &&
        r.Hwnd == _hwnd && r.Region == _region && r.ExpectedProcessId == _rootPid &&
        r.ExpectedProcessStart == _processStart;

    private HintResponse Continue(HintRequest r) {
        if (!SameSnapshot(r) || _discovery is null || r.GroupId != 0) {
            return Program.Reply(r, HintOutcome.StaleTarget, "Discovery scope changed") with { GroupId = r.GroupId };
        }
        _discovery.Step();
        foreach (var entry in _discovery.Entries) { _targets[entry.Target.Token] = entry; }
        return _discovery.Response(r, _rootPid, _tree?.Hits ?? 0);
    }

    private HintResponse Release(HintRequest r) {
        if (r.SessionId != _session) { return Program.Reply(r, HintOutcome.StaleTarget, "Session changed"); }
        _targets.Clear(); _discovery = null; _root = null; _tree = null;
        return Program.Reply(r, HintOutcome.Success);
    }

    public void Dispose() => _cache?.Clear(RetireCache);

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
            point => AutomationElement.FromPoint(new Point(point.X, point.Y)), entry.Target.RuntimeId, tree, moveOnly: r.MoveOnly);
        if (verified is { } safePoint) {
            return Program.Reply(r, HintOutcome.Success, point: safePoint, rootPid: _rootPid);
        }
        return Reject(HintOutcome.NoSafePoint, "Control is covered or has no verified interior point");
    }

    private static HintRect RectOf(Rect r) => new(r.X, r.Y, r.Width, r.Height);
}

internal sealed class AutomationTree : IHintTree<AutomationElement> {
    private readonly CacheRequest _cache = new() { TreeScope = TreeScope.Element };
    private readonly Dictionary<AutomationElement, HintNode> _prefetched = new(ReferenceEqualityComparer.Instance);
    public AutomationTree() {
        foreach (var property in new[] {
            AutomationElement.RuntimeIdProperty, AutomationElement.ControlTypeProperty, AutomationElement.BoundingRectangleProperty,
            AutomationElement.IsEnabledProperty, AutomationElement.IsOffscreenProperty,
            AutomationElement.ProcessIdProperty, AutomationElement.IsInvokePatternAvailableProperty,
            AutomationElement.IsTogglePatternAvailableProperty, AutomationElement.IsSelectionItemPatternAvailableProperty,
            AutomationElement.IsExpandCollapsePatternAvailableProperty, AutomationElement.IsValuePatternAvailableProperty,
        }) { _cache.Add(property); }
    }
    public HintNode Read(AutomationElement element) {
        if (_prefetched.Remove(element, out var data)) { return data; }
        return ReadCached(element.GetUpdatedCache(_cache));
    }
    private static HintNode ReadCached(AutomationElement cached) {
        var data = cached.Cached;
        HintCapabilities capabilities = HintCapabilities.None;
        if (cached.GetCachedPropertyValue(AutomationElement.IsInvokePatternAvailableProperty) is true) { capabilities |= HintCapabilities.Invoke; }
        if (cached.GetCachedPropertyValue(AutomationElement.IsTogglePatternAvailableProperty) is true) { capabilities |= HintCapabilities.Toggle; }
        if (cached.GetCachedPropertyValue(AutomationElement.IsSelectionItemPatternAvailableProperty) is true) { capabilities |= HintCapabilities.Selection; }
        if (cached.GetCachedPropertyValue(AutomationElement.IsExpandCollapsePatternAvailableProperty) is true) { capabilities |= HintCapabilities.Expand; }
        if (cached.GetCachedPropertyValue(AutomationElement.IsValuePatternAvailableProperty) is true) { capabilities |= HintCapabilities.Value; }
        var bounds = data.BoundingRectangle;
        return new((int[])cached.GetCachedPropertyValue(AutomationElement.RuntimeIdProperty), data.ProcessId, data.ControlType.Id, capabilities,
            new(bounds.X, bounds.Y, bounds.Width, bounds.Height), data.IsEnabled, data.IsOffscreen);
    }
    public int[] RuntimeId(AutomationElement element) => element.GetRuntimeId();
    private AutomationElement? Prefetch(AutomationElement? element) {
        if (element is not null) {
            if (_prefetched.Count == ElementHintProtocol.MaxNodes) { _prefetched.Clear(); }
            _prefetched[element] = ReadCached(element);
        }
        return element;
    }
    public AutomationElement? FirstChild(AutomationElement element) => Prefetch(TreeWalker.ControlViewWalker.GetFirstChild(element, _cache));
    public AutomationElement? NextSibling(AutomationElement element) => Prefetch(TreeWalker.ControlViewWalker.GetNextSibling(element, _cache));
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
