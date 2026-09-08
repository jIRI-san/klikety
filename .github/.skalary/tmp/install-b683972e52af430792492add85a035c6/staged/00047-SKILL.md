---
name: autopilot
description: Autonomous execution mode orchestration for /ci
user-invocable: false
disable-model-invocation: true
---

# Autopilot

Accept `/ci`'s selected runtime and one-phase/whole-plan extent and keep the current launcher, auth,
offline-package, branch, and exit-code contracts. Before launch, require `/ci` to have passed
`Test-PlanCriteriaBaseline`; every launched agent repeats the same installed sibling
`DirectWorkflow.psm1` baseline check before mutation.

The launcher remains blocking and reports `0` completed, `42` operator action, `43` offline rebundle,
and every other nonzero failure exactly. Duration alone never cancels progressing work. Keep declared
deterministic build, test, command, and offline operation timeouts; their failures become evidence
handled by the agent. Preserve recoverable Git and Markdown progress for every non-completed outcome.

Autonomous execution defaults to zero delegated calls and uses the cheap-first model ladder, direct
evidence, risk-selected non-terminal review, one terminal `primary-model-mid` review, and the three-call ceiling
described by the autopilot agent. It has no automatic Judge and does not persist auxiliary review or
learning lifecycle state beyond `assets/ai-credits.json`. Delegated prompts attach at most three artifacts, target 400 words, and must
be narrowed before 800. If the run needs a complex operator
decision, the agent stops with `42` and returns the
host-equivalent context, examples, benefits, pros/cons, recommendation/default, effort, complexity, and
relationship/sequence diagram defined there; free-form input is one focused question at a time.

During finalization, invoke `./assets/design-note-compaction.md` exactly once when the bundled
`.github/skills/autopilot/scripts/Get-DesignNoteCompactionContext.ps1 -RepoRoot
<canonical-repo-root>` reports edited
`docs/design-notes/**`. Never self-approve its cross-note merge/delete proposal: preserve the visible
diff and stop with `42`. Do not invoke it for `docs/operator-guide/**` or other changes alone.

After the completed source commit exists, invoke installed
`.github/skills/autopilot/scripts/Write-RecentLearning.ps1` with zero to ten concise lessons and one
repo-relative source-commit citation per lesson. Commit the replacement; never append it or write
auxiliary history, recovery, or lifecycle state.

Phase targets execute and close only their named phase. For `whole-plan`, the launcher follows the
closed phase set with one explicit completion target. That target alone owns final focused validation,
the terminal review, compaction, and recent-learning publication; on resume it reuses an unchanged
terminal result rather than duplicating review.
