using System.IO;
using System.Reflection;

namespace Klikety.Config;

/// <summary>
/// Extracts embedded resource files to %APPDATA%\Klikety\ on first run.
/// Writes config.json, config.schema.json, theme.schema.json, and built-in themes.
/// </summary>
public static class FirstRunExtractor
{
    private static readonly string ConfigFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Klikety");

    private static readonly string ThemesFolder = Path.Combine(ConfigFolder, "themes");

    private static readonly (string ResourceName, string RelativePath)[] Files =
    [
        ("Klikety.Resources.config.json", "config.json"),
        ("Klikety.Resources.config.schema.json", "config.schema.json"),
        ("Klikety.Resources.theme.schema.json", Path.Combine("themes", "theme.schema.json")),
        ("Klikety.Resources.dark.theme.json", Path.Combine("themes", "dark.theme.json")),
        ("Klikety.Resources.light.theme.json", Path.Combine("themes", "light.theme.json")),
    ];

    /// <summary>
    /// Writes all embedded resource files that don't already exist on disk.
    /// Creates directories as needed.
    /// </summary>
    public static void EnsureDefaults()
    {
        Directory.CreateDirectory(ConfigFolder);
        Directory.CreateDirectory(ThemesFolder);

        var assembly = Assembly.GetExecutingAssembly();
        foreach (var (resourceName, relativePath) in Files)
        {
            var targetPath = Path.Combine(ConfigFolder, relativePath);
            if (File.Exists(targetPath))
                continue;

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
                continue;

            using var file = File.Create(targetPath);
            stream.CopyTo(file);
        }
    }
}
