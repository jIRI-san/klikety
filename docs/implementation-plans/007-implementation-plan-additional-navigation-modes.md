# 007: Additional Navigation Modes (Crosshair & LogCrosshair)

## Decisions

- **Mode names**: `UniformGrid` (existing, renamed from TwoKey/Arrow/Both), `Crosshair` (new), `LogCrosshair` (new). Old `NavigationMode` enum replaced by per-mode config with `arrowKeys` and `twoKey` toggles.
- **Per-mode input scheme**: Each `ModeConfig` has `bool TwoKey` and `bool ArrowKeys` toggles. Old `Arrow` → `twoKey: false, arrowKeys: true`; old `TwoKey` → `twoKey: true, arrowKeys: false`; old `Both` → both true. For Crosshair/LogCrosshair, `twoKey` controls axis-key input; `arrowKeys` controls arrow navigation.
- **All keys configurable**: Every key the user presses is config-driven. Crosshair/LogCrosshair axis keys come from per-mode `horizontalKeys`/`verticalKeys` arrays (default 10-key QWERTY sets). UniformGrid uses existing `firstKeys`/`secondKeys`. Chord keys, action keys, hotkey — all configurable. No hardcoded key arrays anywhere.
- **Default key sets**: UniformGrid fresh installs expand to 10×10: `firstKeys: [A,S,D,F,G,H,J,K,L,;]`, `secondKeys: [Q,W,E,R,T,Y,U,I,O,P]`. Crosshair/LogCrosshair default `horizontalKeys`/`verticalKeys` to the same 10-key sets. Migrated users keep their original key sets for UniformGrid.
- **Chord activation**: Single global hotkey (Alt+Space). Default mode opens immediately. Non-default modes activated by chord key (configurable, default N = Crosshair, M = LogCrosshair) before any nav input. Once any nav/arrow/action key is pressed, mode is locked. Default-mode flash on chord is accepted UX trade-off (deferred render adds latency to the common case).
- **Activation debounce**: `IKeyboardHookService` extended to fire key-up events: `event EventHandler<KeyHookEventArgs>? KeyEvent` where `record KeyHookEventArgs(VKey Key, bool IsDown)`. After hotkey fires, coordinator populates `_debounceKeys` with only actually-held VKeys via `IKeyStateProvider.IsKeyDown()` check per modifier variant. Trigger key added unconditionally. Population BEFORE hook enable (closes TOCTOU). 500ms timeout via `ITimerFactory` reconciles (not blind-clear). Trigger-key fast removal: on first keydown for a different key, remove trigger key from debounce set. `DeactivateOverlay()` clears debounce + cancels timeout. Re-entrant activation ignored.
- **Dynamic key reduction**: At L2/L3, outermost keys dropped from both sides (centered) to keep cells ≥5px. Formula-driven per axis independently. For Crosshair grids with center cell: `maxCells = parentExtentPx / minCellPx`; `maxKeys = maxCells - 1`; rounded to even for center stability. UniformGrid: odd counts allowed with right/bottom tie-break. Absolute key-to-offset mapping preserved — out-of-range keys trigger `InvalidKeyPressed()`. If `maxKeys < 2`, level disabled.
- **Crosshair grid**: `(N+1) × (N+1)` uniform cells where N = keys-per-axis from config. Cross (center row + column) rendered; non-cross dimmed. Keys are absolute offsets from center. Same-axis re-press allowed. L2/L3 subgrids with cross-style nav and reduced keys. Center cell (0,0) requires Enter with no axis keys. Arrow nav traverses cross arms only via `CrossArrowNavigator`.
- **LogCrosshair grid**: 5px center cell at cursor, cells grow outward via `baseSize × (1+d)^exp`. One exponent per axis, auto-computed to fill longer side. On cell selection, new log grid renders at that position (flat — no L2/L3). Esc closes overlay. Element pooling for re-renders. Last-nav-key-wins coalescing with 16ms minimum render interval.
- **Architecture — `IModeSession` pattern**: Each mode provides a self-contained `IModeSession` owning its state machine, renderer, and grid calculator. Coordinator is thin dispatcher: manages overlay lifecycle (show/hide/hook) and delegates key input to active session. `IModeSession`: `Activate(Rectangle, Point)`, `OnKey(VKey)`, `Deactivate()`, events `ActionRequested`, `Cancelled`, `InvalidKeyPressed`, `CursorMoveRequested`. Session `Deactivate()` = internal reset only, no surface events. Coordinator owns canvas clearing via `IOverlayWindow.ClearCanvas()`.
- **Mode switch lifecycle**: `SwitchMode` with `_switching` flag, deferred deactivation, try/catch/finally safety. Unconditional safety layer (hide overlay + disable hook) always executes. Subscribe before Activate. `_activeSession = null` between unsubscribe and new-session assignment for staleness guard.
- **Renderer per mode**: Each mode has its own renderer. `IGridRenderer` retained for UniformGrid only. Crosshair/LogCrosshair have mode-specific APIs.
- **Axis labels**: Single-character labels via `AxisLabelGenerator` using shared `KeyLabelResolver.ResolveLabel(VKey, IntPtr hkl)` extracted from `LabelGenerator`.
- **Arrow navigation in cross grids**: Two flat arrays (horizontal arm, vertical arm). Center cell in both. Axis switch only at center. Perpendicular arrow at non-center = no-op. Wrapping at arm ends.
- **Config migration**: `JsonDocument` pre-pass with JSONC support. Detect old shape, transform to new. `configVersion` integer for future migrations. Error taxonomy: parse/conflict → fail-closed; unknown fields → warn; missing optional → defaults. N/M chord conflicts with existing action bindings → auto-disable conflicting mode. Atomic write with `.bak` backup.
- **Non-QWERTY layouts**: At activation, check active keyboard layout via `IKeyboardLayoutProvider`. If non-QWERTY, show tray warning (once per session) and fall back to UniformGrid. With fully configurable axis keys, non-QWERTY users can set keys matching their layout.
- **Multi-monitor**: Not in scope. Activation guardrail: cursor outside primary screen → suppress overlay + tray notification.
- **Performance**: Render target < 70ms. LogCrosshair element pooling. 16ms min render interval. Hook eviction recovery via Alt+Tab. Phase 4 profiling gates.

## Requirements

| ID | Requirement | Acceptance Criteria |
|----|-------------|---------------------|
| REQ-1 | Config `modes` structure with per-mode `enabled`, `default`, `chordKey`, `arrowKeys`, `twoKey` | Config loads with new shape; old shape auto-migrated; validates; `dotnet format` clean |
| REQ-2 | Per-mode key configuration: `horizontalKeys`/`verticalKeys` for Crosshair/LogCrosshair; `firstKeys`/`secondKeys` for UniformGrid | All axis keys configurable per mode; defaults to 10-key QWERTY sets; validation catches conflicts |
| REQ-3 | Fresh installs: 10×10 defaults. Migrated users: preserve original key sets | L1 grid correct size; migrated 8-key users keep 8×8 UniformGrid |
| REQ-4 | Chord activation: hotkey opens default; chord key switches before first nav input | Default mode appears on hotkey; chord switches mode; after nav key, chord ignored |
| REQ-5 | Key validation: chord keys, axis keys, action keys, hotkey — all mutually disjoint per mode | Startup catches all overlaps; tray notification on conflict |
| REQ-6 | `IModeSession` interface; `UniformGridSession` wraps existing SM + renderer | Existing tests pass; zero behavior change |
| REQ-7 | Coordinator refactored to thin dispatcher | Manages lifecycle only; all input delegated to session |
| REQ-8 | Dynamic key reduction at L2/L3 per axis | Cells ≥5px; out-of-range keys → `InvalidKeyPressed` |
| REQ-9 | Crosshair L1: `(N+1)×(N+1)` grid, cross rendering, axis key selection | Cross visible; axis keys work; both axes → L2; Enter with no axis → center L2 |
| REQ-10 | Crosshair same-axis re-press | Re-press overrides; second axis locks first |
| REQ-11 | Crosshair L2/L3 subgrids with cross-style nav | Key count from reducer; Esc goes up one level |
| REQ-12 | Crosshair rendering: cross + dimmed non-cross + labels | Visual correctness at all levels |
| REQ-13 | LogCrosshair: log-scaled grid at cursor, 5px base, auto-exponent | Grid fills screen; cells grow outward; exponent correct at all resolutions |
| REQ-14 | LogCrosshair re-render at selected position | On selection, new grid renders; repeat until action/Esc |
| REQ-15 | LogCrosshair Esc closes overlay | Cursor restored to initial activation origin |
| REQ-16 | Arrow keys traverse cross arms in Crosshair/LogCrosshair | Axis switch at center only; wrapping; center is a stop |
| REQ-17 | Action keys fire at current position in all modes | Overlay hidden before SendInput |
| REQ-18 | Backward-compatible config auto-migration | Old config → new shape; conflicts handled per error taxonomy |
| REQ-19 | Unit tests for each mode + failure/concurrency paths | Dedicated test classes; coverage of edge cases |
| REQ-20 | `IKeyboardHookService` extended with key-up events | `KeyEvent` replaces `KeyPressed`; existing flows unchanged |
| REQ-21 | Activation debounce | Hotkey modifier keys not captured as nav input |

## Risks

| ID | Risk | L | I | Mitigation |
|----|------|---|---|------------|
| R-1 | Dynamic key reduction feels unintuitive | H | M | Configurable min cell size; label display updates |
| R-2 | 10×10 makes L3 cells sub-pixel | M | M | Dynamic reduction keeps cells ≥5px; L3 threshold gate |
| R-3 | Log exponent produces bad cell sizes on unusual resolutions | M | L | Clamp min cell size; test 1080p/1440p/4K; config override |
| R-4 | WPF Canvas too slow for LogCrosshair 4K re-renders | M | M | Element pooling; coalescing; DrawingVisual fallback if >70ms |
| R-5 | Chord key timing confusion | M | L | Chord only before first nav; invalid after lock → flash |
| R-6 | Config migration breaks edge-case configs | L | M | Error taxonomy; atomic write; `.bak`; unit tests per category |
| R-7 | Interface extraction introduces subtle regressions | L | H | Run full suite after each handler migration; incremental |
| R-8 | Cross rendering distortion on non-16:9 | L | L | Cells from screen dims; non-square acceptable |
| R-9 | Activation race (Space captured by hook) | M | M | Debounce with `IKeyStateProvider`; 500ms timeout reconciliation |
| R-10 | Center cell requires Enter with no axis — unintuitive | M | L | Documented trade-off; revisit after user testing |
| R-11 | LogCrosshair re-render loop under rapid input | L | M | Coalescing; 16ms interval; generation tokens |
| R-12 | Cursor on secondary monitor | M | M | Activation guardrail; full multi-monitor deferred |
| R-13 | `KeyPressed` → `KeyEvent` breaking change ripple | L | M | Isolated sub-step; mechanical find-replace; full suite after |
| R-14 | Non-QWERTY axis key scatter | M | M | All keys configurable per mode; QWERTY detection + warning + fallback |
| R-15 | CrossArrowNavigator backtracking to center | M | L | Inherent to cross; future: direct center-jump option |

## Phase 1: Config & Key Expansion
<!-- worktree: feature/007-additional-navigation-modes-config-key-expansion-step-1-1 -->

- [x] 1.1 New `ConfigModel` shape `M`
  - Add `ModeConfig` class: `bool Enabled`, `bool Default`, `VKey? ChordKey`, `bool ArrowKeys`, `bool TwoKey`, `int LogBaseSize` (LogCrosshair only, default 5), `VKey[]? HorizontalKeys`, `VKey[]? VerticalKeys` (axis keys for Crosshair/LogCrosshair; null = use defaults)
  - Add `ConfigModel.Modes` with three named properties: `UniformGrid`, `Crosshair`, `LogCrosshair`
  - Add `ConfigModel.ConfigVersion` (`int`, default 0)
  - Keep old `NavigationMode` property for migration readability (handled via `JsonDocument` pre-pass)
  - Defaults: `uniformGrid { enabled: true, default: true, arrowKeys: true, twoKey: true }`, `crosshair { enabled: true, chordKey: N, arrowKeys: true, twoKey: true, horizontalKeys: [A,S,D,F,G,H,J,K,L,;], verticalKeys: [Q,W,E,R,T,Y,U,I,O,P] }`, `logCrosshair { enabled: true, chordKey: M, arrowKeys: true, twoKey: true, horizontalKeys: [A,S,D,F,G,H,J,K,L,;], verticalKeys: [Q,W,E,R,T,Y,U,I,O,P] }`
  - HotKey stays at root level

- [x] 1.2 Config auto-migration via `JsonDocument` pre-pass `M`
  - Parse raw JSON with `JsonDocumentOptions { CommentHandling = Skip, AllowTrailingCommas = true }` (existing configs use JSONC)
  - Detect old shape (has `navigationMode`, no `modes`); transform: map `navigationMode` to per-mode `twoKey`/`arrowKeys`; write `configVersion: 1`
  - **Key set migration**: if old `firstKeys`/`secondKeys` match known 8-key defaults → write them explicitly (no silent 64→100 cell change). Only fresh installs get 10-key defaults. Crosshair/LogCrosshair `horizontalKeys`/`verticalKeys` always get 10-key defaults (new feature, no backward compat needed)
  - **Conflict handling**: N/M chord keys conflict with existing `actionBindings` → auto-disable conflicting mode. New keys (G,H,Q,P) conflict with `actionBindings` → keep original key set for that axis. Action binding references invalid VKey → fail-closed
  - **Post-migration normalization**: at least one mode enabled, exactly one default. If default was auto-disabled, promote UniformGrid
  - Atomic write: temp file in same dir → rename. Keep `.bak` of pre-migration config. Clean up stale `.tmp` at migration start
  - `configVersion` > known → fail-closed with tray notification + "Reset config" menu option
  - Unknown fields → warn and preserve in output (round-trip)
  - Tests: old→new migration; `Arrow`-only; missing fields; malformed; N/M conflict; G/H conflict; version too high; mixed shape (both `navigationMode` and `modes` → `modes` wins); idempotency (already-migrated config → no mutations)

- [x] 1.2a "Reset config" tray menu option `S`
  - Visible when config has blocking violations or `configVersion` > current
  - On click: copy embedded default config → `%APPDATA%\Klikety\config.json`; re-trigger config load + coordinator bootstrap
  - Bootstrap: `DeactivateOverlay()` → unregister hotkey → dispose coordinator → reload → validate → reconstruct if valid
  - Tests: menu item appears on violation; full reset cycle; reset while overlay active

- [x] 1.3 Validation `M`
  - Per-mode key disjointness: chord keys vs `firstKeys`/`secondKeys` (UniformGrid), vs `horizontalKeys`/`verticalKeys` (Crosshair/LogCrosshair), vs `actionBindings`, vs arrows/Esc/Enter, vs hotkey trigger + modifier VKeys
  - Cross-mode: chord keys mutually unique; per-mode axis keys vs action bindings; when Crosshair/LogCrosshair enabled, their axis keys vs hotkey
  - Structural: exactly one `default: true`; at least one `enabled: true`; every enabled mode has `twoKey || arrowKeys`
  - Mode-specific: Crosshair/LogCrosshair require `twoKey: true`; `logBaseSize` ∈ [2, 50]
  - Per-mode axis key validation: no duplicates within array, non-empty when mode enabled, no overlap between horiz and vert within same mode
  - Unknown mode entries via `JsonDocument` pre-pass → warning
  - Violations surfaced via tray notification; blocking violations → skip coordinator creation (startup gate)

- [x] 1.4 Expand key sets `M`
  - `ConfigModel` defaults: `firstKeys` → 10 keys, `secondKeys` → 10 keys
  - Verify `LabelGenerator` and `GridCalculator` handle 10×10 (already data-driven)
  - Update embedded `config.json` resource

- [x] 1.5 Update UniformGrid tests `S`
  - Verify 10×10 grid works; add 8-key backward compat test
  - Zero regressions

- [x] 1.6 Update embedded config + schema `S`
  - Schema: add `modes` object with sub-schemas including `horizontalKeys`/`verticalKeys` arrays; add `configVersion`
  - Config: new `modes` block with defaults including axis key arrays
  - Comment: "N and M are default chord keys — do not use in action bindings"

- [x] 1.7 `dotnet format` `S`

## Phase 2: Infrastructure & Interface Extraction

- [ ] 2.1a Extract `KeyLabelResolver` shared helper [after: 1.4] `S`
  - `IKeyLabelResolver` interface + static production impl using `ToUnicode`/`MapVirtualKey`
  - Refactor `LabelGenerator` to accept `IKeyLabelResolver` (zero behavior change)
  - `AxisLabelGenerator`: single-char labels from axis `VKey[]` via `IKeyLabelResolver`
  - Tests use fake `IKeyLabelResolver` (no Win32 dependency)

- [ ] 2.1b `IKeyboardHookService` key-up support [after: 2.1a] `M`
  - `KeyPressed` → `KeyEvent(EventHandler<KeyHookEventArgs>)` where `record KeyHookEventArgs(VKey Key, bool IsDown)`
  - Hook handles `WM_KEYDOWN`/`WM_SYSKEYDOWN` + `WM_KEYUP`/`WM_SYSKEYUP`
  - `FakeKeyboardHookService`: `SimulateKeyDown(VKey)` + `SimulateKeyUp(VKey)`
  - Coordinator handler: `IsDown` → forward to SM; `!IsDown` → debounce removal only
  - At this step coordinator still owns `_stateMachine` directly
  - Compile-breaking change isolated; run full suite after

- [ ] 2.1c Extract `IModeSession` + `UniformGridSession` skeleton [after: 2.1b] `M`
  - `IModeSession` interface: `Activate(Rectangle, Point)`, `OnKey(VKey)`, `Deactivate()`, events `ActionRequested(Point, MouseAction)`, `Cancelled()`, `InvalidKeyPressed()`, `CursorMoveRequested(Point)`
  - `Cancelled` parameterless — coordinator owns `_origin`
  - `Deactivate()` = `Reset()` (direct state, no `OnKey(Escape)`); no surface events; no canvas clear
  - `InvalidKeyPressed` — session flashes via own renderer internally; no coordinator round-trip
  - `UniformGridSession`: wraps SM + renderer. Converts `ModeConfig` booleans → `NavigationMode` enum internally
  - `IOverlayWindow.ClearCanvas()` added; `ClearCanvas()` removed from `IGridRenderer`; `GridRenderer` keeps internal clear as private detail
  - `DeactivateOverlay()` switches to `_overlayWindow.ClearCanvas()`

- [ ] 2.1d Migrate coordinator handlers to `UniformGridSession` [after: 2.1c] `L`
  - Incremental, one group at a time with test runs between:
    1. `OnColumnHighlighted` + `OnSubgridCellHighlighted`
    2. `OnCellEntered` + `OnSubgridCellEntered`
    3. `OnActionKeyPressed` + `OnEscPressed`
    4. `OnInvalidKeyPressed` + `OnSubgridRendered`
  - Final: coordinator holds `IModeSession _activeSession`, delegates `OnKey()` only

- [ ] 2.2 Mode factory [after: 2.1d] `M`
  - Input: mode name, config, theme, canvas, label generator, action mapper
  - Output: `IModeSession` — switch on mode name
  - UniformGrid now; Crosshair/LogCrosshair stubs until Phases 3/4

- [ ] 2.3 Chord dispatch + debounce + guardrails [after: 2.2] `L`
  - **New abstractions** grouped in `IPlatformServices`: `IKeyStateProvider` (wraps `GetAsyncKeyState`), `ITimerFactory` (returns `ITimer` with `Start(TimeSpan)`, `Stop()`, `Elapsed`), `ICursorPositionProvider` (wraps `GetCursorPos`), `IScreenBoundsProvider` (wraps `GetPrimaryScreenBounds`), `IKeyboardLayoutProvider` (wraps `GetKeyboardLayout`)
  - **Multi-monitor guardrail**: cursor outside primary → suppress + tray notification
  - **Debounce**: populate `_debounceKeys` BEFORE hook enable; 500ms timeout reconciles via `IKeyStateProvider`; trigger-key fast removal
  - **Mode lock**: `_modeLocked` bool; nav/arrow/action key sets true
  - **Chord dispatch**: `!_modeLocked` + chord key → `SwitchMode()`. After lock → session handles (flash)
  - **SwitchMode**: `_switching=true`; unsubscribe old; `_activeSession=null`; old.Deactivate(); `ClearCanvas()`; create new via factory; subscribe; increment `_sessionGeneration`; new.Activate(); assign. Try/catch/finally; catch calls idempotent `DeactivateOverlay()`
  - **Bounds validation**: before `SendAction()`, validate `_actionPoint` within `_screenBounds`
  - **Non-QWERTY**: check layout at activation; non-QWERTY + Crosshair/LogCrosshair → fallback to UniformGrid + warning (once per session)
  - **Deferred deactivation**: unconditional safety layer always runs; session teardown deferred during `_switching`
  - Migrate all screen-bounds/cursor calls to new providers

- [ ] 2.4 Dynamic key reduction [after: 1.4] `M`
  - `DynamicKeyReducer.ComputeActiveKeys(VKey[] fullKeys, int parentExtentPx, int minCellPx, bool hasCenterCell) → DynamicKeyReduction`
  - `record DynamicKeyReduction(VKey[] ActiveKeys, int OriginalStartIndex)`
  - Algorithm: `maxCells = parentExtentPx / minCellPx`; `maxKeys = hasCenterCell ? maxCells - 1 : maxCells`; Crosshair: round to even; take centered subset
  - `maxKeys < 2` → empty (level disabled)
  - Tests: various sizes, edge cases, per-axis asymmetry, resolution integration

- [ ] 2.5 Infrastructure tests [after: 2.3] `M`
  - Mode switching lifecycle; event leaks; debounce (suppression, timeout, ordering, re-entrancy)
  - SwitchMode failures (factory throw, Activate throw, old Deactivate throw)
  - Focus-loss during SwitchMode; contract enforcement; multi-monitor guardrail; non-QWERTY fallback
  - Click-through safety ordering; stale keys after hook disable; action triggers focus-loss

- [ ] 2.6 `dotnet format` + full test suite [after: 2.5] `S`

## Phase 3: Crosshair Mode

- [ ] 3.1 `CrosshairGridCalculator` [after: 2.4] `M`
  - `(N+1)×(N+1)` uniform cells from screen bounds + per-axis key count (from mode's `horizontalKeys.Length`/`verticalKeys.Length`)
  - Center cell at `(vertKeys/2, horizKeys/2)`
  - For L2/L3: parent cell bounds + reduced key counts from `DynamicKeyReducer`

- [ ] 3.2 `CrosshairStateMachine` + `CrosshairSession` [after: 3.1, 2.4] `L`
  - States: `Idle`, `AwaitInput`, `HorizSet`, `VertSet`, `L2_*`, `L3_*`
  - Axis keys from mode config `horizontalKeys`/`verticalKeys`
  - Key offset formula: `offset = i < keysPerSide ? i - keysPerSide : i - keysPerSide + 1` (where `keysPerSide = keys.Length / 2`)
  - Same-axis re-press; Enter for single-axis or no-axis (center); L2/L3 via reducer
  - `_actionPoint` updates on each axis key to intersection cell center
  - Arrow keys via `CrossArrowNavigator` — only in `AwaitInput`; after axis key → no-op
  - Esc: clears partial axis first; from `AwaitInput` → level transition
  - Level stack with per-level `DynamicKeyReduction` + cells + arrow state
  - Labels via `AxisLabelGenerator`; `arrowKeys: false` → skip arrow navigator
  - Structured logging

- [ ] 3.3 `CrosshairRenderer` [after: 3.1] `L`
  - Mode-specific API: `RenderCross`, `HighlightAxis`, `HighlightCell`, `RenderSubgridCross`, `FlashInvalidKey`, `SetTransform`
  - Cross cells: borders + single-char labels; non-cross: 15% opacity dim
  - Extract `TextRenderHelper` from `GridRenderer`; extract fan-out label logic
  - `FakeCrosshairRenderer` for tests

- [ ] 3.4 Wire Crosshair into mode factory + integration test [after: 3.2, 3.3, 2.2] `M`
  - Tests: chord N → cross; axis keys → L2 → action; Enter → offset; Esc back

- [ ] 3.5 Crosshair unit tests [after: 3.2, 3.1] `M`
  - State transitions, re-press, Enter variants, Esc, action, cross-arm arrow nav
  - Grid calculator at various sizes; `AxisLabelGenerator` with fake resolver
  - Out-of-range keys; `arrowKeys: false`; custom axis keys; contract tests

- [ ] 3.6 `dotnet format` + full test suite [after: 3.5] `S`

## Phase 4: LogCrosshair Mode

- [ ] 4.1 `LogGridCalculator` [after: 2.4] `M`
  - Input: center point, screen bounds, base size (`logBaseSize`), keys-per-axis (from mode's axis key array lengths)
  - One exponent per axis via binary search; shorter side clips; min distance clamped
  - Degenerate cells flagged; float precision via last-cell absorption
  - Tests: resolutions, cursor positions, edge cases

- [ ] 4.2 `LogCrosshairRenderer` [after: 4.1, 3.3] `M`
  - Mode-specific API; element pooling (shapes mutated, not recreated); staleness via `Parent == null`
  - Performance instrumentation; text geometry caching; DrawingVisual fallback if >70ms
  - `FakeLogCrosshairRenderer` for tests

- [ ] 4.3 `LogCrosshairStateMachine` + `LogCrosshairSession` [after: 4.1, 3.2] `L`
  - States: `Idle`, `AwaitInput`, `HorizSet`, `VertSet` (flat, no L2/L3)
  - Axis keys from mode config `horizontalKeys`/`verticalKeys`
  - Re-render coalescing: `_pendingNavKey` overwritten; bootstrap callback; `_callbackPending` + `_renderInProgress` gates; 16ms min interval; generation + `_active` checks
  - Esc/action bypass coalescing; eager `_actionPoint` update; degenerate cell → `InvalidKeyPressed`
  - Enter with no axis = no-op; arrows via `CrossArrowNavigator`
  - Origin for restoration = initial activation position
  - Structured logging

- [ ] 4.4 LogCrosshair unit tests [after: 4.3, 4.1] `M`
  - State transitions, re-render cycle, coalescing, eager `_actionPoint`, degenerate cells
  - Grid calculator tests; custom axis keys; contract tests

- [ ] 4.5 Wire LogCrosshair into mode factory + integration test [after: 4.3, 4.2, 2.2] `M`
  - Tests: chord M → log grid → select → re-render → action; Esc → origin; multiple re-renders

- [ ] 4.6 `dotnet format` + full test suite [after: 4.5] `S`

## Phase 5: Design Notes & Final Validation

- [ ] 5.1 Update `keyboard-navigator.design.md` [after: 4.6] `M`
  - Document: modes, chord activation, `IModeSession`, dynamic key reduction, configurable axis keys, renderers, multi-monitor non-goal

- [ ] 5.2 Final test suite + `dotnet format` + build clean [after: 5.1] `S`

## Known Plan Issues

Fix during implementation or accept as documented.

| # | Sev | Issue |
|---|-----|-------|
| 1 | M | `GridRenderer` internally clears canvas (private impl); `LogCrosshairRenderer` uses pooling. Document as scoped to `UniformGridSession` |
| 2 | M | QWERTY heuristic (checking `VK_A`/`VK_Q`) passes for Colemak. Check all axis keys or document. Layout change mid-session = known limitation |
| 3 | M | Center cell index assumes even `ActiveKeys.Length` — add `Debug.Assert` when `hasCenterCell` |
| 4 | M | `CrossArrowNavigator` level-stack capture timing: capture at L2 entry. Test: arrow pos 3 → L2 → Esc → still pos 3 |
| 5 | M | LogCrosshair 16ms gate: clarify applies between render *starts*; post-render callback always checks pending |
| 6 | L | Stale `.tmp` cleanup at migration start |
| 7 | L | Even-rounding rationale: odd `maxKeys` → `keys+1` even → center between cells. Add code comment |
