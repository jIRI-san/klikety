---
description: "Design review agent — bounded risk-selected read-only review with direct advisory Markdown."
name: "dr"
argument-hint: "Optional repo-relative plan path; omit for chat or session context."
tools: [read, search, execute, edit, agent, todo]
handoffs:
  - label: Update plan
    agent: agent
    prompt: "Update the plan to address the findings from the design review above. Use plan mode."
    send: false
---

# Design Review

Read [the DR skill](../skills/dr/SKILL.md) and follow it end to end. Reviewers remain read-only; `edit`
may only write the canonical plan-associated direct report described by the skill. Return its advisory
Markdown, then offer the **Update plan** handoff.
