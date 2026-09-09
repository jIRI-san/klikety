# Risks

| ID | Risk | Likelihood | Impact | Mitigation | Steps |
|----|------|------------|--------|------------|-------|
| RISK-1 | A topmost, non-activating overlay may not receive `WM_INPUTLANGCHANGE`. | Medium | Medium | Compare the active HKL on every non-debounced overlay key-down. Stop/escalate when: an active layout changes but neither the message nor the next key-down refreshes labels. | 2.1, 2.2, 2.4, 4.2 |
| RISK-2 | A redraw can replace a selected visual with an incorrect base state or fire navigation events. | Medium | Medium | Replay only each session's stored rendering command after the shared canvas is cleared. Stop/escalate when: a refresh moves the cursor, fires an action/cancel event, or loses a visible selection. | 3.1, 3.3, 4.2 |
