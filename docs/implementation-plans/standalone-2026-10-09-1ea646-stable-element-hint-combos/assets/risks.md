# Risks

| ID | Risk | Likelihood | Impact | Mitigation | Steps |
|----|------|------------|--------|------------|-------|
| RISK-1 | Providers rebuild controls or recycle runtime IDs | Medium | Medium | Exact identity/role/capability matching only; no durable semantic guarantee. Use current discovery and fresh validation, never old targets. A rebuilt control with indistinguishable metadata cannot be detected universally. | 1.1, 2.1 |
| RISK-2 | Reservations create holes; batching changes groups | High | Medium | One-generation reservations and normal paging; exact unchanged groups only. Changed groups/layouts may reset. Stop rather than introduce hierarchy replay, heuristics or scan-completion delays. | 1.2, 2.1 |
