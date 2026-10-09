using System.Diagnostics;
using System.IO;

using Klikety.Automation;

namespace Klikety.Tests;

public class UiaWorkerSupervisorTests {
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "worker-fixture", "Klikety.WorkerFixture.exe");

    [Theory]
    [InlineData("cold-success", HintOutcome.Success)]
    [InlineData("cold-error", HintOutcome.Success)]
    [InlineData("cold-empty", HintOutcome.NoTargets)]
    public async Task EmptyColdScanRetriesOnceWithinTheSameActivation(string scenario, HintOutcome outcome) {
        var worker = new UiaWorkerSupervisor(Fixture, scenario, cacheWindowCount: 2);
        var frames = new List<HintResponse>();
        try {
            await foreach (var frame in worker.DiscoverIncrementallyAsync(new(1, 1),
                new(0, 0, 100, 100), CancellationToken.None)) { frames.Add(frame); }
            var result = Assert.Single(frames);
            Assert.Equal(outcome, result.Outcome);
            Assert.Equal("discoveries=2", result.Reason);
            Assert.NotNull(worker.OwnedProcessId);
        } finally { Assert.True(await worker.RetireAsync()); }
    }

    [Fact]
    public async Task ColdRetryDoesNotResetTheAbsoluteDiscoveryDeadline() {
        var worker = new UiaWorkerSupervisor(Fixture, "cold-delay", discoveryTimeoutMs: 400, cacheWindowCount: 2);
        var watch = Stopwatch.StartNew();
        try {
            HintResponse? result = null;
            await foreach (var frame in worker.DiscoverIncrementallyAsync(new(1, 1),
                new(0, 0, 100, 100), CancellationToken.None)) { result = frame; }
            Assert.NotNull(result);
            Assert.Contains(result.Outcome, new[] { HintOutcome.NoTargets, HintOutcome.Timeout });
            if (result.Outcome == HintOutcome.NoTargets) { Assert.Equal("discoveries=1", result.Reason); }
            Assert.True(watch.ElapsedMilliseconds < 400 + ElementHintProtocol.CleanupMs + 500);
        } finally { Assert.True(await worker.RetireAsync()); }
    }

    [Theory]
    [InlineData("hang", HintOutcome.Timeout)]
    [InlineData("startup-hang", HintOutcome.Timeout)]
    [InlineData("crash", HintOutcome.ProviderError)]
    [InlineData("truncated", HintOutcome.ProtocolError)]
    [InlineData("oversized", HintOutcome.ProtocolError)]
    [InlineData("stderr", HintOutcome.ProtocolError)]
    [InlineData("malformed", HintOutcome.ProtocolError)]
    [InlineData("mismatch", HintOutcome.ProtocolError)]
    public async Task ChildFailuresAreBoundedAndRetired(string scenario, HintOutcome outcome) {
        var worker = new UiaWorkerSupervisor(Fixture, scenario);
        var watch = Stopwatch.StartNew();
        var response = await worker.DiscoverAsync(new(1, Environment.ProcessId), new(0, 0, 100, 100), CancellationToken.None);
        Assert.Equal(outcome, response.Outcome);
        Assert.True(watch.ElapsedMilliseconds < ElementHintProtocol.DiscoveryMs + ElementHintProtocol.CleanupMs + 250);
        Assert.Null(worker.OwnedProcessId);
        Assert.True(await worker.RetireAsync());
        var recovery = new UiaWorkerSupervisor(Fixture, "ok");
        Assert.Equal(HintOutcome.NoTargets, (await recovery.DiscoverAsync(new(1, 1), new(0, 0, 100, 100), CancellationToken.None)).Outcome);
        Assert.True(await recovery.RetireAsync());
    }

    [Fact]
    public async Task MissingExecutableIsModeUnavailableNotStartupCrash() {
        var worker = new UiaWorkerSupervisor(Path.Combine(AppContext.BaseDirectory, "missing-worker.exe"));
        Assert.Equal(HintOutcome.Unavailable, (await worker.DiscoverAsync(new(1, 1),
            new(0, 0, 100, 100), CancellationToken.None)).Outcome);
        Assert.True(await worker.RetireAsync());
    }

    [Fact]
    public async Task CancellationAndNextScanUseOneOwnedWorker() {
        var worker = new UiaWorkerSupervisor(Fixture, "hang", discoveryTimeoutMs: 10000);
        using var cts = new CancellationTokenSource(100);
        var response = await worker.DiscoverAsync(new(1, 1), new(0, 0, 100, 100), cts.Token);
        Assert.Equal(HintOutcome.Cancelled, response.Outcome);
        Assert.Null(worker.OwnedProcessId);
        using var next = new CancellationTokenSource(100);
        Assert.Equal(HintOutcome.Cancelled, (await worker.DiscoverAsync(new(1, 1),
            new(0, 0, 100, 100), next.Token)).Outcome);
        Assert.Null(worker.OwnedProcessId);
    }

    [Fact]
    public async Task LateStartupCompletionDoesNotPermanentlyDisableTheSupervisor() {
        using var startupGate = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int starts = 0;
        var worker = new UiaWorkerSupervisor(Fixture, "ok", beforeStart: () => {
            if (Interlocked.Increment(ref starts) == 1) {
                entered.SetResult();
                startupGate.Wait();
            }
        });
        using var cancel = new CancellationTokenSource();
        try {
            var scan = worker.DiscoverAsync(new(1, 1), new(0, 0, 100, 100), cancel.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancel.Cancel();
            Assert.Equal(HintOutcome.CleanupFailed, (await scan).Outcome);
            Assert.Equal("Helper startup is still pending", worker.CleanupFailureReason);
            Assert.Null(worker.OwnedProcessId);
            Assert.False(await worker.RetireAsync());
            Assert.Equal(1, starts);
            startupGate.Set();
            Assert.Equal(HintOutcome.NoTargets, (await worker.DiscoverAsync(new(1, 1),
                new(0, 0, 100, 100), CancellationToken.None)).Outcome);
            Assert.Equal(2, starts);
            Assert.NotNull(worker.OwnedProcessId);
            Assert.Null(worker.CleanupFailureReason);
        } finally {
            startupGate.Set();
            await worker.RetireAsync();
        }
    }

    [Theory]
    [InlineData(0, "complete")]
    [InlineData(2, "complete")]
    [InlineData(2, "fault")]
    [InlineData(2, "cancel")]
    public async Task LateDiagnosticDrainMustFinishBeforeReplacementButCanRecover(int windows, string completion) {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int readers = 0;
        var worker = new UiaWorkerSupervisor(Fixture, "ok", cacheWindowCount: windows,
            drainDiagnostics: _ => Interlocked.Increment(ref readers) == 1 ? pending.Task : Task.CompletedTask);
        async Task<HintResponse> Scan() {
            HintResponse? last = null;
            await foreach (var response in worker.DiscoverIncrementallyAsync(new(1, 1),
                new(0, 0, 100, 100), CancellationToken.None)) { last = response; }
            return Assert.IsType<HintResponse>(last);
        }
        try {
            Assert.Equal(HintOutcome.NoTargets, (await Scan()).Outcome);
            Assert.NotNull(worker.OwnedProcessId);
            Assert.False(await worker.RetireAsync());
            Assert.Null(worker.OwnedProcessId);
            Assert.Equal("Diagnostic pipe read is still pending", worker.CleanupFailureReason);
            Assert.Equal(HintOutcome.CleanupFailed, (await Scan()).Outcome);
            Assert.Equal(1, readers);
            switch (completion) {
                case "fault": pending.SetException(new IOException("Retired diagnostic pipe closed")); break;
                case "cancel": pending.SetCanceled(); break;
                default: pending.SetResult(); break;
            }
            Assert.Equal(HintOutcome.NoTargets, (await Scan()).Outcome);
            Assert.Equal(2, readers);
            Assert.NotNull(worker.OwnedProcessId);
            Assert.Null(worker.CleanupFailureReason);
        } finally {
            pending.TrySetResult();
            await worker.RetireAsync();
            _ = pending.Task.Exception;
        }
    }

    [Fact]
    public async Task ParentCrashClosesJobAndKillsOwnedChild() {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pid");
        try {
            using var parent = Process.Start(new ProcessStartInfo(Fixture, $"parent \"{path}\"") { UseShellExecute = false, CreateNoWindow = true })!;
            await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            int pid = int.Parse(await File.ReadAllTextAsync(path));
            Assert.True(pid > 0);
            try {
                using var child = Process.GetProcessById(pid);
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
                Assert.True(child.HasExited);
            } catch (ArgumentException) { /* Already reaped by the OS. */ }
        } finally { File.Delete(path); }
    }

    [Fact]
    public async Task SameSupervisorRecoversAfterTimeoutAndValidationHasItsOwnDeadline() {
        string marker = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ready");
        var worker = new UiaWorkerSupervisor(Fixture, $"recover \"{marker}\"");
        try {
            Assert.Equal(HintOutcome.Timeout, (await worker.DiscoverAsync(new(1, 1),
                new(0, 0, 100, 100), CancellationToken.None)).Outcome);
            await File.WriteAllTextAsync(marker, "ready");
            Assert.Equal(HintOutcome.NoTargets, (await worker.DiscoverAsync(new(1, 1),
                new(0, 0, 100, 100), CancellationToken.None)).Outcome);
        } finally { Assert.True(await worker.RetireAsync()); File.Delete(marker); }
        var validationWorker = new UiaWorkerSupervisor(Fixture, "validation-hang", discoveryTimeoutMs: 10000);
        await validationWorker.DiscoverAsync(new(1, 1), new(0, 0, 100, 100), CancellationToken.None);
        var watch = Stopwatch.StartNew();
        Assert.Equal(HintOutcome.Timeout, (await validationWorker.ValidateAsync(1, CancellationToken.None)).Outcome);
        Assert.True(watch.ElapsedMilliseconds < 1000);
        Assert.Null(validationWorker.OwnedProcessId);
    }

    [Fact]
    public async Task MoveOnlyIntentIsSentPerValidationAndNeverLeaksIntoTheNextClick() {
        var worker = new UiaWorkerSupervisor(Fixture, "validation-intent");
        try {
            Assert.Equal(HintOutcome.NoTargets, (await worker.DiscoverAsync(new(1, 1),
                new(0, 0, 100, 100), CancellationToken.None)).Outcome);
            var move = await worker.ValidateAsync(1, CancellationToken.None, moveOnly: true);
            Assert.Equal(HintOutcome.Success, move.Outcome);
            Assert.Equal("MoveOnly", move.Reason);
            var click = await worker.ValidateAsync(1, CancellationToken.None);
            Assert.Equal(HintOutcome.Success, click.Outcome);
            Assert.Equal("DirectAction", click.Reason);
        } finally { Assert.True(await worker.RetireAsync()); }
    }

    [Theory]
    [InlineData(1000, HintOutcome.Timeout)]
    [InlineData(5000, HintOutcome.NoTargets)]
    public async Task ConfiguredDeadlineAllowsSlowDiscoveryButStillBoundsTheWorker(int timeoutMs, HintOutcome outcome) {
        var worker = new UiaWorkerSupervisor(Fixture, "delay 1800", discoveryTimeoutMs: timeoutMs);
        var watch = Stopwatch.StartNew();
        try {
            var response = await worker.DiscoverAsync(new(1, 1), new(0, 0, 100, 100), CancellationToken.None);
            Assert.Equal(outcome, response.Outcome);
            Assert.True(watch.ElapsedMilliseconds < timeoutMs + ElementHintProtocol.CleanupMs + 500);
            if (outcome == HintOutcome.NoTargets) {
                Assert.True(watch.ElapsedMilliseconds >= 1800);
                Assert.NotNull(worker.OwnedProcessId);
            } else {
                Assert.True(watch.ElapsedMilliseconds >= timeoutMs - 100);
                Assert.Null(worker.OwnedProcessId);
            }
        } finally { Assert.True(await worker.RetireAsync()); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    [InlineData(60001)]
    public void InvalidDeadlineCannotDisableTheWatchdog(int timeoutMs) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new UiaWorkerSupervisor(timeoutMs));

    [Theory]
    [InlineData(-1)]
    [InlineData(21)]
    public void InvalidCacheWindowCountIsRejected(int windows) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new UiaWorkerSupervisor(cacheWindowCount: windows));

    [Fact]
    public async Task ConfiguredCacheLimitReachesDiscoveryAndContinuationRequests() {
        var worker = new UiaWorkerSupervisor(Fixture, "incremental-cache-settings", cacheWindowCount: 7);
        try {
            var responses = new List<HintResponse>();
            await foreach (var response in worker.DiscoverIncrementallyAsync(new(1, 1), new(0, 0, 100, 100),
                CancellationToken.None)) { responses.Add(response); }
            Assert.Equal(2, responses.Count);
            Assert.All(responses, response => {
                Assert.Equal(HintOutcome.Success, response.Outcome);
                Assert.Equal("7", response.Reason);
            });
        } finally { Assert.True(await worker.RetireAsync()); }
    }

    [Fact]
    public async Task EmptyDiscoverySlicesAreCoalescedBeforeOverlayPublication() {
        var worker = new UiaWorkerSupervisor(Fixture, "incremental-unproductive");
        try {
            var responses = new List<HintResponse>();
            await foreach (var response in worker.DiscoverIncrementallyAsync(new(1, 1), new(0, 0, 100, 100),
                CancellationToken.None)) { responses.Add(response); }
            Assert.Equal([0, 1, 2], responses.Select(r => r.Targets.Length));
            Assert.False(responses[0].IsComplete);
            Assert.False(responses[1].IsComplete);
            Assert.True(responses[2].IsComplete);
            Assert.Equal(HintOutcome.Success, responses[2].Outcome);
        } finally { Assert.True(await worker.RetireAsync()); }
    }

    [Fact]
    public async Task PublishedHintsValidateBeforeDiscoveryContinuesAndReuseOneBoundedHelper() {
        var worker = new UiaWorkerSupervisor(Fixture, "incremental-hang", cacheWindowCount: 2);
        try {
            await using var scan = worker.DiscoverIncrementallyAsync(new(1, 1), new(0, 0, 100, 100),
                CancellationToken.None).GetAsyncEnumerator();
            Assert.True(await scan.MoveNextAsync());
            Assert.False(scan.Current.IsComplete);
            int? pid = worker.OwnedProcessId;
            var watch = Stopwatch.StartNew();
            Assert.Equal(HintOutcome.Success, (await worker.ValidateAsync(1, CancellationToken.None)).Outcome);
            Assert.True(watch.ElapsedMilliseconds < ElementHintProtocol.ValidationMs + 100);
            Assert.False(await scan.MoveNextAsync());
            Assert.True(await worker.ReleaseAsync());
            Assert.Equal(pid, worker.OwnedProcessId);
            await using var reopened = worker.DiscoverIncrementallyAsync(new(2, 1), new(0, 0, 100, 100),
                CancellationToken.None).GetAsyncEnumerator();
            Assert.True(await reopened.MoveNextAsync());
            Assert.Equal(pid, worker.OwnedProcessId);
        } finally { Assert.True(await worker.RetireAsync()); }
    }

    [Fact]
    public async Task ValidationWatchdogIncludesWaitingForABlockedDiscoverySlice() {
        var worker = new UiaWorkerSupervisor(Fixture, "incremental-hang", discoveryTimeoutMs: 10000);
        try {
            await using var scan = worker.DiscoverIncrementallyAsync(new(1, 1), new(0, 0, 100, 100),
                CancellationToken.None).GetAsyncEnumerator();
            Assert.True(await scan.MoveNextAsync());
            var blocked = scan.MoveNextAsync().AsTask();
            await Task.Delay(100);
            var watch = Stopwatch.StartNew();
            Assert.Equal(HintOutcome.Timeout, (await worker.ValidateAsync(1, CancellationToken.None)).Outcome);
            Assert.True(watch.ElapsedMilliseconds < ElementHintProtocol.ValidationMs + 250);
            Assert.False(await blocked);
        } finally { Assert.True(await worker.RetireAsync()); }
        Assert.Null(worker.OwnedProcessId);
    }

    [Fact]
    public async Task ContinuationCannotRemapPreviouslyPublishedTokens() {
        var worker = new UiaWorkerSupervisor(Fixture, "incremental-remap");
        try {
            var responses = new List<HintResponse>();
            await foreach (var response in worker.DiscoverIncrementallyAsync(new(1, 1), new(0, 0, 100, 100),
                CancellationToken.None)) { responses.Add(response); }
            Assert.Equal(2, responses.Count);
            Assert.Equal(HintOutcome.ProtocolError, responses[1].Outcome);
            Assert.Null(worker.OwnedProcessId);
        } finally { Assert.True(await worker.RetireAsync()); }
    }

    [Fact]
    public async Task NearlyExpiredDiscoveryDoesNotStartABlockedSliceOrDiscardValidatableHints() {
        const int timeoutMs = 2000;
        var worker = new UiaWorkerSupervisor(Fixture, "incremental-hang", discoveryTimeoutMs: timeoutMs);
        var watch = Stopwatch.StartNew();
        try {
            await using var scan = worker.DiscoverIncrementallyAsync(new(1, 1), new(0, 0, 100, 100),
                CancellationToken.None).GetAsyncEnumerator();
            Assert.True(await scan.MoveNextAsync());
            int? pid = worker.OwnedProcessId;
            await Task.Delay(Math.Max(0, timeoutMs - (int)watch.ElapsedMilliseconds - 60));
            Assert.True(await scan.MoveNextAsync());
            Assert.Equal(HintOutcome.Partial, scan.Current.Outcome);
            Assert.Equal("Discovery time limit reached", scan.Current.Reason);
            Assert.True(scan.Current.IsComplete);
            Assert.NotEmpty(scan.Current.Targets);
            Assert.Equal(pid, worker.OwnedProcessId);
            Assert.False(await scan.MoveNextAsync());
            Assert.Equal(HintOutcome.Success, (await worker.ValidateAsync(1, CancellationToken.None)).Outcome);
        } finally { Assert.True(await worker.RetireAsync()); }
    }

    [Fact]
    public async Task CooperativeDeadlineKeepsExplicitPartialTargetsFreshlyValidatable() {
        var worker = new UiaWorkerSupervisor(Fixture, "incremental-endless", discoveryTimeoutMs: 500);
        try {
            HintResponse? last = null;
            await foreach (var response in worker.DiscoverIncrementallyAsync(new(1, 1), new(0, 0, 100, 100),
                CancellationToken.None)) { last = response; }
            Assert.NotNull(last);
            Assert.Equal(HintOutcome.Partial, last.Outcome);
            Assert.Equal("Discovery time limit reached", last.Reason);
            Assert.True(last.IsComplete);
            Assert.NotEmpty(last.Targets);
            Assert.Equal(HintOutcome.Success, (await worker.ValidateAsync(1, CancellationToken.None)).Outcome);
        } finally { Assert.True(await worker.RetireAsync()); }
    }
}

public class ElementHintPackagingTests {
    [Fact]
    public async Task BundledDevelopmentWorkerRunsVersionedProtocol() {
        string path = Path.Combine(AppContext.BaseDirectory, "uia-worker", "Klikety.UiaWorker.exe");
        Assert.True(File.Exists(path));
        Assert.True(File.Exists(Path.ChangeExtension(path, ".runtimeconfig.json")));
        var worker = new UiaWorkerSupervisor(path);
        var watch = Stopwatch.StartNew();
        try {
            var response = await worker.DiscoverAsync(new(0, Environment.ProcessId), new(0, 0, 100, 100), CancellationToken.None);
            Assert.True(response.Outcome == HintOutcome.InvalidRoot,
                $"Expected InvalidRoot; actual={response.Outcome}; reason={response.Reason}; elapsed={watch.ElapsedMilliseconds} ms; ownedPid={worker.OwnedProcessId}");
        } finally { Assert.True(await worker.RetireAsync()); }
    }
}
