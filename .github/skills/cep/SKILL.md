---
name: cep
description: 'Create Epic Plan — confirm an epic cut and scaffold independently executable child plans.'
argument-hint: 'Epic goal or existing epic reference'
user-invocable: true
disable-model-invocation: true
context: fork
---

# Create Epic Plan

Keep the epic an index of independently executable sibling plans. Discover prior work only through a
filtered index or explicit IDs; load at most three selected current Markdown artifacts through
`.github/skills/cep/scripts/Get-DirectPlanArtifactConsumerContext.ps1`. Preserve confinement, secret
screening, untrusted framing, accepted-only provenance, and current-intent/contract precedence.

Before drafting, follow the shared planning protocol installed at
`.github/skills/cep/assets/decision-protocol.md`. It defines host-equivalent complex choices and the
absolute/fuzzy language confirmation gate. Do not draft an unconfirmed absolute or an unobservable fuzzy
requirement.

After intent confirmation, decompose directly with zero delegated calls. Resolve aliases through
[`model-aliases.psd1`](./assets/model-aliases.psd1) before passing a host model. If a concrete unresolved
choice spans design and acceptance criteria, use one combined `primary-model-mid`/high design-and-requirements
call; `secondary-model-mid` is its replacement. Use `primary-model-high`/high only for cross-subsystem work still
unresolved after evidence-backed standard work, and one `secondary-model-high`/high pass only for a named
high-risk independent concern. No
automatic Judge or unchanged-scope rerun exists: deterministic evidence is the normal judge. Every call,
retry, and replacement counts toward a three-call ceiling; a fourth requires a new operator decision.
Delegated prompts target 400 words and must be narrowed before 800.
Use one direct risk-selected DR only when a concrete unresolved design risk remains; no scheduler or
fixed matrix. Apply the two-check progress rule to observable background calls and stop visibly after
one redirect and at most one replacement. Never cancel for elapsed agent time; deterministic command
timeouts remain.

Confirm the accepted epic cut before scaffolding. For every child, state its owned outcome, non-goals,
interface boundaries, and dependency rationale. Route each discovered edge case into preliminary intent
as a requirement candidate, risk, or explicit non-goal rather than leaving it implicit. Child decisions
and references remain explicit; do not duplicate detailed implementation instructions at epic level.
The later `/cip` confirmation owns each child's current intent, requirements, risks, and decisions.
