# 015: Macros — Replayable Mouse Action Recordings

## Decisions

- **Action replay, not key replay**: macros record resolved mouse actions (x, y, actionType, modifiers, timing) rather than overlay keystrokes. Simpler, predictable, no state-machine re-entry complexity. Key replay deferred to potential future enhancement.
- **10 fixed slots**: macros occupy slots 0–9 with configurable invocation keys. One file (`macros.json`) holds all 10. Simpler than dynamic list management. Slot derived from array index (no redundant `Slot` field on model). Array validation: <10 → pad with nulls; >10 → preserve full array for serialization round-trip but only bind first 10 to UI/hotkeys.
- **Screen-size-locked**: macros store `screenWidth`, `screenHeight`, `dpiScale` at recording time. Playback only when these match. Mismatch → tray notification with details + abort (consistent with existing error surface). Multi-monitor arrangement changes are a known v1 limitation (same as main nav).
- **Speed modifier**: decimal config value. 1.0 = original timing, 0.5 = 2× speed, 2.0 = ½ speed, 0 = minimum delay (100ms between steps). Applied as multiplier to recorded `relativeTimeMs`. Negative values → validation warning + clamp to 0. Global minimum delay floor: 50ms after multiplication for all speed values (not just zero) — prevents OS input queue coalescing.
- **Suspend/resume with session re-creation**: during recording, after an action fires, overlay uses `SuspendOverlayForAction()` / `ResumeOverlayForRecording()` (analogous to `ResetOverlayForDrag()`). Suspend hides the overlay **without** full coordinator teardown. Resume creates a new default-mode session (deactivate old → create new → activate with current cursor position as origin), re-shows overlay normally. This avoids adding `IModeSession.Reset()` and follows the existing `ResetOverlayForDrag()` pattern.
- **Hook override states**: recording and playback both use the low-level hook with strict key filtering when the overlay is not visible. During recording gaps, only record-control key + Escape processed; all else passed through via `CallNextHookEx`. During playback, only Escape processed; all else passed through. This is consistent and avoids system-wide bare-key hotkey registration problems.
- **Picker is activating**: `MacroPickerOverlay` takes WPF keyboard focus and handles `KeyDown` events directly. Hook is disabled while picker is active (picker owns all key input; avoids dual-dispatch). When opened via global hotkey (no overlay visible), it's the only Klikety window — focus steal acceptable; use `SetForegroundWindow` P/Invoke after `Show()` to ensure foreground activation from background. When opened via helper key mid-navigation, main overlay suspends and picker takes focus. Picker wires `Deactivated` → self-dismiss + fire `PickerClosed` to prevent stuck `Picking` state on Alt+Tab/focus loss. On picker close, main overlay resumes if it was active.
- **Macro actions bypass config**: during playback, actions execute directly via `IMouseActionService` regardless of current config (e.g. scroll works even if scroll hotkeys disabled).
- **Navigation keys take precedence**: if macro slot keys collide with navigation keys, navigation wins. Warning shown at startup.
- **Config version bump**: `configVersion` 4 → 5 on migration to add `macros` config section.
- **Drag button captured**: drag recording pairs drag-start (DragDrop action) and drag-complete (button action) into a single `MacroStep` with explicit `DragButton` field. Recorder holds partial drag state until second action resolves the button. `_pendingDragStart` cleared on cancel/session reset. Valid drag buttons: `LeftClick`, `RightClick`, `MiddleClick` only — validated at recording and load time.
- **Coordinator macro state mutex**: `_macroState` enum (`Idle`, `Recording`, `Playing`, `Picking`) enforces mutual exclusion. Playing → ignore new triggers. Recording → block playback. Picking → only picker keys (slot + Escape) processed. Prevents concurrent operations and key leakage into navigation.
- **Key dispatch priority**: full chain: (1) debounce → (2) if `_macroState == Playing` → only Escape via hook strict filtering, all else passed through → (3) if `_macroState == Picking` → N/A (hook disabled; picker handles KeyDown directly) → (4) if `_macroState == Recording` → recording control (record toggle, Escape, slot/overwrite Y/N prompts) → (5) record key when `_macroState == Idle` and overlay open → `StartRecording()` → (6) helper key when `_macroState == Idle` and overlay open → (7) chord dispatch (existing, if `!_modeLocked`) → (8) session forwarding (existing).
- **Focus loss guard during recording and playback**: `_macroState != Idle` guard in `OnFocusLost` suppresses `DeactivateOverlay()` during recording and playback (same pattern as `_deactivating` reentrancy guard).
- **Auto-naming**: after recording stops, macro auto-named "Macro N" (N = slot number). User edits name in `macros.json` if desired. Eliminates text-input overlay complexity.
- **Macros file versioned**: `MacrosFile` has a `version` integer (v1 initial). Follows established config migration pattern.
- **Unknown-field preservation**: all persisted macro models use `[JsonExtensionData] Dictionary<string, JsonElement>?` for forward-compatible round-trip.
- **Scroll modifiers**: `IMouseActionService.SendScroll` extended to accept `ActionModifiers` parameter; brackets wheel event with modifier KEYDOWN/KEYUP (matching `SendAction`/`SendDrag` pattern). Enables Ctrl+Scroll (zoom) recording.
- **DPI provider**: `IScreenBoundsProvider` extended with `double GetDpiScale()` backed by `GetDpiForMonitor` or `PresentationSource.CompositionTarget.TransformToDevice.M11`. Corresponding fake for testing.
- **FirstRunExtractor is primary creation path**: `FirstRunExtractor` creates `macros.json` on first run (skip-if-exists). `MacroStore.Load()` handles "file exists but corrupt" recovery. No separate `EnsureDefaults()` method.
- **Post-playback restoration**: global hotkey path → full `DeactivateOverlay()` on playback complete (no overlay was active). Helper key path → resume overlay to L1 (same as recording resume: new session, current cursor as origin). Both paths → `_macroState = Idle`.
- **Playback async lifecycle**: `MacroPlayer.Play()` returns `Task<PlaybackResult>` stored as `_playbackTask`. `async void` wrapper: `try { result = await _player.Play(...); } catch (OperationCanceledException) { /* normal cancel */ } catch (Exception ex) { _logger.LogError(...); } finally { if (!_disposed) OnPlaybackFinished(result); }`. `Dispose()`: cancel `_playbackCts`, `_playbackTask.Wait()` (drain), then proceed with existing teardown. `_disposed` guard in `OnPlaybackFinished()` prevents stale callbacks.
- **Delay abstraction**: `IDelayProvider` interface for testable timing. Tests inject `FakeDelayProvider` advancing virtual time. Production uses `Task.Delay`.
- **Macro file semantic validation**: `MacroStore.Load()` validates per-step semantics: non-null `DragButton` for `DragDrop`, non-null `ScrollDelta` for `Scroll`, non-null `EndX`/`EndY` for `DragDrop`, non-negative `RelativeTimeMs` (max 600000ms = 10min), valid `DragButton` values (`LeftClick`/`RightClick`/`MiddleClick` only), coordinate bounds (`0 ≤ X < ScreenWidth`, `0 ≤ Y < ScreenHeight`, same for `EndX`/`EndY`). Invalid slots quarantined with error message per slot.
- **Resume delay**: `SuspendOverlayForAction()` → action dispatch → 200ms delay via `ITimerFactory` (existing testable abstraction) → `ResumeOverlayForRecording()`. During delay, hook in strict filtering mode (record-control + Escape only). Keys pressed during delay are ignored (not queued).

## Requirements

| ID | Requirement | Acceptance Criteria | Phases/Steps |
|----|-------------|---------------------|--------------|
| REQ-1 | Macro data model with 10 slots, each storing name, screen dimensions, DPI scale, ordered steps, and file version | Model deserializes from JSON; empty slots represented as null; round-trip preserves unknown fields via `JsonExtensionData`; array <10 padded; >10 preserved but only first 10 bound | 1.1, 1.2 |
| REQ-2 | Each macro step records actionType, x, y, modifiers, relativeTimeMs; drag steps add endX/endY + dragButton; scroll steps add scrollDelta | All step types serialize/deserialize correctly; timestamps relative to previous step; drag button captured and validated (LeftClick/RightClick/MiddleClick only) | 1.1, 1.2 |
| REQ-3 | macros.json persistence in %APPDATA%\Klikety\ with atomic write (temp in same directory + rename) | File created on first run via FirstRunExtractor; writes are atomic (same-directory temp); corrupt file → error notification + empty state; per-step semantic validation on load | 1.3, 2.2 |
| REQ-4 | Config additions: macros section with enabled flag, global hotkey, record key, helper key, slot keys array, speed modifier | Config loads with defaults; validates key collisions; migrates from v4→v5 | 1.4, 1.5 |
| REQ-5 | Recording: enter record mode via record key while overlay open → select slot → red border feedback → capture actions → stop via record key | Recording captures correct action sequence with timing; overlay shows red border; Escape cancels recording | 3.1, 3.2, 3.3 |
| REQ-6 | Recording: overlay suspends for action execution, then resumes at full grid for next action in sequence | Each action fires at correct physical position; overlay resumes with new session using current cursor position as origin; no full coordinator teardown between actions; 200ms delay before resume | 3.2 |
| REQ-7 | Recording: slot selection prompt with overwrite confirmation for occupied slots | Occupied slot shows "Slot used: \<name\>, overwrite? y/n"; confirmed → clear slot; denied → abort recording | 3.1 |
| REQ-8 | Recording: auto-naming as "Macro N" after recording stops | Macro auto-named; user edits in macros.json if desired | 3.3 |
| REQ-9 | Macro picker overlay: centered activating window listing 10 slots with names/\<empty\> | Picker takes keyboard focus; shows all 10 slots; selecting empty slot → no-op; selecting occupied slot → starts playback; Escape closes | 4.1 |
| REQ-10 | Global hotkey trigger (default Ctrl+Alt+Shift+M) opens macro picker | Hotkey registered at startup (if enabled); conflict → tray notification; picker appears centered | 4.2 |
| REQ-11 | Overlay helper key trigger (default backtick) opens macro picker mid-navigation | Helper key recognized in overlay; picker appears; slot selection starts playback; navigation resets to L1 on playback complete (not preserved) | 4.3 |
| REQ-12 | Playback: execute recorded steps with timing based on speed modifier | Steps fire at correct positions; timing scaled by speed modifier; 0 speed → 100ms minimum delay | 5.1, 5.2 |
| REQ-13 | Playback: screen size + DPI validation before execution | Mismatch → tray notification with expected vs actual values; macro does not execute | 5.1 |
| REQ-14 | Playback: visual overlay showing "Klikety macro: \<name\>" with progress bar | Overlay visible during playback; progress updates per step; disappears when done; non-activating (no focus steal) | 5.3 |
| REQ-15 | Playback: Escape cancels mid-playback via hook strict filtering | Escape stops execution immediately; remaining steps skipped; overlay dismissed; hook passes all non-Escape keys through | 5.2 |
| REQ-16 | Playback: actions execute directly via IMouseActionService regardless of current config | Scroll, drag, click all work even if corresponding config features are disabled | 5.1 |
| REQ-17 | Key collision validation: full matrix — all macro keys vs all existing key sets | Collisions detected at startup; warning notification lists conflicting keys; navigation keys not overridden; covers record/helper/slot/global vs nav/action/chord/scroll/reserved | 1.5, 4.3 |
| REQ-18 | Modifier keys (Shift, Ctrl, Alt) recorded per action step including scroll | Modifiers captured at action dispatch time; replayed during playback; scroll steps bracket wheel with modifier KEYDOWN/KEYUP | 3.2, 5.1 |
| REQ-19 | Cancel recording via Escape at any point | Escape during slot selection, recording, or name prompt → discard partial recording; overlay returns to normal state | 3.1, 3.2, 3.3 |
| REQ-20 | Hook override: hook stays enabled during recording gaps and playback with strict key filtering | During overlay suspend, only record-control key + Escape processed; during playback, only Escape processed; all other keys passed through via CallNextHookEx | 3.2, 5.2 |
| REQ-21 | Coordinator macro state mutex prevents concurrent recording/playback/picking | Playing → new triggers ignored; Recording → playback blocked; Picking → only picker keys processed; state transitions atomic | 3.2, 4.3, 5.2 |
| REQ-22 | DPI scale provider added to platform services | `IScreenBoundsProvider.GetDpiScale()` returns current DPI; fake available for tests | 1.7 |
| REQ-23 | SendScroll extended with ActionModifiers parameter | `SendScroll(int wheelDelta, ActionModifiers modifiers)` brackets wheel event with modifier keys | 1.8 |
| REQ-24 | Macro file semantic validation on load | Per-step validation: required fields non-null per action type; valid DragButton values; non-negative timing; invalid slots quarantined with error | 1.3 |
| REQ-25 | Playback async lifecycle: exception handling and disposal | `OperationCanceledException` handled as normal cancel; general exceptions logged + cleaned up; `Dispose()` cancels playback and recording | 5.2 |
| REQ-26 | Post-playback restoration per entry path | Global hotkey path → `DeactivateOverlay()`; helper key path → resume overlay to L1 with new session | 5.2 |
| REQ-27 | Delay abstraction for testable timing | `IDelayProvider` with `FakeDelayProvider` for deterministic test timing | 5.1 |

## Risks

| ID | Risk | Likelihood | Impact | Mitigation | Steps |
|----|------|------------|--------|------------|-------|
| RISK-1 | Timing precision: WPF DispatcherTimer may not provide sub-10ms accuracy for fast macro playback | Medium | Low | Use 100ms minimum delay; `Task.Delay` with cancellation token; document that macros are not pixel-perfect timing tools | 5.1, 5.2 |
| RISK-2 | Overlay re-show during recording may steal focus from target application | Medium | Medium | Use `SuspendOverlayForAction()`/`ResumeOverlayForRecording()` with `ShowActivated=false` semantics; `_recording` guard suppresses `OnFocusLost` → `DeactivateOverlay()` cascade | 3.2 |
| RISK-3 | Global macro hotkey conflicts with other applications | Low | Medium | Registration failure → tray notification; user can disable in config | 4.2 |
| RISK-4 | macros.json corruption or concurrent access | Low | Medium | Atomic write (same-directory temp file + rename); load error → tray notification + treat as empty; no file locking (single-process app) | 1.3 |
| RISK-5 | Recording state machine complexity interacting with existing mode session state | Medium | High | Recording is a coordinator-level concern with `_macroState` mutex; session fires actions normally; coordinator intercepts `ActionRequested` to record + suspend/resume overlay | 3.1, 3.2 |
| RISK-6 | Key collision surface area grows (record key, helper key, slot keys, global hotkey vs existing nav/action/chord/scroll keys) | Medium | Medium | Comprehensive collision matrix in `StartupValidator` covering all macro keys vs all existing key sets; clear error messages | 1.5 |
| RISK-7 | Hook lifecycle invariant violated during recording gaps and playback | Medium | High | Hook stays enabled with strict key filtering in both cases; consistent approach: only specific keys processed, all else passed through via `CallNextHookEx` | 3.2, 5.2 |
| RISK-8 | Key dispatch priority ambiguity with new macro states (recording, playing, picking) | Medium | High | Define complete priority chain with `Picking` state added; test priority matrix across all coordinator states × key types | 3.2, 4.3, 5.2 |
| RISK-9 | Playback focus loss: simulated mouse clicks trigger window changes → `DeactivateOverlay()` cascade on disposed objects | Medium | High | `_macroState != Idle` guard in `OnFocusLost` covers both recording and playback | 5.2 |
| RISK-10 | `async void` playback exceptions crash process | Medium | High | Explicit `try/catch` for `OperationCanceledException` and general exceptions; `Dispose()` cancels CTS | 5.2 |

## Phase 1: Data Model, Config & Platform Extensions
<!-- worktree: feature/015-macros-data-model-config-step-1-1 -->

- [x] 1.1 Define `MacroStep` record and `MacroDefinition` model in `Config/` (REQ-1, REQ-2, REQ-18) `S`
  - `MacroStep`: `MacroActionType ActionType`, `int X`, `int Y`, `ActionModifiers Modifiers`, `int RelativeTimeMs`, `int? EndX`, `int? EndY`, `int? ScrollDelta`, `MouseAction? DragButton`
  - `MacroActionType` enum: `LeftClick`, `RightClick`, `MiddleClick`, `DoubleClick`, `MoveOnly`, `DragDrop`, `Scroll` (mirrors `MouseAction` + `Scroll`)
  - `MacroDefinition`: `string Name`, `int ScreenWidth`, `int ScreenHeight`, `double DpiScale`, `List<MacroStep> Steps`; `[JsonExtensionData] Dictionary<string, JsonElement>?` for unknown-field preservation
  - `MacrosFile`: `int Version` (initial = 1), `MacroDefinition?[] Macros` (10 slots, null = empty); `[JsonExtensionData]` on each model
  - Slot derived from array index — no `Slot` property on `MacroDefinition`
  - Load validation: array <10 → pad with nulls; >10 → preserve full array for round-trip, only first 10 bound to UI/hotkeys

- [x] 1.2 Unit tests for macro model serialization round-trip (REQ-1, REQ-2) `S`
  - Serialize → deserialize all step types (click, drag with `DragButton`, scroll, with modifiers)
  - Empty slots (null) preserved
  - Unknown JSON fields preserved at each nesting level (`MacrosFile`, `MacroDefinition`, `MacroStep`)
  - Array padding (<10) and >10 preservation verified
  - Invalid `DragButton` values (MoveOnly, DragDrop, DoubleClick) flagged by validation

- [x] 1.3 `MacroStore` — load/save `macros.json` with atomic write and semantic validation (REQ-3, REQ-24, RISK-4) `M`
  - Path: `%APPDATA%\Klikety\macros.json`
  - Load: `JsonSerializer.Deserialize<MacrosFile>` with `JsonCommentHandling.Skip`; parse error → return empty + error string
  - **Semantic validation per slot**: non-null `DragButton` for `DragDrop` steps (valid values: `LeftClick`/`RightClick`/`MiddleClick`); non-null `ScrollDelta` for `Scroll` steps; non-null `EndX`/`EndY` for `DragDrop`; non-negative `RelativeTimeMs`. Invalid slots → quarantined (set to null) with per-slot error message.
  - Save: write to same-directory temp file (`Path.Combine(configDir, Path.GetRandomFileName())`) → `File.Move(overwrite: true)` to target (same-volume atomic rename)
  - **Save failure**: `Save()` returns success/failure. Caller (coordinator) shows tray notification on failure. In-memory `MacroDefinition` retained (slot not emptied on save failure).
  - No `EnsureDefaults()` — `FirstRunExtractor` is primary creation path
  - Interface: `IMacroStore` for test faking

- [x] 1.4 Add `MacrosConfig` to `ConfigModel`; config migration v4→v5 (REQ-4) `M`
  - `MacrosConfig`: `bool Enabled` (default `true`), `HotKeyConfig? GlobalHotKey` (default Ctrl+Alt+Shift+M), `VKey RecordKey` (default `VKey.Oem5` = backslash), `VKey HelperKey` (default `VKey.Oem3` = backtick), `VKey[] SlotKeys` (default `[VKey.D0..VKey.D9]`), `double SpeedModifier` (default `1.0`)
  - `ConfigModel.Macros` property
  - `ConfigMigrator`: v4→v5 adds `macros` section with defaults if missing; preserves existing user values

- [x] 1.5 Config validation for macro keys — full collision matrix (REQ-17, RISK-6) [after: 1.4] `M`
  - **Macro keys to validate**: `RecordKey`, `HelperKey`, `SlotKeys[10]`, `GlobalHotKey`
  - **Existing key sets to check against**: reserved keys (Escape, arrows, VK_RETURN, hotkey modifiers), `actionBindings`, `horizontalKeys`, `verticalKeys`, chord keys, scroll hotkeys
  - **Intra-macro uniqueness**: `RecordKey ≠ HelperKey`; `SlotKeys` has no duplicates; `GlobalHotKey` base key checked against `RecordKey`/`HelperKey`/`SlotKeys`
  - Complete collision matrix: each macro key vs each existing key set
  - `SpeedModifier` ≥ 0; negative → validation warning + clamp to 0 at load time
  - `SlotKeys.Length` validation: <10 → pad with defaults; >10 → truncate to 10
  - `GlobalHotKey` = null → skip registration (intentional config option, no error)
  - `GlobalHotKey` registration probe (same pattern as main hotkey)
  - All violations collected and returned as validation warnings

- [ ] 1.6 Unit tests for config migration and validation (REQ-4, REQ-17) [after: 1.4, 1.5] `S`
  - Migration from v4 config (no `macros` section) → v5 with defaults
  - Already-v5 config → no mutations
  - Key collision detection: record key vs action bindings, helper key vs chord keys, slot keys vs nav keys, global hotkey vs main hotkey
  - Intra-macro uniqueness: RecordKey ≠ HelperKey, no duplicate SlotKeys
  - SpeedModifier boundary tests (0, negative → clamped, large values)
  - SlotKeys array length validation (<10 padded, >10 truncated)
  - Null GlobalHotKey → skip registration

- [ ] 1.7 Add `GetDpiScale()` to `IScreenBoundsProvider` (REQ-22) `S`
  - Implementation: `GetDpiForMonitor` (Win32) or `PresentationSource.CompositionTarget.TransformToDevice.M11`
  - `FakeScreenBoundsProvider`: add `DpiScale` property (default 1.0)
  - Unit test: fake returns configured value

- [ ] 1.8 Extend `IMouseActionService.SendScroll` with `ActionModifiers` parameter (REQ-23) `S`
  - Signature: `SendScroll(int wheelDelta, ActionModifiers modifiers = ActionModifiers.None)`
  - When `modifiers != None`: bracket wheel event with modifier KEYDOWN/KEYUP in single `SendInput` call (matching `SendAction`/`SendDrag` pattern)
  - Update existing callers (`ScrollHotKeyService`) to pass `ActionModifiers.None`
  - `FakeMouseActionService`: record modifiers in scroll call list
  - Unit test: verify modifier bracketing

- [ ] 1.9 Update `config.design.md`: document v3→v4 scroll migration (prerequisite) [after: 1.4] `S`
  - Fix documentation drift: v3→v4 (scrollHotkeys) is undocumented; update before adding v4→v5

## Phase 2: Macro Store Integration & First-Run
<!-- worktree: -->

- [ ] 2.1 Wire `MacroStore` into `App.xaml.cs` startup (REQ-3) [after: 1.3, 1.4] `S`
  - Load macros via `MacroStore.Load()` during startup (after config load, after FirstRunExtractor)
  - Store instance for coordinator access
  - Load errors → tray notification

- [ ] 2.2 Add `macros.json` to `FirstRunExtractor` as skip-if-exists resource (REQ-3) [after: 1.1] `S`
  - Embedded resource: empty macros file (version 1, 10 null slots)
  - `FirstRunExtractor` skip-if-exists category (like `config.json`)
  - Unit test: extraction creates file; subsequent run skips

- [ ] 2.3 Unit tests for MacroStore load/save (REQ-3, RISK-4) [after: 1.3] `M`
  - Load valid file → correct model
  - Load corrupt file → empty state + error string
  - Load missing file → empty state
  - Save → atomic write (verify temp file in same directory, then moved)
  - Round-trip: save then load → identical model

## Phase 3: Recording
<!-- worktree: -->

- [ ] 3.1 `MacroRecorder` state machine: Idle → SlotSelection → Recording → Complete (REQ-5, REQ-7, REQ-8, REQ-19, RISK-5) [after: 1.1, 1.4, 1.7] `L`
  - States: `Idle`, `AwaitSlot`, `AwaitOverwrite`, `Recording`
  - `StartRecording()`: transition Idle → AwaitSlot
  - `OnSlotKey(VKey)`: map to slot index; if occupied → AwaitOverwrite; if empty → Recording
  - `OnOverwriteResponse(bool)`: confirmed → Recording; denied → Idle
  - `OnActionRecorded(MacroStep)`: append step to current recording
  - `StopRecording()`: transition Recording → Idle; if `_pendingDragStart != null` → discard partial drag step + log warning; auto-name as "Macro N"; fire `RecordingComplete(MacroDefinition)` with completed steps only
  - `Cancel()`: any state → Idle; fire `RecordingCancelled`
  - Captures `screenWidth`, `screenHeight`, `dpiScale` (via `IScreenBoundsProvider.GetDpiScale()`) at recording start
  - Timing: each step records `relativeTimeMs` since previous step (first step = 0)
  - **Drag pairing**: holds partial drag state (`_pendingDragStart`) when `DragDrop` action recorded. Next action key resolves button → emit single `MacroStep` with `DragButton` set (validated: only `LeftClick`/`RightClick`/`MiddleClick`), `EndX`/`EndY` populated. Cancel during pending drag → clear `_pendingDragStart`. Session `Cancelled` event → clear `_pendingDragStart`.
  - Interface: testable via direct method calls, no UI dependency

- [ ] 3.2 Coordinator integration: intercept actions during recording (REQ-5, REQ-6, REQ-18, REQ-20, REQ-21, RISK-2, RISK-5, RISK-7, RISK-8, RISK-9) [after: 3.1] `L`
  - **Macro state mutex**: `_macroState` enum (`Idle`, `Recording`, `Playing`, `Picking`) field on coordinator. Guards all macro entry points.
  - **Key dispatch priority** (full chain in `OnKeyEvent`):
    1. Debounce check (existing)
    2. If `_macroState == Playing` → only Escape processed (cancel playback), all else passed through via `CallNextHookEx`
    3. If `_macroState == Picking` → N/A (hook disabled; picker handles KeyDown directly)
    4. If `_macroState == Recording` → recording control keys: record key (toggle stop), Escape (cancel), slot/overwrite prompt keys (Y/N)
    5. Record key when `_macroState == Idle` and overlay open → `StartRecording()`, set `_macroState = Recording`
    6. Helper key (if overlay open and `_macroState == Idle`)
    7. Chord dispatch (existing, if `!_modeLocked`)
    8. Session forwarding (existing)
  - When `_macroState == Recording`:
    - On `ActionRequested` from session: record step (actionType, point, modifiers via `IModifierDetector`, timing) → `SuspendOverlayForAction()` → let action fire → 200ms delay via `ITimerFactory` → `ResumeOverlayForRecording()`
    - `SuspendOverlayForAction()`: hide overlay, keep hook enabled with strict filtering (record-control + Escape processed, all else passed through via `CallNextHookEx`). Keys pressed during 200ms delay are ignored (not queued).
    - `ResumeOverlayForRecording()`: deactivate old session → create new default-mode session via `ModeSessionFactory` → activate with **current cursor position** as origin (position where last action fired) → re-show overlay normally → restore red border.
    - **Drag recording divergence**: drag-start (DragDrop action) calls `ResetOverlayForDrag()` as normal. Drag-complete calls `SuspendOverlayForAction()` → `SendDrag()` → delay → `ResumeOverlayForRecording()` instead of `DeactivateOverlay()`.
    - **Focus loss guard**: `_macroState != Idle` guard in `OnFocusLost` handler suppresses `DeactivateOverlay()` cascade during recording AND playback.
  - Record key press: dispatch priority step 5 — if `_macroState == Idle` and overlay open → `StartRecording()`, set `_macroState = Recording`; if `_macroState == Recording` (dispatch priority step 4) → `StopRecording()`, set `_macroState = Idle`
  - **Post-recording overlay**: overlay remains open with normal session (user can continue navigating)
  - Escape during recording → `Cancel()` → clear `_pendingDragStart` → clear red border → `_macroState = Idle` → normal overlay state

- [ ] 3.3 Recording UI: slot selection prompt, overwrite confirm, red border (REQ-7, REQ-8, REQ-19) [after: 3.1, 3.2] `M` [discovery]
  - Slot selection: overlay text "Select slot (0-9):" — intercept next key press
  - Overwrite confirmation: overlay text "Slot N: \<name\>. Overwrite? (Y/N)" — Y/N keys added to recording control keys in dispatch step 4
  - Auto-naming on stop: "Macro N" (no text input UI needed)
  - All prompts rendered on the overlay canvas (reuse overlay infrastructure, no new windows)
  - Visual feedback: 3px red border on overlay during active recording

- [ ] 3.4 Unit tests for MacroRecorder state machine (REQ-5, REQ-7, REQ-8, REQ-19) [after: 3.1] `M`
  - Full state transition coverage: Idle→AwaitSlot→Recording→complete
  - Cancel from each state
  - Overwrite flow: occupied slot → confirm → record; deny → abort
  - Timing calculation between steps
  - Screen dimensions + DPI captured at recording start
  - Drag pairing: DragDrop → button action → single step with DragButton
  - Drag pairing: DragDrop → Cancel → partial discarded
  - **StopRecording with `_pendingDragStart != null`** → discard pending drag, log warning, save remaining steps

- [ ] 3.5 Integration tests for recording flow via coordinator (REQ-5, REQ-6, REQ-18, REQ-20, REQ-21) [after: 3.2] `M`
  - Simulate: open overlay → press record key → select slot → navigate → action → verify step recorded → overlay resumed with new session → second action → stop recording
  - Verify: action fires at correct position; overlay suspends/resumes (session re-created, not original); modifier keys captured
  - Verify: Escape cancels at each stage; `_pendingDragStart` cleared on cancel
  - Verify: hook stays enabled during suspend; only record-control + Escape keys processed during delay
  - Verify: `_macroState` prevents concurrent recording/playback
  - Verify: key dispatch priority — record key intercepted before chord dispatch, session forwarding
  - Verify: focus loss during recording → overlay NOT deactivated
  - Verify: resume uses current cursor position as session origin

## Phase 4: Macro Picker
<!-- worktree: -->

- [ ] 4.1 `MacroPickerOverlay` — centered activating window listing 10 slots (REQ-9) [after: 2.1] `M` [discovery]
  - WPF window: `WindowStyle=None`, `AllowsTransparency=True`, `Topmost=True`, centered on primary screen
  - **Activating**: takes WPF keyboard focus. Handles `KeyDown` events directly (slot keys + Escape). Hook is disabled while picker is active (picker owns all key input).
  - **Focus loss**: wires `Deactivated` → self-dismiss + fire `PickerClosed` (prevents stuck `Picking` state on Alt+Tab/focus loss)
  - When opened via global hotkey (no overlay active): `SetForegroundWindow` P/Invoke after `Show()` to ensure foreground activation from background process
  - When opened via helper key (overlay active): coordinator suspends overlay, picker takes focus. On picker close, coordinator resumes overlay if it was active.
  - Shows 10 rows: slot key label + macro name (or "\<empty\>" in gray)
  - Key press on slot key → fire `SlotSelected(int slot)` if occupied; no-op if empty
  - Escape → close picker → fire `PickerClosed`
  - Styling: consistent with existing overlay theme (background color, font, opacity from theme)
  - Interface: `IMacroPickerWindow` for test faking

- [ ] 4.2 Global hotkey registration for macro picker (REQ-10, RISK-3) [after: 4.1, 1.4] `M`
  - New `IMacroHotKeyService` (separate from main `IHotKeyService`) — same pattern as `IScrollHotKeyService`
  - Owns its own `HwndSource` and hotkey ID (`0x3000`)
  - `Register()` attempts registration; failure → tray notification
  - `Activated` event → if `_macroState != Idle` → ignore; else set `_macroState = Picking`, disable hook, show `MacroPickerOverlay`

- [ ] 4.3 Overlay helper key integration (REQ-11, REQ-17, REQ-21) [after: 4.1, 3.2] `M`
  - Helper key (default backtick) processed in coordinator's key dispatch chain (priority step 6, after record key, before chord dispatch)
  - When pressed with overlay open and `_macroState == Idle`: suspend overlay → disable hook → set `_macroState = Picking` → show `MacroPickerOverlay`
  - Slot selected → close picker → start playback (Phase 5)
  - Picker closed without selection (Escape or focus loss) → re-enable hook → resume overlay to L1 (new session, current cursor origin) → `_macroState = Idle`
  - If `_macroState == Recording` → helper key ignored
  - If `_macroState == Playing` → helper key passed through
  - Key collision: if helper key collides with nav keys → log warning, helper key disabled for this session

- [ ] 4.4 Unit tests for macro picker (REQ-9, REQ-10, REQ-11, REQ-21) [after: 4.1, 4.2, 4.3] `S`
  - Slot selection fires correct event
  - Empty slot → no event
  - Escape → close → `PickerClosed` event
  - **Focus loss** → picker dismissed → `PickerClosed` event (prevents stuck `Picking` state)
  - Global hotkey service registration/unregistration
  - Helper key blocked during recording/playback
  - Picking state: hook disabled, picker handles KeyDown directly
  - Helper key path: overlay suspended → hook disabled → picker shown → picker closed → hook re-enabled → overlay resumed

## Phase 5: Playback
<!-- worktree: -->

- [ ] 5.1 `MacroPlayer` — execute recorded steps with timing (REQ-12, REQ-13, REQ-16, REQ-18, REQ-23, REQ-27, RISK-1) [after: 1.1, 1.7, 1.8, 2.1] `L`
  - Constructor: `IMouseActionService`, `IScreenBoundsProvider` (for screen bounds + DPI scale), `IDelayProvider`, `double speedModifier`
  - `Play(MacroDefinition macro, CancellationToken ct)`: async method returning `PlaybackResult`
  - Screen validation: compare current `screenWidth`/`screenHeight` (from `GetPrimaryScreenBounds()`) and `dpiScale` (from `GetDpiScale()`) vs recorded values; mismatch → return `PlaybackResult.ScreenMismatch(expected, actual)`
  - **Action mapping table**:
    - `LeftClick` / `RightClick` / `MiddleClick` / `DoubleClick` → `SendAction(point, MouseAction.*, modifiers)`
    - `MoveOnly` → `SendAction(point, MouseAction.MoveOnly, ActionModifiers.None)`
    - `DragDrop` → `SendDrag(start, end, step.DragButton!.Value, modifiers)`
    - `Scroll` → `SendScroll(step.ScrollDelta!.Value, modifiers)`
  - Step execution loop:
    - Check `CancellationToken` before each step
    - Wait via `IDelayProvider.Delay(delay, ct)` where `delay = Math.Max(50, (int)Math.Min((long)relativeTimeMs * speedModifier, int.MaxValue))` (50ms global minimum floor; int overflow protection)
    - When `speedModifier == 0`: use 100ms fixed delay
    - Execute action per mapping table
  - Fire `StepCompleted(int stepIndex, int totalSteps)` event for progress tracking
  - Return `PlaybackResult.Completed` or `PlaybackResult.Cancelled`

- [ ] 5.2 Coordinator integration: playback lifecycle + Escape cancel (REQ-12, REQ-15, REQ-20, REQ-21, REQ-25, REQ-26, RISK-8, RISK-9, RISK-10) [after: 5.1, 4.3] `M`
  - Track whether playback was initiated via global hotkey or helper key (`_playbackEntryPath`)
  - On slot selected from picker:
    - Guard: if `_macroState != Picking` → ignore (defensive)
    - Set `_macroState = Playing`
    - Load macro from `MacroStore`
    - Create `CancellationTokenSource` stored as `_playbackCts`
    - Re-enable hook with strict key filtering (only Escape processed, all else passed through via `CallNextHookEx`). Hook was disabled for picker; re-enable for Escape capture.
    - Show playback overlay (5.3)
    - Start `MacroPlayer.Play()`, store returned `Task` as `_playbackTask`. Wrapper:
      ```
      try { result = await _player.Play(macro, _playbackCts.Token); }
      catch (OperationCanceledException) { /* normal cancel */ }
      catch (Exception ex) { _logger.LogError(ex, "Macro playback failed"); }
      finally { if (!_disposed) OnPlaybackFinished(result); }
      ```
    - `OnPlaybackFinished`: dismiss playback overlay → disable hook → handle result:
      - `ScreenMismatch` → tray notification with expected vs actual values
      - Global hotkey path → `DeactivateOverlay()` (no prior overlay)
      - Helper key path → `ResumeOverlayAfterPlayback()`: re-enable hook in normal mode → create new default-mode session → activate with current cursor as origin → start debounce timer
      - `_macroState = Idle`
    - On Escape key during playback (via hook strict filter) → cancel `_playbackCts`
  - **Focus loss guard**: `_macroState != Idle` in `OnFocusLost` suppresses `DeactivateOverlay()`
  - **Dispose**: cancel `_playbackCts`, `_playbackTask?.Wait()` (drain before teardown), call `MacroRecorder.Cancel()`, then proceed with existing teardown. `_disposed = true` set before service disposal to guard `OnPlaybackFinished` callback.
  - **Save failure**: `MacroStore.Save()` failure → tray notification + retain `MacroDefinition` in memory (slot not emptied)

- [ ] 5.3 Playback overlay: "Klikety macro: \<name\>" + progress bar (REQ-14) [after: 5.1] `M` [discovery]
  - Small WPF window (fixed size, e.g. 350×80) centered on screen
  - Title: "Klikety macro: \<name\>"
  - Progress bar: filled proportionally as steps complete
  - Step counter text: "Step N / M"
  - `Topmost=True`, `ShowActivated=false`, non-activating (don't steal focus from target app)
  - Updated via `StepCompleted` event
  - Interface: `IMacroPlaybackWindow` for test faking

- [ ] 5.4 Unit tests for MacroPlayer (REQ-12, REQ-13, REQ-15, REQ-16, REQ-18, REQ-23, REQ-27, RISK-1) [after: 5.1] `M`
  - Playback fires correct actions in order with correct coordinates and modifiers
  - Action mapping: each `MacroActionType` → correct `IMouseActionService` method call
  - Speed modifier 1.0: `FakeDelayProvider` receives correct delays
  - Speed modifier 0: delays = 100ms fixed
  - Speed modifier 0.5: delays halved (minimum 50ms floor)
  - **50ms global delay floor**: speed modifier 0.01 with 100ms step → `Delay(50)` not `Delay(1)`
  - Screen mismatch → `PlaybackResult.ScreenMismatch`, no actions fired
  - Cancellation mid-playback → remaining steps skipped, `PlaybackResult.Cancelled`
  - Drag step → `SendDrag` called with start + end + correct `DragButton`
  - Scroll step → `SendScroll` called with delta + modifiers
  - All tests use `FakeDelayProvider` — no real timing, deterministic

- [ ] 5.5 Integration tests for playback via coordinator (REQ-12, REQ-15, REQ-21, REQ-25, REQ-26) [after: 5.2] `M`
  - Simulate: trigger macro → verify actions fired in order via `FakeMouseActionService`
  - Simulate: Escape during playback (via hook) → verify remaining actions not fired
  - Simulate: screen mismatch → verify tray notification, no actions
  - Simulate: trigger playback while already playing → ignored
  - Simulate: trigger playback while recording → ignored
  - Simulate: global hotkey path → `DeactivateOverlay()` on complete
  - Simulate: helper key path → overlay resumed on complete (hook re-enabled, new session, debounce)
  - Simulate: focus loss during playback → overlay NOT deactivated
  - Simulate: `Dispose()` during playback → CTS cancelled, task drained, clean shutdown
  - Simulate: `_disposed` guard prevents `OnPlaybackFinished` callback on disposed services

## Phase 6: Polish & Documentation
<!-- worktree: -->

- [ ] 6.1 Logging: structured log entries for macro operations (REQ-5, REQ-12) [after: 3.2, 5.2] `S`
  - Recording: start, each step captured, stop, cancel
  - Playback: start, screen validation result, each step executed, complete, cancel
  - Use existing `ILogger` + source-generated log methods (partial class)

- [ ] 6.2 Tray menu: macro status indicator (REQ-4) [after: 5.2] `S`
  - "Macros: N/10 defined" info item in tray context menu
  - No interactive tray actions (picker is the UI)

- [ ] 6.3 Design note: create `macros.design.md` [after: 5.5] `S`
  - Document: data model, recording flow (suspend/resume with session re-creation, hook override, drag pairing, resume delay, cursor origin), playback flow (hook strict filtering, delay abstraction, post-playback restoration per entry path), async lifecycle, config schema, key collision rules, dispatch priority chain, macro state mutex (including `Picking`), focus loss guard, semantic validation
  - Add to `.design-notes.md` index table

- [ ] 6.4 Update `config.design.md` with macros config section (REQ-4) [after: 1.9] `S`
  - Document: `MacrosConfig` shape, migration v4→v5, validation rules, full collision matrix
