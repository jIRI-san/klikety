using System.Text.Json.Nodes;

using Klikety.Config;

namespace Klikety.Tests;

public sealed class SettingsRecoveryTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Klikety-recovery-" + Guid.NewGuid());
    private readonly string _path;

    public SettingsRecoveryTests() {
        Directory.CreateDirectory(_root);
        _path = Path.Combine(_root, "config.json");
        File.WriteAllText(_path, "{\"configVersion\":9,\"theme\":\"dark\"}");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void DiskAndRuntimeRestoreIndependentlyFromDifferentBaselines(bool diskSucceeds, bool runtimeSucceeds) {
        var bytes = File.ReadAllBytes(_path);
        var oldRuntime = new ConfigModel { Theme = "runtime-only", FileLoggingEnabled = true };
        var previous = new SettingsRuntimeSnapshot(oldRuntime, HudEnabled: true, ScrollPaused: true);
        var store = new SettingsConfigStore(_path, stage => {
            if (stage == "disk-restore" && !diskSucceeds) { throw new IOException("restore locked"); }
        });
        store.Open();
        SettingsRuntimeSnapshot? restored = null;
        var transaction = new SettingsSaveTransaction(store, new(() => true), _ => { }, () => previous,
            _ => new(false, ["candidate registration failed"]), snapshot => {
                restored = snapshot;
                return new(runtimeSucceeds, runtimeSucceeds ? [] : ["runtime registration failed"]);
            });
        var edits = new Dictionary<string, JsonNode?> { ["theme"] = JsonValue.Create("light") };

        var result = transaction.Execute(edits);

        Assert.False(result.Succeeded);
        Assert.Equal(!diskSucceeds, result.RequiresReload);
        Assert.Equal(!diskSucceeds, store.RequiresReload);
        Assert.Same(previous, restored);
        Assert.Same(oldRuntime, restored!.Config);
        Assert.True(restored.HudEnabled);
        Assert.True(restored.ScrollPaused);
        Assert.Equal(bytes, File.ReadAllBytes(_path + ".settings.bak"));
        Assert.Equal(diskSucceeds ? "dark" : "light", new SettingsConfigStore(_path).Open().Config.Theme);
        Assert.Contains(result.Issues, issue => issue.StartsWith("Disk:", StringComparison.Ordinal));
        Assert.Contains(result.Issues, issue => issue.Contains(runtimeSucceeds ? "previous runtime restored" : "runtime registration failed", StringComparison.Ordinal));
        Assert.Equal("light", edits["theme"]!.GetValue<string>());
    }

    [Fact]
    public void ExternalEditAfterSaveIsKeptAndRequiresExplicitReloadEvenWhenRuntimeRestores() {
        var store = Open();
        var restored = false;
        var transaction = new SettingsSaveTransaction(store, new(() => true), _ => { },
            () => new(new ConfigModel(), false, false),
            _ => { File.AppendAllText(_path, "\n// newer external"); return new(false, ["fail"]); },
            _ => { restored = true; return SettingsApplyOutcome.Success; });
        var result = transaction.Execute(new Dictionary<string, JsonNode?> { ["theme"] = JsonValue.Create("light") });
        Assert.True(result.RequiresReload);
        Assert.True(restored);
        Assert.EndsWith("// newer external", File.ReadAllText(_path));
        Assert.Contains(result.Issues, issue => issue.Contains("Newer external bytes", StringComparison.Ordinal));
    }

    [Fact]
    public void ThrownActivationAndRecoveryFailuresAreExplicitAndDoNotLoseBackup() {
        var store = Open();
        var transaction = new SettingsSaveTransaction(store, new(() => true), _ => { },
            () => new(new ConfigModel(), false, false),
            _ => throw new IOException("activation"), _ => throw new IOException("recovery"));
        var result = transaction.Execute(new Dictionary<string, JsonNode?> { ["theme"] = JsonValue.Create("light") });
        Assert.False(result.Succeeded);
        Assert.Contains(result.Issues, issue => issue.Contains("activation", StringComparison.Ordinal));
        Assert.Contains(result.Issues, issue => issue.Contains("recovery", StringComparison.Ordinal));
        Assert.Equal("dark", new SettingsConfigStore(_path).Open().Config.Theme);
    }

    [Theory]
    [InlineData("busy")]
    [InlineData("preflight")]
    [InlineData("disk-save")]
    public void PrecommitFailuresDoNotActivateOrReplaceBytes(string failure) {
        var before = File.ReadAllBytes(_path);
        var store = new SettingsConfigStore(_path, stage => {
            if (stage == failure) { throw new IOException("write failed"); }
        });
        store.Open();
        var applied = false;
        var transaction = new SettingsSaveTransaction(store, new(() => failure != "busy"),
            _ => { if (failure == "preflight") { throw new InvalidDataException("preflight failed"); } },
            () => new(new ConfigModel(), false, false),
            _ => { applied = true; return SettingsApplyOutcome.Success; }, _ => SettingsApplyOutcome.Success);
        Assert.NotNull(Record.Exception(() => transaction.Execute(new Dictionary<string, JsonNode?> { ["theme"] = JsonValue.Create("light") })));
        Assert.False(applied);
        Assert.Equal(before, File.ReadAllBytes(_path));
        Assert.False(File.Exists(_path + ".settings.bak"));
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public void RetryAfterRecoveredFailureUsesRestoredDiskBaseAndCapturedCandidate() {
        var store = Open();
        var succeed = false;
        var previous = new ConfigModel { Theme = "runtime-only" };
        var transaction = new SettingsSaveTransaction(store, new(() => true), _ => { }, () => new(previous, false, false),
            candidate => {
                Assert.Equal("light", candidate.Theme);
                return new(succeed, []);
            }, snapshot => { Assert.Same(previous, snapshot.Config); return SettingsApplyOutcome.Success; });
        var changes = new Dictionary<string, JsonNode?> { ["theme"] = JsonValue.Create("light") };
        Assert.False(transaction.Execute(changes).Succeeded);
        succeed = true;
        Assert.True(transaction.Execute(changes).Succeeded);
        Assert.False(store.HasUnacceptedCommit);
        Assert.Equal("light", new SettingsConfigStore(_path).Open().Config.Theme);
    }

    private SettingsConfigStore Open() { var store = new SettingsConfigStore(_path); store.Open(); return store; }

    public void Dispose() {
        foreach (var file in Directory.GetFiles(_root)) { File.Delete(file); }
        Directory.Delete(_root);
    }
}
