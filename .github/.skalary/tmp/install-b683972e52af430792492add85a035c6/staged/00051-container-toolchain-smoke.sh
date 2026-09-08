#!/usr/bin/env bash
set -o pipefail

export LC_ALL=C

manifest_path=/usr/local/share/autopilot/toolchain.tsv
provenance_dir=/usr/local/share/autopilot/provenance
fallback_json='{"schema":"skalary/container-toolchain-smoke@1","state":"fail","reasons":["encoder-failed"],"origin":{"os":"","aptHosts":[]},"digests":{"manifestSha256":"","provenanceSha256":""},"cases":[]}'
oversize_json='{"schema":"skalary/container-toolchain-smoke@1","state":"fail","reasons":["output-oversize"],"origin":{"os":"","aptHosts":[]},"digests":{"manifestSha256":"","provenanceSha256":""},"cases":[]}'
fixture_root="$(mktemp -d)"
trap 'rm -rf "$fixture_root"' EXIT

fixture_dir="$fixture_root/fixture"
fixture_file="$fixture_dir/known.txt"
mkdir -p "$fixture_dir"
printf 'known-token\n' > "$fixture_file"

declare -A case_states
declare -A reason_set
encoder_failed=false

# A whole-run failure has no case to hang from. Without a named reason, an unreadable manifest, a
# writable /usr/local/bin, and a run as the wrong user all reported an identical bare `state=fail`,
# and direct diagnostics could say only that smoke had failed. Keep the vocabulary closed so this
# field remains predictable for an operator reading the result.
add_reason() {
    reason_set["$1"]=present
}

run_case() {
    local case_id="$1"
    shift
    local diagnostic
    if diagnostic="$("$@" 2>&1)"; then
        case_states["$case_id"]=pass
    else
        case_states["$case_id"]=fail
        printf 'toolchain case %s failed: %.2048s\n' "$case_id" "$diagnostic" >&2
    fi
}

check_rg() {
    rg --quiet 'known-token' "$fixture_dir"
}

check_fd() {
    [[ "$(readlink /usr/local/bin/fd)" == /usr/bin/fdfind ]] &&
        [[ "$(stat -c '%u:%g' /usr/local/bin/fd)" == 0:0 ]] &&
        fd --quiet '^known[.]txt$' "$fixture_dir"
}

check_bat() {
    [[ "$(readlink /usr/local/bin/bat)" == /usr/bin/batcat ]] &&
        [[ "$(stat -c '%u:%g' /usr/local/bin/bat)" == 0:0 ]] &&
        bat --plain --paging=never "$fixture_file" | grep -Fqx 'known-token'
}

check_tree() {
    tree -a "$fixture_dir" | grep -Fq 'known.txt'
}

check_less() {
    less --version | grep -Fq 'less'
}

check_file() {
    file --brief --mime-type "$fixture_file" | grep -Fqx 'text/plain'
}

check_zip() {
    (cd "$fixture_dir" && zip -q archive.zip known.txt) &&
        [[ -s "$fixture_dir/archive.zip" ]]
}

check_unzip() {
    unzip -p "$fixture_dir/archive.zip" known.txt | grep -Fqx 'known-token'
}

check_rsync() {
    mkdir -p "$fixture_root/rsync"
    rsync -a "$fixture_file" "$fixture_root/rsync/"
    cmp "$fixture_file" "$fixture_root/rsync/known.txt"
}

check_native_build() {
    printf '#include <stdio.h>\nint main(void){puts("native-ok");return 0;}\n' > "$fixture_root/smoke.c"
    cc "$fixture_root/smoke.c" -o "$fixture_root/smoke"
    [[ "$("$fixture_root/smoke")" == native-ok ]]
}

check_python() {
    [[ "$(python3 -c 'print("python-ok")')" == python-ok ]]
}

check_python_venv() {
    python3 -m venv "$fixture_root/venv"
    [[ "$("$fixture_root/venv/bin/python" -c 'print("venv-ok")')" == venv-ok ]]
}

check_python_pip() {
    python3 -m pip --version | grep -Fq 'pip '
}

check_shellcheck() {
    printf '#!/bin/sh\nprintf "shell-ok\\n"\n' > "$fixture_root/valid.sh"
    shellcheck "$fixture_root/valid.sh"
}

check_process() {
    ps -p "$$" -o pid= | grep -Eq '[0-9]+'
}

check_lsof() {
    exec 9< "$fixture_file"
    lsof -a -p "$$" -d 9 -Fn | grep -Fqx "n$fixture_file"
    local result=$?
    exec 9<&-
    return "$result"
}

check_ip() {
    ip link show dev lo | grep -Fq 'LOOPBACK'
}

check_dig() {
    dig -v 2>&1 | grep -Fq 'DiG'
}

check_nc() {
    local output
    output="$(nc -h 2>&1)"
    [[ "$output" == *OpenBSD* ]]
}

check_ssh() {
    local output
    output="$(ssh -V 2>&1)"
    [[ "$output" == OpenSSH_* ]]
}

check_sqlite() {
    [[ "$(sqlite3 :memory: 'select 42;')" == 42 ]]
}

# CASE:rg-search
run_case rg-search check_rg
# CASE:fd-find
run_case fd-find check_fd
# CASE:bat-render
run_case bat-render check_bat
# CASE:tree-list
run_case tree-list check_tree
# CASE:less-version
run_case less-version check_less
# CASE:file-type
run_case file-type check_file
# CASE:zip-create
run_case zip-create check_zip
# CASE:unzip-extract
run_case unzip-extract check_unzip
# CASE:rsync-copy
run_case rsync-copy check_rsync
# CASE:native-build
run_case native-build check_native_build
# CASE:python-run
run_case python-run check_python
# CASE:python-venv
run_case python-venv check_python_venv
# CASE:python-pip
run_case python-pip check_python_pip
# CASE:shellcheck-valid
run_case shellcheck-valid check_shellcheck
# CASE:process-self
run_case process-self check_process
# CASE:lsof-open
run_case lsof-open check_lsof
# CASE:ip-loopback
run_case ip-loopback check_ip
# CASE:dig-version
run_case dig-version check_dig
# CASE:nc-help
run_case nc-help check_nc
# CASE:ssh-version
run_case ssh-version check_ssh
# CASE:sqlite-query
run_case sqlite-query check_sqlite

overall_state=pass
if [[ "$(id -un)" != autopilot ]]; then
    overall_state=fail
    add_reason not-autopilot-user
fi
if [[ -w /usr/local/bin ]]; then
    overall_state=fail
    add_reason usr-local-bin-writable
fi

cases_json='[]'
declare -A manifest_cases
if [[ -f "$manifest_path" && -r "$manifest_path" && -s "$manifest_path" ]]; then
    while IFS=$'\t' read -r case_id package_name command_name; do
        [[ -z "$case_id" || "$case_id" == \#* ]] && continue
        if [[ -n "${manifest_cases[$case_id]+present}" ]]; then
            overall_state=fail
            add_reason manifest-duplicate-case
        fi
        manifest_cases["$case_id"]=present
        case_state="${case_states[$case_id]:-fail}"
        package_version="$(dpkg-query -W -f='${Version}' "$package_name" 2>/dev/null)"
        package_version="$(printf '%s' "${package_version:0:128}" | tr -d '\000-\037\177')"
        if [[ "$case_state" != pass || -z "$package_version" || ! -x "$(command -v "$command_name" 2>/dev/null)" ]]; then
            case_state=fail
            overall_state=fail
        fi
        next_cases_json=
        if next_cases_json="$(
            jq -cn \
                --argjson cases "$cases_json" \
                --arg id "$case_id" \
                --arg state "$case_state" \
                --arg version "$package_version" \
                '$cases + [{id:$id,state:$state,version:$version}]'
        )"; then
            cases_json="$next_cases_json"
        else
            cases_json='[]'
            encoder_failed=true
            overall_state=fail
            add_reason encoder-failed
            break
        fi
    done < "$manifest_path"
else
    overall_state=fail
    add_reason manifest-unreadable
fi

if (( ${#manifest_cases[@]} != ${#case_states[@]} )); then
    overall_state=fail
    add_reason case-count-mismatch
fi

provenance_files=(
    apt-sources.txt
    dependency-closure.tsv
    final-apt-sources.txt
    final-npm-globals.json
    final-packages.tsv
    os-release
    requested-packages.tsv
    selected-origins.txt
)
provenance_ready=true
for provenance_file in "${provenance_files[@]}"; do
    if [[ ! -f "$provenance_dir/$provenance_file" || ! -r "$provenance_dir/$provenance_file" || ! -s "$provenance_dir/$provenance_file" ]]; then
        provenance_ready=false
        overall_state=fail
        add_reason provenance-incomplete
    fi
done

os_id=
os_version=
apt_hosts_json='[]'
if [[ -f "$provenance_dir/os-release" && -r "$provenance_dir/os-release" ]]; then
    os_id="$(sed -n 's/^ID=//p' "$provenance_dir/os-release" | tr -d '"' | head -n 1)"
    os_version="$(sed -n 's/^VERSION_ID=//p' "$provenance_dir/os-release" | tr -d '"' | head -n 1)"
fi
os_origin="$(printf '%s:%s' "$os_id" "$os_version" | tr -cd 'A-Za-z0-9._:+-' | head -c 64)"
# The reported origins describe the shipped image, so they come from the final root-layer
# capture rather than the Debian baseline one, which predates the later package layers.
if [[ -f "$provenance_dir/final-apt-sources.txt" && -r "$provenance_dir/final-apt-sources.txt" ]]; then
    if ! apt_hosts_json="$(
        sed -E 's#^https?://([^/:]+).*#\1#' "$provenance_dir/final-apt-sources.txt" |
            tr '[:upper:]' '[:lower:]' |
            LC_ALL=C sort -u |
            jq -Rsc 'split("\n") | map(select(length > 0) | .[0:253])'
    )"; then
        apt_hosts_json='[]'
        encoder_failed=true
        overall_state=fail
        add_reason encoder-failed
    fi
fi

manifest_digest=
if [[ -f "$manifest_path" && -r "$manifest_path" && -s "$manifest_path" ]]; then
    if ! manifest_digest="$(sha256sum "$manifest_path" 2>/dev/null | cut -d ' ' -f1)"; then
        manifest_digest=
        overall_state=fail
        add_reason manifest-digest-unavailable
    fi
else
    overall_state=fail
    add_reason manifest-unreadable
fi

provenance_digest=
if [[ "$provenance_ready" == true ]]; then
    provenance_hashes="$fixture_root/provenance-hashes"
    : > "$provenance_hashes"
    for provenance_file in "${provenance_files[@]}"; do
        file_digest=
        if file_digest="$(sha256sum "$provenance_dir/$provenance_file" 2>/dev/null | cut -d ' ' -f1)" &&
            [[ "$file_digest" =~ ^[a-f0-9]{64}$ ]]; then
            printf '%s\0%s\n' "$provenance_file" "$file_digest" >> "$provenance_hashes"
        else
            provenance_ready=false
            overall_state=fail
            add_reason provenance-digest-unavailable
            break
        fi
    done
    if [[ "$provenance_ready" == true ]]; then
        if ! provenance_digest="$(sha256sum "$provenance_hashes" 2>/dev/null | cut -d ' ' -f1)"; then
            provenance_digest=
            overall_state=fail
            add_reason provenance-digest-unavailable
        fi
    fi
fi
if [[ ! "$manifest_digest" =~ ^[a-f0-9]{64}$ ]]; then
    overall_state=fail
    add_reason manifest-digest-unavailable
fi
if [[ ! "$provenance_digest" =~ ^[a-f0-9]{64}$ ]]; then
    overall_state=fail
    add_reason provenance-digest-unavailable
fi

# Sorted so two runs that fail the same way produce byte-identical output, and unique because the
# host rejects a duplicated reason as malformed rather than merging it silently.
reasons_json='[]'
if (( ${#reason_set[@]} > 0 )); then
    if ! reasons_json="$(printf '%s\n' "${!reason_set[@]}" | LC_ALL=C sort -u | jq -Rsc 'split("\n") | map(select(length > 0))')"; then
        reasons_json='["encoder-failed"]'
        encoder_failed=true
        overall_state=fail
    fi
fi

json=
if ! json="$(
    jq -cn \
        --arg schema 'skalary/container-toolchain-smoke@1' \
        --arg state "$overall_state" \
        --argjson reasons "$reasons_json" \
        --arg os "$os_origin" \
        --argjson aptHosts "$apt_hosts_json" \
        --arg manifestSha256 "$manifest_digest" \
        --arg provenanceSha256 "$provenance_digest" \
        --argjson cases "$cases_json" \
        '{
            schema:$schema,
            state:$state,
            reasons:$reasons,
            origin:{os:$os,aptHosts:$aptHosts},
            digests:{manifestSha256:$manifestSha256,provenanceSha256:$provenanceSha256},
            cases:$cases
        }'
)"; then
    encoder_failed=true
    overall_state=fail
    json="$fallback_json"
fi

if [[ "$encoder_failed" == true ]]; then
    overall_state=fail
    json="$fallback_json"
elif (( ${#json} > 65535 )); then
    overall_state=fail
    json="$oversize_json"
fi

if ! printf '%s\n' "$json"; then
    exit 1
fi
[[ "$overall_state" == pass ]]
