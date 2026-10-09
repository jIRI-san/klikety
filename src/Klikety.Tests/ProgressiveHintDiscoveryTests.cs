using Klikety.Automation;
using Klikety.UiaWorker;

namespace Klikety.Tests;

public sealed class ProgressiveHintDiscoveryTests {
    internal sealed class Node(int id, bool interactive = true) {
        public HintNode Data { get; set; } = new([42, id], 42, interactive ? 50000 : 50033,
            interactive ? HintCapabilities.Invoke : HintCapabilities.None, new(0, id, 100, 10));
        public List<Node> Children { get; } = [];
        public Node? Parent { get; private set; }
        public Node? Next { get; private set; }
        public void Add(params Node[] children) {
            foreach (var child in children) {
                child.Parent = this;
                if (Children.Count > 0) { Children[^1].Next = child; }
                Children.Add(child);
            }
        }
    }
    internal sealed class Tree : IHintTree<Node> {
        public List<int> Reads { get; } = [];
        public int NavigationCalls { get; private set; }
        public int? FailingSiblingId { get; init; }
        public HintNode Read(Node node) { Reads.Add(node.Data.RuntimeId[1]); return node.Data; }
        public int[] RuntimeId(Node node) => node.Data.RuntimeId;
        public Node? FirstChild(Node node) { NavigationCalls++; return node.Children.FirstOrDefault(); }
        public Node? NextSibling(Node node) {
            NavigationCalls++;
            if (node.Data.RuntimeId[1] == FailingSiblingId) { throw new InvalidOperationException("Provider sibling failure"); }
            return node.Next;
        }
        public Node? Parent(Node node) => node.Parent;
        public bool IsBranchFailure(Exception ex) => ex is InvalidOperationException;
    }
    private static readonly HintRect Region = new(0, 0, 1000, 100000);
    private static void Complete(ProgressiveHintDiscovery<Node> discovery) {
        for (int i = 0; !discovery.IsComplete && i < 10000; i++) { discovery.Step(1000); }
        Assert.True(discovery.IsComplete);
    }
    private static void Check(ProgressiveHintDiscovery<Node> discovery) {
        var request = new HintRequest(1, Guid.NewGuid(), Guid.NewGuid(), HintCommand.Continue,
            Region: Region);
        ElementHintProtocol.CheckResponse(request, discovery.Response(request, 42, 0));
    }

    [Fact]
    public void LeavesAppearBeforeDescendantsAndSmallerParentsExpandFirstAtEachDepth() {
        var root = new Node(1, false);
        var large = new Node(2, false); large.Add(new(20), new(21), new(22));
        var small = new Node(3, false); small.Add(new Node(30));
        root.Add(large, small, new(4));
        var tree = new Tree();
        var scan = new ProgressiveHintDiscovery<Node>(root, tree, Region, 7, 8);
        while (scan.Entries.Count == 0) { scan.Step(1000); Check(scan); }
        Assert.Equal(4, Assert.Single(scan.Entries).Target.RuntimeId[1]);
        Assert.DoesNotContain(20, tree.Reads);
        Assert.DoesNotContain(30, tree.Reads);
        Complete(scan); Check(scan);
        Assert.Equal([1, 2, 3, 4, 30, 20, 21, 22], tree.Reads);
        Assert.Equal([4, 30, 20, 21, 22], scan.Entries.Select(e => e.Target.RuntimeId[1]));
    }

    [Theory]
    [InlineData(11)]
    [InlineData(31)]
    [InlineData(101)]
    public void WideBranchesAreFullyDiscoveredWithoutChildCountGroups(int count) {
        var root = new Node(1, false);
        var pane = new Node(2, false);
        pane.Add(Enumerable.Range(20, count).Select(i => new Node(i)).ToArray());
        root.Add(pane, new(3));
        var tree = new Tree();
        var scan = new ProgressiveHintDiscovery<Node>(root, tree, Region, 7, 8);
        Complete(scan); Check(scan);
        Assert.Equal(count + 1, scan.Entries.Count);
        Assert.Equal(count + 3, tree.Reads.Count);
        Assert.Null(scan.Response(new(1, Guid.NewGuid(), Guid.NewGuid(), HintCommand.Continue), 42, 0).Groups);
        Assert.All(scan.Entries, e => Assert.Equal(0, e.Target.DiscoveryGroupId));
    }

    [Fact]
    public void NestedWrappersDoNotHideWideSessionListsAndCompletedRowsStreamIndependently() {
        var root = new Node(1, false);
        var wrapper = new Node(2, false);
        var list = new Node(3, false);
        foreach (int id in Enumerable.Range(100, 40)) {
            var row = new Node(id);
            row.Data = row.Data with { ControlType = 50007, Capabilities = HintCapabilities.None };
            var button = new Node(id + 100);
            button.Data = button.Data with { Bounds = row.Data.Bounds };
            row.Add(button); list.Add(row);
        }
        wrapper.Add(list); root.Add(wrapper);
        var tree = new Tree();
        var scan = new ProgressiveHintDiscovery<Node>(root, tree, Region, 7, 8);
        while (scan.Entries.Count == 0) { scan.Step(1000); Check(scan); }
        Assert.Equal(200, Assert.Single(scan.Entries).Target.RuntimeId[1]);
        Assert.DoesNotContain(239, tree.Reads);
        Assert.False(scan.IsComplete);
        Complete(scan); Check(scan);
        Assert.Equal(Enumerable.Range(200, 40), scan.Entries.Select(e => e.Target.RuntimeId[1]));
    }

    [Fact]
    public void ActionableDocumentDoesNotHoldCompletedControlsUntilItsEntireSubtreeFinishes() {
        var root = new Node(1, false);
        var document = new Node(2);
        document.Data = document.Data with { ControlType = 50030, Capabilities = HintCapabilities.Value, Bounds = Region };
        var nested = new Node(100, false);
        nested.Add(Enumerable.Range(1000, 40).Select(i => new Node(i)).ToArray());
        document.Add([new(20), nested, .. Enumerable.Range(200, 40).Select(i => new Node(i))]);
        root.Add(document, new(3));
        var tree = new Tree();
        var scan = new ProgressiveHintDiscovery<Node>(root, tree, Region, 7, 8);
        var request = new HintRequest(1, Guid.NewGuid(), Guid.NewGuid(), HintCommand.Continue, Region: Region);
        HintResponse? previous = null;
        bool streamed = false;
        while (!scan.IsComplete) {
            scan.Step(1000, maxOperations: 1); Check(scan);
            var response = scan.Response(request, 42, 0);
            if (previous is not null) { ElementHintProtocol.CheckProgress(previous, response); }
            previous = response;
            if (scan.Entries.Any(e => e.Target.RuntimeId[1] == 20) && !streamed) {
                streamed = true;
                Assert.DoesNotContain(1000, tree.Reads);
                Assert.DoesNotContain(scan.Entries, e => e.Target.RuntimeId[1] == 2);
                Assert.False(scan.IsComplete);
            }
        }
        Assert.True(streamed);
        Assert.Equal(83, scan.Entries.Count);
        Assert.Equal(83, scan.Entries.Select(e => e.Target.Token).Distinct().Count());
        Assert.NotEmpty(previous!.Containers!);
    }

    [Fact]
    public void CompletePatternlessRowFoldsBeforeItsFirstPublicationAndKeepsCompoundMetadata() {
        var root = new Node(1, false);
        var row = new Node(2);
        row.Data = row.Data with { ControlType = 50007, Capabilities = HintCapabilities.None };
        var button = new Node(3); button.Data = button.Data with { Bounds = row.Data.Bounds };
        row.Add(button); root.Add(row);
        var scan = new ProgressiveHintDiscovery<Node>(root, new Tree(), Region, 7, 8);
        while (!scan.IsComplete) {
            scan.Step(1000); Check(scan);
            Assert.DoesNotContain(scan.Entries, e => e.Target.RuntimeId[1] == 2);
        }
        Assert.Equal(3, Assert.Single(scan.Entries).Target.RuntimeId[1]);
    }

    [Fact]
    public void InvalidActionableAncestorDoesNotHideValidDescendantsDuringCanonicalization() {
        var root = new Node(1, false);
        var parent = new Node(2);
        var invalid = new Node(3);
        invalid.Data = invalid.Data with { Bounds = default };
        invalid.Add(new Node(4)); parent.Add(invalid); root.Add(parent);
        var scan = new ProgressiveHintDiscovery<Node>(root, new Tree(), Region, 7, 8);
        Complete(scan); Check(scan);
        Assert.Equal([4, 2], scan.Entries.Select(entry => entry.Target.RuntimeId[1]));
        Assert.Equal([2, 4], scan.Snapshot().Entries.Select(entry => entry.Target.RuntimeId[1]));
        Assert.Equal(1, scan.Omitted);
    }

    [Fact]
    public void FailedChildEnumerationKeepsKnownDescendantsWithoutClaimingCompleteAncestry() {
        var root = new Node(1, false);
        var parent = new Node(2);
        var child = new Node(3);
        child.Add(new Node(4)); parent.Add(child); root.Add(parent);
        var scan = new ProgressiveHintDiscovery<Node>(root, new Tree { FailingSiblingId = 3 }, Region, 7, 8);
        Complete(scan); Check(scan);
        Assert.Equal([2, 4, 3], scan.Entries.Select(entry => entry.Target.RuntimeId[1]));
        var snapshot = scan.Snapshot();
        Assert.Equal([2, 3, 4], snapshot.Entries.Select(entry => entry.Target.RuntimeId[1]));
        Assert.Equal(0, snapshot.Entries[0].Target.ContainerId);
        Assert.DoesNotContain(snapshot.Containers, container => container.TargetToken == snapshot.Entries[0].Target.Token);
        Assert.Equal(1, scan.Omitted);
    }

    [Fact]
    public void EnumerationAndVisitedNodeLimitsStayBoundedWithoutDroppingAlreadyDiscoveredLeaves() {
        var root = new Node(1, false);
        root.Add(Enumerable.Range(2, ElementHintProtocol.MaxNodes + 1).Select(i => new Node(i, false)).ToArray());
        var tree = new Tree();
        var scan = new ProgressiveHintDiscovery<Node>(root, tree, Region, 7, 8);
        Complete(scan); Check(scan);
        Assert.Equal(ElementHintProtocol.MaxNodes, scan.Visited);
        Assert.Equal(ElementHintProtocol.MaxNodes, tree.Reads.Count);
        Assert.Contains("Node limit", scan.Reason);
        Assert.True(tree.NavigationCalls <= ElementHintProtocol.MaxNodes * 2);
    }
}

public sealed class HintTreeCacheTests {
    private sealed class Wrapper(ProgressiveHintDiscoveryTests.Node value) {
        public ProgressiveHintDiscoveryTests.Node Value { get; } = value;
    }
    private sealed class WrapperTree : IHintTree<Wrapper> {
        public List<int> Reads { get; } = [];
        public HintNode Read(Wrapper element) { Reads.Add(element.Value.Data.RuntimeId[1]); return element.Value.Data; }
        public int[] RuntimeId(Wrapper element) => element.Value.Data.RuntimeId;
        public Wrapper? FirstChild(Wrapper element) => Wrap(element.Value.Children.FirstOrDefault());
        public Wrapper? NextSibling(Wrapper element) => Wrap(element.Value.Next);
        public Wrapper? Parent(Wrapper element) => Wrap(element.Value.Parent);
        public bool IsBranchFailure(Exception exception) => exception is InvalidOperationException;
        private static Wrapper? Wrap(ProgressiveHintDiscoveryTests.Node? value) => value is null ? null : new(value);
    }

    [Fact]
    public void CachedRuntimeIdentityReusesUnrelatedPanesWhenNavigationReturnsNewWrappers() {
        var root = new ProgressiveHintDiscoveryTests.Node(1, false);
        var left = new ProgressiveHintDiscoveryTests.Node(2, false); left.Add(new ProgressiveHintDiscoveryTests.Node(3));
        var right = new ProgressiveHintDiscoveryTests.Node(4, false); right.Add(new ProgressiveHintDiscoveryTests.Node(5));
        root.Add(left, right);
        var source = new WrapperTree();
        var cache = new HintTreeCache<Wrapper>(source, cachedIdentity: element => element.Value.Data.RuntimeId);
        var rootWrapper = new Wrapper(root);
        void Scan() {
            cache.Refresh();
            var scan = new ProgressiveHintDiscovery<Wrapper>(rootWrapper, cache,
                new(0, 0, 1000, 100000), 7, 8);
            while (!scan.IsComplete) { scan.Step(1000); }
        }
        Scan();
        cache.MarkDirty(right.Children[0].Data.RuntimeId);
        Scan();
        Assert.Equal([4, 5], source.Reads.Skip(5));
        Assert.True(cache.Hits > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BatchedPaneAndChildChangesKeepUnrelatedPaneCached(bool childFirst) {
        var root = new ProgressiveHintDiscoveryTests.Node(1, false);
        var left = new ProgressiveHintDiscoveryTests.Node(2, false); left.Add(new ProgressiveHintDiscoveryTests.Node(3));
        var right = new ProgressiveHintDiscoveryTests.Node(4, false); right.Add(new ProgressiveHintDiscoveryTests.Node(5));
        root.Add(left, right);
        var source = new WrapperTree();
        var cache = new HintTreeCache<Wrapper>(source, () => 0, element => element.Value.Data.RuntimeId);
        var rootWrapper = new Wrapper(root);
        ProgressiveHintDiscovery<Wrapper> Scan() {
            cache.Refresh();
            var scan = new ProgressiveHintDiscovery<Wrapper>(rootWrapper, cache,
                new(0, 0, 1000, 100000), 7, 8);
            while (!scan.IsComplete) { scan.Step(1000); }
            return scan;
        }
        Scan();
        var child = right.Children[0];
        child.Data = child.Data with { Enabled = false };
        ProgressiveHintDiscoveryTests.Node[] changes = childFirst ? [child, right] : [right, child];
        foreach (var change in changes) { cache.MarkDirty(change.Data.RuntimeId); }
        var refreshed = Scan();
        Assert.Equal([4, 5], source.Reads.Skip(5));
        Assert.Equal(3, Assert.Single(refreshed.Entries).Target.RuntimeId[1]);
        int beforeUnknown = source.Reads.Count;
        cache.MarkDirty(right.Data.RuntimeId);
        cache.MarkDirty([42, 999]);
        Scan();
        Assert.Equal([1, 2, 3, 4, 5], source.Reads.Skip(beforeUnknown).Order());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverlappingDirtyPaneRootsInvalidateTheAncestorOnceAndKeepUnrelatedPanes(bool descendantFirst) {
        var root = new ProgressiveHintDiscoveryTests.Node(1, false);
        var left = new ProgressiveHintDiscoveryTests.Node(2, false); left.Add(new ProgressiveHintDiscoveryTests.Node(3));
        var outer = new ProgressiveHintDiscoveryTests.Node(4, false);
        var inner = new ProgressiveHintDiscoveryTests.Node(5, false); inner.Add(new ProgressiveHintDiscoveryTests.Node(7));
        outer.Add(inner, new(6)); root.Add(left, outer);
        var source = new ProgressiveHintDiscoveryTests.Tree();
        var cache = new HintTreeCache<ProgressiveHintDiscoveryTests.Node>(source, () => 0);
        void Scan() {
            cache.Refresh();
            var scan = new ProgressiveHintDiscovery<ProgressiveHintDiscoveryTests.Node>(root, cache,
                new(0, 0, 1000, 100000), 7, 8);
            while (!scan.IsComplete) { scan.Step(1000); }
        }
        Scan();
        ProgressiveHintDiscoveryTests.Node[] changes = descendantFirst ? [inner, outer] : [outer, inner];
        foreach (var change in changes) { cache.MarkDirty(change.Data.RuntimeId); }
        Scan();
        Assert.Equal([4, 5, 6, 7], source.Reads.Skip(7));
    }

    [Fact]
    public void PerWindowCacheRetentionNeverExceedsTheNodeLimit() {
        var source = new ProgressiveHintDiscoveryTests.Tree();
        var cache = new HintTreeCache<ProgressiveHintDiscoveryTests.Node>(source);
        for (int i = 0; i < ElementHintProtocol.MaxNodes * 2 + 1; i++) {
            cache.Read(new(i));
            Assert.InRange(cache.Count, 1, ElementHintProtocol.MaxNodes);
        }
    }

    [Fact]
    public void PaneInvalidationReusesUnrelatedPaneAndUnknownEventsInvalidateTheWindow() {
        var root = new ProgressiveHintDiscoveryTests.Node(1, false);
        var left = new ProgressiveHintDiscoveryTests.Node(2, false); left.Add(new ProgressiveHintDiscoveryTests.Node(3));
        var right = new ProgressiveHintDiscoveryTests.Node(4, false); right.Add(new ProgressiveHintDiscoveryTests.Node(5));
        root.Add(left, right);
        var source = new ProgressiveHintDiscoveryTests.Tree();
        var cache = new HintTreeCache<ProgressiveHintDiscoveryTests.Node>(source);
        void Scan() {
            cache.Refresh();
            var scan = new ProgressiveHintDiscovery<ProgressiveHintDiscoveryTests.Node>(root, cache,
                new(0, 0, 1000, 100000), 7, 8);
            while (!scan.IsComplete) { scan.Step(1000); }
        }
        Scan(); Assert.Equal(5, source.Reads.Count);
        Scan(); Assert.Equal(5, source.Reads.Count); Assert.True(cache.Hits > 0);
        cache.MarkDirty(right.Children[0].Data.RuntimeId);
        Scan();
        Assert.Equal([4, 5], source.Reads.Skip(5));
        cache.MarkDirty([42, 999]);
        Scan(); Assert.Equal(12, source.Reads.Count);
    }

    [Fact]
    public void CacheAgeAndWindowLruBoundRetentionIncludingDisabledReuse() {
        long now = 0;
        var source = new ProgressiveHintDiscoveryTests.Tree();
        var tree = new HintTreeCache<ProgressiveHintDiscoveryTests.Node>(source, () => now);
        var node = new ProgressiveHintDiscoveryTests.Node(1);
        tree.Read(node); tree.Read(node);
        Assert.Single(source.Reads);
        now = ElementHintProtocol.CacheMaxAgeMs;
        tree.Refresh(); tree.Read(node);
        Assert.Equal(2, source.Reads.Count);
        var windows = new HintWindowCache<ProgressiveHintDiscoveryTests.Node>(2);
        var retired = new List<HintTreeCache<ProgressiveHintDiscoveryTests.Node>>();
        HintTreeCache<ProgressiveHintDiscoveryTests.Node> Create() => new(source);
        var first = windows.Get(new(1, 42, 1), Create, retired.Add);
        var second = windows.Get(new(2, 42, 1), Create, retired.Add);
        Assert.Same(first, windows.Get(new(1, 42, 1), Create, retired.Add));
        windows.Get(new(3, 42, 1), Create, retired.Add);
        Assert.Same(second, Assert.Single(retired));
        Assert.Equal(2, windows.Count);
        windows.Clear(retired.Add); Assert.Equal(0, windows.Count); Assert.Equal(3, retired.Count);
        var disabled = new HintWindowCache<ProgressiveHintDiscoveryTests.Node>(0);
        Assert.NotSame(disabled.Get(new(1, 42, 1), Create, retired.Add),
            disabled.Get(new(1, 42, 1), Create, retired.Add));
        Assert.Equal(0, disabled.Count);
    }
}
