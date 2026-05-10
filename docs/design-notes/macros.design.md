---
description: Macro recording and playback — data model, recorder state machine, player engine, picker overlay, click indicator, coordinator integration, dispatch priority.
globs:
  - src/Klikety/Config/MacroModels.cs
  - src/Klikety/Config/MacroStore.cs
  - src/Klikety/Navigation/MacroRecorder.cs
  - src/Klikety/Navigation/MacroPlayer.cs
  - src/Klikety/Overlay/MacroPickerOverlay.xaml*
  - src/Klikety/Overlay/MacroPlaybackOverlay.xaml*
  - src/Klikety/Overlay/ClickIndicatorWindow.cs
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
- `RelativeTimeMs`: delay before this step (relative to recording start, not previous step).
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

`MacroState` enum on `NavigatorCoordinator`: `Idle`, `Recording`, `Playing`, `Picking`. Enforces mutual exclusion — only one macro operation active. Guards all entry points.

## Key Dispatch Priority

Full chain in `OnKeyEvent`:

1. Debounce check
2. `Playing` → only Escape (cancel playback), all else passed through via `CallNextHookEx`
3. `Picking` → N/A (hook disabled; picker handles `KeyDown` directly)
4. `Recording` → recording control keys (record toggle, Escape, slot/overwrite Y/N)
5. Record key when `Idle` + overlay open → `StartRecording()`
6. Helper key when `Idle` + overlay open → open picker
6b. Direct slot key (D0–D9) when `Idle` + overlay open → play macro directly
7. Chord dispatch (existing, if `!_modeLocked`)
8. Session forwarding (existing)

## Recording Flow

`MacroRecorder` state machine: `Idle` → `AwaitSlot` → `AwaitOverwrite` (if occupied) → `Recording`.

- **Slot selection**: overlay shows prompt; next digit key selects slot. Occupied → overwrite confirm (Y/N).
- **Step capture**: coordinator intercepts `ActionRequested` from session → records step (action type, point, modifiers, timing) → `SuspendOverlayForAction()` → action fires → 200ms delay → `ResumeOverlayForRecording()`.
- **Suspend/resume**: suspend hides overlay without coordinator teardown. Resume creates new default-mode session with current cursor position as origin. Hook stays enabled with strict filtering during gap.
- **Drag pairing**: `DragDrop` action held as `_pendingDragStart`. Next action resolves the button → emit single `MacroStep` with `DragButton`, `EndX`, `EndY`. Cancel during pending drag → clear `_pendingDragStart`.
- **Stop**: auto-names "Macro N". Sets `SpeedModifier = 1.0`. If `_pendingDragStart` is non-null → discarded with warning. Fires `RecordingComplete(slot, macro)`.
- **Focus loss guard**: `_macroState != Idle` in `OnFocusLost` suppresses `DeactivateOverlay()`.

## Picker

`MacroPickerOverlay` — activating WPF window. Takes keyboard focus; hook disabled while picker active.

- Shows 10 rows: slot key + name (or `<empty>` grayed).
- Slot key on occupied slot → `SlotSelected(slot)` → close picker → start playback.
- Escape or focus loss → `PickerClosed` → resume overlay.
- `_slotSelected` guard prevents `Deactivated` from firing `PickerClosed` after a slot selection (race fix).
- **Global hotkey path**: `MacroHotKeyService` (Ctrl+Alt+Shift+M) → `SetForegroundWindow` after `Show()`.
- **Helper key path**: overlay suspends → picker opens. On close → overlay resumes to L1.

## Playback Flow

`MacroPlayer` — async engine executing steps with timing.

- **Screen validation**: current width/height/DPI must match recorded values. Mismatch → `PlaybackResult.ScreenMismatch` + tray notification.
- **Speed modifier**: `delay = Math.Max(50, (int)(relativeTimeMs × speedModifier))`. When `speedModifier == 0` → 100ms fixed. 50ms global floor prevents input coalescing. Per-macro `SpeedModifier` overrides global config when ≠ 1.0.
- **Delay chunking**: delays split into 50ms ticks for live countdown updates. `DelayUpdate(remainingMs, actionType)` event fires each tick.
- **Click indicator**: `IClickIndicator.ShowAndWait(x, y)` called before each action step (except `MoveOnly`). Non-activating, click-through WPF window. Shrinking circle animation (configurable via `PlaybackIndicatorConfig`). Waits for animation to complete before executing the click.
- **Cancellation**: `CancellationToken` checked before each step. Escape via hook → cancel CTS.
- **Progress**: `StepCompleted(completed, total)` event per step → updates `MacroPlaybackOverlay` progress bar.

### Playback Overlay

`MacroPlaybackOverlay` — non-activating (`ShowActivated=false`), topmost progress window. Shows macro name, progress bar, step counter, and delay countdown text ("Waiting N ms… then ActionType").

### Async Lifecycle

```csharp
try { result = await _player.Play(macro, _playbackCts.Token); }
catch (OperationCanceledException) { /* normal cancel */ }
catch (Exception ex) { _logger.LogError(...); }
finally { if (!_disposed) OnPlaybackFinished(result); }
```

`Dispose()`: cancel CTS → `_playbackTask.Wait()` (drain) → `_disposed = true` → existing teardown.

### Post-Playback Restoration

- **Global hotkey path**: `DeactivateOverlay()` (no overlay was active).
- **Helper key path**: resume overlay to L1 — re-enable hook, new default-mode session, current cursor as origin.

## Config

`MacrosConfig` on `ConfigModel`:

- `Enabled` (bool, default `true`)
- `GlobalHotKey` (HotKeyConfig?, default Ctrl+Alt+Shift+M; null → skip registration)
- `RecordKey` (VKey, default `OemPipe` = backslash)
- `HelperKey` (VKey, default `OemTilde` = backtick)
- `SlotKeys` (VKey[10], default `D0`–`D9`)
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
