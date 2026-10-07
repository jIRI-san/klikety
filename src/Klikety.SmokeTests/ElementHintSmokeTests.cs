using System.Diagnostics;

using Klikety.Automation;
using Klikety.Services;

namespace Klikety.SmokeTests;

[Trait("Category", "Smoke")]
public class ElementHintSmokeTests {
    [Theory]
    [InlineData("move")]
    [InlineData("disable")]
    [InlineData("replace")]
    public async Task RealWpfSnapshotIncludesIndependentControlsAndRejectsStaleTargets(string mutation) {
        string path = Path.Combine(AppContext.BaseDirectory, "worker-fixture", "Klikety.WorkerFixture.exe");
        using var fixture = Process.Start(new ProcessStartInfo(path, "ui") {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
        })!;
        var worker = new UiaWorkerSupervisor();
        try {
            long hwnd = long.Parse((await fixture.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)))!);
            var guard = new ElementPointGuard();
            var context = guard.Capture((nint)hwnd, Environment.ProcessId);
            var windowBounds = PlatformServices.Instance.ForegroundWindow.GetWindowBounds((nint)hwnd);
            var result = await worker.DiscoverAsync(context, new(windowBounds.X, windowBounds.Y,
                windowBounds.Width, windowBounds.Height), CancellationToken.None);
            Assert.Equal(HintOutcome.Success, result.Outcome);
            Assert.Single(result.Targets, t => t.ControlType == 50000);
            Assert.Equal(2, result.Targets.Count(t => t.ControlType == 50004));
            Assert.Single(result.Targets, t => t.ControlType == 50002);
            Assert.DoesNotContain(result.Targets, t => t.ControlType == 50020);
            var target = Assert.Single(result.Targets, t => t.ControlType == 50000);
            PlatformServices.Instance.ForegroundWindow.SetForegroundWindow((nint)hwnd);
            var healthy = await worker.ValidateAsync(target.Token, CancellationToken.None);
            Assert.True(healthy.Outcome == HintOutcome.Success, $"{healthy.Outcome}: {healthy.Reason}");
            Assert.NotNull(healthy.Point);
            await fixture.StandardInput.WriteLineAsync(mutation); await fixture.StandardInput.FlushAsync();
            Assert.Equal("done", await fixture.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            PlatformServices.Instance.ForegroundWindow.SetForegroundWindow((nint)hwnd);
            var validation = await worker.ValidateAsync(target.Token, CancellationToken.None);
            Assert.NotEqual(HintOutcome.Success, validation.Outcome);
            Assert.Null(validation.Point);
        } finally {
            Assert.True(await worker.RetireAsync());
            if (!fixture.HasExited) {
                fixture.StandardInput.Close();
                if (!fixture.WaitForExit(1000)) { fixture.Kill(entireProcessTree: true); }
            }
        }
    }
}
