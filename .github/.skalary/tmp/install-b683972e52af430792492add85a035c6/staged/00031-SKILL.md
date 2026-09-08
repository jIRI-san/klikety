---
name: cip
description: 'Create Implementation Plan — confirm criteria and draft an implementation-ready vertical plan.'
argument-hint: 'Plan name or existing plan reference'
user-invocable: true
disable-model-invocation: true
context: fork
---

# Create Implementation Plan

Resolve/scaffold the plan with existing deterministic scripts. Discover prior work only from a filtered
index or explicit canonical IDs, then load at most three selected current Markdown artifacts through
`.github/skills/cip/scripts/Get-DirectPlanArtifactConsumerContext.ps1`. Keep confinement, secret
screening, accepted-only provenance, untrusted framing, and current intent/contracts above history.

Before drafting, follow
[`./assets/decision-protocol.md`](./assets/decision-protocol.md). It defines host-equivalent complex
choices and the absolute/fuzzy language confirmation gate. Do not draft an unconfirmed absolute or an
unobservable fuzzy requirement.

Confirm current intent first and draft directly with zero delegated calls. Resolve aliases through
[`model-aliases.psd1`](./assets/model-aliases.psd1) before passing a host model. If a concrete unresolved
choice spans design and acceptance criteria, use one combined `primary-model-mid`/high
design-and-requirements call; `secondary-model-mid` is its replacement. Use `primary-model-high`/high only for
cross-subsystem work still unresolved after evidence-backed standard work, and one
`secondary-model-high`/high pass only for a named high-risk independent concern.
No automatic Judge or unchanged-scope rerun exists: deterministic evidence is the normal judge. Every
call, retry, and replacement counts toward a three-call ceiling; a fourth requires a new operator
decision. Delegated prompts target 400 words and must be narrowed before 800. Use a direct risk-selected
DR only for concrete unresolved design risk; no scheduler or fixed
matrix. For observable background calls, two no-progress checks permit one same-agent redirect and at
most one replacement. Elapsed agent time never cancels work; declared deterministic command timeouts
remain.

Before final drafting, confirm the current intent, requirements, risks, and decisions together. Persist
the existing planning-confirmed marker only after that confirmation. Typed evidence remains exactly
`test:`, `file:`, and `review:`. Draft MVP-first vertical steps and retain script-owned validation/stage
mutations. Every discovered edge case must resolve to a requirement, risk, or explicit non-goal. Give
each nontrivial AI step a compact details block naming its outcome, likely touchpoints, constraints, and
verification. For uncertain or high-risk work, name the concrete condition that stops execution and
escalates rather than asking the implementation model to infer it. Any later criteria correction returns
to the affected confirmation rather than weakening it.
