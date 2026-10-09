using System.Diagnostics;
using System.Drawing;

using Klikety.Automation;
using Klikety.Config;
using Klikety.Input;
using Klikety.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Klikety.Navigation;

public interface IElementHintsRenderer {
    void RebuildLabels(Grid.IKeyLabelResolver resolver);
    int GetPageCapacity(Rectangle region, int keyCapacity, bool singleKey = false);
    int GetGroupPageCapacity(Rectangle region) => 9;
    void Render(HintLevelView view);
    void FlashInvalidKey();
}

public sealed record ElementHintsHelpState(
    HintOutcome? Outcome, string Status, int Page, int PageCount, int TargetCount,
    int? Prefix, bool HasSelection, int Depth = 1, bool SingleKey = false,
    bool FocusedGroup = false, bool ArrowKeys = true, int? EntryCount = null,
    IReadOnlyList<VKey>? GroupKeys = null);

public sealed partial class ElementHintsSession : IModeSession {
    private readonly VKey[] _horizontal, _vertical;
    private readonly ActionMapper _actions;
    private readonly IElementHintsRenderer? _renderer;
    private readonly IElementHintService _service;
    private readonly ILogger _logger;
    internal ElementHintAssignmentCache? AssignmentCache { get; init; }
    private ElementHintAssignments? _assignments;
    private string _assignmentLayout = "";
    private bool _hadUsableFrame;
    private CancellationTokenSource? _lifetime;
    private HintTarget[] _targets = [];
    private HintContainer[] _containers = [];
    private readonly Dictionary<int, List<HintEntry>> _scopeEntries = [];
    private readonly HashSet<int> _plannedTokens = [], _plannedGroups = [], _completedScopes = [];
    private bool _streaming;
    private int _discoveryGeneration, _nextGroupId;
    private readonly Dictionary<int, (HintOutcome? Outcome, string Status)> _scopeStates = [];
    private Stopwatch? _discoveryWatch;
    private bool _firstHintLogged;
    private int _frameNumber;
    public Guid DiagnosticId { get; private set; }
    private readonly List<Level> _levels = [];
    private readonly bool _arrowKeys;
    private int _capacity, _page;
    private int? _prefix;
    private HintTarget? _selected;
    private string _status = "Finding controls...";
    private HintOutcome? _outcome;
    private Rectangle _bounds;
    private int? _focusedId;
    private int? _activeRegionId;
    private sealed record Level(IReadOnlyList<HintEntry> Entries, ElementHintLevelAssignments Assignments,
        ElementHintLevelAssignments GroupAssignments, bool Compact,
        int? RemoteGroupId = null) {
        public bool SingleKey => Assignments.SingleKey;
        public bool NumberedGroups => Assignments.Key == "root";
        public int Capacity => Assignments.Capacity;
        public int Page { get; set; }
        public int? FocusedId { get; set; }
        public HintOutcome? Outcome { get; set; }
        public string Status { get; set; } = "Finding controls...";
        public int Slot(HintEntry entry) => (NumberedGroups && entry.IsGroup ? GroupAssignments : Assignments).Slot(entry.Id);
        public int EntryCapacity(HintEntry entry) => NumberedGroups && entry.IsGroup ? GroupAssignments.Capacity : Capacity;
    }
    private Level? Current => _levels.LastOrDefault();
    public int Depth => _levels.Count;
    public IReadOnlyList<HintLabel> Labels => PageEntries().Select(entry => {
        int slot = Current!.Slot(entry) % Current.EntryCapacity(entry);
        if (Current.NumberedGroups && entry.IsGroup) { return new HintLabel(entry, (VKey)((int)VKey.D1 + slot), null); }
        return new HintLabel(entry, _horizontal[Current.SingleKey ? slot : slot / _vertical.Length],
            Current.SingleKey ? null : _vertical[slot % _vertical.Length]);
    }).ToArray();
    public IReadOnlyList<HintLabel> Regions {
        get {
            if (_levels.Count == 0) { return []; }
            var root = _levels[0];
            int page = Depth == 1 ? _page : root.Page;
            return root.Entries.Where(e => e.IsGroup && root.Slot(e) / root.EntryCapacity(e) == page)
                .OrderBy(e => root.Slot(e)).Select(e => new HintLabel(e,
                    (VKey)((int)VKey.D1 + root.Slot(e) % root.EntryCapacity(e)), null)).ToArray();
        }
    }

    public ElementHintsSession(VKey[] horizontal, VKey[] vertical, ActionMapper actions,
        ElementTargetContext context, IElementHintService service, IElementHintsRenderer? renderer,
        ILogger? logger = null, bool arrowKeys = true) {
        _horizontal = horizontal; _vertical = vertical; _actions = actions;
        Context = context; _service = service; _renderer = renderer;
        _logger = logger ?? NullLogger.Instance;
        _arrowKeys = arrowKeys;
        _capacity = horizontal.Length * vertical.Length;
    }
    public ElementTargetContext Context { get; }
    public int RootProcessId { get; private set; }
    public int Page => Math.Max(0, Array.IndexOf(Pages(), _page));
    public int PageCount => Math.Max(1, Pages().Length);
    private int LevelCapacity => Current?.Capacity ?? _capacity;
    public int? Prefix => _prefix;
    public HintTarget? Selected => _selected;
    public string Status => _status;
    public ElementHintsHelpState HelpState =>
        new(_outcome, _status, Page, PageCount, _targets.Length, _prefix, _selected is not null,
            Math.Max(1, Depth), Current?.SingleKey ?? false,
            PageEntries().Any(e => e.Id == _focusedId && e.IsGroup), _arrowKeys, PageEntries().Count,
            Regions.Select(l => l.First).ToArray());
    public Task Discovery { get; private set; } = Task.CompletedTask;
    public Task Retirement { get; private set; } = Task.CompletedTask;
    public event Action<Point, MouseAction>? ActionRequested;
    public event Action? Cancelled;
    public event Action<Point>? CursorMoveRequested;
    public event Action? GridFallbackRequested;
    public event Action<string>? FailureReported;
    public event Action? StateChanged;

    public void Activate(Rectangle screenBounds, Point origin) {
        DiagnosticId = Guid.NewGuid();
        _frameNumber = 0;
        _bounds = screenBounds;
        _status = "Finding controls...";
        _outcome = null;
        _assignments = null;
        _hadUsableFrame = false;
        RootProcessId = 0;
        _lifetime = new CancellationTokenSource();
        _discoveryWatch = Stopwatch.StartNew();
        if (_logger.IsEnabled(LogLevel.Debug)) {
            long hwnd = Context.Hwnd.ToInt64();
            LogHintActivation(DiagnosticId, Environment.ProcessId, hwnd, Context.ProcessId,
                _bounds, AssignmentCache is not null);
        }
        Render(relayout: true, reason: "activation");
        Discovery = DiscoverAsync(_lifetime);
    }
    private async Task DiscoverAsync(CancellationTokenSource lifetime) {
        int generation = ++_discoveryGeneration;
        var activation = DiagnosticId;
        try {
            await foreach (var response in _service.DiscoverIncrementallyAsync(Context,
                new(_bounds.X, _bounds.Y, _bounds.Width, _bounds.Height), lifetime.Token)) {
                if (lifetime.IsCancellationRequested || _lifetime != lifetime || generation != _discoveryGeneration) {
                    LogHintIgnoredFrame(activation, generation, _discoveryGeneration, response.GroupId);
                    return;
                }
                ApplyDiscovery(response);
            }
        } catch (OperationCanceledException) { return; }
    }

    private async Task DiscoverGroupAsync(int groupId, bool resume, CancellationTokenSource lifetime) {
        int generation = ++_discoveryGeneration;
        var activation = DiagnosticId;
        _outcome = null; _status = "Finding controls...";
        Render();
        try {
            var discovery = resume ? _service.ResumeAsync(groupId, lifetime.Token) : _service.ExpandAsync(groupId, lifetime.Token);
            await foreach (var response in discovery) {
                if (lifetime.IsCancellationRequested || _lifetime != lifetime || generation != _discoveryGeneration) {
                    LogHintIgnoredFrame(activation, generation, _discoveryGeneration, response.GroupId);
                    return;
                }
                ApplyDiscovery(response);
            }
        } catch (OperationCanceledException) { return; }
    }

    private void ApplyDiscovery(HintResponse response) {
        LogHintFrame(DiagnosticId, ++_frameNumber, _discoveryGeneration, response.GroupId,
            response.IsComplete, response.Outcome, response.Targets.Length, response.Containers?.Length ?? 0,
            response.Visited, response.Omitted, response.CacheHits, _discoveryWatch?.ElapsedMilliseconds ?? 0);
        bool usable = response.Outcome is HintOutcome.Success or HintOutcome.Partial or HintOutcome.NoTargets;
        if (usable) {
            _hadUsableFrame = true;
            _targets = response.Targets;
            _containers = response.Containers ?? [];
            RootProcessId = response.RootProcessId;
        }
        _streaming |= !response.IsComplete || response.Groups is { Length: > 0 };
        _outcome = response.IsComplete ? response.Outcome : null;
        if (response.IsComplete) { _completedScopes.Add(response.GroupId); }
        if (response.IsComplete) {
            LogDiscovery(response.Outcome, _targets.Length, response.Visited, response.Omitted, response.Reason,
                _discoveryWatch?.ElapsedMilliseconds ?? 0, response.CacheHits, response.GroupId);
        }
        _status = !response.IsComplete ? "Finding controls..." : response.Outcome switch {
            HintOutcome.Success => "",
            HintOutcome.Partial => "Some controls unavailable",
            HintOutcome.NoTargets => "No controls found",
            HintOutcome.Timeout => "Control discovery timed out",
            HintOutcome.AccessDenied => "Application access denied",
            HintOutcome.Unavailable => "Element hints unavailable",
            HintOutcome.InvalidRoot => "Application unavailable",
            HintOutcome.StaleTarget => "Controls changed; reopen hints",
            HintOutcome.CleanupFailed => "Helper cleanup pending; reopen hints to retry",
            _ => "Control discovery failed",
        };
        _scopeStates[response.GroupId] = (_outcome, _status);
        if (!_streaming) { Render(relayout: true, reason: "complete-snapshot"); return; }
        if (usable) {
            foreach (var group in response.Groups ?? []) {
                if (!_plannedGroups.Add(group.Id)) { continue; }
                Entries(group.ParentId).Add(new(-ElementHintProtocol.MaxNodes - group.Id, null,
                    group.Bounds, group.Bounds.Center, $"Controls ({group.ChildCount}+ children)", [],
                    RemoteGroupId: group.Id));
            }
            foreach (var scope in _targets.Where(t => !_plannedTokens.Contains(t.Token)).GroupBy(t => t.DiscoveryGroupId)) {
                var targets = scope.OrderBy(t => t.Bounds.Y).ThenBy(t => t.Bounds.X).ThenBy(t => t.Token).ToArray();
                var planner = new ElementHintHierarchy(_targets, _containers, _nextGroupId);
                var entries = Entries(scope.Key);
                int previousCount = entries.Count, rememberedCount = 0, childCapacity = _capacity;
                bool overflow = scope.Key == 0 && entries.Count + targets.Length > _capacity;
                if (overflow) {
                    var remembered = targets.Where(t => _assignments!.WasAssigned("root", t)).ToArray();
                    rememberedCount = remembered.Length;
                    entries.AddRange(remembered.Select(ElementHintHierarchy.Leaf));
                    childCapacity = Math.Clamp(_renderer?.GetPageCapacity(_bounds, _horizontal.Length,
                        singleKey: true) ?? _horizontal.Length, 1, _horizontal.Length);
                    entries.AddRange(planner.BuildRegions(targets.Except(remembered).ToArray(), childCapacity));
                } else {
                    entries.AddRange(planner.Build(targets, _capacity));
                }
                if (_logger.IsEnabled(LogLevel.Debug)) {
                    int groups = entries.Count(e => e.IsGroup);
                    LogHintPlan(DiagnosticId, scope.Key, targets.Length, previousCount, entries.Count,
                        groups, overflow, rememberedCount, _capacity, childCapacity);
                }
                _nextGroupId = planner.NextGroupId;
                foreach (var target in targets) { _plannedTokens.Add(target.Token); }
            }
            for (int i = 0; i < _levels.Count; i++) {
                var level = _levels[i];
                if (level.RemoteGroupId is not { } scope) { continue; }
                var known = Entries(scope);
                var existing = level.Entries.Select(e => e.Id).ToHashSet();
                var additions = known.Where(e => !existing.Contains(e.Id)).ToArray();
                if (level.Entries.Count == 0 && additions.Length > 0) {
                    _levels[i] = CreateLevel(additions, level.Compact, scope, freezePairs: !response.IsComplete,
                        key: level.Assignments.Key);
                } else if (additions.Length > 0) {
                    Allocate(level.Assignments, level.GroupAssignments, additions);
                    _levels[i] = level with { Entries = [.. level.Entries, .. additions] };
                }
                if (scope == response.GroupId) {
                    _levels[i].Outcome = _outcome; _levels[i].Status = _status;
                }
            }
        }
        if (!usable && Current is { } current) { current.Outcome = _outcome; current.Status = _status; }
        Render(reason: "discovery-frame");
        if (!_firstHintLogged && Labels.Count > 0) {
            _firstHintLogged = true;
            LogFirstHints(_discoveryWatch?.ElapsedMilliseconds ?? 0, Labels.Count, response.CacheHits);
        }
    }

    private List<HintEntry> Entries(int scope) {
        if (!_scopeEntries.TryGetValue(scope, out var entries)) { _scopeEntries.Add(scope, entries = []); }
        return entries;
    }

    public void OnKey(VKey key) {
        if (_lifetime is null) { return; }
        if (key == VKey.Return) { GridFallbackRequested?.Invoke(); return; }
        if (key == VKey.Escape) {
            if (_prefix is not null) { _prefix = null; _selected = null; Render(); } else if (_levels.Count > 1) {
                bool supersededRemote = Current!.RemoteGroupId is > 0;
                _levels.RemoveAt(_levels.Count - 1);
                if (supersededRemote) { _discoveryGeneration++; }
                _page = Current!.Page; _focusedId = Current.FocusedId; _selected = null;
                if (Depth == 1) { _activeRegionId = null; }
                _outcome = Current.Outcome; _status = Current.Status;
                Render(reason: "level-back");
                var parentScope = _levels.LastOrDefault(l => l.RemoteGroupId is not null)?.RemoteGroupId;
                if (supersededRemote && _streaming && parentScope is { } scope && !_completedScopes.Contains(scope)) {
                    Discovery = DiscoverGroupAsync(scope, resume: true, _lifetime);
                }
            } else { Cancelled?.Invoke(); }
            return;
        }
        if (key is VKey.Prior or VKey.Next) {
            if (PageCount <= 1) { _renderer?.FlashInvalidKey(); return; }
            var pages = Pages();
            _page = pages[(Page + (key == VKey.Prior ? pages.Length - 1 : 1)) % pages.Length];
            _prefix = null; _selected = null; _focusedId = null; Render(); return;
        }
        if (key is VKey.Left or VKey.Right or VKey.Up or VKey.Down) {
            if (!_arrowKeys || PageEntries().Count == 0) { _renderer?.FlashInvalidKey(); return; }
            MoveFocus(key); return;
        }
        if (key is >= VKey.D1 and <= VKey.D9) {
            var regions = Regions;
            var group = regions.FirstOrDefault(l => l.First == key);
            if (_logger.IsEnabled(LogLevel.Debug)) {
                LogHintNumber(DiagnosticId, key, Depth, Page, _page, group?.Entry.Id, regions.Count);
            }
            if (group is not null) { SwitchRegion(group.Entry); } else { _renderer?.FlashInvalidKey(); }
            return;
        }
        if (_actions.Map(key) is { } action) {
            if (_selected is null) { _renderer?.FlashInvalidKey(); return; }
            ActionRequested?.Invoke(new(_selected.Preview.X, _selected.Preview.Y), action); return;
        }
        int first = Array.IndexOf(_horizontal, key), second = Array.IndexOf(_vertical, key);
        if (Current?.SingleKey == true && first >= 0) {
            if (EntryAt(first) is { } entry) { Choose(entry); return; }
        } else if (PageEntries().Count > 0 && first >= 0) {
            _prefix = first; _selected = null; _focusedId = null; Render(); return;
        }
        if (_prefix is { } col && second >= 0) {
            int index = col * _vertical.Length + second;
            if (EntryAt(index) is { } entry) { Choose(entry); return; }
        }
        _renderer?.FlashInvalidKey();
    }

    public Task<HintResponse> ValidateAsync(CancellationToken ct, bool moveOnly = false) =>
        _selected is { } target ? _service.ValidateAsync(target.Token, ct, moveOnly) :
        Task.FromResult(new HintResponse(ElementHintProtocol.Version, Guid.Empty, Guid.Empty, HintOutcome.StaleTarget, []));

    public void Redraw() => Render(reason: "glyph-redraw");
    public void Relayout() {
        if (_streaming) { _discoveryGeneration++; }
        _assignments = new(null);
        Render(relayout: true, reason: "viewport-relayout");
        if (_streaming && !_completedScopes.Contains(0) && _lifetime is { } lifetime) {
            Discovery = DiscoverGroupAsync(0, resume: true, lifetime);
        }
    }
    private int[] Pages() => Current?.Entries.Select(e => Current.Slot(e) / Current.EntryCapacity(e))
        .Distinct().Order().ToArray() ?? [];
    private List<HintEntry> PageEntries() =>
        Current?.Entries.Where(e => Current.Slot(e) / Current.EntryCapacity(e) == _page)
            .OrderBy(e => Current.Slot(e)).ThenBy(e => e.Id).ToList() ?? [];
    private HintEntry? EntryAt(int slot) => PageEntries().FirstOrDefault(e => (!Current!.NumberedGroups || !e.IsGroup) &&
        Current!.Slot(e) % LevelCapacity == slot);
    private void Allocate(ElementHintLevelAssignments assignments, ElementHintLevelAssignments groups,
        IReadOnlyList<HintEntry> entries) {
        foreach (var entry in entries) {
            (assignments.Key == "root" && entry.IsGroup ? groups : assignments).Allocate(entry, _assignments!.EntryKey(entry));
        }
    }
    private Level CreateLevel(IReadOnlyList<HintEntry> entries, bool compact, int? remoteGroupId = null,
        bool freezePairs = false, string key = "root") {
        int singleCapacity = Math.Clamp(_renderer?.GetPageCapacity(_bounds, _horizontal.Length, singleKey: true) ??
            _horizontal.Length, 1, _horizontal.Length);
        bool singleKey = !freezePairs && entries.Count(e => key != "root" || !e.IsGroup) <= singleCapacity;
        var assignments = _assignments!.Level(key, singleKey, singleCapacity, _capacity);
        int groupCapacity = Math.Clamp(_renderer?.GetGroupPageCapacity(_bounds) ?? 9, 1, 9);
        var groups = _assignments.Level(key + ":groups", true, groupCapacity, groupCapacity);
        Allocate(assignments, groups, entries);
        return new(entries, assignments, groups, compact, remoteGroupId) {
            Outcome = _outcome, Status = _status
        };
    }
    private void SwitchRegion(HintEntry entry) {
        int previousDepth = Depth;
        bool supersededRemote = _levels.Skip(1).Any(l => l.RemoteGroupId is > 0);
        LogHintRegionSwitch(DiagnosticId, _activeRegionId, entry.Id, previousDepth);
        if (Depth > 1) {
            _levels.RemoveRange(1, Depth - 1);
            _page = Current!.Page; _outcome = Current.Outcome; _status = Current.Status;
            if (supersededRemote) { _discoveryGeneration++; }
        }
        Choose(entry);
        if (supersededRemote && entry.RemoteGroupId == 0 && _streaming &&
            !_completedScopes.Contains(0) && _lifetime is { } lifetime) {
            Discovery = DiscoverGroupAsync(0, resume: true, lifetime);
        }
    }
    private void Choose(HintEntry entry) {
        _prefix = null; _selected = null; _focusedId = entry.Id;
        if (entry.IsGroup) {
            if (Depth == 1) { _activeRegionId = entry.Id; }
            Current!.Page = _page; Current.FocusedId = entry.Id;
            var children = entry.RemoteGroupId != 0 ? Entries(entry.RemoteGroupId) : entry.Children;
            LogHintGroupOpened(DiagnosticId, entry.Id, Depth, children.Count, entry.RemoteGroupId,
                entry.Compact, entry.Bounds);
            string key = _assignments!.ChildLevelKey(Current.Assignments.Key, entry);
            _levels.Add(CreateLevel(children.ToArray(), entry.Compact,
                entry.RemoteGroupId == 0 ? null : entry.RemoteGroupId,
                freezePairs: entry.RemoteGroupId != 0 && !_completedScopes.Contains(entry.RemoteGroupId), key: key));
            _page = Pages().FirstOrDefault(); _focusedId = null; Render(reason: "group-open");
            if (entry.RemoteGroupId != 0 && !_completedScopes.Contains(entry.RemoteGroupId) && _lifetime is { } lifetime) {
                Discovery = DiscoverGroupAsync(entry.RemoteGroupId, resume: false, lifetime);
            }
        } else {
            _selected = entry.Target; Render();
            CursorMoveRequested?.Invoke(new(entry.Preview.X, entry.Preview.Y));
        }
    }
    private void MoveFocus(VKey key) {
        var entries = PageEntries();
        int index = entries.ToList().FindIndex(e => e.Id == _focusedId);
        int next = 0;
        if (index >= 0) {
            var point = entries[index].Preview;
            bool horizontal = key is VKey.Left or VKey.Right;
            int direction = key is VKey.Left or VKey.Up ? -1 : 1;
            var candidates = entries.Select((entry, i) => {
                double along = horizontal ? (double)entry.Preview.X - point.X : (double)entry.Preview.Y - point.Y;
                double across = horizontal ? (double)entry.Preview.Y - point.Y : (double)entry.Preview.X - point.X;
                return (Index: i, Along: direction * along, Across: Math.Abs(across));
            }).Where(c => c.Index != index && c.Along > 0)
                .OrderBy(c => c.Along + c.Across * 2).ThenBy(c => c.Index).ToArray();
            next = Current!.Compact || candidates.Length == 0
                ? (index + direction + entries.Count) % entries.Count : candidates[0].Index;
        }
        var focused = entries[next];
        _focusedId = focused.Id; _prefix = null; _selected = focused.Target;
        Render();
        if (_selected is not null) {
            CursorMoveRequested?.Invoke(new(focused.Preview.X, focused.Preview.Y));
        }
    }
    private void Render(bool relayout = false, string reason = "input-state") {
        long started = _logger.IsEnabled(LogLevel.Debug) ? Stopwatch.GetTimestamp() : 0;
        if (relayout) {
            _capacity = Math.Clamp(_renderer?.GetPageCapacity(_bounds, _horizontal.Length * _vertical.Length) ??
                _horizontal.Length * _vertical.Length, 1, _horizontal.Length * _vertical.Length);
            _page = 0; _prefix = null; _selected = null; _focusedId = null;
            _activeRegionId = null;
            _levels.Clear();
            int singleCapacity = Math.Clamp(_renderer?.GetPageCapacity(_bounds, _horizontal.Length, singleKey: true) ??
                _horizontal.Length, 1, _horizontal.Length);
            _assignmentLayout = ElementHintAssignments.Layout(_bounds, _capacity, singleCapacity, _horizontal, _vertical);
            if (_assignments is null) {
                var snapshot = AssignmentCache?.Load(Context, _assignmentLayout);
                _assignments = new(snapshot);
                if (_logger.IsEnabled(LogLevel.Debug)) {
                    int records = snapshot?.Levels.Values.Sum(l => l.Slots.Count) ?? 0;
                    LogHintAssignmentCache(DiagnosticId, snapshot is not null, snapshot?.Levels.Count ?? 0,
                        records, AssignmentCache?.Count ?? 0);
                }
            }
            if (_streaming) {
                if (_scopeStates.TryGetValue(0, out var state)) { _outcome = state.Outcome; _status = state.Status; }
                _levels.Add(CreateLevel(Entries(0).ToArray(), compact: false, remoteGroupId: 0,
                    freezePairs: !_completedScopes.Contains(0)));
            } else {
                _levels.Add(CreateLevel(new ElementHintHierarchy(_targets, _containers).Build(_targets,
                    Math.Max(_capacity, singleCapacity)), compact: false, remoteGroupId: 0));
            }
            LogPageCapacity(_capacity);
        }
        if (!Pages().Contains(_page)) { _page = Pages().FirstOrDefault(); }
        _renderer?.GetPageCapacity(_bounds, Current?.SingleKey == true ? _horizontal.Length :
            _horizontal.Length * _vertical.Length, Current?.SingleKey == true);
        var labels = Labels;
        var regions = Regions;
        _renderer?.Render(new(labels, Math.Max(1, Depth), Page, PageCount, _prefix,
            _focusedId, _selected?.Token, _status, Current?.SingleKey ?? false, _arrowKeys,
            Current?.Compact ?? false, _lifetime is not null && _outcome is null, DiagnosticId,
            Depth > 1 ? regions : null, _activeRegionId));
        if (_logger.IsEnabled(LogLevel.Debug) && Current is { } current) {
            int groups = labels.Count(l => l.Entry.IsGroup);
            double renderMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            int regionPage = Depth == 1 ? _page : _levels[0].Page;
            LogHintRegionState(DiagnosticId, _activeRegionId, regions.Count, regionPage);
            LogHintRender(DiagnosticId, reason, relayout, Depth, Page, _page, PageCount, labels.Count,
                groups, current.Capacity, current.GroupAssignments.Capacity,
                current.Assignments.ReservedCount, current.Assignments.RestoredCount,
                current.GroupAssignments.ReservedCount, current.GroupAssignments.RestoredCount,
                renderMs);
            foreach (var label in labels.Where(l => l.Entry.IsGroup)) {
                int slot = current.Slot(label.Entry);
                LogHintVisibleGroup(DiagnosticId, label.First, label.Entry.Id, slot,
                    label.Entry.Children.Count, label.Entry.Bounds);
            }
        }
        StateChanged?.Invoke();
    }
    public void Suspend() => Deactivate();
    public void Deactivate() {
        if (_lifetime is not null) {
            LogHintClosed(DiagnosticId, _outcome, _targets.Length, Depth, _discoveryWatch?.ElapsedMilliseconds ?? 0);
        }
        if (_lifetime is not null && _hadUsableFrame && _assignments is not null) {
            var snapshot = _assignments.Capture(_assignmentLayout, out bool limited);
            AssignmentCache?.Save(Context, snapshot);
            if (_logger.IsEnabled(LogLevel.Debug)) {
                int records = snapshot.Levels.Values.Sum(l => l.Slots.Count);
                LogHintAssignmentsSaved(DiagnosticId, snapshot.Levels.Count, records, limited);
            }
            if (limited) { LogAssignmentLimit(); }
        }
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
        _discoveryGeneration++;
        _targets = []; _containers = []; _levels.Clear(); _prefix = null; _selected = null; _page = 0; _focusedId = null;
        _activeRegionId = null;
        _scopeEntries.Clear(); _plannedTokens.Clear(); _plannedGroups.Clear(); _completedScopes.Clear();
        _scopeStates.Clear(); _streaming = false; _nextGroupId = 0; _firstHintLogged = false;
        _discoveryWatch = null;
        _assignments = null; _hadUsableFrame = false;
        Retirement = RetireAsync();
    }
    private async Task RetireAsync() {
        if (!await _service.ReleaseAsync()) {
            string reason = _service.CleanupFailureReason ?? "Teardown unconfirmed";
            LogCleanupPending(reason);
            FailureReported?.Invoke($"UIA helper cleanup pending ({reason}); reopen hints to retry.");
        }
    }
    [LoggerMessage(Level = LogLevel.Warning, Message = "UIA helper cleanup pending: {Reason}")]
    private partial void LogCleanupPending(string reason);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Element hint assignment retention reached its record limit.")]
    private partial void LogAssignmentLimit();
    [LoggerMessage(Level = LogLevel.Debug, Message = "UIA discovery {Outcome}: retained={Retained}, visited={Visited}, omitted={Omitted}, reason={Reason}, elapsedMs={ElapsedMs}, cacheHits={CacheHits}, scope={Scope}")]
    private partial void LogDiscovery(HintOutcome outcome, int retained, int visited, int omitted, string? reason,
        long elapsedMs, int cacheHits, int scope);
    [LoggerMessage(Level = LogLevel.Debug, Message = "UIA first usable hints: elapsedMs={ElapsedMs}, entries={Entries}, cacheHits={CacheHits}")]
    private partial void LogFirstHints(long elapsedMs, int entries, int cacheHits);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Element hint pages recomputed for viewport: capacity={Capacity}")]
    private partial void LogPageCapacity(int capacity);
    [LoggerMessage(Level = LogLevel.Debug, Message = "HintDiag activation={Activation} start appPid={AppPid} hwnd={Hwnd} targetPid={TargetPid} bounds={Bounds} assignmentCacheEnabled={AssignmentCacheEnabled}")]
    private partial void LogHintActivation(Guid activation, int appPid, long hwnd, int targetPid, Rectangle bounds, bool assignmentCacheEnabled);
    [LoggerMessage(Level = LogLevel.Debug, Message = "HintDiag activation={Activation} ignored-frame generation={Generation} currentGeneration={CurrentGeneration} scope={Scope}")]
    private partial void LogHintIgnoredFrame(Guid activation, int generation, int currentGeneration, int scope);
    [LoggerMessage(Level = LogLevel.Debug, Message = "HintDiag activation={Activation} region-switch previous={Previous} next={Next} previousDepth={PreviousDepth}")]
    private partial void LogHintRegionSwitch(Guid activation, int? previous, int next, int previousDepth);
    [LoggerMessage(Level = LogLevel.Debug, Message = "HintDiag activation={Activation} region-state active={Active} visible={Visible} rootPage={RootPage}")]
    private partial void LogHintRegionState(Guid activation, int? active, int visible, int rootPage);
    [LoggerMessage(Level = LogLevel.Debug, Message = "HintDiag activation={Activation} frame={Frame} generation={Generation} scope={Scope} complete={Complete} outcome={Outcome} targets={Targets} containers={Containers} visited={Visited} omitted={Omitted} sourceCacheHits={SourceCacheHits} elapsedMs={ElapsedMs}")]
    private partial void LogHintFrame(Guid activation, int frame, int generation, int scope, bool complete, HintOutcome outcome,
        int targets, int containers, int visited, int omitted, int sourceCacheHits, long elapsedMs);
    [LoggerMessage(Level = LogLevel.Debug, Message = "HintDiag activation={Activation} plan scope={Scope} incoming={Incoming} previousEntries={PreviousEntries} entries={Entries} groups={Groups} overflow={Overflow} rememberedRootControls={RememberedRootControls} capacity={Capacity} childCapacity={ChildCapacity}")]
    private partial void LogHintPlan(Guid activation, int scope, int incoming, int previousEntries, int entries, int groups,
        bool overflow, int rememberedRootControls, int capacity, int childCapacity);
    [LoggerMessage(Level = LogLevel.Debug, Message = "HintDiag activation={Activation} number key={Key} depth={Depth} page={Page} logicalPage={LogicalPage} matchedGroup={MatchedGroup} visibleGroups={VisibleGroups}")]
    private partial void LogHintNumber(Guid activation, VKey key, int depth, int page, int logicalPage, int? matchedGroup, int visibleGroups);
    [LoggerMessage(Level = LogLevel.Debug, Message = "HintDiag activation={Activation} open-group id={Id} parentDepth={ParentDepth} children={Children} remoteScope={RemoteScope} compact={Compact} bounds={Bounds}")]
    private partial void LogHintGroupOpened(Guid activation, int id, int parentDepth, int children, int remoteScope, bool compact, HintRect bounds);
    [LoggerMessage(Level = LogLevel.Debug, Message = "HintDiag activation={Activation} assignment-cache reused={Reused} levels={Levels} records={Records} windows={Windows}")]
    private partial void LogHintAssignmentCache(Guid activation, bool reused, int levels, int records, int windows);
    [LoggerMessage(Level = LogLevel.Debug, Message = "HintDiag activation={Activation} render reason={Reason} relayout={Relayout} depth={Depth} page={Page} logicalPage={LogicalPage} pages={Pages} labels={Labels} groups={Groups} controlCapacity={ControlCapacity} groupCapacity={GroupCapacity} reservedControls={ReservedControls} restoredControls={RestoredControls} reservedGroups={ReservedGroups} restoredGroups={RestoredGroups} renderMs={RenderMs}")]
    private partial void LogHintRender(Guid activation, string reason, bool relayout, int depth, int page, int logicalPage, int pages,
        int labels, int groups, int controlCapacity, int groupCapacity, int reservedControls, int restoredControls,
        int reservedGroups, int restoredGroups, double renderMs);
    [LoggerMessage(Level = LogLevel.Debug, Message = "HintDiag activation={Activation} visible-group key={Key} id={Id} slot={Slot} children={Children} bounds={Bounds}")]
    private partial void LogHintVisibleGroup(Guid activation, VKey key, int id, int slot, int children, HintRect bounds);
    [LoggerMessage(Level = LogLevel.Debug, Message = "HintDiag activation={Activation} close outcome={Outcome} targets={Targets} depth={Depth} elapsedMs={ElapsedMs}")]
    private partial void LogHintClosed(Guid activation, HintOutcome? outcome, int targets, int depth, long elapsedMs);
    [LoggerMessage(Level = LogLevel.Debug, Message = "HintDiag activation={Activation} assignment-cache saved levels={Levels} records={Records} limited={Limited}")]
    private partial void LogHintAssignmentsSaved(Guid activation, int levels, int records, bool limited);
}
