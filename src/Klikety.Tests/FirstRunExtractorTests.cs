using System.IO;
using System.Reflection;

using Klikety.Config;

namespace Klikety.Tests;

public class FirstRunExtractorTests : IDisposable {
    private readonly string _tempDir;
    private readonly string _themesDir;
    private readonly Assembly _assembly;

    public FirstRunExtractorTests() {
        _tempDir = Path.Combine(Path.GetTempPath(), "klikety-test-" + Guid.NewGuid());
        _themesDir = Path.Combine(_tempDir, "themes");
        _assembly = typeof(FirstRunExtractor).Assembly;
    }

    public void Dispose() {
        if (Directory.Exists(_tempDir)) {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void EnsureDefaults_FreshDir_WritesAllFiles() {
        FirstRunExtractor.EnsureDefaults(_tempDir, _themesDir, _assembly);

        Assert.True(File.Exists(Path.Combine(_tempDir, "config.json")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "config.schema.json")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "macros.json")));
        Assert.True(File.Exists(Path.Combine(_themesDir, "theme.schema.json")));
        Assert.True(File.Exists(Path.Combine(_themesDir, "dark.theme.json")));
        Assert.True(File.Exists(Path.Combine(_themesDir, "light.theme.json")));
    }

    [Fact]
    public void EnsureDefaults_StaleSchema_OverwritesWithEmbeddedContent() {
        Directory.CreateDirectory(_tempDir);
        var schemaPath = Path.Combine(_tempDir, "config.schema.json");
        File.WriteAllText(schemaPath, "stale content");

        FirstRunExtractor.EnsureDefaults(_tempDir, _themesDir, _assembly);

        var content = File.ReadAllText(schemaPath);
        Assert.NotEqual("stale content", content);
        Assert.Contains("$schema", content);
    }

    [Fact]
    public void EnsureDefaults_ExistingConfig_NotOverwritten() {
        Directory.CreateDirectory(_tempDir);
        var configPath = Path.Combine(_tempDir, "config.json");
        File.WriteAllText(configPath, "user config");

        FirstRunExtractor.EnsureDefaults(_tempDir, _themesDir, _assembly);

        Assert.Equal("user config", File.ReadAllText(configPath));
    }

    [Fact]
    public void EnsureDefaults_ExistingMacros_NotOverwritten() {
        Directory.CreateDirectory(_tempDir);
        var macrosPath = Path.Combine(_tempDir, "macros.json");
        File.WriteAllText(macrosPath, "user macros");

        FirstRunExtractor.EnsureDefaults(_tempDir, _themesDir, _assembly);

        Assert.Equal("user macros", File.ReadAllText(macrosPath));
    }

    [Fact]
    public void EnsureDefaults_ExistingTheme_NotOverwritten() {
        Directory.CreateDirectory(_themesDir);
        var themePath = Path.Combine(_themesDir, "dark.theme.json");
        File.WriteAllText(themePath, "custom theme");

        FirstRunExtractor.EnsureDefaults(_tempDir, _themesDir, _assembly);

        Assert.Equal("custom theme", File.ReadAllText(themePath));
    }

    [Fact]
    public void EnsureDefaults_ReadOnlySchema_DoesNotThrow_ReturnsWarning() {
        Directory.CreateDirectory(_tempDir);
        var schemaPath = Path.Combine(_tempDir, "config.schema.json");
        File.WriteAllText(schemaPath, "old");
        File.SetAttributes(schemaPath, FileAttributes.ReadOnly);

        try {
            var warnings = FirstRunExtractor.EnsureDefaults(_tempDir, _themesDir, _assembly);

            Assert.Contains(warnings, w => w.Contains("config.schema.json"));
        } finally {
            // Clean up read-only for disposal.
            File.SetAttributes(schemaPath, FileAttributes.Normal);
        }
    }

    [Fact]
    public void EnsureDefaults_SchemaOverwrite_IsAtomic() {
        Directory.CreateDirectory(_tempDir);
        var schemaPath = Path.Combine(_tempDir, "config.schema.json");
        File.WriteAllText(schemaPath, "original");

        FirstRunExtractor.EnsureDefaults(_tempDir, _themesDir, _assembly);

        // No temp files left behind.
        var dir = new DirectoryInfo(_tempDir);
        var leftover = dir.GetFiles().Where(f =>
            f.Name != "config.json" &&
            f.Name != "config.schema.json" &&
            f.Name != "macros.json").ToArray();
        Assert.Empty(leftover);
    }
}
