using System.Diagnostics;

using Klikety.Automation;

namespace Klikety.UiaWorker;

internal sealed class ProgressiveHintDiscovery<T> where T : class {
    internal sealed class Branch(T element, int depth, Branch? parent) {
        public T Element { get; } = element;
        public int Depth { get; } = depth;
        public Branch? Parent { get; } = parent;
        public HintNode? Data { get; set; }
        public List<Branch> Children { get; } = [];
        public T? NextChild { get; set; }
        public bool Started { get; set; }
        public bool Prepared { get; set; }
        public bool Complete { get; set; }
        public bool Published { get; set; }
        public bool Failed { get; set; }
        public int Id { get; set; }
        public int SiblingIndex { get; } = parent?.Children.Count ?? 0;
        public int RemainingChildren { get; set; }
        public bool CompletionCounted { get; set; }
    }

    private sealed class CapturedTree : IHintTree<Branch> {
        public HintNode Read(Branch element) {
            var data = element.Data ?? new([], 0, 50033, HintCapabilities.None, default);
            return data with { ChildrenComplete = element.Prepared && !element.Failed };
        }
        public int[] RuntimeId(Branch element) => Read(element).RuntimeId;
        public Branch? FirstChild(Branch element) => element.Children.FirstOrDefault(c => c.Started);
        public Branch? NextSibling(Branch element) {
            var siblings = element.Parent?.Children;
            return siblings?.Skip(element.SiblingIndex + 1).FirstOrDefault(c => c.Started);
        }
        public Branch? Parent(Branch element) => element.Parent;
        public bool IsBranchFailure(Exception exception) => exception is InvalidOperationException;
    }

    private readonly IHintTree<T> _tree;
    private readonly HintRect _region;
    private readonly int _owner, _worker;
    private readonly List<Branch> _all = [];
    private List<Branch> _level = [];
    private readonly Dictionary<string, int> _identities = new(StringComparer.Ordinal);
    private readonly List<(T Element, HintTarget Target)> _entries = [];
    private readonly List<HintContainer> _containers = [];
    private int _index, _visited, _omitted, _nextContainer;
    private bool _capped;
    private bool _nodeLimited;
    private string? _reason;
    public bool IsComplete { get; private set; }
    public IReadOnlyList<(T Element, HintTarget Target)> Entries => _entries;
    public int Visited => _visited;
    public int Omitted => _omitted;
    public string? Reason => _reason;

    public ProgressiveHintDiscovery(T root, IHintTree<T> tree, HintRect region, int owner, int worker) {
        _tree = tree; _region = region; _owner = owner; _worker = worker;
        _level.Add(NewBranch(root, 0, null));
    }

    private Branch NewBranch(T element, int depth, Branch? parent) {
        var branch = new Branch(element, depth, parent) { Id = _all.Count + 1 };
        _all.Add(branch);
        return branch;
    }

    public void Step(int milliseconds = ElementHintProtocol.DiscoverySliceMs, int maxOperations = 64) {
        if (IsComplete) { return; }
        var watch = Stopwatch.StartNew();
        int operations = 0, initialEntries = _entries.Count;
        do {
            if (_index == _level.Count) {
                _level = _level.OrderBy(b => b.Children.Count).ThenBy(b => b.Id)
                    .SelectMany(b => b.Children).Where(b => !b.Started).ToList();
                _index = 0;
                if (_level.Count == 0 || _capped) { Finish(); break; }
            }
            var branch = _level[_index];
            try {
                if (!branch.Started) {
                    if (_visited == ElementHintProtocol.MaxNodes) { Cap("Node limit reached"); continue; }
                    _visited++;
                    branch.Started = true;
                    branch.Data = _tree.Read(branch.Element);
                    CheckCandidate(branch);
                    branch.NextChild = _tree.FirstChild(branch.Element);
                    if (branch.Depth == ElementHintProtocol.MaxDepth && branch.NextChild is not null) {
                        branch.Failed = true;
                        branch.NextChild = null;
                        Omit("Depth limit reached");
                    }
                } else if (branch.NextChild is { } child) {
                    if (_all.Count == ElementHintProtocol.MaxNodes) {
                        branch.Failed = true; _nodeLimited = true;
                        branch.NextChild = null; branch.Prepared = true;
                        Omit("Node limit reached");
                        _index++;
                        continue;
                    }
                    branch.Children.Add(NewBranch(child, branch.Depth + 1, branch));
                    branch.RemainingChildren++;
                    branch.NextChild = _tree.NextSibling(child);
                }
                if (branch.NextChild is null) {
                    branch.Prepared = true;
                    if (branch.Children.Count == 0) { CompleteBranch(branch); }
                    _index++;
                }
            } catch (Exception ex) when (_tree.IsBranchFailure(ex)) {
                if (branch.Depth == 0) { throw; }
                branch.Failed = true;
                branch.Prepared = true;
                branch.NextChild = null;
                Omit("Provider branch unavailable");
                CompleteBranch(branch);
                _index++;
            }
            operations++;
            // The first useful result must not wait for the rest of its sibling list.
            if (initialEntries == 0 && _entries.Count != initialEntries) { break; }
        } while (!_capped && operations < maxOperations && watch.ElapsedMilliseconds < milliseconds);
        if (_capped) { Finish(); }
    }

    private bool CanPublish(Branch branch) => branch.Data is { } data &&
        UiaTreeAlgorithms.ValidId(data.RuntimeId) && data.ProcessId > 0 &&
        data.Bounds.IsValid && data.Bounds.Clip(_region).IsValid;

    private bool Candidate(Branch branch) => branch.Data is { } data &&
        data.ProcessId != _owner && data.ProcessId != _worker && data.Enabled && !data.Offscreen &&
        ElementHintCandidatePolicy.IsInteractive(data.ControlType, data.Capabilities);

    private void CheckCandidate(Branch branch) {
        if (!Candidate(branch)) { return; }
        var data = branch.Data!;
        if (data.ProcessId <= 0 || !UiaTreeAlgorithms.ValidId(data.RuntimeId)) {
            Omit("Missing target identity");
        } else if (!data.Bounds.IsValid) {
            Omit("Invalid target geometry");
        }
    }

    private void CompleteBranch(Branch branch) {
        branch.Complete = true;
        for (var current = branch; current is not null; current = current.Parent) {
            if (!current.Complete && (!current.Prepared || current.RemainingChildren != 0)) { break; }
            current.Complete = true;
            if (!current.CompletionCounted) {
                current.CompletionCounted = true;
                if (current.Parent is { } parent) { parent.RemainingChildren--; }
            }
        }
        for (var current = branch; current is not null; current = current.Parent) {
            if (!current.Complete || current.Published || !Candidate(current) || !CanPublish(current)) { continue; }
            bool protectedByAncestor = false;
            for (var parent = current.Parent; parent is not null; parent = parent.Parent) {
                // Only the narrow ListItem folding rule can remove a discovered descendant's wrapper.
                if (!parent.Published && parent.Data is { ControlType: 50007, Capabilities: HintCapabilities.None } &&
                    Candidate(parent) && CanPublish(parent)) {
                    protectedByAncestor = true; break;
                }
            }
            if (!protectedByAncestor) { Publish(current); }
        }
    }

    private void Publish(Branch root) {
        var canonical = UiaTreeAlgorithms.Canonicalize(root, new CapturedTree(), _region, _owner, _worker);
        var tokenMap = new Dictionary<int, int>();
        foreach (var entry in canonical.Entries) {
            int token = AddTarget(entry.Element);
            if (token != 0) { tokenMap[entry.Target.Token] = token; }
        }
        var containerMap = new Dictionary<int, int>();
        var canonicalContainers = canonical.Containers.ToDictionary(c => c.Id);
        var retainedCounts = canonical.Containers.ToDictionary(c => c.Id, _ => 0);
        foreach (var entry in canonical.Entries.Where(e => tokenMap.ContainsKey(e.Target.Token))) {
            for (int id = entry.Target.ContainerId; id != 0; id = canonicalContainers[id].ParentId) { retainedCounts[id]++; }
        }
        foreach (var container in canonical.Containers) {
            if (retainedCounts[container.Id] < 2 ||
                container.TargetToken != 0 && !tokenMap.ContainsKey(container.TargetToken)) { continue; }
            if (_containers.Count == ElementHintProtocol.MaxContainers) { break; }
            int id = ++_nextContainer;
            containerMap[container.Id] = id;
            _containers.Add(container with {
                Id = id,
                ParentId = containerMap.GetValueOrDefault(container.ParentId),
                TargetToken = tokenMap.GetValueOrDefault(container.TargetToken)
            });
        }
        foreach (var entry in canonical.Entries) {
            if (!tokenMap.TryGetValue(entry.Target.Token, out int token)) { continue; }
            var retained = _entries[token - 1];
            _entries[token - 1] = (retained.Element, retained.Target with {
                ContainerId = containerMap.GetValueOrDefault(entry.Target.ContainerId)
            });
        }
        MarkPublished(root);
    }

    private static void MarkPublished(Branch branch) {
        branch.Published = true;
        foreach (var child in branch.Children.Where(c => c.Started)) { MarkPublished(child); }
    }

    private int AddTarget(Branch branch) {
        if (!Candidate(branch)) { return 0; }
        var data = branch.Data!;
        var bounds = data.Bounds.Clip(_region);
        if (data.ProcessId <= 0 || !UiaTreeAlgorithms.ValidId(data.RuntimeId) || !bounds.IsValid) { return 0; }
        string identity = string.Join(",", data.RuntimeId);
        if (_identities.ContainsKey(identity)) { return 0; }
        if (_entries.Count == ElementHintProtocol.MaxTargets) { Cap("Target limit reached"); return 0; }
        int token = _entries.Count + 1;
        _identities.Add(identity, token);
        _entries.Add((branch.Element, new(token, data.RuntimeId, data.ProcessId, data.ControlType,
            data.Capabilities, data.Bounds, bounds, bounds.Center)));
        return token;
    }

    private void Omit(string reason) { _omitted++; _reason ??= reason; }
    private void Cap(string reason) { _capped = true; Omit(reason); }
    private void Finish() {
        if (_capped || _nodeLimited) {
            _containers.Clear();
            foreach (var branch in _all.Where(b => b.Started && !b.Published)) { AddTarget(branch); }
            for (int i = 0; i < _entries.Count; i++) {
                var entry = _entries[i];
                _entries[i] = (entry.Element, entry.Target with { ContainerId = 0 });
            }
        } else {
            // Complete ancestry can arrive later without changing target identities or displayed labels.
            var canonical = UiaTreeAlgorithms.Canonicalize(_all[0], new CapturedTree(), _region, _owner, _worker);
            var tokens = canonical.Entries.ToDictionary(e => e.Target.Token,
                e => _identities[string.Join(",", e.Target.RuntimeId)]);
            _containers.Clear();
            _containers.AddRange(canonical.Containers.Select(c => c with {
                TargetToken = c.TargetToken == 0 ? 0 : tokens[c.TargetToken]
            }));
            foreach (var entry in canonical.Entries) {
                int token = tokens[entry.Target.Token];
                var retained = _entries[token - 1];
                _entries[token - 1] = (retained.Element, retained.Target with { ContainerId = entry.Target.ContainerId });
            }
        }
        IsComplete = true;
    }

    public HintResponse Response(HintRequest request, int rootPid, int cacheHits) =>
        new(ElementHintProtocol.Version, request.SessionId, request.RequestId,
            _reason is not null ? HintOutcome.Partial : IsComplete && _entries.Count == 0
                ? HintOutcome.NoTargets : HintOutcome.Success,
            _entries.Select(e => e.Target).ToArray(), _visited, _omitted, _reason,
            RootProcessId: rootPid, Containers: _containers.ToArray(),
            IsComplete: IsComplete, CacheHits: cacheHits);

    public HintDiscovery<T> Snapshot() {
        var canonical = UiaTreeAlgorithms.Canonicalize(_all[0], new CapturedTree(), _region, _owner, _worker,
            forceCapped: _capped || _nodeLimited);
        return new(canonical.Entries.Select(e => (e.Element.Element, e.Target)).ToArray(),
            _visited, _omitted, _reason, canonical.Containers);
    }
}
