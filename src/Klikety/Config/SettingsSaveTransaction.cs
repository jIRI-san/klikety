using System.IO;
using System.Text.Json.Nodes;

namespace Klikety.Config;

internal sealed record SettingsSaveResult(
    bool Succeeded, bool RequiresReload, ConfigModel Candidate, IReadOnlyList<string> Issues);

internal sealed class SettingsSaveTransaction(
    SettingsConfigStore store,
    SettingsOperationGate gate,
    Action<ConfigModel> preflight,
    Func<SettingsRuntimeSnapshot> captureRuntime,
    Func<ConfigModel, SettingsApplyOutcome> apply,
    Func<SettingsRuntimeSnapshot, SettingsApplyOutcome> restoreRuntime) {

    public SettingsSaveResult Execute(IReadOnlyDictionary<string, JsonNode?> changes) {
        using var operation = gate.Enter();
        var previousRuntime = captureRuntime();
        var candidate = store.Save(changes, preflight);
        SettingsApplyOutcome activated;
        try {
            activated = apply(candidate);
        } catch (Exception ex) {
            activated = new(false, [$"Runtime activation failed: {ex.Message}"]);
        }
        if (activated.Succeeded) {
            store.AcceptLastCommit();
            return new(true, false, candidate, activated.Issues);
        }

        var disk = store.HasUnacceptedCommit
            ? store.RestoreLastCommit()
            : new SettingsDiskRecoveryOutcome(true, "No config replacement needed restoration.");
        SettingsApplyOutcome runtime;
        try {
            runtime = restoreRuntime(previousRuntime);
        } catch (Exception ex) {
            runtime = new(false, [$"Runtime recovery failed: {ex.Message}"]);
        }
        return new(false, !disk.Succeeded, candidate, [
            "Apply failed. The draft is retained.",
            .. activated.Issues,
            "Disk: " + disk.Message,
            "Runtime: " + (runtime.Succeeded ? "previous runtime restored." : string.Join("; ", runtime.Issues)),
            .. runtime.Succeeded ? runtime.Issues : [],
        ]);
    }
}
