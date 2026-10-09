using System.Text.Json.Nodes;

using Klikety.Config;

namespace Klikety.Tests;

public sealed class SettingsCoverageTests {
    private static readonly string[] HintOnlyFields = [".discoveryTimeoutMs", ".cacheWindowCount"];
    [Fact]
    public void ExplicitCoverageMapMatchesEveryKnownEditableModelLeaf() {
        var document = SettingsFieldCases.Serialize(new ConfigModel());
        var known = Flatten(document, "").Where(path => path != "configVersion" &&
            // Only ElementHints consumes these fields of the shared mode shape.
            (!HintOnlyFields
                .Any(suffix => path.EndsWith(suffix, StringComparison.Ordinal)) ||
                path.StartsWith("modes.elementHints.", StringComparison.Ordinal))).Order(StringComparer.Ordinal).ToArray();
        var covered = SettingsFieldCases.All.Select(field => field.Path).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(known, covered);
        Assert.Equal(covered.Length, covered.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(Enumerable.Range(0, 7), SettingsFieldCases.All.Select(field => field.Page).Distinct().Order());
    }

    private static IEnumerable<string> Flatten(JsonObject value, string prefix) {
        foreach (var (name, child) in value) {
            var path = prefix.Length == 0 ? name : prefix + "." + name;
            if (child is JsonObject nested && path != "actionBindings") {
                foreach (var leaf in Flatten(nested, path)) { yield return leaf; }
            } else { yield return path; }
        }
    }
}
