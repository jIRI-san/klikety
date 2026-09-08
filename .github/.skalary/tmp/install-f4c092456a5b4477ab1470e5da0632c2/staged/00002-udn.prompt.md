---
description: Update design notes based on what was implemented or changed in this chat session. Shortcut for the design-notes skill's Update workflow.
name: udn
agent: agent
---

# /udn

Update the design notes in `docs/design-notes/` to reflect implementation changes,
new components, or architectural decisions made in this chat session.

1. Read the design-notes skill: [../skills/design-notes/SKILL.md](../skills/design-notes/SKILL.md).
2. Run its **Update** workflow — read the governance rules, analyze the session, edit the affected notes (not unrelated content), and add a new note only when a new subsystem has none.
3. If the scaffold is missing, let the skill run its **Init** workflow first, then continue.
