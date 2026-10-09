using System.Drawing;

using Klikety.Automation;
using Klikety.Config;
using Klikety.Grid;
using Klikety.Input;
using Klikety.Navigation;
using Klikety.Tests.Fakes;

using Microsoft.Extensions.Logging.Abstractions;

namespace Klikety.Tests;

internal sealed class FakeElementHintService : IElementHintService {
    public TaskCompletionSource<HintResponse>? ScanCompletion { get; set; }
    public TaskCompletionSource<HintResponse>? ValidationCompletion { get; set; }
    public List<(ElementTargetContext Context, HintRect Region)> Scans { get; } = [];
    public List<int> Validations { get; } = [];
    public List<bool> ValidationMoveOnlyFlags { get; } = [];
    public int RetireCount { get; private set; }
    public bool RetireSucceeded { get; set; } = true;
    public Action? Validating { get; set; }
    public HintResponse Response { get; set; } = Result(1);
    public Task<HintResponse> DiscoverAsync(ElementTargetContext context, HintRect region, CancellationToken ct) {
        Scans.Add((context, region));
        return ScanCompletion?.Task ?? Task.FromResult(Response);
    }
    public Task<HintResponse> ValidateAsync(int token, CancellationToken ct, bool moveOnly = false) {
        ValidationMoveOnlyFlags.Add(moveOnly);
        Validations.Add(token); Validating?.Invoke();
        return ValidationCompletion?.Task ?? Task.FromResult(Response with { Point = new HintPoint(20, 20) });
    }
    public Task<bool> RetireAsync() { RetireCount++; return Task.FromResult(RetireSucceeded); }
    public static HintResponse Result(int count, HintOutcome outcome = HintOutcome.Success) =>
        new(1, Guid.NewGuid(), Guid.NewGuid(), outcome,
            Enumerable.Range(1, count).Select(i => new HintTarget(i, [42, i], 42, 50000, HintCapabilities.Invoke,
                new(10, 10, 100, 100), new(10, 10, 100, 100), new(60, 60))).ToArray(), RootProcessId: 42);
}

internal sealed class FakeElementPointGuard : IElementPointGuard {
    public bool Allowed { get; set; } = true;
    public ElementTargetContext Capture(nint hwnd, int ownerProcessId) => new(hwnd, ownerProcessId, 42, 1);
    public bool IsCurrent(ElementTargetContext context, int rootProcessId, HintPoint point) => Allowed;
}

public class ElementHintsStateMachineTests {
    [Fact]
    public void DiagnosticsCorrelateFramesAssignmentsNumbersAndCloseWithoutTargetIdentities() {
        var service = new FakeElementHintService { Response = FakeElementHintService.Result(11) };
        var logger = new CapturingLogger();
        var cache = new ElementHintAssignmentCache(5);
        var context = new ElementTargetContext(1, 1, 42, 1);
        ElementHintsSession NewSession() => new([VKey.A, VKey.S], [VKey.Q, VKey.W],
            new ActionMapper([]), context, service, null, logger) { AssignmentCache = cache };
        var session = NewSession();
        session.Activate(new(0, 0, 1000, 1000), default);
        var activation = session.DiagnosticId;
        session.OnKey(VKey.D9);
        session.OnKey(VKey.D1);
        session.Deactivate();
        Assert.Contains(logger.Entries, e => e.Message.Contains($"activation={activation}") && e.Message.Contains("frame=1"));
        Assert.Contains(logger.Entries, e => e.Message.Contains("number key=D9") && e.Message.Contains("visibleGroups=3"));
        Assert.Contains(logger.Entries, e => e.Message.Contains("open-group") && e.Message.Contains("children=4"));
        Assert.Contains(logger.Entries, e => e.Message.Contains("assignment-cache saved"));
        var next = NewSession();
        next.Activate(new(0, 0, 1000, 1000), default);
        try {
            Assert.NotEqual(activation, next.DiagnosticId);
            Assert.Contains(logger.Entries, e => e.Message.Contains($"activation={next.DiagnosticId}") &&
                e.Message.Contains("assignment-cache reused=True"));
            Assert.Contains(logger.Entries, e => e.Message.Contains($"activation={next.DiagnosticId}") &&
                e.Message.Contains("restoredGroups=3"));
            Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("RuntimeId") || e.Message.Contains("fingerprint"));
        } finally { next.Deactivate(); }
    }

    private sealed class HelpCapacityRenderer : IElementHintsRenderer {
        public int Capacity { get; set; } = 3;
        public List<bool> DiscoveryStates { get; } = [];
        public int GetPageCapacity(Rectangle region, int keyCapacity, bool singleKey = false) => Math.Min(Capacity, keyCapacity);
        public void RebuildLabels(IKeyLabelResolver resolver) { }
        public void Render(HintLevelView view) => DiscoveryStates.Add(view.IsDiscovering);
        public void FlashInvalidKey() { }
    }

    [Theory]
    [InlineData(HintOutcome.Success)]
    [InlineData(HintOutcome.NoTargets)]
    [InlineData(HintOutcome.Timeout)]
    [InlineData(HintOutcome.ProviderError)]
    public async Task LoadingIndicatorTracksPendingDiscoveryAndStopsForEveryResult(HintOutcome outcome) {
        var service = new FakeElementHintService { ScanCompletion = new() };
        var renderer = new HelpCapacityRenderer();
        var session = new ElementHintsSession([VKey.A], [VKey.Q],
            new ActionMapper([]), new(1, 1), service, renderer);
        try {
            session.Activate(new(0, 0, 100, 100), default);
            session.Redraw();
            Assert.All(renderer.DiscoveryStates, state => Assert.True(state));
            service.ScanCompletion.SetResult(FakeElementHintService.Result(outcome == HintOutcome.Success ? 1 : 0, outcome));
            await session.Discovery;
            Assert.False(renderer.DiscoveryStates.Last());
            session.Redraw();
            Assert.False(renderer.DiscoveryStates.Last());
        } finally { session.Deactivate(); }
    }

    [Fact]
    public void HelpSnapshotUsesActualRendererCapacityAndRelayoutState() {
        var renderer = new HelpCapacityRenderer();
        var service = new FakeElementHintService { Response = FakeElementHintService.Result(10) };
        var session = new ElementHintsSession([VKey.A, VKey.S], [VKey.Q, VKey.W],
            new ActionMapper([]), new(1, 1), service, renderer);
        session.Activate(new(0, 0, 100, 100), default);
        Assert.Equal(1, session.HelpState.PageCount);
        Assert.Equal(2, session.Labels.Count);
        session.OnKey(VKey.D1);
        Assert.Equal(2, session.HelpState.Depth);
        session.OnKey(VKey.D1);
        Assert.Equal(0, session.HelpState.Page);
        Assert.Null(session.HelpState.Prefix);
        Assert.Equal(2, session.Depth);
        var nested = session.Labels.First(l => l.Entry.IsGroup);
        session.OnKey(nested.First);
        if (nested.Second is { } second) { session.OnKey(second); }
        Assert.Equal(3, session.Depth);
        session.OnKey(VKey.A); session.OnKey(VKey.Q);
        Assert.True(session.HelpState.HasSelection);
        renderer.Capacity = 2;
        session.Relayout();
        var state = session.HelpState;
        Assert.Equal(new ElementHintsHelpState(HintOutcome.Success, "", 0, 1, 10, null, false,
            SingleKey: true, EntryCount: 2, GroupKeys: state.GroupKeys), state);
        Assert.Equal([VKey.D1, VKey.D2], session.HelpState.GroupKeys);
        Assert.Single(service.Scans);
        Assert.Empty(service.Validations);
        session.Deactivate();
    }

    [Fact]
    public async Task RetiredDiscoveryCannotNotifyOrReplaceActiveHelpState() {
        var service = new FakeElementHintService { ScanCompletion = new() };
        using var manager = Manager(service);
        int updates = 0;
        manager.ElementHintsStateChanged += () => updates++;
        manager.ActivateDefaultSession(new(0, 0, 100, 100), default, "ElementHints");
        var oldSession = Assert.IsType<ElementHintsSession>(manager.ActiveSession);
        Assert.Equal(1, updates);
        manager.SwitchMode("UniformGrid");
        service.ScanCompletion.SetResult(FakeElementHintService.Result(10));
        await oldSession.Discovery;
        Assert.Equal(1, updates);
        Assert.IsType<UniformGridSession>(manager.ActiveSession);
    }

    private static SessionManager Manager(FakeElementHintService service) {
        var config = new ConfigModel {
            Modes = new() { ElementHints = new() { Enabled = true, ChordKey = VKey.Tab, TwoKey = true, ArrowKeys = true } }
        };
        return new SessionManager(new ModeSessionFactory(config, new ActionMapper([]), null,
            elementHintsService: service), new FakeOverlayWindow(), NullLogger.Instance) {
            TargetContext = new(123, 10, 42, 1),
        };
    }

    [Fact]
    public void ScopeDisplayPickerAndMacroResumesRetainOriginalTargetAndRescan() {
        var service = new FakeElementHintService();
        using var manager = Manager(service);
        var first = new Rectangle(0, 0, 1920, 1080);
        var second = new Rectangle(-1600, -400, 1600, 900);
        var scoped = new Rectangle(-1400, -200, 800, 600);
        manager.ActivateDefaultSession(first, default, "ElementHints");
        manager.RestartOnDisplay(second, new(-1000, 0));
        manager.SwitchToAppScope(scoped, new(-1000, 0));
        manager.SuspendElementHints();
        manager.ResumeElementHints();
        manager.ResumeForRecording("ElementHints", second, new(-1000, 0), true, scoped);
        manager.DeactivateSession();
        manager.ResumeAfterPlayback("ElementHints", second, new(-1000, 0));
        Assert.Equal(6, service.Scans.Count);
        Assert.All(service.Scans, s => Assert.Equal(manager.TargetContext, s.Context));
        Assert.Equal([new HintRect(0, 0, 1920, 1080), new(-1600, -400, 1600, 900),
            new(-1400, -200, 800, 600), new(-1400, -200, 800, 600), new(-1400, -200, 800, 600),
            new(-1600, -400, 1600, 900)], service.Scans.Select(s => s.Region));
        Assert.Equal(5, service.RetireCount);
    }

    [Fact]
    public void CleanupFailureIsReportedBeforeSessionEventsAreUnsubscribed() {
        var service = new FakeElementHintService { RetireSucceeded = false };
        using var manager = Manager(service);
        var failures = new List<string>();
        manager.FailureReported += failures.Add;
        manager.ActivateDefaultSession(new(0, 0, 100, 100), default, "ElementHints");
        manager.DeactivateSession();
        Assert.Equal("UIA helper cleanup pending (Teardown unconfirmed); reopen hints to retry.",
            Assert.Single(failures));
    }

    [Fact]
    public void EveryTargetIsReachableAcrossGroupsAndLabelsNeverClick() {
        var service = new FakeElementHintService { Response = FakeElementHintService.Result(11) };
        var session = new ElementHintsSession([VKey.A, VKey.S], [VKey.Q, VKey.W],
            new ActionMapper([]), new(1, 1), service, null);
        int clicks = 0, cursorMoves = 0;
        session.ActionRequested += (_, _) => clicks++;
        session.CursorMoveRequested += _ => cursorMoves++;
        session.Activate(new(0, 0, 1000, 1000), new(5, 5));
        Assert.Equal(1, session.PageCount);
        var selected = new List<int>();
        void Visit() {
            var labels = session.Labels.ToArray();
            foreach (var label in labels) {
                int before = cursorMoves;
                session.OnKey(label.First);
                if (label.Second is { } second) { session.OnKey(second); }
                if (label.Entry.IsGroup) {
                    Assert.Equal(before, cursorMoves);
                    Assert.Null(session.Selected);
                    session.OnKey(VKey.Space);
                    Assert.Equal(0, clicks);
                    Visit();
                    session.OnKey(VKey.Escape);
                } else {
                    Assert.Equal(label.Entry.Target!.Token, session.Selected!.Token);
                    selected.Add(session.Selected.Token);
                    session.Redraw();
                    Assert.Equal(label.Entry.Target.Token, session.Selected.Token);
                }
            }
        }
        Visit();
        Assert.Equal(Enumerable.Range(1, 11), selected);
        Assert.Equal(11, cursorMoves);
        Assert.Equal(0, clicks);
        while (session.Selected is null) {
            var label = session.Labels[0];
            session.OnKey(label.First);
            if (label.Second is { } second) { session.OnKey(second); }
        }
        session.OnKey(VKey.Space);
        Assert.Equal(1, clicks);
        session.Deactivate();
    }

    [Fact]
    public void PrefixReplacementEscapeAndActionsWithoutSelection() {
        var session = new ElementHintsSession([VKey.A, VKey.S], [VKey.Q, VKey.W],
            new ActionMapper([]), new(1, 1), new FakeElementHintService { Response = FakeElementHintService.Result(4) }, null);
        int cancelled = 0, actions = 0;
        session.Cancelled += () => cancelled++;
        session.ActionRequested += (_, _) => actions++;
        session.Activate(new(0, 0, 1000, 1000), default);
        session.OnKey(VKey.Space);
        Assert.Equal(0, actions);
        session.OnKey(VKey.A); session.OnKey(VKey.S); session.OnKey(VKey.W);
        Assert.Equal(4, session.Selected!.Token);
        session.OnKey(VKey.A); session.OnKey(VKey.Escape);
        Assert.Null(session.Prefix); Assert.Null(session.Selected); Assert.Equal(0, cancelled);
        session.OnKey(VKey.A); session.OnKey(VKey.Q);
        session.OnKey(VKey.Escape); Assert.Equal(1, cancelled);
        session.Deactivate();
    }

    [Fact]
    public async Task LoadingFallbackAndCancellationDoNotWaitForProviderOrAcceptLateResults() {
        var service = new FakeElementHintService { ScanCompletion = new() };
        var session = new ElementHintsSession([VKey.A], [VKey.Q], new ActionMapper([]), new(1, 1), service, null);
        int fallback = 0, cancel = 0, actions = 0;
        session.GridFallbackRequested += () => fallback++;
        session.Cancelled += () => cancel++;
        session.ActionRequested += (_, _) => actions++;
        session.Activate(new(0, 0, 100, 100), default);
        session.OnKey(VKey.Space); session.OnKey(VKey.Return); session.OnKey(VKey.Escape);
        Assert.Equal(1, fallback); Assert.Equal(1, cancel); Assert.Equal(0, actions);
        Assert.False(session.Discovery.IsCompleted);
        session.Deactivate();
        service.ScanCompletion.SetResult(FakeElementHintService.Result(1));
        await session.Discovery;
        Assert.Null(session.Selected);
        Assert.Equal(1, session.PageCount);
        Assert.Equal(1, service.RetireCount);
    }

    [Fact]
    public async Task DisposalRetiresPendingDiscoveryAndCannotReviveTheOldSession() {
        var service = new FakeElementHintService { ScanCompletion = new() };
        using var manager = Manager(service);
        manager.ActivateDefaultSession(new(0, 0, 100, 100), default, "ElementHints");
        var session = Assert.IsType<ElementHintsSession>(manager.ActiveSession);
        manager.Dispose();
        Assert.False(manager.IsActive);
        Assert.Equal(1, service.RetireCount);
        service.ScanCompletion.SetResult(FakeElementHintService.Result(10));
        await session.Discovery;
        Assert.Equal("Finding controls...", session.Status);
        Assert.Equal(1, session.PageCount);
        session.OnKey(VKey.A); session.OnKey(VKey.Q);
        Assert.Null(session.Selected);
        Assert.Single(service.Scans);
    }
}
