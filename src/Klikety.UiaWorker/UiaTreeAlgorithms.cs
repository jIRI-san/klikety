using Klikety.Automation;

namespace Klikety.UiaWorker;

internal sealed record HintNode(int[] RuntimeId, int ProcessId, int ControlType,
    HintCapabilities Capabilities, HintRect Bounds, bool Enabled = true, bool Offscreen = false);

internal interface IHintTree<T> where T : class {
    HintNode Read(T element);
    int[] RuntimeId(T element);
    T? FirstChild(T element);
    T? NextSibling(T element);
    T? Parent(T element);
    bool IsBranchFailure(Exception exception);
}

internal sealed record HintDiscovery<T>(IReadOnlyList<(T Element, HintTarget Target)> Entries,
    int Visited, int Omitted, string? Reason) where T : class;

internal readonly record struct HintRootIdentity(int ProcessId, long ProcessStart, int[] RuntimeId, HintRect Bounds);

internal static class UiaTreeAlgorithms {
    private sealed class DiscoveryBranch(int parent) {
        public int Parent { get; } = parent;
        public int Candidate { get; set; } = -1;
        public int Descendants { get; set; }
        public int SoleDescendant { get; set; } = -1;
        public bool Complete { get; set; } = true;
    }

    public static bool ValidId(int[] id) => id is { Length: > 0 and <= ElementHintProtocol.MaxRuntimeId };

    public static HintDiscovery<T> Discover<T>(T root, IHintTree<T> tree, HintRect region,
        int ownerProcessId, int workerProcessId) where T : class {
        var entries = new List<(T Element, HintTarget Target)>();
        var branches = new List<DiscoveryBranch>();
        var pending = new Stack<(T Element, int Depth, int Parent)>();
        pending.Push((root, 0, -1));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int visited = 0, omitted = 0;
        string? reason = null;
        bool capped = false;
        while (pending.Count > 0) {
            if (visited == ElementHintProtocol.MaxNodes) { reason = "Node limit reached"; omitted++; capped = true; break; }
            var (element, depth, parent) = pending.Pop();
            int index = branches.Count;
            var branch = new DiscoveryBranch(parent);
            branches.Add(branch);
            visited++;
            try {
                var data = tree.Read(element);
                if (data.ProcessId != ownerProcessId && data.ProcessId != workerProcessId &&
                    data.Enabled && !data.Offscreen &&
                    ElementHintCandidatePolicy.IsInteractive(data.ControlType, data.Capabilities)) {
                    if (data.ProcessId <= 0 || !ValidId(data.RuntimeId)) {
                        branch.Complete = false;
                        omitted++; reason ??= "Missing target identity";
                    } else if (!data.Bounds.IsValid) {
                        branch.Complete = false;
                        omitted++; reason ??= "Invalid target geometry";
                    } else {
                        var clipped = data.Bounds.Clip(region);
                        if (clipped.IsValid && seen.Add(string.Join(",", data.RuntimeId))) {
                            if (entries.Count == ElementHintProtocol.MaxTargets) {
                                omitted++; reason = "Target limit reached"; capped = true; break;
                            }
                            branch.Candidate = entries.Count;
                            entries.Add((element, new(entries.Count + 1, data.RuntimeId, data.ProcessId,
                                data.ControlType, data.Capabilities, data.Bounds, clipped, clipped.Center)));
                        }
                    }
                }
                var sibling = depth == 0 ? null : tree.NextSibling(element);
                if (sibling is not null) { pending.Push((sibling, depth, parent)); }
                if (depth < ElementHintProtocol.MaxDepth) {
                    var child = tree.FirstChild(element);
                    if (child is not null) { pending.Push((child, depth + 1, index)); }
                } else if (tree.FirstChild(element) is not null) {
                    branch.Complete = false;
                    omitted++; reason ??= "Depth limit reached";
                }
            } catch (Exception ex) when (tree.IsBranchFailure(ex)) {
                if (depth == 0) { throw; }
                branch.Complete = false;
                if (parent >= 0) { branches[parent].Complete = false; }
                omitted++; reason ??= "Provider branch unavailable";
            }
        }
        var redundant = new HashSet<int>();
        if (!capped) {
            // Control-view ancestry is already captured by traversal; no extra provider calls.
            for (int i = branches.Count - 1; i >= 0; i--) {
                var branch = branches[i];
                if (branch.Candidate >= 0) {
                    if (branch.Complete && branch.Descendants == 1 &&
                        IsRedundantListWrapper(entries[branch.Candidate].Target, entries[branch.SoleDescendant].Target)) {
                        redundant.Add(branch.Candidate);
                    } else {
                        branch.SoleDescendant = branch.Descendants == 0 ? branch.Candidate : -1;
                        branch.Descendants = Math.Min(2, branch.Descendants + 1);
                    }
                }
                if (branch.Parent < 0) { continue; }
                var parent = branches[branch.Parent];
                parent.Complete &= branch.Complete;
                if (branch.Descendants > 0) {
                    parent.SoleDescendant = parent.Descendants == 0 && branch.Descendants == 1 ? branch.SoleDescendant : -1;
                    parent.Descendants = Math.Min(2, parent.Descendants + branch.Descendants);
                }
            }
        }
        entries = entries.Where((_, i) => !redundant.Contains(i)).ToList();
        return new(entries.OrderBy(e => e.Target.Bounds.Y).ThenBy(e => e.Target.Bounds.X)
            .ThenBy(e => e.Target.Token).ToArray(), visited, omitted, reason);
    }

    private static bool IsRedundantListWrapper(HintTarget wrapper, HintTarget descendant) =>
        wrapper.ControlType == 50007 && wrapper.Capabilities == HintCapabilities.None &&
        descendant.ControlType == 50000 && descendant.Capabilities == HintCapabilities.Invoke &&
        wrapper.ProcessId == descendant.ProcessId && wrapper.Bounds == descendant.Bounds;

    public static string? RootChanged(HintRootIdentity expected, HintRootIdentity current,
        long hwnd, long foregroundHwnd) {
        if (expected.ProcessId != current.ProcessId || expected.ProcessStart != current.ProcessStart ||
            !current.RuntimeId.SequenceEqual(expected.RuntimeId)) { return "Application identity changed"; }
        if (expected.Bounds != current.Bounds) { return "Application bounds changed"; }
        return hwnd != foregroundHwnd ? "Application lost foreground" : null;
    }

    public static bool TargetChanged(HintTarget target, HintNode current) =>
        !current.Enabled || current.Offscreen || target.ProcessId != current.ProcessId ||
        !target.RuntimeId.SequenceEqual(current.RuntimeId) || target.Bounds != current.Bounds;

    public static bool DescendsFrom<T>(T element, int[] id, IHintTree<T> tree) where T : class {
        for (int depth = 0; depth <= ElementHintProtocol.MaxDepth; depth++) {
            if (tree.RuntimeId(element).SequenceEqual(id)) { return true; }
            var parent = tree.Parent(element);
            if (parent is null) { return false; }
            element = parent;
        }
        return false;
    }

    public static bool OwnsHit<T>(T element, int[] selectedId, IHintTree<T> tree) where T : class {
        for (int depth = 0; depth <= ElementHintProtocol.MaxDepth; depth++) {
            if (tree.RuntimeId(element).SequenceEqual(selectedId)) { return true; }
            var data = tree.Read(element);
            if (ElementHintCandidatePolicy.IsInteractive(data.ControlType, data.Capabilities)) { return false; }
            var parent = tree.Parent(element);
            if (parent is null) { return false; }
            element = parent;
        }
        return false;
    }

    public static HintPoint? VerifiedPoint<T>(HintRect area, HintPoint? clickable,
        Func<HintPoint, bool> nativeOwns, Func<HintPoint, T> hit, int[] selectedId, IHintTree<T> tree) where T : class {
        var points = new List<HintPoint>();
        if (clickable is { } preferred) { points.Add(preferred); }
        points.Add(area.Center);
        foreach (double x in new[] { .25, .75 }) {
            foreach (double y in new[] { .25, .75 }) {
                points.Add(new((int)Math.Floor(area.X + area.Width * x), (int)Math.Floor(area.Y + area.Height * y)));
            }
        }
        foreach (var point in points.Distinct()) {
            if (area.Contains(point) && nativeOwns(point) && OwnsHit(hit(point), selectedId, tree)) { return point; }
        }
        return null;
    }
}
