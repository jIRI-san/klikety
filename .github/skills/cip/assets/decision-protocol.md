# Decision-ready questions and policy language

Use this protocol while confirming intent, requirements, risks, decisions, and relevant active policy.
It is an authoring conversation, not a runtime policy checker.

## Questions

- If a predefined choice is complex because its consequences, terminology, relationships, or sequencing
  are not obvious, present current context, a concrete example, expected benefits, each option's pros and
  cons, a recommendation/default, `effort: <1-10>`, and `complexity: <1-10>`. Add a Mermaid diagram only
  when relationships or sequencing affect the decision.
- Present the same ordered labels and context in both hosts. In VS Code, pass the list to
  `vscode_askQuestions`; in Copilot CLI, render it as a numbered list and accept the number or exact label.
- If the answer is free-form, ask one focused question at a time. If a yes/no choice is trivial and its
  consequence is already explicit, ask it directly without expanding it into a decision brief.

## Language confirmation

Before drafting, inspect operator requirements and the relevant active instructions, skills, agents, and
architecture/design notes for behavior-asserting uses of `always`, `never`, `must`, `shall`, `required`,
`only`, `cannot`, `do not`, `refuse`, and `prohibit`.

- If the operator already confirmed that a rule has no condition or exception, retain it as an invariant
  and record the reason.
- Otherwise present a candidate as `Condition: ...`, `Behavior: ...`, `Exception: ...`, then ask the
  operator to confirm or revise it before drafting.

Also inspect requirements for `detailed`, `thorough`, `robust`, `appropriate`, `comprehensive`, `fast`,
and `secure`. If one states a requirement without an observable meaning, ask for a criterion, threshold,
or example, one focused question at a time, before drafting it. For example, turn “always deploy after a
green build” into a candidate covering which branch, which checks, and the manual-release exception; ask
what failures and retry count make “robust retries” true.

Ignore code keywords, tool/schema fields, quotations, examples being analyzed, format grammar, and
ordinary descriptive prose whose meaning is already observable.
