---
name: ci
description: 'Continue Implementation — execute confirmed plan criteria with direct evidence and bounded review.'
argument-hint: 'Optional plan reference'
user-invocable: true
disable-model-invocation: true
context: fork
---

# Continue Implementation

Resolve state and admission with existing deterministic plan scripts. Before any checklist, branch,
worktree, log, or source mutation, call installed sibling `DirectWorkflow.psm1`
`Test-PlanCriteriaBaseline`. Refuse missing, uncommitted, ambiguous, or byte-drifted intent,
requirements, risks, or decisions and return to `/cip`; checklist, stage, and worktree markers remain
mutable.

When installed `Get-PlanState.ps1` returns `Kind: epic`, take the hard host-only route:

```powershell
$epicScripts = Join-Path <canonical-repo-root> '.github/skills/autopilot/scripts'
& (Join-Path $epicScripts 'Invoke-EpicAutopilot.ps1') -Epic <state.EpicId> `
    -Target HEAD -RepoRoot <canonical-repo-root>
```

Bind the canonical `EpicId`, literal local `HEAD`, and the same canonical root used for state
resolution. Do not select `NextChild` or fall through to ordinary plan handling. If
`AUTOPILOT_CONTAINER=true`, refuse because the epic wrapper is host-only. Block on the wrapper and
preserve its exact exit status and outcome. A successful terminal epic result refreshes the child
mirror and moves the completed index to `docs/implementation-plans/archived/epics/` through installed
`Archive-Epic.ps1`; archived epics remain resolvable history and are never relaunched.

Work directly with zero delegated calls. Resolve aliases through
[`model-aliases.psd1`](./assets/model-aliases.psd1). Routine work uses `primary-model-low`/medium with
`secondary-model-low` replacement. One unresolved design/acceptance choice permits a combined
`primary-model-mid`/high Designer/Validator with `secondary-model-mid` replacement. Use `primary-model-high`/high
after unresolved standard work, and one `secondary-model-high`/high pass only for a named high-risk
concern. No automatic Judge exists: deterministic evidence is
the normal judge. Every call, retry, and replacement counts toward a three-call ceiling; a fourth
requires a new operator decision. For observable background work, compare tool output, file/commit
state, completed subtasks, and blockers across two checks; redirect once, replace once within budget,
then stop. Never kill an agent for elapsed time. Retain deterministic build/test/command timeouts as
evidence. Delegated prompts attach at most three artifacts, target 400 words, and must be narrowed before
800.

For a complex predefined operator choice, provide context, an example, benefits, pros/cons,
recommendation/default, effort 1-10, and complexity 1-10. Add Mermaid only when relationships or sequencing matter. Pass
the same ordered list to `vscode_askQuestions` in VS Code or number it in Copilot CLI. Ask free-form
input one focused question at a time.

Run focused validation and call `Invoke-DirectEvidence` for only `test:`, `file:`, and `review:`.
Supply the active in-memory review result, current source, and requested scope; persisted
Markdown is not authority. Keep completed/refused/blocked/stuck/interrupted outcomes distinct. Exit `0`
means completed, operator-action stops retain `42`, and other failures are nonzero.

If a concrete changed-scope risk exists, non-terminal review may select one `primary-model-mid`/high event and at most
one corrective replacement. The terminal phase skips post-phase review; finalization runs one
`primary-model-mid`/high whole-plan direct CR. If scope is unchanged, do not rerun it. Exhausted calls or
unresolved/incomplete review stops non-clean.
During finalization, invoke `.github/skills/autopilot/assets/design-note-compaction.md` once when
`Get-DesignNoteCompactionContext.ps1` reports edited `docs/design-notes/**`; other docs do not trigger it.
On completion, run `Write-RecentLearning.ps1` against the source commit with zero to ten
repo-cited lessons. Commit its replacement of `docs/feedback/recent-learning.md`; create no extra state.

Before interactive archival, offer installed `/pfb`. Decline, no answer, or an absent skill skips it:
feedback is never blocking and never substitutes
for a failed evidence marker. Headless execution skips `/pfb`; it never queues or invents a verdict.
