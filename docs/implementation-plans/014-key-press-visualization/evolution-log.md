# Evolution Log — 014: Key Press Visualization

## Round 1

**Models**: Opus, Codex, Gemini

**Issues found (16)**:
1. [Critical] `ToUnicode` dead-key state corruption during live typing
2. [Critical] Sensitive data (passwords) displayed during screenshare
3. [Critical] Config type uses `record` — conflicts with established `sealed class` pattern
4. [High] HiDPI display scaling error in window positioning (physical→DIP conversion)
5. [High] WPF animation architecture mismatch — ViewModel Opacity vs DependencyProperty
6. [High] Click-through needs `WS_EX_NOACTIVATE` + `ShowActivated=False` + `Focusable=False`
7. [High] TextBlock does not support outline rendering — must use path-based geometry
8. [High] Missing testability seams for time, monitor, and key resolution
9. [Medium] Modifier state can become stale after missed key-up events
10. [Medium] Unnecessary duplicate interface `IKeyPressHookService`
11. [Medium] `MonitorFromWindow` P/Invoke doesn't exist — needs explicit addition
12. [Medium] Fade-out vs overflow eviction conflict
13. [Medium] Toggle activation failure handling incomplete
14. [Medium] REQ-1 acceptance too absolute for Windows constraints
15. [Low] Repeat detection 500ms window too aggressive for normal typing
16. [Low] No logging for hook lifecycle

**All issues fixed**:
1. → Cached VKey→label mapping at enable-time via `IKeyLabelResolver`. No `ToUnicode` during live processing.
2. → Added REQ-18 (never persist/log), REQ-19 (visible indicator), RISK-6 with mitigations. Documented as known limitation.
3. → Changed to `sealed class` with `{ get; init; }` properties.
4. → Specified `TransformFromDevice` conversion; `IMonitorService` returns DIP `Rect`.
5. → Replaced with `DispatcherTimer`-based opacity decrement on ViewModel (testable, no DependencyProperty needed).
6. → Added `WS_EX_NOACTIVATE`, `ShowActivated="False"`, `Focusable="False"`, `SetWindowLongPtr`.
7. → Specified path-based outlined text via `FormattedText.BuildGeometry()` matching existing pattern.
8. → Inject `IKeyLabelResolver`, `TimeProvider`, new `IMonitorService`. All testable via fakes.
9. → Added `GetAsyncKeyState` reconciliation on each non-modifier key-down + reset on enable/disable. Added RISK-7.
10. → Removed duplicate interface; reuse existing `IKeyboardHookService` with second instance.
11. → Explicitly listed `MonitorFromWindow` as new P/Invoke; created `IMonitorService` with helper.
12. → Defined: overflow eviction = immediate removal (no fade). Fade only on idle timeout. Updated REQ-3.
13. → Added activation transaction pattern with rollback. Added RISK-8.
14. → Qualified REQ-1: "within normal interactive desktop sessions (secure desktop excluded)".
15. → Reduced to 150ms; made configurable as `RepeatWindowMs`.
16. → Added `ILogger` injection to `KeyboardHookService` with enable/disable logging.

**Issues deferred**: None.
