---
description: Architecture note for <SUBSYSTEM> — its interface-level boundary and the contracts that govern it. Load when working on code under the scope globs below.
globs:
  - "<SCOPE_GLOB>"
---

# <SUBSYSTEM> — Architecture Note

<!-- Terse. Describe the boundary and the unbreakable contracts, not implementation detail.
     Implementation guidance belongs in docs/design-notes/. Keep this note context-cheap. -->

## Boundary

<BOUNDARY_PROSE>

## Contracts

<!-- Reference the machine-checkable contract ids that govern this boundary. -->

| Contract Id | Maturity | Enforces |
|---|---|---|
| `<CONTRACT_ID>` | draft | Boundary and invariants in this note |

## Invariants

- Preserve the boundary above.

## Depends On / Depended On By

- None recorded.
