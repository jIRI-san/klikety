using System.Diagnostics;

using Klikety.Automation;
using Klikety.Interop;
using Klikety.Services;

using Xunit.Abstractions;

namespace Klikety.SmokeTests;

[Trait("Category", "Smoke")]
public class ElementHintSmokeTests(ITestOutputHelper output) {
    private sealed class Fixture : IAsyncDisposable {
        private readonly Process _process;
        public long Hwnd { get; private set; }
        public string? Startup { get; private set; }
        public static async Task<Fixture> StartAsync() {
            string path = Path.Combine(AppContext.BaseDirectory, "worker-fixture", "Klikety.WorkerFixture.exe");
            var fixture = new Fixture(Process.Start(new ProcessStartInfo(path, "ui") {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            })!);
            try {
                fixture.Hwnd = long.Parse((await fixture._process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)))!);
                fixture.Startup = await fixture._process.StandardError.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(1));
                return fixture;
            } catch {
                await fixture.DisposeAsync();
                throw;
            }
        }
        private Fixture(Process process) => _process = process;
        public async Task<string?> CommandAsync(string command) {
            await _process.StandardInput.WriteLineAsync(command); await _process.StandardInput.FlushAsync();
            return await _process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        public async ValueTask DisposeAsync() {
            if (!_process.HasExited) {
                _process.StandardInput.Close();
                try { await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(1)); } catch (TimeoutException) {
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(1));
                }
            }
            _process.Dispose();
        }
    }
    private async Task FocusAsync(Fixture fixture) {
        string? request = await fixture.CommandAsync("focus");
        var actual = PlatformServices.Instance.ForegroundWindow.GetForegroundWindowHandle().ToInt64();
        output.WriteLine($"{fixture.Startup}; {request}; observed={actual}; expected={fixture.Hwnd}");
        if (actual != fixture.Hwnd) {
            bool accepted = NativeMethods.SetForegroundWindow((nint)fixture.Hwnd);
            var settle = Stopwatch.StartNew();
            while (actual != fixture.Hwnd && settle.ElapsedMilliseconds < 200) {
                await Task.Delay(20);
                actual = PlatformServices.Instance.ForegroundWindow.GetForegroundWindowHandle().ToInt64();
            }
            output.WriteLine($"parent-focus accepted={accepted}; observed={actual}; expected={fixture.Hwnd}; settle={settle.ElapsedMilliseconds} ms");
        }
        Assert.True(actual == fixture.Hwnd, $"Fixture activation did not acquire foreground: {request}; actual={actual}, expected={fixture.Hwnd}");
    }
    private async Task<HintResponse> ValidateAsync(UiaWorkerSupervisor worker, Fixture fixture, int token) {
        long before = PlatformServices.Instance.ForegroundWindow.GetForegroundWindowHandle().ToInt64();
        var watch = Stopwatch.StartNew();
        var response = await worker.ValidateAsync(token, CancellationToken.None);
        long after = PlatformServices.Instance.ForegroundWindow.GetForegroundWindowHandle().ToInt64();
        output.WriteLine($"validation={response.Outcome}: {response.Reason}; elapsed={watch.ElapsedMilliseconds} ms; before={before}; after={after}; expected={fixture.Hwnd}");
        return response;
    }

    [Fact]
    public async Task RealWpfDiscoveryStaysWithinRootEvenWhenAnotherWindowIsForeground() {
        await using var fixture = await Fixture.StartAsync();
        var worker = new UiaWorkerSupervisor();
        try {
            Assert.Equal("done", await fixture.CommandAsync("other-window"));
            var context = new ElementPointGuard().Capture((nint)fixture.Hwnd, Environment.ProcessId);
            var result = await worker.DiscoverAsync(context, new(-1000, -1000, 4000, 3000), CancellationToken.None);
            output.WriteLine($"root discovery={result.Outcome}: {result.Reason}; visited={result.Visited}; targets={result.Targets.Length}");
            Assert.Equal(HintOutcome.Success, result.Outcome);
            Assert.Single(result.Targets, t => t.ControlType == 50000);
            Assert.Equal(2, result.Targets.Count(t => t.ControlType == 50004));
            Assert.Single(result.Targets, t => t.ControlType == 50002);
            Assert.DoesNotContain(result.Targets, t => t.ControlType == 50020);
            var target = Assert.Single(result.Targets, t => t.ControlType == 50000);
            long actual = PlatformServices.Instance.ForegroundWindow.GetForegroundWindowHandle().ToInt64();
            Assert.NotEqual(fixture.Hwnd, actual);
            var validation = await ValidateAsync(worker, fixture, target.Token);
            Assert.Equal(HintOutcome.StaleTarget, validation.Outcome);
            Assert.StartsWith("Application lost foreground", validation.Reason);
            Assert.Null(validation.Point);
        } finally { Assert.True(await worker.RetireAsync()); }
    }

    [Fact]
    public async Task DestroyedCapturedWindowRejectsBeforeForegroundOrPointValidation() {
        await using var fixture = await Fixture.StartAsync();
        var worker = new UiaWorkerSupervisor();
        try {
            var context = new ElementPointGuard().Capture((nint)fixture.Hwnd, Environment.ProcessId);
            var bounds = PlatformServices.Instance.ForegroundWindow.GetWindowBounds((nint)fixture.Hwnd);
            var result = await worker.DiscoverAsync(context, new(bounds.X, bounds.Y, bounds.Width, bounds.Height), CancellationToken.None);
            Assert.Equal(HintOutcome.Success, result.Outcome);
            var target = Assert.Single(result.Targets, t => t.ControlType == 50000);
            Assert.Equal("done", await fixture.CommandAsync("close"));
            var response = await ValidateAsync(worker, fixture, target.Token);
            Assert.Equal(HintOutcome.StaleTarget, response.Outcome);
            Assert.Equal("Application or snapshot changed", response.Reason);
            Assert.Null(response.Point);
        } finally { Assert.True(await worker.RetireAsync()); }
    }
    [Theory]
    [InlineData("move")]
    [InlineData("move-control")]
    [InlineData("disable")]
    [InlineData("replace")]
    [InlineData("close")]
    public async Task RealWpfSnapshotIncludesIndependentControlsAndRejectsStaleTargets(string mutation) {
        await using var fixture = await Fixture.StartAsync();
        var worker = new UiaWorkerSupervisor();
        try {
            var guard = new ElementPointGuard();
            await FocusAsync(fixture);
            var context = guard.Capture((nint)fixture.Hwnd, Environment.ProcessId);
            var windowBounds = PlatformServices.Instance.ForegroundWindow.GetWindowBounds((nint)fixture.Hwnd);
            var result = await worker.DiscoverAsync(context, new(windowBounds.X, windowBounds.Y,
                windowBounds.Width, windowBounds.Height), CancellationToken.None);
            Assert.Equal(HintOutcome.Success, result.Outcome);
            Assert.Single(result.Targets, t => t.ControlType == 50000);
            Assert.Equal(2, result.Targets.Count(t => t.ControlType == 50004));
            Assert.Single(result.Targets, t => t.ControlType == 50002);
            Assert.DoesNotContain(result.Targets, t => t.ControlType == 50020);
            var target = Assert.Single(result.Targets, t => t.ControlType == 50000);
            var healthy = await ValidateAsync(worker, fixture, target.Token);
            Assert.True(healthy.Outcome == HintOutcome.Success, $"{healthy.Outcome}: {healthy.Reason}");
            Assert.NotNull(healthy.Point);
            Assert.Equal("done", await fixture.CommandAsync(mutation));
            var validation = await ValidateAsync(worker, fixture, target.Token);
            Assert.NotEqual(HintOutcome.Success, validation.Outcome);
            Assert.Null(validation.Point);
        } finally {
            Assert.True(await worker.RetireAsync());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealCoveredControlUsesInteriorPointOrRejectsWholeCoverage(bool whole) {
        await using var fixture = await Fixture.StartAsync();
        var worker = new UiaWorkerSupervisor();
        try {
            await FocusAsync(fixture);
            var context = new ElementPointGuard().Capture((nint)fixture.Hwnd, Environment.ProcessId);
            var bounds = PlatformServices.Instance.ForegroundWindow.GetWindowBounds((nint)fixture.Hwnd);
            var scan = await worker.DiscoverAsync(context, new(bounds.X, bounds.Y, bounds.Width, bounds.Height), CancellationToken.None);
            Assert.Equal(HintOutcome.Success, scan.Outcome);
            var target = Assert.Single(scan.Targets, t => t.ControlType == 50000);
            Assert.Equal("done", await fixture.CommandAsync(whole ? "cover-all" : "cover-center"));
            var response = await ValidateAsync(worker, fixture, target.Token);
            Assert.Equal(whole ? HintOutcome.NoSafePoint : HintOutcome.Success, response.Outcome);
            if (whole) { Assert.Null(response.Point); } else {
                Assert.NotNull(response.Point);
                Assert.NotEqual(target.VisibleBounds.Center, response.Point.Value);
                Assert.True(target.VisibleBounds.Contains(response.Point.Value));
            }
        } finally { Assert.True(await worker.RetireAsync()); }
    }

    [Fact]
    public async Task OtherTopLevelWindowIsExcludedAndActualForegroundLossIsRejected() {
        await using var fixture = await Fixture.StartAsync();
        var worker = new UiaWorkerSupervisor();
        try {
            await FocusAsync(fixture);
            Assert.Equal("done", await fixture.CommandAsync("other-window"));
            await FocusAsync(fixture);
            var context = new ElementPointGuard().Capture((nint)fixture.Hwnd, Environment.ProcessId);
            var scan = await worker.DiscoverAsync(context, new(-1000, -1000, 4000, 3000), CancellationToken.None);
            Assert.Equal(HintOutcome.Success, scan.Outcome);
            var target = Assert.Single(scan.Targets, t => t.ControlType == 50000);
            Assert.Equal(HintOutcome.Success, (await ValidateAsync(worker, fixture, target.Token)).Outcome);
            Assert.Equal("done", await fixture.CommandAsync("other-window"));
            Assert.NotEqual(fixture.Hwnd, PlatformServices.Instance.ForegroundWindow.GetForegroundWindowHandle().ToInt64());
            var response = await ValidateAsync(worker, fixture, target.Token);
            Assert.Equal(HintOutcome.StaleTarget, response.Outcome);
            Assert.StartsWith("Application lost foreground", response.Reason);
            Assert.Null(response.Point);
        } finally { Assert.True(await worker.RetireAsync()); }
    }
}
