# RCS audit guide

## Scan ownership boundary

The default survey covers customer repository-owned content, not installed Skalary plugins. Resolve
this boundary before reading candidate content or running broad text searches:

- Use available local installation metadata to identify Skalary payload paths: direct installs have
  per-plugin receipts under `.github/.skalary/receipts`; their names identify plugins, not per-file
  ownership. Match them to `files[].dest` in the corresponding manifest or catalog and prefix each
  destination with `.github/`. Prefer metadata matching the receipt's installed ref; a newer catalog
  alone does not establish the complete installed file set. For host-managed plugins, use the host's
  plugin origin and installation root. Do not fetch a remote checkout to expand a routine survey.
- Exclude identified installed payload files (skills, agents, prompts, scripts, bundled assets and
  templates), including tracked files and locally modified installed copies. Exclude Skalary receipt,
  catalog, marketplace, cache, and bootstrap/lifecycle tooling from customer-code review. Loading
  `/rcs` guidance or invoking its helpers is tool use, not surveying those tools for findings.
- Do not blanket-exclude `.github/`, `skills/`, `agents/`, `prompts/`, or `plugins/`. Customer-authored
  instructions, skills, agents, prompts, workflows, source, tests, and configuration remain in scope,
  including additional customer files beside installed payloads. First-use project content such as
  plan records, design/architecture notes, local standards, and the maintenance record remains in
  scope even when created from a Skalary scaffold; `scaffolds[]` are not installed `files[]`.
- In the Skalary source repository, canonical plugin sources and tooling are repository-owned and
  remain in scope. Use its local manifests to exclude generated dogfood destinations under `.github/`;
  do not mistake the canonical sources for a customer installation.
- If ownership cannot be established from available metadata, mark the ambiguous paths as a coverage
  gap and leave them unscanned until the operator clarifies ownership. Do not guess from a skill name,
  file extension, or Git tracking, and do not silently include suspected installed payloads.

Build an explicit repository-owned path set and constrain content searches, subsystem discovery,
dead-code traces, and delegated scopes to it. Do not run an unfiltered recursive repository search
and discard plugin matches afterward. Stop a trace at an installed-plugin boundary; describe the
customer integration or call site, not the plugin internals. Installed manifests may be read as
ownership metadata, but their entry points are not customer subsystems or dead-code candidates.
Record excluded payload paths/roots and ownership gaps in coverage. An earlier plugin finding outside
the corrected scope is preserved as unreviewed history, not revalidated, resolved, or handed to `/cip`.

## Precedence and classification

Use current explicit human intent, confirmed plan criteria, active architecture contracts, applicable
design notes, local coding standards, and relevant operator documentation with provenance. Start at
`docs/design-notes/.design-notes.md`, `docs/architecture-notes/.architecture-notes.md`, and
`docs/review-standards.md` when present; plan sources are under `docs/implementation-plans`. Current
source establishes behavior, not intent. Historical reports are leads only. A cleanup disposition
cannot supersede a confirmed criterion or locked contract.

For each selected trace, record the applicable expectation or improvement rationale, current
implementation location and behavior, a concrete trigger or reachability path, impact, exceptions
and counter-evidence, uncertainty, and the smallest useful action. Cite repo-relative paths and
specific lines or symbols. Distinguish:

- **Defect/drift:** evidence shows current behavior violates an applicable expectation or contract.
- **Alignment question:** expectations conflict, are ambiguous, or omit a decision needed to judge.
- **Unfinished work:** plan work is still pending or blocked; it is not automatically drift.
- **Optional improvement:** a concrete design, architecture, standards, or maintainability opportunity
  without a claimed requirement violation.
- **Coverage gap:** source, subsystem, test, trace, or tooling was not inspected or could not be read.

Respect explicit, scoped exceptions unless their assumptions, evidence, or scope changed. State why a
source applies; do not turn generic preference into a defect. If a trace cannot be completed, label it
incomplete and identify the next evidence needed.

## Inventory and risk selection

Summarize the surveyed plan/epic corpus and discovered subsystems before deep review. Use existing
indexes, repository-owned manifests, entry points, project/build files, and layout; classify boundaries from evidence,
not a new subsystem list. Include active and archived plan inventory, legacy layout, plans with no
intent, malformed records, and absent corpora as applicable. For relevant plans inspect current state;
historical intake is capped at three selected Markdown artifacts in total. Disclose when the current
index cannot search human intent/epic goal snippets, or when a relevant human source remains unread.

Prioritize concrete risk: externally reachable behavior, security-sensitive or destructive paths,
public interfaces, cross-subsystem coupling, unsupported state transitions, and requirements with
thin or contradictory evidence. Show surveyed, traced, skipped, blocked, and unread areas separately.
Do not assign a quality score or certify the whole repository.

## Coding standards and cross-codebase design

Inspect the active local standards and relevant repository standards before generic conventions.
Apply Simplicity First: prefer deletion/reuse/local change before machinery. Identify standard drift
only when a specific standard applies and current code evidence demonstrates a mismatch.

Include concrete codebase-wide architecture/design proposals, not only plan findings. Cite the
subsystems and locations involved; explain the current coupling, duplication, responsibility overlap,
or avoidable complexity, the smallest corrective change, expected benefit, tradeoff, effort (1-10),
and complexity (1-10). Label preferences as optional improvement, not as defects.

## Dead-code reachability

No-reference alone is insufficient evidence. Enumerate the roots and mechanisms investigated:
commands, scripts and package tasks; public/exported APIs; plugin/manifest registrations; manual
maintenance tools; tests; configuration; dependency injection, reflection, events and dynamic dispatch;
generated code; documentation-supported entry points; and plausible external consumers. Generated
copies are not independent removal targets.

An unused private helper can be a removal candidate only after repository-owned callers and relevant
indirect paths are traced. Preserve exported APIs, operator-invoked tools, registered handlers, and
test fixtures unless the owning contract is explicitly changed. If language tooling is unavailable or
external callers cannot be established, describe the uncertainty and propose investigation, not
unconditional deletion. `/rcs` never deletes code; corrective removal goes through a normal plan.

## Recommendation shape

Each candidate states category, subject and scope; source expectation or improvement rationale;
specific citations; current behavior/reachability; trigger; impact; exceptions/counter-evidence;
uncertainty; smallest action; benefits and tradeoffs; effort and complexity from 1 to 10; and
applicable corrective-plan/archive handoff. Cite negative evidence and the roots searched for
dead-code claims. Never present an uninspected subsystem as clean.
