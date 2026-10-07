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
    HintCapabilities Capabilities, HintRect Bounds, HintRect VisibleBounds, HintPoint Preview);
public sealed record HintRequest(int Version, Guid SessionId, Guid RequestId, HintCommand Command,
    long Hwnd = 0, int OwnerProcessId = 0, HintRect Region = default, int Token = 0,
    int ExpectedProcessId = 0, long ExpectedProcessStart = 0);
public sealed record HintResponse(int Version, Guid SessionId, Guid RequestId, HintOutcome Outcome,
    HintTarget[] Targets, int Visited = 0, int Omitted = 0, string? Reason = null,
    HintPoint? Point = null, int RootProcessId = 0);

public static class ElementHintProtocol {
    public const int Version = 1;
    public const int DiscoveryMs = 1500, ValidationMs = 500, CleanupMs = 500;
    public const int MaxNodes = 20000, MaxDepth = 64, MaxTargets = 2000;
    public const int MaxRequestBytes = 64 * 1024, MaxResponseBytes = 2 * 1024 * 1024;
    public const int MaxDiagnosticBytes = 64 * 1024, MaxRuntimeId = 64;
    private static readonly JsonSerializerOptions Options = new() { MaxDepth = 32 };

    public static HintResponse BoundSnapshotResponse(HintResponse response) {
        if (JsonSerializer.SerializeToUtf8Bytes(response, Options).Length <= MaxResponseBytes) { return response; }
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
