---
name: design-notes
description: 'Design-notes toolkit — bootstrap the docs/design-notes/ scaffold, create a new design note, or update existing notes from the current session. Use when initializing design notes in a repo, capturing a new subsystem/decision as a design note, or reflecting freshly implemented changes back into the notes. Invoke with /design-notes init (scaffold), /design-notes create <name> (new note), or /design-notes update (sync notes from this chat).'
argument-hint: 'init (or bootstrap) | create <name> | update'
user-invocable: true
disable-model-invocation: true
context: fork
---

# Design Notes

> **Goal:** keep `docs/design-notes/` an accurate, discoverable record of the project's
> subsystems and decisions. This skill owns three workflows — **init**, **create**, and
> **update** — dispatched by the argument after `/design-notes`.

## Workflow dispatch

Read the argument that follows `/design-notes` and run exactly one workflow:

| Argument | Workflow |
|---|---|
| `init`, `bootstrap`, empty, or unrecognized | **Init** — scaffold `docs/design-notes/` from the bundled templates |
| `create <name>` | **Create** — add a new design note named `<name>` |
| `update` | **Update** — reflect this session's changes into existing notes |

`create` and `update` both depend on the scaffold existing; each runs **Init** first when
[docs/design-notes/.design-notes.md](../../../docs/design-notes/.design-notes.md) is missing.

## Init — scaffold the design-notes structure

Run the bundled owner through an argument array:

```powershell
& ./.github/skills/design-notes/scripts/Initialize-DesignNotes.ps1 -RepoRoot (Get-Location)
```

It creates `docs/design-notes/` and `docs/design-notes/project/` from the installed templates,
stops with `design-notes already initialized` when the index exists, and never overwrites consumer
content. Report the result and point the user at `/design-notes create <name>` and
`/design-notes update` (or their `/cdn` and `/udn` shortcuts).

> The templates are repo-agnostic starting points. After init, edit `docs/design-notes/.design-notes.md`
> to describe your project and add rows to the Available Skills table as you create notes.

## Create — add a new design note

Create a new design note based on the `<name>` provided after `create` (or after `/cdn`).

### Derive the filename

Convert the name to lowercase kebab-case and append `.design.md`. Examples:
- `architecture update` → `architecture-update.design.md`
- `job scheduling` → `job-scheduling.design.md`
- `retry policies` → `retry-policies.design.md`

### Steps

1. **Ensure the scaffold exists.** If [docs/design-notes/.design-notes.md](../../../docs/design-notes/.design-notes.md)
   is missing, run the **Init** workflow above first, then continue.
2. **Read the governance rules** from [docs/design-notes/.design-notes.md](../../../docs/design-notes/.design-notes.md).
   Pay attention to the frontmatter format and the "Adding a New Note" checklist. Also read the writing-style
   guide at `docs/design-notes/project/design-note-writing-style.design.md`.
3. **Infer scope and content** from:
   - Relevant source files already in the workspace that relate to the named topic
   - Anything discussed in this chat session about the topic
   - If neither applies, generate a well-structured skeleton with placeholder sections
4. **Create `docs/design-notes/<subfolder>/<derived-filename>`** by primary scope:
   `architecture/` for component boundaries, `testing/` for test infrastructure, `orchestration/` for
   workflow/agent coordination, `ui/` for user-facing interfaces, or `project/` for repo-wide tooling:
   - YAML frontmatter (`description`, `globs` covering the relevant source paths)
   - An _Overview_ section explaining what the subsystem/topic is
   - An _Implementation_ section with current patterns and code examples
   - A _Design Decisions_ section explaining key "why" choices
   - A _Limitations / Trade-offs_ section for known constraints
5. **Update `docs/design-notes/.design-notes.md`** — add a row to the Available Skills table for the new file.

> No change to `.github/copilot-instructions.md` is required — it loads `.design-notes.md` as the single
> discovery layer, so a new row in the Available Skills table is enough for the agent to find the note.

## Update — sync notes from this session

Review the current chat history and update the design notes in `docs/design-notes/` to reflect any
implementation changes, new components, or architectural decisions made in this session.

1. **Ensure the scaffold exists.** If [docs/design-notes/.design-notes.md](../../../docs/design-notes/.design-notes.md)
   is missing, run the **Init** workflow above first, then continue.
2. **Read the governance rules** from [docs/design-notes/.design-notes.md](../../../docs/design-notes/.design-notes.md) first.
3. **Analyze the chat** for:
   - New components, subsystems, or files created
   - Changed behavior or implementation patterns
   - Architectural decisions and their rationale
   - Trade-offs, constraints, and limitations that were explicitly discussed or imposed
4. **Update existing design notes** that cover the changed areas. For each update include current
   implementation details reflecting the new state, code examples, and the "why" behind decisions.
5. **Create a new design note** (via the **Create** workflow's structure) if a new subsystem was introduced
   that has no existing note. Follow the frontmatter format from the governance file.
6. **Update `docs/design-notes/.design-notes.md`** (the Available Skills table) if a new design note was created.
7. **Keep it accurate and specific** — only update sections actually affected by this session's changes.
   Do not rewrite unrelated content.

> Do not create a changelog or summary file. Update only the design notes in `docs/design-notes/`.
