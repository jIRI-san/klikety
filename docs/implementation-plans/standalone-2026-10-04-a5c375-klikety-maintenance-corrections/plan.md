# a5c375: Klikety maintenance corrections
<!-- plan-id: a5c375 -->
<!-- cip-stage: implementing -->
<!-- planning-confirmed: sha256:9124e6ecb7f0e898ad13d5d0dc07023674c5421d7912f642cb38ffea76e05942 -->
<!-- execution-mode: manual -->
<!-- scope: phase -->
<!-- evidence: required -->
<!-- phase-budget-points: 6 -->
<!-- expected-packages: none -->

## Assets

`plan.md` holds only the markers above, this index, and the phases/steps below. Everything else lives under `assets/` and is loaded on demand — never wholesale.

- Intent — [assets/intent.md](assets/intent.md)
- Domain model — [assets/domain.md](assets/domain.md)
- Approved design — [assets/design.md](assets/design.md)
- Requirements — [assets/requirements.md](assets/requirements.md)
- Risks — [assets/risks.md](assets/risks.md)
- Decisions — [assets/decisions.md](assets/decisions.md) (extended rationale in `assets/decisions/<topic>.md`)
- References — [assets/references.md](assets/references.md)
- Review results — advisory `assets/reviews/phase-<N>.md` and `assets/reviews/final.md`
- AI-credit ledger — `assets/ai-credits.json` (created by autonomous execution)

A subfolder is created only when a concern needs more than one file (`assets/decisions/`, `assets/logs/`); single-file concerns stay flat under `assets/`.

## Phase 1: Cancel pending macro input and clarify timing

- [x] 1.1 Make indicator cancellation prevent pending dispatch, with regression coverage and docs (REQ-1, RISK-1) `L`
  <details><summary>Implementation contract</summary>

  **Outcome:** cancelling a pending indicator completes playback as cancelled, skips that action and progress notification, hides the indicator and releases its subscriptions. Playback teardown initiates cancellation without synchronously draining on the dispatcher; operation-owned cleanup releases CTS/task state and stale completions cannot mutate a replacement operation.

  **Likely touchpoints:** MacroPlayer, MacroHandler, IClickIndicator, ClickIndicatorAdapter/Window and a composed internal lifecycle seam, TestFakes, MacroPlayerTests, macros.design.md.

  **Constraints:** check the token after awaited work immediately before dispatch, including MoveOnly/no-indicator paths. Keep UI cleanup dispatcher-owned; no synchronous dispatcher wait from a cancellation callback. Cancellation does not undo input already dispatched.

  **Verify:** deterministic pending-indicator cancellation tests for click/drag/scroll, cancellation just after completion, pre-cancelled/no-indicator paths, normal completion, and pure lifecycle tests with fake dispatcher/view operations for cancellation/completion races, disposal, hide/unsubscribe and reuse. No live WPF/STA harness.

  **Stop/escalate when:** resource disposal cannot be made nonblocking with operation-owned completion without changing another confirmed lifecycle contract. Do not retain the known synchronous drain or hide it with timeouts.

  </details>
- [x] 1.2 Correct RelativeTimeMs documentation without changing saved-format semantics (REQ-2) `S`

## Phase 2: Propagate native input failure end to end

- [x] 2.1 Add typed input outcomes and safe partial-send handling through all callers (REQ-3, REQ-4, RISK-2, RISK-3) [after: 1.1] `L`
  <details><summary>Implementation contract</summary>

  **Outcome:** failed movement suppresses a dependent click; partial input triggers bounded release cleanup; callers receive an explicit failure; macros stop on failed input rather than report completion.

  **Likely touchpoints:** MouseActionService, ServiceInterfaces, MacroPlayer/Handler, ActionDispatcher, NavigatorCoordinator, ScrollHotKeyService, App bootstrap, test fakes and smoke call sites.

  **Constraints:** use a typed internal sender and geometry/delay seams for deterministic tests. Preserve Win32 INPUT layout, virtual-screen normalization, modifier ordering and native drag phase delays. Await drag results without blocking the dispatcher; start the next saved interval after completion, intentionally removing the old overlap. Release-only compensation is attempted once; failed cleanup remains explicit. Do not replay a partially sent action.

  **Verify:** requested/sent-count and cleanup-failure tests; left/right/middle/double clicks with/without modifiers; move, scroll, drag phases and ClearStuckModifiers; macro failure stops later steps with no success progress for the failed step; drag-to-action sequencing proves the next interval starts after completion; non-macro callers observe failure.

  **Stop/escalate when:** return-type changes require a new lifecycle/recording policy beyond asynchronous result observation; bring that choice to the operator before altering recording contents or overlay restoration.

  </details>
- [x] 2.2 Complete regression coverage and update interop/macro/testing notes (REQ-1, REQ-2, REQ-3, REQ-4, RISK-1, RISK-2, RISK-3) [after: 2.1, 1.2] `M`
  <details><summary>Implementation contract</summary>

  **Outcome:** input safety and failure reporting are demonstrated with hermetic tests and documented API contracts.

  **Likely touchpoints:** Klikety.Tests, Klikety.SmokeTests compilation, macros.design.md, win32-interop.design.md, testing.design.md.

  **Constraints:** no real input injection in unit tests; no new dependencies or unrelated interop cleanup.

  **Verify:** focused xUnit selectors for changed services/callers and macro/recording regressions; build the app and both test projects; compare successful input batches and lifecycle behavior to baseline.

  </details>

## Phase 3: Repair the duplicated plan record

- [ ] 3.1 Establish historical ownership, repair identities/assets/links, and verify supported lookup (REQ-5, RISK-4) `L`
  <details><summary>Implementation contract</summary>

  **Outcome:** both retained keyboard-layout records have distinct canonical identities; intended assets and historical links resolve; supported lookup for 000021 returns one full state.

  **Likely touchpoints:** the active and archived 021-keyboard-layout-refresh records, their assets and directly referring documents; existing read-only inventory/state helpers; a focused scripts/Test-KeyboardLayoutPlanRecords.ps1 check.

  **Constraints:** preserve historical content and completion evidence. Use current file comparison and relevant git history to establish ownership; do not infer an archive decision from checked steps. Do not change installed planning tools, move a plan into the archive, or delete a record.

  **Verify:** implement/run the REQ-5 scoped executable check; retain actual command outcome as test:PlanIdentityRepairChecks evidence. It asserts index uniqueness, supported state identity/path/full-state results, and local links across touched records/assets and affected incoming references. File existence is not acceptance evidence.

  **Stop/escalate when:** evidence cannot establish which record retains 000021, assets cannot be recovered faithfully, or references require a compatibility choice. Present the concrete alternatives and obtain an operator decision before mutation.

  </details>
