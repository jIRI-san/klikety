#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PlanFile,

    [Parameter(Mandatory)]
    [string]$Stage,

    [switch]$ConfirmPlanningContext
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'PlanState.psm1') -Force -DisableNameChecking

$stageValue = $Stage.Trim()
if ([string]::IsNullOrWhiteSpace($stageValue)) {
    throw 'Stage must be a non-empty value.'
}
if ($stageValue -match '[>\r\n]') {
    throw "Stage '$Stage' must not contain '>' or newlines."
}

# The single writer of the anchor is also the gate on what may be written: a stage that never reaches
# the file cannot later be misread as "skip every check" (RISK-6). Throws on anything outside the set.
$stageValue = (Resolve-PlanStage -Stage $stageValue).Stage

$fullPath = (Resolve-Path -LiteralPath $PlanFile).Path
$planDir = Split-Path -Parent $fullPath
$raw = Get-Content -LiteralPath $fullPath -Raw
$normalized = $raw -replace "`r`n", "`n"

$anchor = "<!-- cip-stage: $stageValue -->"
$pattern = '<!--\s*cip-stage:\s*[^>]*-->'

# Scoped to the header because that is the only region readers parse. Matching over the whole file let
# a step description quoting an anchor absorb the write while the header kept none.
$split = Split-PlanHeader -Content $normalized

if ([regex]::IsMatch($split.Header, $pattern)) {
    $header = [regex]::Replace($split.Header, $pattern, $anchor, 1)
}
else {
    $lines = [System.Collections.Generic.List[string]]::new()
    if ($split.HeaderLineCount -gt 0) {
        $lines.AddRange([string[]]($split.Header.Split("`n")))
    }

    $insertAfter = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '<!--\s*plan-id:') { $insertAfter = $i; break }
    }
    if ($insertAfter -lt 0) {
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match '^#\s') { $insertAfter = $i; break }
        }
    }

    if ($insertAfter -ge 0) {
        $lines.Insert($insertAfter + 1, $anchor)
    }
    else {
        $lines.Insert(0, $anchor)
    }
    $header = $lines -join "`n"
}

$planningPattern = '<!--\s*planning-confirmed\s*:\s*[^\r\n]*?-->'
$malformedPlanningPattern = '(?m)^[ \t]*<!--\s*planning-confirmed(?![\w-])[^\r\n]*$'
$planningMarkers = Get-PlanHeaderMarkers -Content $normalized
$planningMarker = $planningMarkers.PlanningConfirmed
$hasPlanningMarker = $planningMarkers.All.Contains('planning-confirmed')
if ($ConfirmPlanningContext) {
    Assert-PlanningContextReady -PlanDir $planDir
    $planningAnchor = "<!-- planning-confirmed: sha256:$(Get-PlanningContextDigest -PlanDir $planDir) -->"
    if ([regex]::IsMatch($header, $planningPattern)) {
        $header = [regex]::Replace($header, $planningPattern, $planningAnchor, 1)
    }
    elseif ([regex]::IsMatch($header, $malformedPlanningPattern)) {
        $header = [regex]::Replace($header, $malformedPlanningPattern, $planningAnchor, 1)
    }
    else {
        throw "Plan '$fullPath' is not enrolled for planning confirmation."
    }
}
elseif ($stageValue -eq 'scaffolded' -and -not $hasPlanningMarker) {
    $planningAnchor = '<!-- planning-confirmed: pending -->'
    $header = [regex]::Replace($header, $pattern, "`$0`n$planningAnchor", 1)
}
elseif ($hasPlanningMarker -and
    (Test-PlanStageAtLeast -Stage $stageValue -Minimum 'drafted')) {
    $planningState = Get-PlanningContextState -PlanDir $planDir
    if (-not $planningState.CanProceed) {
        throw "Planning context is '$($planningState.Status)'. Reconfirm intent and design with -ConfirmPlanningContext before advancing to '$stageValue'."
    }
}

$updated = if ($split.HasBody) { @($header, $split.Body) -join "`n" } else { $header }

$content = $updated.TrimEnd("`n") + "`n"
Set-Content -LiteralPath $fullPath -Value $content -Encoding utf8NoBOM -NoNewline

# Read back through the parser every consumer uses, so a write that lands where no reader looks fails
# loudly instead of returning the stage it did not persist.
$persisted = (Get-PlanHeaderMarkers -Path $fullPath).CipStage
if ($persisted -ne $stageValue) {
    throw "Set-PlanStage wrote stage '$stageValue' to '$fullPath', but the header reads '$persisted'."
}

return [pscustomobject]@{
    PlanFile = $fullPath
    Stage    = $stageValue
}
