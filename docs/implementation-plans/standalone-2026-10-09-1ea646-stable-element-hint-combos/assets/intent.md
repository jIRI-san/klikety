# Intent

## Goal

Reduce combo churn when reopening hints on the same window, without slowing
progressive discovery or building a semantic control-matching system.

## User direction

2026-10-09: "create implementation plan for the recommended solution, keep it
simple, best effort, nothing too complicated"

2026-10-09: "ok, commit the plan, then implement it"

The user approved implementation after a quick review. The two review
clarifications pin the active logical page and remember all allocated
assignments, including off-page entries and previously visited levels.
The simplification remembers exact assignments, not old hierarchies.

## Desired outcome

Surviving controls in unchanged navigation levels usually retain their combos
across consecutive reopenings, including after helper idle retirement.

## Success signals

Exact identity matches in unchanged levels keep their physical VKeys across
reordered batches. Missing reservations never select old targets, late earlier
pages never interrupt typing, and window/record retention stays bounded.

## Non-goals

- Persistence across Klikety restarts or guaranteed application-restart stability.
- Names, text, AutomationId collection, fuzzy/position matching or app adapters.
- Replaying groups, migrating controls between groups, or stable combos when
  provider identities, grouping, key arrays or viewport/capacity change.
- Discovery/cache redesign, new dependencies or new configuration fields.

## Definition of done

Bounded in-memory reuse, early publication, current-target-only actions, focused
regressions and updated docs. Changed/rebuilt UI may reset assignments explicitly
within the documented best-effort contract.
