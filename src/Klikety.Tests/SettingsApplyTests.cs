using System.Text.Json.Nodes;

using Klikety.Config;
using Klikety.Input;
using Klikety.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Klikety.Tests;

public sealed class SettingsApplyTests {
    [Fact]
    public void PreviousLoggerLivesThroughCandidateTeardownRecoveryAndOutcomeReporting() {
        var prior = new TrackingLoggerFactory();
        var candidate = new TrackingLoggerFactory();
        var recovered = new TrackingLoggerFactory();
        var lifetime = new SettingsLoggerLifetime();
        lifetime.Retain(prior);
        lifetime.Retain(prior);
        Assert.Equal(0, prior.Disposals);
        lifetime.Retain(candidate);
        Assert.Equal(0, prior.Disposals);
        Assert.Equal(0, candidate.Disposals);
        lifetime.Complete(recovered);
        Assert.Equal(1, prior.Disposals);
        Assert.Equal(1, candidate.Disposals);
        Assert.Equal(0, recovered.Disposals);
        lifetime.Retain(recovered);
        lifetime.Complete(recovered);
        Assert.Equal(0, recovered.Disposals);
    }

    private sealed class TrackingLoggerFactory : ILoggerFactory {
        public int Disposals { get; private set; }
        public bool FailDispose { get; set; }
        public ILogger CreateLogger(string categoryName) => NullLogger.Instance;
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() {
            Disposals++;
            if (FailDispose) { throw new IOException("logger still owned"); }
        }
    }

    [Fact]
    public void LoggerCleanupFailureIsReportedWithoutSkippingOthersAndCanBeRetried() {
        var broken = new TrackingLoggerFactory { FailDispose = true };
        var other = new TrackingLoggerFactory();
        var lifetime = new SettingsLoggerLifetime();
        lifetime.Retain(broken);
        lifetime.Retain(other);
        Assert.Contains("logger still owned", Assert.Throws<InvalidOperationException>(() => lifetime.Complete(null)).Message);
        Assert.Equal(1, broken.Disposals);
        Assert.Equal(1, other.Disposals);
        broken.FailDispose = false;
        lifetime.Complete(null);
        Assert.Equal(2, broken.Disposals);
        Assert.Equal(1, other.Disposals);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    [InlineData(true, true, false)]
    public void ReplacementUsesCapturedModelPreservesRuntimeTogglesAndOwnsOneGeneration(
        bool hudEnabled, bool scrollPaused, bool scrollEnabled) {
        var previous = new SettingsRuntimeSnapshot(new ConfigModel { Theme = "old" }, hudEnabled, scrollPaused);
        var candidate = new ConfigModel { Theme = "new", ScrollHotKeys = new() { Enabled = scrollEnabled } };
        var releases = 0;
        var creates = 0;
        var toggleCalls = new List<(bool Hud, bool Paused)>();
        var outcome = SettingsRuntimeReplacement.Activate(previous, candidate,
            () => { releases++; return []; },
            model => { Assert.Same(candidate, model); creates++; return new(true, ["advisory"]); },
            (hud, paused) => { toggleCalls.Add((hud, paused)); return SettingsApplyOutcome.Success; });
        Assert.True(outcome.Succeeded);
        Assert.Equal(1, creates);
        Assert.Equal(1, releases);
        Assert.Equal([(hudEnabled, scrollPaused && scrollEnabled)], toggleCalls);
        Assert.Equal(["advisory"], outcome.Issues);
    }

    [Fact]
    public void FailedHudRefreshReleasesCandidateAndDoesNotPublishSuccessfulActivation() {
        var releases = 0;
        var outcome = SettingsRuntimeReplacement.Activate(new(new ConfigModel(), true, true), new ConfigModel(),
            () => { releases++; return []; },
            _ => SettingsApplyOutcome.Success,
            (_, _) => new(false, ["HUD creation failed"]));
        Assert.False(outcome.Succeeded);
        Assert.Equal(2, releases);
        Assert.Equal(["HUD creation failed"], outcome.Issues);
    }

    [Fact]
    public void FailedOldResourceTeardownDoesNotConstructCandidate() {
        var outcome = SettingsRuntimeReplacement.Activate(new(new ConfigModel(), false, false), new ConfigModel(),
            () => ["old hook cleanup failed"],
            _ => throw new Xunit.Sdk.XunitException("Unsafe construction"), (_, _) => SettingsApplyOutcome.Success);
        Assert.False(outcome.Succeeded);
        Assert.Equal(["old hook cleanup failed"], outcome.Issues);
    }

    [Fact]
    public void RepeatedTransitionsReleasePreviousHandlersBeforeAcquiringTheNextGeneration() {
        var active = 1;
        var maxActive = active;
        var previous = new SettingsRuntimeSnapshot(new ConfigModel(), false, false);
        for (var index = 0; index < 10; index++) {
            var candidate = new ConfigModel { Theme = index.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            var result = SettingsRuntimeReplacement.Activate(previous, candidate,
                () => { active--; return []; },
                model => { active++; maxActive = Math.Max(maxActive, active); return SettingsApplyOutcome.Success; },
                (_, _) => SettingsApplyOutcome.Success);
            Assert.True(result.Succeeded);
            previous = new(candidate, false, false);
        }
        Assert.Equal(1, active);
        Assert.Equal(1, maxActive);
    }

    [Theory]
    [InlineData("logger")]
    [InlineData("overlay")]
    [InlineData("coordinator")]
    [InlineData("main")]
    [InlineData("scroll-up")]
    [InlineData("scroll-down")]
    [InlineData("macro")]
    [InlineData("indicator")]
    [InlineData("hud")]
    public void PartialCandidateReleasesEveryAcquiredResourceInReverseOrder(string failedStage) {
        string[] stages = ["logger", "overlay", "coordinator", "main", "scroll-up", "scroll-down", "macro", "indicator", "hud"];
        var acquired = new List<string>();
        var released = new List<string>();
        var error = Assert.Throws<InvalidOperationException>(() => SettingsRuntimeResources.Create(owner => {
            foreach (var stage in stages) {
                acquired.Add(stage);
                owner.Own(stage, () => released.Add(stage));
                owner.Checkpoint(stage);
            }
        }, stage => {
            if (stage == failedStage) { throw new IOException("Injected " + stage); }
        }));

        Assert.Contains("Injected " + failedStage, error.Message);
        Assert.Equal(acquired.AsEnumerable().Reverse(), released);
    }

    [Fact]
    public void CleanupFailureDoesNotSkipOtherResourcesOrReportSuccess() {
        var released = new List<string>();
        var owner = new SettingsRuntimeResources();
        var fail = true;
        owner.Own("oldest", () => released.Add("oldest"));
        owner.Own("broken", () => {
            if (fail) { throw new IOException("cleanup"); }
            released.Add("broken");
        });
        owner.Own("newest", () => released.Add("newest"));
        Assert.Equal(["broken cleanup failed: cleanup"], owner.Release());
        Assert.Equal(["newest", "oldest"], released);
        Assert.True(owner.HasResources);
        fail = false;
        Assert.Empty(owner.Release());
        Assert.False(owner.HasResources);
        Assert.Equal(["newest", "oldest", "broken"], released);
    }

    [Fact]
    public void FailedCompositionRetainsFailedCleanupOwnershipForRetry() {
        SettingsRuntimeResources? pending = null;
        var fail = true;
        Assert.Throws<InvalidOperationException>(() => SettingsRuntimeResources.Create(owner => {
            owner.Own("hook callback", () => { if (fail) { throw new IOException("still hooked"); } });
            throw new IOException("composition failed");
        }, retainFailed: owner => pending = owner));
        Assert.NotNull(pending);
        Assert.True(pending.HasResources);
        fail = false;
        Assert.Empty(pending.Release());
        Assert.False(pending.HasResources);
    }

    [Fact]
    public void NativeUnregisterFailureKeepsOwnershipUntilSuccessfulRetry() {
        var registered = true;
        var calls = 0;
        var issue = HotKeyRegistrationCleanup.Release(ref registered, () => {
            calls++;
            System.Runtime.InteropServices.Marshal.SetLastPInvokeError(5);
            return false;
        });
        Assert.False(string.IsNullOrEmpty(issue));
        Assert.True(registered);
        Assert.Null(HotKeyRegistrationCleanup.Release(ref registered, () => { calls++; return true; }));
        Assert.False(registered);
        Assert.Null(HotKeyRegistrationCleanup.Release(ref registered, () => throw new Xunit.Sdk.XunitException("Not owned")));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void OwnershipTransferReleasesCoordinatorBeforeItsWindowsAndDoesNotDoubleDisposeHook() {
        var released = new List<string>();
        var owner = new SettingsRuntimeResources();
        owner.Own("hook", () => released.Add("hook"));
        owner.Own("overlay", () => released.Add("overlay"));
        owner.Own("coordinator", () => { released.Add("coordinator"); released.Add("hook"); });
        owner.Transfer("hook");
        owner.Own("picker", () => released.Add("picker"));
        owner.ReleaseFirst("coordinator");
        Assert.Empty(owner.Release());
        Assert.Equal(["coordinator", "hook", "picker", "overlay"], released);
    }

    [Fact]
    public void GateRejectsBusyAndReentrantSaveReloadResetBeforeAnyMutation() {
        var idle = false;
        var gate = new SettingsOperationGate(() => idle);
        Assert.Contains("idle", Assert.Throws<InvalidOperationException>(() => gate.Enter()).Message);
        idle = true;
        using (gate.Enter()) {
            foreach (var operation in new[] { "save", "reload", "reset" }) {
                Assert.Contains("in progress", Assert.Throws<InvalidOperationException>(() => gate.Enter()).Message);
            }
        }
        using var next = gate.Enter();
    }

    [Fact]
    public async Task GateRejectsConcurrentThreadWithoutBlockingOrMutating() {
        var gate = new SettingsOperationGate(() => true);
        using var active = gate.Enter();
        var failure = await Task.Run(() => Record.Exception(() => gate.Enter()));
        Assert.IsType<InvalidOperationException>(failure);
    }

    [Fact]
    public void ShortcutInventoryIncludesEveryEnabledRegistrationAndDefersOwnedReassignment() {
        var config = new ConfigModel { ScrollHotKeys = new() { Enabled = true } };
        var inventory = SettingsShortcutInventory.From(config);
        Assert.Equal(["hotKey", "scrollHotkeys.scrollUpKey", "scrollHotkeys.scrollDownKey", "macros.globalHotKey"],
            inventory.Select(entry => entry.Name));
        var probed = new List<HotKeyConfig>();
        SettingsShortcutInventory.Preflight(config, inventory, key => { probed.Add(key); return null; });
        Assert.Empty(probed);
        SettingsShortcutInventory.Preflight(config, [], key => { probed.Add(key); return null; });
        Assert.Equal(4, probed.Count);
        Assert.Equal(2, SettingsShortcutInventory.From(config, scrollPaused: true).Count);

        var reassigned = new ConfigModel {
            HotKey = config.ScrollHotKeys.ScrollUpKey,
            ScrollHotKeys = new() { Enabled = false },
            Macros = new() { GlobalHotKey = config.HotKey },
        };
        SettingsShortcutInventory.Preflight(reassigned, inventory, _ => throw new Xunit.Sdk.XunitException("Probed owned key"));
    }

    [Theory]
    [InlineData("hotKey")]
    [InlineData("scrollHotkeys.scrollUpKey")]
    [InlineData("scrollHotkeys.scrollDownKey")]
    [InlineData("macros.globalHotKey")]
    public void PreflightRejectsEachExternalRegistrationFailureBeforeWriting(string name) {
        var config = new ConfigModel { ScrollHotKeys = new() { Enabled = true } };
        var failed = SettingsShortcutInventory.From(config).Single(entry => entry.Name == name).HotKey;
        var error = Assert.Throws<InvalidDataException>(() => SettingsShortcutInventory.Preflight(config, [],
            key => key == failed ? "occupied" : null));
        Assert.Contains(name, error.Message);
    }

    [Fact]
    public void PreflightRejectsGlobalCollisionIncludingMacroVersusScroll() {
        var scroll = new ScrollHotKeyConfig { Enabled = true };
        var config = new ConfigModel { ScrollHotKeys = scroll, Macros = new() { GlobalHotKey = scroll.ScrollUpKey } };
        Assert.Contains("duplicate", Assert.Throws<InvalidDataException>(() =>
            SettingsShortcutInventory.Preflight(config, [], _ => null)).Message);
    }
}
