#requires -Version 7.0
<#
.SYNOPSIS
    Moves one completed plan and all its assets into the canonical archive.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory, Position = 0)]
    [Alias('PlanReference')]
    [string]$Plan,

    [string]$RepoRoot = (Get-Location).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'PlanState.psm1') -Force -DisableNameChecking

$root = [System.IO.Path]::GetFullPath($RepoRoot)
$corpus = New-PlanCorpusConfinementContext -RepoRoot $root
$resolved = Resolve-Plan -Reference $Plan -RepoRoot $root
$source = [string]$resolved.Path
[void](New-PlanConfinementContext -PlanDir $source -CorpusContext $corpus)
$planFile = Join-Path $source 'plan.md'

$pending = [System.Collections.Generic.Stack[string]]::new()
$pending.Push($source)
while ($pending.Count -gt 0) {
    foreach ($item in @(Get-ChildItem -LiteralPath $pending.Pop() -Force)) {
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Cannot archive plan '$($resolved.Id)': link or reparse point '$($item.FullName)'."
        }
        if ($item.PSIsContainer) { $pending.Push($item.FullName) }
    }
}

if ($resolved.IsArchived) {
    return [pscustomobject]@{
        Status = 'already-archived'
        PlanId = $resolved.Id
        Path = $source
        PlanFile = $planFile
    }
}

$progress = Get-PlanProgress -Path $planFile -RepoRoot $root
if (-not $progress.IsComplete) {
    throw "Cannot archive plan '$($resolved.Id)': incomplete ($($progress.Completed)/$($progress.Total) steps complete)."
}

$archiveRoot = Join-Path $corpus.PlansPath 'archived'
$destination = Join-Path $archiveRoot $resolved.FolderName
if (Test-Path -LiteralPath $archiveRoot) {
    [void](New-PlanConfinementContext -PlanDir $archiveRoot -CorpusContext $corpus)
}
if (Test-Path -LiteralPath $destination) {
    throw "Cannot archive plan '$($resolved.Id)': destination already exists at '$destination'."
}

$status = if ($WhatIfPreference) { 'what-if' } else { 'declined' }
if ($PSCmdlet.ShouldProcess($source, "Move completed plan to '$destination'")) {
    if (-not (Test-Path -LiteralPath $archiveRoot -PathType Container)) {
        [void](New-Item -ItemType Directory -Path $archiveRoot)
    }
    [void](New-PlanConfinementContext -PlanDir $archiveRoot -CorpusContext $corpus)
    [System.IO.Directory]::Move($source, $destination)
    $status = 'archived'
}

[pscustomobject]@{
    Status = $status
    PlanId = $resolved.Id
    Path = $destination
    PlanFile = Join-Path $destination 'plan.md'
}
