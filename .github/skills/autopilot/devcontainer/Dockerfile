# Autopilot execution container — .NET 10 SDK + Copilot CLI + tooling
# Used by plugins/autopilot/scripts/launch-container.ps1 via `docker build`

FROM debian:trixie-slim

ARG GH_CLI_VERSION=2.74.0
# Fallback only — launch-container.ps1 resolves and passes the latest published
# version via --build-arg COPILOT_CLI_VERSION so each build auto-upgrades.
ARG COPILOT_CLI_VERSION=1.0.60

SHELL ["/bin/bash", "-o", "pipefail", "-c"]

COPY devcontainer/toolchain.tsv /usr/local/share/autopilot/toolchain.tsv

# Bootstrap dependencies stay distinct from the manifest-owned agent baseline.
RUN set -eux; \
    bootstrap_packages=(git curl jq ca-certificates gnupg nodejs npm); \
    awk -F '\t' ' \
        /^[[:space:]]*(#|$)/ { next } \
        NF != 3 || $1 !~ /^[a-z0-9]+(-[a-z0-9]+)*$/ || $2 !~ /^[a-z0-9][a-z0-9+.-]*$/ || $3 !~ /^[a-z0-9][a-z0-9+.-]*$/ { \
            print "Invalid toolchain.tsv row at line " NR > "/dev/stderr"; exit 1 \
        } \
        seen[$1]++ { print "Duplicate toolchain case ID: " $1 > "/dev/stderr"; exit 1 } \
        { count++; print $2 } \
        END { if (count == 0) { print "toolchain.tsv has no package rows" > "/dev/stderr"; exit 1 } } \
    ' /usr/local/share/autopilot/toolchain.tsv > /tmp/autopilot-toolchain-packages; \
    mapfile -t toolchain_packages < /tmp/autopilot-toolchain-packages; \
    if apt-config dump | grep -Eiq '^[[:space:]]*(Binary::[^[:space:]]+::(Root)?Dir|RootDir)([[:space:]]|::)'; then \
        echo "APT binary-scoped Dir or RootDir overrides are not allowed" >&2; exit 1; \
    fi; \
    if [[ -n "${APT_CONFIG:-}" ]]; then \
        echo "APT_CONFIG must not be set" >&2; exit 1; \
    fi; \
    for proxy_var in http_proxy https_proxy ftp_proxy all_proxy HTTP_PROXY HTTPS_PROXY FTP_PROXY ALL_PROXY; do \
        [[ -z "${!proxy_var:-}" ]] \
            || { echo "APT proxy environment variable is not allowed: $proxy_var" >&2; exit 1; }; \
    done; \
    if apt-config dump | grep -Eiq '^[[:space:]]*[^[:space:]]*proxy'; then \
        echo "APT proxy configuration is not allowed" >&2; exit 1; \
    fi; \
    for trust_key in Acquire::AllowInsecureRepositories Acquire::AllowWeakRepositories \
        Acquire::AllowDowngradeToInsecureRepositories Acquire::AllowReleaseInfoChange::Origin \
        Acquire::AllowReleaseInfoChange::Label Acquire::AllowReleaseInfoChange::Suite \
        Acquire::AllowReleaseInfoChange::Codename APT::Get::AllowUnauthenticated \
        APT::Get::AllowInsecureRepositories APT::Get::AllowDowngradeToInsecureRepositories \
        APT::Authentication::TrustCDROM; do \
        trust_value="$(apt-config shell apt_trust "$trust_key/b")"; \
        [[ -z "$trust_value" || "$trust_value" == "apt_trust='false'" ]] \
            || { echo "APT trust-weakening setting is not allowed: $trust_key" >&2; exit 1; }; \
    done; \
    for verify_key in Acquire::Check-Valid-Until Acquire::Check-Date \
        Acquire::https::Verify-Peer Acquire::https::Verify-Host; do \
        verify_value="$(apt-config shell apt_verify "$verify_key/b")"; \
        [[ -z "$verify_value" || "$verify_value" == "apt_verify='true'" ]] \
            || { echo "APT verification must not be disabled: $verify_key" >&2; exit 1; }; \
    done; \
    [[ "$(apt-config shell apt_root Dir)" == "apt_root='/'" ]] \
        || { echo "APT Dir must remain '/'" >&2; exit 1; }; \
    [[ "$(apt-config shell apt_etc Dir::Etc)" == "apt_etc='etc/apt'" ]] \
        || { echo "APT Dir::Etc must remain 'etc/apt'" >&2; exit 1; }; \
    [[ "$(apt-config shell apt_source_list Dir::Etc::sourcelist)" == "apt_source_list='sources.list'" ]] \
        || { echo "APT sourcelist must remain 'sources.list'" >&2; exit 1; }; \
    [[ "$(apt-config shell apt_source_parts Dir::Etc::sourceparts)" == "apt_source_parts='sources.list.d'" ]] \
        || { echo "APT sourceparts must remain 'sources.list.d'" >&2; exit 1; }; \
    [[ "$(apt-config shell apt_source_list_path Dir::Etc::sourcelist/f)" == "apt_source_list_path='/etc/apt/sources.list'" ]] \
        || { echo "APT sourcelist must resolve to /etc/apt/sources.list" >&2; exit 1; }; \
    [[ "$(apt-config shell apt_source_parts_path Dir::Etc::sourceparts/d)" == "apt_source_parts_path='/etc/apt/sources.list.d/'" ]] \
        || { echo "APT sourceparts must resolve to /etc/apt/sources.list.d/" >&2; exit 1; }; \
    apt_source_files=(); \
    if [[ -e /etc/apt/sources.list || -L /etc/apt/sources.list ]]; then \
        [[ -f /etc/apt/sources.list && ! -L /etc/apt/sources.list ]] \
            || { echo "APT sourcelist must be a regular non-symlink file" >&2; exit 1; }; \
        apt_source_files+=(/etc/apt/sources.list); \
    fi; \
    if [[ -e /etc/apt/sources.list.d || -L /etc/apt/sources.list.d ]]; then \
        [[ -d /etc/apt/sources.list.d && ! -L /etc/apt/sources.list.d ]] \
            || { echo "APT sourceparts must be a non-symlink directory" >&2; exit 1; }; \
        if find /etc/apt/sources.list.d -maxdepth 1 -type l \( -name '*.list' -o -name '*.sources' \) -print -quit | grep -q .; then \
            echo "APT sourceparts must not contain symlinked source files" >&2; exit 1; \
        fi; \
        mapfile -t source_part_files < <(find /etc/apt/sources.list.d -maxdepth 1 -type f \( -name '*.list' -o -name '*.sources' \) -print | LC_ALL=C sort); \
        apt_source_files+=("${source_part_files[@]}"); \
    fi; \
    : > /tmp/autopilot-apt-source-uris; \
    for source_file in "${apt_source_files[@]}"; do \
        awk ' \
            { lower = tolower($0) } \
            lower ~ /^[[:space:]]*#/ { next } \
            match(lower, /(^|[^a-z0-9._+-])(trusted|allow-insecure|allow-weak|allow-downgrade-to-insecure)[[:space:]]*[:=][[:space:]]*[a-z0-9]*/) { \
                option = substr(lower, RSTART, RLENGTH); \
                sub(/^[^a-z]*/, "", option); \
                sub(/^[a-z-]+[[:space:]]*[:=][[:space:]]*/, "", option); \
                if (option != "no" && option != "false" && option != "0") { \
                    print "Trust-bypassing apt source option in " FILENAME ": " $0 > "/dev/stderr"; exit 1 \
                } \
            } \
        ' "$source_file"; \
    done; \
    for source_file in "${apt_source_files[@]}"; do \
        case "$source_file" in \
            *.list) \
                awk ' \
                    /^[[:space:]]*(#|$)/ { next } \
                    /^[[:space:]]*deb(-src)?[[:space:]]/ { \
                        line = $0; \
                        sub(/^[[:space:]]*deb(-src)?[[:space:]]+/, "", line); \
                        sub(/^\[[^]]*\][[:space:]]+/, "", line); \
                        split(line, fields, /[[:space:]]+/); \
                        print fields[1] \
                    } \
                ' "$source_file" \
                ;; \
            *.sources) \
                awk ' \
                    BEGIN { RS = ""; FS = "\n" } \
                    { \
                        enabled = 1; collecting = 0; uris = ""; \
                        for (i = 1; i <= NF; i++) { \
                            line = $i; \
                            if (line ~ /^[[:space:]]*[Ee][Nn][Aa][Bb][Ll][Ee][Dd]:[[:space:]]*([Nn][Oo]|[Ff][Aa][Ll][Ss][Ee]|0)[[:space:]]*$/) enabled = 0; \
                            if (line ~ /^[[:space:]]*[Uu][Rr][Ii][Ss]:[[:space:]]*/) { \
                                sub(/^[[:space:]]*[Uu][Rr][Ii][Ss]:[[:space:]]*/, "", line); \
                                uris = uris " " line; collecting = 1; continue \
                            } \
                            if (collecting && line ~ /^[[:space:]]+/ && line !~ /^[[:space:]]*#/) { \
                                sub(/^[[:space:]]+/, "", line); \
                                uris = uris " " line; continue \
                            } \
                            collecting = 0 \
                        } \
                        if (enabled) { \
                            count = split(uris, values, /[[:space:]]+/); \
                            for (i = 1; i <= count; i++) if (values[i] != "") print values[i] \
                        } \
                    } \
                ' "$source_file" \
                ;; \
        esac; \
    done | LC_ALL=C sort -u > /tmp/autopilot-apt-source-uris; \
    test -s /tmp/autopilot-apt-source-uris; \
    while IFS= read -r uri; do \
        if [[ "$uri" =~ ^https?://([^/:]+)(:[0-9]+)?(/|$) ]]; then \
            host="${BASH_REMATCH[1],,}"; \
        else \
            echo "Unsupported active apt source URI: $uri" >&2; exit 1; \
        fi; \
        case "$host" in \
            deb.debian.org|security.debian.org) ;; \
            *) echo "Disallowed active apt source host: $host" >&2; exit 1 ;; \
        esac; \
    done < /tmp/autopilot-apt-source-uris; \
    apt-get update; \
    apt-get install -y --no-install-recommends "${bootstrap_packages[@]}" "${toolchain_packages[@]}"; \
    install -d -m 0755 /usr/local/share/autopilot/provenance; \
    cp /etc/os-release /usr/local/share/autopilot/provenance/os-release; \
    cp /tmp/autopilot-apt-source-uris /usr/local/share/autopilot/provenance/apt-sources.txt; \
    requested_packages=("${bootstrap_packages[@]}" "${toolchain_packages[@]}"); \
    dpkg-query -W -f='${binary:Package}\t${Version}\t${Architecture}\n' "${requested_packages[@]}" \
        | LC_ALL=C sort -u > /usr/local/share/autopilot/provenance/requested-packages.tsv; \
    apt-cache depends --recurse --important "${requested_packages[@]}" \
        | awk '/^[^[:space:]<]/ { package = $1; sub(/:.*/, "", package); print package }' \
        | LC_ALL=C sort -u > /tmp/autopilot-dependency-package-names; \
    dpkg-query -W -f='${db:Status-Status}\t${Package}\t${binary:Package}\t${Version}\t${Architecture}\n' \
        | awk -F '\t' '$1 == "installed" { print $2 "\t" $3 "\t" $4 "\t" $5 }' \
        | LC_ALL=C sort -t $'\t' -k1,1 > /tmp/autopilot-installed-package-records; \
    join -t $'\t' -o 2.2,2.3,2.4 \
        /tmp/autopilot-dependency-package-names \
        /tmp/autopilot-installed-package-records \
        > /usr/local/share/autopilot/provenance/dependency-closure.tsv; \
    test -s /usr/local/share/autopilot/provenance/dependency-closure.tsv; \
    mapfile -t dependency_packages < <(cut -f1 /usr/local/share/autopilot/provenance/dependency-closure.tsv); \
    apt-cache policy "${dependency_packages[@]}" > /usr/local/share/autopilot/provenance/selected-origins.txt; \
    rm -f \
        /tmp/autopilot-toolchain-packages \
        /tmp/autopilot-apt-source-uris \
        /tmp/autopilot-dependency-package-names \
        /tmp/autopilot-installed-package-records; \
    rm -rf /var/lib/apt/lists/*

# PowerShell and .NET 10 SDK
# The .deb and the Docker signing key are fetched over the network and then installed as root,
# so each is bound to a digest or fingerprint recorded here. A rotated upstream artifact fails
# the build closed instead of installing an unreviewed payload with root trust.
RUN set -eux; \
    curl -fsSL https://packages.microsoft.com/config/debian/13/packages-microsoft-prod.deb -o /tmp/packages-microsoft-prod.deb; \
    echo "d0c2f69250c6ce0d4c6220b142f999d039a3c560af7f980b943687d106ca8e38  /tmp/packages-microsoft-prod.deb" | sha256sum -c -; \
    dpkg -i /tmp/packages-microsoft-prod.deb; \
    rm /tmp/packages-microsoft-prod.deb; \
    apt-get update; \
    apt-get install -y --no-install-recommends dotnet-sdk-10.0 powershell; \
    rm -rf /var/lib/apt/lists/*

# GitHub CLI
RUN set -eux; \
    curl -fsSL "https://github.com/cli/cli/releases/download/v${GH_CLI_VERSION}/gh_${GH_CLI_VERSION}_linux_amd64.deb" -o /tmp/gh.deb; \
    echo "14720066647ceac6138e4134c5d0c31790e81e5cbc4719611323ea0e4ed231ba  /tmp/gh.deb" | sha256sum -c -; \
    dpkg -i /tmp/gh.deb; \
    rm /tmp/gh.deb

# Docker CLI (for Testcontainers — talks to host Docker daemon via socket mount)
# The key is pinned by fingerprint, and only the pinned key is exported into the keyring named by
# `signed-by=`. Asserting that the fingerprint is merely *present* would accept a served file
# carrying the genuine Docker key alongside an attacker's, and apt would then trust both.
RUN set -eux; \
    curl -fsSL https://download.docker.com/linux/debian/gpg -o /tmp/docker.asc; \
    GNUPGHOME="$(mktemp -d)"; export GNUPGHOME; \
    gpg --batch --import /tmp/docker.asc; \
    gpg --batch --export 9DC858229FC7DD38854AE2D88D81803C0EBFCD88 \
        > /usr/share/keyrings/docker-archive-keyring.gpg; \
    rm -rf "$GNUPGHOME" /tmp/docker.asc; unset GNUPGHOME; \
    test -s /usr/share/keyrings/docker-archive-keyring.gpg; \
    gpg --batch --no-default-keyring --keyring /usr/share/keyrings/docker-archive-keyring.gpg \
        --list-keys --with-colons \
        | awk -F: '$1 == "pub" { primary = 1; next } $1 == "fpr" && primary { print $10; primary = 0 }' \
        > /tmp/docker-primaries.txt; \
    test "$(wc -l < /tmp/docker-primaries.txt)" -eq 1; \
    grep -qx 9DC858229FC7DD38854AE2D88D81803C0EBFCD88 /tmp/docker-primaries.txt; \
    rm -f /tmp/docker-primaries.txt; \
    echo "deb [arch=amd64 signed-by=/usr/share/keyrings/docker-archive-keyring.gpg] https://download.docker.com/linux/debian trixie stable" \
        > /etc/apt/sources.list.d/docker.list; \
    apt-get update; \
    apt-get install -y --no-install-recommends docker-ce-cli; \
    rm -rf /var/lib/apt/lists/*

# GitHub Copilot CLI
# `--ignore-scripts` is the enforceable half of this install: npm cannot pin the tarball's contents
# to a digest here (a version tag can be republished), but it can be stopped from executing
# arbitrary install-time scripts as root. The unpinned-content residue is recorded in the design
# note's residual risks rather than papered over.
RUN npm install -g --ignore-scripts --no-fund --no-audit @github/copilot@${COPILOT_CLI_VERSION}

# Pester (pinned) for PowerShell unit gates in autopilot container mode
# `[5.6.1]` is an exact-version range, not a minimum, so a newer publish cannot satisfy it. No
# signature check is claimed: PSResourceGet's `-AuthenticodeCheck` validates only on Windows, so
# passing it on Debian would name a control that does not run. The pin is on the version, and the
# residual — that a version can be unpublished and republished with different bytes — is the same
# one the Copilot CLI install carries, and is recorded with it.
RUN pwsh -NoProfile -Command 'Install-PSResource -Name Pester -Version "[5.6.1]" -Repository PSGallery -Scope AllUsers -TrustRepository -ErrorAction Stop'

# Container entry-point script
COPY scripts/container-entrypoint.sh /usr/local/bin/container-entrypoint.sh
COPY scripts/Get-PhaseExecutionState.ps1 scripts/DirectWorkflow.psm1 scripts/PlanState.psm1 scripts/SecretGuard.psm1 /usr/local/lib/autopilot/
COPY scripts/plan-dispatch.sh /usr/local/lib/autopilot/plan-dispatch.sh
COPY devcontainer/container-toolchain-smoke.sh /usr/local/bin/container-toolchain-smoke
RUN chmod 0755 /usr/local/bin/container-entrypoint.sh /usr/local/bin/container-toolchain-smoke \
    && chmod 0444 /usr/local/lib/autopilot/* \
    && chown root:root \
        /usr/local/share/autopilot/toolchain.tsv \
        /usr/local/lib/autopilot/* \
        /usr/local/bin/container-toolchain-smoke \
    && ln -s /usr/bin/fdfind /usr/local/bin/fd \
    && ln -s /usr/bin/batcat /usr/local/bin/bat \
    && test "$(stat -c '%u:%g' /usr/local/bin/fd)" = 0:0 \
    && test "$(stat -c '%u:%g' /usr/local/bin/bat)" = 0:0

# Non-root user for security — add to docker group for socket access
RUN groupadd -g 998 docker || true \
    && useradd -m -s /bin/bash -G docker autopilot

# Final root-layer provenance — the baseline capture above predates the PowerShell, GitHub CLI,
# Docker CLI and npm layers, so on its own it cannot describe the image that is actually shipped.
# This records the apt origins and package set as they stand after the last root install, which is
# what the gate attests against. It is deliberately the *last* root layer, after the non-root user
# is created: the non-root-user comment above is the documented anchor for extending the image, so a
# capture placed before it would silently omit anything a later maintainer adds there — which is the
# exact failure the baseline capture already demonstrated.
RUN set -eux; \
    if [[ -n "${APT_CONFIG:-}" ]]; then \
        echo "APT_CONFIG must not be set" >&2; exit 1; \
    fi; \
    for proxy_var in http_proxy https_proxy ftp_proxy all_proxy HTTP_PROXY HTTPS_PROXY FTP_PROXY ALL_PROXY; do \
        [[ -z "${!proxy_var:-}" ]] \
            || { echo "APT proxy environment variable is not allowed: $proxy_var" >&2; exit 1; }; \
    done; \
    if apt-config dump | grep -Eiq '^[[:space:]]*[^[:space:]]*proxy'; then \
        echo "APT proxy configuration is not allowed" >&2; exit 1; \
    fi; \
    for trust_key in Acquire::AllowInsecureRepositories Acquire::AllowWeakRepositories \
        Acquire::AllowDowngradeToInsecureRepositories Acquire::AllowReleaseInfoChange::Origin \
        Acquire::AllowReleaseInfoChange::Label Acquire::AllowReleaseInfoChange::Suite \
        Acquire::AllowReleaseInfoChange::Codename APT::Get::AllowUnauthenticated \
        APT::Get::AllowInsecureRepositories APT::Get::AllowDowngradeToInsecureRepositories \
        APT::Authentication::TrustCDROM; do \
        trust_value="$(apt-config shell apt_trust "$trust_key/b")"; \
        [[ -z "$trust_value" || "$trust_value" == "apt_trust='false'" ]] \
            || { echo "APT trust-weakening setting is not allowed: $trust_key" >&2; exit 1; }; \
    done; \
    for verify_key in Acquire::Check-Valid-Until Acquire::Check-Date \
        Acquire::https::Verify-Peer Acquire::https::Verify-Host; do \
        verify_value="$(apt-config shell apt_verify "$verify_key/b")"; \
        [[ -z "$verify_value" || "$verify_value" == "apt_verify='true'" ]] \
            || { echo "APT verification must not be disabled: $verify_key" >&2; exit 1; }; \
    done; \
    [[ "$(apt-config shell apt_source_list_path Dir::Etc::sourcelist/f)" == "apt_source_list_path='/etc/apt/sources.list'" ]] \
        || { echo "APT sourcelist must resolve to /etc/apt/sources.list" >&2; exit 1; }; \
    [[ "$(apt-config shell apt_source_parts_path Dir::Etc::sourceparts/d)" == "apt_source_parts_path='/etc/apt/sources.list.d/'" ]] \
        || { echo "APT sourceparts must resolve to /etc/apt/sources.list.d/" >&2; exit 1; }; \
    : > /tmp/autopilot-final-apt-uris; \
    for source_file in /etc/apt/sources.list /etc/apt/sources.list.d/*.list /etc/apt/sources.list.d/*.sources; do \
        [[ -f "$source_file" && ! -L "$source_file" ]] || continue; \
        awk ' \
            { lower = tolower($0) } \
            /^[[:space:]]*#/ { collecting = 0; next } \
            match(lower, /(^|[^a-z0-9._+-])(trusted|allow-insecure|allow-weak|allow-downgrade-to-insecure)[[:space:]]*[:=][[:space:]]*[a-z0-9]*/) { \
                option = substr(lower, RSTART, RLENGTH); \
                sub(/^[^a-z]*/, "", option); \
                sub(/^[a-z-]+[[:space:]]*[:=][[:space:]]*/, "", option); \
                if (option != "no" && option != "false" && option != "0") { \
                    print "Trust-bypassing apt source option in " FILENAME ": " $0 > "/dev/stderr"; exit 1 \
                } \
            } \
            /^[[:space:]]*deb(-src)?[[:space:]]/ { \
                line = $0; \
                sub(/^[[:space:]]*deb(-src)?[[:space:]]+/, "", line); \
                sub(/^\[[^]]*\][[:space:]]+/, "", line); \
                split(line, fields, /[[:space:]]+/); \
                if (fields[1] != "") print fields[1]; \
                collecting = 0; next \
            } \
            /^[[:space:]]*[Uu][Rr][Ii][Ss]:/ { \
                line = $0; \
                sub(/^[[:space:]]*[Uu][Rr][Ii][Ss]:[[:space:]]*/, "", line); \
                count = split(line, values, /[[:space:]]+/); \
                for (i = 1; i <= count; i++) if (values[i] != "") print values[i]; \
                collecting = 1; next \
            } \
            collecting && /^[[:space:]]+[^[:space:]]/ { \
                line = $0; \
                sub(/^[[:space:]]+/, "", line); \
                count = split(line, values, /[[:space:]]+/); \
                for (i = 1; i <= count; i++) if (values[i] != "") print values[i]; \
                next \
            } \
            { collecting = 0 } \
        ' "$source_file" >> /tmp/autopilot-final-apt-uris; \
    done; \
    LC_ALL=C sort -u /tmp/autopilot-final-apt-uris \
        > /usr/local/share/autopilot/provenance/final-apt-sources.txt; \
    rm -f /tmp/autopilot-final-apt-uris; \
    test -s /usr/local/share/autopilot/provenance/final-apt-sources.txt; \
    awk '!/^https?:\/\/[^[:space:]]+$/ { print "Unsupported final apt source URI: " $0 > "/dev/stderr"; exit 1 }' \
        /usr/local/share/autopilot/provenance/final-apt-sources.txt; \
    dpkg-query -W -f='${db:Status-Status}\t${binary:Package}\t${Version}\t${Architecture}\n' \
        | awk -F '\t' '$1 == "installed" { print $2 "\t" $3 "\t" $4 }' \
        | LC_ALL=C sort -u > /usr/local/share/autopilot/provenance/final-packages.tsv; \
    test -s /usr/local/share/autopilot/provenance/final-packages.tsv; \
    npm ls -g --depth 0 --json > /usr/local/share/autopilot/provenance/final-npm-globals.json; \
    test -s /usr/local/share/autopilot/provenance/final-npm-globals.json; \
    chown root:root /usr/local/share/autopilot/provenance/final-apt-sources.txt \
        /usr/local/share/autopilot/provenance/final-packages.tsv \
        /usr/local/share/autopilot/provenance/final-npm-globals.json; \
    chmod 0644 /usr/local/share/autopilot/provenance/final-apt-sources.txt \
        /usr/local/share/autopilot/provenance/final-packages.tsv \
        /usr/local/share/autopilot/provenance/final-npm-globals.json

USER autopilot
WORKDIR /work

# Verify tools are installed
RUN git --version && pwsh --version && gh --version && copilot --version
