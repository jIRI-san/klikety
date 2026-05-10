# 017: Relative Position Macros (Window-Bound)

## Decisions

- **Position mode auto-derived from context**: app-scope → `WindowRelative`, full-screen → `Absolute`. No user prompt.
- **Window-relative = offsets from window top-left**: `X - windowLeft`, `Y - windowTop`. Stored alongside window metadata (title substring, size, DPI).
- **App-scope required for window-relative**: the scoped overlay physically constrains actions to the window bounds, preventing out-of-window coordinates.
- **Recording resume preserves app-scope**: after each action during recording, overlay re-opens still scoped to the same window (current behavior creates a fresh full-screen session; change scopes it to the stored window bounds). Uses stored `_preOverlayHwnd` — never re-queries foreground during resume (avoids race with notifications/focus steal).
- **Coordinator owns coordinate transform**: coordinator pre-transforms screen-space action points to window-relative offsets before passing to `MacroRecorder.RecordAction()`. Recorder has no knowledge of window bounds — it stores whatever coordinates it receives. This avoids bounds-propagation complexity when windows move during recording.
- **Playback validation: foreground window**: user must focus the target window before triggering playback. Title substring must match, window size must match exactly, DPI must match (epsilon 0.01 for floating-point tolerance).
- **Playback per-step drift check**: before each action step (click, drag, scroll), re-verify foreground HWND matches and window bounds haven't changed. Drift → abort with `PlaybackResult.WindowDrift`.
- **Resolved coordinates validated within window bounds**: if any offset + window position falls outside window rect, abort playback — no silent clipping.
- **StartFromCursor on DragDrop steps**: drag start = cursor position captured at macro playback start (stored in `PlaybackContext.InitialCursorPosition`), drag end = stored window-relative coordinate. Cursor position validated within window bounds at playback.
- **StartFromCursor X/Y storage**: recorded steps store the original position (from drag pairing) for display/debugging purposes. Validation skips start coordinates when `StartFromCursor == true`, only validates `EndX`/`EndY`.
- **MacrosFile version bump v1→v2**: new fields on `MacroDefinition` (`PositionMode`, `WindowWidth`, `WindowHeight`, `WindowTitlePattern`). New field on `MacroStep` (`StartFromCursor`). Old v1 macros default to `Absolute` (missing fields = backward compatible). `MacroStore.Save()` always writes `Version = 2`.
- **No config.json migration**: all changes are in macros.json data model. Config version stays at v6.
- **One StartFromCursor drag per macro**: sufficient for the email-sorting use case. Can relax later if needed.
- **Title matching: substring (case-insensitive)**: `windowTitle.Contains(pattern, StringComparison.OrdinalIgnoreCase)`. Simple, covers Outlook et al.
- **Title pattern extraction**: at recording time, extract last segment after " - " separator as the default pattern (e.g., "Outlook" from "Inbox - user@example.com - Outlook"). If no separator found, use full title. User can edit in `macros.json`.
- **Empty window title blocks recording**: if `GetWindowTitle()` returns empty for the target HWND, refuse to start window-relative recording with tray notification. Prevents silent quarantine on reload.
- **Window title captured at app-scope activation**: `IForegroundWindowProvider` extended with `string GetWindowTitle(nint hwnd)` method.
- **Existing absolute macros unchanged**: no migration, no behavior change. `PositionMode` absent in JSON → deserialized as `Absolute` (default enum value).
- **Picker pre-captures HWND**: for window-relative playback via picker, the target HWND is captured *before* the picker window opens (reuses existing `_preOverlayHwnd` pattern). Avoids foreground-capture race.
- **Drag recording preserves app-scope**: `ResetOverlayForDrag()` during window-relative recording creates a new session scoped to app-scope bounds (not full-screen). Prevents out-of-window drag endpoints.
- **HWND invalid on resume → auto-stop + save**: if the target window is closed during recording (HWND becomes invalid on resume), auto-stop and save the macro with steps recorded so far. Do not cancel/discard. Final-step window close is a valid end of recording.
- **Multi-monitor DPI**: known limitation — `GetDpiScale()` returns primary monitor DPI. Window on secondary monitor with different DPI → validation may pass incorrectly. Documented, not solved in this plan.
- **Window-relative offsets are frame-independent**: because offsets are `point - windowLeft/Top`, a window move with same size produces identical offsets. Safe to update bounds on resume without invalidating previously recorded steps.

## Requirements

| ID | Requirement | Acceptance Criteria | Phases/Steps |
|----|-------------|---------------------|--------------|
| REQ-1 | `IForegroundWindowProvider.GetWindowTitle(nint)` returns the window title for a given HWND | Fake returns configurable title; production uses `GetWindowTextW`; zero/invalid HWND → empty string | 1.1, 1.2 |
| REQ-2 | `MacroDefinition` extended with `PositionMode`, `WindowWidth`, `WindowHeight`, `WindowTitlePattern` | New fields serialize/deserialize correctly; `PositionMode` defaults to `Absolute` when absent in JSON; `JsonExtensionData` round-trip preserved | 1.3 |
| REQ-3 | `MacroStep` extended with `StartFromCursor` bool | Defaults to `false` when absent; serializes only when `true` | 1.3 |
| REQ-4 | MacrosFile version bump v1→v2 with backward compatibility | v1 files load without error (missing fields default); v2 files include new fields; `MacroStore.Load()` accepts both versions | 1.3, 1.4 |
| REQ-5 | Recording in app-scope auto-sets `WindowRelative` mode | When `_appScoped == true` at recording start, recorder captures window metadata (title, size, DPI) and stores coordinates as window-relative offsets | 2.1, 2.2, 2.3 |
| REQ-6 | Recording in full-screen continues as `Absolute` | No change to existing absolute recording flow; `PositionMode` set to `Absolute`; screen metadata captured as before | 2.1 |
| REQ-7 | Recording resume preserves app-scope state | After action fires during recording, overlay re-opens scoped to the same window bounds with app-scope border visible | 2.2 |
| REQ-8 | Coordinate transformation during recording | Each recorded `MacroStep.X/Y` = `actionPoint.X - windowBounds.Left`, `.Y - windowBounds.Top` when in `WindowRelative` mode | 2.3 |
| REQ-9 | DragDrop `StartFromCursor` prompt during recording | After a DragDrop step is captured, coordinator shows prompt "Use cursor position as drag start? [Y/N]"; Y → set `StartFromCursor = true` on the step | 2.4 |
| REQ-10 | Playback of `WindowRelative` macro validates foreground window | Title substring match + exact width + exact height + DPI match (epsilon 0.01). Mismatch → `PlaybackResult.WindowMismatch` with descriptive message + tray notification | 3.1 |
| REQ-11 | Playback resolves window-relative offsets to screen coordinates | Each step: `screenX = windowBounds.Left + step.X`, `screenY = windowBounds.Top + step.Y`. Resolved point validated within window bounds. Out-of-bounds → abort | 3.2 |
| REQ-12 | Playback of `StartFromCursor` DragDrop uses cursor position captured at playback start as drag start | Drag start = `PlaybackContext.InitialCursorPosition` (not stored X/Y). Cursor validated within window bounds. Drag end = resolved window-relative EndX/EndY | 3.3 |
| REQ-13 | Absolute macro playback unchanged | Existing `Absolute` macros play back identically to current behavior; no regression | 3.1 |
| REQ-14 | `MacroStore` validation extended for window-relative macros | `WindowRelative` macros: `WindowWidth > 0`, `WindowHeight > 0`, `WindowTitlePattern` non-empty, coordinates within `WindowWidth`/`WindowHeight` bounds (skip start coords when `StartFromCursor`). `StartFromCursor` only valid on `DragDrop` steps | 1.4 |
| REQ-15 | Picker shows position mode indicator | Macro picker displays `[W]` badge for window-relative macros, `[S]` for absolute (screen). Helps user identify which macro type they're about to play | 4.1 |
| REQ-16 | Playback overlay shows window-relative context | During `WindowRelative` playback, progress overlay includes target window title pattern | 4.2 |
| REQ-17 | Per-step drift check during `WindowRelative` playback | Before each action step, re-verify foreground HWND and window bounds match. Drift → `PlaybackResult.WindowDrift` + abort | 3.2 |
| REQ-18 | Empty window title blocks recording start | If `GetWindowTitle(_preOverlayHwnd)` returns empty when starting window-relative recording → reject with status text "Window has no title — cannot record" | 2.1 |
| REQ-19 | Drag recording preserves app-scope in window-relative mode | `ResetOverlayForDrag()` during window-relative recording creates session scoped to app-scope bounds, not full-screen | 2.2 |
| REQ-20 | HWND invalid on resume → auto-stop + save | If window is closed during recording (HWND invalid on resume), stop recording and save steps captured so far | 2.2 |
| REQ-21 | `PlaybackResultKind` extended with new values + all consumers updated | New enum values: `WindowMismatch`, `CoordinateOutOfBounds`, `WindowDrift`. All switch/match on `PlaybackResultKind` updated (coordinator, overlay, tests) | 3.1, 3.2 |

## Risks

| ID | Risk | Likelihood | Impact | Mitigation | Steps |
|----|------|------------|--------|------------|-------|
| RISK-1 | `GetWindowTextW` returns empty for UWP/Store apps that use different title mechanisms | Low | Medium | Block recording if title empty (REQ-18). Document limitation. | 1.1, 2.1 |
| RISK-2 | Window title changes between recording and playback (e.g., Outlook shows email subject in title) | Medium | Medium | Extract last segment after " - " as default pattern (e.g., "Outlook"). Substring match. User can edit pattern in macros.json. | 2.1, 3.1 |
| RISK-3 | Recording resume in app-scope: window may have moved or resized between action fire and resume | Low | Medium | Re-query window bounds via stored `_preOverlayHwnd` on resume. Size changed → cancel. Position changed (same size) → update bounds. Offsets are frame-independent (safe). HWND invalid → auto-stop + save (REQ-20). | 2.2 |
| RISK-4 | Coordinate transform off-by-one at window edge | Low | Low | Validate coordinates strictly: `0 <= offset < windowWidth/Height`. Reject on boundary violation. Unit tests cover edge cells. | 2.3, 3.2 |
| RISK-5 | `StartFromCursor` prompt interrupts recording flow — user confusion | Medium | Low | Prompt is simple Y/N after DragDrop only. Default N (no change). Status text on overlay, same pattern as overwrite confirmation. Escape → decline (step saved without `StartFromCursor`). Unrelated keys ignored. | 2.4 |
| RISK-6 | MacrosFile v2 forward-compatibility: older Klikety versions can't read new fields | Low | Low | `JsonExtensionData` on all models preserves unknown fields for round-trip. Older versions ignore unknown properties. Only risk: old version overwrites v2 file → new fields lost. Acceptable for single-user app. | 1.3 |
| RISK-7 | Picker foreground capture race during WindowRelative playback | Medium | High | Use `_preOverlayHwnd` (captured before overlay shown) for picker and helper-key paths. Global hotkey path: capture foreground HWND before overlay, carry into playback context. Never query foreground after picker/overlay is visible. | 3.1 |
| RISK-8 | Mid-playback window drift (moved, resized, focus lost) | Medium | High | Per-step drift check: re-verify foreground HWND + bounds before each action step. Drift → `PlaybackResult.WindowDrift` + abort. | 3.2 |
| RISK-9 | Drag recording in window-relative mode loses app-scope via `ResetOverlayForDrag()` | Low | Medium | Modify `ResetOverlayForDrag()` to preserve app-scope when `_recordingAppScoped` is true. Create session scoped to window bounds. | 2.2 |
| RISK-10 | Multi-monitor DPI mismatch: window on secondary monitor with different scaling | Low | Medium | Known limitation. `GetDpiScale()` returns primary monitor DPI. Documented in design notes. Not solved in this plan. | 3.1 |

## Phase 1: Data Model & Platform Layer
<!-- worktree: feature/017-relative-position-macros -->

- [x] 1.1 Add `GetWindowTitle(nint hwnd)` to `IForegroundWindowProvider` + production implementation + fake (REQ-1, RISK-1) `S`
  <details><summary>Spec</summary>

  - **`IForegroundWindowProvider`** (`Services/IPlatformServices.cs`): add `string GetWindowTitle(nint hwnd)`.
  - **Production** (`Services/PlatformServices.cs` → `ForegroundWindowProvider`): delegate to `NativeMethods.GetWindowTitle(hwnd)`.
  - **`NativeMethods`** (`Interop/NativeMethods.cs`): add `GetWindowTextW` + `GetWindowTextLengthW` P/Invoke. Wrapper: `GetWindowTitle(nint hwnd)` → `hwnd == 0` → `string.Empty`; get length → allocate `char[]` → call `GetWindowTextW` → return `new string(...)`. Trim result.
  - **`FakeForegroundWindowProvider`** (`Tests/Fakes/TestFakes.cs`): add `public string Title { get; set; } = string.Empty;` property. `GetWindowTitle(nint hwnd)` → `hwnd == Handle && Handle != 0 ? Title : string.Empty`.

  </details>

- [x] 1.2 Unit tests for `GetWindowTitle` fake behavior (REQ-1) [after: 1.1] `S`
  <details><summary>Spec</summary>

  - In `ForegroundWindowProviderTests.cs`: add tests — configured title returned for matching handle; empty for zero handle; empty for mismatched handle.

  </details>

- [x] 1.3 Extend `MacroDefinition` and `MacroStep` data models (REQ-2, REQ-3, RISK-6) `S`
  <details><summary>Spec</summary>

  - **`MacroPositionMode` enum** (in `MacroModels.cs`): `Absolute = 0`, `WindowRelative = 1`. Default `Absolute` ensures backward compatibility (missing JSON field → 0).
  - **`MacroDefinition`**: add `MacroPositionMode PositionMode { get; init; }` (default `Absolute`), `int WindowWidth { get; init; }`, `int WindowHeight { get; init; }`, `string WindowTitlePattern { get; init; } = string.Empty`.
  - **`MacroStep`**: add `bool StartFromCursor { get; init; }` (default `false`). Add `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]` to avoid serializing `false` on every step.
  - **`MacrosFile.Version`**: change default from `1` to `2`. Existing v1 files still load (no structural change needed).

  </details>

- [x] 1.4 Extend `MacroStore` validation for window-relative macros (REQ-4, REQ-14) [after: 1.3] `M`
  <details><summary>Spec</summary>

  - **Version handling**: accept version 1 and 2. Version > 2 → quarantine with error (same pattern as existing unknown-version handling, if any; otherwise add). `Save()` always writes `Version = 2`.
  - **`WindowRelative` validation** (in `ValidateSlot`):
    - `PositionMode == WindowRelative` requires: `WindowWidth > 0`, `WindowHeight > 0`, `WindowTitlePattern` non-null and non-empty.
    - Coordinate bounds: `step.X >= 0 && step.X < def.WindowWidth` and `step.Y >= 0 && step.Y < def.WindowHeight` (instead of `ScreenWidth`/`ScreenHeight`).
    - **Exception**: when `step.StartFromCursor == true` (DragDrop only), skip X/Y start coordinate validation — these are vestigial. Still validate `EndX`/`EndY` against window bounds.
    - `DragDrop` with `EndX`/`EndY`: same window-relative bounds check.
  - **`StartFromCursor` validation**: only valid on `DragDrop` steps. `StartFromCursor == true` on non-DragDrop → error, quarantine.
  - **`Absolute` validation**: existing checks unchanged — uses `ScreenWidth`/`ScreenHeight`.
  - Dispatch validation based on `PositionMode` in a helper method: `ValidateAbsoluteStep` / `ValidateWindowRelativeStep`.

  </details>

- [x] 1.5 Unit tests for extended MacroStore validation (REQ-4, REQ-14) [after: 1.4] `M`
  <details><summary>Spec</summary>

  - In `MacroStoreTests.cs`:
    - v1 file loads correctly, defaults to `Absolute`.
    - v2 file with `WindowRelative` fields loads correctly.
    - `WindowRelative` macro with missing `WindowTitlePattern` → quarantined.
    - `WindowRelative` macro with coordinates exceeding `WindowWidth`/`WindowHeight` → quarantined.
    - `StartFromCursor` on non-DragDrop step → quarantined.
    - `StartFromCursor` on DragDrop step → valid.
    - Version > 2 → quarantined with error.

  </details>

- [x] 1.6 `dotnet format` + build clean (all REQs) [after: 1.5] `S`

## Phase 2: Recording Flow
<!-- worktree: -->

- [ ] 2.1 Pass app-scope context to `MacroRecorder` at recording start (REQ-5, REQ-6, REQ-18, RISK-1, RISK-2) [after: 1.6] `M`
  <details><summary>Spec</summary>

  - **`MacroRecorder`**: add `StartRecording(MacroRecordingContext context)` overload. `MacroRecordingContext` is a new record:
    ```csharp
    public sealed record MacroRecordingContext(
        bool IsWindowRelative,
        int WindowWidth,    // 0 for Absolute
        int WindowHeight,   // 0 for Absolute
        string WindowTitlePattern,  // empty for Absolute
        double DpiScale
    );
    ```
    Note: no `WindowBounds` in the context — coordinator owns the transform (pre-transforms coordinates before calling `RecordAction`). Recorder stores whatever coordinates it receives.
  - Context stored as `_recordingContext` field on `StartRecording()` (before `AwaitSlot`). Threaded through to `BeginRecording()` without additional parameter — `_recordingContext` is already a field.
  - Existing parameterless `StartRecording()` continues to work (constructs context with `IsWindowRelative = false` using screen bounds from `_screen`).
  - **`StopRecording()`** populates `MacroDefinition` based on context:
    - `WindowRelative`: `PositionMode = WindowRelative`, `WindowWidth`, `WindowHeight`, `WindowTitlePattern`, `DpiScale` from context. `ScreenWidth`/`ScreenHeight` set to 0 (not used for window-relative; avoids confusing absolute-mode validation if someone inspects the JSON).
    - `Absolute`: existing behavior — `ScreenWidth`/`ScreenHeight` from screen bounds, `PositionMode = Absolute`.
  - **`NavigatorCoordinator`**: at recording start, if `_appScoped`:
    - Get window title: `_platform.ForegroundWindow.GetWindowTitle(_preOverlayHwnd)`.
    - **Empty title guard** (REQ-18): if title is empty, show status text "Window has no title — cannot record", abort recording start, return.
    - **Extract title pattern**: find last " - " separator → take substring after it as pattern. If no separator, use full title. E.g., `"Inbox - user@example.com - Outlook"` → `"Outlook"`.
    - Build context with `IsWindowRelative = true`, `WindowWidth = _appScopeBounds.Width`, `WindowHeight = _appScopeBounds.Height`, DPI from `_platform.Screen.GetDpiScale()`.
  - If not `_appScoped`: existing flow, context with `IsWindowRelative = false`.

  </details>

- [ ] 2.2 Recording resume preserves app-scope (REQ-7, REQ-19, REQ-20, RISK-3, RISK-9) [after: 2.1] `M`
  <details><summary>Spec</summary>

  - **`ResumeOverlayForRecording()`** in `NavigatorCoordinator.cs`:
    - Add field `_recordingAppScoped` (bool) and `_recordingWindowBounds` (Rectangle) — captured at recording start from `_appScoped` / `_appScopeBounds`.
    - On resume: if `_recordingAppScoped`:
      1. Re-query window bounds via stored `_preOverlayHwnd`: `_platform.ForegroundWindow.GetWindowBounds(_preOverlayHwnd)`. Uses the pre-captured HWND — never calls `GetForegroundWindowHandle()` during resume (avoids race with notifications/focus steal).
      2. **HWND invalid / empty bounds** (REQ-20): window was closed → auto-stop recording: call `_macroRecorder.StopRecording()`, save macro with steps captured so far, show tray notification "Window closed — recording saved". Do not cancel/discard.
      3. If bounds have **different size** → cancel recording with tray notification "Window resized during recording".
      4. If bounds just **moved** (same size, different position) → update `_recordingWindowBounds` to new bounds. Offsets are frame-independent — previously recorded steps remain valid.
      5. Clamp origin into new bounds.
      6. Activate session with window bounds (not screen bounds).
      7. Show app-scope border.
      8. Set `_appScoped = true`, `_appScopeBounds = newBounds`.
    - If not `_recordingAppScoped`: existing behavior (full-screen).
  - **`SuspendOverlayForAction()`**: no change needed — it already tears down session + hides overlay. `_recordingAppScoped`, `_recordingWindowBounds`, and `_preOverlayHwnd` survive suspension (fields, not tied to session).
  - **`ResetOverlayForDrag()` during window-relative recording** (REQ-19, RISK-9): when `_recordingAppScoped`, create the new drag-phase session scoped to `_recordingWindowBounds` instead of full-screen. Set `_appScoped = true`, `_appScopeBounds = _recordingWindowBounds`, show app-scope border. This prevents drag endpoints from falling outside the window bounds.

  </details>

- [ ] 2.3 Coordinate transformation for window-relative recording (REQ-8, RISK-4) [after: 2.2] `M`
  <details><summary>Spec</summary>

  - **Coordinator owns the transform**: `NavigatorCoordinator.OnSessionActionRequested()` pre-transforms the action point before calling `MacroRecorder.RecordAction()`:
    - If `_recordingAppScoped`: `transformedX = point.X - _recordingWindowBounds.Left`, `transformedY = point.Y - _recordingWindowBounds.Top`.
    - Otherwise: pass `point.X`, `point.Y` unchanged (existing behavior).
  - This avoids propagating bounds to the recorder or needing an `UpdateBounds()` API on the recorder. Recorder stores whatever coordinates it receives.
  - Same transform applied to scroll step coordinates in `RecordScroll()`.
  - Same transform for DragDrop endpoints: both start and end points transformed by coordinator before passing to `RecordAction`.
  - **`StopRecording()`**: populate `MacroDefinition` with context:
    - `WindowRelative`: `PositionMode = WindowRelative`, `WindowWidth`, `WindowHeight`, `WindowTitlePattern`, `DpiScale` from `_recordingContext`. `ScreenWidth`/`ScreenHeight` set to 0 (not applicable).
    - `Absolute`: existing behavior — `ScreenWidth`/`ScreenHeight` from screen bounds, `PositionMode = Absolute`.

  </details>

- [ ] 2.4 `StartFromCursor` prompt for DragDrop during recording (REQ-9, RISK-5) [after: 2.3] `M`
  <details><summary>Spec</summary>

  - Only offered in `WindowRelative` mode (absolute macros have no concept of "current cursor" — positions are fixed).
  - **`MacroRecorder`**: new state `AwaitStartFromCursorConfirm` inserted between DragDrop completion and step finalization.
    - After a DragDrop step is fully paired (start + end resolved), if `_recordingContext.IsWindowRelative`:
      - Store the completed step in `_pendingStartFromCursorStep`.
      - Transition to `AwaitStartFromCursorConfirm`.
      - Fire new event `StartFromCursorRequested`.
    - New method `OnStartFromCursorResponse(bool confirmed)`:
      - If confirmed: rebuild step with `StartFromCursor = true` (use `with`-expression or new constructor — `MacroStep` has `init` properties, do not make mutable).
      - Add step to `_steps`. Clear pending. Return to `Recording`.
  - **`MacroRecorderState`**: add `AwaitStartFromCursorConfirm`.
  - **State transitions from `AwaitStartFromCursorConfirm`**:
    - **Y key**: confirm → step saved with `StartFromCursor = true` → return to `Recording`.
    - **N key**: decline → step saved with `StartFromCursor = false` → return to `Recording`.
    - **Escape**: decline (save step without `StartFromCursor`) → cancel entire recording (same as Escape from `Recording`).
    - **Unrelated keys**: silently ignored (same pattern as `AwaitOverwrite`).
    - **Focus loss**: coordinator's `OnFocusLost` is suppressed during `_macroState != Idle` — no change needed.
  - **Key dispatch priority**: in `OnKeyEvent`, add check after existing recording key handling (position 4 in chain): `if (_macroRecorder.State == MacroRecorderState.AwaitStartFromCursorConfirm)` → handle Y/N/Escape, consume all other keys.
  - **`NavigatorCoordinator`**: subscribe to `StartFromCursorRequested` → show status text "Drag from cursor? [Y/N]".
  - Since `MacroStep` uses `init` properties, create a new step with `StartFromCursor = true` (copy with-expression or constructor). Do not make properties mutable.

  </details>

- [ ] 2.5 Unit tests for recording flow (REQ-5, REQ-6, REQ-7, REQ-8, REQ-9, REQ-18, REQ-19, REQ-20) [after: 2.4] `L`
  <details><summary>Spec</summary>

  - **`MacroRecorderTests.cs`**:
    - Recording with `IsWindowRelative = true` context → `StopRecording()` produces `MacroDefinition` with `PositionMode = WindowRelative`, correct `WindowWidth`, `WindowHeight`, `WindowTitlePattern`.
    - Recording with `IsWindowRelative = false` → `PositionMode = Absolute` (existing behavior).
    - DragDrop in window-relative mode → `StartFromCursorRequested` fires; Y response → step has `StartFromCursor = true`; N → `false`.
    - DragDrop in absolute mode → no `StartFromCursorRequested` event.
    - `AwaitStartFromCursorConfirm` + Escape → recording cancelled, step not saved.
    - `AwaitStartFromCursorConfirm` + unrelated key → ignored, still in `AwaitStartFromCursorConfirm`.
  - **`NavigatorCoordinatorTests.cs`** (or `MacroRecordingIntegrationTests.cs`):
    - App-scoped recording start → context is `WindowRelative` with correct window metadata.
    - App-scoped recording start with empty window title → recording rejected, status text shown (REQ-18).
    - Full-screen recording start → context is `Absolute`.
    - Recording resume in app-scope → overlay re-opens scoped to window (REQ-7).
    - Recording resume when window moved (same size) → bounds updated, recording continues.
    - Recording resume when window resized → recording cancelled.
    - Recording resume when HWND invalid (window closed) → auto-stop + save (REQ-20).
    - Drag recording in app-scope → `ResetOverlayForDrag` preserves app-scope bounds (REQ-19).
    - Coordinate transformation: action point at (500, 300) with window at (100, 50) → recorded as (400, 250).

  </details>

- [ ] 2.6 `dotnet format` + build clean [after: 2.5] `S`

## Phase 3: Playback Flow
<!-- worktree: -->

- [ ] 3.1 Window validation for `WindowRelative` playback (REQ-10, REQ-13, REQ-17, REQ-21, RISK-2, RISK-7, RISK-10) [after: 2.6] `M`
  <details><summary>Spec</summary>

  - **`PlaybackResultKind`** (REQ-21): add `WindowMismatch`, `CoordinateOutOfBounds`, `WindowDrift`. Update all `switch`/pattern-match consumers: coordinator (post-playback handler), `MacroPlaybackOverlay` (display), all existing tests.
  - **`MacroPlayer`**: extend constructor or `Play()` to accept a `PlaybackContext` record:
    ```csharp
    public sealed record PlaybackContext(
        MacroPositionMode PositionMode,
        Rectangle WindowBounds,     // Empty for Absolute
        string WindowTitle,         // empty for Absolute
        nint WindowHwnd,            // 0 for Absolute — for drift checks
        Point InitialCursorPosition // for StartFromCursor steps
    );
    ```
  - Also inject `IForegroundWindowProvider` into `MacroPlayer` for per-step drift checks.
  - **At `Play()` start**, if `macro.PositionMode == WindowRelative`:
    - Validate: `macro.WindowTitlePattern` non-empty.
    - Check `context.WindowTitle.Contains(macro.WindowTitlePattern, StringComparison.OrdinalIgnoreCase)` → mismatch → `PlaybackResult.WindowMismatch("Window title '...' does not match pattern '...'")`.
    - Check `context.WindowBounds.Width == macro.WindowWidth && .Height == macro.WindowHeight` → mismatch → `PlaybackResult.WindowMismatch("Window size ...")`.
    - DPI check: `Math.Abs(dpi - macro.DpiScale) > 0.01` → mismatch (epsilon tolerance for floating-point, REQ-10).
  - If `macro.PositionMode == Absolute`: existing screen validation, unchanged (keep existing `Math.Abs(dpi - macro.DpiScale) > 0.001` or unify to 0.01).
  - **`NavigatorCoordinator`** (playback entry points — picker, helper key, direct slot, global hotkey):
    - **Pre-capture HWND** (RISK-7): for picker path, use `_preOverlayHwnd` (captured before overlay shown). For global hotkey path, capture foreground HWND before overlay in `OnHotKeyActivated`. Never query foreground after picker/overlay is visible.
    - If macro is `WindowRelative`: get bounds + title from pre-captured HWND → build `PlaybackContext`. If HWND is 0 or bounds empty → reject with tray notification.
    - Capture `InitialCursorPosition` from `_platform.Cursor.GetCursorPosition()` at playback start.
    - If `Absolute`: existing flow, `PlaybackContext` with empty bounds/title.

  </details>

- [ ] 3.2 Coordinate resolution + per-step drift check during `WindowRelative` playback (REQ-11, REQ-17, REQ-21, RISK-4, RISK-8) [after: 3.1] `M`
  <details><summary>Spec</summary>

  - **`MacroPlayer.ExecuteStep()`**: resolve coordinates before executing:
    - If `_positionMode == WindowRelative`: `screenX = windowBounds.Left + step.X`, `screenY = windowBounds.Top + step.Y`.
    - Bounds check: `screenPoint` must be within `windowBounds`. If not → throw `InvalidOperationException("Resolved coordinate ... outside window bounds")` → caught by `Play()` → return `PlaybackResult.CoordinateOutOfBounds(message)`.
    - Same transform for `EndX`/`EndY` on DragDrop steps.
    - Add `CoordinateOutOfBounds` to `PlaybackResultKind` enum (done in 3.1).
  - **Per-step drift check** (REQ-17, RISK-8): before each action step in `WindowRelative` mode:
    1. Re-query foreground HWND via `IForegroundWindowProvider.GetForegroundWindowHandle()`.
    2. If HWND differs from `_context.WindowHwnd` → return `PlaybackResult.WindowDrift("Target window lost focus")`.
    3. Re-query window bounds via `IForegroundWindowProvider.GetWindowBounds(_context.WindowHwnd)`.
    4. If bounds size changed → return `PlaybackResult.WindowDrift("Window resized during playback")`.
    5. If bounds position changed → update working `windowBounds` for coordinate resolution. Position drift is safe (offsets remain valid).
  - **`Absolute` mode**: no transform, no drift check, existing behavior.
  - Click indicator coordinates also resolved (show indicator at screen position, not offset).

  </details>

- [ ] 3.3 `StartFromCursor` playback for DragDrop (REQ-12) [after: 3.2] `S`
  <details><summary>Spec</summary>

  - **`MacroPlayer.ExecuteStep()`**: for `DragDrop` with `StartFromCursor == true`:
    - Drag start = `_context.InitialCursorPosition` (captured at macro playback start, not live cursor — avoids mid-macro cursor movement from previous steps overriding the user's intended position).
    - Validate `InitialCursorPosition` within window bounds → if not, abort with `PlaybackResult.CoordinateOutOfBounds("Cursor not within target window")`.
    - Drag end = resolved window-relative `EndX`/`EndY` (same as 3.2).
    - Click indicator shown at `InitialCursorPosition` (start) before drag.
  - No additional injection needed — `InitialCursorPosition` is already in `PlaybackContext` from step 3.1.

  </details>

- [ ] 3.4 Unit tests for playback flow (REQ-10, REQ-11, REQ-12, REQ-13, REQ-17, REQ-21) [after: 3.3] `L`
  <details><summary>Spec</summary>

  - **`MacroPlayerTests.cs`**:
    - `WindowRelative` macro + matching window → steps execute at resolved screen coordinates.
    - Title mismatch → `WindowMismatch` result.
    - Size mismatch → `WindowMismatch` result.
    - DPI mismatch (beyond epsilon 0.01) → `WindowMismatch` result.
    - DPI within epsilon → passes.
    - Resolved coordinate outside window → `CoordinateOutOfBounds`.
    - `StartFromCursor` DragDrop → drag start = `InitialCursorPosition`, drag end = resolved offset.
    - `StartFromCursor` with cursor outside window → `CoordinateOutOfBounds`.
    - Per-step drift: foreground HWND changes mid-playback → `WindowDrift`.
    - Per-step drift: window resized mid-playback → `WindowDrift`.
    - Per-step drift: window moved (same size) → coordinates resolve against new position, playback continues.
    - `Absolute` macro → existing behavior (screen validation, no offset transform, no drift check).
  - **Integration tests** (`MacroPlaybackIntegrationTests.cs`):
    - Full flow: coordinator triggers `WindowRelative` playback via helper key on app-scoped window → validates → plays → mouse actions at resolved coordinates.
    - Global hotkey `WindowRelative` playback → pre-captured HWND used (not foreground after overlay).
    - Picker-path playback → pre-captured HWND survives picker focus change.

  </details>

- [ ] 3.5 `dotnet format` + build clean [after: 3.4] `S`

## Phase 4: UI & Polish
<!-- worktree: -->

- [ ] 4.1 Picker shows position mode badge (REQ-15) [after: 3.5] `S`
  <details><summary>Spec</summary>

  - **`MacroPickerOverlay`**: for each non-empty slot, append `[W]` if `PositionMode == WindowRelative`, `[S]` if `Absolute`, after the macro name.
  - Update `IMacroPickerWindow` interface if needed to pass `MacroDefinition` (currently passes name + slot — may need full definition or just the position mode).

  </details>

- [ ] 4.2 Playback overlay shows window context (REQ-16) [after: 3.5] `S`
  <details><summary>Spec</summary>
+ embedded resource (REQ-2, REQ-3, REQ-4) [after: 1.3] `S`
  <details><summary>Spec</summary>

  - Add `positionMode` enum (`"absolute"`, `"windowRelative"`) to `MacroDefinition` schema.
  - Add `windowWidth`, `windowHeight`, `windowTitlePattern` to `MacroDefinition`.
  - Add `startFromCursor` (boolean, + embedded resource (REQ-2, REQ-3, REQ-4) [after: 1.3] `S`
  <details><summary>Spec</summary>

  - Add `positionMode` enum (`"absolute"`, `"windowRelative"`) to `MacroDefinition` schema.
  - Add `windowWidth`, `windowHeight`, `windowTitlePattern` to `MacroDefinition`.
  - Add `startFromCursor` (boolean, default false) to `MacroStep`.
  - Bump schema description to note v2.
  - Verify the embedded resource `Resources\macros.schema.json` in `Klikety.csproj` references the updated file. `FirstRunExtractor` always-overwrites schemas on startup — no additional wiring needed, but the embedded resource must contain the v2 schemafault false) to `MacroStep`.
  - Bump schema description to note v2.

  </details>

- [ ] 4.4 Update design notes (all REQs) [after: 4.2] `M`
  <details><summary>Spec</summary>

  - **`macros.design.md`**: document `MacroPositionMode`, window-relative recording flow, coordinate transformation, `StartFromCursor`, playback validation, recording resume in app-scope.
  - **`win32-interop.design.md`**: document `GetWindowTextW` / `GetWindowTextLengthW` P/Invoke.
  - **`testing.design.md`**: update fake list with `FakeForegroundWindowProvider.Title`.

  </details>


## Known Limitations

- **Multi-monitor DPI**: `GetDpiScale()` returns primary monitor DPI. Window on secondary monitor with different scaling → DPI validation may pass incorrectly. Documented, not solved.
- **UWP/Store app titles**: `GetWindowTextW` may return empty for some UWP apps. Recording blocked by REQ-18; playback fails on empty pattern. Workaround: none (re-record once Microsoft surfaces title properly).
- **Tray notification routing**: playback failures (`WindowMismatch`, `CoordinateOutOfBounds`, `WindowDrift`) are surfaced via `PlaybackResult` to the coordinator. The coordinator's existing post-playback handler routes to tray notifications via `App.xaml.cs` wiring. No new notification abstraction introduced — follows existing pattern.
- [ ] 4.5 `dotnet format` + full test run + build clean [after: 4.4] `S`
