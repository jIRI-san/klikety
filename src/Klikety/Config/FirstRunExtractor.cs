using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Klikety.Config;

/// <summary>
/// Extracts embedded resource files to %APPDATA%\Klikety\ on first run.
/// Writes config.json, config.schema.json, theme.schema.json, and built-in themes.
/// Schema files are always overwritten (not user-editable); other files are skip-if-exists.
/// </summary>
public static class FirstRunExtractor {
    private static readonly string ConfigFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Klikety");

    private static readonly string ThemesFolder = Path.Combine(ConfigFolder, "themes");

    /// <summary>Files that are always overwritten on startup (schemas).</summary>
    private static readonly (string ResourceName, string RelativePath)[] AlwaysOverwrite =
    [
        ("Klikety.Resources.config.schema.json", "config.schema.json"),
        ("Klikety.Resources.theme.schema.json", Path.Combine("themes", "theme.schema.json")),
    ];

    /// <summary>Files written only if they don't already exist (user-editable).</summary>
    private static readonly (string ResourceName, string RelativePath)[] SkipIfExists =
    [
        ("Klikety.Resources.config.json", "config.json"),
        ("Klikety.Resources.dark.theme.json", Path.Combine("themes", "dark.theme.json")),
        ("Klikety.Resources.light.theme.json", Path.Combine("themes", "light.theme.json")),
    ];

    /// <summary>
    /// Extracts embedded resource files to disk.
    /// Schema files are always overwritten via atomic temp+rename.
    /// User config and theme files are written only on first run.
    /// Failures per file are traced and skipped (non-blocking).
    /// </summary>
    public static void EnsureDefaults() {
        var warnings = EnsureDefaults(ConfigFolder, ThemesFolder, Assembly.GetExecutingAssembly());
        foreach (var warning in warnings) {
            Trace.TraceWarning(warning);
        }
    }

    /// <summary>Testable overload accepting explicit paths and assembly. Returns per-file warning messages.</summary>
    internal static List<string> EnsureDefaults(string configFolder, string themesFolder, Assembly assembly) {
        var warnings = new List<string>();

        try {
            Directory.CreateDirectory(configFolder);
            Directory.CreateDirectory(themesFolder);
        } catch (IOException ex) {
            warnings.Add($"Failed to create config directory: {ex.Message}");
            return warnings;
        } catch (UnauthorizedAccessException ex) {
            warnings.Add($"Failed to create config directory: {ex.Message}");
            return warnings;
        }

        foreach (var (resourceName, relativePath) in AlwaysOverwrite) {
            try {
                var targetPath = Path.Combine(configFolder, relativePath);
                WriteAtomic(targetPath, resourceName, assembly);
            } catch (IOException ex) {
                warnings.Add($"Failed to write schema '{relativePath}': {ex.Message}");
            } catch (UnauthorizedAccessException ex) {
                warnings.Add($"Failed to write schema '{relativePath}': {ex.Message}");
            }
        }

        foreach (var (resourceName, relativePath) in SkipIfExists) {
            try {
                var targetPath = Path.Combine(configFolder, relativePath);
                if (File.Exists(targetPath)) {
                    continue;
                }

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream is null) {
                    continue;
                }

                using var file = File.Create(targetPath);
                stream.CopyTo(file);
            } catch (IOException ex) {
                warnings.Add($"Failed to write '{relativePath}': {ex.Message}");
            } catch (UnauthorizedAccessException ex) {
                warnings.Add($"Failed to write '{relativePath}': {ex.Message}");
            }
        }

        return warnings;
    }

    private static void WriteAtomic(string targetPath, string resourceName, Assembly assembly) {
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null) {
            return;
        }

        var dir = Path.GetDirectoryName(targetPath)!;
        var tempPath = Path.Combine(dir, Path.GetRandomFileName());
        try {
            using (var file = File.Create(tempPath)) {
                stream.CopyTo(file);
            }

            File.Move(tempPath, targetPath, overwrite: true);
        } catch {
            // Clean up temp file on any failure.
            try { File.Delete(tempPath); } catch { /* best effort */ }
            throw;
        }
    }
}
