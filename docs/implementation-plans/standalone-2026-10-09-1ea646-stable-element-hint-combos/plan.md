# 1ea646: Best-effort stable element hint combos
<!-- plan-id: 1ea646 -->
<!-- cip-stage: drafted -->
<!-- planning-confirmed: sha256:4ca443e20a7f7f3275aded4003625fb64f1c9dab73c1dc855a399c747986b74e -->
<!-- execution-mode: manual -->
<!-- scope: step -->
<!-- evidence: required -->
<!-- phase-budget-points: 6 -->
<!-- expected-packages: none -->

## Assets

- Intent - [assets/intent.md](assets/intent.md)
- Domain - [assets/domain.md](assets/domain.md)
- Proposed design - [assets/design.md](assets/design.md)
- Requirements - [assets/requirements.md](assets/requirements.md)
- Risks - [assets/risks.md](assets/risks.md)
- Decisions - [assets/decisions.md](assets/decisions.md)
- References - [assets/references.md](assets/references.md)

## Phase 1: Remember and reuse assignments

- [ ] 1.1 Add a bounded parent-side assignment registry (REQ-1, REQ-2, RISK-1) `M`
  <details><summary>Implementation contract</summary>

  **Outcome:** Recently used windows retain one metadata-only assignment snapshot
  across session close and worker retirement.

  **Likely touchpoints:** A small Navigation helper, `ModeSessionFactory`,
  `ElementTargetContext`, focused registry tests.

  **Constraints:** Reuse `cacheWindowCount`; 0 disables memory, LRU limits windows.
  Match exact process/runtime identity plus role/capabilities. No provider calls,
  persistence, semantic matching, new settings or protocol changes.

  **Verify:** `test:ElementHintAssignmentTests`

  </details>
- [ ] 1.2 Use explicit slots for progressive labels and key lookup (REQ-1, REQ-3, REQ-4, RISK-2) [after: 1.1] `L`
  <details><summary>Implementation contract</summary>

  **Outcome:** Reopening an unchanged level restores remembered combos despite
  different discovery order/batches. New entries cannot take reserved combos.

  **Likely touchpoints:** `ElementHintsSession.Labels`, `OnKey`, level/page state,
  `ElementHintHierarchy`, progressive session tests.

  **Constraints:** Display and input use the same sparse slot map. Preserve the
  remembered key scheme and current prefix/selection. Exact unchanged groups
  may reuse assignments; changed group structure uses normal allocation.
  Publish only controls in the current scan, using current tokens and validation.

  **Verify:** `test:ProgressiveElementHintsSessionTests`

  **Stop/escalate when:** Reuse requires replaying old provider trees, delaying
  first hints until scan completion, or replacing existing capacity grouping.
  Do not expand scope to solve those cases.

  </details>

## Phase 2: Verify and document best-effort boundaries

- [ ] 2.1 Add regression coverage and update directly related docs (REQ-1, REQ-2, REQ-3, REQ-4, REQ-5, RISK-1, RISK-2) [after: 1.2] `M`
  <details><summary>Implementation contract</summary>

  **Outcome:** Exact reuse, safe misses, memory bounds and current-target routing
  are covered; user docs explain when assignments reset.

  **Likely touchpoints:** Assignment/session/factory tests, element-hints and
  testing design notes, `docs/navigation.md`, `docs/settings.md`.

  **Constraints:** Include reversed/differently split batches, additions/removals,
  partial/cancelled scans, unchanged/changed groups, holes and overflow paging,
  off-page/visited-level retention and a late earlier page filling while typing,
  invalid window identity, LRU/disabled memory, worker restart, key/viewport
  changes, and late retired frames. Keep existing helper safety tests unchanged.

  **Verify:** `test:ElementHintAssignmentTests`,
  `test:ProgressiveElementHintsSessionTests`,
  `test:ElementHintsStateMachineTests`, `test:UiaWorkerSupervisorTests`,
  `file:docs/design-notes/element-hints.design.md#contains:best-effort`,
  `file:docs/navigation.md#contains:best-effort`.
  Run the smallest affected suites, solution build, formatting and diff checks.

  </details>
- [ ] 2.2 Check a few repeated reopenings on an unchanged app window (REQ-5) @human [after: 2.1] `S`
  <details><summary>Details</summary>

  **Steps:**
  1. Note several static chrome/sidebar combos, close hints and reopen repeatedly.
  2. Repeat after over 30 seconds idle so the worker has retired.
  3. Change dynamic content and reopen; inspect unchanged root controls separately
     from rebuilt controls or changed groups.

  **Verify:** Surviving controls in unchanged levels keep their combos; rebuilt
  controls/changed groups may reset. Early hints remain usable. This is
  representative manual evidence, not a guarantee for every provider.

  **Rollback:** Revert the feature commit; nothing is persisted or migrated.

  </details>
