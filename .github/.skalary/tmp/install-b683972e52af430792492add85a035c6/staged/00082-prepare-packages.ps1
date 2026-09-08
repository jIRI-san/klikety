<#
.SYNOPSIS
    Host-side offline package feed builder for autopilot container/sandbox runtimes.
.DESCRIPTION
    Pre-downloads packages on the host (which already has private-stream auth) into
    a per-repo/per-branch local folder feed. The container/sandbox mounts that feed
    read-only, copies it to a writable cache, and restores from it fully offline, so
    a private package stream is resolved once on the host and never needs credentials
    inside the disposable runtime.

    Feed layout: <FeedRoot>/<repo-leaf>/<branch>/{nuget,npm}
      * nuget = a NuGet global-packages folder (dotnet restore --packages)
      * npm   = an npm cache (cacache)

    This file is dot-sourceable: it defines Invoke-PreparePackages (and helpers)
    without side effects so tests can mock dotnet/npm/git. It executes only when run
    as a script.
.PARAMETER RepoRoot
    Repository root. Defaults to 'git rev-parse --show-toplevel'.
.PARAMETER Branch
    Work branch for rebundle mode. When set, fetch that branch into a temp worktree,
    run an UNLOCKED restore that regenerates the lockfile and repopulates the feed,
    then commit + push the regenerated lockfile so the relaunched runtime clones a
    consistent manifest+lock pair.
.PARAMETER Ecosystems
    Restrict to these ecosystems ('dotnet'/'npm'). Omitted = auto-detect.
.PARAMETER FeedRoot
    Feed root directory. Defaults to $env:LOCALAPPDATA/autopilot-package-feed.
.OUTPUTS
    [string] Path to the per-repo/per-branch feed root (contains nuget/ and npm/).
#>
param(
    [string]$RepoRoot,
    [string]$Branch,
    [string[]]$Ecosystems,
    [string]$FeedRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function ConvertTo-FeedSegment {
    param([Parameter(Mandatory)][string]$Value)

    $trimmed = $Value.Trim()
    if ([string]::IsNullOrWhiteSpace($trimmed)) {
        throw "Feed path segment cannot be empty."
    }
    # Collapse anything outside the safe set to '-', so a branch like 'feature/foo'
    # or a repo leaf with spaces can never introduce a path separator and escape
    # the feed root.
    $sanitized = [regex]::Replace($trimmed, '[^A-Za-z0-9._-]', '-')
    # Defense in depth: reject traversal even after sanitization.
    if ($sanitized -eq '.' -or $sanitized -eq '..' -or $sanitized -match '\.\.') {
        throw "Refusing unsafe feed path segment '$Value'."
    }
    return $sanitized
}

function Assert-PathUnder {
    param(
        [Parameter(Mandatory)][string]$Base,
        [Parameter(Mandatory)][string]$Path
    )

    $fullBase = [System.IO.Path]::GetFullPath($Base)
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $sep = [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullBase.EndsWith($sep)) { $fullBase += $sep }
    if (-not ($fullPath + $sep).StartsWith($fullBase, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Computed feed path '$fullPath' escapes feed root '$fullBase'."
    }
}

function Get-DetectedEcosystem {
    param(
        [Parameter(Mandatory)][string]$Root,
        [string[]]$Override
    )

    if ($Override) {
        foreach ($e in $Override) {
            if ($e -notin @('dotnet', 'npm')) {
                throw "Unknown ecosystem '$e' (expected 'dotnet' or 'npm')."
            }
        }
        return @($Override | Select-Object -Unique)
    }

    $found = [System.Collections.Generic.List[string]]::new()
    $proj = Get-ChildItem -LiteralPath $Root -Recurse -File -Include *.csproj, *.sln -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($proj) { $found.Add('dotnet') }
    if (Test-Path -LiteralPath (Join-Path $Root 'package.json')) { $found.Add('npm') }
    return @($found)
}

function Assert-Lockfile {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Ecosystem
    )

    switch ($Ecosystem) {
        'dotnet' {
            $lock = Get-ChildItem -LiteralPath $Root -Recurse -File -Filter 'packages.lock.json' -ErrorAction SilentlyContinue |
                Select-Object -First 1
            if (-not $lock) {
                throw "Offline dotnet restore requires a committed packages.lock.json. Enable RestorePackagesWithLockFile and commit the lockfile so host and runtime resolve identically."
            }
        }
        'npm' {
            if (-not (Test-Path -LiteralPath (Join-Path $Root 'package-lock.json'))) {
                throw "Offline npm restore requires a committed package-lock.json. Run 'npm install' and commit the lockfile."
            }
        }
    }
}

function Invoke-DotnetRestore {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$NugetFeed,
        [switch]$Locked
    )

    $restoreArgs = @('restore', '--packages', $NugetFeed)
    if ($Locked) { $restoreArgs += '--locked-mode' }
    Push-Location $Root
    try {
        & dotnet @restoreArgs
        if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed (exit $LASTEXITCODE)." }
    }
    finally {
        Pop-Location
    }
}

function Invoke-NpmRestore {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$NpmCache,
        [switch]$Locked,
        [switch]$InPlace
    )

    if ($InPlace) {
        # Restore directly in $Root (a throwaway worktree) so the regenerated
        # package-lock.json lands there and can be committed.
        Push-Location $Root
        try {
            & npm install --cache $NpmCache --no-audit --no-fund --ignore-scripts
            if ($LASTEXITCODE -ne 0) { throw "npm install failed (exit $LASTEXITCODE)." }
        }
        finally {
            Pop-Location
        }
        return
    }

    # Populate the cache without mutating the repo working tree: copy the manifest
    # and lockfile into a throwaway dir and restore there. The cache (cacache) is
    # what the runtime reuses offline; node_modules in the temp dir is discarded.
    $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("autopilot-npm-" + [System.IO.Path]::GetRandomFileName())
    New-Item -ItemType Directory -Path $tmp -Force | Out-Null
    try {
        Copy-Item -LiteralPath (Join-Path $Root 'package.json') -Destination $tmp
        Copy-Item -LiteralPath (Join-Path $Root 'package-lock.json') -Destination $tmp
        Push-Location $tmp
        try {
            if ($Locked) {
                & npm ci --cache $NpmCache --no-audit --no-fund --ignore-scripts
            }
            else {
                & npm install --cache $NpmCache --no-audit --no-fund --ignore-scripts
            }
            if ($LASTEXITCODE -ne 0) { throw "npm restore failed (exit $LASTEXITCODE)." }
        }
        finally {
            Pop-Location
        }
    }
    finally {
        Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-RebundleMode {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$Branch,
        [Parameter(Mandatory)][string]$FeedRoot,
        [Parameter(Mandatory)][string]$RepoLeaf,
        [string[]]$Ecosystems
    )

    # Validate the remote ref exists before doing any work.
    & git -C $RepoRoot ls-remote --exit-code --heads origin $Branch 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Rebundle branch 'origin/$Branch' not found. Push the rebundle-request commit before re-preparing the feed."
    }

    & git -C $RepoRoot fetch origin $Branch
    if ($LASTEXITCODE -ne 0) { throw "git fetch origin $Branch failed (exit $LASTEXITCODE)." }

    $branchSeg = ConvertTo-FeedSegment $Branch
    $feed = Join-Path (Join-Path $FeedRoot $RepoLeaf) $branchSeg
    Assert-PathUnder -Base $FeedRoot -Path $feed

    $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("autopilot-rebundle-" + [System.IO.Path]::GetRandomFileName())
    & git -C $RepoRoot worktree add --detach $tmp FETCH_HEAD
    if ($LASTEXITCODE -ne 0) { throw "git worktree add failed (exit $LASTEXITCODE)." }

    try {
        $detected = Get-DetectedEcosystem -Root $tmp -Override $Ecosystems
        if (-not $detected) {
            Write-Host "No dotnet/npm ecosystems detected in '$Branch'; nothing to rebundle."
            return $feed
        }

        # UNLOCKED restore regenerates the lockfile (the offline agent could not) and
        # repopulates the branch-scoped feed.
        if ('dotnet' -in $detected) {
            $nuget = Join-Path $feed 'nuget'
            New-Item -ItemType Directory -Path $nuget -Force | Out-Null
            Invoke-DotnetRestore -Root $tmp -NugetFeed $nuget
        }
        if ('npm' -in $detected) {
            $npm = Join-Path $feed 'npm'
            New-Item -ItemType Directory -Path $npm -Force | Out-Null
            Invoke-NpmRestore -Root $tmp -NpmCache $npm -InPlace
        }

        # Stage only regenerated lockfiles; if any changed, commit + push so the
        # relaunched runtime clones a consistent manifest+lock pair.
        & git -C $tmp add ':(glob)**/packages.lock.json' ':(glob)**/package-lock.json' 2>&1 | Out-Null
        $staged = & git -C $tmp diff --cached --name-only
        if ($LASTEXITCODE -ne 0) { throw "git diff --cached failed (exit $LASTEXITCODE)." }
        if ($staged) {
            & git -C $tmp commit -m 'autopilot: rebundle lockfile' | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "git commit (rebundle lockfile) failed (exit $LASTEXITCODE)." }
            & git -C $tmp push origin "HEAD:$Branch"
            if ($LASTEXITCODE -ne 0) { throw "git push origin HEAD:$Branch failed (exit $LASTEXITCODE)." }
            Write-Host "Rebundled feed + pushed regenerated lockfile to origin/$Branch."
        }
        else {
            Write-Host "Rebundle restore produced no lockfile changes (feed repopulated)."
        }

        return $feed
    }
    finally {
        & git -C $RepoRoot worktree remove --force $tmp 2>&1 | Out-Null
    }
}

function Invoke-PreparePackages {
    param(
        [string]$RepoRoot,
        [string]$Branch,
        [string[]]$Ecosystems,
        [string]$FeedRoot
    )

    if (-not $RepoRoot) {
        $RepoRoot = (git rev-parse --show-toplevel 2>$null | Select-Object -First 1)
        if (-not $RepoRoot) { throw "Failed to resolve repository root via git." }
        $RepoRoot = $RepoRoot.Trim()
    }
    if (-not $FeedRoot) {
        $FeedRoot = Join-Path $env:LOCALAPPDATA 'autopilot-package-feed'
    }

    $repoLeaf = ConvertTo-FeedSegment (Split-Path $RepoRoot -Leaf)

    if ($Branch) {
        return Invoke-RebundleMode -RepoRoot $RepoRoot -Branch $Branch -FeedRoot $FeedRoot -RepoLeaf $repoLeaf -Ecosystems $Ecosystems
    }

    $currentBranch = (git -C $RepoRoot rev-parse --abbrev-ref HEAD 2>$null | Select-Object -First 1)
    if (-not $currentBranch) { throw "Failed to resolve current branch via git." }
    $branchSeg = ConvertTo-FeedSegment ($currentBranch.Trim())

    $feed = Join-Path (Join-Path $FeedRoot $repoLeaf) $branchSeg
    Assert-PathUnder -Base $FeedRoot -Path $feed

    $detected = Get-DetectedEcosystem -Root $RepoRoot -Override $Ecosystems
    if (-not $detected) {
        Write-Host "No dotnet/npm ecosystems detected; nothing to bundle."
        return $feed
    }

    foreach ($eco in $detected) { Assert-Lockfile -Root $RepoRoot -Ecosystem $eco }

    if ('dotnet' -in $detected) {
        $nuget = Join-Path $feed 'nuget'
        New-Item -ItemType Directory -Path $nuget -Force | Out-Null
        Invoke-DotnetRestore -Root $RepoRoot -NugetFeed $nuget -Locked
    }
    if ('npm' -in $detected) {
        $npm = Join-Path $feed 'npm'
        New-Item -ItemType Directory -Path $npm -Force | Out-Null
        Invoke-NpmRestore -Root $RepoRoot -NpmCache $npm -Locked
    }

    Write-Host "Feed ready: $feed"
    return $feed
}

# Execute only when run as a script (not when dot-sourced for testing).
if ($MyInvocation.InvocationName -ne '.') {
    Invoke-PreparePackages @PSBoundParameters
}
