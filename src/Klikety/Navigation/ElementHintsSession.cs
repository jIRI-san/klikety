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
    void Render(HintLevelView view);
    void FlashInvalidKey();
}

public sealed record ElementHintsHelpState(
    HintOutcome? Outcome, string Status, int Page, int PageCount, int TargetCount,
    int? Prefix, bool HasSelection, int Depth = 1, bool SingleKey = false,
    bool FocusedGroup = false, bool ArrowKeys = true);

public sealed partial class ElementHintsSession : IModeSession {
    private readonly VKey[] _horizontal, _vertical;
    private readonly ActionMapper _actions;
    private readonly IElementHintsRenderer? _renderer;
    private readonly IElementHintService _service;
    private readonly ILogger _logger;
    private CancellationTokenSource? _lifetime;
    private HintTarget[] _targets = [];
    private HintContainer[] _containers = [];
    private readonly List<Level> _levels = [];
    private readonly bool _arrowKeys;
    private int _capacity, _page;
    private int? _prefix;
    private HintTarget? _selected;
    private string _status = "Finding controls...";
    private HintOutcome? _outcome;
    private Rectangle _bounds;
    private int? _focusedId;
    private sealed record Level(IReadOnlyList<HintEntry> Entries, bool SingleKey, bool Compact, int Capacity) {
        public int Page { get; set; }
        public int? FocusedId { get; set; }
    }
    private Level? Current => _levels.LastOrDefault();
    public int Depth => _levels.Count;
    public IReadOnlyList<HintLabel> Labels => PageEntries().Select((entry, index) => new HintLabel(entry,
        _horizontal[Current?.SingleKey == true ? index : index / _vertical.Length],
        Current?.SingleKey == true ? null : _vertical[index % _vertical.Length])).ToArray();

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
    public int Page => _page;
    public int PageCount => Math.Max(1, ((Current?.Entries.Count ?? 0) + LevelCapacity - 1) / LevelCapacity);
    private int LevelCapacity => Current?.Capacity ?? _capacity;
    public int? Prefix => _prefix;
    public HintTarget? Selected => _selected;
    public string Status => _status;
    public ElementHintsHelpState HelpState =>
        new(_outcome, _status, _page, PageCount, _targets.Length, _prefix, _selected is not null,
            Math.Max(1, Depth), Current?.SingleKey ?? false,
            PageEntries().Any(e => e.Id == _focusedId && e.IsGroup), _arrowKeys);
    public Task Discovery { get; private set; } = Task.CompletedTask;
    public Task Retirement { get; private set; } = Task.CompletedTask;
    public event Action<Point, MouseAction>? ActionRequested;
    public event Action? Cancelled;
    public event Action<Point>? CursorMoveRequested;
    public event Action? GridFallbackRequested;
    public event Action<string>? FailureReported;
    public event Action? StateChanged;

    public void Activate(Rectangle screenBounds, Point origin) {
        _bounds = screenBounds;
        _status = "Finding controls...";
        _outcome = null;
        RootProcessId = 0;
        _lifetime = new CancellationTokenSource();
        Render(relayout: true);
        Discovery = DiscoverAsync(_lifetime);
    }
    private async Task DiscoverAsync(CancellationTokenSource lifetime) {
        HintResponse response;
        try {
            response = await _service.DiscoverAsync(Context,
                new(_bounds.X, _bounds.Y, _bounds.Width, _bounds.Height), lifetime.Token);
        } catch (OperationCanceledException) { return; }
        if (lifetime.IsCancellationRequested || _lifetime != lifetime) { return; }
        _targets = response.Targets;
        _containers = response.Containers ?? [];
        _outcome = response.Outcome;
        RootProcessId = response.RootProcessId;
        LogDiscovery(response.Outcome, _targets.Length, response.Visited, response.Omitted, response.Reason);
        _status = response.Outcome switch {
            HintOutcome.Success => "",
            HintOutcome.Partial => "Some controls unavailable",
            HintOutcome.NoTargets => "No controls found",
            HintOutcome.Timeout => "Control discovery timed out",
            HintOutcome.AccessDenied => "Application access denied",
            HintOutcome.Unavailable => "Element hints unavailable",
            HintOutcome.InvalidRoot => "Application unavailable",
            HintOutcome.CleanupFailed => "Helper cleanup failed; restart Klikety",
            _ => "Control discovery failed",
        };
        Render(relayout: true);
    }

    public void OnKey(VKey key) {
        if (_lifetime is null) { return; }
        if (key == VKey.Return) { GridFallbackRequested?.Invoke(); return; }
        if (key == VKey.Escape) {
            if (_prefix is not null) { _prefix = null; _selected = null; Render(); } else if (_levels.Count > 1) {
                _levels.RemoveAt(_levels.Count - 1);
                _page = Current!.Page; _focusedId = Current.FocusedId; _selected = null;
                Render();
            } else { Cancelled?.Invoke(); }
            return;
        }
        if (key is VKey.Prior or VKey.Next) {
            if (PageCount <= 1) { _renderer?.FlashInvalidKey(); return; }
            _page = (_page + (key == VKey.Prior ? PageCount - 1 : 1)) % PageCount;
            _prefix = null; _selected = null; _focusedId = null; Render(); return;
        }
        if (key is VKey.Left or VKey.Right or VKey.Up or VKey.Down) {
            if (!_arrowKeys || PageEntries().Count == 0) { _renderer?.FlashInvalidKey(); return; }
            MoveFocus(key); return;
        }
        if (_actions.Map(key) is { } action) {
            if (_selected is null) { _renderer?.FlashInvalidKey(); return; }
            ActionRequested?.Invoke(new(_selected.Preview.X, _selected.Preview.Y), action); return;
        }
        int first = Array.IndexOf(_horizontal, key), second = Array.IndexOf(_vertical, key);
        if (Current?.SingleKey == true && first >= 0) {
            if (first < PageEntries().Count) { Choose(PageEntries()[first]); return; }
        } else if (PageEntries().Count > 0 && first >= 0) {
            _prefix = first; _selected = null; _focusedId = null; Render(); return;
        }
        if (_prefix is { } col && second >= 0) {
            int index = col * _vertical.Length + second;
            if (index < PageEntries().Count) { Choose(PageEntries()[index]); return; }
        }
        _renderer?.FlashInvalidKey();
    }

    public Task<HintResponse> ValidateAsync(CancellationToken ct, bool moveOnly = false) =>
        _selected is { } target ? _service.ValidateAsync(target.Token, ct, moveOnly) :
        Task.FromResult(new HintResponse(ElementHintProtocol.Version, Guid.Empty, Guid.Empty, HintOutcome.StaleTarget, []));

    public void Redraw() => Render();
    public void Relayout() => Render(relayout: true);
    private List<HintEntry> PageEntries() =>
        Current?.Entries.Skip(_page * LevelCapacity).Take(LevelCapacity).ToList() ?? [];
    private Level CreateLevel(IReadOnlyList<HintEntry> entries, bool compact) {
        int singleCapacity = Math.Clamp(_renderer?.GetPageCapacity(_bounds, _horizontal.Length, singleKey: true) ??
            _horizontal.Length, 1, _horizontal.Length);
        bool singleKey = entries.Count <= singleCapacity;
        return new(entries, singleKey, compact, singleKey ? singleCapacity : _capacity);
    }
    private void Choose(HintEntry entry) {
        _prefix = null; _selected = null; _focusedId = entry.Id;
        if (entry.IsGroup) {
            Current!.Page = _page; Current.FocusedId = entry.Id;
            _levels.Add(CreateLevel(entry.Children, entry.Compact));
            _page = 0; _focusedId = null; Render();
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
    private void Render(bool relayout = false) {
        if (relayout) {
            _capacity = Math.Clamp(_renderer?.GetPageCapacity(_bounds, _horizontal.Length * _vertical.Length) ??
                _horizontal.Length * _vertical.Length, 1, _horizontal.Length * _vertical.Length);
            _page = 0; _prefix = null; _selected = null; _focusedId = null;
            _levels.Clear();
            int singleCapacity = Math.Clamp(_renderer?.GetPageCapacity(_bounds, _horizontal.Length, singleKey: true) ??
                _horizontal.Length, 1, _horizontal.Length);
            _levels.Add(CreateLevel(new ElementHintHierarchy(_targets, _containers).Build(_targets,
                Math.Max(_capacity, singleCapacity)), compact: false));
            LogPageCapacity(_capacity);
        }
        _renderer?.GetPageCapacity(_bounds, Current?.SingleKey == true ? _horizontal.Length :
            _horizontal.Length * _vertical.Length, Current?.SingleKey == true);
        _renderer?.Render(new(Labels, Math.Max(1, Depth), _page, PageCount, _prefix,
            _focusedId, _selected?.Token, _status, Current?.SingleKey ?? false, _arrowKeys,
            Current?.Compact ?? false, _lifetime is not null && _outcome is null));
        StateChanged?.Invoke();
    }
    public void Suspend() => Deactivate();
    public void Deactivate() {
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
        _targets = []; _containers = []; _levels.Clear(); _prefix = null; _selected = null; _page = 0; _focusedId = null;
        Retirement = RetireAsync();
    }
    private async Task RetireAsync() {
        if (!await _service.RetireAsync()) { FailureReported?.Invoke("UIA helper cleanup failed; further scans disabled."); }
    }
    [LoggerMessage(Level = LogLevel.Debug, Message = "UIA discovery {Outcome}: retained={Retained}, visited={Visited}, omitted={Omitted}, reason={Reason}")]
    private partial void LogDiscovery(HintOutcome outcome, int retained, int visited, int omitted, string? reason);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Element hint pages recomputed for viewport: capacity={Capacity}")]
    private partial void LogPageCapacity(int capacity);
}
