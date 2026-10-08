# Risks

| ID | Risk | Likelihood | Impact | Mitigation | Steps |
|----|------|------------|--------|------------|-------|
| RISK-1 | Existing custom config already uses default help key | Medium | High | Versioned conflict-aware migration persists disabled help with warning; runtime ignores invalid explicit help bindings. Stop if migration requires stealing or replacing a binding. | 1.1, 3.1 |
| RISK-2 | Async hook delivery reads wrong modifiers or changes existing consumers | Medium | High | Separate hook flags including Win, snapshot at capture, compatible default event argument/fakes; leave ActionModifiers unchanged. Stop on any required change to system-key pass-through. | 1.1, 3.1 |
| RISK-3 | Activation gap, repeat or cleanup leaks inputs into preserved session | Medium | High | Visible-plus-active gate; key-up latches retain debounce removal; idempotent flag/latch cleanup before every hide/suspend; tests/live focus checks. Stop if stale help survives hide/resume. | 1.1, 2.1, 3.1 |
| RISK-4 | Legend diverges from actual effective command dispatch | Medium | Medium | Reuse binding resolution and runtime availability; custom bindings test matrix, no hardcoded default command list. | 1.2, 3.1 |
| RISK-5 | Recording/drag help has inaccurate meanings or global picker bypasses suspension | Medium | High | Read-only substate projections, contextual tests, preserve clock; reuse helper-picker hook-disable/suspend for global picker with visible navigation. Stop if help requires recorder mutation or stale global resume. | 2.1, 3.1 |
| RISK-6 | Layout/glyph assumptions hide bindings on small or non-US displays | Medium | Medium | Confirmed envelope with canonical geometry/layout-aware labels, auxiliary entries, text floor/scrolling, explicit layout-event and DPI/size refresh. Stop if fitting omits entries or makes text unreadable. | 2.2, 3.1 |
