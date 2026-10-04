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

    public static AppPaths User => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Klikety"));
}
