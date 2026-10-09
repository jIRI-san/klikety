# Element hints performance [DONE]

Archived on 2026-10-08 at the user's request after implementation and the
code-review fixes. Recorded evidence and provider/acceptance limits remain unchanged.

## Accepted scope

- Breadth-first discovery; show leaf controls first and expand smaller sibling
  branches before larger ones.
- Publish bounded batches while discovery continues. Never remap a displayed
  label, active prefix, selection, or entered navigation level.
- Collapse branches with more than a configurable number of immediate UIA
  control-view children (default 10). Discover their contents on demand.
- Allow validation between discovery slices; showing early labels must also
  make them usable early.
- Reuse unchanged subtrees using structure/property notifications, conservative
  invalidation, and bounded-age refresh. Keep fresh action validation.
- Limit reuse to a configurable number of recently used **windows** (default 5,
  0 disables caching). Multiple windows of an app count separately.
- Preserve the foreground-root boundary, single owned helper, watchdogs,
  geometry/identity checks, and explicit partial/failure reporting.

## Implementation

1. Add a resumable breadth-first traversal. Probe immediate children once,
   retain the enumeration within each scope, classify branches as 0, 1-10, or
   >10, and prioritize known small branches. Opening a deferred group refreshes
   its child enumeration. Preserve canonicalization of complete actionable
   subtrees without additional provider calls.
2. Add bounded discover/continue/expand exchanges. A discovery slice returns
   before the next request; validation can interrupt at that boundary. Keep the
   one-shot discovery API for existing diagnostic/package consumers.
3. Integrate progressive levels into the overlay session. Append new entries
   without changing existing keys; freeze single/pair scheme on first content.
   Load deferred groups on entry and ignore retired/late completions.
4. Keep an event-observing helper alive for bounded idle reuse. Store a window
   LRU with per-window node limits; invalidate only the affected branch where
   known, otherwise invalidate the window. Event callbacks only enqueue dirty
   identities. Use bounded-age refresh for missing provider notifications.
5. Expose `groupChildThreshold` (1-1000, default 10) and `cacheWindowCount`
   (0-20, default 5) alongside the existing discovery deadline in JSONC, schema,
   migration, runtime validation and native Settings.
6. Add hermetic traversal, label-stability, protocol, interruption, invalidation,
   eviction, settings, and lifecycle regressions. Build, run targeted tests and
   formatting, and update the relevant design notes.

## Delivery

Implemented all six steps. The compatibility one-shot API also uses breadth-first
provider discovery; depth-first replay is limited to captured metadata.
Settings and JSONC expose both new limits without changing config version 9.
Progressive hints keep their keys through later batches, lazy expansion and
parent-scope resume. The helper reuses unaffected cached panes, including when UIA
returns new wrappers, and retains no more than the configured window LRU.

Completed evidence:

- Solution build: no warnings or errors.
- Initial implementation unit suite: 1612 passed; one unrelated macro-teardown test failed with
  `"Expected: Idle"` / `"Actual: Playing"`. The same test failed in isolation.
  Its test and `MacroHandler` are unchanged from `HEAD`: playback completion posts
  UI restoration to the captured synchronization context without awaiting that
  post, while the test asserts the restored state immediately after awaiting the
  playback task. No unrelated macro fix or test weakening was made. All 1612
  remaining tests passed in a separate run.
- Controlled WPF progressive-worker smoke: passed with real IPC, cached helper
  reuse, idle property invalidation and control replacement/structure invalidation.
- Regressions cover breadth-first/small-parent order, the 10/11-child boundary,
  deferred and nested groups, early actions, Escape/resume, stable labels/tokens,
  byte limits without removing published hints, cache age/node/window limits,
  settings round trips and owned-helper cleanup. Invalid ancestor metadata and
  failed child enumeration retain known valid descendants without claiming
  complete ancestry.
- Changed C# files formatted with `dotnet format`; related design notes updated.

## Code-review follow-up

Both reported findings are fixed:

- Expanding the current scope now resumes it, matching parked-scope behavior.
  Ignored replies followed by reopening cannot duplicate nested groups or assign
  their controls to competing discovery scopes.
- Cache refresh resolves dirty identities and roots before removing nodes, then
  coalesces duplicate and ancestor-covered roots. Known event batches preserve
  unrelated panes and do not leave a sibling stale when nested roots overlap.
  Genuinely unknown identities still invalidate the window.

Six new regression cases reproduced the defects before the fixes. They cover
incomplete/completed active groups, parked-scope resume, stable nested groups and
tokens, unchanged navigation work, pane/child notifications in both orders,
overlapping pane roots, new UIA wrappers and fresh disabled-control state.
The 136 directly affected traversal/cache/protocol/session/supervisor cases pass.
The solution builds without warnings/errors, and the real WPF progressive-worker
cache/property/structure-invalidation smoke passes with these fixes. Changed C#
files are formatted.

## Evidence boundaries

Provider events are hints, not a complete change log. Reusing a cache does not
authorize input. No universal provider coverage, clinical accessibility claim,
host input injection, or third-party latency result is implied by synthetic
tests. Live acceptance must distinguish first usable hint, completed discovery,
cached reopen, and action latency.
No representative VS Code/Electron latency measurement or physical-input
acceptance is claimed by this delivery. Provider event granularity can require
whole-window invalidation; the bounded-age refresh backs up missed events.
