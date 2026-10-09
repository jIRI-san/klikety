using Klikety.Automation;
using Klikety.Input;

namespace Klikety.Navigation;

public sealed record HintEntry(int Id, HintTarget? Target, HintRect Bounds, HintPoint Preview,
    string Description, IReadOnlyList<HintEntry> Children, bool Compact = false, int RemoteGroupId = 0) {
    public bool IsGroup => Target is null;
}

public sealed record HintLabel(HintEntry Entry, VKey First, VKey? Second);
public sealed record HintLevelView(IReadOnlyList<HintLabel> Labels, int Depth, int Page, int PageCount,
    int? Prefix, int? FocusedId, int? SelectedToken, string Status, bool SingleKey,
    bool ArrowKeys, bool Compact, bool IsDiscovering = false);

internal sealed class ElementHintHierarchy {
    private int _nextGroup;
    private readonly Dictionary<int, HintContainer> _containers;
    private readonly Dictionary<int, HashSet<int>> _members;

    public int NextGroupId => _nextGroup;
    public ElementHintHierarchy(HintTarget[] targets, HintContainer[] containers, int nextGroupId = 0) {
        _nextGroup = nextGroupId;
        _containers = containers.ToDictionary(c => c.Id);
        _members = containers.ToDictionary(c => c.Id, _ => new HashSet<int>());
        foreach (var target in targets) {
            for (int id = target.ContainerId; id != 0; id = _containers[id].ParentId) {
                _members[id].Add(target.Token);
            }
        }
    }

    public IReadOnlyList<HintEntry> Build(HintTarget[] targets, int capacity) {
        var leaves = targets.Select(t => new HintEntry(t.Token, t, t.VisibleBounds, t.Preview,
            Describe(t), [], false)).ToList();
        return Plan(leaves, capacity, []);
    }

    private List<HintEntry> Plan(List<HintEntry> entries, int capacity, HashSet<int> excluded) {
        // A parent/child relationship alone is not enough: their badges must also compete for space.
        foreach (var container in _containers.Values) {
            if (excluded.Contains(container.Id) || container.TargetToken == 0) { continue; }
            var own = entries.FirstOrDefault(e => e.Target?.Token == container.TargetToken);
            if (own is null) { continue; }
            var members = entries.Where(e => Belongs(e, container.Id)).ToList();
            if (members.Count < 2 || !members.Any(e => e.Id != own.Id && Competes(own.Bounds, e.Bounds))) { continue; }
            Replace(entries, members, Group(members, capacity, excluded, container, compact: true));
        }
        while (entries.Count > capacity) {
            var candidate = _containers.Values.Where(c => !excluded.Contains(c.Id))
                .Select(c => (Container: c, Entries: entries.Where(e => Belongs(e, c.Id)).ToList()))
                .Where(c => c.Entries.Count >= 2 && c.Entries.Count < entries.Count)
                .OrderByDescending(c => c.Entries.Count).ThenBy(c => c.Container.Id).FirstOrDefault();
            if (candidate.Container is null) { break; }
            Replace(entries, candidate.Entries,
                Group(candidate.Entries, capacity, excluded, candidate.Container, compact: false));
        }
        // With one readable slot, grouping cannot reduce a level. This is the paging fallback.
        while (capacity > 1 && entries.Count > capacity) {
            entries = entries.Chunk(capacity).Select(chunk => chunk.Length == 1 ? chunk[0] :
                NewGroup(chunk, chunk, compact: false, "Controls")).ToList();
        }
        return entries;
    }

    private HintEntry Group(List<HintEntry> members, int capacity, HashSet<int> excluded,
        HintContainer container, bool compact) {
        var nextExcluded = new HashSet<int>(excluded) { container.Id };
        // Already formed groups retain their children; parent-own-action leaves remain selectable.
        var children = Plan(members.ToList(), capacity, nextExcluded);
        return NewGroup(members, children, compact, Role(container.ControlType));
    }

    private HintEntry NewGroup(IReadOnlyList<HintEntry> members, IReadOnlyList<HintEntry> children,
        bool compact, string role) {
        double x = members.Min(e => e.Bounds.X), y = members.Min(e => e.Bounds.Y);
        var bounds = new HintRect(x, y, members.Max(e => e.Bounds.X + e.Bounds.Width) - x,
            members.Max(e => e.Bounds.Y + e.Bounds.Height) - y);
        return new(--_nextGroup, null, bounds, bounds.Center,
            $"{role} ({CountTargets(members)} controls)", children, compact);
    }

    private static int CountTargets(IReadOnlyList<HintEntry> entries) =>
        entries.Sum(e => e.Target is not null ? 1 : CountTargets(e.Children));
    private bool Belongs(HintEntry entry, int container) =>
        entry.Target is { } target ? _members[container].Contains(target.Token) :
        entry.Children.All(e => Belongs(e, container));
    private static void Replace(List<HintEntry> entries, List<HintEntry> members, HintEntry group) {
        int index = entries.FindIndex(e => e.Id == members[0].Id);
        entries.RemoveAll(e => members.Any(m => m.Id == e.Id));
        entries.Insert(index, group);
    }
    private static bool Competes(HintRect a, HintRect b) {
        var overlap = a.Clip(b);
        return overlap.IsValid && (overlap.Width * overlap.Height >= .8 * Math.Max(a.Width * a.Height, b.Width * b.Height) ||
            Math.Abs(a.Center.X - b.Center.X) < 36 && Math.Abs(a.Center.Y - b.Center.Y) < 24);
    }
    private static string Describe(HintTarget target) {
        string actions = string.Join("/", new[] {
            (HintCapabilities.Invoke, "invoke"), (HintCapabilities.Toggle, "toggle"),
            (HintCapabilities.Selection, "select"), (HintCapabilities.Expand, "expand"),
            (HintCapabilities.Value, "edit")
        }.Where(pair => target.Capabilities.HasFlag(pair.Item1)).Select(pair => pair.Item2));
        return Role(target.ControlType) + (actions.Length == 0 ? "" : $" - {actions}");
    }
    private static string Role(int type) => type switch {
        50000 => "Button",
        50002 => "Check box",
        50003 => "Combo box",
        50004 => "Edit",
        50005 => "Link",
        50007 => "List item",
        50011 => "Menu item",
        50013 => "Radio button",
        50015 => "Slider",
        50016 => "Spinner",
        50019 => "Tab",
        50024 => "Tree item",
        50029 => "Data item",
        _ => "Controls"
    };
}
