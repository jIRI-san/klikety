using System.Buffers.Binary;
using System.IO;
using System.Text.Json;

namespace Klikety.Automation;

public enum HintOutcome {
    Success, Partial, NoTargets, Timeout, AccessDenied, ProviderError,
    InvalidRoot, StaleTarget, NoSafePoint, Unavailable, ProtocolError, CleanupFailed, Cancelled,
}

public enum HintCommand { Hello, Discover, Validate }

[Flags]
public enum HintCapabilities { None = 0, Invoke = 1, Toggle = 2, Selection = 4, Expand = 8, Value = 16 }

public readonly record struct HintRect(double X, double Y, double Width, double Height) {
    public bool IsValid => double.IsFinite(X) && double.IsFinite(Y) &&
        double.IsFinite(Width) && double.IsFinite(Height) && Width > 0 && Height > 0 &&
        X >= int.MinValue && Y >= int.MinValue && X + Width <= int.MaxValue && Y + Height <= int.MaxValue;
    public bool Contains(HintPoint point) =>
        point.X >= X && point.X < X + Width && point.Y >= Y && point.Y < Y + Height;
    public HintRect Clip(HintRect other) {
        if (!IsValid || !other.IsValid) { return default; }
        double x = Math.Max(X, other.X), y = Math.Max(Y, other.Y);
        return new(x, y, Math.Min(X + Width, other.X + other.Width) - x,
            Math.Min(Y + Height, other.Y + other.Height) - y);
    }
    public HintPoint Center => new((int)Math.Floor(X + Width / 2), (int)Math.Floor(Y + Height / 2));
}

public readonly record struct HintPoint(int X, int Y);
public sealed record HintTarget(int Token, int[] RuntimeId, int ProcessId, int ControlType,
    HintCapabilities Capabilities, HintRect Bounds, HintRect VisibleBounds, HintPoint Preview,
    int ContainerId = 0);
public sealed record HintContainer(int Id, int ParentId, int TargetToken, int ProcessId,
    int ControlType, HintRect Bounds);
public sealed record HintRequest(int Version, Guid SessionId, Guid RequestId, HintCommand Command,
    long Hwnd = 0, int OwnerProcessId = 0, HintRect Region = default, int Token = 0,
    int ExpectedProcessId = 0, long ExpectedProcessStart = 0, bool MoveOnly = false);
public sealed record HintResponse(int Version, Guid SessionId, Guid RequestId, HintOutcome Outcome,
    HintTarget[] Targets, int Visited = 0, int Omitted = 0, string? Reason = null,
    HintPoint? Point = null, int RootProcessId = 0, HintContainer[]? Containers = null);

public static class ElementHintProtocol {
    public const int Version = 1;
    public const int DiscoveryMs = 1500, ValidationMs = 500, CleanupMs = 500;
    public const int MinDiscoveryMs = 100, MaxDiscoveryMs = 60000;
    public const int MaxNodes = 20000, MaxDepth = 64, MaxTargets = 2000;
    public const int MaxContainers = MaxTargets * 2;
    public const int MaxRequestBytes = 64 * 1024, MaxResponseBytes = 2 * 1024 * 1024;
    public const int MaxDiagnosticBytes = 64 * 1024, MaxRuntimeId = 64;
    private static readonly JsonSerializerOptions Options = new() { MaxDepth = 32 };

    public static HintResponse BoundSnapshotResponse(HintResponse response) {
        if (JsonSerializer.SerializeToUtf8Bytes(response, Options).Length <= MaxResponseBytes) { return response; }
        // Byte truncation invalidates subtree completeness. Keep targets, not grouping claims.
        response = response with {
            Targets = response.Targets.Select(t => t with { ContainerId = 0 }).ToArray(),
            Containers = null
        };
        var partial = response with { Outcome = HintOutcome.Partial, Reason = "Response byte limit" };
        int low = 0, high = response.Targets.Length;
        while (low < high) {
            int count = (low + high + 1) / 2;
            var candidate = partial with {
                Targets = response.Targets[..count],
                Omitted = response.Omitted + response.Targets.Length - count,
            };
            if (JsonSerializer.SerializeToUtf8Bytes(candidate, Options).Length <= MaxResponseBytes) { low = count; } else { high = count - 1; }
        }
        return partial with { Targets = response.Targets[..low], Omitted = response.Omitted + response.Targets.Length - low };
    }

    public static async Task WriteAsync<T>(Stream stream, T value, int limit, CancellationToken ct) {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        if (body.Length > limit) { throw new InvalidDataException("UIA frame exceeds byte limit."); }
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        await stream.WriteAsync(body, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public static async Task<T> ReadAsync<T>(Stream stream, int limit, CancellationToken ct) {
        byte[] header = new byte[4];
        int headerBytes = await stream.ReadAtLeastAsync(header, 4, throwOnEndOfStream: false, ct).ConfigureAwait(false);
        if (headerBytes == 0) { throw new EndOfStreamException("UIA stream closed."); }
        if (headerBytes != 4) { throw new InvalidDataException("Truncated UIA frame header."); }
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > limit) { throw new InvalidDataException("Invalid UIA frame length."); }
        byte[] body = new byte[length];
        if (await stream.ReadAtLeastAsync(body, length, throwOnEndOfStream: false, ct).ConfigureAwait(false) != length) {
            throw new InvalidDataException("Truncated UIA frame body.");
        }
        return JsonSerializer.Deserialize<T>(body, Options) ?? throw new InvalidDataException("Empty UIA frame.");
    }

    public static void CheckResponse(HintRequest request, HintResponse response) {
        if (response.Version != Version || response.SessionId != request.SessionId ||
            response.RequestId != request.RequestId || !Enum.IsDefined(response.Outcome) ||
            response.Targets is null || response.Targets.Length > MaxTargets ||
            response.Visited < 0 || response.Visited > MaxNodes || response.Omitted < 0 ||
            response.Reason?.Length > 256) {
            throw new InvalidDataException("UIA protocol identity or limits mismatch.");
        }
        var tokens = new HashSet<int>();
        foreach (var target in response.Targets) {
            if (target is null || target.Token <= 0 || !tokens.Add(target.Token) ||
                target.RuntimeId is not { Length: > 0 and <= MaxRuntimeId } ||
                target.ProcessId <= 0 || !target.Bounds.IsValid || !target.VisibleBounds.IsValid ||
                target.Bounds.Clip(request.Region) != target.VisibleBounds ||
                !target.VisibleBounds.Contains(target.Preview)) {
                throw new InvalidDataException("Invalid UIA candidate.");
            }
        }
        var containers = new Dictionary<int, HintContainer>();
        var depths = new Dictionary<int, int>();
        var ownTokens = new HashSet<int>();
        if (response.Containers?.Length > MaxContainers) {
            throw new InvalidDataException("UIA container limit exceeded.");
        }
        foreach (var container in response.Containers ?? []) {
            if (container is null || container.Id <= 0 || containers.ContainsKey(container.Id) ||
                container.ParentId < 0 || container.ParentId >= container.Id ||
                container.ParentId != 0 && !containers.ContainsKey(container.ParentId) ||
                container.ProcessId <= 0 || !container.Bounds.IsValid ||
                !container.Bounds.Clip(request.Region).IsValid ||
                container.TargetToken < 0 || container.TargetToken != 0 &&
                    (!tokens.Contains(container.TargetToken) || !ownTokens.Add(container.TargetToken))) {
                throw new InvalidDataException("Invalid UIA container.");
            }
            int depth = container.ParentId == 0 ? 1 : depths[container.ParentId] + 1;
            if (depth > MaxDepth + 1 || container.ParentId != 0 &&
                containers[container.ParentId].ProcessId != container.ProcessId) {
                throw new InvalidDataException("Invalid UIA container ancestry.");
            }
            containers.Add(container.Id, container);
            depths.Add(container.Id, depth);
        }
        var counts = containers.Keys.ToDictionary(id => id, _ => 0);
        foreach (var target in response.Targets) {
            if (target.ContainerId < 0 || target.ContainerId != 0 &&
                (!containers.TryGetValue(target.ContainerId, out var container) ||
                 container.ProcessId != target.ProcessId)) {
                throw new InvalidDataException("Invalid UIA target ancestry.");
            }
            if (target.ContainerId != 0) { counts[target.ContainerId]++; }
        }
        foreach (var container in containers.Values.Reverse()) {
            if (container.TargetToken != 0 && !response.Targets.Any(t =>
                t.Token == container.TargetToken && t.ContainerId == container.Id &&
                t.ProcessId == container.ProcessId && t.Bounds == container.Bounds)) {
                throw new InvalidDataException("Invalid UIA container action.");
            }
            if (counts[container.Id] < 2) { throw new InvalidDataException("Incomplete UIA container."); }
            if (container.ParentId != 0) { counts[container.ParentId] += counts[container.Id]; }
        }
        if (response.Point is { } point && !request.Region.Contains(point)) {
            throw new InvalidDataException("UIA validation point outside region.");
        }
        if (request.Command == HintCommand.Discover &&
            ((response.Outcome is HintOutcome.Success && response.Targets.Length == 0) ||
             (response.Outcome is HintOutcome.NoTargets && response.Targets.Length != 0))) {
            throw new InvalidDataException("Inconsistent UIA scan outcome.");
        }
        if (request.Command == HintCommand.Validate && response.Outcome == HintOutcome.Success &&
            (response.Point is null || response.RootProcessId <= 0 || response.Targets.Length != 0)) {
            throw new InvalidDataException("Incomplete UIA validation approval.");
        }
    }
}

public static class ElementHintCandidatePolicy {
    // UIA control-type IDs, shared without UIAutomation references in the parent.
    public static bool IsInteractive(int type, HintCapabilities capabilities) =>
        type is 50000 or 50002 or 50003 or 50004 or 50005 or 50007 or 50011 or
            50013 or 50015 or 50016 or 50019 or 50024 or 50029 ||
        capabilities != HintCapabilities.None;
}
