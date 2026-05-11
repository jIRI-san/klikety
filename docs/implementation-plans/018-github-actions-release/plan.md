# 018: GitHub Actions Release Workflow

## Decisions

- **Release-only workflow** — no separate CI workflow. Tests and format check run as part of the release pipeline (this is the only automated quality gate).
- **Triggers** — tag push (`v*`) and manual `workflow_dispatch` with required version input. Manual dispatch restricted to `main` branch.
- **Version derivation** — strip `v` prefix from git tag (or use manual input). Both paths validated against `^\d+\.\d+\.\d+(-[a-zA-Z0-9.]+)?$`. Pass to MSBuild via `-p:Version=`. Do not modify csproj `<Version>` element.
- **Version policy** — strict SemVer: `MAJOR.MINOR.PATCH` with optional prerelease suffix containing only alphanumerics and dots (e.g. `1.2.0`, `2.0.0-rc.1`). No underscores, no build metadata (`+`).
- **Self-contained publish** — `dotnet publish -r win-x64 --self-contained -c Release` without trimming (WPF trimming is fragile).
- **Asset format** — zip archive named `Klikety-v{version}-win-x64.zip`.
- **Release type** — published (not draft), with auto-generated release notes from commits.
- **No third-party actions for release creation** — use built-in `gh release create` CLI instead of community actions. Eliminates supply-chain risk.
- **Actions pinning** — first-party actions (`actions/checkout`, `actions/setup-dotnet`, `actions/upload-artifact`) pinned to major version tags (acceptable risk for GitHub-owned actions).
- **Two-job pipeline** — `build` job (read-only permissions) builds, tests, and uploads artifact; `release` job (write permissions) downloads artifact and creates GitHub Release. Minimizes token exposure.
- **Test scope** — unit tests only (`Klikety.Tests`). Smoke tests are in a separate project (`Klikety.SmokeTests`) and are not referenced by the test step — no `--filter` needed.
- **Runner** — `windows-latest` (WPF build requires Windows SDK).
- **NuGet caching** — enabled via `actions/setup-dotnet` built-in caching.
- **Concurrency** — serialized releases via `concurrency` block; no cancellation of in-progress runs.
- **Idempotency** — early check for existing release; fail fast if release already exists.
- **Out of scope** — code signing, MSI/installer, NuGet publishing, multi-RID builds.

## Requirements

| ID | Requirement | Acceptance Criteria | Phases/Steps |
|----|-------------|---------------------|--------------|
| REQ-1 | Workflow triggers on tag push matching `v*` | Pushing tag `v1.2.0` starts the workflow | 1.1 |
| REQ-2 | Workflow triggers on manual dispatch with version input | Running workflow_dispatch with version `1.2.0` starts the workflow and uses that version | 1.1 |
| REQ-3 | Version derived from tag or manual input, validated | Both paths produce valid SemVer; malformed tag `vfoo` or input `abc` fails early with clear message | 1.1 |
| REQ-4 | Manual dispatch restricted to main branch | Dispatch from non-main branch fails early with clear message | 1.1 |
| REQ-5 | Unit tests pass before release | `dotnet test` runs `Klikety.Tests`; workflow fails if tests fail | 1.1 |
| REQ-6 | Format check passes before release | `dotnet format --verify-no-changes` runs; workflow fails if formatting violations found | 1.1 |
| REQ-7 | Self-contained win-x64 publish without trimming | `dotnet publish -r win-x64 --self-contained -c Release` produces output | 1.1 |
| REQ-8 | Publish output zipped as `Klikety-v{version}-win-x64.zip` | Zip artifact exists with correct name | 1.1 |
| REQ-9 | GitHub Release created with the zip attached | Release exists at tag `v{version}` with the zip as an asset and auto-generated notes | 1.1 |
| REQ-10 | Existing release blocks re-release | If release `v{version}` already exists, workflow fails early before building | 1.1 |
| REQ-11 | Build artifact persisted independently of release step | Artifact uploaded via `actions/upload-artifact` before release creation | 1.1 |
| REQ-12 | CI/CD design note created | `docs/design-notes/ci-cd.design.md` documents the release workflow | 1.2 |

## Risks

| ID | Risk | Likelihood | Impact | Mitigation | Steps |
|----|------|------------|--------|------------|-------|
| RISK-1 | .NET 10 SDK not available on `windows-latest` runner image | Medium | High | Use `actions/setup-dotnet@v4` with `dotnet-version: '10.0.x'` to install the SDK explicitly. If desktop targeting packs missing, pin to a specific SDK version known to include them | 1.1 |
| RISK-2 | `GITHUB_TOKEN` lacks permissions to create releases | Low | Medium | Set explicit `permissions: contents: write` on the release job only | 1.1 |
| RISK-3 | Parallel release runs race on tag/release creation | Low | Medium | `concurrency: { group: release, cancel-in-progress: false }` serializes runs | 1.1 |

## Phase 1: Release Workflow
<!-- worktree: feature/018-github-actions-release -->

- [x] 1.1 Create `.github/workflows/release.yml` (REQ-1, REQ-2, REQ-3, REQ-4, REQ-5, REQ-6, REQ-7, REQ-8, REQ-9, REQ-10, REQ-11, RISK-1, RISK-2, RISK-3) `M`

  Workflow file `.github/workflows/release.yml` with:

  **Triggers:**
  ```yaml
  on:
    push:
      tags: ['v*']
    workflow_dispatch:
      inputs:
        version:
          description: 'Version number (e.g. 1.2.0)'
          required: true
          type: string
  ```

  **Top-level concurrency:**
  ```yaml
  concurrency:
    group: release
    cancel-in-progress: false
  ```

  **Job 1: `build`** on `windows-latest`, default permissions (read-only):

  1. **Checkout** — `actions/checkout@v4`
  2. **Setup .NET** — `actions/setup-dotnet@v4` with `dotnet-version: '10.0.x'` and `cache: true`
  3. **Determine version** — `shell: bash` step:
     - Tag trigger: `VERSION="${GITHUB_REF_NAME#v}"` (strip `v` prefix)
     - Manual trigger: `VERSION="${{ inputs.version }}"`
     - Validate both: `[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[a-zA-Z0-9.]+)?$ ]]` or fail with `"Invalid version '$VERSION'. Expected N.N.N or N.N.N-suffix."`
     - Write to `$GITHUB_ENV` and `$GITHUB_OUTPUT`
  4. **Guard manual dispatch to main** — `shell: bash`, only runs on `workflow_dispatch`:
     - `if [[ "${{ github.ref }}" != "refs/heads/main" ]]; then echo "::error::Manual dispatch must run from main branch"; exit 1; fi`
  5. **Check existing release** — `shell: bash`:
     - `gh release view "v$VERSION" >/dev/null 2>&1 && echo "::error::Release v$VERSION already exists" && exit 1 || true`
     - Uses `GITHUB_TOKEN` (read-only is sufficient for `gh release view`)
  6. **Format check** — `dotnet format Klikety.slnx --verify-no-changes`
  7. **Test** — `dotnet test src/Klikety.Tests/Klikety.Tests.csproj -c Release`
     - No `--filter` needed — smoke tests are in a separate project not referenced here
     - No `--no-build` — let `dotnet test` build what it needs (avoids RID mismatch issues)
  8. **Publish** — `dotnet publish src/Klikety/Klikety.csproj -r win-x64 --self-contained -c Release -p:Version=${{ env.VERSION }} -o publish/`
     - Standalone `dotnet build` step omitted — publish handles the build. Test step above builds RID-agnostic which can't be reused for RID-specific publish anyway.
  9. **Zip** — `shell: pwsh`, `Compress-Archive -Path publish/* -DestinationPath Klikety-v${{ env.VERSION }}-win-x64.zip`
  10. **Upload artifact** — `actions/upload-artifact@v4` with name `release-zip` and path `Klikety-v${{ env.VERSION }}-win-x64.zip`

  **Job 2: `release`** on `ubuntu-latest`, needs `build`, permissions `contents: write`:

  1. **Download artifact** — `actions/download-artifact@v4` with name `release-zip`
  2. **Create Release** — `shell: bash`:
     ```bash
     gh release create "v$VERSION" \
       --title "Klikety v$VERSION" \
       --generate-notes \
       Klikety-v${VERSION}-win-x64.zip
     ```
     - `VERSION` passed from build job outputs
     - For manual dispatch (no tag): `gh release create` creates the tag automatically
     - Uses `GITHUB_TOKEN` with `contents: write`

- [x] 1.2 Create `docs/design-notes/ci-cd.design.md` (REQ-12) `S`

  Document the release workflow: triggers, version policy, two-job pipeline, artifact format, concurrency, and idempotency checks. Add entry to `.design-notes.md` Available Skills table.

- [x] 1.3 Test workflow with a dry-run tag push (REQ-1, REQ-3, REQ-5, REQ-6, REQ-7, REQ-8, REQ-9, REQ-11) [after: 1.1, 1.2] @human `S`
  <details><summary>Details</summary>

  **Steps:**
  1. Push the workflow file and design note to `main`.
  2. Create and push a test tag: `git tag v1.1.1 && git push origin v1.1.1`
  3. Verify in GitHub Actions: workflow runs, format check passes, tests pass, zip artifact uploaded, GitHub Release published with zip attached.
  4. If release is a test: delete the release and tag from GitHub.

  **Rollback:** Delete the GitHub Release via UI, then `git push origin --delete v1.1.1 && git tag -d v1.1.1`.

  </details>

- [x] 1.4 Test manual dispatch (REQ-2, REQ-3, REQ-4, REQ-10) [after: 1.3] @human `S`
  <details><summary>Details</summary>

  **Steps:**
  1. Go to GitHub → Actions → Release → Run workflow.
  2. Enter version `1.1.2` and run.
  3. Verify: workflow runs, creates release `v1.1.2` with correct zip name.
  4. Test validation: run with malformed version (e.g. `abc`), verify workflow fails with clear error.
  5. Test idempotency: re-run with version `1.1.2`, verify workflow fails early ("Release already exists").
  6. Clean up test releases/tags.

  **Rollback:** Delete test releases and tags from GitHub.

  </details>
