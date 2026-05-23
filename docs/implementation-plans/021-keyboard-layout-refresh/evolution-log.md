# Evolution Log — 021: Keyboard Layout Refresh

## Round 1

**Reviewers:** Opus · Codex · Gemini (via @dr)

**Issues found:**
1. [Critical] Coordinator has no access path to label generators inside renderers
2. [High] `GetKeyboardLayout(0)` returns current thread layout, not foreground window layout
3. [High] SM state accessors insufficient for full visual reconstruction (missing cell lists, arrow positions)
4. [High] Direct `NativeMethods` calls in `KeyPressProcessor` break testability seam
5. [Medium] Sessions can't call `ClearCanvas()` — no `IOverlayWindow` reference
6. [Medium] Step ordering: interface declaration (3.5) must precede implementations (3.1-3.4)
7. [Medium] `DebugLogGridSession` also implements `IModeSession` — needs stub
8. [Medium] No in-session fallback if `WM_INPUTLANGCHANGE` fails to arrive
9. [Medium] `Rebuild()` mutability needs explicit single-thread guarantee documentation
10. [Medium] LogGrid first-key indicator uses raw VKey instead of `AxisLabelGenerator`

**Issues fixed (all 10 applied):**
- Added `RebuildLabels(IKeyLabelResolver)` to renderer interfaces as dedicated step (1.3)
- Replaced all `GetKeyboardLayout(0)` references with `GetActiveKeyboardLayout()`
- Expanded SM accessor scope: full enumeration of needed fields per state machine
- `KeyPressProcessor` now takes injectable `Func<nint> hklProvider` + `Func<nint, IKeyLabelResolver> resolverFactory`
- `ClearCanvas()` moved to `SessionManager.RedrawActiveSession()` (sessions don't call it)
- Reordered Phase 3: interface (3.1) → SM accessors (3.2, 3.4, 3.7) → implementations (3.3, 3.5, 3.6, 3.8)
- `DebugLogGridSession.Redraw()` no-op stub explicitly included in step 3.1
- Added secondary HKL-check-on-key-event fallback in coordinator (step 2.3)
- Single-thread constraint documented in Decisions section
- LogGrid first-key indicator routed through `AxisLabelGenerator.LabelFor(col)` (step 3.8)

**Issues deferred:** None.
