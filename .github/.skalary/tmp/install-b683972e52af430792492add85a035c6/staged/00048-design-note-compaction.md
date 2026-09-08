# Design-note finalization compaction

Run this pass once per `/ci` or autopilot finalization invocation, after implementation and before final
focused validation and the terminal review. Do not run it per step or as a non-terminal review. Use the
active skill's bundled sibling `Get-DesignNoteCompactionContext.ps1 -RepoRoot
<canonical-repo-root>` with the plan criteria-baseline commit. Run only when its Git inventory reports
a changed path under `docs/design-notes/**`; changes only
under `docs/operator-guide/**` or elsewhere do not trigger it. A resumed cross-note approval continues
the visible proposal instead of starting a second pass. Keep this an in-memory workflow action: create
no service, schema, receipt, or corpus cache.

## Select bounded candidates

1. Read `docs/design-notes/.design-notes.md`, not the whole note tree. Begin with touched design notes.
2. From index `Scope` and `Key Patterns`, add notes whose scopes overlap changed implementation paths.
   From already loaded touched notes, add directly linked notes and notes sharing named concepts. Add
   active notes explicitly chosen by the operator.
3. Pass the selected paths back to `Get-DesignNoteCompactionContext.ps1`. Read full text only for its
   current batch of at most five notes, using the baseline Git version for a touched note already deleted
   in the proposal. Process further batches sequentially; between batches retain only a concise
   accumulated summary of candidate path, overlap, unique content to preserve, and proposed owner. Never
   hold all candidate full texts together.

## Preserve semantics

Remove stale implementation narration and repeated rationale or contracts. Merge overlap only when one
clear owner remains. Preserve every unique active decision, architectural contract, constraint,
exception, and minimal representative example. Keep each retained note's frontmatter and globs accurate;
after a move or deletion, update the active index row and all affected links. If ownership or uniqueness
is uncertain, retain the content and do not delete the note.

Same-note duplicate compression may proceed through the ordinary `/ci` review without a separate choice.
Show its final `git diff` before terminal review.

## Cross-note approval

For a cross-note merge or deletion, first materialize the proposed result as ordinary uncommitted
working-tree changes. Before treating it as final, show:

- exact candidate paths, the resulting owner, and each unique item preserved;
- the complete relevant Git diff, including frontmatter, globs, index rows, and links;
- benefits, pros and cons, and `effort: <1-10>` / `complexity: <1-10>`;
- exactly `Apply` and `Cancel`.

Only explicit operator `Apply` authorizes retaining a cross-note merge or deletion. In headless
autopilot, never self-approve: leave the proposed diff visible, report the bounded decision handoff, and
stop with operator-action exit `42`. `Cancel`, uncertainty, or failure also leaves ordinary working-tree
changes visible for correction or Git revert; never claim transactional rollback. Show the final
design-note diff before terminal review in every path.
