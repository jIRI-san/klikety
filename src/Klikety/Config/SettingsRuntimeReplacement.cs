namespace Klikety.Config;

internal static class SettingsRuntimeReplacement {
    public static SettingsApplyOutcome Activate(
        SettingsRuntimeSnapshot previous,
        ConfigModel candidate,
        Func<IReadOnlyList<string>> release,
        Func<ConfigModel, SettingsApplyOutcome> compose,
        Func<bool, bool, SettingsApplyOutcome> restoreToggles) {
        var cleanup = release();
        if (cleanup.Count > 0) { return new(false, cleanup); }
        SettingsApplyOutcome built;
        try {
            built = compose(candidate);
            if (built.Succeeded) {
                var toggles = restoreToggles(previous.HudEnabled, previous.ScrollPaused && candidate.ScrollHotKeys.Enabled);
                built = new(toggles.Succeeded, [.. built.Issues, .. toggles.Issues]);
            }
        } catch (Exception ex) {
            built = new(false, [$"Runtime composition failed: {ex.Message}"]);
        }
        if (!built.Succeeded) {
            built = built with { Issues = [.. built.Issues, .. release()] };
        }
        return built;
    }
}
