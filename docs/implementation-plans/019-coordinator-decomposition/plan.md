# 019: NavigatorCoordinator Decomposition

## Decisions

- **Composition via delegation**: NavigatorCoordinator holds references to extracted helper classes and calls their methods. No event-based decoupling between coordinator and helpers.
- **Namespace**: Extracted classes live in `Klikety.Navigation` (same as `IModeSession`, `ModeSessionFactory`, etc.).
- **File placement**: New files in `src/Klikety/Navigation/` alongside existing navigation types.
- **No functional changes**: Pure structural refactor. All existing behavior preserved exactly.
- **MacroHandler owns macro state**: `MacroState` enum (namespace-level type in `MacroHandler.cs`), `MacroRecorder`, `MacroPlayer`, picker/hotkey refs, playback/recording state, all macro event handlers. Coordinator queries `MacroHandler.State` for dispatch decisions.
- **Overlay visibility exclusively owned by coordinator**: MacroHandler never calls `_overlayWindow.Show()` or `_overlayWindow.Hide()` directly. Instead it fires intent events (`SuspendOverlayRequested`, `ResumeOverlayRequested`, `DeactivateRequested`) that the coordinator handles — preserving single-owner overlay and HWND control. MacroHandler stores its own target HWND once at recording/playback start (passed by coordinator), decoupled from coordinator's `_preOverlayHwnd` lifecycle.
- **SessionManager owns session lifecycle + scope state**: The repeated pattern of unsubscribe → deactivate → clear canvas → create → subscribe → activate appears 6× in the coordinator. `SessionManager` encapsulates this. Also owns `_origin`, `_screenBounds`, `_appScoped`, `_appScopeBounds` as session-level scope, exposed via read-only properties.
- **ActionDispatcher owns drag state**: `_dragMode`, `_dragStartPoint`, action branching logic. Calls `SessionManager` and queries `MacroHandler` for recording state. Reads `ActiveBounds` and `Origin` from `SessionManager`.
- **DebounceHandler is a debounce controller**: Owns `_debounceKeys` HashSet, timer, and platform key-state queries for reconciliation. Full lifecycle: key-state population, set management, timer-based reconciliation, disposal.
- **Disposal order**: `_macroHandler.Dispose()` (cancel CTS + drain playback task + unsubscribe) → `_sessionManager.Dispose()` (unsubscribe active session events) → `_debounce.Dispose()` → coordinator-level `DeactivateOverlay()` + close overlay.
- **Test split mirrors module split**: Tests that exercise only debounce, drag, or mode-switching mechanics move to new test files named by behavior: `CoordinatorDebounceTests.cs`, `CoordinatorDragTests.cs`, `CoordinatorModeSwitchingTests.cs`. Integration tests exercising full hotkey → action flow stay in `NavigatorCoordinatorTests.cs`. A shared `CreateCoordinator()` helper is extracted to a common fixture before any test moves.
- **Log declarations move with methods**: Each `[LoggerMessage]` partial moves to the class that contains the calling method. Classes that need logging become `partial` with their own log methods.
- **Macro integration tests unchanged**: `MacroRecordingIntegrationTests.cs`, `MacroPlaybackIntegrationTests.cs`, `MacroPickerIntegrationTests.cs` already test macro coordinator behavior. They continue constructing `NavigatorCoordinator` and exercising it end-to-end; no split needed.
- **Macro service setter idempotency preserved**: `MacroHotKeyService` and `MacroPickerWindow` setters on `MacroHandler` preserve the idempotent detach-then-attach pattern from current coordinator.
- **Dead code removal**: `OnSessionCancelledDuringRecording()` (unreferenced private method) deleted in Phase 4.

## Requirements

| ID | Requirement | Acceptance Criteria | Phases/Steps |
|----|-------------|---------------------|--------------|
| REQ-1 | Extract `DebounceHandler` from coordinator | `DebounceHandler` class in `Klikety.Navigation` with `Add`, `Remove`, `Contains`, `Clear`, `StartTimer`, `StopAndDispose` methods. Coordinator calls these instead of direct `_debounceKeys` / `_debounceTimer` manipulation. | 1.1, 1.2, 1.3 |
| REQ-2 | Extract `SessionManager` from coordinator | `SessionManager` class manages `_activeSession`, `_switching`, `_modeLocked`, `_currentModeName`. Exposes `ActivateSession`, `SwitchMode`, `SwitchToAppScope`, `DeactivateSession`, `ResetForDrag`, `ResumeForRecording`, `ResumeAfterPlayback`. Forwards `ActionRequested`, `Cancelled`, `CursorMoveRequested` events. | 2.1, 2.2, 2.3 |
| REQ-3 | Extract `ActionDispatcher` from coordinator | `ActionDispatcher` class owns `_dragMode`, `_dragStartPoint`. Handles `OnSessionActionRequested` logic: bounds checking, recording intercept, drag phases, normal dispatch. | 3.1, 3.2, 3.3 |
| REQ-4 | Extract `MacroHandler` from coordinator | `MacroHandler` class owns `MacroState`, recorder, player, picker/hotkey refs, playback/recording state, all macro event handlers. Exposes `TryHandleKey` for key dispatch, `StartRecording`, `ShowPicker`, `StartDirectPlayback`. | 3.1, 3.2, 3.3 |
| REQ-5 | Slim NavigatorCoordinator to ~300 lines | Coordinator retains: constructor (wiring), `OnHotKeyActivated`, `OnKeyEvent` (dispatch router), `OnFocusLost`, `OnSessionCancelled`, `OnSessionCursorMoveRequested`, `DeactivateOverlay`, `Dispose`, `GetDefaultModeName`, `IsQwertyLayout`, `ExtractTitlePattern`, overlay Show/Hide handlers for macro intent events. All logic delegated to helpers. | 4.1, 4.2 |
| REQ-6 | Split `NavigatorCoordinatorTests.cs` to match modules | Debounce tests → `CoordinatorDebounceTests.cs`. Drag tests → `CoordinatorDragTests.cs`. Mode-switching tests → `CoordinatorModeSwitchingTests.cs`. Action dispatch tests → `CoordinatorDragTests.cs`. Core lifecycle/integration tests stay in `NavigatorCoordinatorTests.cs`. Shared `CreateCoordinator()` helper extracted to `CoordinatorTestHelper.cs` before any moves. | 1.1, 1.2, 2.2, 3.2, 4.2 |
| REQ-7 | All 64 existing coordinator tests pass | `dotnet test` passes with zero failures after each phase. Test infrastructure refactoring allowed (shared helper extraction, file moves). No test assertion logic changes. | 1.0, 1.3, 2.3, 3.3, 4.2 |
| REQ-8 | Zero build warnings after each phase | `dotnet build` produces zero warnings. Source-generated `[LoggerMessage]` partials move correctly. `CA1859` suppression moves with `_activeSession` field. | 1.3, 2.3, 3.3, 4.2 |
| REQ-9 | Update design notes | `state-machine.design.md` and `macros.design.md` updated to reflect new file locations and class responsibilities. | 4.3 |

## Risks

| ID | Risk | Likelihood | Impact | Mitigation | Steps |
|----|------|------------|--------|------------|-------|
| RISK-1 | Circular dependencies between extracted classes | Medium | High | Design interfaces/callbacks for cross-helper communication rather than direct refs. `ActionDispatcher` receives `SessionManager` and `MacroHandler` via constructor — one-way dependency graph: Coordinator → {SessionManager, MacroHandler, ActionDispatcher, DebounceHandler}, ActionDispatcher → {SessionManager, MacroHandler}, MacroHandler → {SessionManager, IKeyboardHookService}. No cycles. | 2.1, 3.1 |
| RISK-2 | Event subscription ordering breaks during Dispose | Medium | Medium | Exact disposal order: `_macroHandler.Dispose()` (cancel CTS + drain playback + unsubscribe recorder) → `_sessionManager.Dispose()` (unsubscribe session events) → `_debounce.Dispose()` → coordinator `DeactivateOverlay()` + close. `SessionManager` and `MacroHandler` implement `IDisposable`. | 3.1, 4.1 |
| RISK-3 | Macro integration tests break due to changed internal wiring | Low | Medium | Macro integration tests construct `NavigatorCoordinator` directly — public API unchanged. Internal delegation is invisible to tests. Run full test suite after each phase. | 3.3 |
| RISK-4 | `SessionManager` event forwarding introduces double-subscription bugs | Medium | Medium | `SessionManager` always unsubscribes old session synchronously before any transition (deactivation or new session creation). Coordinator subscribes to `SessionManager` events once in constructor. No raw session event access from coordinator. | 2.1, 2.3 |
| RISK-5 | HWND dual-owner between coordinator and MacroHandler | Medium | High | Overlay Show/Hide exclusively owned by coordinator. MacroHandler fires intent events; coordinator handles actual visibility. MacroHandler stores its own target HWND copy at recording/playback start, independent of coordinator's `_preOverlayHwnd`. | 3.1, 3.3 |

## Phase 1: Extract DebounceHandler
<!-- worktree: feature/019-coordinator-decomposition -->

Smallest, most isolated extraction. No dependencies on other helpers.

- [x] 1.0 Extract shared test helper (REQ-6) `S`
  - Extract `CreateCoordinator()` from `NavigatorCoordinatorTests.cs` into `CoordinatorTestHelper.cs` as an `internal static` class
  - All existing test files (`NavigatorCoordinatorTests.cs`, `AppScopeCoordinatorTests.cs`, macro integration tests) use the shared helper
  - `dotnet test` — all tests pass with no helper duplication

- [x] 1.1 Create `DebounceHandler` class (REQ-1, REQ-8) [after: 1.0] `S`
  - File: `src/Klikety/Navigation/DebounceHandler.cs`
  - Constructor takes `IPlatformServices`, `ConfigModel` (for `HotKey` modifiers/key)
  - Methods: `PopulateFromHotKey()` (current `PopulateDebounceKeys`), `StartTimer()` (current `StartDebounceTimer`), `Contains(VKey)`, `Remove(VKey)`, `Clear()`, `StopAndDispose()`, `OnTimerElapsed()` (current `OnDebounceTimerElapsed` reconciliation)
  - Internal state: `HashSet<VKey> _keys`, `IDebounceTimer? _timer`
  - Implements `IDisposable` for timer cleanup
  - No `[LoggerMessage]` needed — debounce has no log calls currently

- [x] 1.2 Move debounce tests to `CoordinatorDebounceTests.cs` (REQ-6) [after: 1.0] `S`
  - Tests to move (8 tests): `Debounce_TriggerKeySuppressedUntilReleased`, `Debounce_ModifierKeySuppressedWhenHeld`, `Debounce_KeyUpRemovesFromDebounceSet`, `Debounce_DifferentKeyRemovesTrigger`, `Debounce_TimerReconciles`, `Debounce_MultipleModifiers_AllSuppressed`, `Debounce_KeyUpThenDown_SecondDownProcessed`, `DebounceTimer_ReconcilesClearedKeys`
  - Use shared `CoordinatorTestHelper.CreateCoordinator()`. Tests still exercise debounce through coordinator's public surface.
  - `DeactivateOverlay_ClearsDebounceTimer` stays in coordinator tests (tests `DeactivateOverlay` behavior).

- [x] 1.3 Wire `DebounceHandler` into coordinator, remove inline debounce code (REQ-1, REQ-7, REQ-8) [after: 1.1, 1.2] `S`
  - Replace `_debounceKeys` HashSet and `_debounceTimer` fields with `DebounceHandler _debounce`
  - Replace `PopulateDebounceKeys()` → `_debounce.PopulateFromHotKey()`
  - Replace `StartDebounceTimer()` → `_debounce.StartTimer()`
  - Replace `_debounceKeys.Contains(key)` → `_debounce.Contains(key)`
  - Replace `_debounceKeys.Remove(key)` → `_debounce.Remove(key)`
  - Replace debounce cleanup in `DeactivateOverlay` → `_debounce.StopAndDispose()`
  - Remove `CheckAndAddDebounceKey`, `OnDebounceTimerElapsed` methods
  - `dotnet build` — zero warnings
  - `dotnet test` — all tests pass

## Phase 2: Extract SessionManager
<!-- worktree: -->

Session lifecycle pattern used in 6 places. Most impactful extraction for readability.

- [x] 2.1 Create `SessionManager` class (REQ-2, REQ-8, RISK-1, RISK-4) `M`
  - File: `src/Klikety/Navigation/SessionManager.cs`
  - Constructor takes `ModeSessionFactory`, `IOverlayWindow`, `ILogger`
  - State: `IModeSession? _activeSession`, `bool _switching`, `bool _modeLocked`, `string _currentModeName`, `Point _origin`, `Rectangle _screenBounds`, `bool _appScoped`, `Rectangle _appScopeBounds`
  - Read-only properties: `ActiveSession` (for coordinator's null checks), `IsActive`, `IsSwitching`, `IsModeLocked`, `CurrentModeName`, `Origin`, `ScreenBounds`, `AppScoped`, `AppScopeBounds`, `ActiveBounds` (returns `_appScoped ? _appScopeBounds : _screenBounds`)
  - Implements `IDisposable` (unsubscribes active session events on disposal)
  - Methods:
    - `ActivateDefaultSession(Rectangle bounds, Point origin, string modeName)` — create + subscribe + activate
    - `SwitchMode(string targetModeName, Rectangle activeBounds, Point origin)` — unsubscribe old → clear canvas → create new → subscribe → activate
    - `SwitchToAppScope(Rectangle bounds, Point origin)` — same pattern but sets app-scope border, updates `_appScoped`/`_appScopeBounds`/`_origin`
    - `ResetForDrag(string defaultModeName, Rectangle bounds, Point origin, bool recordingAppScoped)` — current `ResetOverlayForDrag` session lifecycle
    - `ResumeForRecording(string defaultModeName, Rectangle bounds, Point origin, bool appScoped)` — current `ResumeOverlayForRecording` session part
    - `ResumeAfterPlayback(string defaultModeName, Rectangle bounds, Point origin)` — current `ResumeOverlayAfterPlayback` session part
    - `DeactivateSession()` — unsubscribe synchronously first, then deactivate + null. Does NOT call overlay.Hide/ClearCanvas (coordinator owns full `DeactivateOverlay` sequence). Resets `_appScoped`/`_appScopeBounds`.
    - `SetOrigin(Point)` / `SetScreenBounds(Rectangle)` — coordinator sets on activation
    - `LockMode()` / `UnlockMode()` — set `_modeLocked`
    - `ForwardKey(VKey key)` — delegates to `_activeSession.OnKey(key)`
  - Events forwarded: `ActionRequested`, `Cancelled`, `CursorMoveRequested`
  - **Event forwarding invariant**: all transition methods (SwitchMode, SwitchToAppScope, ResetForDrag, etc.) unsubscribe old session synchronously before deactivation or new session creation. Prevents stale event delivery during in-handler transitions.
  - `partial class` with `[LoggerMessage]` for `LogModeSwitching`, `LogModeSwitchFailed`
  - `CA1859` suppression on `_activeSession` moves here
  - Implements `IDisposable`: unsubscribes active session events, deactivates session

- [x] 2.2 Move mode-switching tests to `CoordinatorModeSwitchingTests.cs` (REQ-6) `S`
  - Tests to move (6 tests): `ChordKey_BeforeLock_SwitchesMode`, `ChordKey_AfterModeLock_ForwardedToSession`, `DisabledMode_ChordKeyIgnored`, `ModeLock_NavKeyLocksMode`, `SwitchMode_FactoryThrows_DeactivatesOverlay`, `SwitchMode_OldSessionDeactivated`
  - Use shared `CoordinatorTestHelper.CreateCoordinator()`. Still exercise mode switching through coordinator surface.

- [ ] 2.3 Wire `SessionManager` into coordinator, remove inline session code (REQ-2, REQ-7, REQ-8, RISK-4) [after: 2.1, 2.2] `M`
  - Replace `_activeSession`, `_switching`, `_modeLocked`, `_currentModeName` fields with `SessionManager _sessionManager`
  - Replace all 6 session lifecycle patterns with `SessionManager` method calls
  - Coordinator subscribes to `_sessionManager.ActionRequested` / `.Cancelled` / `.CursorMoveRequested` once in constructor
  - Remove `SwitchMode()`, `SwitchToAppScope()`, `ResetOverlayForDrag()` methods from coordinator
  - Move session parts of `ResumeOverlayForRecording()` and `ResumeOverlayAfterPlayback()` to `SessionManager`
  - `dotnet build` — zero warnings
  - `dotnet test` — all tests pass

## Phase 3: Extract ActionDispatcher and MacroHandler
<!-- worktree: -->

These two are coupled (action dispatch branches on macro state) — extract together to avoid intermediate breakage.

- [ ] 3.1 Create `ActionDispatcher` and `MacroHandler` classes (REQ-3, REQ-4, REQ-8, RISK-1) `L`
  - **`ActionDispatcher`** — File: `src/Klikety/Navigation/ActionDispatcher.cs`
    - Constructor takes `IMouseActionService`, `IModifierDetector`, `IOverlayWindow`, `SessionManager`, `ILogger`, callback `Action` for `DeactivateOverlay`
    - State: `_dragMode`, `_dragStartPoint`
    - Reads `ActiveBounds` and `Origin` from `SessionManager` properties
    - Methods:
      - `HandleAction(Point point, MouseAction action)` — current `OnSessionActionRequested` non-recording path (bounds check, drag phases, normal dispatch)
      - `HandleRecordingAction(Point point, MouseAction action, MacroHandler macro)` — current `OnSessionActionRequested` recording path
      - `CancelDrag()` — drag cleanup, returns `bool` indicating whether status text needs clearing
      - `IsDragMode` — read-only property for coordinator's `OnSessionCancelled`
    - `partial class` with `[LoggerMessage]` for `LogActionRequested`, `LogActionOutOfBounds`, `LogDragInvalidAction`
  - **`MacroHandler`** — File: `src/Klikety/Navigation/MacroHandler.cs`
    - `MacroState` enum defined as namespace-level type in this file (not nested)
    - Constructor takes `ConfigModel`, `IPlatformServices`, `IKeyboardHookService`, `IMouseActionService`, `SessionManager`, `ILogger`, `IMacroStore?`, `MacrosFile?`
    - Does NOT receive `IOverlayWindow` — fires intent events instead (see below)
    - State: `MacroState`, `MacroRecorder?`, `MacroPlayer?`, picker/hotkey refs, playback/recording fields, `_slotKeyMap`, `_resumeTimer`, `_recordingAppScoped`, `_recordingWindowBounds`, `_targetHwnd` (own HWND copy, independent of coordinator's `_preOverlayHwnd`)
    - Properties: `State` (current `_macroState`), `MacroHotKeyService` (setter with idempotent detach-then-attach event wiring), `MacroPickerWindow` (setter with idempotent detach-then-attach event wiring), `MacroPlaybackWindow`, `ClickIndicator`, `DelayProvider`
    - Methods:
      - `TryHandleKey(VKey key, nint preOverlayHwnd)` → `bool` — returns true if key consumed by macro subsystem (recording control, record key, helper key, slot key)
      - `IsPlayingOrRecording()` — for focus-loss guard
      - `StartRecording(...)`, `CancelRecording()`, `HandleRecordingKey(VKey)` — recording lifecycle
      - `ShowMacroPicker()`, `StartPlayback(...)`, `StartDirectPlayback(...)` — picker/playback lifecycle
      - `ResumeOverlayForRecording()` — performs HWND re-validation, window-resize detection, window-move adaptation, origin clamping, then fires `ResumeOverlayRequested` for coordinator to call Show(); delegates session creation to `SessionManager.ResumeForRecording()`
      - `Dispose()` — cancel CTS, drain playback task synchronously, unsubscribe recorder events. Implements `IDisposable`.
    - Dependencies: `IKeyboardHookService` for `Enable()`/`Disable()` during playback/picker (dual-owner hook control, mutual exclusion via `MacroState`)
    - `partial class` with `[LoggerMessage]` for all macro-related log methods
    - Events:
      - `SuspendOverlayRequested` — fires when recording needs overlay hidden for action execution
      - `ResumeOverlayRequested` — fires when recording needs overlay re-shown (after HWND validation)
      - `DeactivateRequested` — fires when macro handler needs coordinator to run `DeactivateOverlay`
      - `ShowStatusTextRequested(string text)` — fires when macro handler needs to show status text on overlay
      - `ClearStatusTextRequested` — fires when macro handler needs to clear status text
      - `SetRecordingBorderRequested(bool show)` — fires for recording border changes

- [ ] 3.2 Move drag and action dispatch tests to `CoordinatorDragTests.cs` (REQ-6) `S`
  - Tests to move (10 drag tests): `DragDrop_StartsPhase_ResetsOverlay_ShowsStatusText`, `DragDrop_SecondAction_LeftClick_SendsDrag`, `DragDrop_SecondAction_RightClick_SendsRightDrag`, `DragDrop_SecondAction_WithModifiers_PassesModifiers`, `DragDrop_Escape_AbortsDrag_RestoresCursorToOrigin`, `DragDrop_MoveOnlyInDragMode_Ignored`, `DragDrop_DragDropInDragMode_Ignored`, `DragDrop_StatusTextClearedOnCompletion`, `DragDrop_FocusLoss_AbortsDrag_RestoresCursor`, `DragDrop_Completion_NoIntermediateMoveTo_Origin`
  - Also move action dispatch tests (7 tests): `ActionOutOfBounds_Suppressed`, `ActionOutOfBounds_RestoresCursorAndSuppresses`, `MoveOnly_DeactivatesOverlay_NoClick`, `Modifiers_CapturedAndPassedToSendAction`, `MoveOnly_IgnoresModifiers`, `NoModifiers_PassesNoneToSendAction`, `ClickThroughSafety_HideBeforeSendAction`
  - Use shared `CoordinatorTestHelper.CreateCoordinator()`.

- [ ] 3.3 Wire `ActionDispatcher` and `MacroHandler` into coordinator (REQ-3, REQ-4, REQ-7, REQ-8, RISK-1, RISK-3) [after: 3.1, 3.2] `M`
  - Coordinator constructor creates `MacroHandler` and `ActionDispatcher`, passes `SessionManager`
  - `OnKeyEvent` macro dispatch: `if (_macroHandler.TryHandleKey(key, _preOverlayHwnd)) return;`
  - `OnSessionActionRequested` delegates to `_actionDispatcher.HandleAction(...)` or `_actionDispatcher.HandleRecordingAction(..., _macroHandler)`
  - `OnFocusLost` checks `_macroHandler.IsPlayingOrRecording()`
  - Remove all macro fields/methods/event-handlers from coordinator
  - Remove drag fields/action-dispatch methods from coordinator
  - Coordinator `Dispose()` calls `_macroHandler.Dispose()`, `_actionDispatcher` (if disposable), `_debounce.Dispose()`
  - `dotnet build` — zero warnings
  - `dotnet test` — all tests pass (including all macro integration tests)

## Phase 4: Final Cleanup & Verification
<!-- worktree: -->

- [ ] 4.1 Slim coordinator to final form (REQ-5, RISK-2) [after: 3.3] `S`
  - Verify coordinator is ~300 lines: ctor, `OnHotKeyActivated`, `OnKeyEvent`, `OnFocusLost`, `DeactivateOverlay`, `Dispose`, `GetDefaultModeName`, `IsQwertyLayout`, `ExtractTitlePattern`, remaining log declarations
  - Delete dead method `OnSessionCancelledDuringRecording()` (unreferenced)
  - Remove any other dead code, unused usings
  - `dotnet format` + verify zero warnings

- [ ] 4.2 Full test suite verification (REQ-7, REQ-8) [after: 4.1] `S`
  - `dotnet test` — all 64 coordinator tests + all macro integration tests + all other tests pass
  - `dotnet build` — zero warnings across all projects
  - Verify test file distribution:
    - `NavigatorCoordinatorTests.cs` — lifecycle, activation, QWERTY fallback, multi-monitor, factory tests (~25 tests)
    - `CoordinatorDebounceTests.cs` — debounce tests (~8 tests)
    - `CoordinatorModeSwitchingTests.cs` — mode switching tests (~6 tests)
    - `CoordinatorDragTests.cs` — action dispatch + drag tests (~17 tests)
    - `CoordinatorTestHelper.cs` — shared `CreateCoordinator()` helper
    - `AppScopeCoordinatorTests.cs` — unchanged
    - `MacroRecordingIntegrationTests.cs` / `MacroPlaybackIntegrationTests.cs` / `MacroPickerIntegrationTests.cs` — unchanged

- [ ] 4.3 Update design notes (REQ-9) [after: 4.1] `S`
  - `state-machine.design.md`: update globs to include new files, document `SessionManager` and `ActionDispatcher` roles, update "Overlay Lifecycle" section to reflect delegation pattern
  - `macros.design.md`: update globs to include `MacroHandler.cs`, update "Macro State Mutex" and "Key Dispatch Priority" sections to reference `MacroHandler`
  - `.design-notes.md`: update Available Skills table with new file paths in globs
