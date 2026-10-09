using System.Drawing;
using System.Runtime.CompilerServices;

using Klikety.Automation;
using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Navigation;
using Klikety.Tests.Fakes;

using Microsoft.Extensions.Logging;

namespace Klikety.Tests;

public sealed class ProgressiveElementHintsSessionTests {
    private sealed class Service : IElementHintService {
        public HintResponse First { get; set; } = FakeElementHintService.Result(1) with { IsComplete = false };
        public TaskCompletionSource<HintResponse> Next { get; } = new();
        public IReadOnlyList<HintResponse> More { get; init; } = [];
        public TaskCompletionSource<HintResponse> Group { get; set; } = new();
        public TaskCompletionSource<HintResponse> Resumed { get; } = new();
        public List<int> Opened { get; } = [];
        public List<int> ResumedScopes { get; } = [];
        public bool RetirementSucceeds { get; set; } = true;
        public string? CleanupFailureReason { get; set; }
        public List<int> Validations { get; } = [];
        public Task<HintResponse> DiscoverAsync(ElementTargetContext context, HintRect region, CancellationToken ct) =>
            Task.FromResult(First);
        public async IAsyncEnumerable<HintResponse> DiscoverIncrementallyAsync(ElementTargetContext context, HintRect region,
            [EnumeratorCancellation] CancellationToken ct) {
            yield return First;
            if (!First.IsComplete) {
                yield return await Next.Task;
                foreach (var frame in More) { yield return frame; }
            }
        }
        public async IAsyncEnumerable<HintResponse> ExpandAsync(int groupId, [EnumeratorCancellation] CancellationToken ct) {
            Opened.Add(groupId);
            yield return await Group.Task;
        }
        public async IAsyncEnumerable<HintResponse> ResumeAsync(int groupId, [EnumeratorCancellation] CancellationToken ct) {
            ResumedScopes.Add(groupId);
            yield return await Resumed.Task;
        }
        public Task<HintResponse> ValidateAsync(int token, CancellationToken ct, bool moveOnly = false) {
            Validations.Add(token);
            return Task.FromResult(First with { Targets = [], Point = new(60, 60) });
        }
        public Task<bool> RetireAsync() => Task.FromResult(RetirementSucceeds);
    }
    private sealed class Renderer : IElementHintsRenderer {
        public HintLevelView? Last { get; private set; }
        public int? Capacity { get; init; }
        public int GroupCapacity { get; init; } = 9;
        public int GetPageCapacity(Rectangle region, int keyCapacity, bool singleKey = false) =>
            Math.Min(Capacity ?? keyCapacity, keyCapacity);
        public int GetGroupPageCapacity(Rectangle region) => GroupCapacity;
        public void Render(HintLevelView view) => Last = view;
        public void RebuildLabels(IKeyLabelResolver resolver) { }
        public void FlashInvalidKey() { }
    }
    private static ElementHintsSession Session(Service service, Renderer? renderer = null,
        ElementHintAssignmentCache? assignments = null, ILogger? logger = null) =>
        new([VKey.A, VKey.S], [VKey.Q, VKey.W], new ActionMapper([]), new(1, 1, 42, 1), service, renderer, logger) {
            AssignmentCache = assignments
        };
    private static HintResponse Frame(params int[] ids) => FakeElementHintService.Result(0) with {
        Targets = ids.Select(id => new HintTarget(id + 100, [42, id], 42, 50000, HintCapabilities.Invoke,
            new(10, id * 20, 10, 10), new(10, id * 20, 10, 10), new(15, id * 20 + 5))).ToArray()
    };
    private static void Choose(ElementHintsSession session, HintLabel label) {
        session.OnKey(label.First);
        if (label.Second is { } second) { session.OnKey(second); }
    }
    private static async Task<Dictionary<int, (VKey First, VKey? Second)>> Seed(
        ElementHintAssignmentCache cache, int count = 4) {
        var service = new Service { First = Frame(1, 2) with { IsComplete = false } };
        var session = Session(service, assignments: cache);
        session.Activate(new(0, 0, 1000, 1000), default);
        var labels = session.Labels.ToDictionary(l => l.Entry.Target!.RuntimeId[1], l => (l.First, l.Second));
        service.Next.SetResult(Frame(Enumerable.Range(1, count).ToArray()));
        await session.Discovery;
        foreach (var label in session.Labels) { labels[label.Entry.Target!.RuntimeId[1]] = (label.First, label.Second); }
        session.Deactivate();
        await session.Retirement;
        return labels;
    }

    [Fact]
    public async Task RegionNumbersRemainVisibleAndSwitchFromAnyNestedDepthWithoutActing() {
        var service = new Service { First = Frame(Enumerable.Range(1, 33).ToArray()) };
        var renderer = new Renderer();
        var logger = new CapturingLogger();
        var session = Session(service, renderer, logger: logger);
        int moves = 0, actions = 0;
        session.CursorMoveRequested += _ => moves++;
        session.ActionRequested += (_, _) => actions++;
        try {
            session.Activate(new(0, 0, 1000, 1000), default);
            var regions = session.Regions.ToArray();
            Assert.Equal(2, regions.Length);
            session.OnKey(VKey.D1);
            Assert.Equal(2, session.Depth);
            Assert.Equal(regions, renderer.Last!.Regions);
            Assert.Equal(regions[0].Entry.Id, renderer.Last.ActiveRegionId);
            Assert.Equal([VKey.D1, VKey.D2], session.HelpState.GroupKeys);
            var nested = session.Labels.First(l => l.Entry.IsGroup);
            Assert.DoesNotContain(nested.First, new[] { VKey.D1, VKey.D2 });
            Choose(session, nested);
            Assert.Equal(3, session.Depth);
            Choose(session, session.Labels.First(l => l.Entry.Target is not null));
            Assert.NotNull(session.Selected);
            Assert.Equal(1, moves);
            session.OnKey(VKey.D2);
            Assert.Equal(2, session.Depth);
            Assert.Null(session.Selected);
            Assert.Null(session.Prefix);
            Assert.Equal(regions, renderer.Last.Regions);
            Assert.Equal(regions[1].Entry.Id, renderer.Last.ActiveRegionId);
            session.OnKey(VKey.D1);
            Assert.Equal(2, session.Depth);
            Assert.Equal(regions[0].Entry.Id, renderer.Last.ActiveRegionId);
            Assert.Equal(1, moves);
            Assert.Equal(0, actions);
            Assert.Empty(service.Validations);
            Assert.Contains(logger.Entries, e => e.Message.Contains("region-switch") &&
                e.Message.Contains("previousDepth=3"));
            Assert.Contains(logger.Entries, e => e.Message.Contains("region-state") &&
                e.Message.Contains($"active={regions[1].Entry.Id} visible=2 rootPage=0"));
            session.OnKey(VKey.Escape);
            Assert.Equal(1, session.Depth);
            Assert.Null(renderer.Last.ActiveRegionId);
        } finally { session.Deactivate(); await session.Retirement; }
    }

    [Fact]
    public async Task ReturningFromLocalNestingKeepsRootDiscoveryAliveForRegionSwitching() {
        var service = new Service { First = Frame(Enumerable.Range(1, 33).ToArray()) with { IsComplete = false } };
        var renderer = new Renderer();
        var session = Session(service, renderer);
        try {
            session.Activate(new(0, 0, 1000, 1000), default);
            session.OnKey(VKey.D1);
            Choose(session, session.Labels.First(l => l.Entry.IsGroup));
            Assert.Equal(3, session.Depth);
            session.OnKey(VKey.Escape);
            session.OnKey(VKey.D2);
            Assert.Equal(2, session.Depth);
            service.Next.SetResult(Frame(Enumerable.Range(1, 35).ToArray()));
            await session.Discovery;
            Assert.False(renderer.Last!.IsDiscovering);
            Assert.Equal(35, session.HelpState.TargetCount);
            Assert.Empty(service.ResumedScopes);
            Assert.Empty(service.Opened);
        } finally { session.Deactivate(); await session.Retirement; }
    }

    [Fact]
    public async Task PagingChildrenKeepsRootRegionPagePinnedUntilReturningToRoot() {
        var service = new Service {
            First = Frame() with {
                Groups = [new(2, 0, new(10, 10, 100, 100), 11), new(3, 0, new(200, 10, 100, 100), 11)]
            }
        };
        var renderer = new Renderer { Capacity = 1, GroupCapacity = 1 };
        var session = Session(service, renderer);
        try {
            session.Activate(new(0, 0, 1000, 1000), default);
            Assert.Equal(2, session.PageCount);
            var firstRegion = Assert.Single(session.Regions);
            session.OnKey(VKey.D1);
            service.Group.SetResult(Frame(1, 2) with {
                GroupId = 2,
                Targets = Frame(1, 2).Targets.Select(t => t with { DiscoveryGroupId = 2 }).ToArray()
            });
            await session.Discovery;
            Assert.Equal(2, session.PageCount);
            session.OnKey(VKey.Next);
            Assert.Equal(1, session.Page);
            Assert.Equal(firstRegion, Assert.Single(session.Regions));
            session.OnKey(VKey.A);
            Assert.NotNull(session.Prefix);
            session.OnKey(VKey.D1);
            Assert.Equal(2, session.Depth);
            Assert.Equal(0, session.Page);
            Assert.Null(session.Prefix);
            Assert.Equal(firstRegion.Entry.Id, renderer.Last!.ActiveRegionId);
            session.OnKey(VKey.Escape);
            session.OnKey(VKey.Next);
            var secondRegion = Assert.Single(session.Regions);
            Assert.NotEqual(firstRegion.Entry.Id, secondRegion.Entry.Id);
            Assert.Equal(VKey.D1, secondRegion.First);
            service.Group = new();
            session.OnKey(VKey.D1);
            service.Group.SetResult(Frame() with { GroupId = 3 });
            await session.Discovery;
            Assert.Equal(secondRegion.Entry.Id, renderer.Last.ActiveRegionId);
            Assert.Equal([2, 3], service.Opened);
            Assert.Empty(service.Validations);
        } finally { session.Deactivate(); await session.Retirement; }
    }

    [Fact]
    public async Task RootDiscoveryContinuesWhileRegionSwitchesKeepNumbersAndChildLabelsStable() {
        var config = new ConfigModel();
        var service = new Service { First = Frame(Enumerable.Range(1, 120).ToArray()) with { IsComplete = false } };
        var renderer = new Renderer();
        var session = new ElementHintsSession(config.HorizontalKeys, config.VerticalKeys, new ActionMapper([]),
            new(1, 1, 42, 1), service, renderer);
        try {
            session.Activate(new(0, 0, 4000, 4000), default);
            var regions = session.Regions.ToArray();
            Assert.Equal(2, regions.Length);
            session.OnKey(VKey.D1);
            session.OnKey(VKey.D2);
            Assert.Equal(2, session.Depth);
            var children = session.Labels.ToArray();
            service.Next.SetResult(Frame(Enumerable.Range(1, 140).ToArray()));
            await session.Discovery;
            Assert.Equal(children, session.Labels);
            Assert.Equal(regions, renderer.Last!.Regions);
            Assert.Equal(regions[1].Entry.Id, renderer.Last.ActiveRegionId);
            Assert.False(renderer.Last.IsDiscovering);
            Assert.Empty(service.ResumedScopes);
            Assert.Empty(service.Opened);
        } finally { session.Deactivate(); await session.Retirement; }
    }

    [Fact]
    public async Task SwitchingPendingRemoteRegionsRejectsTheSupersededScope() {
        var service = new Service {
            First = Frame() with {
                Groups = [new(2, 0, new(10, 10, 100, 100), 11), new(3, 0, new(200, 10, 100, 100), 11)]
            }
        };
        var logger = new CapturingLogger();
        var session = Session(service, logger: logger);
        try {
            session.Activate(new(0, 0, 1000, 1000), default);
            session.OnKey(VKey.D1);
            var previous = session.Discovery;
            session.OnKey(VKey.D2);
            Assert.Equal(2, session.Depth);
            Assert.Equal([2, 3], service.Opened);
            service.Group.SetResult(Frame(1) with {
                GroupId = 3,
                Targets = Frame(1).Targets.Select(t => t with { DiscoveryGroupId = 3 }).ToArray()
            });
            await Task.WhenAll(previous, session.Discovery);
            Assert.Equal(101, Assert.Single(session.Labels).Entry.Target!.Token);
            Assert.Equal([VKey.D1, VKey.D2], session.HelpState.GroupKeys);
            Assert.Contains(logger.Entries, e => e.Message.Contains("ignored-frame") &&
                e.Message.Contains("scope=3"));
        } finally { session.Deactivate(); await session.Retirement; }
    }

    [Fact]
    public async Task GrowingWindowAddsNumberedSpatialRegionsWithoutRemappingPublishedControls() {
        var config = new ConfigModel();
        var service = new Service { First = Frame(Enumerable.Range(1, 80).ToArray()) with { IsComplete = false } };
        var session = new ElementHintsSession(config.HorizontalKeys, config.VerticalKeys, new ActionMapper([]),
            new(1, 1, 42, 1), service, new Renderer());
        try {
            session.Activate(new(0, 0, 4000, 4000), default);
            var original = session.Labels.ToArray();
            Choose(session, original[0]);
            int selected = session.Selected!.Token;
            service.Next.SetResult(Frame(Enumerable.Range(1, 180).ToArray()));
            await session.Discovery;
            Assert.Equal(selected, session.Selected.Token);
            Assert.Equal(original, session.Labels.Where(l => !l.Entry.IsGroup));
            Assert.Equal(9, session.Labels.Count(l => l.Entry.IsGroup));
            var region = session.Labels.First(l => l.Entry.IsGroup);
            Assert.Equal(VKey.D1, region.First);
            Assert.Null(region.Second);
            Choose(session, region);
            Assert.Equal(2, session.Depth);
            Assert.True(session.HelpState.SingleKey);
            Assert.Equal(10, session.Labels.Count);
            Assert.All(session.Labels, l => Assert.NotNull(l.Entry.Target));
            Assert.Empty(service.Validations);
        } finally { session.Deactivate(); await session.Retirement; }
    }

    [Fact]
    public async Task ReopenedWindowRestoresCombosAcrossBatchesAndUsesOnlyCurrentTokens() {
        var cache = new ElementHintAssignmentCache(5);
        var expected = await Seed(cache);
        var service = new Service {
            First = Frame(4, 3) with {
                IsComplete = false,
                Targets = Frame(4, 3).Targets.Select(t => t with { Token = t.Token + 1000 }).ToArray()
            }
        };
        var session = Session(service, assignments: cache);
        try {
            session.Activate(new(0, 0, 1000, 1000), default);
            Assert.Equal(2, session.Labels.Count);
            session.OnKey(VKey.A); session.OnKey(VKey.Q);
            Assert.Null(session.Selected);
            var selected = session.Labels.Single(l => l.Entry.Target!.RuntimeId[1] == 4);
            Choose(session, selected);
            Assert.Equal(1104, session.Selected!.Token);
            await session.ValidateAsync(CancellationToken.None);
            Assert.Equal([1104], service.Validations);
            service.Next.SetResult(Frame(4, 3, 2, 1) with {
                Targets = Frame(4, 3, 2, 1).Targets.Select(t => t with { Token = t.Token + 1000 }).ToArray()
            });
            await session.Discovery;
            Assert.Equal(1104, session.Selected.Token);
            foreach (var label in session.Labels) {
                Assert.Equal(expected[label.Entry.Target!.RuntimeId[1]], (label.First, label.Second));
            }
            Assert.False(session.HelpState.SingleKey);
        } finally { session.Deactivate(); await session.Retirement; }
    }

    [Fact]
    public async Task NewControlCannotStealReservedComboAndLateEarlierPageCannotInterruptTyping() {
        var cache = new ElementHintAssignmentCache(5);
        await Seed(cache);
        var service = new Service { First = Frame(5) with { IsComplete = false } };
        var session = Session(service, assignments: cache);
        try {
            session.Activate(new(0, 0, 1000, 1000), default);
            Assert.Equal(0, session.Page);
            var first = Assert.Single(session.Labels);
            Assert.Equal((VKey.A, (VKey?)VKey.Q), (first.First, first.Second));
            session.OnKey(VKey.A);
            service.Next.SetResult(Frame(1, 2, 3, 4, 5));
            await session.Discovery;
            Assert.Equal(1, session.Page);
            Assert.Equal(2, session.PageCount);
            Assert.Equal(0, session.Prefix);
            Assert.Equal(first, Assert.Single(session.Labels));
            session.OnKey(VKey.Q);
            Assert.Equal(105, session.Selected!.Token);
            session.OnKey(VKey.Prior);
            Assert.Equal(0, session.Page);
            Assert.Equal(4, session.Labels.Count);
            Assert.DoesNotContain(session.Labels, l => l.Entry.Target!.RuntimeId[1] == 5);
        } finally { session.Deactivate(); await session.Retirement; }
    }

    [Fact]
    public async Task OffPageAssignmentsAreSavedWithoutVisitingThePage() {
        var cache = new ElementHintAssignmentCache(5);
        var service = new Service {
            First = Frame(1, 2, 3, 4) with { IsComplete = false },
            More = [Frame(1, 2, 3, 4, 5, 6) with { IsComplete = false },
                Frame(1, 2, 3, 4, 5, 6, 7) with { IsComplete = false }, Frame(1, 2, 3, 4, 5, 6, 7, 8)]
        };
        var session = Session(service, assignments: cache);
        session.Activate(new(0, 0, 1000, 1000), default);
        service.Next.SetResult(Frame(1, 2, 3, 4, 5) with { IsComplete = false });
        await session.Discovery;
        Assert.Equal(2, session.PageCount);
        Assert.Equal(0, session.Page);
        session.Deactivate(); await session.Retirement;
        var nextService = new Service { First = Frame(1, 2, 3, 4, 5, 6, 7, 8) with { IsComplete = false } };
        var next = Session(nextService, assignments: cache);
        try {
            next.Activate(new(0, 0, 1000, 1000), default);
            Assert.Equal(2, next.PageCount);
            Assert.All(next.Labels, l => Assert.False(l.Entry.IsGroup));
            next.OnKey(VKey.Next);
            var label = next.Labels.Single(l => l.Entry.Target!.RuntimeId[1] == 8);
            Assert.Equal((VKey.S, (VKey?)VKey.W), (label.First, label.Second));
            Choose(next, label);
            Assert.Equal(108, next.Selected!.Token);
            nextService.Next.SetResult(Frame(1, 2, 3, 4, 5, 6, 7, 8));
            await next.Discovery;
        } finally { next.Deactivate(); await next.Retirement; }
    }

    [Fact]
    public async Task UnchangedGroupsAndPreviouslyVisitedLevelsRestoreTheirAssignments() {
        var cache = new ElementHintAssignmentCache(5);
        var service = new Service { First = Frame(1, 2, 3, 4, 5, 6) };
        var session = Session(service, assignments: cache);
        session.Activate(new(0, 0, 1000, 1000), default);
        var rootGroup = session.Labels[0];
        Choose(session, rootGroup);
        var expected = session.Labels.ToDictionary(l => l.Entry.Target!.RuntimeId[1], l => (l.First, l.Second));
        session.OnKey(VKey.Escape);
        Assert.Equal(1, session.Depth);
        session.Deactivate(); await session.Retirement;
        var next = Session(new Service { First = Frame(4, 3, 2, 1, 6, 5) }, assignments: cache);
        try {
            next.Activate(new(0, 0, 1000, 1000), default);
            var group = next.Labels[0];
            Assert.Equal((rootGroup.First, rootGroup.Second), (group.First, group.Second));
            Choose(next, group);
            foreach (var label in next.Labels) {
                Assert.Equal(expected[label.Entry.Target!.RuntimeId[1]], (label.First, label.Second));
            }
        } finally { next.Deactivate(); await next.Retirement; }
    }

    [Theory]
    [InlineData(HintOutcome.ProviderError, false)]
    [InlineData(HintOutcome.NoTargets, true)]
    public async Task FailurePreservesHistoryButValidNoTargetsClearsIt(HintOutcome outcome, bool cleared) {
        var cache = new ElementHintAssignmentCache(5);
        await Seed(cache);
        var failing = Session(new Service { First = Frame() with { Outcome = outcome } }, assignments: cache);
        failing.Activate(new(0, 0, 1000, 1000), default);
        failing.Deactivate(); await failing.Retirement;
        var next = Session(new Service { First = Frame(4) }, assignments: cache);
        try {
            next.Activate(new(0, 0, 1000, 1000), default);
            var label = Assert.Single(next.Labels);
            Assert.Equal(cleared ? VKey.A : VKey.S, label.First);
            Assert.Equal(cleared ? null : (VKey?)VKey.W, label.Second);
        } finally { next.Deactivate(); await next.Retirement; }
    }

    [Fact]
    public async Task CancelledPartialScanReplacesOnlySeenAssignmentsAndIgnoresLateFrames() {
        var cache = new ElementHintAssignmentCache(5);
        await Seed(cache);
        var service = new Service { First = Frame(4) with { IsComplete = false } };
        var partial = Session(service, assignments: cache);
        partial.Activate(new(0, 0, 1000, 1000), default);
        partial.Deactivate(); await partial.Retirement;
        service.Next.SetResult(Frame(1, 2, 3, 4));
        await partial.Discovery;
        var next = Session(new Service { First = Frame(1, 4) }, assignments: cache);
        try {
            next.Activate(new(0, 0, 1000, 1000), default);
            Assert.Equal(VKey.A, next.Labels.Single(l => l.Entry.Target!.RuntimeId[1] == 1).First);
            Assert.Equal(VKey.W, next.Labels.Single(l => l.Entry.Target!.RuntimeId[1] == 4).Second);
        } finally { next.Deactivate(); await next.Retirement; }
    }

    [Fact]
    public async Task FactorySharesAssignmentsAcrossNewSessionsAndDropsThemOnDisposal() {
        var config = new ConfigModel {
            HorizontalKeys = [VKey.A, VKey.S],
            VerticalKeys = [VKey.Q, VKey.W],
            Modes = new() { ElementHints = new() { Enabled = true, ChordKey = VKey.Tab, TwoKey = true } }
        };
        var service = new FakeElementHintService { Response = Frame(1, 2, 3, 4) };
        var factory = new ModeSessionFactory(config, new ActionMapper([]), null, elementHintsService: service);
        ElementHintsSession Open(ModeSessionFactory owner) {
            var session = Assert.IsType<ElementHintsSession>(owner.Create("ElementHints", new(1, 1, 42, 1)));
            session.Activate(new(0, 0, 1000, 1000), default);
            return session;
        }
        var first = Open(factory);
        var expected = first.Labels.Single(l => l.Entry.Target!.RuntimeId[1] == 4);
        first.Deactivate(); await first.Retirement;
        await service.RetireAsync();
        service.Response = Frame(4, 3, 2, 1);
        var next = Open(factory);
        var restored = next.Labels.Single(l => l.Entry.Target!.RuntimeId[1] == 4);
        Assert.Equal((expected.First, expected.Second),
            (restored.First, restored.Second));
        next.Deactivate(); await next.Retirement;
        await factory.RetireElementHintsAsync();
        var replacement = Open(new(config, new ActionMapper([]), null, elementHintsService: service));
        Assert.Equal(VKey.A, replacement.Labels[0].First);
        replacement.Deactivate(); await replacement.Retirement;
    }

    [Fact]
    public async Task PendingCleanupReportsItsStageWithoutClaimingScansArePermanentlyDisabled() {
        var service = new Service {
            First = FakeElementHintService.Result(1),
            RetirementSucceeds = false,
            CleanupFailureReason = "Diagnostic pipe read is still pending"
        };
        var session = Session(service);
        var failures = new List<string>();
        session.FailureReported += failures.Add;
        session.Activate(new(0, 0, 1000, 1000), default);
        await session.Discovery;
        session.Deactivate();
        await session.Retirement;
        Assert.Equal("UIA helper cleanup pending (Diagnostic pipe read is still pending); reopen hints to retry.",
            Assert.Single(failures));
        service.RetirementSucceeds = true;
        service.CleanupFailureReason = null;
        try {
            session.Activate(new(0, 0, 1000, 1000), default);
            await session.Discovery;
            Assert.Single(session.Labels);
        } finally {
            session.Deactivate();
            await session.Retirement;
        }
        Assert.Single(failures);
    }

    [Fact]
    public async Task AppendingEarlierPhysicalTargetsNeverRemapsKeysPrefixSelectionOrEnteredLevels() {
        var service = new Service();
        var renderer = new Renderer();
        var session = Session(service, renderer);
        try {
            session.Activate(new(0, 0, 1000, 1000), default);
            var first = Assert.Single(session.Labels);
            Assert.Equal(VKey.Q, first.Second);
            Assert.True(renderer.Last!.IsDiscovering);
            session.OnKey(first.First);
            var final = FakeElementHintService.Result(2);
            final = final with {
                Targets = [final.Targets[0], final.Targets[1] with {
                Bounds = new(0, 0, 10, 10), VisibleBounds = new(0, 0, 10, 10), Preview = new(5, 5)
            }]
            };
            service.Next.SetResult(final);
            await session.Discovery;
            Assert.Equal(0, session.Prefix);
            Assert.Equal(first, session.Labels[0]);
            Assert.False(renderer.Last.IsDiscovering);
            session.OnKey(VKey.Q);
            Assert.Equal(1, session.Selected!.Token);
            session.Redraw();
            Assert.Equal(1, session.Selected.Token);
            Assert.Equal(first, session.Labels[0]);
        } finally { session.Deactivate(); await session.Retirement; }
    }

    [Fact]
    public async Task SelectingAnEarlyHintAllowsActionsBeforeFinalDiscovery() {
        var service = new Service();
        var session = Session(service);
        int actions = 0;
        session.ActionRequested += (_, _) => actions++;
        try {
            session.Activate(new(0, 0, 1000, 1000), default);
            session.OnKey(VKey.A); session.OnKey(VKey.Q); session.OnKey(VKey.Space);
            Assert.Equal(1, actions);
            Assert.False(session.Discovery.IsCompleted);
            Assert.Equal(new HintPoint(60, 60), (await session.ValidateAsync(CancellationToken.None)).Point);
            service.Next.SetResult(FakeElementHintService.Result(2));
            await session.Discovery;
            Assert.Equal(1, session.Selected!.Token);
        } finally { session.Deactivate(); await session.Retirement; }
    }

    [Fact]
    public async Task LargeGroupLoadsOnlyOnEntryAndLateGroupCompletionCannotReplaceParentState() {
        var service = new Service {
            First = FakeElementHintService.Result(0) with {
                Groups = [new(2, 0, new(10, 10, 100, 100), 11)],
                IsComplete = true
            }
        };
        var session = Session(service);
        try {
            session.Activate(new(0, 0, 1000, 1000), default);
            Assert.Empty(service.Opened);
            var group = Assert.Single(session.Labels);
            Assert.True(group.Entry.IsGroup);
            session.OnKey(group.First);
            if (group.Second is { } second) { session.OnKey(second); }
            Assert.Equal([2], service.Opened);
            Assert.Equal(2, session.Depth);
            Assert.Empty(session.Labels);
            var loading = session.Discovery;
            session.OnKey(VKey.Escape);
            Assert.Equal(1, session.Depth);
            Assert.Equal(HintOutcome.Success, session.HelpState.Outcome);
            service.Group.SetResult(FakeElementHintService.Result(1) with {
                GroupId = 2,
                Targets = [FakeElementHintService.Result(1).Targets[0] with { DiscoveryGroupId = 2 }],
                Groups = service.First.Groups
            });
            await loading;
            Assert.Equal(group, Assert.Single(session.Labels));
            Assert.Equal(HintOutcome.Success, session.HelpState.Outcome);
        } finally { session.Deactivate(); await session.Retirement; }
    }

    [Fact]
    public async Task EscapingAnUnfinishedGroupResumesParentWithoutAcceptingTheSupersededStreams() {
        var service = new Service {
            First = FakeElementHintService.Result(0) with {
                Groups = [new(2, 0, new(10, 10, 100, 100), 11)],
                IsComplete = false
            }
        };
        var session = Session(service);
        try {
            session.Activate(new(0, 0, 1000, 1000), default);
            var original = session.Discovery;
            var label = Assert.Single(session.Labels);
            Choose(session, label);
            var group = session.Discovery;
            session.OnKey(VKey.Escape);
            Assert.Equal([0], service.ResumedScopes);
            Assert.Null(session.HelpState.Outcome);
            service.Next.SetResult(FakeElementHintService.Result(2));
            service.Group.SetResult(FakeElementHintService.Result(3) with { GroupId = 2 });
            await Task.WhenAll(original, group);
            Assert.Equal(label, Assert.Single(session.Labels));
            service.Resumed.SetResult(FakeElementHintService.Result(1) with { Groups = service.First.Groups });
            await session.Discovery;
            Assert.Equal(1, session.Depth);
            Assert.Equal(2, session.Labels.Count);
            Assert.Equal(label, session.Labels[0]);
            Assert.Equal(HintOutcome.Success, session.HelpState.Outcome);
        } finally { session.Deactivate(); await session.Retirement; }
    }

    [Fact]
    public async Task RetiredBatchesCannotRestoreLabelsOrNotifyHelp() {
        var service = new Service();
        var session = Session(service);
        int updates = 0;
        session.StateChanged += () => updates++;
        session.Activate(new(0, 0, 1000, 1000), default);
        session.Deactivate();
        int retiredUpdates = updates;
        service.Next.SetResult(FakeElementHintService.Result(2));
        await session.Discovery;
        Assert.Empty(session.Labels);
        Assert.Equal(retiredUpdates, updates);
    }
}
