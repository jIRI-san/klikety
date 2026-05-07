# Evolution Log

## Round 1

**Findings (13 total):**

| # | Severity | Finding | Action |
|---|----------|---------|--------|
| 1 | High | Key-index mapping unspecified; center cell not excluded from scan | Fixed — methods reuse `GetCrossLabel`; CenterCol/CenterRow excluded |
| 2 | High | Opacity rules in step 2.3 contradicted REQ-10 | Fixed — aligned all render states with inline opacity patterns |
| 3 | High | Screen-edge checks before overlap resolution; no post-resolution clamping | Fixed — checks moved to after resolution; explicit clamping added |
| 4 | Medium | `Func<int, double>` delegate allocations per frame | Fixed — replaced with `highlightCol`/`highlightRow` int + two opacity doubles |
| 5 | Medium | No rendering primitive for external labels | Fixed — added `UseExternalLabel` method to step 1.3 |
| 6 | Medium | `UseLine` has no opacity parameter | Fixed — added `double opacity = 1.0` parameter |
| 7 | Medium | Tests cover only thresholds, not rendering/opacity/placement | Fixed — added opacity resolution and center-cell exclusion tests to step 3.1 |
| 8 | Medium | Line pool cleanup: `_linePool.Clear()` not in staleness branch | Fixed — explicit `_linePool.Clear()` in staleness branch; `Visibility.Collapsed` pattern in EndRender |
| 9 | Medium | "Transition boundary" vs "bounding box" ambiguous | Fixed — clarified: bounding box of cells strictly below threshold, excluding first readable cell |
| 10 | Medium | ResolveOverlaps coupling between renderers | Rejected — `internal static` is the simplest sharing for one method; no new class warranted |
| 11 | Medium | FormattedText measurement overhead per frame | Rejected — single-char measurements are cheap; premature optimization |
| 12 | Low | Threshold helper visibility mismatch between Phase 1.2 and 3.1 | Fixed — defined as `internal static` with parameter in Phase 1.2 from the start |
| 13 | Low | Alleged duplicate step 3.1 | Dismissed — false positive; no duplicate exists |
