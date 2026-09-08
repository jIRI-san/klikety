# Architecture Notes

Interface-level architectural contracts and decision records (ADRs) for this project. This
tier is the **AI-optimized source of truth**: terse, contract-focused, and auto-loaded by
`/cip` and `/ci`. It captures the **unbreakable, high-level contracts** a human owns — the
*what* and the boundaries — while implementation detail lives in `docs/design-notes/`.

> Markdown notes are the human-readable architecture. Legacy JSON contracts are validated directly
> and do not produce a generated Markdown mirror.

## How These Work

- Load this index first. Then read only the arch note(s) and contract(s) relevant to the task.
- Legacy JSON contracts follow the documented architecture-notes convention; locked content remains
  digest-pinned. New boundaries should live directly in terse Markdown notes.
- `locked` Markdown contracts are reviewer-approved; `draft`/`provisional` are advisory.
  Transferred locked JSON contracts remain digest-pinned until converted.

## Maintenance Protocol (Required)

When a change alters an interface-level contract or an architectural boundary, update the
corresponding Markdown note **in the same change**. Treat this as part of the definition of done,
not optional follow-up.

Keep the auto-loaded tier lean: retire superseded ADRs (archive/summarize) so only **active**
decisions remain here.

## Contracts

| Contract Id | Title | Maturity | Applies To | Note |
|---|---|---|---|---|
| _none yet_ | | | | |

## Architecture Notes

| File | Scope | Contracts |
|---|---|---|
| _none yet_ | | |

## Decision Records (active)

| ADR | Decision | Status | Date |
|---|---|---|---|
| _none yet_ | | | |

## Adding a Contract or Note

1. Add one terse Markdown note from the arch-note template.
2. Add rows to the tables above.
3. Use the update flow (`/uan`) for later changes.
