---
name: ci
description: 'Continue Implementation — use when executing an implementation plan created by /cip. Selects the active plan from docs/implementation-plans/, detects git branch/worktree state and creates a worktree when on main/master, implements one step at a time, runs dotnet build and dotnet test, invokes @cr for code review, and commits with explicit user approval. Invoke with /ci or /ci <plan-slug>.'
argument-hint: 'Optional: plan slug or filename to select a specific plan (e.g. "001-data-persistence")'
user-invocable: true
disable-model-invocation: true
---

# Continue Implementation

> This skill requires **agent mode** — it writes files, runs terminal commands, and manages git. If you are in plan or ask mode, switch to agent mode before continuing.

## Step 1: Select Plan

Scan `docs/implementation-plans/` for `*.md` files (exclude `archive/`).

- **Argument given** → find the file whose name contains the argument slug; confirm with user.
- **One plan found** → load it; confirm: "Working on: NNN — Plan Title. Correct?"
- **Multiple plans found** → list them with a status summary (count of `[ ]`/`[~]`/`[x]` steps per plan); ask which to use.

## Step 2: Load Context

1. Read and parse the selected plan file.
2. Read `docs/design-notes/.design-notes.md` to get the index.
3. Identify the subsystems touched by the next pending step.
4. Load the relevant design notes for those subsystems.

## Step 3: Branch Detection

Run `git branch --show-current`.

Ask the user: **"Create a new worktree for this work, or use the current branch `<current-branch>`?"**
- Option A: **New worktree** (default for `main`/`master`)
- Option B: **Use current branch** (for one-off or ad-hoc work; skips branch name validation)

### Option B: Use current branch
Proceed directly to Step 4 using the current branch — skip all worktree creation and branch-name matching. No `<!-- worktree: ... -->` comment is recorded.

### Option A: New worktree

#### If current branch is `main` or `master`
1. Find the next `[ ]` step across all phases.
2. Derive the worktree branch name: `feature/<plan-slug>-<phase-slug>-<step-N>`
   - `plan-slug`: the `NNN-<name>` part of the plan filename (strip `implementation-plan-`)
   - `phase-slug`: kebab-case of the phase heading
   - `step-N`: step number (e.g. `step-1-1`)
3. Determine the worktree root: sibling folder to the repo named `<repo-folder>.worktrees` — e.g. `c:\dev\qz` → `c:\dev\qz.worktrees`. Create it if it does not exist (`mkdir` / `New-Item -ItemType Directory`).
4. Run: `git worktree add <worktree-root>/<branch-name> -b <branch-name>`
5. Run: `code <worktree-root>/<branch-name>` to open a new VS Code instance in the worktree.
6. Tell the user: "Worktree created at `<worktree-root>/<branch-name>`. New VS Code window opened. Run `/ci` there to continue."
7. **Stop** — do NOT record the branch in the plan file here; that happens on first run inside the worktree.

#### If current branch is a feature branch
Check the plan file for a `<!-- worktree: <branch-name> -->` comment in the current or next pending phase:

- **Comment absent** — this is the first `/ci` run in this worktree. Record it now: add `<!-- worktree: <current-branch> -->` on the line immediately after the phase heading. This comment is committed with the first step's changes as part of that step's commit.
- **Comment present and matches current branch** → continue to Step 4.
- **Comment present but does not match** → warn: "Current branch `<current>` does not match plan branch `<recorded>`. Proceed anyway? (yes / no)"

## Step 4: Identify Next Step

1. Find the next `[ ]` or `[~]` step in the plan (top-down, first incomplete phase, first incomplete step).
2. Update its status to `[~]` in the plan file.
3. Present the step to the user: **"Next: Step X.Y — [title]. Scope: [brief description of what will change]. Proceed?"**
4. Wait for confirmation before implementing.

## Step 5: Implement

Implement the single confirmed step — not the full phase.

- Follow patterns from the loaded design notes.
- Make only the changes necessary for this step.
- Do not refactor unrelated code.

## Step 6: Build and Test

```
dotnet build src/Qz/Qz.csproj
dotnet test src/Qz.Tests/Qz.Tests.csproj [--filter <relevant-filter>]
```

If a relevant test filter can be identified from the changed subsystem (e.g. `Category=Scheduling`), use it. Otherwise run all tests.

If build or tests fail: diagnose, fix, and re-run. Iterate until both pass.

## Step 7: Code Review

Invoke `@cr` scoped to the current branch changes (`cr branch`).

- Print the **complete `@cr` output verbatim** — do not summarize or truncate.
- Ask which findings to fix (by number, range, or "all").
- Apply the selected fixes.
- Re-run build and tests until both pass.

## Step 8: Update Design Notes

Run `/udn` to update any design notes affected by this step's changes.

- `/udn` analyzes the current chat session and edits the relevant files under `docs/design-notes/`
- Include the updated design notes in the commit in the next step

## Step 9: Commit

Ask: **"Ready to commit? (yes / no)"**

Wait for explicit "yes" before proceeding.

On approval:
1. Stage only the files touched in this step: `git add <file1> <file2> ...` (never `git add -A`)
2. Commit: `git commit -m "feat(<scope>): <step title> [plan-NNN step X.Y]"`
   - `scope`: the primary subsystem changed (e.g. `scheduling`, `orchestration`)
3. Mark the step `[x]` in the plan file.
4. Commit the updated plan file and any updated design notes: `git commit -m "chore: mark plan-NNN step X.Y done"`

## Step 10: Continue or Pause

After committing, check if all steps in the plan are `[x]` (see Step 11).

If not all done, ask: **"Continue to the next step or stop here?"**

- **Continue** → loop back to Step 4.
- **Stop** → summarize progress (steps done, steps remaining) and exit.

## Step 11: Plan Completion

After each commit, check whether every step across every phase is `[x]`.

If complete:
1. Edit the plan file title to append `[DONE]`: `# NNN: Plan Title [DONE]`
2. Move the file to `docs/implementation-plans/archive/` using PowerShell (Move-Item handles the delete of the original):
   `Move-Item docs/implementation-plans/<file>.md docs/implementation-plans/archive/<file>.md`
3. Stage the move: `git add docs/implementation-plans/archive/<file>.md` and `git rm docs/implementation-plans/<file>.md`
4. Commit: `git commit -m "chore: archive completed plan NNN"`
5. Tell the user: "Plan NNN is complete and archived."
