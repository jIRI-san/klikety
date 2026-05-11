# Evolution Log — 018: GitHub Actions Release Workflow

## Round 1

**Issues found (15):**

1. **[Critical]** Third-party action (`softprops/action-gh-release`) pinned to mutable tag — supply-chain risk
2. **[High]** Manual dispatch can release from arbitrary branches
3. **[High]** Tag-derived versions not validated (only manual input was)
4. **[High]** Release reruns not idempotent — no check for existing release
5. **[High]** No NuGet caching
6. **[Medium]** Write-scoped token exposed to build and test steps
7. **[Medium]** No concurrency control for parallel release runs
8. **[Medium]** Shell semantics not specified for version-extraction step
9. **[Medium]** No format verification despite being the only CI gate
10. **[Medium]** Version regex inconsistent (allows underscores via `\w`)
11. **[Medium]** Redundant build step (double compile)
12. **[Medium]** RISK-2 workload mitigation technically inaccurate
13. **[Low]** Smoke test `--filter` is a no-op (different project)
14. **[Low]** Missing CI/CD design note
15. **[Low]** No intermediate artifact upload before release step

**Issues fixed (all 15):**

1. Replaced `softprops/action-gh-release` with built-in `gh release create` CLI
2. Added branch guard step for manual dispatch (must be `main`)
3. Both tag-derived and manual-input versions now validated with same regex
4. Added early check: `gh release view` fails fast if release exists
5. Added `cache: true` to `actions/setup-dotnet`
6. Split into two jobs: `build` (read-only) and `release` (write permissions)
7. Added `concurrency: { group: release, cancel-in-progress: false }`
8. Specified `shell: bash` with actual commands for version extraction
9. Added `dotnet format Klikety.slnx --verify-no-changes` step
10. Tightened regex to `^[0-9]+\.[0-9]+\.[0-9]+(-[a-zA-Z0-9.]+)?$`, documented version policy
11. Removed standalone build step; publish handles its own build, test builds independently
12. Replaced RISK-2 with corrected mitigation (SDK version pinning, removed `dotnet workload install`)
13. Removed `--filter` from test step; smoke tests are in separate project
14. Added step 1.2 to create `ci-cd.design.md`
15. Added `actions/upload-artifact` step before release job

**Issues deferred:** None
