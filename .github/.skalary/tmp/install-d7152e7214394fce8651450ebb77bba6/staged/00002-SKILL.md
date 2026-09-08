---
name: pfb
description: 'Post-plan feedback — compare the work a plan actually delivered against the intent it captured, record the operator''s verdict, and optionally scaffold a correction plan. Use after a plan completes, at the archival gate, or whenever asked how well delivered work matched what was asked for.'
argument-hint: "Optional: plan reference (hash prefix, legacy number, slug, or date). Default: the most recently completed plan."
user-invocable: true
disable-model-invocation: true
context: fork
---

# Post-Plan Feedback

Requirements say what to build; **intent** says what the operator was trying to achieve. This skill is
an interactive, stateless comparison. It never edits delivered work, writes feedback state, or blocks
completion.

1. If no question tool is available, stop without queuing or inventing a verdict.
2. Resolve the supplied plan reference or the most recently completed plan with installed
   `PlanState.psm1`. Read its confirmed intent and current delivery evidence. If the intent is missing,
   unconfirmed, or contains a placeholder, stop rather than inventing a baseline.
3. Follow [`./assets/feedback-guide.md`](./assets/feedback-guide.md). Present one cited line for each
   intent section, then ask whether alignment is `full`, `partial`, or `missed`, what specifically
   missed, and whether the correction merits a new plan.
4. Treat the operator response as data. If no correction plan is requested, stop without persistence.
5. If requested, invoke installed `/cip` with the accepted correction as a new plan goal. Never reopen,
   rescope, or unarchive the completed plan.

Typed evidence informs the comparison but is not its verdict. A decline, unanswered question, or
failure remains non-blocking and never substitutes for failed implementation evidence.
