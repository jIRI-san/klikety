# Evolution Log — 013: Action Button Extensions

## Round 1

**Reviewers:** Opus, Codex (Gemini returned no output)

**Issues found:** 13 (2 Critical, 3 High, 7 Medium, 1 Low)

**Issues fixed (all 13):**

1. **[Critical] ModifierDetector bypass** — Replaced static `ModifierDetector` with injectable `IModifierDetector` interface + `FakeModifierDetector` for tests. Follows project's Win32-behind-interface pattern.
2. **[Critical] Drag-abort cursor restore** — `_origin` (overlay-open position) is now the cancel restore target throughout drag flow. `_dragStartPoint` used only for `SendDrag`. Added explicit restore in `DeactivateOverlay` when `_dragMode` was active.
3. **[High] Drag action matrix** — Defined explicit second-phase matrix: `LeftClick`/`DoubleClick` → left, `RightClick` → right, `MiddleClick` → middle, `MoveOnly` → invalid, `DragDrop` → invalid. Added `!_dragMode` guard on DragDrop-start branch.
4. **[High] Naming consistency** — Standardized: `scrollHotkeys` (camelCase) in JSON, `ScrollHotKeys` (PascalCase) in C# with `[JsonPropertyName("scrollHotkeys")]`. All references audited.
5. **[High] Status text lifecycle** — Status text rendered in separate XAML `Grid` layer above main `Canvas`. `ClearCanvas` only clears Canvas children. No re-show needed after mode switch.
6. **[Medium] Focus loss + drag origin** — `DeactivateOverlay` restores `_origin` when `_dragMode` was active.
7. **[Medium] Scroll config validation** — Added `scrollAmount` range check (1–100), scroll key conflict checks against reserved/action/chord/nav keys, duplicate rejection. Surfaced via tray notification.
8. **[Medium] SendInput partial-send handling** — Validate return count; compensating KEYUP/BUTTONUP on partial sends; log warning. Added as RISK-6.
9. **[Medium] Status text rendering** — Uses existing geometry-path two-layer approach (`BuildGeometry` → stroke + fill). Theme-driven colors instead of hardcoded.
10. **[Medium] ResetOverlayForDrag error handling** — Wrapped `Activate()` in try/catch matching `SwitchMode` pattern. On failure: `_dragMode = false`, clear status text, `DeactivateOverlay()`.
11. **[Medium] Test seams for scroll service** — Added `IScrollHotKeyService` interface. Win32 assertions scoped to smoke tests; unit tests use fakes for logic.
12. **[Medium] Guard DragDrop in SendAction** — Both `DragDrop` and `MoveOnly` return after `MoveTo` in `SendAction`. Defensive guard prevents garbage mouse events.
13. **[Low] INPUT count** — Corrected Decisions: "4+ inputs" instead of "3 inputs". Detailed count in drag step.

**Issues deferred:** None
