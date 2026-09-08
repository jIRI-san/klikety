#requires -Version 7.0
[CmdletBinding(DefaultParameterSetName = 'ValidatePlan')]
param(
    [Parameter(Mandatory, ParameterSetName = 'ValidatePlan')]
    [string]$PlanPath,

    [Parameter(ParameterSetName = 'ValidatePlan')]
    [Parameter(ParameterSetName = 'VerifyEvidence')]
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path,

    [Parameter(ParameterSetName = 'ValidatePlan')]
    [ValidateSet('Draft', 'PhaseCrosscheck', 'PlanCrosscheck')]
    [string]$Stage = 'Draft',

    [Parameter(Mandatory, ParameterSetName = 'VerifyEvidence')]
    [string]$EvidenceMarker,

    [Parameter(ParameterSetName = 'VerifyEvidence')]
    [ValidateSet('Draft', 'PhaseCrosscheck', 'PlanCrosscheck')]
    [string]$EvidenceStage = 'PhaseCrosscheck'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'DirectWorkflow.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'PlanState.psm1') -Force -DisableNameChecking

# Get-TypedEvidenceMarkers now lives in PlanState.psm1 (the shared parser) so the closed vocabulary and its
# fail-loud-on-unknown-prefix behavior are single-sourced; it is imported below and called by this validator.

function Get-StepPoints {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Size
    )

    switch ($Size) {
        'S' { return 1 }
        'M' { return 2 }
        'L' { return 3 }
        default { return 0 }
    }
}

function Get-HumanStepDetailGap {
    <#
    .SYNOPSIS
    Reports what the `human-step-detail` gate is missing for one `@human` step.

    .DESCRIPTION
    An `@human` step is an operator round-trip: autopilot exits 42 and a person has to act. Without
    **Steps**, **Verify**, and **Rollback** spelled out, that round-trip takes several passes. Returns
    an empty string when the step satisfies the gate, otherwise the reason it does not.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string]$Detail
    )

    if ([string]::IsNullOrWhiteSpace($Detail)) {
        return 'has no <details> block'
    }

    $missing = @()
    foreach ($section in @('Steps', 'Verify', 'Rollback')) {
        # Tolerate a list marker before the bold label; the section is what matters, not the bullet.
        if ($Detail -notmatch "(?im)^\s*(?:[-*+]\s+|\d+\.\s+)?\*\*${section}:?\*\*") {
            $missing += , $section
        }
    }

    if ($missing.Count -gt 0) {
        return "is missing the **$($missing -join '**, **')** section(s) in its <details> block"
    }

    return ''
}

function Test-PlanMetadata {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        $Metadata,

        [ValidateSet('Draft', 'PhaseCrosscheck', 'PlanCrosscheck')]
        [string]$Stage = 'Draft'
    )

    $errors = [System.Collections.Generic.List[string]]::new()
    $warnings = [System.Collections.Generic.List[string]]::new()
    foreach ($warning in $Metadata.Warnings) {
        $warnings.Add($warning)
    }

    $isOptedIn = $Metadata.Content -match '(?m)^\s*<!--\s*evidence:\s*required\s*-->\s*$'
    if (-not $isOptedIn) {
        $warnings.Add("Legacy plan mode: '$($Metadata.PlanPath)' has no <!-- evidence: required --> marker, so integrity findings are warn-only.")
    }

    $reqIds = @($Metadata.Requirements.Keys | Sort-Object)
    if ($reqIds.Count -eq 0) {
        $errors.Add('Requirements table is missing REQ-* rows.')
    }
    else {
        $numbers = @($Metadata.Requirements.Values | Sort-Object Number | ForEach-Object { $_.Number })
        for ($index = 0; $index -lt $numbers.Count; $index++) {
            $expected = $index + 1
            if ($numbers[$index] -ne $expected) {
                $message = "REQ numbering is not sequential from REQ-1 (missing or out-of-order near REQ-$expected)."
                if ($isOptedIn) {
                    $errors.Add($message)
                }
                else {
                    $warnings.Add($message)
                }
                break
            }
        }
    }

    $riskIds = @($Metadata.Risks.Keys | Sort-Object)
    if ($riskIds.Count -gt 0) {
        $numbers = @($Metadata.Risks.Values | Sort-Object)
        for ($index = 0; $index -lt $numbers.Count; $index++) {
            $expected = $index + 1
            if ($numbers[$index] -ne $expected) {
                $message = "RISK numbering is not sequential from RISK-1 (missing or out-of-order near RISK-$expected)."
                if ($isOptedIn) {
                    $errors.Add($message)
                }
                else {
                    $warnings.Add($message)
                }
                break
            }
        }
    }

    $stepIds = @{}
    $reqRefsByStep = @{}
    $riskRefsByStep = @{}
    foreach ($step in $Metadata.Steps) {
        if ($stepIds.ContainsKey($step.Id)) {
            $message = "Duplicate step ID '$($step.Id)'."
            if ($isOptedIn) {
                $errors.Add($message)
            }
            else {
                $warnings.Add($message)
            }
            continue
        }
        $stepIds[$step.Id] = $true

        if ($step.Role -ne 'human' -and $step.Role -ne 'ai-agent') {
            $message = "Step '$($step.Id)' has invalid role '@$($step.Role)'."
            if ($isOptedIn) {
                $errors.Add($message)
            }
            else {
                $warnings.Add($message)
            }
        }

        if ($step.Role -eq 'human') {
            $stepDetail = if ($step.PSObject.Properties['Detail']) { [string]$step.Detail } else { '' }
            $gap = Get-HumanStepDetailGap -Detail $stepDetail
            if ($gap) {
                $message = "human-step-detail: step '$($step.Id)' is @human but $gap. Give it **Steps**, **Verify**, and **Rollback** so the operator round-trip is single-pass."
                $isArchivedPlan = ($Metadata.PSObject.Properties['IsArchived']) -and $Metadata.IsArchived
                if ($isOptedIn -and -not $isArchivedPlan) {
                    $errors.Add($message)
                }
                else {
                    $warnings.Add($message)
                }
            }
        }

        if ($step.Size -notin @('S', 'M', 'L')) {
            $message = "Step '$($step.Id)' is missing size marker `S`, `M`, or `L`."
            if ($isOptedIn) {
                $errors.Add($message)
            }
            else {
                $warnings.Add($message)
            }
        }

        $reqRefs = @($step.Refs | Where-Object { $_ -match '^REQ-\d+$' })
        $riskRefs = @($step.Refs | Where-Object { $_ -match '^RISK-\d+$' })
        $reqRefsByStep[$step.Id] = $reqRefs
        $riskRefsByStep[$step.Id] = $riskRefs

        if ($reqRefs.Count -eq 0) {
            $message = "Step '$($step.Id)' must reference at least one REQ-* ID."
            if ($isOptedIn) {
                $errors.Add($message)
            }
            else {
                $warnings.Add($message)
            }
        }

        foreach ($reqRef in $reqRefs) {
            if (-not $Metadata.Requirements.ContainsKey($reqRef)) {
                $message = "Step '$($step.Id)' references unknown requirement '$reqRef'."
                if ($isOptedIn) {
                    $errors.Add($message)
                }
                else {
                    $warnings.Add($message)
                }
            }
        }

        foreach ($riskRef in $riskRefs) {
            if (-not $Metadata.Risks.ContainsKey($riskRef)) {
                $message = "Step '$($step.Id)' references unknown risk '$riskRef'."
                if ($isOptedIn) {
                    $errors.Add($message)
                }
                else {
                    $warnings.Add($message)
                }
            }
        }

        foreach ($afterId in $step.After) {
            if ($afterId -notmatch '^\d+\.\d+[a-z]?$') {
                $message = "Step '$($step.Id)' has invalid [after:] target '$afterId'."
                if ($isOptedIn) {
                    $errors.Add($message)
                }
                else {
                    $warnings.Add($message)
                }
                continue
            }

            if (-not $stepIds.ContainsKey($afterId) -and -not ($Metadata.Steps | Where-Object { $_.Id -eq $afterId })) {
                $message = "Step '$($step.Id)' depends on unknown step '$afterId'."
                if ($isOptedIn) {
                    $errors.Add($message)
                }
                else {
                    $warnings.Add($message)
                }
            }
        }
    }

    $graph = @{}
    foreach ($step in $Metadata.Steps) {
        $graph[$step.Id] = @($step.After)
    }

    $states = @{}
    function Visit-Step {
        param(
            [string]$StepId,
            [string[]]$Stack
        )

        $state = if ($states.ContainsKey($StepId)) { [int]$states[$StepId] } else { 0 }
        if ($state -eq 1) {
            $cycle = ($Stack + $StepId) -join ' -> '
            $message = "Dependency cycle detected: $cycle"
            if ($isOptedIn) {
                $errors.Add($message)
            }
            else {
                $warnings.Add($message)
            }
            return
        }
        if ($state -eq 2) {
            return
        }

        $states[$StepId] = 1
        foreach ($dependency in @($graph[$StepId])) {
            if ($graph.ContainsKey($dependency)) {
                Visit-Step -StepId $dependency -Stack ($Stack + $StepId)
            }
        }
        $states[$StepId] = 2
    }

    foreach ($step in $Metadata.Steps) {
        Visit-Step -StepId $step.Id -Stack @()
    }

    $referencedReqs = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $referencedRisks = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($stepId in $reqRefsByStep.Keys) {
        foreach ($reqRef in @($reqRefsByStep[$stepId])) {
            [void]$referencedReqs.Add($reqRef)
        }
    }
    foreach ($stepId in $riskRefsByStep.Keys) {
        foreach ($riskRef in @($riskRefsByStep[$stepId])) {
            [void]$referencedRisks.Add($riskRef)
        }
    }

    foreach ($reqId in $Metadata.Requirements.Keys) {
        if (-not $referencedReqs.Contains($reqId)) {
            $message = "Requirement '$reqId' is not referenced by any step."
            if ($isOptedIn) {
                $errors.Add($message)
            }
            else {
                $warnings.Add($message)
            }
        }
    }

    foreach ($riskId in $Metadata.Risks.Keys) {
        if (-not $referencedRisks.Contains($riskId)) {
            $message = "Risk '$riskId' is not referenced by any step."
            if ($isOptedIn) {
                $errors.Add($message)
            }
            else {
                $warnings.Add($message)
            }
        }
    }

    foreach ($requirement in $Metadata.Requirements.Values) {
        $markers = Get-TypedEvidenceMarkers -AcceptanceCriteria $requirement.AcceptanceCriteria
        if ($markers.Count -eq 0) {
            $message = "Requirement '$($requirement.Id)' has no typed evidence marker in acceptance criteria."
            if ($isOptedIn) {
                $errors.Add($message)
            }
            else {
                $warnings.Add($message)
            }
            continue
        }

        foreach ($marker in $markers) {
            if ($marker.StartsWith('test:')) {
                continue
            }

            if ($marker -match '^review:(cr|dr)$') {
                continue
            }

            if ($marker.StartsWith('file:')) {
                if ($marker -cnotmatch '^file:(?<path>[^#]+)#(?:exists|contains:.+|count>=\d+|dircount>=\d+)$') {
                    $errors.Add("$($requirement.Id): Invalid file evidence assertion [$marker]")
                    continue
                }
                $relative = $Matches.path.Trim()
                if ([string]::IsNullOrWhiteSpace($relative) -or
                    [System.IO.Path]::IsPathRooted($relative) -or
                    $relative -match '^[A-Za-z]:' -or
                    ($relative -replace '\\', '/').Split('/') -contains '..') {
                    $errors.Add("$($requirement.Id): File evidence path must be relative and remain inside repository root [$marker]")
                }
                continue
            }

            $message = "$($requirement.Id): unknown evidence marker '$marker'."
            if ($isOptedIn) {
                $errors.Add($message)
            }
            else {
                $warnings.Add($message)
            }
        }
    }

    $budgetCap = 6
    $headerMarkers = Get-PlanHeaderMarkers -Content $Metadata.Content
    if ($headerMarkers.All.Contains('phase-budget-points')) {
        $budgetRaw = [string]$headerMarkers.All['phase-budget-points']
        $budgetParsed = 0
        # TryParse, not a [int] cast: plan text is untrusted, and an out-of-range digit string must take
        # the warn-and-default branch instead of throwing out of a check that never blocks.
        if ([int]::TryParse($budgetRaw, [ref]$budgetParsed) -and $budgetParsed -gt 0) {
            $budgetCap = $budgetParsed
        }
        else {
            $warnings.Add("Invalid <!-- phase-budget-points: $budgetRaw --> marker; using the default cap of $budgetCap.")
        }
    }

    foreach ($phaseName in $Metadata.PhaseSteps.Keys) {
        $total = 0
        foreach ($step in $Metadata.PhaseSteps[$phaseName]) {
            $total += Get-StepPoints -Size $step.Size
        }

        if ($total -gt $budgetCap) {
            $warnings.Add("$phaseName uses $total phase-budget points (advisory cap is $budgetCap).")
        }
    }

    return [pscustomobject]@{
        Errors = @($errors)
        Warnings = @($warnings)
        OptedIn = $isOptedIn
    }
}

if ($PSCmdlet.ParameterSetName -eq 'VerifyEvidence') {
    try {
        $repoRootPath = [System.IO.Path]::GetFullPath($RepoRoot)
        $result = @(Invoke-DirectEvidence -RepoRoot $repoRootPath -Marker $EvidenceMarker)[0]
        if ($result.Success) {
            Write-Host "Evidence passed: $EvidenceMarker"
            exit 0
        }

        Write-Host "Evidence failed: $EvidenceMarker" -ForegroundColor Red
        exit 1
    }
    catch {
        Write-Host "Evidence failed: $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }
}

try {
    $metadata = Get-PlanMetadata -Path $PlanPath -RepoRoot $RepoRoot
    $result = Test-PlanMetadata -Metadata $metadata -Stage $Stage
}
catch {
    Write-Host "Test-Plan failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

foreach ($warning in $result.Warnings) {
    Write-Host "WARN: $warning" -ForegroundColor Yellow
}

if ($result.Errors.Count -gt 0) {
    foreach ($validationError in $result.Errors) {
        Write-Host "ERROR: $validationError" -ForegroundColor Red
    }
    exit 1
}

Write-Host "Test-Plan passed: '$($metadata.PlanPath)' ($Stage)" -ForegroundColor Green
exit 0
