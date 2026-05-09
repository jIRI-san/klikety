# Evolution Log — 015: Macros

## Round 1

**Reviewers**: Opus, Codex, Gemini (via `@dr`)

### Issues Found

1. **[Critical] Hook lifecycle invariant broken** — Recording (overlay hidden for action) and playback (hook enabled without overlay) break "hook only active while overlay visible" invariant.
2. **[Critical] Drag button not captured** — `MacroStep` with `DragDrop` didn't record which button completed the drag.
3. **[High] Recording re-show via DeactivateOverlay** — Full teardown + `Task.Delay` for re-show breaks session, events, hook.
4. **[High] No DPI provider** — `IPlatformServices`/`IScreenBoundsProvider` had no DPI access; needed for recording + playback validation.
5. **[High] Key dispatch priority undefined** — New macro key-interception points had no formal priority in `OnKeyEvent` chain.
6. **[High] Scroll modifiers not supported** — `SendScroll` accepted no modifiers; Ctrl+Scroll (zoom) would lose Ctrl.
7. **[High] Config doc drift** — v3→v4 (scrollHotkeys) migration undocumented in config.design.md.
8. **[Medium] Focus loss cascades to cancel** — `Deactivated` event during recording suspend → `DeactivateOverlay()` cascade.
9. **[Medium] No recording/playback mutex** — Concurrent `MacroPlayer.Play()` or recording during playback undefined.
10. **[Medium] Macro UI abstractions needed** — Text input complexity for name prompt; picker focus/key routing unspecified.
11. **[Medium] No schema version on macros file** — No migration path if `MacroStep` schema changes.
12. **[Medium] Unknown-field round-trip needs mechanism** — Typed records drop unknown JSON properties.
13. **[Medium] Key collision matrix incomplete** — Missing: record key vs action bindings, helper vs chord, global vs main hotkey, slots vs scroll.
14. **[Medium] Cross-volume temp file move** — `Path.GetRandomFileName()` may land on different volume → `File.Move` throws.
15. **[Low] MacroActionType → service mapping table** — Missing explicit dispatch table for player.
16. **[Low] Speed modifier negative handling** — Consequence of negative value unspecified.
17. **[Low] Slot array length validation** — <10 or >10 elements unhandled; redundant `Slot` field.
18. **[Low] EnsureDefaults / FirstRunExtractor overlap** — Three code paths for same file creation.

### Issues Fixed (all applied)

All 18 issues addressed in plan revision:

- **#1**: Introduced hook override states with strict key filtering during recording; playback uses dedicated hotkey for Escape (no hook).
- **#2**: Added `MouseAction? DragButton` to `MacroStep`; recorder pairs drag phases before emitting step.
- **#3**: Replaced `DeactivateOverlay()` + `Task.Delay` with `SuspendOverlayForAction()` / `ResumeOverlayForRecording()` via `ITimerFactory`.
- **#4**: Added `GetDpiScale()` to `IScreenBoundsProvider` (step 1.7).
- **#5**: Defined full key dispatch priority chain: debounce → playback → recording → helper → chord → session.
- **#6**: Extended `SendScroll` with `ActionModifiers` parameter (step 1.8).
- **#7**: Added prerequisite step 1.9 to update config.design.md with v3→v4 docs.
- **#8**: Added `_recording` focus loss guard in coordinator.
- **#9**: Added `_macroState` enum mutex (Idle/Recording/Playing) guarding all entry points.
- **#10**: Simplified to auto-naming "Macro N" (no text input); picker specified as non-activating with key routing from hook; added interfaces for test faking.
- **#11**: Added `version` integer to `MacrosFile`.
- **#12**: Specified `[JsonExtensionData] Dictionary<string, JsonElement>?` on all persisted models.
- **#13**: Expanded collision matrix to cover all macro keys vs all existing key sets.
- **#14**: Changed to same-directory temp file (`Path.Combine(configDir, Path.GetRandomFileName())`).
- **#15**: Added explicit action mapping table in step 5.1.
- **#16**: Specified: negative → validation warning + clamp to 0.
- **#17**: Slot derived from array index; <10 padded, >10 truncated.
- **#18**: `FirstRunExtractor` is primary creation path; removed `EnsureDefaults()`.

### Issues Deferred

None.

## Round 2

**Reviewers**: Opus, Codex, Gemini (via `@dr`)

### Issues Found

1. **[Critical] Picker input channel** — Non-activating picker opened via global hotkey has no key input path (hook disabled, WPF focus not taken).
2. **[High] Playback Escape mechanism** — Bare `VK_ESCAPE` global hotkey intercepts system-wide; registration failure leaves no cancel path.
3. **[High] Session reset contradiction** — `SuspendOverlayForAction()` says "do NOT reset session" but `ResumeOverlayForRecording()` says "reset to L1" — no `IModeSession.Reset()` exists.
4. **[High] Playback async lifecycle** — `async void` exceptions crash process; `Dispose()` doesn't cancel playback; stale callbacks on disposed services.
5. **[High] No picker coordinator state** — `_macroState` lacks `Picking`; slot keys leak into navigation during picker.
6. **[High] Post-playback restoration undefined** — Global hotkey vs helper key paths have different restoration needs.
7. **[High] Macro file semantic validation** — Null-forgiving access (`step.DragButton!.Value`) crashes on malformed data.
8. **[High] Playback focus loss guard** — No guard for `Playing` state; simulated clicks trigger focus changes → `DeactivateOverlay()` cascade.
9. **[High] Task.Delay testability** — Direct `Task.Delay` in core loop makes tests slow and flaky.
10. **[Medium] Drag recording flow underspecified** — Coordinator divergence from normal drag-complete path; stale `_pendingDragStart`.
11. **[Medium] Non-activating overlay show** — `ResumeOverlayForRecording()` needs `ShowActivated=false` but overlay always activates.
12. **[Medium] Resume delay and cursor origin** — No delay duration specified; no origin point for session re-activation.
13. **[Medium] Multi-monitor limitations** — Screen validation doesn't cover arrangement changes or per-monitor DPI.
14. **[Low] DragButton accepts invalid values** — `MoveOnly`, `DragDrop`, `DoubleClick` silently fail on playback.
15. **[Low] Screen mismatch presentation** — "Error overlay" undefined; inconsistent with tray notification pattern.
16. **[Low] Slot truncation data loss** — >10 truncation + re-save permanently deletes overflow macros.

### Issues Fixed (all applied)

- **#1**: Picker changed to activating (takes WPF keyboard focus). Acceptable focus steal when it's the only Klikety window. Helper key path: suspend overlay → picker takes focus → resume on close.
- **#2**: Replaced bare Escape hotkey with hook-based strict filtering during playback (consistent with recording). Only Escape processed, all else passed through via `CallNextHookEx`.
- **#3**: Resolved contradiction: resume creates new default-mode session (deactivate old → create new → activate with current cursor as origin). Follows `ResetOverlayForDrag()` pattern. Removed "do NOT reset session" from suspend.
- **#4**: Added explicit `try/catch` for `OperationCanceledException` and general exceptions. `Dispose()` cancels `_playbackCts` and `MacroRecorder.Cancel()`. Added REQ-25.
- **#5**: Added `Picking` state to `_macroState` enum. Dispatch priority updated: `Picking` → only slot keys + Escape processed.
- **#6**: Added `_playbackEntryPath` tracking. Global hotkey → `DeactivateOverlay()` on complete. Helper key → resume overlay to L1. Added REQ-26.
- **#7**: Added semantic validation in `MacroStore.Load()` — per-step field validation, invalid slots quarantined. Added REQ-24.
- **#8**: Extended focus loss guard to cover `Playing` state: `_macroState != Idle` in `OnFocusLost`. Added RISK-9.
- **#9**: Introduced `IDelayProvider` abstraction with `FakeDelayProvider` for deterministic test timing. Added REQ-27.
- **#10**: Specified coordinator drag-complete divergence during recording. `_pendingDragStart` cleared on cancel and session reset.
- **#11**: Mooted by #3: resume creates new session + re-shows overlay normally (not `ShowActivated=false`). Session re-creation includes normal activation.
- **#12**: Specified 200ms delay via `ITimerFactory`. Keys during delay ignored. Cursor origin = current cursor position (where last action fired).
- **#13**: Documented as v1 limitation (consistent with main nav multi-monitor non-goal).
- **#14**: Added validation at recording time and load time: only `LeftClick`/`RightClick`/`MiddleClick` valid for `DragButton`.
- **#15**: Changed to tray notification (consistent with existing error surface).
- **#16**: Changed: >10 preserved for serialization round-trip, only first 10 bound to UI/hotkeys.

### Issues Deferred

None.

## Round 3

**Reviewers**: Opus, Codex, Gemini (via `@dr`)

### Issues Found

1. **[High] Dispatch chain missing record-key entry** — Record key (toggle recording on/off) not in dispatch priority chain as explicit step for `_macroState == Idle`.
2. **[High] Picker hook state** — Dispatch step 3 said "slot keys + Escape processed" but picker is activating with WPF KeyDown — hook should be disabled, not filtering.
3. **[High] Picker focus loss** — No `Deactivated` handler on picker → stuck `Picking` state on Alt+Tab or focus loss.
4. **[High] Picker SetForegroundWindow** — Global hotkey path: background process can't guarantee foreground activation without `SetForegroundWindow` P/Invoke after `Show()`.
5. **[High] Coordinator picker wiring** — Coordinator must explicitly disable hook and set `_macroState = Picking` before showing picker, not after.
6. **[High] Playback _disposed guard** — `OnPlaybackFinished()` callback can fire on disposed coordinator if `Dispose()` races with playback completion.
7. **[High] Playback Dispose sequence** — `Dispose()` must wait for `_playbackTask` to drain (cancel CTS → `Wait()` → teardown) to prevent stale callbacks.
8. **[High] Post-playback helper-key resume** — Resume path referenced but implementation pattern not specified (reuse `ResumeOverlayForRecording()`: new session → re-enable hook → activate).
9. **[High] Playback hook state** — Hook was "disabled for picker" — needs explicit re-enable for playback (strict filtering: only Escape).
10. **[Medium] Overwrite prompt keys** — Y/N keys for overwrite confirmation not explicitly listed in dispatch step 4 (recording control keys).
11. **[Medium] Speed modifier delay floor** — Very small non-zero speed modifiers (e.g. 0.01) produce sub-millisecond delays. Need 50ms global floor for non-zero modifiers.
12. **[Medium] Integer overflow in delay calc** — `relativeTimeMs * speedModifier` can overflow int range. Need explicit protection.
13. **[Medium] StopRecording with pending drag** — `_pendingDragStart != null` when recording stops → behavior unspecified (discard + warning).
14. **[Medium] MacroStore.Save() failure** — Save failure path: retain in-memory macro, tray notification.
15. **[Medium] Post-recording overlay state** — Unclear whether overlay remains open after recording stops (it should — user continues navigating).
16. **[Low] Config validation edge cases** — Intra-macro key uniqueness (RecordKey == HelperKey), SlotKeys length != 10, null GlobalHotKey not covered.
17. **[Low] Missing test cases** — Focus loss during picker, Dispose during playback task drain, 50ms delay floor, StopRecording with pending drag.

### Issues Fixed (all applied)

- **#1**: Added record key as dispatch priority step 5 (`_macroState == Idle` and overlay open → `StartRecording()`).
- **#2**: Updated dispatch step 3 (Picking): N/A — hook disabled, picker owns all key input via WPF KeyDown.
- **#3**: Picker wires `Deactivated` → self-dismiss + fire `PickerClosed`.
- **#4**: Added `SetForegroundWindow` P/Invoke after `Show()` for global-hotkey path.
- **#5**: Coordinator: set `_macroState = Picking`, disable hook, then show picker.
- **#6**: Added `_disposed` guard in `OnPlaybackFinished()` — skip callback if disposed.
- **#7**: `Dispose()` sequence: cancel CTS → `_playbackTask.Wait()` → proceed with teardown. `_disposed = true` set before service disposal.
- **#8**: `ResumeOverlayAfterPlayback()` reuses `ResumeOverlayForRecording()` pattern: new session → re-enable hook in normal mode → activate with current cursor as origin → start debounce timer.
- **#9**: Hook re-enabled after picker closes for playback (strict filter: only Escape processed).
- **#10**: Y/N keys added to recording control keys in dispatch step 4.
- **#11**: 50ms global minimum floor: `delay = Math.Max(50, ...)` for non-zero speed modifiers. `speedModifier == 0` uses 100ms fixed.
- **#12**: Delay calculation: `Math.Min((long)relativeTimeMs * speedModifier, int.MaxValue)` — cast to long before multiply, clamp to int range.
- **#13**: `StopRecording()` with `_pendingDragStart != null` → discard pending drag + log warning + save remaining steps. Test added to 3.4.
- **#14**: `MacroStore.Save()` returns success/failure. Caller shows tray notification on failure, retains in-memory `MacroDefinition`.
- **#15**: Post-recording: overlay remains open with normal session (user continues navigating).
- **#16**: Config validation: intra-macro uniqueness (RecordKey != HelperKey, no duplicate SlotKeys), SlotKeys length exactly 10 (pad/truncate), null `GlobalHotKey` → feature disabled (no registration).
- **#17**: Added: focus loss during picker test (4.4), Dispose during playback task drain + `_disposed` guard (5.5), 50ms delay floor test (5.4), StopRecording with pending drag (3.4).

### Issues Deferred

None.
