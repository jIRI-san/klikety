using System.Drawing;
using System.Runtime.CompilerServices;

using Klikety.Automation;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Navigation;

namespace Klikety.Tests;

public sealed class ProgressiveElementHintsSessionTests {
    private sealed class Service : IElementHintService {
        public HintResponse First { get; set; } = FakeElementHintService.Result(1) with { IsComplete = false };
        public TaskCompletionSource<HintResponse> Next { get; } = new();
        public TaskCompletionSource<HintResponse> Group { get; } = new();
        public TaskCompletionSource<HintResponse> Resumed { get; } = new();
        public List<int> Opened { get; } = [];
        public List<int> ResumedScopes { get; } = [];
        public bool RetirementSucceeds { get; set; } = true;
        public string? CleanupFailureReason { get; set; }
        public Task<HintResponse> DiscoverAsync(ElementTargetContext context, HintRect region, CancellationToken ct) =>
            Task.FromResult(First);
        public async IAsyncEnumerable<HintResponse> DiscoverIncrementallyAsync(ElementTargetContext context, HintRect region,
            [EnumeratorCancellation] CancellationToken ct) {
            yield return First;
            if (!First.IsComplete) { yield return await Next.Task; }
        }
        public async IAsyncEnumerable<HintResponse> ExpandAsync(int groupId, [EnumeratorCancellation] CancellationToken ct) {
            Opened.Add(groupId);
            yield return await Group.Task;
        }
        public async IAsyncEnumerable<HintResponse> ResumeAsync(int groupId, [EnumeratorCancellation] CancellationToken ct) {
            ResumedScopes.Add(groupId);
            yield return await Resumed.Task;
        }
        public Task<HintResponse> ValidateAsync(int token, CancellationToken ct, bool moveOnly = false) =>
            Task.FromResult(First with { Targets = [], Point = new(60, 60) });
        public Task<bool> RetireAsync() => Task.FromResult(RetirementSucceeds);
    }
    private sealed class Renderer : IElementHintsRenderer {
        public HintLevelView? Last { get; private set; }
        public int GetPageCapacity(Rectangle region, int keyCapacity, bool singleKey = false) => keyCapacity;
        public void Render(HintLevelView view) => Last = view;
        public void RebuildLabels(IKeyLabelResolver resolver) { }
        public void FlashInvalidKey() { }
    }
    private static ElementHintsSession Session(Service service, Renderer? renderer = null) =>
        new([VKey.A, VKey.S], [VKey.Q, VKey.W], new ActionMapper([]), new(1, 1), service, renderer);

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
            session.OnKey(label.First); session.OnKey(label.Second!.Value);
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
