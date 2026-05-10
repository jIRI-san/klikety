using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Klikety.Config;

/// <summary>
/// Result of loading macros from disk.
/// </summary>
public sealed class MacroLoadResult {
    public required MacrosFile File { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
}

/// <summary>
/// Result of saving macros to disk.
/// </summary>
public sealed class MacroSaveResult {
    public required bool Success { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Loads and saves macros.json with atomic writes and per-slot semantic validation.
/// </summary>
public interface IMacroStore {
    MacroLoadResult Load();
    MacroSaveResult Save(MacrosFile file);
}

/// <summary>
/// Loads and saves %APPDATA%\Klikety\macros.json.
/// </summary>
public sealed class MacroStore : IMacroStore {
    private static readonly string ConfigFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Klikety");

    private static readonly string MacrosPath = Path.Combine(ConfigFolder, "macros.json");

    private static readonly JsonSerializerOptions JsonOptions = new() {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _path;

    public MacroStore() : this(MacrosPath) { }

    internal MacroStore(string path) => _path = path;

    public MacroLoadResult Load() {
        if (!File.Exists(_path)) {
            return new MacroLoadResult { File = new MacrosFile() };
        }

        string json;
        try {
            json = File.ReadAllText(_path);
        } catch (IOException ex) {
            return new MacroLoadResult {
                File = new MacrosFile(),
                Errors = [$"Failed to read macros file: {ex.Message}"],
            };
        } catch (UnauthorizedAccessException ex) {
            return new MacroLoadResult {
                File = new MacrosFile(),
                Errors = [$"Failed to read macros file: {ex.Message}"],
            };
        }

        MacrosFile? file;
        try {
            file = JsonSerializer.Deserialize<MacrosFile>(json, JsonOptions);
        } catch (JsonException ex) {
            return new MacroLoadResult {
                File = new MacrosFile(),
                Errors = [$"macros.json could not be parsed: {ex.Message}"],
            };
        }

        if (file is null) {
            return new MacroLoadResult { File = new MacrosFile() };
        }

        // Null-guard Macros array (user wrote "macros": null)
        if (file.Macros is null) {
            file = new MacrosFile {
                Version = file.Version,
                Macros = new MacroDefinition?[10],
                ExtensionData = file.ExtensionData,
            };
        }

        // Pad array to 10 if shorter
        if (file.Macros.Length < 10) {
            var padded = new MacroDefinition?[10];
            Array.Copy(file.Macros, padded, file.Macros.Length);
            file = new MacrosFile {
                Version = file.Version,
                Macros = padded,
                ExtensionData = file.ExtensionData,
            };
        }

        // Semantic validation per slot
        var errors = new List<string>();

        if (file.Version > 2) {
            return new MacroLoadResult {
                File = new MacrosFile(),
                Errors = [$"macros.json has unsupported version {file.Version} (max 2)"],
            };
        }

        for (var i = 0; i < file.Macros.Length; i++) {
            var def = file.Macros[i];
            if (def is null) {
                continue;
            }

            var slotErrors = ValidateSlot(i, def);
            if (slotErrors.Count > 0) {
                file.Macros[i] = null; // quarantine
                errors.AddRange(slotErrors);
            }
        }

        return new MacroLoadResult { File = file, Errors = errors };
    }

    public MacroSaveResult Save(MacrosFile file) {
        if (file.Version != 2) {
            file = new MacrosFile {
                Version = 2,
                Macros = file.Macros,
                ExtensionData = file.ExtensionData,
            };
        }

        try {
            var dir = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(dir);
            var tempPath = Path.Combine(dir, Path.GetRandomFileName());
            try {
                var json = JsonSerializer.Serialize(file, JsonOptions);
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _path, overwrite: true);
                return new MacroSaveResult { Success = true };
            } catch {
                try { File.Delete(tempPath); } catch { /* best effort */ }
                throw;
            }
        } catch (Exception ex) {
            return new MacroSaveResult { Success = false, Error = ex.Message };
        }
    }

    private static readonly HashSet<MouseAction> ValidDragButtons =
        [MouseAction.LeftClick, MouseAction.RightClick, MouseAction.MiddleClick];

    private static List<string> ValidateSlot(int slot, MacroDefinition def) {
        var errors = new List<string>();

        if (def.Steps is null) {
            errors.Add($"Slot {slot}: steps array is null");
            return errors;
        }

        if (def.PositionMode == MacroPositionMode.WindowRelative) {
            if (def.WindowWidth <= 0) {
                errors.Add($"Slot {slot}: windowRelative macro requires WindowWidth > 0");
            }

            if (def.WindowHeight <= 0) {
                errors.Add($"Slot {slot}: windowRelative macro requires WindowHeight > 0");
            }

            if (string.IsNullOrEmpty(def.WindowTitlePattern)) {
                errors.Add($"Slot {slot}: windowRelative macro requires non-empty WindowTitlePattern");
            }

            if (errors.Count > 0) {
                return errors;
            }
        }

        for (var s = 0; s < def.Steps.Count; s++) {
            var step = def.Steps[s];
            var prefix = $"Slot {slot}, step {s}";

            if (step.RelativeTimeMs < 0) {
                errors.Add($"{prefix}: relativeTimeMs is negative ({step.RelativeTimeMs})");
            }

            if (step.RelativeTimeMs > 600_000) {
                errors.Add($"{prefix}: relativeTimeMs exceeds 10 minutes ({step.RelativeTimeMs})");
            }

            if (step.StartFromCursor && step.ActionType != MacroActionType.DragDrop) {
                errors.Add($"{prefix}: StartFromCursor is only valid on DragDrop steps");
            }

            if (def.PositionMode == MacroPositionMode.WindowRelative) {
                ValidateWindowRelativeStep(prefix, def, step, errors);
            } else {
                ValidateAbsoluteStep(prefix, def, step, errors);
            }

            switch (step.ActionType) {
                case MacroActionType.DragDrop:
                    if (step.DragButton is null) {
                        errors.Add($"{prefix}: DragDrop step missing DragButton");
                    } else if (!ValidDragButtons.Contains(step.DragButton.Value)) {
                        errors.Add($"{prefix}: invalid DragButton '{step.DragButton}' (must be LeftClick, RightClick, or MiddleClick)");
                    }

                    if (step.EndX is null || step.EndY is null) {
                        errors.Add($"{prefix}: DragDrop step missing EndX/EndY");
                    }

                    break;

                case MacroActionType.Scroll:
                    if (step.ScrollDelta is null) {
                        errors.Add($"{prefix}: Scroll step missing ScrollDelta");
                    }

                    break;
            }
        }

        return errors;
    }

    private static void ValidateAbsoluteStep(string prefix, MacroDefinition def, MacroStep step, List<string> errors) {
        if (step.X < 0 || step.Y < 0) {
            errors.Add($"{prefix}: coordinates are negative (x={step.X}, y={step.Y})");
        }

        if (def.ScreenWidth > 0 && step.X >= def.ScreenWidth) {
            errors.Add($"{prefix}: x ({step.X}) >= screenWidth ({def.ScreenWidth})");
        }

        if (def.ScreenHeight > 0 && step.Y >= def.ScreenHeight) {
            errors.Add($"{prefix}: y ({step.Y}) >= screenHeight ({def.ScreenHeight})");
        }

        if (step.ActionType == MacroActionType.DragDrop) {
            if (step.EndX is not null && step.EndX.Value < 0) {
                errors.Add($"{prefix}: endX is negative ({step.EndX})");
            }

            if (step.EndY is not null && step.EndY.Value < 0) {
                errors.Add($"{prefix}: endY is negative ({step.EndY})");
            }

            if (step.EndX is not null && def.ScreenWidth > 0 && step.EndX.Value >= def.ScreenWidth) {
                errors.Add($"{prefix}: endX ({step.EndX}) >= screenWidth ({def.ScreenWidth})");
            }

            if (step.EndY is not null && def.ScreenHeight > 0 && step.EndY.Value >= def.ScreenHeight) {
                errors.Add($"{prefix}: endY ({step.EndY}) >= screenHeight ({def.ScreenHeight})");
            }
        }
    }

    private static void ValidateWindowRelativeStep(string prefix, MacroDefinition def, MacroStep step, List<string> errors) {
        // Skip start coordinate validation when StartFromCursor — X/Y are vestigial
        if (!step.StartFromCursor) {
            if (step.X < 0 || step.Y < 0) {
                errors.Add($"{prefix}: coordinates are negative (x={step.X}, y={step.Y})");
            }

            if (step.X >= def.WindowWidth) {
                errors.Add($"{prefix}: x ({step.X}) >= windowWidth ({def.WindowWidth})");
            }

            if (step.Y >= def.WindowHeight) {
                errors.Add($"{prefix}: y ({step.Y}) >= windowHeight ({def.WindowHeight})");
            }
        }

        if (step.ActionType == MacroActionType.DragDrop) {
            if (step.EndX is not null && step.EndX.Value < 0) {
                errors.Add($"{prefix}: endX is negative ({step.EndX})");
            }

            if (step.EndY is not null && step.EndY.Value < 0) {
                errors.Add($"{prefix}: endY is negative ({step.EndY})");
            }

            if (step.EndX is not null && step.EndX.Value >= def.WindowWidth) {
                errors.Add($"{prefix}: endX ({step.EndX}) >= windowWidth ({def.WindowWidth})");
            }

            if (step.EndY is not null && step.EndY.Value >= def.WindowHeight) {
                errors.Add($"{prefix}: endY ({step.EndY}) >= windowHeight ({def.WindowHeight})");
            }
        }
    }
}
