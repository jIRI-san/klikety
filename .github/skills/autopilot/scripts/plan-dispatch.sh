#!/bin/bash

autopilot_phase_numbers() {
    grep -oE '^## Phase [0-9]+' "$1" | grep -oE '[0-9]+' || true
}

autopilot_phase_has_incomplete_steps() {
    local plan_path="$1"
    local phase_number="$2"

    awk -v phase="${phase_number}" '
        $0 ~ "^## Phase " phase "([^0-9]|$)" { in_phase = 1; next }
        in_phase && /^## / { exit }
        in_phase && /^- \[( |~)\]/ { print "incomplete"; exit }
    ' "${plan_path}" | grep -q .
}

autopilot_phase_has_noncomplete_steps() {
    local plan_path="$1"
    local phase_number="$2"

    awk -v phase="${phase_number}" '
        $0 ~ "^## Phase " phase "([^0-9]|$)" { in_phase = 1; next }
        in_phase && /^## / { exit }
        in_phase && /^- \[[^x]\]/ { print "noncomplete"; exit }
    ' "${plan_path}" | grep -q .
}

autopilot_phase_has_unsupported_steps() {
    local plan_path="$1"
    local phase_number="$2"

    awk -v phase="${phase_number}" '
        $0 ~ "^## Phase " phase "([^0-9]|$)" { in_phase = 1; next }
        in_phase && /^## / { exit }
        in_phase && /^- \[[^x ~]\]/ { print "unsupported"; exit }
    ' "${plan_path}" | grep -q .
}

autopilot_plan_has_steps() {
    awk '
        /^## Phase [0-9]+/ { in_phase = 1; next }
        /^## / { in_phase = 0 }
        in_phase && /^- \[[x ~]\]/ { print "step"; exit }
    ' "$1" | grep -q .
}

autopilot_plan_all_steps_complete() {
    local plan_path="$1"
    local phase_number

    autopilot_plan_has_steps "${plan_path}" || return 1
    while IFS= read -r phase_number; do
        if autopilot_phase_has_noncomplete_steps "${plan_path}" "${phase_number}"; then
            return 1
        fi
    done < <(autopilot_phase_numbers "${plan_path}")
    return 0
}

autopilot_target_owns_finalization() {
    local target="$1"

    [ "${target}" = "completion-only" ]
}

autopilot_branch_has_published_pr() {
    local repo_root="$1"
    local expected_branch="$2"
    local target_branch="${3:-}"
    local gh_bin="${AUTOPILOT_GH_BIN:-gh}"
    local current_branch
    local local_head
    local remote_output
    local remote_oid
    local remote_ref
    local pr_output
    local pr_head
    local pr_oid
    local pr_base
    local pr_state
    local matching_prs=0
    local branch_published=0
    local extra
    local row
    local -a remote_rows=()
    local -a pr_rows=()

    if ! git -C "${repo_root}" check-ref-format --branch "${expected_branch}" >/dev/null 2>&1; then
        printf 'ERROR: expected work branch is invalid for close proof.\n' >&2
        return 2
    fi
    if [ -n "${target_branch}" ] &&
        ! git -C "${repo_root}" check-ref-format --branch "${target_branch}" >/dev/null 2>&1; then
        printf 'ERROR: expected target branch is invalid for close proof.\n' >&2
        return 2
    fi
    if ! current_branch=$(git -C "${repo_root}" branch --show-current) ||
        [ -z "${current_branch}" ]; then
        printf 'ERROR: unable to resolve the current branch for PR close proof.\n' >&2
        return 2
    fi
    if [ "${current_branch}" != "${expected_branch}" ]; then
        return 1
    fi
    if ! local_head=$(git -C "${repo_root}" rev-parse --verify 'HEAD^{commit}') ||
        [[ ! "${local_head}" =~ ^([0-9a-f]{40}|[0-9a-f]{64})$ ]]; then
        printf 'ERROR: local HEAD is not a full commit id for close proof.\n' >&2
        return 2
    fi
    if ! remote_output=$(
        git -C "${repo_root}" ls-remote --refs origin "refs/heads/${expected_branch}"
    ); then
        printf 'ERROR: unable to inspect the published work branch for close proof.\n' >&2
        return 2
    fi
    if [ -n "${remote_output}" ]; then
        mapfile -t remote_rows <<< "${remote_output}"
    fi
    for row in "${remote_rows[@]}"; do
        IFS=$'\t' read -r remote_oid remote_ref extra <<< "${row}"
        if [ -n "${extra:-}" ] ||
            [[ ! "${remote_oid:-}" =~ ^([0-9a-f]{40}|[0-9a-f]{64})$ ]] ||
            [ -z "${remote_ref:-}" ]; then
            printf 'ERROR: published work branch close proof returned malformed ref data.\n' >&2
            return 2
        fi
        if [ "${remote_ref}" != "refs/heads/${expected_branch}" ]; then
            return 1
        fi
    done
    if [ "${#remote_rows[@]}" -eq 1 ]; then
        IFS=$'\t' read -r remote_oid remote_ref extra <<< "${remote_rows[0]}"
        if [ "${remote_oid}" = "${local_head}" ]; then
            branch_published=1
        fi
    elif [ "${#remote_rows[@]}" -gt 1 ]; then
        return 1
    fi

    if ! pr_output=$(
        cd "${repo_root}" &&
            "${gh_bin}" pr list \
                --head "${expected_branch}" \
                --state all \
                --limit 100 \
                --json headRefName,headRefOid,baseRefName,state \
                --jq '.[] | [.headRefName, .headRefOid, .baseRefName, .state] | @tsv'
    ); then
        printf 'ERROR: unable to inspect the branch pull request for close proof.\n' >&2
        return 2
    fi
    if [ -n "${pr_output}" ]; then
        mapfile -t pr_rows <<< "${pr_output}"
    fi
    if [ "${#pr_rows[@]}" -eq 0 ]; then
        return 1
    fi
    for row in "${pr_rows[@]}"; do
        IFS=$'\t' read -r pr_head pr_oid pr_base pr_state extra <<< "${row}"
        if [ -n "${extra:-}" ] ||
            [ -z "${pr_head:-}" ] ||
            [[ ! "${pr_oid:-}" =~ ^([0-9a-f]{40}|[0-9a-f]{64})$ ]] ||
            [ -z "${pr_base:-}" ] ||
            [ -z "${pr_state:-}" ]; then
            printf 'ERROR: pull request close proof returned malformed typed metadata.\n' >&2
            return 2
        fi
        if [ "${pr_head}" = "${expected_branch}" ] &&
            [ "${pr_oid}" = "${local_head}" ] &&
            { [ -z "${target_branch}" ] || [ "${pr_base}" = "${target_branch}" ]; } &&
            { [ "${pr_state}" = "MERGED" ] ||
                { [ "${pr_state}" = "OPEN" ] && [ "${branch_published}" -eq 1 ]; }; }; then
            matching_prs=$((matching_prs + 1))
        fi
    done
    if [ "${matching_prs}" -ne 1 ]; then
        return 1
    fi
    return 0
}

autopilot_tree_records() {
    local repo_root="$1"
    local commit="$2"
    local tree_path="$3"
    local metadata
    local path

    git -C "${repo_root}" ls-tree -r "${commit}" -- "${tree_path}" |
        while IFS=$'\t' read -r metadata path; do
            printf '%s\t%s\n' "${metadata}" "${path#"${tree_path}/"}"
        done
}

autopilot_archive_transition_is_committed() {
    local repo_root="$1"
    local active_dir="$2"
    local archived_dir="$3"
    local archived_plan="$4"
    local archive_commit="$5"
    local archive_parent="${archive_commit}^"
    local active_tree_at_parent
    local archived_tree_at_parent
    local archived_tree_at_commit
    local archived_tree_at_head

    active_tree_at_parent=$(autopilot_tree_records "${repo_root}" "${archive_parent}" "${active_dir}")
    archived_tree_at_parent=$(autopilot_tree_records "${repo_root}" "${archive_parent}" "${archived_dir}")
    archived_tree_at_commit=$(autopilot_tree_records "${repo_root}" "${archive_commit}" "${archived_dir}")
    archived_tree_at_head=$(autopilot_tree_records "${repo_root}" HEAD "${archived_dir}")

    git -C "${repo_root}" cat-file -e "HEAD:${archived_plan}" 2>/dev/null &&
        [ -n "${active_tree_at_parent}" ] &&
        [ -z "${archived_tree_at_parent}" ] &&
        [ -n "${archived_tree_at_commit}" ] &&
        [ -n "${archived_tree_at_head}" ] &&
        [ -z "$(git -C "${repo_root}" ls-tree -r --name-only "${archive_commit}" -- "${active_dir}")" ] &&
        [ -z "$(git -C "${repo_root}" ls-tree -r --name-only HEAD -- "${active_dir}")" ] &&
        [ -z "$(git -C "${repo_root}" status --porcelain --untracked-files=all -- "${active_dir}" "${archived_dir}")" ]
}

autopilot_target_close_state() {
    local plan_path="$1"
    local target="$2"
    local final_phase_number="$3"
    local expected_work_branch="${4:-}"
    local target_branch="${5:-}"
    local phase_number
    local phase_state
    local phase_state_status
    local repo_root="${AUTOPILOT_REPO_ROOT:-.}"
    local state_script="${AUTOPILOT_PHASE_STATE_SCRIPT:-${repo_root}/.github/skills/autopilot/scripts/Get-PhaseExecutionState.ps1}"
    local active_plan_dir
    local plan_root
    local plan_slug
    local archived_plan
    local effective_plan_path="${plan_path}"
    local archive_transition=0
    local repo_root_full
    local archived_plan_relative
    local archived_dir_relative
    local active_plan_relative
    local active_dir_relative
    local archive_commit
    local archive_parent
    local pr_status
    local plan_status

    if [ ! -f "${plan_path}" ]; then
        if autopilot_target_owns_finalization "${target}" "${final_phase_number}"; then
            active_plan_dir=$(dirname "${plan_path}")
            plan_root=$(dirname "${active_plan_dir}")
            plan_slug=$(basename "${active_plan_dir}")
            archived_plan="${plan_root}/archived/${plan_slug}/plan.md"
            if [ -f "${archived_plan}" ]; then
                effective_plan_path="${archived_plan}"
                archive_transition=1
            else
                printf 'ERROR: active plan disappeared without archived close state at %s.\n' "${archived_plan}" >&2
                return 2
            fi
        else
            printf 'ERROR: active plan disappeared before target %s closed.\n' "${target}" >&2
            return 2
        fi
    fi

    if [[ "${target}" == phase:* ]] || [[ "${target}" == phase-completion:* ]]; then
        phase_number="${target#*:}"
        if autopilot_phase_has_unsupported_steps "${effective_plan_path}" "${phase_number}"; then
            printf 'ERROR: phase %s contains an unsupported step state.\n' "${phase_number}" >&2
            return 2
        fi
        if autopilot_phase_has_noncomplete_steps "${effective_plan_path}" "${phase_number}"; then
            printf '%s\n' 'close-pending'
            return
        fi
    fi
    if [ -z "${phase_number:-}" ] &&
        autopilot_target_owns_finalization "${target}" "${final_phase_number}"; then
        phase_number="${final_phase_number}"
    fi

    if [ ! -f "${state_script}" ]; then
        printf 'ERROR: canonical phase close probe is unavailable.\n' >&2
        return 2
    fi
    if phase_state=$(
        pwsh -NoProfile -File "${state_script}" \
            -PlanPath "${effective_plan_path}" \
            -Phase "${phase_number}" \
            -RepoRoot "${repo_root}"
    ); then
        phase_state_status=0
    else
        phase_state_status=$?
    fi
    if [ "${phase_state_status}" -ne 0 ]; then
        printf 'ERROR: phase close validation failed for phase %s.\n' "${phase_number}" >&2
        return 2
    fi
    case "${phase_state}" in
        execution-required|close-pending)
            printf '%s\n' 'close-pending'
            return
            ;;
        closed) ;;
        *)
            printf 'ERROR: phase close state returned an unknown result for phase %s.\n' "${phase_number}" >&2
            return 2
            ;;
    esac
    if ! plan_status=$(
        git -C "${repo_root}" status --porcelain --untracked-files=all \
            -- "$(dirname "${effective_plan_path}")"
    ); then
        printf 'ERROR: unable to inspect committed plan close state.\n' >&2
        return 2
    fi
    if [ -n "${plan_status}" ]; then
        printf '%s\n' 'close-pending'
        return
    fi

    if autopilot_target_owns_finalization "${target}" "${final_phase_number}"; then
        if ! autopilot_plan_all_steps_complete "${effective_plan_path}"; then
            printf '%s\n' 'close-pending'
            return
        fi
        if [ "${archive_transition}" -eq 1 ]; then
            if ! repo_root_full=$(git -C "${repo_root}" rev-parse --show-toplevel); then
                printf 'ERROR: unable to resolve repository root for archived close state.\n' >&2
                return 2
            fi
            if ! archived_plan_relative=$(
                git -C "${repo_root_full}" ls-files --full-name --error-unmatch \
                    -- "${effective_plan_path}" 2>/dev/null
            ); then
                printf '%s\n' 'close-pending'
                return
            fi
            archived_dir_relative=$(dirname "${archived_plan_relative}")
            archive_commit=$(
                git -C "${repo_root_full}" log --diff-filter=A -1 --format=%H \
                    -- "${archived_plan_relative}"
            )
            archive_parent="${archive_commit}^"
            if [ -z "${archive_commit}" ]; then
                printf '%s\n' 'close-pending'
                return
            fi
            active_plan_relative=$(
                git -C "${repo_root_full}" ls-tree -r --name-only "${archive_parent}" \
                    -- "${plan_path}"
            )
            if [ -z "${active_plan_relative}" ]; then
                printf '%s\n' 'close-pending'
                return
            fi
            active_dir_relative=$(dirname "${active_plan_relative}")
            if ! autopilot_archive_transition_is_committed \
                "${repo_root_full}" "${active_dir_relative}" "${archived_dir_relative}" \
                "${archived_plan_relative}" "${archive_commit}"; then
                printf '%s\n' 'close-pending'
                return
            fi
            if [ -z "${expected_work_branch}" ]; then
                printf 'ERROR: expected work branch is required for final close proof.\n' >&2
                return 2
            fi
            if autopilot_branch_has_published_pr \
                "${repo_root_full}" "${expected_work_branch}" "${target_branch}"; then
                pr_status=0
            else
                pr_status=$?
            fi
            case "${pr_status}" in
                0) ;;
                1)
                    printf '%s\n' 'close-pending'
                    return
                    ;;
                *) return 2 ;;
            esac
            printf '%s\n' 'closed'
            return
        fi
        printf '%s\n' 'close-pending'
        return
    fi

    printf '%s\n' 'closed'
}

autopilot_completion_handoff_action() {
    local exit_code="$1"
    local close_state="$2"
    local handoff_count="$3"
    local handoff_limit="$4"

    if [ "${exit_code}" -eq 43 ]; then
        printf '%s\n' 'rebundle'
    elif [ "${exit_code}" -eq 42 ]; then
        printf '%s\n' 'human-stop'
    elif [ "${exit_code}" -ne 0 ]; then
        printf '%s\n' 'target-failed'
    elif [ "${close_state}" = "closed" ]; then
        printf '%s\n' 'complete'
    elif [ "${close_state}" = "operator-decision" ]; then
        printf '%s\n' 'human-stop'
    elif [ "${close_state}" = "close-pending" ] &&
        [ "${handoff_count}" -lt "${handoff_limit}" ]; then
        printf '%s\n' 'resume'
    elif [ "${close_state}" = "close-pending" ]; then
        printf '%s\n' 'pending-failed'
    else
        printf '%s\n' 'invalid-close'
    fi
}

autopilot_execution_targets() {
    local plan_path="$1"
    local mode="$2"
    local phase_number
    local -a phase_numbers=()
    local -a incomplete_phases=()

    mapfile -t phase_numbers < <(autopilot_phase_numbers "${plan_path}")
    for phase_number in "${phase_numbers[@]}"; do
        if autopilot_phase_has_unsupported_steps "${plan_path}" "${phase_number}"; then
            printf 'ERROR: phase %s contains an unsupported step state.\n' "${phase_number}" >&2
            return 2
        fi
        if autopilot_phase_has_incomplete_steps "${plan_path}" "${phase_number}"; then
            incomplete_phases+=("${phase_number}")
        fi
    done

    if [ "${#incomplete_phases[@]}" -eq 0 ]; then
        if [ "${mode}" = "next-phase" ]; then
            return
        fi
    elif [ "${mode}" = "next-phase" ]; then
        printf 'phase:%s\n' "${incomplete_phases[0]}"
        return
    fi

    autopilot_plan_has_steps "${plan_path}" || return
    for phase_number in "${phase_numbers[@]}"; do
        if autopilot_phase_has_incomplete_steps "${plan_path}" "${phase_number}"; then
            printf 'phase:%s\n' "${phase_number}"
            continue
        fi
    done

    if [ "${mode}" = "whole-plan" ]; then
        printf '%s\n' 'completion-only'
    fi
}
