---
description: Opt-in foreground-window UI Automation hints, isolated helper, bounded ancestry, adaptive hierarchical labels and validated physical actions.
globs:
  - src/Klikety/Automation/**
  - src/Klikety.UiaWorker/**
  - src/Klikety/Navigation/ElementHintsSession.cs
  - src/Klikety/Navigation/ElementHintHierarchy.cs
  - src/Klikety/Overlay/ElementHintsRenderer.cs
  - src/Klikety.WorkerFixture/**
  - scripts/Test-UiaWorkerPackage.ps1
---

# Element Hints

`modes.elementHints` is disabled by default. Enable it with `twoKey` true
(adaptive one/two-key labels) and keep UniformGrid enabled. `arrowKeys` optionally
enables focus navigation, not paging. The default chord is Tab. Config version 9 adds
the disabled block without rebinding existing commands. Explicit conflicts disable
element-mode dispatch and appear in configuration violations.

The coordinator captures the foreground HWND, PID and process start identity
before showing the overlay. SessionManager passes that immutable context through
mode, scope, display and drag transitions. Discovery never enumerates the desktop
or another top-level window. Legitimate cross-process UIA descendants are allowed.

## Process boundary

`Klikety.UiaWorker` is a windowless .NET 10 Windows executable. Its MTA main thread
owns all managed UIA references; async pipe reads finish before UIA calls resume on
that same thread. It uses cached properties and incremental control-view traversal.
The parent contains no UIAutomation assembly dependency.

One lazily created `UiaWorkerSupervisor` per factory serializes scan/validation/retirement. The
absolute bundled `uia-worker\Klikety.UiaWorker.exe` path uses redirected inherited
stdio, no shell or PATH lookup. Before sending a UIA request the parent assigns the
helper to a kill-on-close Windows job. Closing stdin also exits the worker normally.
Startup is off the UI thread and within the discovery deadline. Unreaped startup,
unconfirmed cleanup or unsettled pipe reads prevent replacement workers rather than
accumulating abandoned children/tasks.

| Contract | Limit |
|---|---|
| Discovery, including startup | `modes.elementHints.discoveryTimeoutMs`: default 1500 ms, range 100-60000 ms |
| Validation | 500 ms |
| Retirement | 500 ms cleanup budget |
| Visited nodes / depth / retained targets | 20000 / 64 / 2000 |
| Request / response / diagnostic stderr | 64 KiB / 2 MiB / 64 KiB |
| Runtime identity | 64 integers |
| Pruned complete containers | 4000; ancestry depth bounded by traversal |

The optional discovery field keeps config version 9 and the existing 1500 ms
behavior when absent. Settings exposes it on Navigation's Element hints card.
Invalid values report a configuration violation, suppress hint dispatch and block
Settings saves, including when hints are disabled; they are never clamped or
interpreted as an unlimited timeout. A larger deadline accommodates slow providers
such as Word but does not guarantee coverage or a successful scan. Validation and
retirement remain fixed at 500 ms; cancellation/Enter/Escape still retire the owned
helper promptly instead of waiting for the discovery deadline.

Frames use a four-byte little-endian byte length followed by versioned JSON.
Session/request IDs, limits, finite geometry, clipping and validation approvals
are checked before consumption. Diagnostics contain outcomes/error codes/counts,
not element names, values, passwords or document text. Timeout, provider failure,
access denial, invalid root, no targets and partial scans remain distinct.

Worst-case runtime identities/geometry can fill the byte budget before the target
count cap. The worker keeps the fitting prefix and returns an explicit partial
snapshot with omitted counts. Truncated headers/bodies are protocol errors.
The additive optional `HintTarget.ContainerId`/`HintResponse.Containers` fields
carry navigation metadata, never actionable identities. `HintContainer` records
parent ID, optional own-action token, process, type and full bounds. Parents precede
children; the parent checks references, unique identities, same-process membership,
depth, finite geometry, own-action consistency and at least two retained targets
per container. Byte truncation drops all ancestry claims and resets target
container IDs before retaining the fitting target prefix. It cannot leave dangling
references or claim a truncated subtree is complete.

## Candidates, labels and rendering

Enabled, onscreen, finite, clipped targets require a standard interactive control
type or Invoke/Toggle/SelectionItem/ExpandCollapse/Value capability. Readonly edits
remain focus targets. Focusability alone is insufficient. Runtime identities
deduplicate candidates; names or coincident rectangles alone never do. A fully
traversed, patternless ListItem wrapper folds into its sole remaining Button
descendant only when the button advertises Invoke alone, shares the process, and
has exactly the same full physical bounds. Passive intermediate nodes are allowed.
The button retains its identity, point and token. This handles observed Copilot
list-row/button pairs without changing hit validation or discarding standalone
patternless rows. Rows with their own patterns, multiple action descendants,
different full bounds, different processes or other descendant semantics stay
separate. Independently actionable nested buttons/links/editors/toggles and
coincident siblings remain reachable.

The bounded traversal records control-view parent indices and aggregates
canonical descendant counts bottom-up, saturated at two. It makes no additional
UIA/parent/hit-test calls for canonicalization. Failed/invalid/depth-limited
descendants prevent folding their ancestors. Node/target truncation skips folding
altogether, retaining the existing caps and explicit partial response rather than
assuming unvisited actions are absent. Unrelated invalid branches do not prevent
folding a complete list row. Other provider/proxy overlaps remain ambiguous and
are not generalized from this narrow evidence.

Branch failures and traversal caps report partial results and
omitted branch/target counts, not a claim of complete provider coverage.
`UiaTreeAlgorithms` is the production traversal/identity/hit-testing kernel,
shared by source with hermetic tests. `AutomationTree` supplies cached native UIA
properties and bounded ancestry reads. Invalid interactive-target identity or
geometry contributes an explicit omission; invalid root geometry rejects discovery.

## Adaptive hierarchy

Traversal reuses cached control-view ancestry and reads, aggregates retained counts
and process consistency bottom-up, then emits only complete branching containers
or actionable ancestors with at least two targets. Passive unary chains are pruned.
Incomplete ancestors stay flat; unrelated complete branches can still group in
a partial snapshot. Node/target truncation emits no grouping claims. Canonical
wrapper folding remains the narrow, independent policy above.

`ElementHintHierarchy` is a pure planner over this metadata. `HintEntry` separates
original actionable leaves from negative-ID navigation groups; groups carry child
entries, member-union geometry and presentation metadata, never validation tokens.
It groups an actionable ancestor and its descendants when badges compete:
intersection covers at least 80% of the larger rectangle, or overlapping centers
are within 36x24 physical pixels. Ancestry is required; coincident siblings are not
merged. Both parent-own-action and independent children survive in the next level.
These compound levels keep rectangular key badges beside their controls.
Badge glyphs/borders, control outlines and displaced-badge leaders share accents
and solid/dashed/dotted patterns. Coincident outlines receive separate visual
insets without changing target bounds or action points. Matching patterns,
placement/leaders and the focused/selected role/capability footer supplement
color for colorblind users. Extreme layouts retain a contained scrolling
role/capability list. No accessibility names or document contents are read.

Passive UIA containers are candidates, not mandatory levels. When the flat level
exceeds measured/key capacity, complete containers that reduce it are preferred.
Otherwise ordered capacity chunks create navigation-only groups, recursively only
as needed. At default capacity 100, 250 flat controls yield three groups with
100/100/50 children. With only one readable slot grouping cannot reduce a level,
so PgUp/PgDn paging is the fallback. Every retained target occurs once in the tree.

Targets sort by physical top/left/token. Each frozen level uses configured horizontal
single keys when its entries fit both that axis and measured single-key capacity;
otherwise horizontal-first, vertical-second VKey pairs. Current-layout glyphs come
from `IKeyLabelResolver`. Assignments, level capacity and scheme freeze on entry.
Glyph redraw changes paint metrics but not keys, focus, selection or stack.
Explicit viewport relayout rebuilds L1 from the same snapshot, without rescanning.
The measured font floor and viewport bound level capacity. Hints first try their
target center, clamped to the viewport, then four bounded rings of nearby slots
with collision gaps. An ordinary top-edge target or one overlap does not relocate
every hint. Severe crowding uses a compact right-side page-local list; capacity
also fits that narrower list so initial pages do not hide label rows. Extremely
small viewports/long labels use contained scrolling, never smaller text or silent
target removal. Frozen glyph-only redraws retain all assignments and scroll toward
the selected target or active prefix. Only a displaced selected label gets a
connector; ordinary levels outline only selected, focused or prefix targets.
Compound levels instead outline each visible entry with its badge's association
style, using corner placement before bounded nearby slots. Their six accent/pattern
styles repeat after six entries, not as globally unique target identities.
Black backing contours and at least a 3-DIP black glyph halo preserve contrast;
these compound-only accents/halos intentionally override theme label/outline
colors while retaining theme font, badge fill and prefix/selection opacity.
Unselected labels dim after selection. Capacity and painting share adaptive footer/list regions,
including sub-8-DIP viewports; the initial physical-region fallback is retained
until a measured canvas viewport is available. Labels are window-origin-relative DIPs; click coordinates
remain physical desktop pixels.

Badge background brushes use 40% opacity independently of text and borders, in
both nearby and fallback-list layouts. Existing theme color alpha multiplies that
opacity. Initial, matching-prefix and selected labels retain full-strength
outlined glyphs/borders; the existing nonmatching/unselected dimming still applies.
This makes underlying icons visible without weakening label contrast.

The session passes a typed `isDiscovering` flag to its renderer, based on the
active lifetime and absence of a discovery outcome, not status text. A rotating
theme-colored arc appears at the viewport center while discovery is pending,
scaled down only for tiny viewports. Redraw replaces/stops the previous animation;
unloading the spinner also stops its clock. All completed outcomes remove it.
The existing footer and Enter/Escape handling remain available during loading,
and retired discovery cannot repaint an old spinner or labels.

Completing a group label opens its children without moving or clicking. Completing
a leaf label selects and previews the cursor, without clicking. Action keys need
an original leaf selection; group focus does not validate or dispatch.
Optional arrows focus entries spatially within the level, with ordered navigation
in compound levels and deterministic wrapping when no directional target exists.
Leaf focus previews the cursor; group focus only highlights its badge/area.
PgUp/PgDn wrap rare pages without moving the cursor and clear prefix/selection,
independent of arrow enablement. Escape clears a partial pair, otherwise pops
the level (including from a selected leaf); at L1 it cancels.
Enter explicitly falls back to
UniformGrid, including loading/failure/mode lock. Redraw/layout-glyph refresh
preserves snapshot identities, label VKeys, level and selection. Viewport capacity
changes explicitly reset to L1. A content-sized bottom footer shows concise
loading/partial/error state, selection guidance, paging only when needed, and
Enter/Escape fallback. Counts, omission reasons and viewport-capacity changes go
to debug logging, not an intrusive normal-mode banner. This stays on RootCanvas,
independent of macro/drag StatusCanvas and HelpCanvas.

## Action transaction

An action snapshots `HookModifierFlags` into `ActionModifiers` at the initiating
event. The coordinator drains capture, hides the entire overlay host and enabled
key-press HUD, restores the
intended window and requests helper validation without retiring the snapshot.
Duplicate actions are ignored. Cancellation/reopen/config disposal invalidate the
pending transaction; a late completion cannot inject or record.

Validation checks captured PID/process start, HWND/root runtime ID/root bounds,
target runtime ID/current state/unchanged bounds and ancestry. Native PID/start
checks precede reacquiring UIA for a possibly reused HWND; foreground is sampled
after fresh root metadata. It tries a provider
clickable point, then center and four interior points. Native HWND ownership and
UIA hit-test ancestry must agree; passive nested text is allowed, but another
interactive child is not treated as its parent's click/drag point. Cursor-only
`MoveOnly` intent is captured from the initiating action and sent explicitly
through session/supervisor as the optional request field `MoveOnly` (default
false). It may accept a hit on an interactive descendant of the selected target,
using the same bounded raw ancestry check. This fixes Copilot sidebar TreeItems
with Selection/Expand capabilities and full-size Invoke Buttons: they remain
independent targets in a compound level, but moving into the row can land on its child without invoking
it. Siblings, unrelated covering controls and foreign HWNDs still reject. Root/
target identity, foreground, unchanged bounds, containment and the validation
deadline are unchanged for moves. Missing intent on older requests stays strict;
every validation sets intent anew so it cannot leak from a move to a later click.
A final native foreground/
PID/point guard precedes the existing dispatcher and bounds check. Rejection closes,
notifies through the tray, and records no step. UIA never invokes, sets values or
changes focus. Physical click/move/drag and coordinate macro formats remain intact.

There is an unavoidable validation-to-SendInput race; this is not atomic input.
Elevated/secure desktops, custom-drawn controls and independent popup HWNDs may
provide no usable targets. Reopen after changes or use Enter for grid.

## Lifecycle and distribution

Help preserves session state and does not rescan. Picker/playback suspension retires
element work; resume scans the original application, never an overlay HWND.
The shared contextual help uses a typed session snapshot for configured label
axes, depth, single/pair scheme, group focus, rare page/page count, prefix/selection, discovery state and effective action
availability. Discovery/capacity changes refresh visible help through SessionManager;
retired sessions cannot update it. Help distinguishes close-only Escape from hint
clear/cancel stages and respects recording/setup's higher-priority key handling.
Enter fallback is described for loading/failure/selected/locked states. Help explains
label-only group opening, optional arrow focus, independent paging and Escape back.
The merged native Settings editor includes all five modes in its default picker
and preserves each mode block plus user extensions/comments. It edits the existing
hint enable/default/chord/two-key/arrow/discovery-timeout fields and shared axes/actions/help on their
normal pages. PgUp/PgDn are reserved against label/action/mode/scope/help/macro collisions while
hints are enabled. Invalid fallback, default, key and label-floor combinations block
save instead of silently enabling/rebinding/normalizing them. Helper traversal,
wire, validation and cleanup limits remain internal. See `settings.design.md`.
Recording resumes with retained target/display context. Display changes rescan the
same application clipped to the selected display. Drag start validates coordinates
before the normal default-mode reset. Topology/config/deactivation retire the helper.

Normal app builds copy worker output to an isolated subfolder. Publish invokes the
worker with the app's RID/self-contained/version settings. The release workflow
extracts its ZIP and runs `scripts/Test-UiaWorkerPackage.ps1` with PATH empty and
DOTNET_ROOT unavailable, proving the helper uses its bundled runtime. Test-only
hang/crash/malformed-output commands exist in WorkerFixture, never the production
protocol.

The package gate starts its 1500 ms clock before process creation and includes
response decoding/validation. `-Diagnostics` separates preparation, process start,
write, frame receipt and parsing; worker stderr reports readiness startup time
without provider contents. Passing warm retries do not prove the cause of a cold
timeout. Follow-up runs also show that WPF `IsActive` is not native foreground
ownership: normal activation can be denied while validation correctly fails closed.

Plan 4af565 closed delivery on 2026-10-08 by explicit user acceptance of the
UIA/help/Settings result. Its finalization record retains unperformed physical-input,
mixed-display/DPI, third-party/elevated-provider and <=100 ms live responsiveness
checks as deferred, not passed. Automated WPF discovery is not universal coverage.
Offline Windows Sandbox WinForms captures at 200% establish the banner/connector
clutter correction. Final committed-code captures used a separate
`Application.Run` fixture with the same 39 control geometries and explicit bounded
guest-only provider initialization. That is warm-condition rendering evidence;
bounded cold discovery/handshake failures and fixture foreground-acquisition
denial remain unresolved. User acceptance does not establish their causes or
change the deadlines or fail-closed action guards.

A bounded read-only probe on the user's Copilot sidebar reproduced a TreeItem's
five candidate points all hitting its full-size Button descendant, with matching
native HWND ownership and raw ancestry. The updated production helper retained
`NoSafePoint` for a strict action (83 ms) and approved `MoveOnly` at the row center
(42 ms), within the unchanged 500 ms validation deadline. No input was injected;
this proves live provider/validation behavior, not an observed physical move.

The hierarchy follow-up probed the same captured Copilot instance read-only using
the updated production worker and planner. A partial snapshot retained 96 targets
and 32 complete containers; L1 had 63 entries including five compound groups.
All original target identities were preserved exactly once. The sidebar TreeItem/
full-size Button pair appeared in one compound group with six L2 entries, retaining
the row's own action. Strict parent action still returned `NoSafePoint` (51 ms);
move-only approved its verified center (25 ms), with foreground unchanged. No
input was injected. This establishes that provider's grouping/validation behavior,
not universal UIA coverage or final visual/physical-input acceptance.

The [README demo capture](readme-demo.design.md) adds illustrative native
hierarchy navigation over a DPI-aware synthetic WinForms dashboard in an offline
Sandbox. Warm production discovery retained 47 targets and yielded 20 L1 entries;
the captured combo-box group preserves its three targets as one-key L2 badges
with matching control outlines, border patterns and a displaced-badge leader.
The production footer confirms both levels before capture. No action keys,
clicks or drags are sent; this does not establish cold Word discovery, third-party
coverage or physical-input acceptance.
