using System.Text.Json.Nodes;

using Klikety;
using Klikety.Config;
using Klikety.Services;
using Klikety.Tests.Fakes;

namespace Klikety.Tests;

public sealed class SettingsIsolationTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Klikety-isolation-" + Guid.NewGuid());
    public SettingsIsolationTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void ThemeReferenceMustResolveAndParseButColorWarningsAndUnchangedFallbackRemainAdvisory() {
        var themes = Path.Combine(_root, "themes");
        Directory.CreateDirectory(themes);
        File.WriteAllText(Path.Combine(themes, "custom.theme.json"), "{\"labelColor\":\"not-a-color\"}");
        ThemeLoader.ValidateSettingsReference("custom", "dark", _root);
        Assert.NotNull(ThemeLoader.Load("custom", _root).Warning);
        ThemeLoader.ValidateSettingsReference("missing", "missing", _root);
        Assert.Throws<InvalidDataException>(() => ThemeLoader.ValidateSettingsReference("missing", "dark", _root));
        File.WriteAllText(Path.Combine(themes, "broken.theme.json"), "null");
        Assert.Throws<InvalidDataException>(() => ThemeLoader.ValidateSettingsReference("broken", "dark", _root));
        foreach (var path in new[] { "..\\outside.theme.json", "C:\\outside.theme.json", "", "other.txt" }) {
            Assert.Throws<InvalidDataException>(() => ThemeLoader.ValidateSettingsReference(path, "dark", _root));
        }
    }

    [Fact]
    public void LoggerIoFailureDoesNotFallBackOrTouchUnrelatedFiles() {
        var logs = Path.Combine(_root, "logs");
        File.WriteAllText(logs, "a file blocks the log directory");
        Assert.Throws<IOException>(() => LoggingSetup.CreateLoggerFactory("Warning", true, 7, logs));
        Assert.Equal("a file blocks the log directory", File.ReadAllText(logs));
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public void SaveNeverWritesSeparateThemesMacrosTopologyOrRegistryFiles() {
        var paths = new AppPaths(_root);
        Directory.CreateDirectory(paths.ThemesFolder);
        File.WriteAllText(paths.ConfigPath, "{\"configVersion\":9}");
        File.WriteAllText(paths.MacrosPath, "{\"external\":\"macro sentinel\"}");
        File.WriteAllText(paths.DisplayTopologyPath, "{\"external\":\"topology sentinel\"}");
        var theme = Path.Combine(paths.ThemesFolder, "dark.theme.json");
        File.WriteAllText(theme, "{\"external\":\"theme sentinel\"}");
        var before = new[] { paths.MacrosPath, paths.DisplayTopologyPath, theme }.ToDictionary(path => path, File.ReadAllBytes);
        var store = new SettingsConfigStore(paths.ConfigPath);
        store.Open();
        store.Save(new Dictionary<string, JsonNode?> { ["theme"] = JsonValue.Create("light") });
        foreach (var (path, bytes) in before) { Assert.Equal(bytes, File.ReadAllBytes(path)); }
        Assert.Equal(4, Directory.GetFiles(_root).Length);
        Assert.Single(Directory.GetFiles(paths.ThemesFolder));
    }

    [Theory]
    [InlineData("disk-save")]
    [InlineData("disk-restore")]
    [InlineData("overlay")]
    [InlineData("main")]
    [InlineData("scroll-up")]
    [InlineData("scroll-down")]
    [InlineData("macro")]
    [InlineData("indicator")]
    [InlineData("hud")]
    public void FixtureFaultsAreOneShotAndConfinedToTheSelectedRoot(string stage) {
        var paths = new AppPaths(_root);
        var faults = new SettingsFixtureFaults(paths);
        faults.Arm(stage);
        Assert.Throws<IOException>(() => faults.Check(stage));
        faults.Check(stage);
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public void CandidateAndRecoveryFaultsCanBeArmedIndependentlyAndExternalEditIsObservable() {
        var paths = new AppPaths(_root);
        File.WriteAllText(paths.ConfigPath, "{\"configVersion\":9}");
        var faults = new SettingsFixtureFaults(paths);
        faults.Arm("external-edit");
        faults.Arm("main", recovery: true);
        Assert.Throws<IOException>(() => faults.Check("external-edit"));
        Assert.Contains("// fixture external edit", File.ReadAllText(paths.ConfigPath));
        faults.SetRecovery(true);
        Assert.Throws<IOException>(() => faults.Check("main"));
        faults.Check("main");
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public void FixtureConflictReservationOwnsOnlyItsServiceAndFailedRegistrationCleansIt() {
        var faults = new SettingsFixtureFaults(new AppPaths(_root));
        var service = new FakeHotKeyService();
        var unrelated = new FakeHotKeyService();
        unrelated.Register(new HotKeyConfig());
        faults.ReserveConflict(() => service);
        Assert.True(faults.HasConflict);
        Assert.True(service.IsRegistered);
        var candidate = new ConfigModel { HotKey = SettingsFixtureFaults.ConflictHotKey };
        Assert.Throws<InvalidDataException>(() => SettingsShortcutInventory.Preflight(candidate, [],
            key => key.Key == SettingsFixtureFaults.ConflictHotKey.Key ? "reserved by fixture conflict control" : null));
        Assert.Throws<InvalidOperationException>(() => faults.ReserveConflict(() => unrelated));
        Assert.Empty(faults.ReleaseConflict());
        Assert.False(faults.HasConflict);
        Assert.False(service.IsRegistered);
        Assert.True(unrelated.IsRegistered);
        Assert.Empty(faults.ReleaseConflict());
        var rejected = new RejectingHotKeyService();
        Assert.Throws<InvalidOperationException>(() => faults.ReserveConflict(() => rejected));
        Assert.True(rejected.Disposed);
        Assert.False(faults.HasConflict);
        unrelated.Dispose();
    }

    private sealed class RejectingHotKeyService : IHotKeyService {
        public event EventHandler? Activated { add { } remove { } }
        public bool Disposed { get; private set; }
        public bool Register(HotKeyConfig config) => false;
        public void Unregister() { }
        public void Dispose() => Disposed = true;
    }

    public void Dispose() {
        foreach (var file in Directory.GetFiles(_root, "*", SearchOption.AllDirectories)) { File.Delete(file); }
        foreach (var directory in Directory.GetDirectories(_root)) { Directory.Delete(directory); }
        Directory.Delete(_root);
    }
}
