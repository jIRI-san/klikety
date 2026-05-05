using Klikety.Input;

namespace Klikety.Config;

/// <summary>
/// Result of LogGrid key-policy evaluation. Immutable after construction.
/// </summary>
public sealed class LogGridKeyPolicyResult {
    public required VKey[] EffectiveHorizontalKeys { get; init; }
    public required VKey[] EffectiveVerticalKeys { get; init; }
    public required bool IsAvailable { get; init; }
    public string? Warning { get; init; }
}

/// <summary>
/// Mode-scoped key-policy validator for LogGrid.
/// Evaluates axis key counts and produces effective key arrays and availability.
/// <para>
/// Rules:
/// - If either axis has fewer than 10 keys, LogGrid is unavailable for the run.
/// - If either axis has more than 10 keys, the first 10 are used.
/// - Other modes are never affected.
/// </para>
/// </summary>
public static class LogGridKeyPolicy {
    private const int RequiredKeyCount = 10;

    /// <summary>
    /// Evaluates the key policy for LogGrid mode against the given axis key arrays.
    /// Returns effective keys, availability, and an optional warning message.
    /// </summary>
    public static LogGridKeyPolicyResult Evaluate(VKey[] horizontalKeys, VKey[] verticalKeys) {
        var horizCount = horizontalKeys.Length;
        var vertCount = verticalKeys.Length;

        // Fewer than required on either axis → unavailable
        if (horizCount < RequiredKeyCount || vertCount < RequiredKeyCount) {
            var axis = horizCount < RequiredKeyCount ? "horizontal" : "vertical";
            var count = horizCount < RequiredKeyCount ? horizCount : vertCount;
            return new LogGridKeyPolicyResult {
                EffectiveHorizontalKeys = horizontalKeys,
                EffectiveVerticalKeys = verticalKeys,
                IsAvailable = false,
                Warning = $"LogGrid unavailable: {axis} axis has {count} keys (minimum {RequiredKeyCount} required).",
            };
        }

        // More than required → trim to first 10
        var effectiveHoriz = horizCount > RequiredKeyCount
            ? horizontalKeys[..RequiredKeyCount]
            : horizontalKeys;

        var effectiveVert = vertCount > RequiredKeyCount
            ? verticalKeys[..RequiredKeyCount]
            : verticalKeys;

        string? warning = null;
        if (horizCount > RequiredKeyCount || vertCount > RequiredKeyCount) {
            warning = $"LogGrid: axis keys trimmed to first {RequiredKeyCount} (horizontal had {horizCount}, vertical had {vertCount}).";
        }

        return new LogGridKeyPolicyResult {
            EffectiveHorizontalKeys = effectiveHoriz,
            EffectiveVerticalKeys = effectiveVert,
            IsAvailable = true,
            Warning = warning,
        };
    }
}
