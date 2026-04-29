using System.IO;

using Klikety.Config;

namespace Klikety.Tests;

public class ConfigResetterTests {
    [Fact]
    public void ResetToDefaults_WritesEmbeddedConfig() {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "config.json");
        try {
            var error = ConfigResetter.ResetToDefaults(path);
            Assert.Null(error);
            Assert.True(File.Exists(path));

            var content = File.ReadAllText(path);
            Assert.Contains("hotKey", content);
            Assert.Contains("actionBindings", content);
        } finally {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ResetToDefaults_CreatesBackup() {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "config.json");
        try {
            File.WriteAllText(path, """{ "broken": true }""");
            var error = ConfigResetter.ResetToDefaults(path);
            Assert.Null(error);
            Assert.True(File.Exists(path + ".bak"));
            Assert.Contains("broken", File.ReadAllText(path + ".bak"));
        } finally {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ResetToDefaults_NoExistingFile_Succeeds() {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var path = Path.Combine(dir, "config.json");
        try {
            var error = ConfigResetter.ResetToDefaults(path);
            Assert.Null(error);
            Assert.True(File.Exists(path));
        } finally {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ResetToDefaults_ResultLoadsCleanly() {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "config.json");
        try {
            ConfigResetter.ResetToDefaults(path);
            var result = ConfigLoader.Load(path);
            // Embedded config should load without blocking violations
            // (migration warnings about new modes are expected and OK)
            Assert.DoesNotContain(result.Violations, v =>
                v.Contains("could not be parsed") || v.Contains("newer than supported"));
        } finally {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
