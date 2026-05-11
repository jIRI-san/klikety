# Evolution Log

## Round 1

**Reviewers:** Opus, Codex, Gemini (via @dr)

### Issues Found (14 total)

| # | Severity | Issue | Resolution |
|---|----------|-------|------------|
| 1 | Critical | HWND and overlay visibility split across coordinator and MacroHandler | Fixed: overlay Show/Hide exclusively owned by coordinator. MacroHandler fires intent events. MacroHandler stores own target HWND. |
| 2 | High | `_origin`, `_screenBounds`, `_appScoped`, `_appScopeBounds` ownership undefined | Fixed: moved to SessionManager as session-level scope with read-only properties. |
| 3 | High | Test split requires shared helper extraction | Fixed: added step 1.0 to extract `CreateCoordinator()` to `CoordinatorTestHelper.cs`. Renamed test files to behavior-based names. |
| 4 | High | Disposal and async drain ordering not specified | Fixed: explicit disposal order documented. `SessionManager` and `MacroHandler` implement `IDisposable`. |
| 5 | High | Event forwarding re-entrant transition paths | Fixed: specified synchronous unsubscribe-first invariant in SessionManager. |
| 6 | Medium | `OnSessionCancelled` routing not assigned | Fixed: added to REQ-5 coordinator-retained methods. `ActionDispatcher` exposes `IsDragMode`/`CancelDrag()`. |
| 7 | Medium | `ResumeOverlayForRecording` split unspecified | Fixed: MacroHandler performs HWND validation, fires intent event; SessionManager performs session creation. |
| 8 | Medium | DebounceHandler called "pure filter" but owns timer/platform | Fixed: renamed to "debounce controller". |
| 9 | Medium | Macro service setter idempotency not called out | Fixed: added explicit requirement for detach-then-attach pattern. |
| 10 | Low | MacroState enum location not specified | Fixed: namespace-level type in `MacroHandler.cs`. |
| 11 | Low | Dead method `OnSessionCancelledDuringRecording` not flagged | Fixed: flagged for deletion in Phase 4 step 4.1. |
| 12 | Low | Phase 3 disproportionately large | Deferred: Phase 3 already sized L. Splitting adds overhead without clear benefit since MacroHandler and ActionDispatcher are coupled via recording action dispatch. |
| 13 | Low | Dependency graph omits MacroHandler → IKeyboardHookService | Fixed: added to RISK-1 dependency graph and MacroHandler constructor/dependencies. |
| 14 | Low | Logger partial relocation needs per-phase verification | Already covered by REQ-8 zero-warnings criterion. No plan change needed. |

### Issues Deferred

- **Phase 3 splitting** (#12): MacroHandler and ActionDispatcher share recording-action-dispatch coupling. Splitting into 3a/3b risks intermediate compile failures. Keep as single phase sized L.
