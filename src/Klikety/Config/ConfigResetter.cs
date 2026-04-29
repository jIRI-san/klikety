using System.IO;
using System.Reflection;

namespace Klikety.Config;

/// <summary>
/// Resets the user config to embedded defaults. Used by the "Reset Configuration"
/// tray menu item when config has blocking violations.
/// </summary>
public static class ConfigResetter {
    private const string EmbeddedConfigResource = "Klikety.Resources.config.json";

    private static readonly string ConfigFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Klikety");

    private static readonly string ConfigPath = Path.Combine(ConfigFolder, "config.json");

    /// <summary>
    /// Overwrites the user config with the embedded default. Creates a .bak backup first.
    /// </summary>
    /// <returns>Null on success, error message on failure.</returns>
    public static string? ResetToDefaults() => ResetToDefaults(ConfigPath);

    /// <summary>
    /// Overwrites config at <paramref name="path"/> with the embedded default.
    /// Creates a .bak backup of the existing file first.
    /// </summary>
    public static string? ResetToDefaults(string path) {
        try {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(EmbeddedConfigResource);
            if (stream is null) {
                return "Embedded default config resource not found.";
            }

            var dir = Path.GetDirectoryName(path);
            if (dir is not null) {
                Directory.CreateDirectory(dir);
            }

            // Backup existing config
            if (File.Exists(path)) {
                File.Copy(path, path + ".bak", overwrite: true);
            }

            using var file = File.Create(path);
            stream.CopyTo(file);
            return null;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return $"Failed to reset config: {ex.Message}";
        }
    }
}
