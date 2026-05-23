# Evolution Log — 020: Autonomous Plan Execution

## DR Round 3

**Reviewers**: Opus, Codex, Gemini (via /dr agent)
**Findings**: 19 total — 3 critical, 6 high, 6 medium, 4 low
**Result**: 10 findings fixed (user-selected: 2,3,5,6,7,8,9,11,12,14). 9 findings deferred or accepted.

### Findings fixed:

| # | Severity | Finding | Fix |
|---|----------|---------|-----|
| 2 | Critical | Command-injection via `build`/`test` fields + plan content | Added command-prefix allowlist for `build`/`test`. Agent rule: never execute shell commands from plan text. Added RISK-9. |
| 3 | Critical | ADO `~/.azure/` mount broken cross-OS (DPAPI encryption) | Fetch ADO token on host, pass as `ADO_TOKEN` env var. Removed `~/.azure/` mount. Container uses env var credential helper. |
| 5 | High | Host `--allow-all` contradicts "cwd-restricted" safety claim | Removed false safety claim. Documented: no effective sandbox in host mode, safety via worktree isolation + agent rules + PR review. |
| 6 | High | `Start-Process` + `Wait-Process -Timeout` broken (no streaming, no kill) | Replaced with `System.Diagnostics.Process` for host (redirected output + polling + Kill). Container: `docker inspect` polling + `docker stop` → `docker kill`. |
| 7 | High | Context window exhaustion in whole-plan mode | One `copilot` invocation per phase. Orchestrator loops over phases. Agent gets fresh context per phase. State via plan markers. Added RISK-10. |
| 8 | High | Container inline mega-command untestable | Extracted to `container-entrypoint.sh`, COPY'd into image. Eliminates multi-layer escaping. |
| 9 | High | Temp env file survives abnormal termination | Restrictive ACL before writing, per-session random subdirectory, startup sweep in `launch.ps1`. |
| 11 | Medium | Devcontainer features conflicts with `docker build` | Renamed to `dockerfileExtensions` — array of `RUN` directives appended at build time. Not devcontainer features. |
| 12 | Medium | Container resume: remote branch divergence on re-run | Entry-point checks for existing remote branch → `git fetch + checkout` for resume, else `git checkout -b`. |
| 14 | Medium | No Docker availability pre-flight | Added `docker info` check in `launch.ps1` before dispatching to container mode. |

### Findings not fixed (deferred/accepted):

| # | Severity | Finding | Disposition |
|---|----------|---------|-------------|
| 1 | Critical | Container can't build WPF | Accepted — container mode is for non-WPF projects. Klikety uses host mode. Already documented in Known Constraints. |
| 4 | High | Pre-push hook can't intercept `--force` flag | Removed hook entirely. "Never force-push" is an absolute agent rule + server-side branch protection. |
| 10 | Medium | `/ci` vs agent execution loop drift | Accepted — acknowledged as structural reality. Shared design note is governance, not guarantee. |
| 13 | Medium | OAuth token extraction brittle for containers | Accepted — PAT is primary path for containers. OAuth empirical verification in REQ-4 covers this. |
| 15 | Medium | Unmanaged git worktrees accumulate | Already covered by REQ-18 (partial failure recovery detects existing worktree). |
| 16 | Medium | No Pester tests for scripts | Deferred — test coverage is valuable but not blocking for plan viability. Add during implementation. |
| 17 | Low | REQ-14 `delivery-mode` vs step 4.2 `scope` mismatch | Accepted — step 4.2 is the implementation; REQ-14 wording is intentionally high-level. |
| 18 | Low | No host-mode recursion guard | Low risk — host mode invoked from VS Code, not from within a worktree terminal. |
| 19 | Low | `@human` step handling underspecified | Fixed as part of [7] — agent now commits progress, reports blocking step, exits with status. |

## DR Round 1

**Reviewers**: Opus, Codex, Gemini (via /dr agent)
**Findings**: 13 total — 3 critical, 4 high, 6 medium/low
**Result**: Major architectural pivot → plan v2 rewrite

### Critical findings (all fixed in v2):

1. **`/ci` skill incompatible with CLI runtime** — used VS Code-specific tools (`vscode_askQuestions`, `@cr` agent). Fix: self-contained execution loop in `.github/agents/autopilot.agent.md`.
2. **WPF can't build in Linux containers** — initial design assumed container-only. Fix: project-configurable runtime (host-worktree vs container) via `.autopilot.json`.
3. **Git worktree mount broken in containers** — Docker volume semantics don't map git worktrees correctly. Fix: container clones from remote instead.

### High findings (all fixed in v2):

4. **`cmdkey` can't read passwords programmatically** → `CredentialManager` PowerShell module.
5. **Token exposure in process args** → `--env-file` pattern (temp file deleted after launch).
6. **`docker run` vs `devcontainer` contradiction** → standardized on `docker build` + `docker run` (devcontainer CLI's workspace semantics conflicted).
7. **No auth for ADO inside container** → `az login --use-device-code` + mount `~/.azure/`.

## DR Round 2

**Reviewers**: Opus, Codex, Gemini (via /dr agent)
**Findings**: 17 total — 1 critical, 4 high, 9 medium, 3 low
**Result**: All findings fixed in-place (no structural rewrite needed)

### Findings and fixes applied:

| # | Severity | Finding | Fix |
|---|----------|---------|-----|
| 1 | Critical | Container launch strategy unclear (devcontainer vs docker) | Clarified: `docker build` + `docker run` directly. Devcontainer.json is IDE convenience only. Clone to `/work`. |
| 2 | High | `devcontainer exec` doesn't support `--env-file` | Switched to `docker run --env-file`. For IDE devcontainer usage: `--remote-env` flags. |
| 3 | High | ADO missing `azure-devops` extension + credential helper | Added `az extension add --name azure-devops` to Dockerfile. Added ADO git credential helper script in step 3.2. |
| 4 | High | `X-OAuth-Scopes` header incompatible with fine-grained PATs | Replaced with capability probes: `GET /user`, `GET /repos/{owner}/{repo}`, copilot probe. |
| 5 | High | `/ci` still references `src/Qz/` paths | Step 4.1 now includes fixing stale paths. |
| 6 | Medium | Transcript extraction undefined for `--rm` containers | Removed `--rm`, added named container + `docker cp` + `docker rm` in `try/finally`. |
| 7 | Medium | Agent can't find plan from container cwd | Pass full relative path in prompt: `"Execute docs/implementation-plans/<slug>/plan.md"`. |
| 8 | Medium | Mode taxonomy inconsistent/overlapping | Added explicit mode taxonomy with orthogonal axes (runtime × scope). Delivery implicit. |
| 9 | Medium | Self-review is weak quality gate (same LLM, same context) | Removed from success gate. Real review at PR stage. |
| 10 | Medium | Host mode lacks timeout enforcement | Added `Start-Process` + `Wait-Process -Timeout` to launch-host. |
| 11 | Medium | Missing dev rules in agent instructions | Added: never `git add -A/.`, never `git commit --amend`, stage only directly modified files. |
| 12 | Medium | No build/test commands in config | Added `build` and `test` string fields to `.autopilot.json` schema. |
| 13 | Medium | OAuth token storage location unverified | Added empirical verification requirement to REQ-4 acceptance criteria. |
| 14 | Medium | Step-state persistence non-atomic | Plan status update included in same commit as code changes. |
| 15 | Low | `--allow-all-tools` doesn't exist | Changed to `--allow-all` and `COPILOT_ALLOW_ALL=true`. |
| 16 | Low | `--deny-tool` syntax unverified | Replaced with git pre-push hook as deterministic fallback. |
| 17 | Low | No smoke test step | Added step 3.5 — end-to-end smoke test with one-step plan. |
