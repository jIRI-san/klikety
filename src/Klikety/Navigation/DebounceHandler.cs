using Klikety.Config;
using Klikety.Input;
using Klikety.Services;

namespace Klikety.Navigation;

/// <summary>
/// Manages hotkey debounce state: suppresses keys that were physically held
/// at activation time until they are released, with timer-based reconciliation.
/// </summary>
internal sealed class DebounceHandler : IDisposable {
    private static readonly TimeSpan DebounceTimeout = TimeSpan.FromMilliseconds(500);

    private readonly IPlatformServices _platform;
    private readonly HotKeyConfig _hotKey;
    private readonly HashSet<VKey> _keys = [];
    private IDebounceTimer? _timer;

    public DebounceHandler(IPlatformServices platform, ConfigModel config) {
        _platform = platform;
        _hotKey = config.HotKey;
    }

    /// <summary>
    /// Populates the debounce set with the trigger key (unconditionally)
    /// and any hotkey modifier keys that are currently physically held.
    /// </summary>
    public void PopulateFromHotKey() {
        _keys.Clear();
        _keys.Add(_hotKey.Key);

        var modifiers = _hotKey.Modifiers;

        if (modifiers.HasFlag(HotKeyModifiers.Alt)) {
            CheckAndAdd(VKey.LMenu);
            CheckAndAdd(VKey.RMenu);
            CheckAndAdd(VKey.Menu);
        }

        if (modifiers.HasFlag(HotKeyModifiers.Control)) {
            CheckAndAdd(VKey.LControl);
            CheckAndAdd(VKey.RControl);
        }

        if (modifiers.HasFlag(HotKeyModifiers.Shift)) {
            CheckAndAdd(VKey.LShift);
            CheckAndAdd(VKey.RShift);
        }

        if (modifiers.HasFlag(HotKeyModifiers.Win)) {
            CheckAndAdd(VKey.LWin);
            CheckAndAdd(VKey.RWin);
        }
    }

    public bool Contains(VKey key) => _keys.Contains(key);

    public void Remove(VKey key) => _keys.Remove(key);

    public void Clear() => _keys.Clear();

    /// <summary>
    /// Removes the trigger key from debounce if it is no longer physically held.
    /// Called on first keydown for a different key.
    /// </summary>
    public void RemoveTriggerIfReleased() {
        if (_keys.Contains(_hotKey.Key) && !_platform.KeyState.IsKeyDown(_hotKey.Key)) {
            _keys.Remove(_hotKey.Key);
        }
    }

    /// <summary>
    /// Starts the debounce reconciliation timer. Disposes any previous timer.
    /// </summary>
    public void StartTimer() {
        _timer?.Dispose();
        _timer = _platform.Timers.Create();
        _timer.Elapsed += OnTimerElapsed;
        _timer.Start(DebounceTimeout);
    }

    /// <summary>
    /// Stops and disposes the debounce timer and clears the key set.
    /// </summary>
    public void StopAndDispose() {
        _timer?.Stop();
        _timer?.Dispose();
        _timer = null;
        _keys.Clear();
    }

    public void Dispose() {
        _timer?.Dispose();
    }

    private void OnTimerElapsed() {
        var toRemove = new List<VKey>();
        foreach (var key in _keys) {
            if (!_platform.KeyState.IsKeyDown(key)) {
                toRemove.Add(key);
            }
        }

        foreach (var key in toRemove) {
            _keys.Remove(key);
        }

        _timer?.Stop();
    }

    private void CheckAndAdd(VKey key) {
        if (_platform.KeyState.IsKeyDown(key)) {
            _keys.Add(key);
        }
    }
}
