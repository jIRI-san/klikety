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
    int GetPageCapacity(Rectangle region, int keyCapacity);
    void Render(IReadOnlyList<HintTarget> targets, int page, int pageCount, int? prefix,
        int? selectedToken, string status);
    void FlashInvalidKey();
}

public sealed record ElementHintsHelpState(
    HintOutcome? Outcome, string Status, int Page, int PageCount, int TargetCount,
    int? Prefix, bool HasSelection);

public sealed partial class ElementHintsSession : IModeSession {
    private readonly VKey[] _horizontal, _vertical;
    private readonly ActionMapper _actions;
    private readonly IElementHintsRenderer? _renderer;
    private readonly IElementHintService _service;
    private readonly ILogger _logger;
    private CancellationTokenSource? _lifetime;
    private HintTarget[] _targets = [];
    private int _capacity, _page;
    private int? _prefix;
    private HintTarget? _selected;
    private string _status = "Finding controls...";
    private HintOutcome? _outcome;
    private Rectangle _bounds;

    public ElementHintsSession(VKey[] horizontal, VKey[] vertical, ActionMapper actions,
        ElementTargetContext context, IElementHintService service, IElementHintsRenderer? renderer, ILogger? logger = null) {
        _horizontal = horizontal; _vertical = vertical; _actions = actions;
        Context = context; _service = service; _renderer = renderer;
        _logger = logger ?? NullLogger.Instance;
        _capacity = horizontal.Length * vertical.Length;
    }
    public ElementTargetContext Context { get; }
    public int RootProcessId { get; private set; }
    public int Page => _page;
    public int PageCount => Math.Max(1, (_targets.Length + _capacity - 1) / _capacity);
    public int? Prefix => _prefix;
    public HintTarget? Selected => _selected;
    public string Status => _status;
    public ElementHintsHelpState HelpState =>
        new(_outcome, _status, _page, PageCount, _targets.Length, _prefix, _selected is not null);
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
            if (_prefix is not null || _selected is not null) { _prefix = null; _selected = null; Render(); } else { Cancelled?.Invoke(); }
            return;
        }
        if (key is VKey.Left or VKey.Right) {
            if (_targets.Length == 0) { _renderer?.FlashInvalidKey(); return; }
            _page = (_page + (key == VKey.Left ? PageCount - 1 : 1)) % PageCount;
            _prefix = null; _selected = null; Render(); return;
        }
        if (_actions.Map(key) is { } action) {
            if (_selected is null) { _renderer?.FlashInvalidKey(); return; }
            ActionRequested?.Invoke(new(_selected.Preview.X, _selected.Preview.Y), action); return;
        }
        int first = Array.IndexOf(_horizontal, key), second = Array.IndexOf(_vertical, key);
        if (_targets.Length > 0 && first >= 0) { _prefix = first; _selected = null; Render(); return; }
        if (_prefix is { } col && second >= 0) {
            int index = _page * _capacity + col * _vertical.Length + second;
            if (index < Math.Min(_targets.Length, (_page + 1) * _capacity)) {
                _selected = _targets[index]; _prefix = null; Render();
                CursorMoveRequested?.Invoke(new(_selected.Preview.X, _selected.Preview.Y)); return;
            }
        }
        _renderer?.FlashInvalidKey();
    }

    public Task<HintResponse> ValidateAsync(CancellationToken ct) =>
        _selected is { } target ? _service.ValidateAsync(target.Token, ct) :
        Task.FromResult(new HintResponse(ElementHintProtocol.Version, Guid.Empty, Guid.Empty, HintOutcome.StaleTarget, []));

    public void Redraw() => Render();
    public void Relayout() => Render(relayout: true);
    private void Render(bool relayout = false) {
        int capacity = Math.Max(1, _renderer?.GetPageCapacity(_bounds, _horizontal.Length * _vertical.Length) ?? _capacity);
        if (relayout && capacity != _capacity) {
            _capacity = capacity; _page = 0; _prefix = null; _selected = null;
            LogPageCapacity(_capacity);
        }
        _renderer?.Render(_targets.Skip(_page * _capacity).Take(_capacity).ToArray(),
            _page, PageCount, _prefix, _selected?.Token, _status);
        StateChanged?.Invoke();
    }
    public void Suspend() => Deactivate();
    public void Deactivate() {
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
        _targets = []; _prefix = null; _selected = null; _page = 0;
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
