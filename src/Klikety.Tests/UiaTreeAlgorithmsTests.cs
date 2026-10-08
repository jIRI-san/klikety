using Klikety.Automation;
using Klikety.UiaWorker;

namespace Klikety.Tests;

public class UiaTreeAlgorithmsTests {
    private static readonly HintRect Region = new(0, 0, 100, 100);
    private sealed class Node(HintNode data) {
        public HintNode Data { get; set; } = data;
        public Node? Child { get; private set; }
        public Node? Next { get; set; }
        public Node? Parent { get; set; }
        public Exception? Failure { get; set; }
        public void Add(params Node[] children) {
            Child = children[0];
            for (int i = 0; i < children.Length; i++) {
                children[i].Parent = this;
                if (i + 1 < children.Length) { children[i].Next = children[i + 1]; }
            }
        }
    }
    private sealed class Tree : IHintTree<Node> {
        public int Reads { get; private set; }
        public int Parents { get; private set; }
        public HashSet<Node> SiblingQueries { get; } = [];
        public HintNode Read(Node element) {
            Reads++;
            if (element.Failure is { } failure) { throw failure; }
            return element.Data;
        }
        public int[] RuntimeId(Node element) => element.Data.RuntimeId;
        public Node? FirstChild(Node element) => element.Child;
        public Node? NextSibling(Node element) { SiblingQueries.Add(element); return element.Next; }
        public Node? Parent(Node element) { Parents++; return element.Parent; }
        public bool IsBranchFailure(Exception exception) => exception is InvalidOperationException;
    }
    private static Node Element(int id, int type = 50000, int pid = 42, HintCapabilities capabilities = HintCapabilities.None) =>
        new(new([42, id], pid, type, capabilities, new(10, 10, 80, 80)));
    private static HintDiscovery<Node> Scan(Node root, Tree tree) => UiaTreeAlgorithms.Discover(root, tree, Region, 7, 8);
    private static HintTarget Target(Node node) => new(1, node.Data.RuntimeId, node.Data.ProcessId,
        node.Data.ControlType, node.Data.Capabilities, node.Data.Bounds, node.Data.Bounds, node.Data.Bounds.Center);

    [Fact]
    public void RootOnlyRetainsIndependentNestedAndCrossProcessTargetsWithoutRectangleDedup() {
        var root = Element(1, 50032);
        var parent = Element(2, 50025, capabilities: HintCapabilities.Invoke);
        var nested = Element(3);
        var text = Element(4, 50020);
        parent.Add(nested, text);
        var duplicate = Element(3);
        var readonlyEdit = Element(5, 50004);
        var crossProcess = Element(6, pid: 99);
        var owner = Element(7, pid: 7);
        var worker = Element(8, pid: 8);
        var disabled = Element(9); disabled.Data = disabled.Data with { Enabled = false };
        var offscreen = Element(10); offscreen.Data = offscreen.Data with { Offscreen = true };
        root.Add(parent, duplicate, readonlyEdit, crossProcess, owner, worker, disabled, offscreen);
        root.Next = Element(999);
        var tree = new Tree();
        var result = Scan(root, tree);
        Assert.Null(result.Reason);
        Assert.Equal(0, result.Omitted);
        Assert.Equal([2, 3, 5, 6], result.Entries.Select(e => e.Target.RuntimeId[1]));
        Assert.Equal(99, result.Entries[^1].Target.ProcessId);
        Assert.DoesNotContain(root, tree.SiblingQueries);
        Assert.All(result.Entries, e => Assert.Equal(Region.Clip(e.Target.Bounds), e.Target.VisibleBounds));
    }

    [Theory]
    [InlineData(HintCapabilities.Invoke)]
    [InlineData(HintCapabilities.Toggle)]
    [InlineData(HintCapabilities.Selection)]
    [InlineData(HintCapabilities.Expand)]
    [InlineData(HintCapabilities.Value)]
    public void EveryAdvertisedInteractivePatternIncludesCustomControls(HintCapabilities capabilities) {
        var root = Element(1, 50032);
        root.Add(Element(2, 50025, capabilities: capabilities));
        Assert.Equal(capabilities, Assert.Single(Scan(root, new Tree()).Entries).Target.Capabilities);
    }

    [Fact]
    public void ClippingExcludesOutsideScopeAndBadIdentityOrGeometryIsExplicitlyPartial() {
        var root = Element(1, 50032);
        var clipped = Element(2); clipped.Data = clipped.Data with { Bounds = new(-20, -20, 40, 40) };
        var outside = Element(3); outside.Data = outside.Data with { Bounds = new(200, 200, 40, 40) };
        var badGeometry = Element(4); badGeometry.Data = badGeometry.Data with { Bounds = new(double.NaN, 0, 10, 10) };
        var missingId = Element(5); missingId.Data = missingId.Data with { RuntimeId = [] };
        var tooLong = Element(6); tooLong.Data = tooLong.Data with { RuntimeId = new int[65] };
        var missingPid = Element(7); missingPid.Data = missingPid.Data with { ProcessId = 0 };
        root.Add(clipped, outside, badGeometry, missingId, tooLong, missingPid);
        var result = Scan(root, new Tree());
        Assert.Equal(new HintRect(0, 0, 20, 20), Assert.Single(result.Entries).Target.VisibleBounds);
        Assert.Equal(4, result.Omitted);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public void NodeCapStopsAtExactly20000WithoutReadingAnUnboundedSiblingList() {
        var root = Element(1, 50032);
        root.Add(Enumerable.Range(2, 20001).Select(i => Element(i, 50020)).ToArray());
        var tree = new Tree();
        var result = Scan(root, tree);
        Assert.Equal(20000, result.Visited);
        Assert.Equal(20000, tree.Reads);
        Assert.Empty(result.Entries);
        Assert.Equal("Node limit reached", result.Reason);
        Assert.Equal(1, result.Omitted);
    }

    [Fact]
    public void TargetCapRetainsExactly2000AndReportsOmittedBranch() {
        var root = Element(1, 50032);
        root.Add(Enumerable.Range(2, 2002).Select(i => Element(i)).ToArray());
        var result = Scan(root, new Tree());
        Assert.Equal(2000, result.Entries.Count);
        Assert.Equal(2002, result.Visited);
        Assert.Equal("Target limit reached", result.Reason);
        Assert.Equal(1, result.Omitted);
    }

    [Fact]
    public void DepthCapIncludesDepth64ButDoesNotReadDepth65() {
        var root = Element(1, 50032);
        var parent = root;
        for (int depth = 1; depth <= 66; depth++) {
            var child = Element(depth + 1);
            parent.Add(child); parent = child;
        }
        var tree = new Tree();
        var result = Scan(root, tree);
        Assert.Equal(65, tree.Reads);
        Assert.Equal(64, result.Entries.Count);
        Assert.Equal("Depth limit reached", result.Reason);
        Assert.Equal(1, result.Omitted);
    }

    [Fact]
    public void ProviderBranchFailureIsPartialButRootAndUnexpectedFailuresAreNotSwallowed() {
        var root = Element(1, 50032);
        var bad = Element(3); bad.Failure = new InvalidOperationException("Unavailable branch");
        root.Add(Element(2), bad, Element(4));
        var result = Scan(root, new Tree());
        Assert.Single(result.Entries);
        Assert.Equal(3, result.Visited);
        Assert.Equal(1, result.Omitted);
        Assert.Equal("Provider branch unavailable", result.Reason);
        root.Failure = new InvalidOperationException("Unavailable root");
        Assert.Throws<InvalidOperationException>(() => Scan(root, new Tree()));
        root.Failure = null; bad.Failure = new ArgumentException("Unexpected failure");
        Assert.Throws<ArgumentException>(() => Scan(root, new Tree()));
    }

    [Fact]
    public void NestedPassiveTextBelongsToItsActionButIndependentActionsDoNot() {
        var parent = Element(1);
        var text = Element(2, 50020);
        var childAction = Element(3, 50025, capabilities: HintCapabilities.Toggle);
        parent.Add(text, childAction);
        var tree = new Tree();
        Assert.True(UiaTreeAlgorithms.OwnsHit(text, parent.Data.RuntimeId, tree));
        Assert.False(UiaTreeAlgorithms.OwnsHit(childAction, parent.Data.RuntimeId, tree));
        Assert.True(UiaTreeAlgorithms.OwnsHit(childAction, childAction.Data.RuntimeId, tree));
        Assert.True(UiaTreeAlgorithms.DescendsFrom(childAction, parent.Data.RuntimeId, tree));
        Assert.False(UiaTreeAlgorithms.DescendsFrom(childAction, [999], tree));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CoveredCenterUsesOnlyBoundedVerifiedInteriorPoints(bool completelyCovered) {
        var target = Element(1);
        var blocker = Element(2);
        var attempts = new List<HintPoint>();
        var point = UiaTreeAlgorithms.VerifiedPoint(target.Data.Bounds, target.Data.Bounds.Center,
            p => { attempts.Add(p); return !completelyCovered; },
            p => p == target.Data.Bounds.Center ? blocker : target, target.Data.RuntimeId, new Tree());
        Assert.Equal(completelyCovered ? null : new HintPoint(30, 30), point);
        Assert.Equal(completelyCovered ? 5 : 2, attempts.Count);
        Assert.Equal(attempts.Distinct(), attempts);
        Assert.All(attempts, p => Assert.True(target.Data.Bounds.Contains(p)));
    }

    [Fact]
    public void OutsideProviderPointAndForeignNativeWindowAreNeverAccepted() {
        var target = Element(1);
        int hitReads = 0;
        var point = UiaTreeAlgorithms.VerifiedPoint(target.Data.Bounds, new(999, 999), _ => false,
            _ => { hitReads++; return target; }, target.Data.RuntimeId, new Tree());
        Assert.Null(point);
        Assert.Equal(0, hitReads);
    }

    [Fact]
    public void ReusedWindowIdentityChangedBoundsAndActualForegroundLossRejectIndependently() {
        var expected = new HintRootIdentity(42, 100, [42, 1], Region);
        Assert.Null(UiaTreeAlgorithms.RootChanged(expected, expected, 123, 123));
        Assert.Equal("Application identity changed", UiaTreeAlgorithms.RootChanged(expected, expected with { ProcessId = 99 }, 123, 123));
        Assert.Equal("Application identity changed", UiaTreeAlgorithms.RootChanged(expected, expected with { ProcessStart = 101 }, 123, 123));
        Assert.Equal("Application identity changed", UiaTreeAlgorithms.RootChanged(expected, expected with { RuntimeId = [42, 2] }, 123, 123));
        Assert.Equal("Application bounds changed", UiaTreeAlgorithms.RootChanged(expected, expected with { Bounds = new(1, 0, 100, 100) }, 123, 123));
        Assert.Equal("Application lost foreground", UiaTreeAlgorithms.RootChanged(expected, expected, 123, 999));
    }

    [Fact]
    public void TargetStateIdentityAndGeometryChangesRejectWithoutRetargeting() {
        var node = Element(1);
        var target = Target(node);
        Assert.False(UiaTreeAlgorithms.TargetChanged(target, node.Data));
        foreach (var changed in new[] {
            node.Data with { Enabled = false }, node.Data with { Offscreen = true },
            node.Data with { ProcessId = 99 }, node.Data with { RuntimeId = [42, 2] },
            node.Data with { Bounds = new(11, 10, 80, 80) },
        }) { Assert.True(UiaTreeAlgorithms.TargetChanged(target, changed)); }
    }

    [Fact]
    public void CyclicOrTooDeepHitAncestryCannotHangValidation() {
        var node = Element(1, 50020); node.Parent = node;
        var tree = new Tree();
        Assert.False(UiaTreeAlgorithms.OwnsHit(node, [999], tree));
        Assert.Equal(65, tree.Parents);
        var ancestry = new Tree();
        Assert.False(UiaTreeAlgorithms.DescendsFrom(node, [999], ancestry));
        Assert.Equal(65, ancestry.Parents);
    }
}
