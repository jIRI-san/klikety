using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

using Microsoft.Win32.SafeHandles;

namespace Klikety.Automation;

public sealed record ElementTargetContext(nint Hwnd, int OwnerProcessId, int ProcessId = 0, long ProcessStart = 0);

public interface IElementHintService {
    Task<HintResponse> DiscoverAsync(ElementTargetContext context, HintRect region, CancellationToken ct);
    Task<HintResponse> ValidateAsync(int token, CancellationToken ct);
    Task<bool> RetireAsync();
}

public interface IElementPointGuard {
    ElementTargetContext Capture(nint hwnd, int ownerProcessId);
    bool IsCurrent(ElementTargetContext context, int rootProcessId, HintPoint point);
}

public sealed class ElementPointGuard : IElementPointGuard {
    public ElementTargetContext Capture(nint hwnd, int ownerProcessId) {
        if (WorkerNative.GetWindowThreadProcessId(hwnd, out uint pid) == 0 || pid == 0) {
            return new(hwnd, ownerProcessId);
        }
        try {
            using var process = Process.GetProcessById((int)pid);
            return new(hwnd, ownerProcessId, (int)pid, process.StartTime.ToUniversalTime().Ticks);
        } catch (Exception ex) when (ex is Win32Exception or ArgumentException or InvalidOperationException) {
            return new(hwnd, ownerProcessId);
        }
    }
    public bool IsCurrent(ElementTargetContext context, int rootProcessId, HintPoint point) =>
        WorkerNative.GetForegroundWindow() == context.Hwnd &&
        WorkerNative.GetWindowThreadProcessId(context.Hwnd, out uint pid) != 0 &&
        pid == rootProcessId && WorkerNative.GetAncestor(WorkerNative.WindowFromPoint(point), 2) == context.Hwnd;
}

public sealed class UiaWorkerSupervisor : IElementHintService {
    private readonly SemaphoreSlim _gate = new(1);
    private readonly string _executable;
    private readonly string? _arguments;
    private Process? _process;
    private SafeFileHandle? _job;
    private Task? _stderr;
    private Task? _responseRead;
    private Task? _startup;
    private bool _started;
    private HintRequest? _snapshot;
    private bool _cleanupFailed;
    internal int? OwnedProcessId => _started && _process is not null ? _process.Id : null;

    public UiaWorkerSupervisor() : this(Path.Combine(AppContext.BaseDirectory, "uia-worker", "Klikety.UiaWorker.exe")) { }
    internal UiaWorkerSupervisor(string executable, string? arguments = null) {
        _executable = Path.GetFullPath(executable);
        _arguments = arguments;
    }

    public async Task<HintResponse> DiscoverAsync(ElementTargetContext context, HintRect region, CancellationToken ct) {
        var request = new HintRequest(ElementHintProtocol.Version, Guid.NewGuid(), Guid.NewGuid(),
            HintCommand.Discover, context.Hwnd.ToInt64(), context.OwnerProcessId, region,
            ExpectedProcessId: context.ProcessId, ExpectedProcessStart: context.ProcessStart);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try {
            if (!await RetireCoreAsync().ConfigureAwait(false)) { return Failure(request, HintOutcome.CleanupFailed); }
            _snapshot = request;
            return await ExchangeAsync(request, ElementHintProtocol.DiscoveryMs, start: true, ct).ConfigureAwait(false);
        } finally { _gate.Release(); }
    }

    public async Task<HintResponse> ValidateAsync(int token, CancellationToken ct) {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try {
            if (_snapshot is null) {
                return Failure(new(ElementHintProtocol.Version, Guid.Empty, Guid.Empty, HintCommand.Validate), HintOutcome.StaleTarget);
            }
            return await ExchangeAsync(_snapshot with { Command = HintCommand.Validate, RequestId = Guid.NewGuid(), Token = token },
                ElementHintProtocol.ValidationMs, start: false, ct).ConfigureAwait(false);
        } finally { _gate.Release(); }
    }

    private async Task<HintResponse> ExchangeAsync(HintRequest request, int timeoutMs, bool start, CancellationToken ct) {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeoutMs);
        var token = deadline.Token;
        try {
            if (start) {
                // Start/job assignment are off the UI thread and part of the absolute scan deadline.
                _startup = Task.Run(() => StartWorker(token), CancellationToken.None);
                await _startup.WaitAsync(token).ConfigureAwait(false);
            }
            if (_process is null || _process.HasExited) { throw new IOException("UIA worker exited."); }
            await ElementHintProtocol.WriteAsync(_process.StandardInput.BaseStream, request,
                ElementHintProtocol.MaxRequestBytes, token).ConfigureAwait(false);
            var responseTask = ElementHintProtocol.ReadAsync<HintResponse>(_process.StandardOutput.BaseStream,
                ElementHintProtocol.MaxResponseBytes, token);
            _responseRead = responseTask;
            if (_stderr is not null && await Task.WhenAny(responseTask, _stderr).ConfigureAwait(false) == _stderr) {
                await _stderr.ConfigureAwait(false);
            }
            var response = await responseTask.ConfigureAwait(false);
            ElementHintProtocol.CheckResponse(request, response);
            token.ThrowIfCancellationRequested();
            _responseRead = null;
            return response;
        } catch (OperationCanceledException) {
            return await FailAndRetireAsync(request, ct.IsCancellationRequested ? HintOutcome.Cancelled : HintOutcome.Timeout).ConfigureAwait(false);
        } catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or Win32Exception or
            System.Text.Json.JsonException or UnauthorizedAccessException) {
            return await FailAndRetireAsync(request, ex is InvalidDataException or System.Text.Json.JsonException
                ? HintOutcome.ProtocolError : ex is UnauthorizedAccessException ? HintOutcome.AccessDenied
                : start && !_started ? HintOutcome.Unavailable : HintOutcome.ProviderError).ConfigureAwait(false);
        }
    }

    private void StartWorker(CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        if (_cleanupFailed) { throw new InvalidOperationException("Previous UIA helper did not exit."); }
        var process = new Process {
            StartInfo = new ProcessStartInfo(_executable) {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                Arguments = _arguments ?? string.Empty,
            },
        };
        _job = WorkerNative.CreateKillOnCloseJob();
        _process = process;
        if (!process.Start()) { throw new IOException("UIA worker failed to start."); }
        _started = true;
        if (!WorkerNative.AssignProcessToJobObject(_job, process.Handle)) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
        if (ct.IsCancellationRequested) { _job.Dispose(); ct.ThrowIfCancellationRequested(); }
        _stderr = DrainDiagnosticsAsync(process.StandardError.BaseStream);
    }

    private static async Task DrainDiagnosticsAsync(Stream stream) {
        byte[] buffer = new byte[4096];
        int total = 0, count;
        while ((count = await stream.ReadAsync(buffer).ConfigureAwait(false)) > 0) {
            total += count;
            if (total > ElementHintProtocol.MaxDiagnosticBytes) { throw new InvalidDataException("UIA diagnostic byte limit exceeded."); }
        }
    }

    private async Task<HintResponse> FailAndRetireAsync(HintRequest r, HintOutcome outcome) =>
        Failure(r, await RetireCoreAsync().ConfigureAwait(false) ? outcome : HintOutcome.CleanupFailed);

    private static HintResponse Failure(HintRequest r, HintOutcome outcome) =>
        new(ElementHintProtocol.Version, r.SessionId, r.RequestId, outcome, [], Reason: outcome.ToString());

    public async Task<bool> RetireAsync() {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return await RetireCoreAsync().ConfigureAwait(false); } finally { _gate.Release(); }
    }

    private async Task<bool> RetireCoreAsync() {
        _snapshot = null;
        if (_cleanupFailed) { return false; }
        using var cleanup = new CancellationTokenSource(ElementHintProtocol.CleanupMs);
        if (_startup is not null) {
            try { await _startup.WaitAsync(cleanup.Token).ConfigureAwait(false); } catch (OperationCanceledException) when (cleanup.IsCancellationRequested) { _cleanupFailed = true; return false; } catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or OperationCanceledException) {
                // Startup failure still requires retiring any process/job created before the failure.
            }
            _startup = null;
        }
        if (_process is null) { _job?.Dispose(); _job = null; return true; }
        try {
            _job?.Dispose(); // kills only this owned job, including on parent death
            _job = null;
            if (_started) {
                if (!_process.HasExited) { _process.Kill(entireProcessTree: true); }
                await _process.WaitForExitAsync(cleanup.Token).ConfigureAwait(false);
            }
        } catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or OperationCanceledException) {
            if (_started && !_process.HasExited) { _cleanupFailed = true; return false; }
        } finally {
            if (!_cleanupFailed) {
                _process.Dispose();
                _process = null;
                _started = false;
                if (_responseRead is { } read) {
                    try { await read.WaitAsync(cleanup.Token).ConfigureAwait(false); } catch (Exception ex) when (ex is IOException or InvalidDataException or
                        System.Text.Json.JsonException or ObjectDisposedException or OperationCanceledException) {
                        // The exchange already reported this failure; observe its pipe read after teardown.
                        if (!read.IsCompleted) { _cleanupFailed = true; }
                    }
                    if (!_cleanupFailed) { _responseRead = null; }
                }
                if (_stderr is not null) {
                    try { await _stderr.WaitAsync(cleanup.Token).ConfigureAwait(false); } catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or OperationCanceledException) {
                        // Expected when retiring closed pipes; no provider diagnostics are logged.
                        if (!_stderr.IsCompleted) { _cleanupFailed = true; }
                    }
                    if (!_cleanupFailed) { _stderr = null; }
                }
            }
        }
        return !_cleanupFailed;
    }
}

internal static class WorkerNative {
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] internal static extern nint WindowFromPoint(HintPoint point);
    [DllImport("user32.dll")] internal static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeFileHandle CreateJobObject(nint attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool AssignProcessToJobObject(SafeFileHandle job, nint process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(SafeFileHandle job, int infoClass, ref JobLimits limits, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits {
        public long ProcessTime, JobTime;
        public uint Flags;
        public nuint MinWorkingSet, MaxWorkingSet;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct JobLimits {
        public BasicLimits Basic;
        public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
        public nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    internal static SafeFileHandle CreateKillOnCloseJob() {
        var job = CreateJobObject(0, null);
        var limits = new JobLimits { Basic = new BasicLimits { Flags = 0x2000 } };
        if (job.IsInvalid || !SetInformationJobObject(job, 9, ref limits, Marshal.SizeOf<JobLimits>())) {
            int error = Marshal.GetLastWin32Error();
            job.Dispose();
            throw new Win32Exception(error);
        }
        return job;
    }
}
