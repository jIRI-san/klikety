---
name: cip
description: 'Create Implementation Plan — use when planning a new feature, designing an implementation, starting development work, or resuming/refining an existing plan. Conducts thorough requirements gathering across all aspects (goals, constraints, API surface, error handling, testing, observability, security, performance), drafts a phased plan with step-level tracking, runs iterative design review via @dr, and saves to docs/implementation-plans/. Invoke with /cip <name> for a new plan or /cip <slug> to resume an existing one.'
argument-hint: 'Plan name for a new plan (e.g. "data persistence"), or an existing plan slug to resume (e.g. "001-data-persistence")'
user-invocable: true
disable-model-invocation: true
---

# Create Implementation Plan

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

Do not proceed to drafting until you have solid answers to all of the following. Ask follow-ups on vague or incomplete answers — push for specifics.

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

**Performance**
- Expected throughput, latency targets, or load concerns?

**Migration / rollout**
- Feature-flagged? Backward-compatible?
- Any one-time migration steps?

Once all areas are covered, present a structured summary back to the user and ask: **"Does this capture everything? Anything to add or correct?"** — wait for confirmation before drafting.

## Step 4: Draft Plan

Build the plan document using the template at [./assets/plan-template.md](./assets/plan-template.md).

Guidelines:
- **Decisions** — record key choices made during the interview (e.g. "Use feature flag X to gate rollout").
- **Requirements table** — one row per requirement, ID format `REQ-N`.
- **Phases** — group related steps logically (e.g. "Phase 1: Data layer", "Phase 2: API", "Phase 3: Tests").
- **Steps** — title + requirement reference only; no implementation prose. Keep it scannable for human review.
- **Dependencies** — for each step, note which earlier steps it depends on using `[after: X.Y]` suffix. Steps with no dependency annotation (or `[after: none]`) can start immediately and run in parallel with other independent steps.
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
