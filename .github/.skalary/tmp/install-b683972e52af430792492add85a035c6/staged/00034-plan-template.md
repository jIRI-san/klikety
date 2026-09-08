# <plan-id>: Plan Title
<!-- plan-id: <6hex> -->
<!-- Folder naming: <epic-id|standalone>-<yyyy-mm-dd>-<6hex>-<slug> · plan-id is the canonical handle (date/slug/hash all resolve via Resolve-Plan). New-Plan.ps1 fills these in. -->

<!-- Optional execution metadata — defaults used by /ci mode selection -->
<!-- execution-mode: manual | host-autopilot | container-autopilot | sandbox-autopilot -->
<!-- scope: step | phase | plan -->
<!-- evidence: required -->
<!-- phase-budget-points: 6 -->
<!-- Offline package bundling (autonomous container/sandbox plans): list expected new third-party packages so they can be batched and the offline rebundle round-trip fires at most once. Use `none` when the plan adds no packages. -->
<!-- expected-packages: dotnet:<list>; npm:<list> -->

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

## Phase 1: Name
<!-- worktree: (recorded by /ci when worktree is created) -->
<!-- Steps with no [after:] annotation can start immediately and run in parallel. -->
<!-- Roles: @ai-agent (default, not annotated) or @human (explicit).
     Nontrivial AI steps carry a compact details block with Outcome, Likely touchpoints, Constraints,
     Verify, and—when uncertain or high risk—Stop/escalate when. Omit it for self-explanatory S work. -->
<!-- Sizes: S (< 30 min) · M (30 min – 2 h) · L (2 h+) -->
<!-- Point legend: S=1, M=2, L=3 (phase-budget cap comes from the phase-budget-points marker; default 6) -->

- [ ] 1.1 Step title (REQ-1) `M`
  <details><summary>Implementation contract</summary>

  **Outcome:** the observable state this step must leave behind.

  **Likely touchpoints:** expected files, components, or interfaces; guidance, not a closed file list.

  **Constraints:** relevant compatibility, behavior, and boundary requirements.

  **Verify:** the focused evidence that proves the outcome.

  **Stop/escalate when:** concrete unresolved evidence or risk requiring a stronger model/operator; omit when none.

  </details>
- [ ] 1.2 Step title (REQ-1, RISK-1) @human `M`
  <details><summary>Details</summary>

  **Steps:**
  1. Navigate to **Azure Portal > Resource Group > ...**
  2. Run: `az resource ...`

  **Verify:** the concrete, observable condition that proves the step worked.

  **Rollback:** Delete the resource / revert the setting to X.

  </details>

## Phase 2: Name
<!-- worktree: (recorded by /ci when worktree is created) -->

- [ ] 2.1 Step title (REQ-1, RISK-1) [after: 1.1] `S`

## Finalization (conditional)

<!-- Every @human step needs a <details> block carrying **Steps**, **Verify**, and **Rollback** —
     Test-Plan.ps1 fails the plan without it, and /ci prints the block verbatim at the handoff. -->

- [ ] X.Y Finalization gate (REQ-1) @human `S`
  <details><summary>Details</summary>

  **Steps:**
  1. What the operator has to do, in order.

  **Verify:** what proves it worked.

  **Rollback:** how to undo it.

  </details>
