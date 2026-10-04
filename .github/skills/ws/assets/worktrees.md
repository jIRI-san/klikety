# Isolated prototype execution

## Common base and worktree rules

Before mutation, inspect `git status --short`, current branch, `git rev-parse HEAD`, and
`git worktree list --porcelain`. Record the source checkout and exact common base SHA. Every
approved variant starts from that SHA, never from the default branch by assumption or another
variant's tip.

If staged, unstaged, or relevant untracked changes exist, stop for the operator to choose a committed
checkpoint or explicitly use committed HEAD without those changes. Never silently stash, reset,
commit the operator's work, or copy a dirty checkout into variants. A detached source needs an
operator-approved branch at the recorded SHA.

Choose unused variant branch names and absolute worktree paths. Prototype edits belong only in their
isolated worktrees. If creation or the initial SHA check fails, stop that variant before editing; do
not fall back to a shared checkout. Keep variants serial when they share mutable services, generated
outputs, or ports. Give concurrent UI demos separate ports.

## Copilot app

Use a native child project session with `workspace_type: "worktree"` only when needed and approved.
Set `base_branch` explicitly to the source branch; do not use the project default implicitly. Before
each session, verify that branch still points to the recorded SHA. The kickoff first verifies its
own HEAD equals that SHA and that its worktree is isolated from the source and peers. If the host
cannot materialize the exact base, stop visibly. Record the returned session ID and use its app URL
when reporting or explicitly transferring the handoff. One implementer may own each approved
variant; do not add coordinator/reviewer sessions, nested delegates, retry fleets, or dynamic
workflows.

Each bounded kickoff includes the approved goal and concept, steps/touchpoints, assumptions,
non-goals, applicable repository constraints, source branch and expected SHA, isolation checks,
exact demo/check, validation budget, and separate port if relevant. Require edits only to its
assigned variant, no `/cip`, `/ci`, reviews, formal plans, evidence bundles, or nested delegation.
Ask it to commit only prototype paths on that branch and report commit SHA, demo, check, shortcuts,
gaps, and blockers. Keep each prompt near 400 words and below 800.

## VS Code and terminal CLI

Use direct Git worktrees with actual branch names, absolute paths, and the recorded SHA:

```text
git worktree add -b <unique-variant-branch> <absolute-new-worktree-path> <common-base-sha>
git -C <absolute-new-worktree-path> rev-parse HEAD
git -C <absolute-new-worktree-path> status --short
```

Verify each HEAD before edits. Direct tools must target the variant worktree; a tool confined to the
source checkout cannot edit another worktree. Open the worktree in a supported agent/editor context
or request an explicit operator action. Never simulate alternatives by switching one checkout
between branches. Stage only prototype paths and commit on the variant branch.

## Inspection, handoff, and retention

Record each variant's branch, absolute path, base SHA, resulting commit, demo, and check in the host
conversation/session artifacts. For each central path, trace the actual source layout and interfaces
through its entrypoint, connection/insertion point, consumer, demo, and touched codepaths. Label
peripheral mocks and unsupported behavior. Keep blocked variants visible; do not imply prompt
contracts or static checks prove live host behavior.

Before CIP, verify that the chosen branch/path/session and commit still match the selection. Transfer
a complete handoff explicitly to the selected app session or have the operator paste it with `/cip`
in the chosen VS Code/CLI worktree. CIP may redesign the prototype and must reconfirm intent. Never
automatically merge, publish, archive, or delete variants. Retain losing worktrees and app sessions
until the operator authorizes cleanup; never discard dirty work.
