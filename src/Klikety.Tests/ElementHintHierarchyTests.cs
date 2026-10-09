using System.Drawing;
using System.IO;

using Klikety.Automation;
using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Navigation;

namespace Klikety.Tests;

public class ElementHintHierarchyTests {
    private static HintTarget Target(int token, int container = 0, int type = 50000,
        HintCapabilities capabilities = HintCapabilities.Invoke, HintRect? bounds = null) {
        var rect = bounds ?? new HintRect(token * 20, token * 30, 18, 24);
        return new(token, [42, token], 42, type, capabilities, rect, rect, rect.Center, container);
    }
    private static IEnumerable<HintTarget> Leaves(IEnumerable<HintEntry> entries) =>
        entries.SelectMany(e => e.Target is { } target ? [target] : Leaves(e.Children));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(11, 2)]
    [InlineData(250, 100)]
    [InlineData(2000, 2)]
    [InlineData(2000, 100)]
    public void CapacityGroupsPreserveEveryOriginalTargetExactlyOnce(int count, int capacity) {
        var targets = Enumerable.Range(1, count).Select(i => Target(i)).ToArray();
        var entries = new ElementHintHierarchy(targets, []).Build(targets, capacity);
        Assert.Equal(targets, Leaves(entries));
        void Check(IEnumerable<HintEntry> level) {
            Assert.InRange(level.Count(), 1, capacity);
            foreach (var group in level.Where(e => e.IsGroup)) {
                Assert.True(group.Id < 0);
                Assert.Null(group.Target);
                Check(group.Children);
            }
        }
        Check(entries);
        if (count == 250) {
            Assert.Equal(3, entries.Count);
            Assert.Equal([100, 100, 50], entries.Select(e => e.Children.Count));
        }
    }

    [Fact]
    public void CoincidentSiblingsAreNotMergedWithoutProvenAncestry() {
        var bounds = new HintRect(10, 10, 100, 40);
        var targets = new[] { Target(1, bounds: bounds), Target(2, bounds: bounds) };
        var entries = new ElementHintHierarchy(targets, []).Build(targets, 100);
        Assert.All(entries, e => Assert.False(e.IsGroup));
        Assert.Equal(targets, Leaves(entries));
    }

    [Fact]
    public void CompoundPickerKeepsParentActionAndDistinctChildrenEvenInPartialSnapshot() {
        var bounds = new HintRect(10, 10, 300, 40);
        var targets = new[] {
            Target(1, 1, 50024, HintCapabilities.Selection | HintCapabilities.Expand, bounds),
            Target(2, 1, bounds: bounds),
            Target(3, 1, bounds: new(15, 15, 20, 20))
        };
        var containers = new[] { new HintContainer(1, 0, 1, 42, 50024, bounds) };
        var entries = new ElementHintHierarchy(targets, containers).Build(targets, 100);
        var group = Assert.Single(entries);
        Assert.True(group.IsGroup);
        Assert.True(group.Compact);
        Assert.Equal(targets, Leaves(group.Children));
        Assert.Contains("select/expand", group.Children[0].Description);
        Assert.Contains("invoke", group.Children[1].Description);
        Assert.All(group.Children, e => Assert.False(e.IsGroup));
    }

    [Fact]
    public void NestedCompoundsHaveFiniteLevelsAndRetainBothParents() {
        var bounds = new HintRect(10, 10, 100, 40);
        var targets = new[] { Target(1, 1, bounds: bounds), Target(2, 2, bounds: bounds), Target(3, 2, bounds: bounds) };
        var containers = new[] {
            new HintContainer(1, 0, 1, 42, 50000, bounds), new HintContainer(2, 1, 2, 42, 50000, bounds)
        };
        var group = Assert.Single(new ElementHintHierarchy(targets, containers).Build(targets, 100));
        Assert.Equal(targets, Leaves(group.Children));
        Assert.Equal(2, group.Children.Count);
        Assert.Equal(1, group.Children[0].Target!.Token);
        Assert.Equal([2, 3], Leaves(group.Children[1].Children).Select(t => t.Token));
    }

    [Fact]
    public void LogicalContainersGroupEvenWhenFlatLabelsFit() {
        var targets = Enumerable.Range(1, 8).Select(i => Target(i, i <= 4 ? 1 : 2)).ToArray();
        var containers = new[] {
            new HintContainer(1, 0, 0, 42, 50026, new(0, 0, 200, 200)),
            new HintContainer(2, 0, 0, 42, 50026, new(0, 200, 200, 200))
        };
        var logical = new ElementHintHierarchy(targets, containers).Build(targets, 100);
        Assert.Equal(2, logical.Count);
        Assert.All(logical, e => Assert.True(e.IsGroup));
        var grouped = new ElementHintHierarchy(targets, containers).Build(targets, 4);
        Assert.Equal(2, grouped.Count);
        Assert.Equal(targets, Leaves(grouped));
        Assert.All(grouped, e => Assert.Equal(4, e.Children.Count));
    }

    private sealed class Renderer(int capacity) : IElementHintsRenderer {
        public int Capacity { get; set; } = capacity;
        public HintLevelView? Last { get; private set; }
        public int Invalid { get; private set; }
        public int GetPageCapacity(Rectangle region, int keyCapacity, bool singleKey = false) => Math.Min(Capacity, keyCapacity);
        public void RebuildLabels(IKeyLabelResolver resolver) { }
        public void Render(HintLevelView view) => Last = view;
        public void FlashInvalidKey() => Invalid++;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SmallLevelsUseOneKeyAndOptionalArrowsNeverPage(bool arrows) {
        var renderer = new Renderer(100);
        var service = new FakeElementHintService { Response = FakeElementHintService.Result(3) };
        var session = new ElementHintsSession([VKey.A, VKey.S, VKey.D], [VKey.Q, VKey.W],
            new ActionMapper([]), new(1, 1), service, renderer, arrowKeys: arrows);
        int actions = 0;
        session.ActionRequested += (_, _) => actions++;
        try {
            session.Activate(new(0, 0, 1000, 1000), default);
            Assert.True(session.HelpState.SingleKey);
            Assert.All(session.Labels, l => Assert.Null(l.Second));
            session.OnKey(VKey.S);
            Assert.Equal(2, session.Selected!.Token);
            session.OnKey(VKey.Q);
            Assert.Equal(1, renderer.Invalid);
            session.OnKey(VKey.Right);
            Assert.Equal(arrows ? 3 : 2, session.Selected.Token);
            Assert.Equal(0, session.Page);
            session.OnKey(VKey.Space);
            Assert.Equal(1, actions);
            Assert.Empty(service.Validations);
        } finally { session.Deactivate(); }
    }

    [Fact]
    public void TwoHundredFiftyTargetsUseThreeSingleKeyGroupsWithoutPagesOrGroupActions() {
        var config = new ConfigModel();
        var renderer = new Renderer(100);
        var service = new FakeElementHintService { Response = FakeElementHintService.Result(250) };
        var session = new ElementHintsSession(config.HorizontalKeys, config.VerticalKeys, new ActionMapper([]),
            new(1, 1), service, renderer);
        int actions = 0, moves = 0;
        session.ActionRequested += (_, _) => actions++;
        session.CursorMoveRequested += _ => moves++;
        try {
            session.Activate(new(0, 0, 1000, 1000), default);
            Assert.Equal(3, session.Labels.Count);
            Assert.True(session.HelpState.SingleKey);
            session.OnKey(VKey.Right);
            Assert.True(session.HelpState.FocusedGroup);
            session.OnKey(VKey.Space);
            Assert.Equal(0, actions);
            Assert.Equal(0, moves);
            session.OnKey(VKey.D3);
            Assert.Equal(2, session.Depth);
            Assert.Equal(50, session.Labels.Count);
            Assert.False(session.HelpState.SingleKey);
            Assert.Equal(1, session.PageCount);
            session.OnKey(VKey.A); session.OnKey(VKey.Q);
            Assert.Equal(201, session.Selected!.Token);
            Assert.Equal(1, moves);
            session.OnKey(VKey.Escape);
            Assert.Equal(1, session.Depth);
            Assert.True(session.HelpState.FocusedGroup);
            Assert.Null(session.Selected);
            Assert.Equal(VKey.D3, session.Labels[2].First);
            Assert.Empty(service.Validations);
        } finally { session.Deactivate(); }
    }

    [Fact]
    public void OneSlotFallbackPagesWithoutArrowsAndClearsSelection() {
        var renderer = new Renderer(1);
        var service = new FakeElementHintService { Response = FakeElementHintService.Result(3) };
        var session = new ElementHintsSession([VKey.A, VKey.S], [VKey.Q, VKey.W],
            new ActionMapper([]), new(1, 1), service, renderer, arrowKeys: false);
        try {
            session.Activate(new(0, 0, 1000, 1000), default);
            Assert.Equal(3, session.PageCount);
            session.OnKey(VKey.Next);
            session.OnKey(VKey.A); session.OnKey(VKey.Q);
            Assert.Equal(2, session.Selected!.Token);
            session.OnKey(VKey.Left);
            Assert.Equal(1, session.Page);
            session.OnKey(VKey.Prior);
            Assert.Equal(0, session.Page);
            Assert.Null(session.Selected);
            Assert.Null(session.Prefix);
        } finally { session.Deactivate(); }
    }

    [Fact]
    public async Task PartialCompoundUsesCompactPickerAndOnlyOriginalTokenIsValidated() {
        var bounds = new HintRect(10, 10, 100, 40);
        var targets = new[] { Target(1, 1, 50024, HintCapabilities.Selection, bounds), Target(2, 1, bounds: bounds) };
        var service = new FakeElementHintService {
            Response = FakeElementHintService.Result(0, HintOutcome.Partial) with {
                Targets = targets,
                Containers = [new(1, 0, 1, 42, 50024, bounds)]
            }
        };
        var renderer = new Renderer(100);
        var session = new ElementHintsSession([VKey.A, VKey.S], [VKey.Q, VKey.W],
            new ActionMapper([]), new(1, 1), service, renderer);
        int cancel = 0, fallback = 0;
        session.Cancelled += () => cancel++;
        session.GridFallbackRequested += () => fallback++;
        try {
            session.Activate(new(0, 0, 1000, 1000), default);
            var rejected = await session.ValidateAsync(CancellationToken.None);
            Assert.Equal(HintOutcome.StaleTarget, rejected.Outcome);
            Assert.Empty(service.Validations);
            session.OnKey(VKey.D1);
            Assert.True(renderer.Last!.Compact);
            Assert.True(session.HelpState.SingleKey);
            session.OnKey(VKey.S);
            Assert.Equal(2, session.Selected!.Token);
            await session.ValidateAsync(CancellationToken.None, moveOnly: true);
            Assert.Equal([2], service.Validations);
            Assert.Equal([true], service.ValidationMoveOnlyFlags);
            var labels = session.Labels;
            session.Redraw();
            Assert.Equal(labels, session.Labels);
            session.OnKey(VKey.Return);
            Assert.Equal(1, fallback);
            session.OnKey(VKey.Escape);
            Assert.Equal(1, session.Depth);
            Assert.Null(session.Selected);
            session.OnKey(VKey.Escape);
            Assert.Equal(1, cancel);
        } finally { session.Deactivate(); }
    }

    [Fact]
    public void SpatialOverflowSeparatesInterleavedLeftAndRightPanels() {
        var targets = Enumerable.Range(0, 8).Select(i => Target(i + 1,
            bounds: new(i % 2 * 800, i / 2 * 30, 20, 20))).ToArray();
        var groups = new ElementHintHierarchy(targets, []).Build(targets, 4);
        Assert.Equal(2, groups.Count);
        Assert.Equal([1, 3, 5, 7], Leaves(groups[0].Children).Select(t => t.Token));
        Assert.Equal([2, 4, 6, 8], Leaves(groups[1].Children).Select(t => t.Token));
        Assert.All(groups, group => Assert.Equal(20, group.Bounds.Width));
    }

    [Fact]
    public void GroupDigitsPageIndependentlyWithoutCollidingWithControlLabels() {
        var targets = Enumerable.Range(1, 20).SelectMany(i => {
            var bounds = new HintRect(i * 30, 20, 20, 20);
            return new[] { Target(i * 2 - 1, i, bounds: bounds), Target(i * 2, i, bounds: bounds) };
        }).Append(Target(41)).ToArray();
        var containers = Enumerable.Range(1, 20).Select(i =>
            new HintContainer(i, 0, i * 2 - 1, 42, 50000, targets[(i - 1) * 2].Bounds)).ToArray();
        var service = new FakeElementHintService {
            Response = FakeElementHintService.Result(0) with { Targets = targets, Containers = containers }
        };
        var config = new ConfigModel();
        var session = new ElementHintsSession(config.HorizontalKeys, config.VerticalKeys,
            new ActionMapper([]), new(1, 1), service, null);
        try {
            session.Activate(new(0, 0, 1000, 1000), default);
            Assert.Equal(3, session.PageCount);
            Assert.Equal(9, session.Labels.Count(l => l.Entry.IsGroup));
            var leaf = Assert.Single(session.Labels, l => !l.Entry.IsGroup);
            Assert.Equal(VKey.A, leaf.First);
            session.OnKey(VKey.A);
            Assert.Equal(41, session.Selected!.Token);
            session.OnKey(VKey.D9);
            Assert.Equal(2, session.Depth);
            Assert.Null(session.Selected);
            session.OnKey(VKey.Escape);
            session.OnKey(VKey.Next);
            Assert.All(session.Labels, l => Assert.True(l.Entry.IsGroup));
            Assert.Equal(Enumerable.Range(0, 9).Select(i => (VKey)((int)VKey.D1 + i)), session.Labels.Select(l => l.First));
            session.OnKey(VKey.Next);
            Assert.Equal([VKey.D1, VKey.D2], session.Labels.Select(l => l.First));
        } finally { session.Deactivate(); }
    }

    [Fact]
    public void ProtocolRejectsDanglingCyclicForeignAndIncompleteContainerMetadata() {
        var bounds = new HintRect(10, 10, 100, 40);
        var targets = new[] { Target(1, 1, bounds: bounds), Target(2, 1, bounds: bounds) };
        var request = new HintRequest(1, Guid.NewGuid(), Guid.NewGuid(), HintCommand.Discover, Region: new(0, 0, 1000, 1000));
        var response = new HintResponse(1, request.SessionId, request.RequestId, HintOutcome.Success,
            targets, Containers: [new(1, 0, 1, 42, 50000, bounds)]);
        ElementHintProtocol.CheckResponse(request, response);
        foreach (var invalid in new[] {
            response with { Containers = null },
            response with { Containers = [new(1, 1, 1, 42, 50000, bounds)] },
            response with { Containers = [new(1, 2, 1, 42, 50000, bounds)] },
            response with { Containers = [new(1, 0, 1, 99, 50000, bounds)] },
            response with { Containers = [new(1, 0, 3, 42, 50000, bounds)] },
            response with { Containers = [new(1, 0, 1, 42, 50000, new(1, 1, 1, 1))] },
            response with { Targets = targets[..1] },
            response with { Targets = [targets[0], targets[1] with { ContainerId = 99 }] }
        }) {
            Assert.Throws<InvalidDataException>(() => ElementHintProtocol.CheckResponse(request, invalid));
        }
    }
}
