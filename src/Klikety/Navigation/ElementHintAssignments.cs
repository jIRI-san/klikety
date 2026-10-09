using System.Drawing;
using System.Security.Cryptography;
using System.Text;

using Klikety.Automation;
using Klikety.Input;

namespace Klikety.Navigation;

internal sealed record RememberedHintLevel(bool SingleKey, Dictionary<string, int> Slots);
internal sealed record HintAssignmentSnapshot(string Layout, Dictionary<string, RememberedHintLevel> Levels);

internal sealed class ElementHintAssignmentCache(int capacity) {
    private readonly record struct Window(nint Hwnd, int ProcessId, long ProcessStart);
    private readonly Dictionary<Window, (HintAssignmentSnapshot Snapshot, long Used)> _windows = [];
    private long _clock;
    private bool _retired;
    internal int Count => _windows.Count;

    private static Window? Key(ElementTargetContext context) =>
        context.Hwnd != 0 && context.ProcessId > 0 && context.ProcessStart > 0
            ? new(context.Hwnd, context.ProcessId, context.ProcessStart) : null;

    public HintAssignmentSnapshot? Load(ElementTargetContext context, string layout) {
        if (_retired || capacity == 0 || Key(context) is not { } key) { return null; }
        if (!_windows.TryGetValue(key, out var value) || value.Snapshot.Layout != layout) { return null; }
        _windows[key] = (value.Snapshot, ++_clock);
        return value.Snapshot;
    }

    public void Save(ElementTargetContext context, HintAssignmentSnapshot snapshot) {
        if (_retired || capacity == 0 || Key(context) is not { } key) { return; }
        foreach (var old in _windows.Keys.Where(w => w.Hwnd == key.Hwnd && w != key).ToArray()) { _windows.Remove(old); }
        if (!_windows.ContainsKey(key) && _windows.Count >= capacity) {
            _windows.Remove(_windows.MinBy(w => w.Value.Used).Key);
        }
        _windows[key] = (snapshot, ++_clock);
    }

    public void Retire() { _retired = true; _windows.Clear(); }
}

internal sealed class ElementHintAssignments(HintAssignmentSnapshot? previous) {
    private readonly Dictionary<string, ElementHintLevelAssignments> _levels = [];
    private readonly Dictionary<int, string?> _entryKeys = [];

    public static string Layout(Rectangle bounds, int pairCapacity, int singleCapacity, VKey[] horizontal, VKey[] vertical) =>
        FormattableString.Invariant($"{bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}:{pairCapacity}:{singleCapacity}:") +
        string.Join(",", horizontal.Select(k => (int)k)) + ":" + string.Join(",", vertical.Select(k => (int)k));

    private static string Fingerprint(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public string? EntryKey(HintEntry entry) {
        if (_entryKeys.TryGetValue(entry.Id, out var key)) { return key; }
        if (entry.Target is { } target) {
            key = "T" + Fingerprint(FormattableString.Invariant(
                $"{target.ProcessId}:{target.ControlType}:{(int)target.Capabilities}:") + string.Join(",", target.RuntimeId));
        } else if (entry.RemoteGroupId == 0 && entry.Children.Count > 0) {
            var children = entry.Children.Select(EntryKey).ToArray();
            if (children.All(k => k is not null)) {
                key = "G" + Fingerprint((entry.Compact ? "compact:" : "normal:") +
                    string.Join("\n", children.Order(StringComparer.Ordinal)));
            }
        }
        _entryKeys[entry.Id] = key;
        return key;
    }

    public string ChildLevelKey(string parent, HintEntry entry) =>
        EntryKey(entry) is { } key ? Fingerprint(parent + "/" + key) : "remote:" + entry.RemoteGroupId;

    public ElementHintLevelAssignments Level(string key, bool preferSingle, int singleCapacity, int pairCapacity) {
        if (!_levels.TryGetValue(key, out var level)) {
            level = new(key, previous?.Levels.GetValueOrDefault(key), preferSingle, singleCapacity, pairCapacity);
            _levels.Add(key, level);
        } else {
            level.SetInitialScheme(preferSingle);
        }
        return level;
    }

    public HintAssignmentSnapshot Capture(string layout, out bool limited) {
        int controls = 0, groups = 0;
        limited = false;
        var levels = new Dictionary<string, RememberedHintLevel>();
        foreach (var level in _levels.Values.Where(l => !l.Key.StartsWith("remote:", StringComparison.Ordinal))) {
            var slots = new Dictionary<string, int>();
            foreach (var (key, slot) in level.RememberedSlots) {
                bool fits = key[0] == 'T' ? controls < ElementHintProtocol.MaxTargets : groups < ElementHintProtocol.MaxContainers;
                if (!fits) { limited = true; continue; }
                if (key[0] == 'T') { controls++; } else { groups++; }
                slots.Add(key, slot);
            }
            if (slots.Count > 0) { levels.Add(level.Key, new(level.SingleKey, slots)); }
        }
        return new(layout, levels);
    }
}

internal sealed class ElementHintLevelAssignments {
    private readonly RememberedHintLevel? _previous;
    private readonly int _singleCapacity, _pairCapacity;
    private readonly HashSet<int> _used;
    private readonly Dictionary<int, int> _slots = [];
    private readonly Dictionary<string, int> _remembered = [];
    private int _next;
    public string Key { get; }
    public bool SingleKey { get; private set; }
    public int Capacity => SingleKey ? _singleCapacity : _pairCapacity;
    public IReadOnlyDictionary<string, int> RememberedSlots => _remembered;

    public ElementHintLevelAssignments(string key, RememberedHintLevel? previous, bool preferSingle,
        int singleCapacity, int pairCapacity) {
        Key = key; _previous = previous;
        _singleCapacity = singleCapacity; _pairCapacity = pairCapacity;
        SingleKey = previous?.SingleKey ?? preferSingle;
        _used = previous is null ? [] : previous.Slots.Values.ToHashSet();
    }

    public void SetInitialScheme(bool preferSingle) {
        if (_previous is null && _slots.Count == 0) { SingleKey = preferSingle; }
    }

    public int Slot(int entryId) => _slots[entryId];
    public void Allocate(HintEntry entry, string? key) {
        if (_slots.ContainsKey(entry.Id)) { return; }
        int slot;
        if (key is not null && _previous?.Slots.TryGetValue(key, out slot) == true) {
            _slots.Add(entry.Id, slot);
        } else {
            while (_used.Contains(_next)) { _next++; }
            slot = _next++;
            _used.Add(slot);
            _slots.Add(entry.Id, slot);
        }
        if (key is not null) { _remembered.Add(key, slot); }
    }
}
