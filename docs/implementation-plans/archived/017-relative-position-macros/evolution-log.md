# Evolution Log — 017 Relative Position Macros

## Round 1

**Reviewers**: Opus, Codex, Gemini (via `@dr`)

### Issues Found

| # | Severity | Summary | Resolution |
|---|----------|---------|------------|
| 1 | Critical | Recording resume: HWND not in context, bounds don't propagate to recorder, app-scope restoration underspecified | Fixed — HWND added to context; coordinator owns transform (pre-transforms before calling RecordAction); resume flow fully specified |
| 2 | Critical | Picker foreground capture race during WindowRelative playback | Fixed — pre-capture HWND before picker opens; global hotkey uses `_preOverlayHwnd` |
| 3 | Critical | Empty window title quarantines macro on reload | Fixed — block recording if title empty (reject at recording start with notification) |
| 4 | High | `AwaitStartFromCursorConfirm` state transitions undefined | Fixed — added Escape/unrelated key/focus loss transitions |
| 5 | High | `MacroRecordingContext` flow through recorder state machine unclear | Fixed — context stored on `StartRecording()`, threaded through `BeginRecording()` |
| 6 | High | Window closure during recording discards macro | Fixed — HWND invalid on resume → auto-stop + save (not cancel) |
| 7 | High | Mid-playback focus/bounds drift undetected | Fixed — re-verify foreground HWND + bounds before each action step |
| 8 | High | Tray notification path for playback failures missing | Deferred — existing pattern: coordinator reads `PlaybackResult` and calls tray methods via `App.xaml.cs` event. No new abstraction needed; document existing path. |
| 9 | Medium | `StartFromCursor` X/Y semantics and cursor source ambiguous | Fixed — X/Y store recorded position (for display/debugging); validation skips start coords when `StartFromCursor`; cursor captured at macro start for `StartFromCursor` steps |
| 10 | Medium | Drag recording path resets app-scope | Fixed — `ResetOverlayForDrag` during window-relative recording preserves app-scope bounds |
| 11 | Medium | Full window title fragile for dynamic apps | Fixed — extract last segment after " - " as default pattern (e.g., "Outlook" from "Inbox - Outlook") |
| 12 | Medium | DPI floating-point comparison risky; multi-monitor DPI | Fixed — use epsilon tolerance (0.01); multi-monitor DPI noted as known limitation |
| 13 | Medium | `PlaybackResultKind` new values + version-set logic | Fixed — enumerate all switch consumers; `MacroStore.Save()` sets version to 2 |
| 14 | Medium | Window-moved during recording shifts coordinate frame | Clarified — offsets are frame-independent (same window size = same offsets regardless of position). RISK-3 text reconciled. |
| 15 | Low | `macros.schema.json` embedded resource not updated | Fixed — step 4.3 includes embedded resource note |
