---
description: Opt-in foreground-window UI Automation hints, isolated helper, bounded protocol, paged labels and validated physical actions.
globs:
  - src/Klikety/Automation/**
  - src/Klikety.UiaWorker/**
  - src/Klikety/Navigation/ElementHintsSession.cs
  - src/Klikety/Overlay/ElementHintsRenderer.cs
  - src/Klikety.WorkerFixture/**
  - scripts/Test-UiaWorkerPackage.ps1
---

# Element Hints

`modes.elementHints` is disabled by default. Enable it with `twoKey` and `arrowKeys`
true and keep UniformGrid enabled. The default chord is Tab. Config version 9 adds
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

One `UiaWorkerSupervisor` per factory serializes scan/validation/retirement. The
absolute bundled `uia-worker\Klikety.UiaWorker.exe` path uses redirected inherited
stdio, no shell or PATH lookup. Before sending a UIA request the parent assigns the
helper to a kill-on-close Windows job. Closing stdin also exits the worker normally.
Startup is off the UI thread and within the discovery deadline. Unreaped startup,
unconfirmed cleanup or unsettled pipe reads prevent replacement workers rather than
accumulating abandoned children/tasks.

| Contract | Limit |
|---|---|
| Discovery, including startup | 1500 ms |
| Validation | 500 ms |
| Retirement | 500 ms cleanup budget |
| Visited nodes / depth / retained targets | 20000 / 64 / 2000 |
| Request / response / diagnostic stderr | 64 KiB / 2 MiB / 64 KiB |
| Runtime identity | 64 integers |

Frames use a four-byte little-endian byte length followed by versioned JSON.
Session/request IDs, limits, finite geometry, clipping and validation approvals
are checked before consumption. Diagnostics contain outcomes/error codes/counts,
not element names, values, passwords or document text. Timeout, provider failure,
access denial, invalid root, no targets and partial scans remain distinct.

Worst-case runtime identities/geometry can fill the byte budget before the target
count cap. The worker keeps the fitting prefix and returns an explicit partial
snapshot with omitted counts. Truncated headers/bodies are protocol errors.

## Candidates, labels and rendering

Enabled, onscreen, finite, clipped targets require a standard interactive control
type or Invoke/Toggle/SelectionItem/ExpandCollapse/Value capability. Readonly edits
remain focus targets. Focusability alone is insufficient. Runtime identities
deduplicate candidates, never names or rectangles; nested independent actions
remain separate. Branch failures and traversal caps report partial results and
omitted branch/target counts, not a claim of complete provider coverage.
`UiaTreeAlgorithms` is the production traversal/identity/hit-testing kernel,
shared by source with hermetic tests. `AutomationTree` supplies cached native UIA
properties and bounded ancestry reads. Invalid interactive-target identity or
geometry contributes an explicit omission; invalid root geometry rejects discovery.

Targets sort by physical top/left/token. Per-page labels use horizontal-first,
vertical-second VKey pairs; current-layout glyphs come from `IKeyLabelResolver`.
The measured font floor and viewport bound page capacity. Inline hints are used
when collision-free; crowded hints use a page-local list and connectors. Extremely
small viewports/long labels use a contained scrolling list, never smaller text or
silent target removal. Capacity and painting share adaptive status/list regions,
including sub-8-DIP viewports; the initial physical-region fallback is retained
until a measured canvas viewport is available. Labels are window-origin-relative DIPs; click coordinates
remain physical desktop pixels.

Two keys select and preview the cursor, without input injection. Action keys need
a selection. Left/Right page without moving the cursor and clear prefix/selection.
Escape clears prefix/selection, then cancels. Enter explicitly falls back to
UniformGrid, including loading/failure/mode lock. Redraw/layout-glyph refresh
preserves snapshot identities, label VKeys, page and selection. Viewport capacity
changes explicitly reset pages; loading/partial/error status lives on RootCanvas,
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
interactive child is not treated as its parent's point. A final native foreground/
PID/point guard precedes the existing dispatcher and bounds check. Rejection closes,
notifies through the tray, and records no step. UIA never invokes, sets values or
changes focus. Physical click/move/drag and coordinate macro formats remain intact.

There is an unavoidable validation-to-SendInput race; this is not atomic input.
Elevated/secure desktops, custom-drawn controls and independent popup HWNDs may
provide no usable targets. Reopen after changes or use Enter for grid.

## Lifecycle and distribution

Help preserves session state and does not rescan. Picker/playback suspension retires
element work; resume scans the original application, never an overlay HWND.
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

Live physical-input, mixed-display/DPI and third-party coverage remain the human
gate in plan 4af565. Automated WPF discovery is not universal provider evidence.
