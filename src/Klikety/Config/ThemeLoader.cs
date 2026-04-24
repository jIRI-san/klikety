using System.IO;

namespace Klikety.Config;

/// <summary>
/// Resolves and loads theme files from %APPDATA%\Klikety\themes\.
/// </summary>
public static class ThemeLoader {
    private static readonly string ThemesFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Klikety", "themes");

    /// <summary>
    /// Resolves theme value from config and loads the ThemeModel.
    /// Bare name → themes/{name}.theme.json; relative path → resolved from config folder.
    /// Falls back to built-in dark theme on any error.
    /// </summary>
    /// <returns>The loaded theme and an optional warning message.</returns>
    public static (ThemeModel Theme, string? Warning) Load(string themeValue) {
        try {
            var path = ResolvePath(themeValue);
            if (path is null) {
                return (DefaultDarkTheme(), $"Invalid theme value '{themeValue}': could not resolve path.");
            }

            if (!File.Exists(path)) {
                return (DefaultDarkTheme(), $"Theme file not found: {path}");
            }

            var json = File.ReadAllText(path);
            var options = new System.Text.Json.JsonSerializerOptions {
                ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                PropertyNameCaseInsensitive = true,
            };
            var theme = System.Text.Json.JsonSerializer.Deserialize<ThemeModel>(json, options);
            return (theme ?? DefaultDarkTheme(), null);
        } catch (Exception ex) {
            return (DefaultDarkTheme(), $"Failed to load theme '{themeValue}': {ex.Message}");
        }
    }

    private static string? ResolvePath(string themeValue) {
        if (string.IsNullOrWhiteSpace(themeValue)) {
            return null;
        }

        // Reject path traversal sequences
        if (themeValue.Contains("..", StringComparison.Ordinal)) {
            return null;
        }

        // Bare name → themes/<name>.theme.json
        if (!themeValue.Contains('/') && !themeValue.Contains('\\') && !themeValue.EndsWith(".theme.json", StringComparison.OrdinalIgnoreCase)) {
            return Path.Combine(ThemesFolder, $"{themeValue}.theme.json");
        }

        // Relative path — must end in .theme.json
        if (!themeValue.EndsWith(".theme.json", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        var configFolder = Path.GetDirectoryName(ThemesFolder)!; // %APPDATA%\Klikety
        var resolved = Path.GetFullPath(Path.Combine(configFolder, themeValue));

        // Ensure resolved path stays within the config folder
        if (!resolved.StartsWith(configFolder, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        return resolved;
    }

    private static ThemeModel DefaultDarkTheme() => new();
}
