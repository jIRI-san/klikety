# 011: LogGrid Mode Integration (State Machine, Session, Config, Factory, Chord Dispatch)

> Scope: integrate LogGrid mode into runtime navigation flow. Phase 1 calculator/renderer foundation was completed in Plan 010.

## Decisions
- LogGrid mode is iterative and explicit-action: second key recenters immediately; action key (Space/X/C/V) is always explicit.
- Arrow navigation moves selected cell without recentering; recenter occurs on Enter (arrow-selected cell) or action key.
- Enter after two-key recenter state is ignored.
- Escape always exits overlay and restores cursor to original activation point; no center-position stack.
- First-key feedback uses a large corner indicator (single char), placed in the screen corner opposite to the current grid center quadrant; exact center fallback is bottom-right.
- New mode config entry `Modes.LogGrid`: enabled by default, chord key `OemComma`.
- LogGrid key-count policy is mode-scoped soft-fail and evaluated at coordinator bootstrap (then reused by session activation): when configured axis keys exceed 10, use first 10; when fewer than 10 on either axis, LogGrid is marked unavailable for this run with warning while other modes remain available.
- If LogGrid is unavailable, runtime behavior is non-fatal: LogGrid is excluded from chord dispatch map; if default mode is unavailable, coordinator falls back to first enabled runnable mode (UniformGrid priority) with warning.
- LogGrid uses dedicated `LogGridBaseSize` config under `Modes.LogGrid` (default 10), separate from LogCrosshair `LogBaseSize`.
- Config schema version bumps from 2 to 3; migration upgrades existing configs by adding `modes.logGrid` defaults and `logGridBaseSize` where missing.
- Chord dispatch and mode switching follow existing coordinator pattern (no special-case branch logic).
- Zero build warnings is mandatory at each step (nullable-safe code, no dead private members, analyzer-clean tests).

## Requirements

| ID | Requirement | Acceptance Criteria | Phases/Steps |
|----|-------------|---------------------|--------------|
| REQ-1 | Add LogGrid mode to runtime mode list and mode switching | Given `modes.logGrid.enabled=true`, pressing chord `,` switches to LogGrid session and renders grid | 2.4, 3.2 |
| REQ-2 | Two-key selection recenter loop | When first key + second key are pressed, cursor moves to selected cell center, grid recenters, and waits for further input | 2.2, 2.3 |
| REQ-3 | Arrow navigation without immediate recenter | Arrow keys move selection highlight only; no recenter until Enter or action key | 2.2, 2.3 |
| REQ-4 | Enter semantics | Enter recenters only for arrow-selected cell; Enter in post-two-key state does nothing | 2.2, 2.3 |
| REQ-5 | Escape semantics | Escape closes overlay and restores original cursor position regardless of current recentered position | 2.2, 2.3 |
| REQ-6 | Explicit action dispatch | Action key fires click/action at current selected center; if no selection, uses original activation position | 2.2, 2.3 |
| REQ-7 | First-key corner indicator | After first key, large indicator is rendered in opposite screen corner to current grid-center quadrant; exact-center uses bottom-right | 2.1, 2.2, 2.3 |
| REQ-8 | LogGrid config defaults and schema | Config adds `modes.logGrid` (enabled, chord `,`) with per-mode `logGridBaseSize` default 10, represented in schema | 1.1, 3.2 |
| REQ-9 | Key-count fallback and warnings | If keys > 10, first 10 are used; if keys < 10 on either axis, LogGrid is unavailable for the run and warning shown at start, without disabling other modes | 1.2, 2.2, 2.4 |
| REQ-10 | Migration compatibility | Existing v2 config upgrades to v3 preserving user settings and adding LogGrid defaults without destructive overwrite | 1.3, 3.2 |
| REQ-11 | Factory and app wiring | `ModeSessionFactory` and `App.xaml.cs` construct and inject `ILogGridRenderer`/`LogGridSession` via existing pattern | 2.4, 3.2 |
| REQ-12 | Test coverage mirrors LogCrosshair structure | Session/state/coordinator tests cover happy path + edge cases (escape, blocked activation, enter-ignore, warnings) | 3.1, 3.2 |
| REQ-13 | Analyzer-clean implementation | Every step builds with zero warnings in `Klikety` and `Klikety.Tests` | 1.1, 1.2, 1.3, 2.1, 2.2, 2.3, 2.4, 3.1, 3.2 |

## Risks

| ID | Risk | Likelihood | Impact | Mitigation | Steps |
|----|------|------------|--------|------------|-------|
| RISK-1 | Ambiguous state transitions between two-key flow and arrow flow | Medium | High | Introduce explicit LogGrid session state enum and table-driven transition tests before integration | 2.2, 3.1 |
| RISK-2 | Config migration regressions for existing users | Medium | High | Version bump to 3 with idempotent migrator tests for v2->v3, mixed/missing fields, and rollback-safe writes | 1.3, 3.2 |
| RISK-3 | Key fallback confusion (trim vs block) | Medium | Medium | Keep policy scoped to LogGrid activation only and emit structured warning message once per activation | 1.2, 2.2, 2.4, 3.2 |
| RISK-4 | Corner-indicator placement inconsistency near center | Medium | Medium | Implement deterministic quadrant helper with exact-center fallback and dedicated unit tests | 2.1, 3.1 |
| RISK-5 | Rendering churn from frequent recentering | Low | Medium | Reuse existing pooled renderer path and avoid per-input object allocations in session logic | 2.2, 2.3 |

## Phase 1: Config + Migration Foundation
<!-- worktree: (recorded by /ci when worktree is created) -->

- [x] 1.1 Extend config models and schema: add `ModesConfig.LogGrid` defaults (`Enabled=true`, `ChordKey=OemComma`, `ArrowKeys=true`, `TwoKey=true`, `LogGridBaseSize=10`) and update schema/contracts for per-mode `logGridBaseSize`; update `config.schema.json` with version `3` fields and examples; extend `ValidateModes` entry set to include LogGrid and preserve nullable-safe init-only properties to remain warning-free. (REQ-8, REQ-13) `M`

- [x] 1.2 Add LogGrid key-policy validation: mode-scoped helper computes effective axis keys during bootstrap and exposes cached effective keys/availability for session activation (`Take(10)` if >10, mark only LogGrid unavailable if <10); ensure no path traversal/injection concerns by constraining to in-memory key arrays only; emit one startup warning via existing violations/tray notification pipeline (no duplicate runtime warnings) without blocking other modes. (REQ-9, REQ-13, RISK-3) [after: 1.1] `M`

- [x] 1.3 Implement config migration v2->v3 in `ConfigMigrator`: add missing `modes.logGrid` block and per-mode `logGridBaseSize`, patch both the regular v2->v3 path and the legacy no-`modes` normalization branch, preserve user overrides, keep idempotent atomic write pattern, fail closed for `configVersion > 3`, and update migrator tests for mixed-shape and already-v3 inputs. (REQ-10, REQ-13, RISK-2) [after: 1.1] `M`

## Phase 2: Runtime Integration (Session + Renderer Feedback + Wiring)
<!-- worktree: (recorded by /ci when worktree is created) -->

- [x] 2.1 Extend LogGrid renderer API for runtime UX: update `ILogGridRenderer` contract, `LogGridRenderer` implementation, and `FakeLogGridRenderer` call recording to support first-key indicator rendering (large glyph) and quadrant-corner helper (`(-1,-1)->(1,1)`, `(1,-1)->(-1,1)`, `(-1,1)->(1,-1)`, `(1,1)->(-1,-1)`, center->bottom-right); keep pooled element usage and outlined text path for readability. (REQ-7, REQ-13, RISK-4) [after: 1.1] `L`

- [x] 2.2 Add `LogGridSession : IModeSession` with explicit state machine: `AwaitInput`, `FirstKeySet`, `ArrowCellSet`, `PostTwoKeyRecenter`; implement a closed transition table for every state × key class (first, second, arrow, Enter, Escape, action, invalid), including two-key recenter loop, arrow move-no-recenter, Enter on arrow recenter, Enter ignored in `PostTwoKeyRecenter`, Escape close+origin restore, action explicit dispatch, and mode-scoped key-policy warning/activation blocking for <10 keys; use allocation-light state updates (reuse grid arrays returned by calculator, avoid per-key temporary collections). (REQ-2, REQ-3, REQ-4, REQ-5, REQ-6, REQ-9, REQ-13, RISK-1, RISK-3, RISK-5) [after: 1.2, 2.1] `L`

- [x] 2.3 Integrate session feedback rendering: wire `LogGridSession` to `ILogGridRenderer` methods for full grid render, highlighted first-key indicator, arrow-cell highlight, and invalid-key flash; ensure exact-center indicator fallback rule is applied from quadrant helper; maintain zero-warning event subscriptions/unsubscriptions. (REQ-1, REQ-3, REQ-4, REQ-7, REQ-13, RISK-4, RISK-5) [after: 2.2] `L`

- [x] 2.4 Wire mode into factory/coordinator/app bootstrap: update `ModeSessionFactory` signature and switch map with `"LogGrid"`, construct `LogGridRenderer` in `App.xaml.cs`, include LogGrid in mode-validation/chord-uniqueness/build-chord-map paths and coordinator chord dispatch with existing lock semantics, and implement non-fatal unavailable-mode handling (exclude unavailable LogGrid from chord map; fallback default mode selection to first enabled runnable mode with warning) while allowing trimmed-key fallback (>10). (REQ-1, REQ-9, REQ-11, REQ-13, RISK-3) [after: 1.2, 2.3] `M`

## Phase 3: Tests + Verification
<!-- worktree: (recorded by /ci when worktree is created) -->

- [x] 3.1 Add LogGrid session/state tests modeled after LogCrosshair suite: verify the closed transition table (every state × key class), two-key recenter loop, arrow no-recenter, Enter behavior split, Escape origin restore, explicit action dispatch semantics, first-key corner mapping (incl. exact-center), and no unexpected state carryover. (REQ-2, REQ-3, REQ-4, REQ-5, REQ-6, REQ-7, REQ-12, REQ-13, RISK-1, RISK-4) [after: 2.3] `L`

- [x] 3.2 Add integration/config tests: factory creates LogGrid session; coordinator chord switch works; mode validation/chord uniqueness includes LogGrid; non-fatal unavailable-mode fallback works for chord/default selection; v2->v3 migration passes; key-policy fallback/warnings behave as specified (>10 trimmed, <10 unavailable warning for LogGrid only); run full `Klikety.Tests` with zero warnings. (REQ-1, REQ-8, REQ-9, REQ-10, REQ-11, REQ-12, REQ-13, RISK-2, RISK-3) [after: 1.3, 2.4, 3.1] `L`
