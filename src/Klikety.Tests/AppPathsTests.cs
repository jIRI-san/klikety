using System.Text.Json.Nodes;

using Klikety;
using Klikety.Config;

namespace Klikety.Tests;

public sealed class AppPathsTests {
    [Fact]
    public void FixtureAdmissionRejectsRelativeUserAndAncestorPathsWithoutReadingOrWritingThem() {
        Assert.Throws<InvalidDataException>(() => AppPaths.ForFixture("relative"));
        Assert.Throws<InvalidDataException>(() => AppPaths.ForFixture(AppPaths.User.Root));
        Assert.Throws<InvalidDataException>(() => AppPaths.ForFixture(Path.Combine(AppPaths.User.Root, "child")));
        Assert.Throws<InvalidDataException>(() => AppPaths.ForFixture(Path.GetDirectoryName(AppPaths.User.Root)!));
    }

    [Fact]
    public void FixtureRootConfinesAllAppOwnedPaths() {
        var root = Path.Combine(Path.GetTempPath(), "Klikety-fixture-" + Guid.NewGuid());
        var paths = AppPaths.ForFixture(root);

        Assert.NotEqual(AppPaths.User.ConfigPath, paths.ConfigPath);
        Assert.Equal(Path.GetFullPath(root), paths.Root);
        Assert.Equal(Path.Combine(paths.Root, "config.json"), paths.ConfigPath);
        Assert.Equal(Path.Combine(paths.Root, "macros.json"), paths.MacrosPath);
        Assert.Equal(Path.Combine(paths.Root, "logs"), paths.LogsFolder);
        Assert.Equal(Path.Combine(paths.Root, "themes"), paths.ThemesFolder);
        Assert.Equal(Path.Combine(paths.Root, "display-topologies.json"), paths.DisplayTopologyPath);
        Assert.False(Directory.Exists(paths.Root));
    }

    [Fact]
    public void RuntimeFixturePreparationUsesDedicatedHotkeysAndPreservesOtherSettings() {
        var root = Path.Combine(Path.GetTempPath(), "Klikety-runtime-fixture-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "config.json");
        File.WriteAllText(configPath, """
        {"configVersion":9,"theme":"light","macros":{"enabled":false,"globalHotKey":null}}
        """);

        try {
            App.ConfigureRuntimeFixture(configPath);
            var config = JsonNode.Parse(File.ReadAllText(configPath))!.AsObject();
            Assert.Equal("light", config["theme"]!.GetValue<string>());
            Assert.Equal("F11", config["hotKey"]!["key"]!.GetValue<string>());
            Assert.Equal("Control, Alt, Shift", config["hotKey"]!["modifiers"]!.GetValue<string>());
            Assert.Equal("Pause", config["macros"]!["globalHotKey"]!["key"]!.GetValue<string>());
            Assert.False(config["macros"]!["enabled"]!.GetValue<bool>());
            Assert.Empty(ConfigLoader.ReadSettings(File.ReadAllText(configPath)).SettingsBlockingErrors);
        } finally {
            foreach (var file in Directory.GetFiles(root)) { File.Delete(file); }
            Directory.Delete(root);
        }
    }
}
