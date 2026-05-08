using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

using Klikety.Config;
using Klikety.Overlay;

namespace Klikety.Services;

/// <summary>
/// Manages the key press display: processes key events into display items,
/// handles overflow eviction, repeat collapsing, window positioning, and fade lifecycle.
/// </summary>
public sealed class KeyPressDisplayManager : IDisposable {
    private readonly KeyPressVisualizationConfig _config;
    private readonly KeyPressProcessor _processor;
    private readonly KeyPressWindow _window;
    private readonly IMonitorService _monitorService;
    private readonly ObservableCollection<KeyPressDisplayItem> _items;
    private readonly DispatcherTimer _idleTimer;
    private readonly DispatcherTimer _fadeTimer;
    private KeyPressEntry? _lastEntry;
    private bool _isFading;

    public KeyPressDisplayManager(
        KeyPressVisualizationConfig config,
        KeyPressProcessor processor,
        KeyPressWindow window,
        IMonitorService monitorService) {
        _config = config;
        _processor = processor;
        _window = window;
        _monitorService = monitorService;
        _items = window.Items;

        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(config.FadeTimeoutMs) };
        _idleTimer.Tick += OnIdleTimerTick;

        _fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _fadeTimer.Tick += OnFadeTimerTick;
    }

    public void HandleKeyEvent(KeyHookEventArgs e) {
        if (e.IsDown) {
            var entry = _processor.ProcessKeyDown(e.Key);
            if (entry is null) return;

            // Cancel any active fade
            if (_isFading) {
                CancelFade();
            }

            // Check repeat
            if (_items.Count > 0 && _processor.IsRepeat(entry, _lastEntry, _config.RepeatWindowMs)) {
                _items[^1].RepeatCount++;
            } else {
                // Add new item, evict oldest if over limit
                if (_items.Count >= _config.MaxVisibleKeys) {
                    _items.RemoveAt(0);
                }
                _items.Add(new KeyPressDisplayItem(entry.Label));
            }

            _lastEntry = entry;
            UpdateWindowPosition();
            ResetIdleTimer();
        } else {
            _processor.ProcessKeyUp(e.Key);
        }
    }

    public void UpdateWindowPosition() {
        var workArea = _monitorService.GetActiveMonitorWorkArea();
        PositionWindow(workArea);
    }

    public void Dispose() {
        _idleTimer.Stop();
        _fadeTimer.Stop();
        _items.Clear();
    }

    /// <summary>
    /// Calculates window position for a given corner, work area, and window size.
    /// Exposed as internal static for unit testing.
    /// </summary>
    internal static (double Left, double Top) CalculatePosition(
        string corner, Rect workArea, double windowWidth, double windowHeight, double margin) {
        double left, top;
        switch (corner.ToUpperInvariant()) {
            case "TOPLEFT":
                left = workArea.Left + margin;
                top = workArea.Top + margin;
                break;
            case "TOPRIGHT":
                left = workArea.Right - windowWidth - margin;
                top = workArea.Top + margin;
                break;
            case "BOTTOMLEFT":
                left = workArea.Left + margin;
                top = workArea.Bottom - windowHeight - margin;
                break;
            default: // BottomRight
                left = workArea.Right - windowWidth - margin;
                top = workArea.Bottom - windowHeight - margin;
                break;
        }
        return (left, top);
    }

    private void PositionWindow(Rect workArea) {
        _window.UpdateLayout();
        var windowWidth = _window.ActualWidth > 0 ? _window.ActualWidth : 200;
        var windowHeight = _window.ActualHeight > 0 ? _window.ActualHeight : 100;

        var (left, top) = CalculatePosition(_config.Corner, workArea, windowWidth, windowHeight, _config.Margin);
        _window.Left = left;
        _window.Top = top;
    }

    private void ResetIdleTimer() {
        _idleTimer.Stop();
        _idleTimer.Start();
    }

    private void OnIdleTimerTick(object? sender, EventArgs e) {
        _idleTimer.Stop();
        if (_items.Count > 0) {
            _isFading = true;
            _fadeTimer.Start();
        }
    }

    private void OnFadeTimerTick(object? sender, EventArgs e) {
        double decrement = 1.0 / (_config.FadeDurationMs / 16.0);
        // Stagger: oldest fades first
        double staggerDelay = _config.FadeDurationMs / (double)_config.MaxVisibleKeys / 16.0;
        bool allDone = true;

        for (int i = 0; i < _items.Count; i++) {
            // Each item starts fading after i * staggerDelay ticks worth of decrements
            var item = _items[i];
            if (item.Opacity > 0) {
                item.Opacity = Math.Max(0, item.Opacity - decrement);
                if (item.Opacity > 0) allDone = false;
            }
        }

        // Remove fully faded items
        for (int i = _items.Count - 1; i >= 0; i--) {
            if (_items[i].Opacity <= 0) {
                _items.RemoveAt(i);
            }
        }

        if (_items.Count == 0 || allDone) {
            _fadeTimer.Stop();
            _isFading = false;
        }
    }

    private void CancelFade() {
        _fadeTimer.Stop();
        _isFading = false;
        foreach (var item in _items) {
            item.Opacity = 1.0;
        }
    }
}
