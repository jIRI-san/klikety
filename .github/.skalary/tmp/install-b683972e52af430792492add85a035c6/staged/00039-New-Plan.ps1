#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Title,

    [Parameter(Mandatory)]
    [string]$Slug,

    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path,

    [string]$Date = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd'),

    [string]$PlanId,

    [string]$EpicId,

    [string]$TemplatePath,

    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'PlanState.psm1') -Force -DisableNameChecking

function Get-SanitizedSlug {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Value
    )

    $clean = $Value.ToLowerInvariant()
    $clean = $clean -replace '[^a-z0-9]+', '-'
    $clean = $clean.Trim('-')
    if ([string]::IsNullOrWhiteSpace($clean)) {
        throw "Slug '$Value' is empty after sanitization; supply alphanumeric characters."
    }

    return $clean
}

function Resolve-ConfinedFolder {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Root,

        [Parameter(Mandatory)]
        [string]$FolderName
    )

    $resolvedRoot = [System.IO.Path]::GetFullPath($Root)
    $candidate = [System.IO.Path]::GetFullPath((Join-Path $resolvedRoot $FolderName))
    $separator = [System.IO.Path]::DirectorySeparatorChar
    $rootWithSeparator = $resolvedRoot.TrimEnd($separator) + $separator
    if (-not $candidate.StartsWith($rootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Resolved plan folder '$candidate' escapes plans root '$resolvedRoot'."
    }

    return $candidate
}

function Get-PlanAssetScaffold {
    [CmdletBinding()]
    param()

    # Scaffolded asset files always carry an explicit placeholder and are never zero-length, so
    # "present-but-empty" (which Resolve-PlanSection fails loud on) stays distinguishable from "authored".
    return [ordered]@{
        'intent.md'       = @'
# Intent

<!-- Captured during the /cip interview. Placeholder — replace before drafting. -->

## Goal

TBD

## Desired outcome

TBD

## Success signals

- TBD

## Non-goals

- TBD

## Definition of done

- TBD
'@
        'domain.md'       = @'
# Domain Model

<!-- Capture project-specific terms, actors, invariants, and boundaries that affect the design. -->

## Terms and meanings

- TBD

## Actors and boundaries

- TBD

## Interfaces and ownership

- TBD

## Invariants

- TBD
'@
        'design.md'       = @'
# Approved Design

<!--
Describe the agreed program shape, not implementation detail. Replace every TBD and confirm the result with
the operator before detailed plan drafting. A Mermaid program flow is required. Call stacks are optional.
-->

## Components and boundaries

- TBD

## Program flow

```mermaid
flowchart TD
    A[TBD] --> B[TBD]
```

## Optional call stacks

Add call stacks only when they clarify important control flow; otherwise state that the Mermaid flow is sufficient.
'@
        'requirements.md' = @'
# Requirements

<!-- Every discovered edge case belongs in this table, assets/risks.md, or an explicit non-goal in assets/intent.md. -->

| ID | Requirement | Acceptance Criteria | Phases/Steps |
|----|-------------|---------------------|--------------|
| REQ-1 | | Use typed evidence markers in criteria: `test:<TestId>` · `file:<path>#exists` · `file:<path>#contains:<regex>` · `file:<path>#count>=1` · `file:<path>#dircount>=1` · `review:cr|dr` | |
'@
        'risks.md'        = @'
# Risks

<!-- For uncertain or high-impact work, the mitigation names the concrete stop/escalation condition. -->

| ID | Risk | Likelihood | Impact | Mitigation | Steps |
|----|------|------------|--------|------------|-------|
| RISK-1 | | Low/Medium/High | Low/Medium/High | | 1.2 |
'@
        'decisions.md'    = @'
# Decisions

<!-- Key decisions made during planning — one bullet per decision. Extended rationale goes in assets/decisions/<topic>.md. -->

- TBD
'@
        'references.md'   = @'
# References

<!-- Design notes, architecture contracts, and prior plans consulted while drafting. -->

- TBD
'@
    }
}

$repoRootPath = [System.IO.Path]::GetFullPath($RepoRoot)
$plansRoot = Join-Path $repoRootPath 'docs/implementation-plans'

if ($Date -notmatch '^\d{4}-\d{2}-\d{2}$') {
    throw "Date '$Date' must be in yyyy-MM-dd format."
}

$slugClean = Get-SanitizedSlug -Value $Slug

# Plans and epics share one id space — `/ci` accepts either handle — so both inventories reserve ids,
# in both directions, and an explicitly supplied id is collision-checked rather than trusted.
$planInventory = @(Get-PlanInventory -RepoRoot $repoRootPath)
$epicInventory = @(Get-EpicInventory -RepoRoot $repoRootPath)
$takenIds = $planInventory + $epicInventory

if ($EpicId) {
    $EpicId = $EpicId.Trim().ToLowerInvariant()
    if ($EpicId -notmatch '^[0-9a-f]{6}$') {
        throw "EpicId '$EpicId' must be exactly 6 hex chars."
    }
    $matchingEpic = @($epicInventory | Where-Object { $_.Id -and $_.Id.ToLowerInvariant() -eq $EpicId })
    if ($matchingEpic.Count -ne 1) {
        throw "EpicId '$EpicId' does not identify an existing epic."
    }
    if (-not (Test-Path -LiteralPath $matchingEpic[0].EpicFile -PathType Leaf)) {
        throw "EpicId '$EpicId' has no epic.md at $($matchingEpic[0].EpicFile)."
    }
}

if ($PlanId) {
    $PlanId = $PlanId.Trim().ToLowerInvariant()
    if ($PlanId -notmatch '^[0-9a-f]{6}$') {
        throw "PlanId '$PlanId' must be exactly 6 hex chars."
    }
}
else {
    $PlanId = New-PlanId -ExistingId @($takenIds | ForEach-Object { $_.Id })
}

$folderPrefix = if ($EpicId) { $EpicId } else { 'standalone' }
$folderName = "$folderPrefix-$Date-$PlanId-$slugClean"
$targetDir = Resolve-ConfinedFolder -Root $plansRoot -FolderName $folderName
$pathComparison = if ($IsWindows) {
    [System.StringComparison]::OrdinalIgnoreCase
}
else {
    [System.StringComparison]::Ordinal
}

foreach ($existing in $takenIds) {
    if (-not $existing.Id -or $existing.Id.ToLowerInvariant() -ne $PlanId) {
        continue
    }

    $existingPath = if ($existing.Path) {
        [System.IO.Path]::GetFullPath([string]$existing.Path)
    }
    else {
        $null
    }
    if (-not $existingPath -or -not [string]::Equals($existingPath, $targetDir, $pathComparison)) {
        throw "Plan id '$PlanId' is already taken by '$($existing.FolderName)'."
    }
}

$planFile = Join-Path $targetDir 'plan.md'

if ((Test-Path -LiteralPath $targetDir) -and -not $Force) {
    throw "Plan folder already exists: $targetDir (use -Force to overwrite plan.md)."
}

if (-not $TemplatePath) {
    # The generated script lives beside the skill assets in authored and installed layouts.
    # A repo-root plugins/ fallback would work only while dogfooding and hide a broken bundle.
    $templateCandidates = @(
        (Join-Path $PSScriptRoot '..' 'assets' 'plan-template.md')
        (Join-Path $repoRootPath '.github/skills/cip/assets/plan-template.md')
    )
    foreach ($candidate in $templateCandidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $TemplatePath = [System.IO.Path]::GetFullPath($candidate)
            break
        }
    }
    if (-not $TemplatePath) {
        throw "Plan template not found; probed: $(($templateCandidates | ForEach-Object { [System.IO.Path]::GetFullPath($_) }) -join ', ')."
    }
}
if (-not (Test-Path -LiteralPath $TemplatePath -PathType Leaf)) {
    throw "Plan template not found: $TemplatePath"
}

if (-not (Test-Path -LiteralPath $plansRoot)) {
    New-Item -ItemType Directory -Path $plansRoot -Force | Out-Null
}

$templateRaw = Get-Content -LiteralPath $TemplatePath -Raw
$normalized = $templateRaw -replace "`r`n", "`n"
$lines = [System.Collections.Generic.List[string]]::new()
$lines.AddRange([string[]]($normalized.Split("`n")))

$titleReplaced = $false
for ($i = 0; $i -lt $lines.Count; $i++) {
    if (-not $titleReplaced -and $lines[$i] -match '^#\s+') {
        $lines[$i] = "# ${PlanId}: $Title"
        if (($i + 1) -lt $lines.Count -and $lines[$i + 1] -match '^\s*<!--\s*plan-id:') {
            $lines[$i + 1] = "<!-- plan-id: $PlanId -->"
        }
        else {
            $lines.Insert($i + 1, "<!-- plan-id: $PlanId -->")
        }
        $titleReplaced = $true
        break
    }
}

if (-not $titleReplaced) {
    $lines.Insert(0, "<!-- plan-id: $PlanId -->")
    $lines.Insert(0, "# ${PlanId}: $Title")
}

if ($EpicId) {
    $epicMarker = "<!-- epic: $EpicId -->"
    $epicMarkerIndex = -1
    $planIdIndex = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^##\s') { break }
        if ($lines[$i] -match '^\s*<!--\s*plan-id\s*:') { $planIdIndex = $i }
        if ($lines[$i] -match '^\s*<!--\s*epic\s*:') {
            $epicMarkerIndex = $i
            break
        }
    }
    if ($epicMarkerIndex -ge 0) {
        $lines[$epicMarkerIndex] = $epicMarker
    }
    else {
        $lines.Insert($planIdIndex + 1, $epicMarker)
    }
}

New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
$content = ($lines -join "`n")
Set-Content -LiteralPath $planFile -Value $content -Encoding utf8NoBOM

# A scaffold is stamped rather than left anchorless: a missing anchor means `drafted`, so an unstamped
# scaffold would be validated as if it were authored. The stamp goes through Set-PlanStage — the single
# writer of the anchor — so the scaffold value is checked against the closed stage set by the same code
# that checks every later transition, instead of this script becoming a second, unchecked writer.
$setPlanStagePath = Join-Path $PSScriptRoot 'Set-PlanStage.ps1'
if (-not (Test-Path -LiteralPath $setPlanStagePath -PathType Leaf)) {
    throw "Set-PlanStage.ps1 not found beside New-Plan.ps1: $setPlanStagePath"
}
$stamped = & $setPlanStagePath -PlanFile $planFile -Stage 'scaffolded'

$assetsDir = Resolve-ConfinedFolder -Root $targetDir -FolderName 'assets'
New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null
$assetFiles = [System.Collections.Generic.List[string]]::new()
foreach ($entry in (Get-PlanAssetScaffold).GetEnumerator()) {
    $assetPath = Join-Path $assetsDir $entry.Key
    # Never overwrite an existing asset: -Force means "overwrite plan.md", and re-scaffolding over
    # authored requirements/risks/decisions would silently destroy the plan's content.
    if (Test-Path -LiteralPath $assetPath -PathType Leaf) {
        $assetFiles.Add($assetPath)
        continue
    }
    Set-Content -LiteralPath $assetPath -Value ($entry.Value -replace "`r`n", "`n") -Encoding utf8NoBOM
    $assetFiles.Add($assetPath)
}

$result = [pscustomobject]@{
    PlanId     = $PlanId
    Slug       = $slugClean
    Date       = $Date
    FolderPrefix = $folderPrefix
    EpicId     = $EpicId
    FolderName = $folderName
    Path       = $targetDir
    PlanFile   = $planFile
    Stage      = $stamped.Stage
    AssetsDir  = $assetsDir
    AssetFiles = $assetFiles.ToArray()
}

Write-Host "Created plan '$folderName' (plan-id $PlanId) at $planFile" -ForegroundColor Green
return $result
