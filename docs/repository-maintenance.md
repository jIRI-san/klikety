# Repository maintenance

This is an advisory record. Current source and current gates remain authoritative.

## Source and scope

<!-- rcs:source:start -->
Source commit: d424a32212c955bae231f30f9d047e82e5d9c259
Dirty worktree paths: docs/repository-maintenance.md
<!-- rcs:source:end -->

## Survey

<!-- rcs:survey:start -->
- Scope correction: this survey covers the Klikety application, its tests, product documentation/plans, schemas, solution, and release workflow. Installed Skalary plugin payloads and their management tooling are excluded. The earlier plugin snapshot finding is explicitly retained as out-of-scope history only.
- Source snapshot: HEAD d424a32212c955bae231f30f9d047e82e5d9c259. The only dirty path before this publication is docs/repository-maintenance.md, created by the earlier pass in this same request; no application source files were modified.
- Product structure: Windows .NET/WPF app with 87 C# source files, three projects (app, unit/integration tests, smoke tests), schemas, release workflow, 14 design notes, and implementation plans.
- Plan corpus: 12 indexed records (1 active, 11 archived), no index parse errors, and 0 epics. The active keyboard-layout plan and archived counterpart normalize to canonical ID 000021; supported state resolution errors as ambiguous. Its 12 checklist steps are checked, but full state is not available.
- Selected product traces: action dispatch through Win32 mouse injection; macro recording/playback timing and Escape cancellation; app-to-coordinator macro collaborator construction; plan inventory/state resolution and linked assets.
- Findings: the plan ID collision blocks plan state resolution; macro Escape cancellation can race an awaited click-indicator animation; the mouse injection path does not fully handle partial SendInput results; macro timing documentation conflicts with recorder semantics. One optional composition improvement is also retained.
<!-- rcs:survey:end -->

## Coverage

<!-- rcs:coverage:start -->
- Read README, solution/project files, release workflow, EditorConfig, product design-note index, and directly relevant state-machine, macro, Win32 interop, config, and testing notes. Reviewed selected C# implementations and targeted tests; no full test suite or live UI run was performed.
- The plan index parsed 12 records without errors; Get-PlanState for 000021 fails because of the duplicate active/archive identity. The active plan links to an assets directory that is absent. Archived intent/review context was not consumed, so the intended canonical record and asset location remain unknown.
- No product architecture index at docs/architecture-notes/.architecture-notes.md or docs/review-standards.md was present. No active plan overlaps the macro or mouse findings; the only active plan is the keyboard-layout record with ambiguous state.
- No broad dead-code reachability investigation, security audit, all-subsystem code review, unit/smoke suite, or runtime validation was performed. Mouse SendInput failure handling has no injected failure seam in the inspected tests; macro cancellation tests cover cancellation between steps, not while the click indicator is pending.
- Excluded by operator scope: installed Skalary plugin payloads/receipts, .github/skills, and scripts/skalary. The prior plugin snapshot finding is retained as out-of-scope history, not as a Klikety product issue.
<!-- rcs:coverage:end -->

## Findings

<!-- Findings are append/preserve only; unseen entries are never resolved or removed. -->

<!-- rcs-finding: RCS-PLUGIN-REGISTRY-DRIFT -->
### RCS-PLUGIN-REGISTRY-DRIFT - Installed plugin snapshot is stale
**Category:** coding-standard
**Subject:** Installed plugin snapshot is stale
**Scope:** scripts/skalary/registry.json, installed plugin receipts and payloads, and Get-Plugin listing.
**Citations:** docs/design-notes/dev-rules.design.md:28-31; scripts/skalary/registry.json#plugins; .github/.skalary/receipts/factory-loop.json:1-10; .github/.skalary/receipts/repository-maintenance.json:1-10; .github/.skalary/receipts/workshop.json:1-10; scripts/skalary/Get-Plugin.ps1:40-57
**Expectation or rationale:** The prior broad pass used the local dev rule requiring common immutable plugin receipts and payload-hash verification after bulk updates; this expectation applies to installed Skalary tooling, not the Klikety application.
**Current behavior / reachability:** The earlier pass measured 15 receipts, 12 registry entries, three omitted plugin names, and 31 mismatches among 152 registry-listed payload hashes. The operator clarified that installed Skalary plugins are outside the requested Klikety product scope.
**Impact:** No impact on the Klikety application is asserted. This observation is retained only to preserve the earlier report history and is excluded from the current product assessment.
**Exceptions / counter-evidence:** All receipts in that separate plugin scope shared one immutable ref. The operator explicitly narrowed this survey to Klikety rather than those installed plugin artifacts.
**Uncertainty and coverage limits:** The Skalary repository and the intent behind its local snapshot were not examined. No conclusion about Skalary plugin correctness is part of this report.
**Recommended action:** No action under the Klikety product survey. Reassess only in a separately requested Skalary/plugin-management audit.
**Benefits:** Preserves provenance while keeping the current product findings scoped to Klikety.
**Tradeoffs:** The record retains an explicitly out-of-scope history item rather than deleting the earlier entry.
**Effort:** 1/10
**Complexity:** 1/10

<!-- rcs-finding: RCS-PLAN-000021-IDENTITY -->
### RCS-PLAN-000021-IDENTITY - Plan 000021 cannot be resolved unambiguously
**Category:** drift
**Subject:** Plan 000021 cannot be resolved unambiguously
**Scope:** Active keyboard-layout plan 021, archived plan inventory, linked context assets, and plan state consumers.
**Citations:** docs/implementation-plans/021-keyboard-layout-refresh/plan.md:2-5; docs/implementation-plans/021-keyboard-layout-refresh/plan.md:13-24; docs/implementation-plans/021-keyboard-layout-refresh/plan.md:28-77; .github/skills/rcs/scripts/PlanState.psm1:1391-1449; .github/skills/rcs/scripts/PlanState.psm1:1466-1518; .github/skills/rcs/scripts/PlanState.psm1:1531-1575; .github/skills/rcs/scripts/Get-PlanState.ps1:35-39
**Expectation or rationale:** Plan inventory includes both active and archived records; New-PlanId avoids IDs already in that inventory, and Resolve-Plan rejects ambiguous matches rather than choosing one silently.
**Current behavior / reachability:** The index contains active and archived records with canonical ID 000021. Get-PlanState 000021 fails with an ambiguous-reference error. The active record links eight assets, but its assets directory is absent; its 12 executable checklist items are all checked.
**Impact:** Supported state/admission/archive consumers cannot identify the plan by its canonical ID, and the active record does not provide the context files its own index says are loaded on demand.
**Exceptions / counter-evidence:** All 12 checklist items are marked complete and the title says DONE; the archived sibling may preserve historical material. These facts do not establish which record is canonical or satisfy an archive gate while state resolution fails.
**Uncertainty and coverage limits:** The historical intent and review context were not consumed, so the reason for the active/archive duplicate and intended asset location are unknown. Do not infer archival completion from the checkboxes alone.
**Recommended action:** Reconcile the active/archive identity while preserving historical links, restore or correct the active asset references, then verify the index is unique and Get-PlanState returns a full state. If archiving is separately selected, use the existing /ci route after its completion gates pass.
**Benefits:** Restores reliable plan lookup and makes status, context, and any later archive decision auditable.
**Tradeoffs:** Identity and link repair may require preserving old references or selecting a canonical record; archival is a separate operator decision and is not part of this survey.
**Effort:** 4/10
**Complexity:** 4/10

<!-- rcs-finding: RCS-MACRO-COMPOSITION -->
### RCS-MACRO-COMPOSITION - Macro collaborators are wired after coordinator construction
**Category:** design-architecture
**Subject:** Macro collaborators are wired after coordinator construction
**Scope:** Application bootstrap and macro picker/playback dependencies passed through NavigatorCoordinator to MacroHandler.
**Citations:** src/Klikety/App.xaml.cs:158-205; src/Klikety/NavigatorCoordinator.cs:63-82; src/Klikety/NavigatorCoordinator.cs:85-110; src/Klikety/Navigation/MacroHandler.cs:24-33; src/Klikety/Navigation/MacroHandler.cs:60-67
**Expectation or rationale:** Optional architecture improvement: establish the coordinator and macro subsystem dependency graph at construction so lifecycle ownership is explicit.
**Current behavior / reachability:** App creates NavigatorCoordinator before assigning its macro hotkey, picker, playback window, and click indicator through mutable pass-through properties; MacroHandler stores these as mutable optional collaborators.
**Impact:** Macro dependency ownership and initialization order are split between the composition root and coordinator, increasing the chance of future partially configured wiring and making lifecycle changes harder to reason about.
**Exceptions / counter-evidence:** Production bootstrap assigns the collaborators synchronously before normal event processing, so no runtime failure was demonstrated. The setter seam is also used by integration tests.
**Uncertainty and coverage limits:** No macro lifecycle defect was observed; this is a maintainability proposal, not a contract violation.
**Recommended action:** Consider constructing a small macro-integration dependency object before NavigatorCoordinator and passing it into the constructor; preserve a clear test seam and keep overlay ownership with the coordinator.
**Benefits:** Makes required and optional collaborators visible at the composition boundary and removes post-construction configuration ordering.
**Tradeoffs:** Changes constructor/test setup and may add a small options type; the current synchronous setter wiring is simpler while collaborator count remains stable.
**Effort:** 3/10
**Complexity:** 4/10

<!-- rcs-finding: RCS-MACRO-CANCEL-INDICATOR -->
### RCS-MACRO-CANCEL-INDICATOR - Escape can still dispatch a macro action during indicator animation
**Category:** drift
**Subject:** Escape can still dispatch a macro action during indicator animation
**Scope:** Macro playback cancellation across MacroHandler, MacroPlayer, IClickIndicator, and ClickIndicatorAdapter.
**Citations:** docs/design-notes/macros.design.md:113; src/Klikety/Navigation/MacroHandler.cs:147-155; src/Klikety/Navigation/MacroPlayer.cs:116-121; src/Klikety/Navigation/MacroPlayer.cs:200-224; src/Klikety/Services/ServiceInterfaces.cs:128-130; src/Klikety/Overlay/ClickIndicatorWindow.cs:123-145; src/Klikety.Tests/MacroPlayerTests.cs:200-217
**Expectation or rationale:** The macro design note says Escape cancels playback through the cancellation token; cancellation should prevent an action that has not yet been dispatched.
**Current behavior / reachability:** Escape cancels the playback token, and the player checks it before each step and during delay chunks. ExecuteStep then awaits ShowAndWait without a token and dispatches the mouse action afterward without another cancellation check.
**Impact:** If Escape arrives while the click indicator is animating, the current macro click/drag/scroll can still execute after the user requested cancellation; only later steps are stopped.
**Exceptions / counter-evidence:** Cancellation is checked at the next step and throughout normal delay chunks. Existing cancellation coverage cancels after a completed step, not during an indicator wait.
**Uncertainty and coverage limits:** The current-step action cannot be canceled once SendInput begins. The finding concerns the pending indicator-to-action interval, which is observable in the implementation but was not run in a live UI.
**Recommended action:** Make the indicator wait cancellation-aware or check the token immediately after it completes and before dispatch; add a test that cancels while a fake indicator is pending and asserts no action is sent.
**Benefits:** Escape reliably prevents pending user-visible macro actions and the cancellation contract remains testable.
**Tradeoffs:** The indicator adapter needs a cancellation/cleanup path so a canceled animation is hidden and its completion subscription is released.
**Effort:** 3/10
**Complexity:** 3/10

<!-- rcs-finding: RCS-MOUSE-PARTIAL-SEND -->
### RCS-MOUSE-PARTIAL-SEND - Simple mouse actions ignore partial SendInput results
**Category:** drift
**Subject:** Simple mouse actions ignore partial SendInput results
**Scope:** MouseActionService cursor movement and click injection, excluding the drag path that already compensates partial sends.
**Citations:** docs/design-notes/win32-interop.design.md:47-50; src/Klikety/Services/MouseActionService.cs:67-87; src/Klikety/Services/MouseActionService.cs:104-147; src/Klikety/Services/MouseActionService.cs:216-252; src/Klikety.Tests/MouseNormalizationTests.cs:1-22
**Expectation or rationale:** The Win32 design note recognizes partial SendInput outcomes and compensation. A click should not proceed as if its target move and complete button down/up sequence succeeded when the API reports fewer events.
**Current behavior / reachability:** MoveTo discards the SendInput count, then SendAction proceeds with a separate click call. The no-modifier click batch also discards its count; the modifier path compensates modifier key-ups but not a mouse button-down. SendDrag explicitly compensates button-up on partial sends.
**Impact:** A failed move followed by a successful click can act at the previous cursor location; a partial click prefix ending after button-down can leave the button pressed and produce drag-like behavior.
**Exceptions / counter-evidence:** The drag path checks partial counts and sends compensating button-up events; modifier paths release modifiers. No partial-send failure was reproduced during this read-only survey.
**Uncertainty and coverage limits:** The inspected unit tests cover coordinate normalization but not native SendInput outcomes. OS failure behavior was not dynamically exercised.
**Recommended action:** Check and surface movement and click SendInput counts; suppress the click when the move fails, compensate any potentially pressed mouse button on partial batches, and add an injectable input sender for deterministic failure tests.
**Benefits:** Prevents a failed coordinate move or incomplete button pair from silently becoming an unintended click or held button.
**Tradeoffs:** Requires an input-sender seam and explicit failure handling in the service contract and callers.
**Effort:** 5/10
**Complexity:** 5/10

<!-- rcs-finding: RCS-MACRO-TIMING-CONTRACT -->
### RCS-MACRO-TIMING-CONTRACT - Macro step timing documentation conflicts with recorder semantics
**Category:** alignment-question
**Subject:** Macro step timing documentation conflicts with recorder semantics
**Scope:** RelativeTimeMs semantics across macro design notes, recording, playback, and tests.
**Citations:** docs/design-notes/macros.design.md:35; src/Klikety/Navigation/MacroRecorder.cs:136-138; src/Klikety/Navigation/MacroRecorder.cs:192; src/Klikety/Navigation/MacroRecorder.cs:232-240; src/Klikety/Navigation/MacroRecorder.cs:306-320; src/Klikety/Navigation/MacroPlayer.cs:116-120; src/Klikety.Tests/MacroPlayerTests.cs:127-136
**Expectation or rationale:** The current macro design note defines RelativeTimeMs as an offset from recording start, while recorder and playback code need one explicit, compatible timing contract.
**Current behavior / reachability:** MacroRecorder reads _stepTimer.ElapsedMilliseconds and restarts the timer after each action/scroll, producing inter-step intervals. MacroPlayer waits that value before each step; its test expects the sum of step delays. The design note instead says offsets are relative to recording start.
**Impact:** The persisted field semantics and design guidance disagree, so future playback or migration changes could unexpectedly alter timing of saved macros.
**Exceptions / counter-evidence:** Recorder, player, and tests consistently implement inter-step delay semantics; the current behavior may be intentional and only the design note may be stale.
**Uncertainty and coverage limits:** No operator source establishes whether absolute timeline offsets or inter-step delays are the intended stable format.
**Recommended action:** Confirm that persisted RelativeTimeMs values are inter-step delays and update the design note, or confirm absolute offsets and plan a versioned compatibility-safe recorder/player change; do not change behavior before that decision.
**Benefits:** A single explicit contract prevents timing regressions and clarifies the serialized macro format.
**Tradeoffs:** Changing the stored interpretation would affect existing macro files; documentation-only correction is lower effort if interval semantics are intended.
**Effort:** 2/10
**Complexity:** 3/10
## Operator decisions

No operator decisions recorded.

<!-- rcs-decision: RCS-D-0001 -->
### RCS-MACRO-CANCEL-INDICATOR - 2026-10-04 - corrective-plan
**Finding:** RCS-MACRO-CANCEL-INDICATOR
**Disposition:** corrective-plan
**Date:** 2026-10-04
**Rationale:** Operator selected corrective planning and confirmed the complete a5c375 planning context.
**Affected scope:** Pending macro dispatch, indicator lifecycle and nonblocking playback teardown
**Assumptions:** Planning is confirmed; implementation is pending. Preserve confirmed scope, historical material and explicit stop conditions.
**Revisit when:** Implementation evidence changes the finding, scope or assumptions; confirm affected criteria before further changes.
**Evidence:** src/Klikety/Navigation/MacroPlayer.cs:200-224; docs/implementation-plans/archived/standalone-2026-10-04-a5c375-klikety-maintenance-corrections/plan.md:1-4
**Successful handoff:** created: docs/implementation-plans/standalone-2026-10-04-a5c375-klikety-maintenance-corrections/plan.md (historical location; now [archived](implementation-plans/archived/standalone-2026-10-04-a5c375-klikety-maintenance-corrections/plan.md))

<!-- rcs-decision: RCS-D-0002 -->
### RCS-MOUSE-PARTIAL-SEND - 2026-10-04 - corrective-plan
**Finding:** RCS-MOUSE-PARTIAL-SEND
**Disposition:** corrective-plan
**Date:** 2026-10-04
**Rationale:** Operator selected corrective planning and confirmed the complete a5c375 planning context.
**Affected scope:** Typed native input outcomes, release compensation and caller failure handling
**Assumptions:** Planning is confirmed; implementation is pending. Preserve confirmed scope, historical material and explicit stop conditions.
**Revisit when:** Implementation evidence changes the finding, scope or assumptions; confirm affected criteria before further changes.
**Evidence:** src/Klikety/Services/MouseActionService.cs:67-147; docs/implementation-plans/archived/standalone-2026-10-04-a5c375-klikety-maintenance-corrections/plan.md:1-4
**Successful handoff:** created: docs/implementation-plans/standalone-2026-10-04-a5c375-klikety-maintenance-corrections/plan.md (historical location; now [archived](implementation-plans/archived/standalone-2026-10-04-a5c375-klikety-maintenance-corrections/plan.md))

<!-- rcs-decision: RCS-D-0003 -->
### RCS-PLAN-000021-IDENTITY - 2026-10-04 - corrective-plan
**Finding:** RCS-PLAN-000021-IDENTITY
**Disposition:** corrective-plan
**Date:** 2026-10-04
**Rationale:** Operator selected corrective planning and confirmed the complete a5c375 planning context.
**Affected scope:** Retained keyboard-layout plan identities, assets and affected references; no archival
**Assumptions:** Planning is confirmed; implementation is pending. Preserve confirmed scope, historical material and explicit stop conditions.
**Revisit when:** Implementation evidence changes the finding, scope or assumptions; confirm affected criteria before further changes.
**Evidence:** docs/implementation-plans/021-keyboard-layout-refresh/plan.md:2-24; docs/implementation-plans/archived/standalone-2026-10-04-a5c375-klikety-maintenance-corrections/plan.md:1-4
**Successful handoff:** created: docs/implementation-plans/standalone-2026-10-04-a5c375-klikety-maintenance-corrections/plan.md (historical location; now [archived](implementation-plans/archived/standalone-2026-10-04-a5c375-klikety-maintenance-corrections/plan.md))

<!-- rcs-decision: RCS-D-0004 -->
### RCS-MACRO-TIMING-CONTRACT - 2026-10-04 - corrective-plan
**Finding:** RCS-MACRO-TIMING-CONTRACT
**Disposition:** corrective-plan
**Date:** 2026-10-04
**Rationale:** Operator selected corrective planning and confirmed the complete a5c375 planning context.
**Affected scope:** Inter-step timing guidance; preserve stored values and speed calculation
**Assumptions:** Planning is confirmed; implementation is pending. Preserve confirmed scope, historical material and explicit stop conditions.
**Revisit when:** Implementation evidence changes the finding, scope or assumptions; confirm affected criteria before further changes.
**Evidence:** docs/design-notes/macros.design.md:35; docs/implementation-plans/archived/standalone-2026-10-04-a5c375-klikety-maintenance-corrections/plan.md:1-4
**Successful handoff:** created: docs/implementation-plans/standalone-2026-10-04-a5c375-klikety-maintenance-corrections/plan.md (historical location; now [archived](implementation-plans/archived/standalone-2026-10-04-a5c375-klikety-maintenance-corrections/plan.md))

## Delivered corrections

Archived plan [a5c375](implementation-plans/archived/standalone-2026-10-04-a5c375-klikety-maintenance-corrections/plan.md) delivers the four selected corrections; the survey, findings and original handoff locations above remain the historical source snapshot. Final review and recent learning are committed.

- **RCS-MACRO-CANCEL-INDICATOR:** pending indicator cancellation now skips input/progress; dispatcher-owned lifecycle cleanup and operation-owned teardown avoid synchronous playback drain, including queued starts and dispatcher shutdown. Evidence: `MacroCancellationTests`, `ClickIndicatorLifecycleTests`, `MacroPlaybackTeardownTests`.
- **RCS-MOUSE-PARTIAL-SEND:** typed requested/sent outcomes suppress clicks after failed movement, track sent-prefix held inputs and attempt one release-only cleanup with explicit cleanup failure. Every caller observes outcomes; failed macros stop as `InputFailed` with one playback error. Drag completion precedes the next unchanged saved interval. Evidence: `MouseInputFailureTests`, `MouseInputSuccessTests`, `MacroInputFailureTests`, `InputFailureCallerTests`, `MacroDragSequencingTests`.
- **RCS-MACRO-TIMING-CONTRACT:** macro guidance now describes persisted inter-step intervals, including first-step and drag-pair timing. Saved format, values, scaling and floors are unchanged. Evidence: `MacroPlayerTests.Play_SpeedModifier1_CorrectDelays`.
- **RCS-PLAN-000021-IDENTITY:** [000021](implementation-plans/archived/021-keyboard-layout-refresh/plan.md) remains the historical archived record; its [retained active-path copy 36ef5d](implementation-plans/021-keyboard-layout-refresh/plan.md) has a unique identity and eight faithfully restored assets. Commit `499a708` archived the original and its assets; `cad26e1` recreated the identical active file alone. Both paths/content remain, with no new archival of either target. Evidence: `scripts/Test-KeyboardLayoutPlanRecords.ps1` checks index uniqueness, exact supported full states and affected local links.
