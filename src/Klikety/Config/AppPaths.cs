using System.IO;

namespace Klikety.Config;

internal sealed class AppPaths {
    public AppPaths(string root) => Root = Path.GetFullPath(root);

    public string Root { get; }
    public string ConfigPath => Path.Combine(Root, "config.json");
    public string MacrosPath => Path.Combine(Root, "macros.json");
    public string LogsFolder => Path.Combine(Root, "logs");
    public string ThemesFolder => Path.Combine(Root, "themes");
    public string DisplayTopologyPath => Path.Combine(Root, "display-topologies.json");

    public static AppPaths ForFixture(string root) {
        if (!Path.IsPathFullyQualified(root)) {
            throw new InvalidDataException("Fixture directory must be absolute.");
        }
        var paths = new AppPaths(root);
        paths.ValidateFixture();
        return paths;
    }

    public void ValidateFixture() {
        var user = User.Root.TrimEnd(Path.DirectorySeparatorChar);
        var fixture = Root.TrimEnd(Path.DirectorySeparatorChar);
        if (fixture.Equals(user, StringComparison.OrdinalIgnoreCase) ||
            fixture.StartsWith(user + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            user.StartsWith(fixture + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidDataException("Fixture refuses the real Klikety AppData directory and its ancestors.");
        }
        for (var directory = new DirectoryInfo(Root); directory is not null; directory = directory.Parent) {
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0) {
                throw new InvalidDataException("Fixture paths cannot traverse symbolic links or junctions.");
            }
        }
        if (!Directory.Exists(Root)) { return; }
        var directories = new Queue<string>();
        directories.Enqueue(Root);
        while (directories.Count > 0) {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directories.Dequeue())) {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) {
                    throw new InvalidDataException("Fixture contents cannot contain symbolic links or junctions.");
                }
                if ((attributes & FileAttributes.Directory) != 0) { directories.Enqueue(entry); }
            }
        }
    }

    public static AppPaths User => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Klikety"));
}
