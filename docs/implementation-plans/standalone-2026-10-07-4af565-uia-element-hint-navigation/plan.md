# 4af565: UIA element-hint navigation
<!-- plan-id: 4af565 -->
<!-- cip-stage: drafted -->
<!-- planning-confirmed: sha256:573a4c124977f1b57f34ef8b85ec4778736a97a19f0e3456572cee46e4b69651 -->
<!-- execution-mode: manual -->
<!-- scope: plan -->
<!-- evidence: required -->
<!-- phase-budget-points: 6 -->
<!-- expected-packages: none -->

## Assets

- Intent - [assets/intent.md](assets/intent.md)
- Domain model - [assets/domain.md](assets/domain.md)
- Approved design - [assets/design.md](assets/design.md)
- Requirements - [assets/requirements.md](assets/requirements.md)
- Risks - [assets/risks.md](assets/risks.md)
- Decisions - [assets/decisions.md](assets/decisions.md)
- References - [assets/references.md](assets/references.md)
- Implementation evidence - [assets/evidence.md](assets/evidence.md)
- Final acceptance and verification limits - [assets/finalization.md](assets/finalization.md)

## User acceptance and finalization, 2026-10-08

The user approved the delivered UIA, contextual help and native Settings result:
**"looks good. finish the plan, merge to main, push"**, received
**2026-10-08T10:42:05.711+02:00**. Step 4.2 is closed by this scoped operator
acceptance, not by claiming its complete live matrix passed. Unperformed scenarios
and unresolved historical startup/focus conditions remain explicitly deferred in
[finalization](assets/finalization.md). Confirmed planning assets and numeric
guards remain unchanged.

Implementation through 4.1, real offline Sandbox rendering, conservative Copilot
duplicate canonicalization and shared help/Settings integration are evidenced.
The final merged suite passed 1499 tests; Release build and changed-code formatting
passed. The self-contained package at source `777afb6` includes its bundled worker
and was launched unchanged for the user's manual validation. Whole-plan review,
learning handoff and archival follow the repository finalization workflow; the
historical evidence below remains separate from the new acceptance.

## Phase 1: Safe, working element-hint slice

- [x] 1.1 Implement supervised UIA helper and foreground-window discovery (REQ-1, REQ-2, REQ-3, REQ-10, RISK-1, RISK-2, RISK-7) `L`
  <details><summary>Implementation contract</summary>

  **Outcome:** a cancellable, bounded discovery service returns typed button/edit candidates from a captured HWND; a blocked helper is terminated without blocking Klikety.

  **Likely touchpoints:** new `src/Klikety.UiaWorker/`, `src/Klikety/Automation/`, solution, app project build-output integration, test-only child-process fixture, service fakes.

  **Constraints:** .NET 10 Windows managed UIA first; no third-party package expected. UIA objects stay on one windowless MTA thread in the helper. Parent uses bounded redirected stdio, an explicit protocol version, request/session IDs, and an absolute bundled executable path. One owned helper at a time; cancellation retires it before replacement. Include helper startup in the discovery deadline. Distinguish no targets, timeout, access denied, provider error, and malformed protocol.

  **Verify:** `ElementHintProtocolTests` and `UiaWorkerSupervisorTests`, including a hung child, startup failure, crash, excessive output, parent shutdown, cancellation, and recovery on the next scan. Build and run from the normal development output.

  **Stop/escalate when:** the managed UIA reference is unavailable under the existing SDK, process teardown cannot be bounded, or the helper cannot run from development output without a system-wide dependency. Do not replace hard isolation with abandoned in-process tasks.

  </details>

- [x] 1.2 Wire an opt-in mode, two-key selection, and explicit grid fallback (REQ-1, REQ-4, REQ-6, REQ-9, RISK-3, RISK-6) [after: 1.1] `L`
  <details><summary>Implementation contract</summary>

  **Outcome:** enable `modes.elementHints`, activate with its chord, see labels on a small fixture, select without clicking, and press Enter to continue in UniformGrid.

  **Likely touchpoints:** `ElementHintsSession`, state machine, minimal renderer, `ModeSessionFactory`, `SessionManager`, coordinator, `ConfigModel`, loader/migrator, embedded config/schema, `HelpBindingPolicy`.

  **Constraints:** capture HWND before overlay activation; pass target context explicitly through the factory/session lifecycle without changing the existing grid activation contract. Use horizontal-first/vertical-second VKeys and current-layout glyphs. Enter fallback works during loading, error, prefix entry, selection, and mode lock. Migration version 9 adds disabled settings without altering existing keys/defaults. Enabled mode requires UniformGrid for fallback. Until step 3.1 installs target validation, do not enable physical action dispatch in the new mode; surface that temporary gate.

  **Verify:** `ElementHintsStateMachineTests`, `ElementHintsCoordinatorTests`, `ElementHintsConfigTests`, and existing config/mode-switch tests. Loading, empty, and failure states expose Escape and Enter with no action emitted.

  **Stop/escalate when:** a chord would steal an existing command, session creation reads the overlay's HWND, or wiring changes existing grid behavior. Report the conflict rather than silently rebinding.

  </details>

## Phase 2: Useful discovery and readable hints

- [x] 2.1 Complete candidate classification, geometry, and bounded traversal (REQ-2, REQ-3, REQ-10, RISK-2, RISK-4, RISK-5) [after: 1.2] `L`
  <details><summary>Implementation contract</summary>

  **Outcome:** buttons, edit fields, links, toggles, tabs, selectable items, and expanders become independent targets without labeling every focusable container.

  **Likely touchpoints:** helper UIA adapter, immutable candidate DTOs, candidate policy, geometry/identity helpers, deterministic policy tests.

  **Constraints:** combine control types with advertised Invoke/Toggle/SelectionItem/ExpandCollapse/Value capabilities; focusability alone is insufficient. Bounded control-view traversal with cached properties, not desktop-wide `FindAll`. Exclude disabled/offscreen/empty or invalid geometry and own-process UI. Retain target identity, actionable nested controls, and legitimate cross-process provider descendants. No text values or document contents. Mark partial results and omitted counts/reasons explicitly; do not imply complete coverage.

  **Verify:** `ElementHintCandidatePolicyTests` and `ElementHintGeometryTests` cover text versus edit, readonly edit focus targets, nested duplicate text, independent nested actions, clipped controls, NaN/infinity, negative display origins, 32/64-bit identity serialization, and traversal/target limits.

  **Stop/escalate when:** a provider lacks sufficient identity or geometry to support later validation. Omit that target with a diagnostic, not an invented click point.

  </details>

- [x] 2.2 Add stable paging, prefix filtering, and collision-safe label layout (REQ-4, REQ-5, RISK-4, RISK-5) [after: 2.1] `L`
  <details><summary>Implementation contract</summary>

  **Outcome:** every retained target is reachable through two-key labels and pages; crowded controls remain readable without moving the physical click point.

  **Likely touchpoints:** label/page model, `ElementHintsStateMachine`, `ElementHintsRenderer`, layout helper, existing theme/text/coordinate helpers.

  **Constraints:** deterministic target order and two-key Cartesian labels per page. Left/Right change pages; page changes clear prefix/selection without cursor movement. Reduce page size when labels cannot fit at `MinLabelFontSize`, then recompute stable pages once per snapshot/viewport. Use external labels/connectors or a contained list for pathological crowding. Labels are window-local DIPs; target bounds remain physical desktop pixels. Keyboard-layout redraw changes glyphs, not identities, VKey assignments, page, or selection.

  **Verify:** `ElementHintLabelTests`, `ElementHintLayoutTests`, and actual WPF `ElementHintRenderingTests` cover more targets than key-pair capacity, custom key sets, crowded/tiny controls, long fallback glyphs, display edges, 100/150/200% DPI, and negative origins. Prove rendered text is contained and every retained target can be selected.

  **Stop/escalate when:** readability requires shrinking below the configured font floor or dropping targets silently. Use the paged list presentation instead.

  </details>

## Phase 3: Validated actions and lifecycle integration

- [x] 3.1 Validate selected targets before mouse actions (REQ-6, REQ-7, REQ-10, RISK-1, RISK-2, RISK-8) [after: 2.2] `L`
  <details><summary>Implementation contract</summary>

  **Outcome:** existing action keys operate on a validated selected control; stale, covered, destroyed, and disabled targets cause no input.

  **Likely touchpoints:** helper validation command, typed pending-action intent, coordinator/session action-preparation seam, `ActionDispatcher`, platform hit-test seam, runtime notification route.

  **Constraints:** snapshot modifiers and action intent at the initiating key event. Hide the overlay host and drain capture before point validation; keep the helper/snapshot alive until approval or cancellation. Verify HWND/process/root/element identity, current state/bounds, point ownership, and current activation token. Prefer a provider clickable point; use a bounded verified interior-point search if needed. UIA detects/validates but never invokes, sets values, or changes focus itself. Exactly one action after successful validation; rejection closes safely with a visible reason. Preserve final bounds checks and existing SendInput modifier/drag semantics.

  **Verify:** `ElementHintActionValidationTests` and coordinator tests exercise moved controls, covered centers, nested text hit-tests, window destruction/reuse, lost foreground, timeout, released modifiers during async validation, duplicate action presses, cancel/reopen, and validation arriving after deactivation. Verify hide-before-validation and no input on rejection.

  **Stop/escalate when:** validation would use the overlay as the hit-test target, async state cannot suppress late input, or a rejected action could still record a macro step. Fail closed; do not click cached coordinates.

  </details>

- [x] 3.2 Integrate help, display/scope changes, drag, and macro suspension (REQ-8, REQ-9, REQ-10, RISK-3, RISK-6, RISK-8) [after: 3.1] `L`
  <details><summary>Implementation contract</summary>

  **Outcome:** element discovery/actions follow every existing session transition without stale labels, orphan helpers, broken help, or unintended macro input.

  **Likely touchpoints:** every `SessionManager` create/restart/resume path, coordinator teardown/focus handling, help content, `MacroHandler`, test fakes and integration suites.

  **Constraints:** preserve captured application identity across overlay focus changes. Display switching cancels work and clips the same app to the new navigation display; app-scope never broadens discovery. Help preserves prefix/page/selection and does not rescan. Pending helper completion does not overwrite help or macro status. Picker/playback suspension retires requests; resume obtains a fresh snapshot with the appropriate target HWND. Validate before recording a physical step. Drag start remains coordinate-based and normal default-mode reset remains intact; no semantic macro format changes.

  **Verify:** `ElementHintsLifecycleTests`, help content/coordinator tests, and existing display, app-scope, drag, recording, picker, and playback integration suites. Include default ElementHints activation, fallback while locked, non-QWERTY activation, redraw, disposal/config reset, and topology change.

  **Stop/escalate when:** a resume path cannot determine its intended application independently of overlay foreground state. Require a target-context correction, not a global foreground lookup.

  </details>

## Phase 4: Distribution, documentation, and compatibility evidence

- [x] 4.1 Ship helper in development/publish/release outputs and document the mode (REQ-9, REQ-10, REQ-11, REQ-12, RISK-7) [after: 3.2] `L`
  <details><summary>Implementation contract</summary>

  **Outcome:** normal build/run and the existing self-contained win-x64 release ship a working helper; feature docs describe actual controls, fallback, and limits.

  **Likely touchpoints:** solution/app/helper project integration, release artifact check, tests, README, new `element-hints.design.md`, design-note index, navigation/config/rendering/interop/state/help/testing/CI notes.

  **Constraints:** no manually installed helper or hardcoded developer paths. Runtime launch uses the bundled path with no shell/PATH search. Publish includes helper executable and required runtime/dependency files under an isolated subfolder; do not overwrite app metadata. Missing helper is a visible mode-unavailable error, not global startup failure. No elevation/UIAccess, browser flags, remote/cloud processing, or new third-party packages without an explicit scope decision.

  **Verify:** `ElementHintPackagingTests`; focused suites followed by existing impacted regressions, app/solution build, formatting check, and self-contained publish. Run the worker protocol from the extracted publish folder, not its build folder. Update related docs as part of each earlier phase, then reconcile the final delivered behavior here.

  **Stop/escalate when:** publishing the app cannot reproducibly include a functioning helper for the selected RID or requires a new SDK/package/distribution policy. Do not declare the feature shipped from unit tests alone.

  </details>

- [x] 4.2 Record live Windows evidence and user acceptance with deferred verification (REQ-1, REQ-2, REQ-3, REQ-5, REQ-6, REQ-7, REQ-8, REQ-11, REQ-12, RISK-1, RISK-2, RISK-4, RISK-5, RISK-7) [after: 4.1] @human `M`
  <details><summary>Live verification</summary>

  **Closure, 2026-10-08:** the user's exact approval above authorizes delivery
  finalization and integration. This checkbox records that acceptance with the
  limits in `assets/finalization.md`; it does not turn unavailable or unsuccessful
  matrix rows into passing evidence. The original procedure and verification
  thresholds below remain the follow-up procedure for unobserved scenarios.

  **Steps:**
  1. Back up the user config, quit the installed build, extract the published build to a test folder, and enable ElementHints with a collision-free chord.
  2. Run the controlled WPF/Win32 fixture through `Klikety.SmokeTests`; verify known controls, readonly/edit fields, nested content, covered controls, dynamic replacement, and ordinary click/modifier/drag actions.
  3. Exercise representative WinUI, browser, Electron, and custom-drawn apps. Record versions and observed UIA coverage; test a normal-user client against an elevated app without changing permissions.
  4. Exercise a busy/provider-hang fixture, Escape/Enter during scanning, repeated cancellation, Alt+Tab, help, recording/picker resume, multiple displays with negative origins, and 100/150/200% DPI.
  5. Confirm the extracted release launches its helper without an installed SDK; capture timing, process cleanup, and any missing provider coverage in the plan assets.

  **Verify:** controlled fixtures satisfy the requirements below; discovery fails visibly at the configured deadline, Escape/Enter remains responsive, no owned helper survives teardown, and no rejected target receives input. Third-party omissions are documented rather than claimed universally fixed. Record evidence against the approved thresholds in `assets/decisions.md` before recording whole-plan completion.

  **Rollback:** quit the test build, confirm its owned helper has exited, restore the backed-up config, and restart the previous installed build. In the original full-matrix workflow a failed scenario kept this gate open. The user-directed delivery closure above does not close those verification gaps: report each exact failure and retain fail-closed behavior and the unchanged thresholds.

  </details>
