using System.Diagnostics;
using System.IO;

using Klikety.Automation;

namespace Klikety.Tests;

public class UiaWorkerSupervisorTests {
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "worker-fixture", "Klikety.WorkerFixture.exe");

    [Theory]
    [InlineData("hang", HintOutcome.Timeout)]
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
        var worker = new UiaWorkerSupervisor(Fixture, "hang");
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
        var validationWorker = new UiaWorkerSupervisor(Fixture, "validation-hang");
        await validationWorker.DiscoverAsync(new(1, 1), new(0, 0, 100, 100), CancellationToken.None);
        var watch = Stopwatch.StartNew();
        Assert.Equal(HintOutcome.Timeout, (await validationWorker.ValidateAsync(1, CancellationToken.None)).Outcome);
        Assert.True(watch.ElapsedMilliseconds < 1000);
        Assert.Null(validationWorker.OwnedProcessId);
    }
}

public class ElementHintPackagingTests {
    [Fact]
    public async Task BundledDevelopmentWorkerRunsVersionedProtocol() {
        string path = Path.Combine(AppContext.BaseDirectory, "uia-worker", "Klikety.UiaWorker.exe");
        Assert.True(File.Exists(path));
        Assert.True(File.Exists(Path.ChangeExtension(path, ".runtimeconfig.json")));
        var worker = new UiaWorkerSupervisor(path);
        var response = await worker.DiscoverAsync(new(0, Environment.ProcessId), new(0, 0, 100, 100), CancellationToken.None);
        Assert.Equal(HintOutcome.InvalidRoot, response.Outcome);
        Assert.True(await worker.RetireAsync());
    }
}
