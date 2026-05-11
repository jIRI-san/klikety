---
description: Release workflow — GitHub Actions CI/CD pipeline for building and publishing releases.
globs:
  - .github/workflows/release.yml
---

# CI/CD

## Release Workflow

Single workflow at `.github/workflows/release.yml`. No separate CI workflow — the release pipeline is the only automated quality gate.

## Triggers

- **Tag push** — `v*` pattern (e.g. `v1.2.0`). Version derived by stripping the `v` prefix.
- **Manual dispatch** — `workflow_dispatch` with required `version` string input. Restricted to `main` branch.

Both paths validate version against `^[0-9]+\.[0-9]+\.[0-9]+(-[a-zA-Z0-9.]+)?$` (strict SemVer with optional prerelease suffix).

## Two-Job Pipeline

**`build` job** (Windows, read-only permissions):
1. Checkout, setup .NET 10
2. Version validation
3. Branch guard (manual dispatch → main only)
4. Idempotency check — fail fast if release already exists
5. Format check — `dotnet format Klikety.slnx --verify-no-changes`
6. Unit tests — `dotnet test src/Klikety.Tests/Klikety.Tests.csproj -c Release`
7. Self-contained publish — `dotnet publish -r win-x64 --self-contained -c Release` (no trimming — WPF trimming is fragile)
8. Zip — `Klikety-v{version}-win-x64.zip`
9. Upload artifact via `actions/upload-artifact`

**`release` job** (Ubuntu, `contents: write` permission, needs `build`):
1. Download artifact
2. `gh release create` with `--target` pinned to `github.sha`, `--generate-notes`, zip attached

## Security

- **No inline expression injection** — `inputs.version`, `github.event_name`, `github.ref` passed through `env:` variables, never interpolated directly in shell scripts.
- **Top-level `permissions: {}`** — drops all default permissions. Each job grants only what it needs (`contents: read` for build, `contents: write` for release).
- **No third-party actions** — release creation uses `gh` CLI. Only first-party GitHub actions used (`actions/checkout`, `actions/setup-dotnet`, `actions/upload-artifact`, `actions/download-artifact`), pinned to major version tags.

## Concurrency & Idempotency

- `concurrency: { group: release, cancel-in-progress: false }` — serializes release runs, no cancellation.
- Pre-build check via `gh api` distinguishes "release exists" (HTTP 200 → fail) from "not found" (HTTP 404 → proceed) from API errors (other codes → fail closed).

## Version Policy

Strict SemVer: `MAJOR.MINOR.PATCH` with optional prerelease suffix (`-alpha.1`, `-rc.2`). No underscores, no build metadata (`+`). Version passed to MSBuild via `-p:Version=` — does not modify the csproj `<Version>` element.

## Asset Format

Single zip: `Klikety-v{version}-win-x64.zip` containing the self-contained publish output.

## Out of Scope

Code signing, MSI/installer, NuGet publishing, multi-RID builds.
