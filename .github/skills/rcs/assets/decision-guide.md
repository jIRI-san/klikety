# RCS decisions and handoffs

## Scoped dispositions

No disposition is inferred from silence. An unchanged `accepted-intentional-drift` or `wont-fix`
decision suppresses repeating that question only while its cited evidence, assumptions, and stated
scope remain unchanged. Reopen on changed evidence, trigger, scope, or assumption and preserve the
older rationale.

Accepted drift and won't-fix decisions require the operator's explicit choice, date, rationale,
affected scope, assumptions, revisit condition, and evidence citations. They do not erase original
intent, waive future defects, change confirmed criteria, or override a locked contract. Use installed
`Write-RepositoryMaintenanceRecord.ps1 -Action RecordDisposition -RepoRoot . -PayloadJson <JSON>`
after the operator chooses. A changed criterion returns through `/cip`; a contract change uses the
existing architecture-maintenance approval flow.

## Corrective planning

For a chosen correction:

1. Search current active plans for outcome, scope, and evidence overlap. Report exact candidates and
   recommend reuse/update before new creation. Do not silently merge findings or create duplicates.
2. The operator chooses an existing plan or `/cip` creates a normal corrective plan. Supply original
   expectation, current evidence, desired correction, boundaries, verification, and any affected
   criteria. The operator selects the exact plan to reuse/update or the new-plan handoff. Keep
   `/cip`'s interview, review, confirmation, and baseline gates; changed confirmed criteria must be
   reconfirmed there, and locked-contract changes use the architecture-maintenance approval flow.
3. If the handoff is absent, cancelled, incomplete, or fails, leave the maintenance finding pending
   and state the precise blocker. Creating a plan is not a delivered fix.
4. Record the linked plan and successful handoff only after its actual outcome is established. Recheck
   implementation later before calling a finding resolved.

The same route applies to dead-code, coding-standard, and optional architecture/design proposals.
Removal is planned work, never direct `/rcs` deletion.

## Separate archival choice

Archiving is neither a drift disposition nor an automatic side effect. First establish current plan or
epic completion from the live plan state and evidence. An already archived item is a no-op: verify and
retain its known archive path rather than proposing another archive. An unknown state is not evidence
of completion; leave the action pending and cite the missing state evidence. A clean maintenance
report, clean worktree, or old review does not prove completion. Reject pending/blocked work, missing
evidence, unresolved dependencies, linked source paths, unarchived epic children, or destination
collisions.

After a separate explicit operator choice, use the existing route. For an epic, installed
`Archive-Epic.ps1` refreshes its generated child table and refuses incomplete or unarchived children.
For a standalone plan, use the repository's existing `/ci` completion/archive route; this plugin
ships no standalone archive command. If the route is missing or unclear, stop pending and report that
limitation. Record a successful archive only after verifying the source is archived and the resulting
path is known.

## Choices

For a complex choice, show the scope and a concrete example, expected benefit, each option's pros and
cons, a recommendation/default, and effort/complexity (1-10). Use a native host choice when available;
otherwise number the same choices. A simple yes/no archive approval needs no expanded brief. One
free-form question at a time.
