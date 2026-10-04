# Decisions

- One standalone corrective plan covers the four listed issues; composition stays advisory. Operator confirmed scope on 2026-10-04.
- Cancellation boundary is pre-dispatch; no claim to undo OS input already sent.
- Operator selected typed failure propagation and stopped/reported failed playback over keeping void APIs with logs. Await asynchronous drag outcomes to make this contract observable.
- Failed playback is reported through typed InputFailed and one error log, without new tray plumbing. Post-drag scheduling is completion-relative, while native phase delays and saved intervals stay unchanged.
- Playback teardown is nonblocking and operation-owned, with stale-generation guards. Indicator lifecycle tests use a pure composed seam instead of a WPF/STA harness.
- Plan repair is proved by a scoped executable check of exact identities, supported full states and affected local links, not file-existence proxies.
- Preserve persisted interval timing, macro versions, speed modifiers and floors; fix guidance, not data.
- Preserve both keyboard-layout records, historical evidence and references; no archive side effect. Canonical ownership is deferred to evidence, then operator if unresolved.
- No new dependencies, broad audits, or installed plugin changes. Use repository test seams and focused selectors.
- Filtered history search found no overlapping active product-fix plan; active 000021 is a repair target, not a host for unrelated product corrections. New plan a5c375 is separate.
- Operator approved bounded historical Decisions from 015/017. Reader returned refusals, so accepted history is empty; missing context remains explicit.
- Baseline at d424a32212c955bae231f30f9d047e82e5d9c259 on jiri-san-repository-cleanup-survey: focused MacroPlayerTests/MouseNormalizationTests ran 26 tests, all passed. Initial --no-restore invocation did not run tests because assets were absent; subsequent restore/test provided the actual baseline.
