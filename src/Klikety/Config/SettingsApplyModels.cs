namespace Klikety.Config;

internal sealed record SettingsRuntimeSnapshot(
    ConfigModel Config,
    bool HudEnabled,
    bool ScrollPaused);

internal sealed record SettingsApplyOutcome(bool Succeeded, IReadOnlyList<string> Issues) {
    public static SettingsApplyOutcome Success { get; } = new(true, []);
}

internal sealed record SettingsDiskRecoveryOutcome(bool Succeeded, string Message);
