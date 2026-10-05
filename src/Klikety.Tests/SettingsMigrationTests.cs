using Klikety.Config;

namespace Klikety.Tests;

public sealed class SettingsMigrationTests {
    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void ExistingMigrationRunsBeforeEditableSnapshotAndNeverDuringPreviewOrSave(int version) {
        var root = Path.Combine(Path.GetTempPath(), "Klikety-settings-migration-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "config.json");
        File.WriteAllText(path, $"{{\"configVersion\":{version},\"future\":{{\"keep\":true}}}}");
        try {
            Assert.Throws<InvalidDataException>(() => new SettingsConfigStore(path).Open());
            var migrated = ConfigLoader.Load(path);
            Assert.Equal(7, migrated.Config.ConfigVersion);
            var snapshot = File.ReadAllBytes(path);
            var store = new SettingsConfigStore(path);
            Assert.Equal(7, store.Open().Config.ConfigVersion);
            store.Preview(new Dictionary<string, System.Text.Json.Nodes.JsonNode?>());
            Assert.Equal(snapshot, File.ReadAllBytes(path));
            store.Save(new Dictionary<string, System.Text.Json.Nodes.JsonNode?>());
            Assert.Equal(snapshot, File.ReadAllBytes(path));
            Assert.Contains("\"future\"", File.ReadAllText(path));
            Assert.False(File.Exists(path + ".settings.bak"));
        } finally {
            foreach (var file in Directory.GetFiles(root)) { File.Delete(file); }
            Directory.Delete(root);
        }
    }
}
