#requires -Version 7.0
<#
.SYNOPSIS
    Archives one completed epic index.
.DESCRIPTION
    Refreshes the epic's generated child table, verifies every child plan is archived, then moves the
    epic folder from `docs/implementation-plans/epics/` to
    `docs/implementation-plans/archived/epics/`. Repeating the command for an already archived epic is
    a successful no-op.
.EXAMPLE
    pwsh -NoProfile -File scripts/skalary/Archive-Epic.ps1 705e6c
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory, Position = 0)]
    [string]$Epic,

    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'PlanState.psm1') -Force -DisableNameChecking

$repoRootPath = [System.IO.Path]::GetFullPath($RepoRoot)
$plansRoot = Join-Path $repoRootPath 'docs/implementation-plans'
$activeRoot = Join-Path $plansRoot 'epics'
$archiveRoot = Join-Path $plansRoot 'archived/epics'
$comparison = if ($IsWindows) {
    [System.StringComparison]::OrdinalIgnoreCase
}
else {
    [System.StringComparison]::Ordinal
}

$epicInventory = @(Get-EpicInventory -RepoRoot $repoRootPath)
$exactActiveMatches = @(
    $epicInventory | Where-Object {
        -not $_.IsArchived -and
        $_.Id -and
        [string]::Equals($_.Id, $Epic.Trim(), [System.StringComparison]::OrdinalIgnoreCase)
    }
)
$resolved = if ($exactActiveMatches.Count -eq 1) {
    $exactActiveMatches[0]
}
else {
    Resolve-Epic -Reference $Epic -RepoRoot $repoRootPath -Inventory $epicInventory
}
if ($resolved.IsArchived) {
    return [pscustomobject]@{
        Status = 'already-archived'
        EpicId = $resolved.Id
        Path = $resolved.Path
        EpicFile = $resolved.EpicFile
    }
}

$sourcePath = [System.IO.Path]::GetFullPath([string]$resolved.Path)
$sourceParent = [System.IO.Path]::GetFullPath((Split-Path -Parent $sourcePath))
$expectedActiveRoot = [System.IO.Path]::GetFullPath($activeRoot)
if (-not [string]::Equals($sourceParent, $expectedActiveRoot, $comparison)) {
    throw "Resolved epic '$($resolved.Id)' is outside the active epic root '$expectedActiveRoot'."
}
if (-not (Test-Path -LiteralPath $resolved.EpicFile -PathType Leaf)) {
    throw "Epic '$($resolved.Id)' has no epic.md at $($resolved.EpicFile)."
}
if (-not [string]::Equals(
        (Resolve-PhysicalRepoPath -Path $sourcePath),
        $sourcePath,
        $comparison
    )) {
    throw "Active epic '$($resolved.Id)' resolves through a link or reparse point."
}

$destinationPath = [System.IO.Path]::GetFullPath(
    (Join-Path $archiveRoot $resolved.FolderName)
)
if (Test-Path -LiteralPath $destinationPath) {
    throw "Cannot archive epic '$($resolved.Id)': destination already exists at '$destinationPath'."
}

$rollup = Get-EpicRollup -EpicId $resolved.Id -RepoRoot $repoRootPath
if (-not $rollup.IsComplete) {
    throw "Epic '$($resolved.Id)' is incomplete ($($rollup.CompleteCount)/$($rollup.ChildCount) children complete)."
}
$unarchivedChildren = @($rollup.Children | Where-Object { -not $_.IsArchived })
if ($unarchivedChildren.Count -gt 0) {
    throw "Epic '$($resolved.Id)' cannot be archived until every child plan is archived: $(
        ($unarchivedChildren.Id | Sort-Object) -join ', '
    )."
}

$refreshCandidates = @((Join-Path $PSScriptRoot 'New-Epic.ps1'))
$refreshScript = @(
    $refreshCandidates |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1
)
if ($refreshScript.Count -ne 1) {
    throw "Cannot archive epic '$($resolved.Id)': New-Epic.ps1 is unavailable for child-table refresh."
}

& $refreshScript[0] -Epic $resolved.Id -RepoRoot $repoRootPath | Out-Null

if ($PSCmdlet.ShouldProcess($sourcePath, "Move completed epic to '$destinationPath'")) {
    if (-not (Test-Path -LiteralPath $archiveRoot -PathType Container)) {
        [void](New-Item -ItemType Directory -Path $archiveRoot -Force)
    }
    if (-not [string]::Equals(
            (Resolve-PhysicalRepoPath -Path $archiveRoot),
            [System.IO.Path]::GetFullPath($archiveRoot),
            $comparison
        )) {
        throw "Epic archive root '$archiveRoot' resolves through a link or reparse point."
    }

    [System.IO.Directory]::Move($sourcePath, $destinationPath)
}

return [pscustomobject]@{
    Status = if ($WhatIfPreference) { 'what-if' } else { 'archived' }
    EpicId = $resolved.Id
    Path = $destinationPath
    EpicFile = Join-Path $destinationPath 'epic.md'
}
