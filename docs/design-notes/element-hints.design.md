---
description: Opt-in foreground-window UI Automation hints, isolated helper, bounded ancestry, adaptive hierarchical labels and validated physical actions.
globs:
  - src/Klikety/Automation/**
  - src/Klikety.UiaWorker/**
  - src/Klikety/Navigation/ElementHintsSession.cs
  - src/Klikety/Navigation/ElementHintHierarchy.cs
  - src/Klikety/Navigation/ElementHintAssignments.cs
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

A cleanup timeout blocks replacement only while teardown is unconfirmed, not
permanently. The next scan/release retries the bounded 500 ms cleanup and observes
late startup and pipe completion, including expected faults/cancellation. The
block clears only after startup has settled, the owned process/job are retired,
and both response and diagnostic pipe tasks have completed. A null process alone
does not establish cleanup: pending pipe tasks still block replacement. This
applies with cache reuse both enabled and disabled.
`CleanupFailureReason` identifies the pending startup, process, response-read or
diagnostic-read stage without provider contents. Session retirement warns/logs
that stage and asks the user to reopen hints to retry rather than claiming scans
are disabled forever. Genuine outstanding cleanup remains fail-closed.

| Contract | Limit |
|---|---|
| Discovery, including startup | `modes.elementHints.discoveryTimeoutMs`: default 10000 ms, range 100-60000 ms |
| Validation | 500 ms |
| Retirement | 500 ms cleanup budget |
| Visited nodes / depth / retained targets | 20000 / 64 / 2000 |
| Request / response / diagnostic stderr | 64 KiB / 2 MiB / 64 KiB |
| Runtime identity | 64 integers |
| Pruned complete containers | 4000; ancestry depth bounded by traversal |

The optional discovery field keeps config version 9 and defaults to 10000 ms
when absent. Explicit existing deadlines are preserved. Settings exposes it on
Navigation's Element hints card. The longer default accommodates whole-window
progressive scans; labels are usable before completion and Enter/Escape still exit.
Invalid values report a configuration violation, suppress hint dispatch and block
Settings saves, including when hints are disabled; they are never clamped or
interpreted as an unlimited timeout. A larger deadline accommodates slow providers
such as Word but does not guarantee coverage or a successful scan. Validation and
retirement remain fixed at 500 ms. Cancellation does not wait for the discovery
deadline: an interrupted exchange retires the helper; closing an idle/completed
session releases its frontier and uses the bounded cache lifetime below.

### Progressive discovery and bounded reuse

Production sessions use cooperative `Discover`/`Continue` exchanges.
`ProgressiveHintDiscovery` prepares one control-view depth at a time, publishes
leaf controls before descending, and schedules the next depth by ascending
immediate-child count of its parents. Child enumeration is retained, not repeated
for sorting. Every branch is scanned within the existing node/depth/target/deadline
bounds; immediate child counts never collapse a document, pane or session list.
Completed controls publish without waiting for an actionable document ancestor.
Only a patternless ListItem that could participate in the narrow wrapper-folding
rule holds its own descendants until that row completes. Complete ancestry may
arrive later without changing published target identities or displayed keys.
Canonicalization uses captured metadata, not another provider traversal.

An exchange performs up to 64 traversal operations or approximately 25 ms of
cooperative work. The first useful result returns promptly; additions are
coalesced at 35 ms. Slices with no target/group additions continue immediately
without another overlay redraw or artificial delay; the initial and final frames
are always published. A single native provider call can still block: only the
parent watchdog can interrupt that call by retiring the owned helper.
Cumulative frames carry original target identities, completed container metadata,
and available container metadata. The worker emits no deferred groups; groups
are planned over known targets only for capacity or competing compound badges.
The retired `rootGroupChildThreshold`/`groupChildThreshold` settings are ignored
in existing files and absent from the editor/defaults/schema. This was chosen
after live Copilot discovery produced four chrome controls and two page-center
labels: a page-wide focus target and a collapsed content wrapper. Raising the
threshold alone still withheld document descendants behind their unfinished
actionable ancestor.
Node/depth/target/frame caps remain bounded across the session.
Read/probe failures report explicit omissions;
completed unrelated branches remain usable.

Progressive levels append entries without changing existing VKeys, prefix,
selection, focus or entered levels. Fresh growing levels freeze the pair-key scheme
when their first entries appear; completed small levels can use single keys.
New chunks use hierarchy planning. Growing root levels put new overflow batches
into numbered spatial regions sized for readable single-key child levels,
without regrouping a previously displayed hint. Remembered root controls retain
their existing level rather than being moved into new overflow groups; isolated
additions and one-slot layouts can still page. Explicit viewport relayout resets L1
and resumes unfinished discovery without starting a new window scan. Keyboard
layout redraw alone does not relayout. Late/retired scope results cannot repaint
an old level. Help distinguishes visible navigation groups from actionable
targets and explains that early labels are usable while discovery continues.

### Best-effort assignments across activations

`ModeSessionFactory` owns a separate metadata-only `ElementHintAssignmentCache`.
It uses the same `cacheWindowCount` LRU bound (0 disables it), survives helper
idle retirement, and drops on factory disposal/config replacement. Window keys
require captured HWND/PID/process-start identity; missing identity skips reuse.
Each window keeps one snapshot with at most 2000 control and 4000 group records,
including off-page allocations and levels visited before returning to L1.
Records contain fingerprints/slots/schemes, never UIA objects or old targets.

`ElementHintAssignments` fingerprints opaque runtime IDs, process, role and
capabilities already in responses. Bounds/token changes do not change a control
key. Groups match only their exact member identities, compactness and nested
structure; child-level keys also incorporate their parent level. Rebuilt,
regrouped or reparented entries can miss: there is no hierarchy replay, provider
lookup, semantic/fuzzy matching or persistent identity guarantee.

Each level reserves previous slots for the whole activation. Rediscovered
entries recover their slots; new entries take the first unreserved slot.
Rendering and key lookup use the same explicit map, not list indices. Holes
reject input, overflow pages, and reused levels preserve their single/pair scheme.
The active logical page stays pinned when an earlier reserved page fills;
empty pages are skipped and the displayed page ordinal may change.
Only freshly discovered entries are selectable with current tokens and normal
action validation. Current prefix/selection/entered levels do not remap.

Close replaces the snapshot with all allocated assignments after a usable frame;
NoTargets clears it, while a scan with no usable frames preserves it. Partial or
cancelled scans can forget unseen assignments next time; they do not prove
absence. Late retired frames cannot save. Viewport/capacity/VKey-array changes,
explicit relayout and LRU eviction can reset memory. Runtime IDs can be recycled
with indistinguishable metadata: best-effort memory is not semantic identity or
permission to dispatch stale actions. Nothing is persisted across app restarts.

Validation increments the discovery generation and acquires the exchange gate
between slices. Its unchanged 500 ms budget includes waiting for a slice; if a
native call blocks that budget, validation fails closed and cancels/retire-bounds
the in-flight exchange. A cooperative overall discovery deadline returns an
explicit partial snapshot when no provider call is in flight, keeping its worker
available for fresh validation. Before starting another slice, it reserves two
slice budgets plus one batch interval for slice/IPC overhead; a nearly exhausted
budget therefore finishes cooperatively rather than starting a doomed call.
A blocked-call timeout still retires the worker;
it is not represented as successful cached discovery.

`modes.elementHints.cacheWindowCount` defaults to 5 (integer 0-20).
It limits an LRU of **windows**, keyed by HWND/PID/process-start identity; separate
windows of one app consume separate slots. Each window retains at most 20000
cached nodes/navigation edges and a bounded dirty-identity set. `0` disables
reuse and event subscriptions. A single owned helper stores the LRU, not one
helper per window. Closed/replaced roots are removed; runtime-root replacement
also replaces its event subscriptions. Window/root bounds changes invalidate
geometry, and a different clipping region reuses source metadata but recomputes
visible geometry.

Structure events and geometry/offscreen/enabled/capability property events are
subscribed/removed on the worker's MTA. Event-source runtime identities are
prefetched; callbacks only enqueue dirty identities, never query UIA or rebuild.
Known changes invalidate the nearest cached pane/group/tree/list and its child
enumeration edges. Dirty identities and their roots are resolved as one batch
before removing cache entries. Duplicate roots and roots covered by a dirty
ancestor are invalidated once, so a pane/child event burst does not turn a known
identity into an unknown one or leave sibling data stale. Other panes keep their
data. Unknown/coarse events invalidate
the whole window. Entries older than 5 seconds refresh conservatively even if
events were missed; text/name/value changes are not polled or read. Provider
notifications are not a complete change log and coarse root events cannot be
cheaply partitioned. Navigation resolves new UIA wrappers to existing cached nodes
using prefetched runtime identities, so re-enumerating a changed parent's children
does not discard an unchanged sibling pane merely because its wrapper is new.

Closing a completed hint session releases its active tokens/frontier but keeps
event-observed source caches. After 30 seconds idle, the parent retires the helper
and its source caches, not the separate parent assignment registry.
Factory/coordinator disposal and configuration replacement
explicitly shut it down, including when the active mode is not ElementHints.
Cancellation of an in-flight blocked exchange still kills the helper, so reuse
is not promised after interruption/failure. Native TreeWalker cache-request
overloads prefetch metadata during navigation instead of following navigation
with another property fetch; fresh validation uses a new uncached reader.
Debug logging records first usable hint time, completed-scope elapsed time,
retained/visited/omitted counts and cache hits, never provider content.

Debug `HintDiag` records share a fresh activation GUID across the session,
coordinator and renderer. Startup records identify the app PID and effective
cache/timeout settings; activation records identify the captured HWND/PID and
viewport, not the window title. Each cumulative frame records scope, generation,
completion, counts and source-cache hits. Planning/render records distinguish
source-tree reuse from assignment snapshot reuse, restored/reserved slots,
overflow grouping, visible region numbers and group entry. Digit routing records
pending-action/help-latch/debounce suppression, macro consumption and forwarding.
Renderer records explain the right-side list fallback (capacity, oversized
labels or placement collision), render duration, DPI/origin and raw versus
clipped/inset group geometry. Numeric region badges are measured/placed
independently from control labels and remain on their outline corners when
controls use the right-side list. Anchored groups do not consume control-list
capacity or rows. Unplaceable numeric badges still use the contained list.
Group contours clip to the full canvas, preserving status-bar member coverage;
badge placement still respects the footer-reserved viewport. Per-group `list`
diagnostics refer to that badge, not the whole control page. Stale frames record
their rejected generation.
No target names, values, descriptions, runtime IDs, fingerprints or document text
are added to these diagnostics. Geometry records describe current discovered
members, not future scan coverage.

Frames use a four-byte little-endian byte length followed by versioned JSON.
Session/request IDs, limits, finite geometry, clipping and validation approvals
are checked before consumption. Diagnostics contain outcomes/error codes/counts,
not element names, values, passwords or document text. Timeout, provider failure,
access denial, invalid root, no targets and partial scans remain distinct.

An incremental root activation retries once after an empty completed scan with
visited nodes, or a transient provider error, only before any targets/groups
have been published. The 150 ms settling delay, both attempts, worker startup
and cleanup share the original discovery deadline. Retry requires enough budget
for the delay and slice/IPC reserve; timeout, access denial, invalid identity,
protocol failure and unconfirmed cleanup are not retried. A fresh request ID
retains the activation's captured HWND/PID/start identity. An empty completed
worker tree is marked dirty before rediscovery so a cached empty result cannot
defeat the retry. Persistent failures remain explicit, and recovered scans
without another reason identify the retry outcome in debug diagnostics.
This handles transient cold-provider emptiness/errors, not a guarantee that
Outlook/Word or a blocked native provider call will recover.

Worst-case runtime identities/geometry can fill the byte budget before the target
count cap. The worker keeps the fitting prefix and returns an explicit partial
snapshot with omitted counts. Progressive frames reserve diagnostic space and
never shrink the already published target/group set: if new metadata would force
that, the completed partial frame keeps the last published set instead.
Truncated headers/bodies are protocol errors.
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
descendants prevent folding their ancestors. One-shot node/target truncation skips
folding altogether, retaining the existing caps and explicit partial response
rather than assuming unvisited actions are absent. Progressive publication folds
only proven-complete ListItem subtrees; a later cap does not remap those already
published identities. Unrelated invalid branches do not prevent
folding a complete list row. Other provider/proxy overlaps remain ambiguous and
are not generalized from this narrow evidence.

Branch failures and traversal caps report partial results and
omitted branch/target counts, not a claim of complete provider coverage.
`ProgressiveHintDiscovery` is the breadth-first production discovery kernel,
including the compatibility one-shot API. `UiaTreeAlgorithms` canonicalizes
recorded metadata and performs identity/hit-testing checks; its depth-first replay
never discovers provider nodes. Both are shared by source with hermetic tests.
Captured child-enumeration completeness is explicit metadata, not a replay
exception: known descendants remain reachable when an ancestor's enumeration
fails, while incomplete ancestry cannot justify wrapper folding or containers.
`AutomationTree` supplies cached native UIA
properties and bounded ancestry reads. Invalid interactive-target identity or
geometry contributes an explicit omission; invalid root geometry rejects discovery.

## Adaptive hierarchy

Traversal reuses cached control-view ancestry and reads, aggregates retained counts
and process consistency bottom-up, then emits only complete branching containers
or actionable ancestors with at least two targets. Passive unary chains are pruned.
Incomplete ancestors stay flat; unrelated complete branches can still group in
a partial snapshot. One-shot node/target truncation emits no grouping claims.
Progressive caps preserve published targets; incomplete containers
are not used to claim complete ancestry. Canonical wrapper folding remains the
narrow, independent policy above.

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

Complete passive toolbars, groups and panes with at least four visible entries
can form logical regions even when flat labels fit. A candidate must reduce the
level and cannot wrap the whole level in a redundant parent. Other complete
containers remain capacity candidates. Without suitable ancestry, overflow
partitions spatially along the widest spread of preview points, recursively
splitting at capacity-sized boundaries rather than slicing interleaved provider
order. At default capacity 100, 250 flat controls still yield three groups with
100/100/50 children. Growing root levels use the smaller measured single-key
capacity for newly arriving overflow regions, while already displayed entries
stay fixed. With only one readable slot grouping cannot reduce a level,
so PgUp/PgDn paging remains the fallback. Every retained target occurs once.

Top-level regions use a separate frozen assignment namespace, with invariant
number-row labels 1-9 and renderer-measured capacity capped at nine. Control
letters keep their single/pair assignments independently; a number never consumes
a control-letter slot. Group and control assignments retain the existing bounded
best-effort fingerprint memory. More groups page with independent numeric slots;
holes are not selectable. Numbered badges and always-visible rounded member-union
outlines share accents; outlines also use repeating line patterns. Focus thickens
the outline without an action connector. The outline covers known members only,
not an unscanned area.

The root page's numbered outlines remain visible at every nested depth. D1-D9
always switch its top-level regions, replacing the entered branch rather than
pushing another sibling. Switching clears the local prefix/selection and never
moves or acts on a target. The active region has a thicker contour; all root
region numbers/contours remain fully visible despite local prefix/selection
dimming, and their accents/patterns derive from the digit rather than child order.
Nested groups share their level's letter assignments with controls, without
duplicate slots or competing numeric shortcuts. Root region paging stays pinned
while inside; Escape back to L1 allows changing that page. Switching remote
regions rejects superseded discovery generations; local switching and Escape
through local nesting keep root discovery running. Leaving a remote level resumes
the nearest remaining remote scope if unfinished. `HintLevelView.Regions` carries the persistent root markers
separately from current-level labels and `ActiveRegionId` identifies the branch.

In ElementHints only, D1-D9 switch displayed top-level regions rather than monitors.
The host omits satellite digits without changing display numbering or removing
the dimmed satellite overlays. Returning to another mode restores monitor digits
and dispatch. Macro setup retains its existing priority over hint navigation.
Contextual help lists the active region numbers instead of display-switch commands.

Targets sort by physical top/left/token within each newly published batch. Batches
append without reordering already displayed entries. Completed levels use configured
horizontal single keys when their entries fit both that axis and measured single-key
capacity; fresh growing levels freeze horizontal-first, vertical-second VKey pairs
on first content. Reused levels retain their remembered scheme, paging if they
grow beyond its capacity. Current-layout glyphs come from `IKeyLabelResolver`. Assignments,
level capacity and scheme freeze on entry.
Glyph redraw changes paint metrics but not keys, focus, selection or stack.
Explicit viewport relayout rebuilds L1 from accumulated entries and resumes an
unfinished root scope; it does not restart provider discovery.
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
these compound/group accents and black halos intentionally override theme label/outline
colors. Group labels use bold type at 150% of the control font, with a 28-DIP
minimum; measurement and painting use the same typeface/size, including contained
fallback rows. Controls retain their existing font. Root region markers never
dim with local selection/prefix; ordinary unselected labels retain their dimming.
Capacity and painting share adaptive footer/list regions,
including sub-8-DIP viewports; the initial physical-region fallback is retained
until a measured canvas viewport is available. Labels are window-origin-relative DIPs; click coordinates
remain physical desktop pixels.

Control badge background brushes use 40% opacity independently of text and borders, in
both nearby and fallback-list layouts. Existing theme color alpha multiplies that
opacity. Initial, matching-prefix and selected labels retain full-strength
outlined glyphs/borders; the existing nonmatching/unselected dimming still applies.
This makes underlying icons visible without weakening label contrast.
Group badges instead use 90% backing opacity and a two-DIP ordinary border
(thicker for active/focused regions), strengthening numeric contrast. Geometry
diagnostics include the effective group font size and bold weight; region-state
and region-switch logs identify persistent visibility and branch changes.

The session passes a typed `isDiscovering` flag to its renderer, based on the
active lifetime and absence of a discovery outcome, not status text. A rotating
theme-colored arc appears at the viewport center while discovery is pending,
scaled down only for tiny viewports. Incremental redraws keep the same spinner
visual and animation clock, updating only its position/size. Other hint visuals
are rebuilt without detaching the spinner, so large scans do not repeatedly reset
its rotation. Completion, canvas removal and unloading stop its clock.
There is one global spinner: discovered-member groups do not describe pending
scan regions or per-region completion. Per-group progress would require an
explicit early-region/completion contract, not an inferred status from group bounds.
The existing footer and Enter/Escape handling remain available during loading,
and retired discovery cannot repaint an old spinner or labels.

Pressing a displayed root region number switches to its children from any depth;
nested group letter labels drill down without moving or clicking. Completing
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
hint enable/default/chord/two-key/arrow/discovery-timeout/cache-size
fields and shared axes/actions/help on their
normal pages. PgUp/PgDn are reserved against label/action/mode/scope/help/macro collisions while
hints are enabled. Invalid fallback, default, key and label-floor combinations block
save instead of silently enabling/rebinding/normalizing them. Helper traversal,
wire, validation and cleanup limits remain internal. See `settings.design.md`.
Recording resumes with retained target/display context. Display changes rescan the
same application clipped to the selected display. Drag start validates coordinates
before the normal default-mode reset. Topology/deactivation release hint work under
the bounded idle-cache policy. Configuration replacement and app disposal retire
the helper and its caches.

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
