#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PlanPath,

    [Parameter(Mandatory)]
    [ValidateRange(0, 999)]
    [int]$Phase,

    [string]$RepoRoot = (git rev-parse --show-toplevel)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

try {
    $repoRootFull = [System.IO.Path]::GetFullPath($RepoRoot)
    $planPathFull = [System.IO.Path]::GetFullPath(
        $(if ([System.IO.Path]::IsPathRooted($PlanPath)) {
                $PlanPath
            }
            else {
                Join-Path $repoRootFull $PlanPath
            })
    )
    if (-not (Test-Path -LiteralPath $planPathFull -PathType Leaf)) {
        throw "Plan file not found: $planPathFull"
    }
    $planDirFull = Split-Path -Parent $planPathFull
    Import-Module (Join-Path $PSScriptRoot 'PlanState.psm1') -Force -DisableNameChecking
    $metadata = Get-PlanMetadata -Path $planPathFull -RepoRoot $repoRootFull
    $phaseHeading = @(
        $metadata.PhaseSteps.Keys |
            Where-Object { [string]$_ -match "^##\s+Phase\s+$Phase(?:\D|$)" }
    )
    if ($phaseHeading.Count -ne 1) {
        throw "Plan must contain exactly one Phase $Phase heading."
    }

    $steps = @($metadata.PhaseSteps[$phaseHeading[0]])
    if ($steps.Count -eq 0) {
        throw "Phase $Phase contains no executable steps."
    }
    if (@($steps | Where-Object { [string]$_.Status -ne 'x' }).Count -gt 0) {
        $inventory = @(Get-PlanInventory -RepoRoot $repoRootFull)
        $markers = Get-PlanHeaderMarkers -Path $planPathFull
        $pathComparison = if ($IsWindows) {
            [System.StringComparison]::OrdinalIgnoreCase
        }
        else {
            [System.StringComparison]::Ordinal
        }
        $planMatches = @($inventory | Where-Object {
                $_.Path -and [string]::Equals(
                    [System.IO.Path]::GetFullPath([string]$_.Path),
                    $planDirFull,
                    $pathComparison
                )
            })
        if ($planMatches.Count -ne 1) {
            throw "Plan path '$planDirFull' is not a unique member of the repository plan inventory."
        }
        $plan = $planMatches[0]
        $gitStatus = & git -C $repoRootFull status --porcelain 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to inspect the repository worktree: $(($gitStatus -join ' ').Trim())"
        }
        $next = Get-NextStep -Metadata $metadata `
            -HasUncommittedChanges:(-not [string]::IsNullOrWhiteSpace(($gitStatus -join '')))
        $planningContext = Get-PlanningContextState -PlanDir $plan.Path -RepoRoot $repoRootFull `
            -Inventory $inventory
        $admission = Get-PhaseAdmission -Plan $plan -Metadata $metadata -Markers $markers `
            -NextStep $next -PlanningContext $planningContext -Inventory $inventory `
            -RepoRoot $repoRootFull
        if (-not $admission.CanProceed -or $admission.PhaseNumber -ne $Phase) {
            $reason = if ([string]::IsNullOrWhiteSpace([string]$admission.Reason)) {
                "Canonical admission selected phase $($admission.PhaseNumber)."
            }
            else {
                [string]$admission.Reason
            }
            throw "Phase $Phase is not admitted: $reason"
        }
        Write-Output 'execution-required'
        return
    }

    $relativePath = (& git -C $repoRootFull ls-files --full-name --error-unmatch -- $planPathFull 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($relativePath)) {
        Write-Output 'close-pending'
        return
    }
    $pathStatus = (& git -C $repoRootFull status --porcelain --untracked-files=all -- $planPathFull)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to inspect committed phase-close path '$planPathFull'."
    }
    if (-not [string]::IsNullOrWhiteSpace(($pathStatus -join "`n"))) {
        Write-Output 'close-pending'
        return
    }

    Write-Output 'closed'
    return
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 2
}
