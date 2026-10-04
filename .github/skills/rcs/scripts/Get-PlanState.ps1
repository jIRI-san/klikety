#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [string]$Reference,

    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path,

    [switch]$HasUncommittedChanges,

    [switch]$Epic,

    [switch]$Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'PlanState.psm1') -Force -DisableNameChecking

$repoRootPath = [System.IO.Path]::GetFullPath($RepoRoot)
$inventory = @(Get-PlanInventory -RepoRoot $repoRootPath)

# A plan reference keeps winning: `Resolve-Plan` and `Resolve-Epic` both accept fuzzy date and slug
# forms, so preferring the epic side would break references that used to resolve to a plan. Hex ids are
# unique across both spaces (New-Plan and New-Epic collision-check each other), so `/ci <epic-id>` still
# lands in epic mode; `-Epic` forces the epic side for the fuzzy forms.
$resolvedEpic = $null
$plan = $null
if ($Epic) {
    $resolvedEpic = Resolve-Epic -Reference $Reference -RepoRoot $repoRootPath
}
else {
    try {
        $plan = Resolve-Plan -Reference $Reference -RepoRoot $repoRootPath -Inventory $inventory
    }
    catch {
        # An ambiguous plan reference is a real error and must not fall through to epic resolution.
        if ($_.Exception.Message -like 'Ambiguous plan reference*') { throw }
        $plan = $null
    }
    if (-not $plan) {
        $resolvedEpic = Resolve-Epic -Reference $Reference -RepoRoot $repoRootPath
    }
}

if ($resolvedEpic) {
    $rollup = Get-EpicRollup -EpicId $resolvedEpic.Id -RepoRoot $repoRootPath `
        -Inventory $inventory

    $epicState = [pscustomobject]@{
        Kind       = 'epic'
        EpicId     = $resolvedEpic.Id
        Reference  = $Reference
        Title      = $resolvedEpic.Title
        FolderName = $resolvedEpic.FolderName
        EpicFile   = $resolvedEpic.EpicFile
        IsArchived = $resolvedEpic.IsArchived
        Rollup     = [pscustomobject]@{
            ChildCount     = $rollup.ChildCount
            CompleteCount  = $rollup.CompleteCount
            BlockedCount   = $rollup.BlockedCount
            TotalSteps     = $rollup.TotalSteps
            CompletedSteps = $rollup.CompletedSteps
            Percent        = $rollup.Percent
            IsComplete     = $rollup.IsComplete
        }
        Children   = @($rollup.Children | ForEach-Object {
            [pscustomobject]@{
                Id               = $_.Id
                Slug             = $_.Slug
                FolderName       = $_.FolderName
                PlanFile         = $_.PlanFile
                IsArchived       = $_.IsArchived
                IsComplete       = $_.IsComplete
                IsBlocked        = $_.IsBlocked
                DependsOn        = @($_.DependsOn)
                UnmetDependsOn   = @($_.UnmetDependsOn)
                UnknownDependsOn = @($_.UnknownDependsOn)
                Completed        = $_.Progress.Completed
                Total            = $_.Progress.Total
                Percent          = $_.Progress.Percent
                NextStepId       = $_.NextStepId
            }
        })
        NextChild  = if ($rollup.NextChild) {
            [pscustomobject]@{
                Id         = $rollup.NextChild.Id
                Slug       = $rollup.NextChild.Slug
                FolderName = $rollup.NextChild.FolderName
                PlanFile   = $rollup.NextChild.PlanFile
                NextStepId = $rollup.NextChild.NextStepId
            }
        }
        else { $null }
    }

    if ($Json) {
        return ($epicState | ConvertTo-Json -Depth 6)
    }

    $epicLines = [System.Collections.Generic.List[string]]::new()
    $archiveLabel = if ($epicState.IsArchived) { ' (archived)' } else { '' }
    $epicLines.Add("Epic:       $($epicState.EpicId)  ($($epicState.FolderName))$archiveLabel")
    $epicLines.Add("Children:   $($rollup.CompleteCount)/$($rollup.ChildCount) complete  blocked=$($rollup.BlockedCount)")
    $epicLines.Add("Steps:      $($rollup.CompletedSteps)/$($rollup.TotalSteps) done ($($rollup.Percent)%)")
    foreach ($child in $rollup.Children) {
        $mark = if ($child.IsComplete) { 'x' } elseif ($child.IsBlocked) { '!' } else { ' ' }
        $flags = @()
        if ($child.IsArchived) { $flags += 'archived' }
        if ($child.UnmetDependsOn.Count -gt 0) { $flags += "unmet: $($child.UnmetDependsOn -join ', ')" }
        if ($child.UnknownDependsOn.Count -gt 0) { $flags += "unresolvable depends-on: $($child.UnknownDependsOn -join ', ')" }
        if (-not $child.IsComplete -and $child.NextStepId) { $flags += "next step $($child.NextStepId)" }
        $flagText = if ($flags.Count -gt 0) { "  [$($flags -join '; ')]" } else { '' }
        $epicLines.Add("  [$mark] $($child.Id) $($child.Slug)  $($child.Progress.Completed)/$($child.Progress.Total)$flagText")
    }
    if ($epicState.NextChild) {
        $epicLines.Add("Next child: $($epicState.NextChild.Id) ($($epicState.NextChild.FolderName))  next step $($epicState.NextChild.NextStepId)")
    }
    elseif ($rollup.IsComplete) {
        $epicLines.Add('Next child: (none — every child plan is complete)')
    }
    else {
        $epicLines.Add('Next child: (none unblocked — resolve a dependency above)')
    }

    return ($epicLines -join [Environment]::NewLine)
}

$planFile = Join-Path $plan.Path 'plan.md'
if (-not (Test-Path -LiteralPath $planFile -PathType Leaf)) {
    throw "Resolved plan '$($plan.Id)' has no plan.md at $planFile."
}

$metadata = Get-PlanMetadata -Path $planFile -RepoRoot $repoRootPath
$markers = Get-PlanHeaderMarkers -Path $planFile
$progress = Get-PlanProgress -Metadata $metadata

if ($PSBoundParameters.ContainsKey('HasUncommittedChanges')) {
    $dirty = [bool]$HasUncommittedChanges
}
else {
    try {
        $status = & git -C $repoRootPath status --porcelain 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "git status failed: $(($status -join ' ').Trim())"
        }
        $dirty = -not [string]::IsNullOrWhiteSpace(($status -join ''))
    }
    catch {
        throw "Unable to inspect the repository worktree: $($_.Exception.Message)"
    }
}

$next = Get-NextStep -Metadata $metadata -HasUncommittedChanges:$dirty
$planningContext = Get-PlanningContextState -PlanDir $plan.Path -RepoRoot $repoRootPath `
    -Inventory $inventory
$admission = Get-PhaseAdmission -Plan $plan -Metadata $metadata -Markers $markers `
    -NextStep $next -PlanningContext $planningContext -Inventory $inventory -RepoRoot $repoRootPath

$state = [pscustomobject]@{
    Kind                  = 'plan'
    PlanId                = if ($markers.PlanId) { $markers.PlanId } else { $plan.Id }
    Reference             = $Reference
    FolderName            = $plan.FolderName
    PlanFile              = $planFile
    Scheme                = $plan.Scheme
    IsArchived            = $plan.IsArchived
    HasUncommittedChanges = $dirty
    Markers               = [pscustomobject]@{
        EpicId        = $markers.EpicId
        ExecutionMode = $markers.ExecutionMode
        Scope         = $markers.Scope
        CipStage      = $markers.CipStage
        PlanningConfirmed = $markers.PlanningConfirmed
        DependsOn     = @($markers.DependsOn)
    }
    PlanningContext       = $planningContext
    Admission             = $admission
    Progress              = [pscustomobject]@{
        Total         = $progress.Total
        Completed     = $progress.Completed
        InProgress    = $progress.InProgress
        Pending       = $progress.Pending
        Percent       = $progress.Percent
        CurrentPhase  = $progress.CurrentPhase
        LastCompleted = $progress.LastCompleted
        IsComplete    = $progress.IsComplete
    }
    NextStep              = [pscustomobject]@{
        Id             = $next.Id
        Status         = $next.Status
        IsHuman        = $next.IsHuman
        IsDiscovery    = $next.IsDiscovery
        Detail         = $next.Detail
        BlockedByAfter = $next.BlockedByAfter
        UnmetAfter     = @($next.UnmetAfter)
        IsComplete     = $next.IsComplete
    }
}

if ($Json) {
    return ($state | ConvertTo-Json -Depth 6)
}

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("Plan:       $($state.PlanId)  ($($state.FolderName))")
$lines.Add("Scheme:     $($state.Scheme)$(if ($state.IsArchived) { ' (archived)' } else { '' })")
$lines.Add("Mode:       $($state.Markers.ExecutionMode)   Scope: $($state.Markers.Scope)   Stage: $($state.Markers.CipStage)")
$lines.Add("Context:    $($state.PlanningContext.Status)")
$lines.Add("Admission:  $($state.Admission.Status)$(if ($state.Admission.Reason) { " — $($state.Admission.Reason)" } else { '' })")
if ($state.Admission.ApplicableRequirements.Count -gt 0) {
    $lines.Add("Phase REQs: $($state.Admission.ApplicableRequirements -join ', ')")
}
if ($state.Markers.EpicId) {
    $lines.Add("Epic:       $($state.Markers.EpicId)  (run Get-PlanState $($state.Markers.EpicId) for the rollup)")
}
if ($state.Markers.DependsOn.Count -gt 0) {
    $lines.Add("Depends-on: $($state.Markers.DependsOn -join ', ')")
}
$lines.Add("Progress:   $($state.Progress.Completed)/$($state.Progress.Total) done ($($state.Progress.Percent)%)  in-progress=$($state.Progress.InProgress)  pending=$($state.Progress.Pending)")
$lines.Add("Phase:      $($state.Progress.CurrentPhase)")
$lines.Add("Last done:  $(if ($state.Progress.LastCompleted) { $state.Progress.LastCompleted } else { '(none)' })")
$lines.Add("Dirty tree: $($state.HasUncommittedChanges)")
if ($state.NextStep.IsComplete) {
    $lines.Add("Next step:  (none — all steps complete)")
}
else {
    $flags = @()
    if ($state.NextStep.IsHuman) { $flags += '@human' }
    if ($state.NextStep.IsDiscovery) { $flags += '[discovery]' }
    if ($state.NextStep.BlockedByAfter) { $flags += "blocked-by-after: $($state.NextStep.UnmetAfter -join ', ')" }
    $flagText = if ($flags.Count -gt 0) { "  [$($flags -join '; ')]" } else { '' }
    $lines.Add("Next step:  $($state.NextStep.Id) (status '$($state.NextStep.Status)')$flagText")

    # An @human step is an operator handoff: print its full detail block, not just the title, so the
    # round-trip is single-pass (the same block the human-step-detail gate requires).
    if ($state.NextStep.IsHuman) {
        $lines.Add('Handoff:')
        if ([string]::IsNullOrWhiteSpace($state.NextStep.Detail)) {
            $lines.Add('  (no <details> block on this step — it does not satisfy the human-step-detail gate)')
        }
        else {
            foreach ($detailLine in ($state.NextStep.Detail -replace "`r`n", "`n").Split("`n")) {
                $lines.Add("  $detailLine".TrimEnd())
            }
        }
    }
}

return ($lines -join [Environment]::NewLine)
