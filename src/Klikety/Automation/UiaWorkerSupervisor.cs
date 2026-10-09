using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Microsoft.Win32.SafeHandles;

namespace Klikety.Automation;

public sealed record ElementTargetContext(nint Hwnd, int OwnerProcessId, int ProcessId = 0, long ProcessStart = 0);

public interface IElementHintService {
    string? CleanupFailureReason => null;
    Task<HintResponse> DiscoverAsync(ElementTargetContext context, HintRect region, CancellationToken ct);
    Task<HintResponse> ValidateAsync(int token, CancellationToken ct, bool moveOnly = false);
    Task<bool> RetireAsync();
    async IAsyncEnumerable<HintResponse> DiscoverIncrementallyAsync(ElementTargetContext context, HintRect region,
        [EnumeratorCancellation] CancellationToken ct) {
        yield return await DiscoverAsync(context, region, ct);
    }
    async IAsyncEnumerable<HintResponse> ExpandAsync(int groupId, [EnumeratorCancellation] CancellationToken ct) {
        await Task.CompletedTask;
        ct.ThrowIfCancellationRequested();
        yield return new(ElementHintProtocol.Version, Guid.Empty, Guid.Empty, HintOutcome.StaleTarget, [],
            Reason: "Group unavailable", GroupId: groupId);
    }
    IAsyncEnumerable<HintResponse> ResumeAsync(int groupId, CancellationToken ct) => ExpandAsync(groupId, ct);
    Task<bool> ReleaseAsync() => RetireAsync();
    Task<bool> ShutdownAsync() => Task.FromResult(true);
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
    private readonly int _discoveryTimeoutMs;
    private readonly int _cacheWindowCount;
    private readonly Func<Stream, Task> _drainDiagnostics;
    private readonly Action? _beforeStart;
    private readonly object _deadlineGate = new();
    private CancellationTokenSource? _activeDeadline;
    private Timer? _idle;
    private int _scanGeneration;
    private HintResponse? _lastDiscoveryResponse;
    private Process? _process;
    private SafeFileHandle? _job;
    private Task? _stderr;
    private Task? _responseRead;
    private Task? _startup;
    private bool _started;
    private HintRequest? _snapshot;
    private string? _cleanupFailureReason;
    public string? CleanupFailureReason => _cleanupFailureReason;
    internal int? OwnedProcessId => _started && _process is not null ? _process.Id : null;

    public UiaWorkerSupervisor(int discoveryTimeoutMs = ElementHintProtocol.DiscoveryMs,
        int cacheWindowCount = ElementHintProtocol.DefaultCacheWindowCount)
        : this(Path.Combine(AppContext.BaseDirectory, "uia-worker", "Klikety.UiaWorker.exe"),
            discoveryTimeoutMs: discoveryTimeoutMs, cacheWindowCount: cacheWindowCount) { }
    internal UiaWorkerSupervisor(string executable, string? arguments = null,
        int discoveryTimeoutMs = ElementHintProtocol.DiscoveryMs,
        int cacheWindowCount = 0, Func<Stream, Task>? drainDiagnostics = null, Action? beforeStart = null) {
        if (discoveryTimeoutMs is < ElementHintProtocol.MinDiscoveryMs or > ElementHintProtocol.MaxDiscoveryMs) {
            throw new ArgumentOutOfRangeException(nameof(discoveryTimeoutMs), discoveryTimeoutMs,
                $"Discovery timeout must be between {ElementHintProtocol.MinDiscoveryMs} and {ElementHintProtocol.MaxDiscoveryMs} ms.");
        }
        _executable = Path.GetFullPath(executable);
        _arguments = arguments;
        _discoveryTimeoutMs = discoveryTimeoutMs;
        if (cacheWindowCount is < 0 or > ElementHintProtocol.MaxCacheWindowCount) {
            throw new ArgumentOutOfRangeException(nameof(cacheWindowCount));
        }
        _cacheWindowCount = cacheWindowCount;
        _drainDiagnostics = drainDiagnostics ?? DrainDiagnosticsAsync;
        _beforeStart = beforeStart;
    }

    public async IAsyncEnumerable<HintResponse> DiscoverIncrementallyAsync(ElementTargetContext context, HintRect region,
        [EnumeratorCancellation] CancellationToken ct) {
        var request = new HintRequest(ElementHintProtocol.Version, Guid.NewGuid(), Guid.NewGuid(),
            HintCommand.Discover, context.Hwnd.ToInt64(), context.OwnerProcessId, region,
            ExpectedProcessId: context.ProcessId, ExpectedProcessStart: context.ProcessStart,
            Incremental: true, CacheWindowCount: _cacheWindowCount);
        int generation = Interlocked.Increment(ref _scanGeneration);
        var watch = Stopwatch.StartNew();
        HintOutcome? retryOutcome = null;
        bool hadTargets = false;
        for (int attempt = 0; attempt < 2; attempt++) {
            bool retry = false;
            await foreach (var response in DiscoverAttemptAsync(request, generation, watch, ct).ConfigureAwait(false)) {
                hadTargets |= response.Targets.Length > 0 || response.Groups is { Length: > 0 };
                if (attempt == 0 && !hadTargets && response.IsComplete &&
                    (response.Outcome == HintOutcome.NoTargets && response.Visited > 0 ||
                        response.Outcome == HintOutcome.ProviderError) &&
                    Remaining(watch) > 150 + 2 * ElementHintProtocol.DiscoverySliceMs + ElementHintProtocol.DiscoveryBatchMs) {
                    retryOutcome = response.Outcome;
                    retry = true;
                    break;
                }
                yield return retryOutcome is null ? response : response with {
                    Reason = response.Reason ?? $"Discovery retry after {retryOutcome}"
                };
            }
            if (!retry || generation != Volatile.Read(ref _scanGeneration)) { yield break; }
            await Task.Delay(150, ct).ConfigureAwait(false);
            if (generation != Volatile.Read(ref _scanGeneration)) { yield break; }
            request = request with { RequestId = Guid.NewGuid() };
        }
    }

    private async IAsyncEnumerable<HintResponse> DiscoverAttemptAsync(HintRequest request, int generation,
        Stopwatch watch, [EnumeratorCancellation] CancellationToken ct) {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        HintResponse first;
        try {
            _idle?.Dispose(); _idle = null;
            if ((_cleanupFailureReason is not null || _cacheWindowCount == 0) &&
                !await RetireCoreAsync().ConfigureAwait(false)) {
                first = Failure(request, HintOutcome.CleanupFailed);
            } else {
                _snapshot = request;
                first = await ExchangeAsync(request, Remaining(watch), start: _process is null, ct).ConfigureAwait(false);
            }
        } finally { _gate.Release(); }
        if (generation != Volatile.Read(ref _scanGeneration)) { yield break; }
        yield return first;
        if (first.IsComplete) { yield break; }
        await foreach (var response in ContinueDiscoveryAsync(request, first, generation, watch, ct).ConfigureAwait(false)) {
            yield return response;
        }
    }

    public async IAsyncEnumerable<HintResponse> ExpandAsync(int groupId, [EnumeratorCancellation] CancellationToken ct) {
        await foreach (var response in DiscoverGroupAsync(groupId, HintCommand.Expand, ct).ConfigureAwait(false)) {
            yield return response;
        }
    }

    public IAsyncEnumerable<HintResponse> ResumeAsync(int groupId, CancellationToken ct) =>
        DiscoverGroupAsync(groupId, HintCommand.Continue, ct);

    private async IAsyncEnumerable<HintResponse> DiscoverGroupAsync(int groupId, HintCommand command,
        [EnumeratorCancellation] CancellationToken ct) {
        var snapshot = _snapshot;
        if (snapshot is null) {
            yield return Failure(new(ElementHintProtocol.Version, Guid.Empty, Guid.Empty, HintCommand.Expand,
                GroupId: groupId), HintOutcome.StaleTarget);
            yield break;
        }
        int generation = Interlocked.Increment(ref _scanGeneration);
        var watch = Stopwatch.StartNew();
        var request = snapshot with { Command = command, RequestId = Guid.NewGuid(), GroupId = groupId };
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        HintResponse first;
        try {
            first = _snapshot != snapshot ? Failure(request, HintOutcome.StaleTarget) :
                await ExchangeAsync(request, Remaining(watch), start: false, ct).ConfigureAwait(false);
        } finally { _gate.Release(); }
        if (generation != Volatile.Read(ref _scanGeneration)) { yield break; }
        yield return first;
        if (first.IsComplete) { yield break; }
        await foreach (var response in ContinueDiscoveryAsync(request, first, generation, watch, ct).ConfigureAwait(false)) {
            yield return response;
        }
    }

    private int Remaining(Stopwatch watch) => Math.Max(1, _discoveryTimeoutMs - (int)watch.ElapsedMilliseconds);

    private async IAsyncEnumerable<HintResponse> ContinueDiscoveryAsync(HintRequest original, HintResponse last, int generation,
        Stopwatch watch, [EnumeratorCancellation] CancellationToken ct) {
        var published = last;
        bool delayNext = last.Targets.Length > 0 || last.Groups is { Length: > 0 };
        while (generation == Volatile.Read(ref _scanGeneration) && !ct.IsCancellationRequested) {
            if (delayNext) { await Task.Delay(ElementHintProtocol.DiscoveryBatchMs, ct).ConfigureAwait(false); }
            if (generation != Volatile.Read(ref _scanGeneration)) { yield break; }
            var request = original with { Command = HintCommand.Continue, RequestId = Guid.NewGuid() };
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            HintResponse response;
            try {
                if (generation != Volatile.Read(ref _scanGeneration)) { yield break; }
                if (Remaining(watch) <= 2 * ElementHintProtocol.DiscoverySliceMs + ElementHintProtocol.DiscoveryBatchMs) {
                    // No provider call is in flight: the retained targets remain freshly validatable.
                    response = last with {
                        RequestId = request.RequestId,
                        Outcome = HintOutcome.Partial,
                        IsComplete = true,
                        Reason = "Discovery time limit reached",
                        Omitted = last.Omitted + 1
                    };
                } else {
                    response = await ExchangeAsync(request, Remaining(watch), start: false, ct).ConfigureAwait(false);
                }
            } finally { _gate.Release(); }
            if (generation != Volatile.Read(ref _scanGeneration)) { yield break; }
            delayNext = response.Targets.Length != published.Targets.Length ||
                (response.Groups?.Length ?? 0) != (published.Groups?.Length ?? 0);
            if (response.IsComplete || delayNext) {
                yield return response;
                published = response;
            }
            if (response.IsComplete) { yield break; }
            last = response;
        }
    }

    public async Task<HintResponse> DiscoverAsync(ElementTargetContext context, HintRect region, CancellationToken ct) {
        var request = new HintRequest(ElementHintProtocol.Version, Guid.NewGuid(), Guid.NewGuid(),
            HintCommand.Discover, context.Hwnd.ToInt64(), context.OwnerProcessId, region,
            ExpectedProcessId: context.ProcessId, ExpectedProcessStart: context.ProcessStart);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try {
            if (!await RetireCoreAsync().ConfigureAwait(false)) { return Failure(request, HintOutcome.CleanupFailed); }
            _snapshot = request;
            return await ExchangeAsync(request, _discoveryTimeoutMs, start: true, ct).ConfigureAwait(false);
        } finally { _gate.Release(); }
    }

    public async Task<HintResponse> ValidateAsync(int token, CancellationToken ct, bool moveOnly = false) {
        Interlocked.Increment(ref _scanGeneration);
        var snapshot = _snapshot;
        var watch = Stopwatch.StartNew();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(ElementHintProtocol.ValidationMs);
        try { await _gate.WaitAsync(deadline.Token).ConfigureAwait(false); } catch (OperationCanceledException) {
            lock (_deadlineGate) { _activeDeadline?.Cancel(); }
            return Failure(snapshot ?? new(ElementHintProtocol.Version, Guid.Empty, Guid.Empty, HintCommand.Validate),
                ct.IsCancellationRequested ? HintOutcome.Cancelled : HintOutcome.Timeout);
        }
        try {
            if (_snapshot is null || _snapshot != snapshot) {
                return Failure(new(ElementHintProtocol.Version, Guid.Empty, Guid.Empty, HintCommand.Validate), HintOutcome.StaleTarget);
            }
            return await ExchangeAsync(_snapshot with {
                Command = HintCommand.Validate,
                RequestId = Guid.NewGuid(),
                Token = token,
                MoveOnly = moveOnly
            },
                Math.Max(1, ElementHintProtocol.ValidationMs - (int)watch.ElapsedMilliseconds), start: false, ct).ConfigureAwait(false);
        } finally { _gate.Release(); }
    }

    private async Task<HintResponse> ExchangeAsync(HintRequest request, int timeoutMs, bool start, CancellationToken ct) {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeoutMs);
        var token = deadline.Token;
        lock (_deadlineGate) { _activeDeadline = deadline; }
        try {
            if (start) {
                // Start/job assignment are off the UI thread and part of the absolute scan deadline.
                _startup = Task.Run(() => {
                    _beforeStart?.Invoke();
                    StartWorker(token);
                }, CancellationToken.None);
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
            if (request.Incremental && request.Command is HintCommand.Discover or HintCommand.Continue or HintCommand.Expand) {
                if (request.Command != HintCommand.Discover && _lastDiscoveryResponse is { } previous) {
                    ElementHintProtocol.CheckProgress(previous, response);
                }
                _lastDiscoveryResponse = response;
            }
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
        } finally {
            lock (_deadlineGate) { if (_activeDeadline == deadline) { _activeDeadline = null; } }
        }
    }

    private void StartWorker(CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        if (_cleanupFailureReason is not null) { throw new InvalidOperationException("Previous UIA helper teardown is still pending."); }
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
        _stderr = _drainDiagnostics(process.StandardError.BaseStream);
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

    private HintResponse Failure(HintRequest r, HintOutcome outcome) =>
        new(ElementHintProtocol.Version, r.SessionId, r.RequestId, outcome, [],
            Reason: outcome == HintOutcome.CleanupFailed ? _cleanupFailureReason : outcome.ToString(), GroupId: r.GroupId);

    public async Task<bool> ReleaseAsync() {
        Interlocked.Increment(ref _scanGeneration);
        await _gate.WaitAsync().ConfigureAwait(false);
        try {
            if (_cacheWindowCount == 0 || _snapshot is null || _process is null) {
                return await RetireCoreAsync().ConfigureAwait(false);
            }
            var response = await ExchangeAsync(_snapshot with {
                Command = HintCommand.Release,
                RequestId = Guid.NewGuid()
            }, ElementHintProtocol.CleanupMs, start: false, CancellationToken.None).ConfigureAwait(false);
            _snapshot = null;
            _lastDiscoveryResponse = null;
            if (response.Outcome != HintOutcome.Success) { return await RetireCoreAsync().ConfigureAwait(false); }
            int generation = Volatile.Read(ref _scanGeneration);
            _idle?.Dispose();
            _idle = new Timer(_ => { _ = RetireIdleAsync(generation); }, null, ElementHintProtocol.CacheIdleMs, Timeout.Infinite);
            return true;
        } finally { _gate.Release(); }
    }

    private async Task RetireIdleAsync(int generation) {
        await _gate.WaitAsync().ConfigureAwait(false);
        try {
            if (_snapshot is null && generation == Volatile.Read(ref _scanGeneration)) {
                await RetireCoreAsync().ConfigureAwait(false);
            }
        } finally { _gate.Release(); }
    }

    public async Task<bool> RetireAsync() {
        Interlocked.Increment(ref _scanGeneration);
        lock (_deadlineGate) { _activeDeadline?.Cancel(); }
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return await RetireCoreAsync().ConfigureAwait(false); } finally { _gate.Release(); }
    }
    public Task<bool> ShutdownAsync() => RetireAsync();

    private async Task<bool> RetireCoreAsync() {
        _idle?.Dispose(); _idle = null;
        _snapshot = null;
        _lastDiscoveryResponse = null;
        using var cleanup = new CancellationTokenSource(ElementHintProtocol.CleanupMs);
        if (_startup is not null) {
            if (!await ObserveRetiredTaskAsync(_startup, cleanup.Token).ConfigureAwait(false)) {
                return CleanupPending("Helper startup is still pending");
            }
            _startup = null;
        }
        try {
            _job?.Dispose(); // kills only this owned job, including on parent death
            _job = null;
            if (_process is not null && _started) {
                if (!_process.HasExited) { _process.Kill(entireProcessTree: true); }
                await _process.WaitForExitAsync(cleanup.Token).ConfigureAwait(false);
            }
        } catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or OperationCanceledException) {
            if (_process is not null && _started && !_process.HasExited) {
                return CleanupPending("Owned helper process has not exited");
            }
        }
        _process?.Dispose();
        _process = null;
        _started = false;
        if (_responseRead is not null) {
            if (!await ObserveRetiredTaskAsync(_responseRead, cleanup.Token).ConfigureAwait(false)) {
                return CleanupPending("Response pipe read is still pending");
            }
            _responseRead = null;
        }
        if (_stderr is not null) {
            if (!await ObserveRetiredTaskAsync(_stderr, cleanup.Token).ConfigureAwait(false)) {
                return CleanupPending("Diagnostic pipe read is still pending");
            }
            _stderr = null;
        }
        _cleanupFailureReason = null;
        return true;
    }

    private bool CleanupPending(string reason) {
        _cleanupFailureReason = reason;
        return false;
    }

    private static async Task<bool> ObserveRetiredTaskAsync(Task task, CancellationToken ct) {
        try {
            await task.WaitAsync(ct).ConfigureAwait(false);
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            if (!task.IsCompleted) { return false; }
            // A task may finish as the wait times out; still observe its actual result.
            return await ObserveRetiredTaskAsync(task, CancellationToken.None).ConfigureAwait(false);
        } catch (Exception ex) when (ex is Win32Exception or IOException or InvalidDataException or InvalidOperationException or
            System.Text.Json.JsonException or OperationCanceledException) {
            // Expected startup/pipe faults still confirm those tasks have finished.
        }
        return true;
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
