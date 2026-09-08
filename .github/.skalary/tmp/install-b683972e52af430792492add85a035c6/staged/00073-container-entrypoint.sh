#!/bin/bash
# container-entrypoint.sh — Autonomous plan execution inside Docker container
# Invoked by: docker run ... /usr/local/bin/container-entrypoint.sh <plan-slug> <mode>
#
# Environment variables expected:
#   GH_TOKEN / COPILOT_GITHUB_TOKEN — GitHub auth
#   ADO_TOKEN (optional) — Azure DevOps access token
#   ADO_ORG, ADO_PROJECT (optional) — ADO config
#   COPILOT_ALLOW_ALL=true
#   COPILOT_MODEL — model override
#   COPILOT_CONTEXT — context tier (default or long_context)
#   REPO_REMOTE — git remote URL to clone

set -euo pipefail

stage_recoverable_work() {
    local repo_path="$1"

    {
        git -C "${repo_path}" diff --name-only -z
        git -C "${repo_path}" ls-files --others --exclude-standard -z
    } | git -C "${repo_path}" add --pathspec-from-file=- --pathspec-file-nul ||
        return 70
}

phase_needs_execution() {
    local plan_path="$1"
    local phase_number="$2"
    local repo_root="${3:-.}"
    local state_script="${AUTOPILOT_PHASE_STATE_SCRIPT:-/usr/local/lib/autopilot/Get-PhaseExecutionState.ps1}"
    local state_output
    local invocation_state

    state_output="$(pwsh -NoProfile -File "${state_script}" \
        -PlanPath "${plan_path}" -Phase "${phase_number}" \
        -RepoRoot "${repo_root}" 2>&1)"
    invocation_state=$?
    if [ "${invocation_state}" -ne 0 ]; then
        echo "ERROR: Phase ${phase_number} close state is invalid." >&2
        printf '%s\n' "${state_output}" >&2
        return 2
    fi
    case "${state_output}" in
        execution-required|close-pending) return 0 ;;
        closed) return 1 ;;
        *)
            echo "ERROR: Phase ${phase_number} state checker returned an invalid result." >&2
            printf '%s\n' "${state_output}" >&2
            return 2
            ;;
    esac
}

verify_expected_start_commit() {
    local repo_path="$1"
    local expected="${2:-}"
    local object="${3:-FETCH_HEAD}"
    local branch_label="${4:-selected start branch}"
    local actual

    [ -z "${expected}" ] && return 0
    if [[ ! "${expected}" =~ ^([0-9a-f]{40}|[0-9a-f]{64})$ ]]; then
        echo "ERROR: EXPECTED_START_COMMIT must be a full lowercase Git commit id." >&2
        return 2
    fi
    if ! actual="$(git -C "${repo_path}" rev-parse --verify \
        "${object}^{commit}" 2>/dev/null)"; then
        echo "ERROR: Unable to resolve fetched start branch '${branch_label}'." >&2
        return 1
    fi
    if [ "${actual,,}" != "${expected}" ]; then
        echo "ERROR: Fetched start branch '${branch_label}' resolved to '${actual,,}', expected '${expected}'." >&2
        return 1
    fi
    printf '%s\n' "${actual,,}"
}

remote_branch_exists() {
    local repo_path="$1"
    local branch="$2"
    local output

    if ! output="$(
        git -C "${repo_path}" ls-remote --refs origin \
            "refs/heads/${branch}" 2>/dev/null
    )"; then
        echo "ERROR: Unable to inspect work branch '${branch}' on origin." >&2
        return 2
    fi
    [ -n "${output}" ]
}

checkout_epic_work_branch() {
    local repo_path="$1"
    local start_branch="$2"
    local expected="$3"
    local work_branch="$4"
    local trusted_retry="${5:-false}"
    local verified_start
    local retry_ref="refs/autopilot/internal-retry"

    if [ "${trusted_retry}" = "true" ]; then
        remote_branch_exists "${repo_path}" "${work_branch}" || {
            local probe_status=$?
            if [ "${probe_status}" -eq 1 ]; then
                echo "ERROR: Trusted internal retry cannot find work branch '${work_branch}' on origin." >&2
            fi
            return "${probe_status}"
        }
        git -C "${repo_path}" fetch --no-tags origin \
            "refs/heads/${work_branch}:${retry_ref}"
        git -C "${repo_path}" checkout -b "${work_branch}" "${retry_ref}"
        return 0
    fi

    if ! git -C "${repo_path}" fetch --no-tags origin \
        "refs/heads/${start_branch}"; then
        echo "ERROR: Selected start branch '${start_branch}' is not available on origin." >&2
        return 1
    fi
    verified_start="$(
        verify_expected_start_commit "${repo_path}" "${expected}" FETCH_HEAD "${start_branch}"
    )" || return $?

    remote_branch_exists "${repo_path}" "${work_branch}" && {
        echo "ERROR: Work branch '${work_branch}' already exists on origin; a fresh epic launch will not resume it." >&2
        return 1
    }
    local probe_status=$?
    if [ "${probe_status}" -ne 1 ]; then
        return "${probe_status}"
    fi

    git -C "${repo_path}" checkout -b "${work_branch}" "${verified_start}"
}

autopilot_expected_close_target_branch() {
    local expected_pr_base="${1:-}"

    if [ -z "${expected_pr_base}" ]; then
        printf '\n'
        return 0
    fi
    printf '%s\n' "${expected_pr_base}"
}

autopilot_entrypoint_target_close_state() {
    local plan_path="$1"
    local target="$2"
    local final_phase_number="$3"
    local work_branch="$4"
    local expected_close_target_branch

    expected_close_target_branch="$(
        autopilot_expected_close_target_branch \
            "${AUTOPILOT_EXPECTED_PR_BASE:-}"
    )" || return $?
    autopilot_target_close_state \
        "${plan_path}" "${target}" "${final_phase_number}" \
        "${work_branch}" "${expected_close_target_branch}"
}

# Expose the pure phase-progress and recovery probes to focused tests.
if [[ "${BASH_SOURCE[0]}" != "$0" ]]; then
    return 0
fi

PLAN_SLUG="${1:?Usage: container-entrypoint.sh <plan-slug> <mode>}"
MODE="${2:?Usage: container-entrypoint.sh <plan-slug> <mode>}"
BRANCH="${REPO_BRANCH:-feature/${PLAN_SLUG}}"
REPO_REMOTE="${REPO_REMOTE:?REPO_REMOTE env var required}"
if ! git check-ref-format --branch "${BRANCH}" > /dev/null 2>&1; then
    echo "ERROR: REPO_BRANCH is not a valid branch name." >&2
    exit 2
fi
if [ -n "${EXPECTED_START_COMMIT:-}" ] &&
    [[ ! "${EXPECTED_START_COMMIT}" =~ ^([0-9a-f]{40}|[0-9a-f]{64})$ ]]; then
    echo "ERROR: EXPECTED_START_COMMIT must be a full lowercase Git commit id." >&2
    exit 2
fi
if [ -n "${AUTOPILOT_EXPECTED_PR_BASE:-}" ] &&
    ! git check-ref-format --branch "${AUTOPILOT_EXPECTED_PR_BASE}" > /dev/null 2>&1; then
    echo "ERROR: AUTOPILOT_EXPECTED_PR_BASE is not a valid branch name." >&2
    exit 2
fi
autopilot_expected_close_target_branch \
    "${AUTOPILOT_EXPECTED_PR_BASE:-}" >/dev/null || exit $?
TRUSTED_INTERNAL_RETRY="${AUTOPILOT_TRUSTED_INTERNAL_RETRY:-false}"
if [ "${TRUSTED_INTERNAL_RETRY}" != "false" ] &&
    [ "${TRUSTED_INTERNAL_RETRY}" != "true" ]; then
    echo "ERROR: AUTOPILOT_TRUSTED_INTERNAL_RETRY must be 'true' or unset." >&2
    exit 2
fi
if [ "${TRUSTED_INTERNAL_RETRY}" = "true" ] &&
    [ -z "${EXPECTED_START_COMMIT:-}" ]; then
    echo "ERROR: A trusted internal retry requires EXPECTED_START_COMMIT." >&2
    exit 2
fi

. /usr/local/lib/autopilot/plan-dispatch.sh

echo "=== Autopilot Container Entry-Point ==="
echo "Plan: ${PLAN_SLUG}"
echo "Mode: ${MODE}"
echo "Branch: ${BRANCH}"
echo "Model: ${COPILOT_MODEL:-<CLI default>}"
echo "Copilot CLI: $(copilot --version 2>/dev/null || echo 'unknown')"

# --- Git credential setup ---
gh auth setup-git

# ADO credential helper (if ADO_TOKEN is set)
if [ -n "${ADO_TOKEN:-}" ]; then
    echo "Configuring ADO credential helper..."
    if [ -n "${ADO_ORG:-}" ]; then
        az devops configure --defaults "organization=${ADO_ORG}" "project=${ADO_PROJECT:-}"
    fi
    git config --global credential.helper '!f() { echo "username=x-token"; echo "password=${ADO_TOKEN}"; }; f'
fi

# --- Clone and branch ---
echo "Preparing configured origin..."
if [ -n "${EXPECTED_START_COMMIT:-}" ]; then
    git init -q /work
    git -C /work remote add origin "${REPO_REMOTE}"
else
    git clone --no-checkout "${REPO_REMOTE}" /work
fi
cd /work

# Determine target branch
WORK_BRANCH="feature/${PLAN_SLUG}"

if [ -n "${EXPECTED_START_COMMIT:-}" ]; then
    checkout_epic_work_branch /work "${BRANCH}" "${EXPECTED_START_COMMIT}" \
        "${WORK_BRANCH}" "${TRUSTED_INTERNAL_RETRY}"
elif git ls-remote --exit-code origin "refs/heads/${WORK_BRANCH}" > /dev/null 2>&1; then
    echo "Work branch ${WORK_BRANCH} exists on remote — resuming..."
    git fetch origin "${WORK_BRANCH}"
    git checkout "${WORK_BRANCH}"
elif [ "${BRANCH}" != "${WORK_BRANCH}" ] && git ls-remote --exit-code origin "refs/heads/${BRANCH}" > /dev/null 2>&1; then
    echo "Starting from branch ${BRANCH}..."
    git fetch origin "${BRANCH}"
    git checkout "${BRANCH}"
    echo "Creating work branch ${WORK_BRANCH} from ${BRANCH}..."
    git checkout -b "${WORK_BRANCH}"
else
    echo "ERROR: Selected start branch '${BRANCH}' is not available on origin." >&2
    exit 1
fi

# --- Configure git identity ---
git config user.name "${GIT_USER_NAME:-autopilot}"
git config user.email "${GIT_USER_EMAIL:-autopilot@noreply}"

# --- Work preservation on termination ---
# The host enforces the whole-run cap with `docker stop --time 30`, which sends
# SIGTERM to this script. Without a handler the run dies holding every commit made
# since the last push, and those commits vanish with the container. Bash also
# defers traps while a foreground child runs, so each `copilot` call below is
# backgrounded and waited on — otherwise this handler could never fire.
COPILOT_PID=""
TERMINATING=0

preserve_work() {
    cd /work 2>/dev/null || return 70
    git rev-parse --git-dir >/dev/null 2>&1 || return 70
    if [ -n "$(git status --porcelain)" ]; then
        echo "Committing in-flight work before exit..."
        if ! stage_recoverable_work /work ||
            { ! git diff --cached --quiet &&
                ! git commit -q -m "chore(autopilot): preserve in-flight work on termination [plan-${PLAN_SLUG}]"; }; then
            echo "ERROR: unable to commit in-flight work; retaining the container workspace."
            touch /tmp/autopilot-preservation-failed
            return 1
        fi
    fi
    echo "Pushing ${WORK_BRANCH}..."
    if ! git push origin "${WORK_BRANCH}"; then
        echo "ERROR: preservation push failed; retaining the container workspace."
        touch /tmp/autopilot-preservation-failed
        return 1
    fi
}

on_terminate() {
    [ "${TERMINATING}" -eq 1 ] && return
    TERMINATING=1
    echo ""
    echo "=== Termination signal received — preserving work ==="
    if [ -n "${COPILOT_PID}" ] && kill -0 "${COPILOT_PID}" 2>/dev/null; then
        kill -TERM "${COPILOT_PID}" 2>/dev/null || true
        for _ in 1 2 3 4 5; do
            kill -0 "${COPILOT_PID}" 2>/dev/null || break
            sleep 1
        done
        kill -KILL "${COPILOT_PID}" 2>/dev/null || true
    fi
    if ! preserve_work; then
        echo "ERROR: Failed to preserve in-flight work; container recovery is required."
        exit 70
    fi
    exit 143
}

trap on_terminate TERM INT

# --- Offline package feed setup ---
# When the host bundled a package feed (mounted read-only at /feed), copy it to a
# writable cache and emit OUT-OF-TREE restore config so all restores resolve from
# the cache with no network access and without writing into /work. The NuGet config
# is passed via NUGET_CONFIG, which REPLACES machine/user config discovery (no merge);
# npm is steered via an out-of-tree userconfig + cache. The committed lockfiles keep
# the restore locked/deterministic.
if [ "${AUTOPILOT_OFFLINE:-}" = "true" ]; then
    FEED_SRC="${AUTOPILOT_FEED:-/feed}"
    CACHE_ROOT="${HOME}/.autopilot-cache"
    echo "Offline mode: copying feed ${FEED_SRC} -> ${CACHE_ROOT} (writable)..."
    mkdir -p "${CACHE_ROOT}"
    cp -a "${FEED_SRC}/." "${CACHE_ROOT}/"

    if [ -d "${CACHE_ROOT}/nuget" ]; then
        export NUGET_CONFIG="${CACHE_ROOT}/nuget.config"
        cat > "${NUGET_CONFIG}" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
  </packageSources>
  <fallbackPackageFolders>
    <clear />
  </fallbackPackageFolders>
  <config>
    <add key="globalPackagesFolder" value="${CACHE_ROOT}/nuget" />
  </config>
</configuration>
EOF
        echo "NuGet offline config: ${NUGET_CONFIG} (globalPackagesFolder=${CACHE_ROOT}/nuget)"
    fi

    if [ -d "${CACHE_ROOT}/npm" ]; then
        export npm_config_userconfig="${CACHE_ROOT}/npmrc"
        export npm_config_cache="${CACHE_ROOT}/npm"
        export npm_config_offline="true"
        cat > "${npm_config_userconfig}" <<EOF
cache=${CACHE_ROOT}/npm
offline=true
EOF
        echo "npm offline config: cache=${CACHE_ROOT}/npm offline=true"
    fi
fi

# --- Execute plan phases ---
PLAN_PATH="docs/implementation-plans/${PLAN_SLUG}/plan.md"

if [ ! -f "${PLAN_PATH}" ]; then
    echo "ERROR: Plan not found at ${PLAN_PATH}"
    exit 1
fi

# Parse actual phase numbers and select only incomplete phases. A whole-plan
# relaunch with every step complete gets one confined completion-only target.
PHASE_NUMS=$(autopilot_phase_numbers "${PLAN_PATH}")
PHASE_COUNT=$(printf '%s\n' "${PHASE_NUMS}" | grep -c '[0-9]' || echo "0")
FINAL_PHASE_NUM=$(printf '%s\n' "${PHASE_NUMS}" | tail -n 1)
echo "Found ${PHASE_COUNT} phases in plan (numbers: $(echo ${PHASE_NUMS} | tr '\n' ' '))."
if ! TARGET_OUTPUT=$(autopilot_execution_targets "${PLAN_PATH}" "${MODE}"); then
    echo "ERROR: Unable to resolve safe autopilot execution targets."
    exit 1
fi
EXECUTION_TARGETS=()
if [ -n "${TARGET_OUTPUT}" ]; then
    mapfile -t EXECUTION_TARGETS <<< "${TARGET_OUTPUT}"
fi

COMPLETION_HANDOFF_LIMIT="${AUTOPILOT_COMPLETION_HANDOFF_LIMIT:-3}"
if [[ ! "${COMPLETION_HANDOFF_LIMIT}" =~ ^[1-9][0-9]*$ ]]; then
    echo "ERROR: AUTOPILOT_COMPLETION_HANDOFF_LIMIT must be a positive integer."
    exit 1
fi
echo "Completion handoff limit: ${COMPLETION_HANDOFF_LIMIT} same-session resume(s) per target."

# Per-target copilot invocations
USAGE_OUTPUT_DIR="/tmp/autopilot-usage"
mkdir -p "${USAGE_OUTPUT_DIR}"
COMPLETION_ALLOWED=1
RUN_EXIT_CODE=0
for TARGET in "${EXECUTION_TARGETS[@]}"; do
    echo ""
    if [ "${COMPLETION_ALLOWED}" -ne 1 ]; then
        if autopilot_target_owns_finalization "${TARGET}" "${FINAL_PHASE_NUM}" ||
            [[ "${TARGET}" == operator-stop:* ]]; then
            echo "Skipping ${TARGET}: an earlier target failed, so completion is not eligible."
            break
        fi
    fi
    if [[ "${TARGET}" == operator-stop:* ]]; then
        PHASE_NUM="${TARGET#operator-stop:}"
        if [ "${PHASE_NUM}" = "plan-finalization" ]; then
            echo "Plan-finalization review is wrapped or requires an operator decision — stopping."
        else
            echo "Phase ${PHASE_NUM} review requires an operator decision — stopping."
        fi
        preserve_work || exit 70
        git push origin "${WORK_BRANCH}" || true
        exit 42
    elif [ "${TARGET}" = "completion-only" ]; then
        if ! autopilot_plan_all_steps_complete "${PLAN_PATH}"; then
            echo "ERROR: Plan completion target is no longer eligible — stopping without finalization."
            exit 1
        fi
        TARGET_LABEL="Plan completion"
        TRANSCRIPT="session-transcript-completion.md"
        PROMPT="Resume ${PLAN_PATH} at Plan Completion only. Every implementation step is already [x]. Do not replay phase completion or reopen completed steps."
    elif [[ "${TARGET}" == phase-completion:* ]]; then
        PHASE_NUM="${TARGET#phase-completion:}"
        TARGET_LABEL="Phase ${PHASE_NUM} completion"
        TRANSCRIPT="session-transcript-phase${PHASE_NUM}-completion.md"
        PROMPT="Resume ${PLAN_PATH}, phase ${PHASE_NUM}, at On Phase Completion only. Every implementation step in the phase is already [x]. Do not reopen or replay completed implementation steps."
    else
        PHASE_NUM="${TARGET#phase:}"
        set +e
        phase_needs_execution "${PLAN_PATH}" "${PHASE_NUM}" "."
        PHASE_STATE=$?
        set -e
        if [ "${PHASE_STATE}" -eq 1 ]; then
            echo "Phase ${PHASE_NUM}: checklist and phase close complete — skipping."
            continue
        elif [ "${PHASE_STATE}" -eq 2 ]; then
            preserve_work || exit 70
            exit 3
        fi
        TARGET_LABEL="Phase ${PHASE_NUM}"
        TRANSCRIPT="session-transcript-phase${PHASE_NUM}.md"
        PROMPT="Execute ${PLAN_PATH}, phase ${PHASE_NUM}"
    fi
    USAGE_OUTPUT="${USAGE_OUTPUT_DIR}/session-usage-${TARGET//:/-}.json"
    echo "=== ${TARGET_LABEL} ==="

    TARGET_SESSION_ID=$(cat /proc/sys/kernel/random/uuid)
    TARGET_PROMPT="${PROMPT}"
    HANDOFF_COUNT=0
    while true; do
        # Pass --model explicitly when set so model selection is deterministic
        # (not just implied by COPILOT_MODEL) and visible in logs.
        MODEL_ARGS=()
        if [ -n "${COPILOT_MODEL:-}" ]; then
            MODEL_ARGS=(--model "${COPILOT_MODEL}")
            echo "Invoking Copilot CLI with model: ${COPILOT_MODEL}"
        else
            echo "Invoking Copilot CLI with CLI default model (COPILOT_MODEL unset)"
        fi

        copilot -p "${TARGET_PROMPT}" \
            "${MODEL_ARGS[@]}" \
            --context "${COPILOT_CONTEXT}" \
            --effort "${COPILOT_REASONING_EFFORT}" \
            --agent autopilot \
            --session-id "${TARGET_SESSION_ID}" \
            --no-ask-user \
            --usage-output-file="${USAGE_OUTPUT}" \
            --share="./${TRANSCRIPT}" &
        COPILOT_PID=$!

        set +e
        wait "${COPILOT_PID}"
        EXIT_CODE=$?
        set -e
        COPILOT_PID=""

        CLOSE_STATE=""
        if [ "${EXIT_CODE}" -eq 0 ]; then
            echo "Publishing ${WORK_BRANCH} before terminal close proof..."
            if ! git push origin "${WORK_BRANCH}"; then
                echo "ERROR: Failed to publish successful child work before close proof."
                preserve_work || true
                exit 70
            fi
            if ! CLOSE_STATE=$(
                autopilot_entrypoint_target_close_state \
                    "${PLAN_PATH}" "${TARGET}" "${FINAL_PHASE_NUM}" "${WORK_BRANCH}"
            ); then
                echo "ERROR: Unable to verify terminal close state for ${TARGET_LABEL}."
                preserve_work
                exit 1
            fi
        fi
        HANDOFF_ACTION=$(
            autopilot_completion_handoff_action \
                "${EXIT_CODE}" "${CLOSE_STATE}" "${HANDOFF_COUNT}" "${COMPLETION_HANDOFF_LIMIT}"
        )
        case "${HANDOFF_ACTION}" in
            complete)
                echo "${TARGET_LABEL} complete."
                break
                ;;
            resume)
                HANDOFF_COUNT=$((HANDOFF_COUNT + 1))
                echo "${TARGET_LABEL} exited zero with close state 'close-pending'; resuming session ${TARGET_SESSION_ID} (${HANDOFF_COUNT}/${COMPLETION_HANDOFF_LIMIT})."
                TARGET_PROMPT="Continue the existing ${TARGET_LABEL} target for ${PLAN_PATH}. The previous response ended while required close work was still pending. Resume any still-running validation through its existing tool session and wait for terminal output; if it is no longer running, rerun the required validation. This runtime resume is not operator authorization: never Continue or Reopen a review gate unless an explicit durable supported authorization record already made the gate eligible. Do not end or report success until validation, durable close receipts, review, push, PR, and archive work required by the original target are terminal. Do not replay completed implementation. Original target: ${PROMPT}"
                ;;
            pending-failed)
                echo "ERROR: ${TARGET_LABEL} remained 'close-pending' after ${HANDOFF_COUNT} same-session handoffs."
                preserve_work
                exit 1
                ;;
            human-stop)
                echo "${TARGET_LABEL} requires operator action — stopping."
                preserve_work || exit 70
                git push origin "${WORK_BRANCH}" || true
                exit 42
                ;;
            rebundle)
                echo "Offline rebundle requested (exit 43) — pushing manifest commit and signaling host."
                git push origin "${WORK_BRANCH}"
                exit 43
                ;;
            invalid-close)
                echo "ERROR: ${TARGET_LABEL} returned invalid close state '${CLOSE_STATE}'."
                preserve_work
                exit 1
                ;;
            target-failed)
                echo "${TARGET_LABEL} exited with code ${EXIT_CODE}"
                preserve_work || exit 70
                COMPLETION_ALLOWED=0
                if [ "${RUN_EXIT_CODE}" -eq 0 ]; then
                    RUN_EXIT_CODE="${EXIT_CODE}"
                fi
                git push origin "${WORK_BRANCH}" || true
                break 2
                ;;
        esac
    done
done

echo ""
echo "=== Execution finished ==="
echo "Pushing branch ${WORK_BRANCH}..."
if ! git push origin "${WORK_BRANCH}"; then
    echo "ERROR: Failed to publish completed work; container recovery is required."
    touch /tmp/autopilot-preservation-failed
    exit 70
fi

# PR creation remains the autopilot agent's responsibility. The entrypoint publishes
# the branch and verifies the resulting PR metadata before accepting final close.

echo "Done."
exit "${RUN_EXIT_CODE}"
