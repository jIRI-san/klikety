using Klikety.Automation;

namespace Klikety.UiaWorker;

internal readonly record struct HintWindowKey(long Hwnd, int ProcessId, long ProcessStart);

internal sealed class HintWindowCache<T>(int capacity) where T : class {
    private readonly Dictionary<HintWindowKey, (HintTreeCache<T> Tree, LinkedListNode<HintWindowKey> Node)> _windows = [];
    private readonly LinkedList<HintWindowKey> _recent = [];
    public int Count => _windows.Count;

    public HintTreeCache<T> Get(HintWindowKey key, Func<HintTreeCache<T>> create, Action<HintTreeCache<T>> retire) {
        if (capacity == 0) { return create(); }
        if (_windows.TryGetValue(key, out var entry)) {
            _recent.Remove(entry.Node);
            _recent.AddFirst(entry.Node);
            return entry.Tree;
        }
        while (_windows.Count >= capacity) {
            var last = _recent.Last!;
            retire(_windows[last.Value].Tree);
            _windows.Remove(last.Value);
            _recent.RemoveLast();
        }
        var tree = create();
        _windows.Add(key, (tree, _recent.AddFirst(key)));
        return tree;
    }

    public void RemoveWhere(Func<HintWindowKey, bool> predicate, Action<HintTreeCache<T>> retire) {
        foreach (var key in _windows.Keys.Where(predicate).ToArray()) {
            var entry = _windows[key];
            retire(entry.Tree);
            _recent.Remove(entry.Node);
            _windows.Remove(key);
        }
    }

    public void Clear(Action<HintTreeCache<T>> retire) => RemoveWhere(_ => true, retire);
}

internal sealed class HintTreeCache<T>(IHintTree<T> source, Func<long>? clock = null,
    Func<T, int[]?>? cachedIdentity = null) : IHintTree<T> where T : class {
    private sealed class CachedNode {
        public HintNode? Data { get; set; }
        public T? Parent { get; set; }
        public T? Child { get; set; }
        public T? Next { get; set; }
        public bool ChildKnown { get; set; }
        public bool NextKnown { get; set; }
    }
    private readonly Dictionary<T, CachedNode> _nodes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, T> _identities = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dirty = new(StringComparer.Ordinal);
    private readonly object _dirtyGate = new();
    private readonly Func<long> _clock = clock ?? (() => Environment.TickCount64);
    private long _created = (clock ?? (() => Environment.TickCount64))();
    private bool _allDirty;
    public int Hits { get; private set; }
    public int Count => _nodes.Count;
    public Action? Unsubscribe { get; set; }
    public T? Root { get; set; }
    public bool TryReadCached(T element, out HintNode? data) {
        data = _nodes.GetValueOrDefault(element)?.Data;
        return data is not null;
    }

    public void MarkDirty(int[]? runtimeId) {
        lock (_dirtyGate) {
            if (!UiaTreeAlgorithms.ValidId(runtimeId ?? []) || _dirty.Count >= ElementHintProtocol.MaxNodes) {
                _allDirty = true; _dirty.Clear();
            } else if (!_allDirty) { _dirty.Add(string.Join(",", runtimeId!)); }
        }
    }

    public void Refresh() {
        lock (_dirtyGate) {
            Hits = 0;
            if (_allDirty || _clock() - _created >= ElementHintProtocol.CacheMaxAgeMs) {
                Clear();
            } else {
                var roots = new HashSet<T>(ReferenceEqualityComparer.Instance);
                foreach (var identity in _dirty) {
                    if (!_identities.TryGetValue(identity, out var element)) { Clear(); roots.Clear(); break; }
                    roots.Add(InvalidationRoot(element));
                }
                var independentRoots = roots.Where(root => !HasInvalidationAncestor(root, roots)).ToArray();
                foreach (var root in independentRoots) { Invalidate(root); }
            }
            _dirty.Clear(); _allDirty = false;
        }
    }

    private void Clear() {
        _nodes.Clear(); _identities.Clear(); _created = _clock();
    }

    private T InvalidationRoot(T element) {
        // Refresh a known pane/list/tree as a unit; unrelated panes keep their data.
        for (T? current = element; current is not null && _nodes.TryGetValue(current, out var data); current = data.Parent) {
            if (data.Data?.ControlType is 50008 or 50023 or 50026 or 50033) { return current; }
        }
        return element;
    }

    private bool HasInvalidationAncestor(T element, HashSet<T> roots) {
        var parent = _nodes.GetValueOrDefault(element)?.Parent;
        for (int depth = 0; depth <= ElementHintProtocol.MaxDepth && parent is not null &&
            !ReferenceEquals(parent, element) && _nodes.TryGetValue(parent, out var data); depth++, parent = data.Parent) {
            if (roots.Contains(parent)) { return true; }
        }
        return false;
    }

    private void Invalidate(T branch) {
        if (!_nodes.TryGetValue(branch, out var node)) { Clear(); return; }
        var parent = node.Parent;
        var pending = new Queue<T>();
        pending.Enqueue(branch);
        while (pending.Count > 0) {
            var current = pending.Dequeue();
            if (!_nodes.Remove(current, out var removed)) { continue; }
            if (removed.Data is { } data) { _identities.Remove(string.Join(",", data.RuntimeId)); }
            var seen = new HashSet<T>(ReferenceEqualityComparer.Instance);
            for (var child = removed.Child; child is not null && seen.Add(child) &&
                _nodes.TryGetValue(child, out var cached); child = cached.Next) {
                pending.Enqueue(child);
            }
        }
        if (parent is not null && _nodes.TryGetValue(parent, out var parentNode)) {
            parentNode.ChildKnown = false;
            // A removal can change the previous sibling's edge without changing that sibling.
            foreach (var child in _nodes.Values.Where(n => ReferenceEquals(n.Parent, parent))) { child.NextKnown = false; }
        }
    }

    private CachedNode Node(T element) {
        if (_nodes.TryGetValue(element, out var node)) { return node; }
        if (_nodes.Count == ElementHintProtocol.MaxNodes) { Clear(); }
        node = new();
        _nodes.Add(element, node);
        return node;
    }

    public HintNode Read(T element) {
        var node = Node(element);
        if (node.Data is { } cached) { Hits++; return cached; }
        var data = source.Read(element);
        node.Data = data;
        if (UiaTreeAlgorithms.ValidId(data.RuntimeId)) { _identities[string.Join(",", data.RuntimeId)] = element; }
        return data;
    }

    public int[] RuntimeId(T element) => Read(element).RuntimeId;
    private T? CanonicalEdge(T? element) {
        if (element is null || cachedIdentity is null || _nodes.ContainsKey(element)) { return element; }
        var identity = cachedIdentity(element);
        return UiaTreeAlgorithms.ValidId(identity ?? []) &&
            _identities.TryGetValue(string.Join(",", identity!), out var existing) ? existing : element;
    }

    public T? FirstChild(T element) {
        var node = Node(element);
        if (node.ChildKnown) { Hits++; return node.Child; }
        node.Child = CanonicalEdge(source.FirstChild(element));
        node.ChildKnown = true;
        if (node.Child is { } child) { Node(child).Parent = element; }
        return node.Child;
    }

    public T? NextSibling(T element) {
        var node = Node(element);
        if (node.NextKnown) { Hits++; return node.Next; }
        node.Next = CanonicalEdge(source.NextSibling(element));
        node.NextKnown = true;
        if (node.Next is { } next) { Node(next).Parent = node.Parent; }
        return node.Next;
    }
    public T? Parent(T element) => source.Parent(element);
    public bool IsBranchFailure(Exception exception) => source.IsBranchFailure(exception);
}
