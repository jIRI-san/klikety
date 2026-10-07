# Intent

## Goal

Correct the four selected Klikety maintenance findings in one standalone plan.

## Desired outcome

Cancellation prevents pending macro actions; native input failure is handled and propagated rather than reported as successful playback; plan 000021 is addressable without ambiguity; timing guidance matches persisted inter-step delays.

### Selected operator wording and confirmed interpretation

- Source/date: current conversation, 2026-10-04.
- Operator wording: "/cip" with arguments "for these listed issues"; "Confirm this scope (Recommended)"; "Propagate typed failures to callers and stop/report failed playback"; "Apply all recommendations; report failed playback through typed result and error log (Recommended)".
- Confirmed interpretation: one plan for macro cancellation, incomplete SendInput handling, duplicate plan identity/assets repair, and inter-step timing documentation. Typed results flow to callers, and failed playback is distinct from user cancellation.
- Scope/exception: cancellation covers work not yet dispatched, not input already handed to Windows. Preserve historical plan material; no automatic archival. The macro-composition suggestion stays advisory.
- Earlier operator wording: "Inter-step delays (current code; update docs) (Recommended)".
- Confirmed timing interpretation: retain the existing serialized format and delay computation; correct documentation rather than convert stored values. The selected review edits explicitly make drag completion precede the following saved interval, removing current overlap (about 150 ms of native phase delays). This is an intentional scheduling change, not a timing-format migration.

## Success signals

- A controlled cancellation while an indicator is pending dispatches no action.
- Simulated native failures produce safe suppression/cleanup and explicit failed results; macros stop/report failure.
- Each retained keyboard-layout plan has a unique identity, resolvable assets and working references.
- Timing notes describe the first interval from recording start and subsequent recorder intervals from its preceding timer reset.

## Non-goals

- Macro dependency-composition redesign; installed Skalary/plugin maintenance.
- Undoing OS input after dispatch, bypassing UIPI, retrying clicks or guaranteeing OS acceptance of release compensation.
- New macro-file versions, timing migration, keyboard-layout feature work or plan archival.
- Broad code/security/dependency audits or unrelated native-service modernization.
- Changing which attempted actions the recorder persists; any newly necessary recording-policy decision returns to the operator.

### Delegated discretion and deferred choices

- Authorized choice and bounds: implementation may choose the smallest strongly typed result/sender seam and dispatcher-safe asynchronous wiring. Keep successful action ordering, geometry, modifier behavior and native drag phase delays; next-step scheduling becomes completion-relative. Use a pure indicator-lifecycle seam rather than a WPF/STA test harness; no third-party packages.
- Deferred choice: which duplicate retains 000021 is evidence-dependent. Implementation owner investigates current files/git history and asks the operator if ownership or compatibility remains ambiguous before editing either record.
- Historical context: operator selected archived 015/017 Decisions. The bounded reader refused legacy IDs as noncanonical, then zero-padded IDs as nonunique; no Markdown was accepted or consumed. 000021 is ambiguous. Missing context is not proof of absent historical intent.

## Definition of done

- Confirmed REQ-1 through REQ-5 have focused evidence, directly related docs are updated, changed app/test projects compile, and implementation respects risks/stop conditions.
- Maintenance findings are linked to this plan after planning confirmation; resolution is recorded after delivered changes are verified, not when this plan is created.
- No implementation checklist item is completed during planning.
