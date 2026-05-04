using System.IO;

namespace Klikety.Config;

/// <summary>
/// Resolves and loads theme files from %APPDATA%\Klikety\themes\.
/// </summary>
public static class ThemeLoader {
    private static readonly string ThemesFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Klikety", "themes");

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() {
        ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

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
            var theme = System.Text.Json.JsonSerializer.Deserialize<ThemeModel>(json, JsonOptions)
                ?? DefaultDarkTheme();
            var colorWarning = ValidateColors(theme);
            return (theme, colorWarning);
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

        // Reject absolute/rooted paths
        if (Path.IsPathRooted(themeValue)) {
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

        // Ensure resolved path stays within the config folder (boundary-safe)
        var configFolderWithSep = configFolder.EndsWith(Path.DirectorySeparatorChar)
            ? configFolder
            : configFolder + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(configFolderWithSep, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        return resolved;
    }

    private static ThemeModel DefaultDarkTheme() => new();

    /// <summary>
    /// Validates that all color properties are parseable WPF color strings.
    /// Returns a warning listing invalid colors (which will fall back to ThemeModel defaults at render time).
    /// </summary>
    private static string? ValidateColors(ThemeModel theme) {
        var invalid = new List<string>();
        ValidateColor(theme.LabelColor, nameof(theme.LabelColor), invalid);
        ValidateColor(theme.LabelOutlineColor, nameof(theme.LabelOutlineColor), invalid);
        ValidateColor(theme.CellBorderColor, nameof(theme.CellBorderColor), invalid);
        ValidateColor(theme.CellBackgroundColor, nameof(theme.CellBackgroundColor), invalid);
        ValidateColor(theme.DimmedOverlayColor, nameof(theme.DimmedOverlayColor), invalid);
        ValidateColor(theme.HighlightedColumnBackground, nameof(theme.HighlightedColumnBackground), invalid);
        ValidateColor(theme.HighlightedColumnBorderColor, nameof(theme.HighlightedColumnBorderColor), invalid);
        ValidateColor(theme.SubgridBorderColor, nameof(theme.SubgridBorderColor), invalid);
        ValidateColor(theme.SubgridLabelColor, nameof(theme.SubgridLabelColor), invalid);
        ValidateColor(theme.ExternalLabelColor, nameof(theme.ExternalLabelColor), invalid);
        ValidateColor(theme.ConnectorLineColor, nameof(theme.ConnectorLineColor), invalid);
        ValidateColor(theme.SmallCellBackgroundColor, nameof(theme.SmallCellBackgroundColor), invalid);
        return invalid.Count > 0
            ? $"Invalid theme colors (will use defaults): {string.Join(", ", invalid)}"
            : null;
    }

    private static void ValidateColor(string value, string name, List<string> invalid) {
        try {
            System.Windows.Media.ColorConverter.ConvertFromString(value);
        } catch {
            invalid.Add($"{name}='{value}'");
        }
    }
}
