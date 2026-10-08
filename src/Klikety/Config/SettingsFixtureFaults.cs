using System.IO;

using Klikety.Input;
using Klikety.Services;

namespace Klikety.Config;

internal sealed class SettingsFixtureFaults {
    internal static readonly string[] Stages = [
        "logger", "overlay", "coordinator", "main", "scroll-up", "scroll-down", "scroll", "macro", "indicator", "hud",
        "disk-save", "disk-restore", "external-edit",
    ];
    private readonly AppPaths _paths;
    private bool _recovering;
    private SettingsRuntimeResources? _conflict;
    public bool HasConflict => _conflict is not null;
    public static HotKeyConfig ConflictHotKey => new() {
        Key = VKey.Back,
        Modifiers = HotKeyModifiers.Control | HotKeyModifiers.Alt | HotKeyModifiers.Shift,
    };

    public SettingsFixtureFaults(AppPaths paths) {
        paths.ValidateFixture();
        _paths = paths;
    }

    public void SetRecovery(bool recovering) => _recovering = recovering;

    public void ReserveConflict(Func<IHotKeyService> create) {
        _paths.ValidateFixture();
        if (HasConflict) { throw new InvalidOperationException("Fixture conflict shortcut is already reserved."); }
        _conflict = SettingsRuntimeResources.Create(owner => {
            var hotkey = owner.Own("fixture conflict", create());
            if (!hotkey.Register(ConflictHotKey)) {
                throw new InvalidOperationException("Fixture conflict shortcut Ctrl+Alt+Shift+Backspace is unavailable.");
            }
        }, retainFailed: pending => _conflict = pending);
    }

    public IReadOnlyList<string> ReleaseConflict() {
        var failures = _conflict?.Release() ?? [];
        if (failures.Count == 0) { _conflict = null; }
        return failures;
    }

    public void Arm(string stage, bool recovery = false) {
        if (!Stages.Contains(stage, StringComparer.Ordinal)) {
            throw new ArgumentException("Unknown fixture fault stage.", nameof(stage));
        }
        File.WriteAllText(FaultPath(recovery || stage == "disk-restore"), stage);
    }

    public void Check(string stage) {
        var recovering = stage == "disk-restore" || _recovering;
        var path = FaultPath(recovering);
        if (!File.Exists(path)) { return; }
        var instruction = File.ReadAllText(path).Trim();
        if (instruction != stage) { return; }
        File.Delete(path);
        if (stage == "external-edit") {
            File.AppendAllText(_paths.ConfigPath, "\n// fixture external edit\n");
        }
        throw new IOException($"Injected fixture {(recovering ? "recovery" : "candidate")}:{stage} failure.");
    }

    private string FaultPath(bool recovery) {
        _paths.ValidateFixture();
        var path = Path.Combine(_paths.Root, recovery ? "settings-recovery-fault.txt" : "settings-candidate-fault.txt");
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) {
            throw new IOException("Fixture fault file cannot be a symbolic link.");
        }
        return path;
    }
}
