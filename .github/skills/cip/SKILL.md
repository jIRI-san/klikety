---
name: cip
description: 'Create Implementation Plan — use when planning a new feature, designing an implementation, starting development work, or resuming/refining an existing plan. Conducts thorough requirements gathering across all aspects (goals, constraints, API surface, error handling, testing, observability, security, performance), drafts a phased plan with step-level tracking, runs iterative design review via @dr, and saves to docs/implementation-plans/. Invoke with /cip <name> for a new plan or /cip <slug> to resume an existing one.'
argument-hint: 'Plan name for a new plan (e.g. "data persistence"), or an existing plan slug to resume (e.g. "001-data-persistence")'
user-invocable: true
disable-model-invocation: true
---

# Create Implementation Plan

> **Goal:** produce a plan concrete and precise enough that each step can be executed with minimal ambiguity. Eliminate uncertainty during the interview — don't defer it to implementation. If an answer is vague, dig deeper. If a design choice is open, resolve it now. The plan should read as a clear checklist, not a wishlist.

## Step 1: Load Context

1. Read `docs/design-notes/.design-notes.md` to get the index of all available design notes.
2. Identify which subsystems are likely touched by this plan (from argument or prior chat context).
3. Load the relevant design notes to ground the planning session.

## Step 2: Locate or Create Plan File

Scan `docs/implementation-plans/` for existing `*.md` files (exclude `archive/`):

- **Argument matches an existing file slug** → load that file; enter *resume mode* (skip Step 3 if plan is already well-specified; otherwise re-interview for gaps).
- **No argument and plans exist** → list them with a one-line status summary; ask the user which to work on (or "new").
- **"new" or no existing plans** → ask for the plan name, derive a kebab-case slug, assign the next sequential number `NNN`, target file: `docs/implementation-plans/NNN-implementation-plan-<slug>.md`.

## Step 3: Interview User

Do not proceed to drafting until you have solid answers to all of the following. Ask follow-ups on vague or incomplete answers — push for specifics. Treat every "TBD", "maybe", or "we'll figure it out later" as a blocker: resolve it now or record it as an explicit risk with a mitigation.

**Goals & scope**
- What behaviour or capability is being added or changed?
- What is explicitly out of scope?

**Requirements**
- What are the functional requirements? List them individually.
- What are the non-functional requirements (performance, scale, SLA)?

**Affected subsystems**
- Which source files, services, or components need to change?
- Are there data model changes (schema, EF migrations)?

**API surface**
- New endpoints, messages, or events? Request/response shape?
- Breaking changes to existing APIs?

**Error handling**
- What failure modes exist? How should each be handled?
- Retry policies, fallback behaviour, partial-failure semantics?

**Testing strategy**
- Unit tests, integration tests, or both?
- New Testcontainers-based fixtures needed?

**Observability**
- What structured log entries are needed?
- New metrics or health-check impacts?

**Security**
- Auth/authz implications?
- Any data sensitivity concerns?

**Corner cases**
- What edge/corner cases could break expected behaviour?
- Boundary conditions, race conditions, empty/null inputs, unusual user flows?
- How should each corner case be handled — error, fallback, or explicit design choice?

**Performance**
- Expected throughput, latency targets, or load concerns?

**Migration / rollout**
- Feature-flagged? Backward-compatible?
- Any one-time migration steps?

**Acceptance criteria**
- For each functional requirement and corner case: what is the concrete, verifiable condition that proves it works?
- Express each criterion as a testable statement (e.g. "When X, then Y", "Given A, expect B").
- Cover both happy-path and failure/edge-case outcomes.

**Roles**
- For each step, who executes it? Assign one of:
  - `@ai-agent` — the AI agent implements this step autonomously (code changes, tests, config).
  - `@human` — a human performs this step (portal configuration, manual verification, external system setup, license activation, etc.).
- Default is `@ai-agent` if not specified. Ask explicitly for any step that might require human action.

**Estimation**
- For each step, assign a T-shirt size: `S` (< 30 min), `M` (30 min – 2 h), `L` (2 h+).
- Sizes are rough guidance, not commitments. Push back if the user skips sizing entirely.

**Risks**
- What could block or derail this plan? Think beyond corner cases: external dependencies, API rate limits, licensing, unclear requirements, tooling gaps.
- For each risk: likelihood (Low/Medium/High), impact (Low/Medium/High), and mitigation or contingency.

**Rollback**
- For `@ai-agent` steps: git revert is assumed. No special guidance needed unless the step has side effects beyond code (e.g. database migrations, published packages).
- For `@human` steps: what is the undo procedure? (e.g. "Delete the resource group", "Revert the portal setting to X").
- For steps with no clean rollback: note this explicitly as a risk.

Once all areas are covered, present a structured summary back to the user and ask: **"Does this capture everything? Anything to add or correct?"** — wait for confirmation before drafting.

## Step 4: Draft Plan

Build the plan document using the template at [./assets/plan-template.md](./assets/plan-template.md).

Guidelines:
- **Decisions** — record key choices made during the interview (e.g. "Use feature flag X to gate rollout").
- **Requirements table** — one row per requirement, ID format `REQ-N`. Corner cases identified during the interview become requirements too (e.g. `REQ-7 Handle empty grid when monitor is disconnected`). Each requirement must have at least one acceptance criterion in the `Acceptance Criteria` column. The `Phases/Steps` column must list every step that addresses this requirement.
- **Phases** — group related steps logically (e.g. "Phase 1: Data layer", "Phase 2: API", "Phase 3: Tests").
- **Steps** — each step line references all related IDs in parentheses: `(REQ-1, REQ-3, RISK-2)`. No implementation prose. Keep it scannable for human review.
- **Roles** — each step is tagged `@ai-agent` or `@human`. Default is `@ai-agent`; only annotate `@human` explicitly. For `@human` steps, add a `Details` sub-section under the step with actionable guidance (portal navigation, CLI commands, manual verification instructions, etc.).
- **Estimation** — each step gets a T-shirt size: `S`, `M`, or `L`.
- **Dependencies** — for each step, note which earlier steps it depends on using `[after: X.Y]` suffix. Steps with no dependency annotation (or `[after: none]`) can start immediately and run in parallel with other independent steps.
- **Risks** — populate the Risks table from the interview. One row per risk, ID format `RISK-N`. The `Steps` column must list every step affected by or mitigating this risk. Steps that relate to a risk must also reference the `RISK-N` ID in their parentheses.
- **Cross-reference integrity** — every `REQ-N` must appear in at least one step; every `RISK-N` must appear in at least one step; every step must reference at least one `REQ-N`. If any ID is orphaned (not linked to a step), either add a step or remove the ID.
- **Rollback** — for `@human` steps and steps with non-code side effects, record rollback instructions in the step's `Details` section.
- Status markers: `[ ]` TODO · `[x]` DONE · `[~]` IN-PROGRESS

## Step 5: Save Plan

- **Agent mode** (can write files): write the plan to `docs/implementation-plans/NNN-implementation-plan-<slug>.md`; update after each planning iteration.
- **Plan mode** (read-only): maintain the plan content in session memory at `/memories/session/plan.md`; at the end offer a handoff to agent mode to persist to disk.

## Step 6: Design Review (Iterative)

1. Invoke `@dr` passing the plan file path (or session memory content).
2. Apply all agreed findings as changes to the plan; update the Decisions section.
3. If `@dr` raised any **High** or **Critical** findings that required substantial plan changes, run `@dr` again on the updated plan.
4. Repeat until no High/Critical findings remain, or the user explicitly approves proceeding.

## Step 7: Finish

- Confirm final plan is saved.
- Ask: **"Ready to start implementation? Use `/ci` to begin."**
