---
description: Macro recording and playback — data model, recorder state machine, player engine, picker overlay, click indicator, coordinator integration, dispatch priority.
globs:
  - src/Klikety/Config/MacroModels.cs
  - src/Klikety/Config/MacroStore.cs
  - src/Klikety/Navigation/MacroRecorder.cs
  - src/Klikety/Navigation/MacroPlayer.cs
  - src/Klikety/Navigation/MacroHandler.cs
  - src/Klikety/Overlay/MacroPickerOverlay.xaml*
  - src/Klikety/Overlay/MacroPlaybackOverlay.xaml*
  - src/Klikety/Overlay/ClickIndicatorWindow.cs
  - src/Klikety/Overlay/ClickIndicatorLifecycle.cs
  - src/Klikety/Services/MacroHotKeyService.cs
---

# Macros

Replayable mouse action recordings. 10 fixed slots, each storing a named sequence of mouse actions with screen context and timing.

## Data Model

**File**: `%APPDATA%\Klikety\macros.json` (JSONC, `$schema` ref to `macros.schema.json`).

```
MacrosFile
  ├── Version (int, v2)
  └── Macros (MacroDefinition?[10])  — index = slot, null = empty
        ├── Name, ScreenWidth, ScreenHeight, DpiScale, SpeedModifier
        ├── PositionMode (Absolute|WindowRelative), WindowWidth, WindowHeight, WindowTitlePattern
        └── Steps (List<MacroStep>)
              └── ActionType, X, Y, Modifiers, RelativeTimeMs, EndX?, EndY?, ScrollDelta?, DragButton?, StartFromCursor
```

- `MacroActionType`: `LeftClick`, `RightClick`, `MiddleClick`, `DoubleClick`, `MoveOnly`, `DragDrop`, `Scroll`.
- `MacroPositionMode`: `Absolute` (default, screen coordinates), `WindowRelative` (offsets from target window top-left).
- `RelativeTimeMs`: persisted inter-step interval. The first step measures from recording start; later steps measure from the preceding recorder timer reset, not an absolute timestamp. A drag pair stores the interval captured at its start; selecting its endpoint resets the timer again, so the following step measures from endpoint selection. Saved values and format are unchanged.
- `SpeedModifier`: per-macro speed multiplier (default 1.0). Overrides global `config.macros.speedModifier` when ≠ 1.0.
- `StartFromCursor`: `bool` on `MacroStep`, default `false`. Only valid on `DragDrop` steps. `[JsonIgnore(Condition = WhenWritingDefault)]` suppresses serialization when false.
- `WindowRelative` fields: `WindowWidth`/`WindowHeight` = recorded window dimensions, `WindowTitlePattern` = substring for title matching at playback.
- All models have `[JsonExtensionData] Dictionary<string, JsonElement>?` for forward-compatible round-trip.
- Array padding: <10 → pad with nulls; >10 → preserved for round-trip, only first 10 bound.
- **Version**: v2. `MacrosFile.Version` defaults to 2. v1 files load (missing fields default to `Absolute`/zero/empty). `MacroStore.Save()` always writes version 2.

## MacroStore

`IMacroStore` → `MacroStore`. Load/save `macros.json`.

- **Load**: parse JSON → version check (>2 → quarantine all) → pad array → per-slot semantic validation. Invalid slots quarantined (set to null) with error messages.
- **Semantic validation**: dispatched by `PositionMode`:
  - **Absolute**: `DragDrop` requires non-null `DragButton` (only `LeftClick`/`RightClick`/`MiddleClick`), `EndX`, `EndY`. `Scroll` requires non-null `ScrollDelta`. Non-negative `RelativeTimeMs`. Coordinate bounds checked against `ScreenWidth`/`ScreenHeight`.
  - **WindowRelative**: requires `WindowWidth > 0`, `WindowHeight > 0`, non-empty `WindowTitlePattern`. Step coordinates checked against `WindowWidth`/`WindowHeight`. `StartFromCursor` on non-`DragDrop` → error. When `StartFromCursor == true`, start X/Y validation skipped (vestigial coords).
- **Save**: atomic write — temp file in same directory (`Path.GetRandomFileName()`) → `File.Move(overwrite: true)`. Same-volume rename = atomic. Always writes `Version = 2`. Returns `MacroSaveResult` with success/failure.
- **First run**: `FirstRunExtractor` creates `macros.json` (skip-if-exists) and `macros.schema.json` (always-overwrite).

## Macro State Mutex

`MacroState` enum (namespace-level in `NavigatorCoordinator.cs`): `Idle`, `Recording`, `Playing`, `Picking`. Enforces mutual exclusion — only one macro operation active. Guards all entry points. `MacroHandler` owns the state field and exposes it via `State` property.

## Key Dispatch Priority

Full chain in `OnKeyEvent` (coordinator delegates to `MacroHandler.TryHandleKey()` for steps 2–6b):

1. Debounce check
2. `Playing` → only Escape (cancel playback), all else passed through via `CallNextHookEx`
3. `Picking` → N/A (hook disabled; picker handles `KeyDown` directly)
4. `Recording` → recording control keys (record toggle, Escape, slot/overwrite Y/N)
5. Record key when `Idle` + overlay open → `StartRecording()`
6. Helper key when `Idle` + overlay open → open picker
6b. Direct slot key (F1–F10) when `Idle` + overlay open → play macro directly. D1–D9 are display-switch keys and are consumed before `MacroHandler`.
7. Chord dispatch (existing, if `!_modeLocked`)
8. Session forwarding (existing)

## Recording Flow

`MacroRecorder` state machine: `Idle` → `AwaitSlot` → `AwaitOverwrite` (if occupied) → `Recording` → (for DragDrop in window-relative) `AwaitStartFromCursorConfirm`.

- **Slot selection**: overlay shows prompt; next digit key selects slot. Occupied → overwrite confirm (Y/N).
- **App-scope context**: when recording starts from app-scoped overlay, coordinator builds `MacroRecordingContext` (window bounds, title pattern, DPI) and passes to `MacroRecorder.StartRecording()`. Empty title → recording rejected with status message.
- **Title pattern extraction**: `ExtractTitlePattern()` takes last segment after " - " separator (e.g. "Document.txt - Notepad" → "Notepad").
- **Step capture**: `ActionDispatcher.HandleRecordingAction()` intercepts `ActionRequested` from session → records step (action type, point, modifiers, timing) → coordinator calls `MacroHandler.SuspendOverlayForAction()` → action fires → 200ms delay → `MacroHandler.ScheduleResumeAfterAction()`.
- **Coordinate transformation**: when `_recordingAppScoped`, `ActionDispatcher` transforms screen coordinates to window-relative: `(point.X - windowBounds.Left, point.Y - windowBounds.Top)`.
- **Resume in app-scope**: `MacroHandler.ResumeOverlayForRecording()` re-queries window bounds. HWND invalid → auto-stop + save. Resized → cancel. Moved → update bounds + clamp origin. Fires `ResumeOverlayRequested` event; coordinator calls `_overlayWindow.Show()`. Delegates session creation to `SessionManager.ResumeForRecording()`.
- **ResetOverlayForDrag**: preserves app-scope when `_recordingAppScoped` — activates with window bounds instead of screen bounds.
- **StartFromCursor prompt**: after DragDrop pair completion in window-relative mode, recorder transitions to `AwaitStartFromCursorConfirm` and fires `StartFromCursorRequested`. Coordinator shows "Drag from cursor? [Y/N]". Y → `StartFromCursor = true` on step. N → normal step. Escape → cancel recording. `StopRecording()` from this state finalizes pending step with default decline.
- **Drag pairing**: `DragDrop` action held as `_pendingDragStart`. Next action resolves the button → emit single `MacroStep` with `DragButton`, `EndX`, `EndY`. Cancel during pending drag → clear `_pendingDragStart`.
- **Stop**: auto-names "Macro N". Sets `SpeedModifier = 1.0`. If `_pendingDragStart` is non-null → discarded with warning. Fires `RecordingComplete(slot, macro)`.
- **Focus loss guard**: `_macroState != Idle` in `OnFocusLost` suppresses `DeactivateOverlay()`.

## Picker

`MacroPickerOverlay` — activating WPF window. Takes keyboard focus; hook disabled while picker active.

- Shows 10 rows: slot key + name + position badge (`[W]` for WindowRelative, `[S]` for Absolute) (or `<empty>` grayed).
- Slot key on occupied slot → `SlotSelected(slot)` → close picker → start playback.
- Escape or focus loss → `PickerClosed` → resume overlay.
- `_slotSelected` guard prevents `Deactivated` from firing `PickerClosed` after a slot selection (race fix).
- **Global hotkey path**: `MacroHotKeyService` (Ctrl+Alt+Shift+M) → `SetForegroundWindow` after `Show()`.
- If the global macro picker opens while navigation is visible, `MacroHandler` disables the keyboard hook and requests overlay suspension before showing the picker. The coordinator clears help state/latches before hiding; closing the picker resumes the same navigation session. The global path remains separate from the overlay-local help toggle.
- With keyboard help open, macro record/helper/slot keys and recording-confirmation keys dismiss help first and then run through the same macro dispatch path. Escape/help closes only help, preserving the current recording prompt; modifier-only presses do not dismiss help.
- **Helper key path**: overlay suspends → picker opens. On close → overlay resumes to L1.

## Playback Flow

`MacroPlayer` — async engine executing steps with timing.

- **Screen validation** (Absolute): current width/height/DPI must match recorded values. Mismatch → `PlaybackResult.ScreenMismatch` + tray notification.
- **Window validation** (WindowRelative): foreground window title must contain `WindowTitlePattern` (case-insensitive). Window dimensions must match `WindowWidth`/`WindowHeight`. DPI must match (epsilon 0.01). Mismatch → `PlaybackResult.WindowMismatch`.
- **Coordinate resolution** (WindowRelative): step coordinates offset from window top-left: `screenPoint = (windowBounds.Left + step.X, windowBounds.Top + step.Y)`. Resolved point checked against window bounds → `PlaybackResult.CoordinateOutOfBounds` if outside.
- **Per-step drift check** (WindowRelative): before each action step, re-verify foreground HWND matches and window bounds haven't changed. Focus lost → `PlaybackResult.WindowDrift`. Resized → `WindowDrift`. Moved → update bounds for next step (coordinates stay correct).
- **StartFromCursor** (WindowRelative DragDrop): when `step.StartFromCursor == true`, drag starts from `PlaybackContext.InitialCursorPosition` instead of resolved step position. End point still resolved normally.
- **PlaybackContext**: record passed to `Play()` with `PositionMode`, `WindowBounds`, `WindowTitle`, `WindowHwnd`, `InitialCursorPosition`. `PlaybackContext.Absolute` for screen-space macros. Built from `_preOverlayHwnd` (captured before overlay opens). **Critical**: `StartPlayback(macro, targetHwnd)` takes an explicit HWND parameter — callers must save `_preOverlayHwnd` before any `_overlayWindow.Hide()` call, because `Hide()` triggers `DeactivateOverlay()` which zeros `_preOverlayHwnd`. Global hotkey path captures HWND at activation time.
- **PlaybackResultKind**: `Completed`, `Cancelled`, `InputFailed`, `ScreenMismatch`, `WindowMismatch`, `CoordinateOutOfBounds`, `WindowDrift`. An incomplete native action carries its `InputResult` and diagnostic message, stops later steps and emits no progress for the failed step. The handler logs one playback error, closes progress and returns to idle with the existing overlay restoration; no new tray event.
- **Speed modifier**: `delay = Math.Max(50, (int)(relativeTimeMs × speedModifier))`. When `speedModifier == 0` → 100ms fixed. 50ms global floor prevents input coalescing. Per-macro `SpeedModifier` overrides global config when ≠ 1.0.
- **Delay chunking**: delays split into 50ms ticks for live countdown updates. `DelayUpdate(remainingMs, actionType)` event fires each tick.
- **Drag sequencing**: await the typed drag outcome before reporting progress or starting the next saved inter-step interval. Native drag delays remain 100 ms then 50 ms. This intentionally removes the old fire-and-forget overlap (about 150 ms); serialized intervals, speed scaling and floors remain unchanged.
- **Click indicator**: `IClickIndicator.ShowAndWait(x, y, ct)` called before each action step (except `MoveOnly`). Non-activating, click-through WPF window. `ClickIndicatorLifecycle` composes dispatcher/view seams; completion, cancellation and disposal stop animations, detach handlers and hide on the dispatcher. Operation identity guards ignore stale completions after reuse. Cancellation callbacks only queue cleanup; they never synchronously invoke the dispatcher.
- **Cancellation**: `CancellationToken` checked before each step and immediately before input dispatch, after any awaited indicator. Escape via hook cancels the current operation. Cancellation skips pending input and its progress event; it cannot undo input already dispatched.
- **Progress**: `StepCompleted(completed, total)` event per step → updates `MacroPlaybackOverlay` progress bar.

### Playback Overlay

`MacroPlaybackOverlay` — non-activating (`ShowActivated=false`), topmost progress window. Shows macro name, progress bar, step counter, and delay countdown text ("Waiting N ms… then ActionType"). For window-relative macros, title includes the window context pattern (e.g. "Klikety macro: Test — Notepad").

### Async Lifecycle

```csharp
try { result = await operation.Player.Play(macro, context, operation.Cancellation.Token); }
catch (OperationCanceledException) when (operation.Cancellation.IsCancellationRequested) { /* normal cancel */ }
catch (Exception ex) { /* InputFailed with diagnostic; handler logs once */ }
finally { /* dispose operation CTS; finish only if still current and not disposed */ }
```

`Dispose()` marks the handler disposed, releases its current-operation reference and initiates cancellation without draining playback. Each operation owns its task/CTS; resource completion and native drag delays do not capture the UI context, so CTS cleanup can finish after the dispatcher stops pumping. A small operation lock serializes cancellation against CTS disposal. Progress/restoration marshal through the captured synchronization context with identity/disposal guards; late callbacks cannot mutate a replacement.

Indicator teardown runs directly when already on its dispatcher, otherwise queues there. Cancellation callbacks queue active-view cleanup; not-yet-started operations complete cancellation without dispatcher pumping and cannot start later. Thus quit can cancel queued starts and stop/hide/unsubscribe before `Shutdown()` without a dispatcher wait. No live WPF/STA harness is required for these ownership rules.

Recording still stores attempted actions before native dispatch, including failed attempts. Non-macro drag observers log typed failures or task exceptions without changing the existing recording prompt, 200 ms resume scheduling or successful overlay restoration policy.

### Post-Playback Restoration

- **Global hotkey path**: `DeactivateOverlay()` (no overlay was active).
- **Helper key path**: resume overlay to L1 — re-enable hook, new default-mode session, current cursor as origin.

## Config

`MacrosConfig` on `ConfigModel`:

- `Enabled` (bool, default `true`)
- `GlobalHotKey` (HotKeyConfig?, default Ctrl+Alt+Shift+M; null → skip registration)
- `RecordKey` (VKey, default `OemPipe` = backslash)
- `HelperKey` (VKey, default `OemTilde` = backtick)
- `SlotKeys` (VKey[10], default `F1`–`F10`)
- `SpeedModifier` (double, default 1.0; 0 → 100ms fixed; negative → clamped to 0)
- `PlaybackIndicator` (PlaybackIndicatorConfig — fill/stroke color, initial/final radius, animation duration)

Config version: v4→v5 migration adds `macros` section. Key collision matrix validates all macro keys against reserved/action/nav/chord/scroll/hotkey sets.

## Schemas

- `macros.schema.json`: JSON Schema draft-07, EmbeddedResource, always-overwritten by `FirstRunExtractor`.
- `config.schema.json`: `macros` section with `playbackIndicator` sub-object.

## Testability Seams

All external dependencies abstracted:

- `IMacroStore` → `FakeMacroStore`
- `IMacroPickerWindow` → `FakeMacroPickerWindow`
- `IMacroPlaybackWindow` → `FakeMacroPlaybackWindow`
- `IClickIndicator` → `FakeClickIndicator`
- `IDelayProvider` → `FakeDelayProvider` (deterministic timing)
- `IMacroHotKeyService` → `FakeMacroHotKeyService`
