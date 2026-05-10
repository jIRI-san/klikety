# Evolution Log — 016: App-Scoped Navigation

## Round 1

**Reviewers:** Opus, Codex, Gemini (via `@dr`)

### Issues Found

| # | Severity | Finding | Resolution |
|---|----------|---------|------------|
| 1 | Critical | `GetForegroundWindow()` returns overlay HWND at chord time — app-scope never activates | Fixed: capture HWND in `OnHotKeyActivated` before `Show()`; store as `_preOverlayHwnd`; retrieve bounds for stored HWND at chord time |
| 2 | Critical | `OnFocusLost` calls `DeactivateOverlay()` unconditionally — Hide→Show during `SwitchToAppScope` kills session | Fixed: add `if (_switching) return;` guard to `OnFocusLost`; wrap both `SwitchToAppScope` and `ResetOverlayForDrag` in `_switching` |
| 3 | Critical | `SwitchMode()` uses `_screenBounds` — mode switch during app-scope reverts to full-screen | Fixed: add `ActiveBounds` property (`_appScoped ? _appScopeBounds : _screenBounds`); use in `SwitchMode()`, `ResetOverlayForDrag()`, `OnSessionActionRequested` |
| 4 | High | `_origin` may lie outside app-scoped bounds — degenerate grids, off-target actions | Fixed: re-query cursor position at chord time, clamp into clipped bounds |
| 5 | High | `IForegroundWindowProvider` API hides HWND — can't support pre-capture pattern | Fixed: redesigned to two-method API: `GetForegroundWindowHandle()` + `GetWindowBounds(nint hwnd)` |
| 6 | High | OS auto-repeat on chord key re-invokes heavyweight `SwitchToAppScope` | Fixed: `if (_appScoped) return;` guard at top of chord handler |
| 7 | Medium | No `_currentModeName` tracked — `SwitchToAppScope` can't recreate correct session | Fixed: added `_currentModeName` field, set on activation and mode switch |
| 8 | Medium | Border on `RootCanvas` cleared by renderers during normal key updates | Fixed: render on `StatusCanvas` instead (survives `ClearCanvas()`) |
| 9 | Medium | Config migration version ambiguous — concurrent plans risk blocking errors | Fixed: committed to v5→v6 unconditionally; macros v4→v5 is prerequisite |
| 10 | Medium | Minimized detection via zero-area DWM bounds is fragile | Fixed: use `IsIconic(hwnd)` as explicit check before DWM query |
| 11 | Medium | `ResetOverlayForDrag` gains Hide→Show but lacks `_switching` guard | Fixed: wrap in `_switching` guard (same as `SwitchToAppScope`) |
| 12 | Medium | `Show(Rectangle)` flashes overlay at old size before repositioning | Fixed: pre-set bounds before WPF `Show()` when PresentationSource already available |
| 13 | Medium | Tests can't simulate WPF `Deactivated` from Hide→Show | Fixed: `FakeOverlayWindow.RaiseFocusLostOnHide` configurable flag; tests verify guard |
| 14 | Low | Theme property `AppScopeBorderColor` deferred to Phase 4 but referenced in Phase 2 | Fixed: moved theme step to Phase 2 as step 2.7 (prerequisite of 2.3); hardcoded fallback in 2.3 |
| 15 | Low | `IPlatformServices` interface change breaks all test setups | Noted in step 1.2 as routine scope impact |

### Issues Deferred

None — all findings applied.
