#requires -Version 7.0
<#
.SYNOPSIS
Materializes a greenfield architecture seed as 1-2 draft Markdown architecture notes.

.DESCRIPTION
The seed operation of the architecture-notes skill. An agent runs the interview (see
./assets/interview-guide.md), records the answers into a seed-spec JSON, and hands it here. This
script is the script-mediated, deterministic materialization step:

  1. Scaffolds the tier index via Copy-ArchScaffold.ps1 (no-overwrite).
  2. Writes each boundary as one terse, self-contained Markdown architecture note (no-overwrite).

It never writes a `locked` contract. The seed-spec must declare 1..MaxBoundaries boundaries
(default 2) — "no big design upfront". Each shipped/written file is reported with the action
taken ('created', 'skipped', or 'whatif') so callers and evals can assert the outcome.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    # Repository root the seed is materialized into.
    [Parameter(Mandatory)][string]$TargetRoot,

    # Path to the seed-spec JSON produced from the interview (see ./assets/interview-guide.md).
    [Parameter(Mandatory)][string]$SeedSpecPath,

    # Root of the shipped assets (…/architecture-notes/assets). Auto-detected when omitted.
    [string]$AssetRoot,

    # Upper bound on seeded boundaries. Keeps the seed light.
    [int]$MaxBoundaries = 2
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $TargetRoot -PathType Container)) {
    throw "TargetRoot not found or not a directory: $TargetRoot"
}
# Resolve to an absolute path so .NET (CWD-based) and PowerShell provider ($PWD-based) path
# resolution can never diverge for a relative -TargetRoot.
$TargetRoot = (Resolve-Path -LiteralPath $TargetRoot).Path
if (-not (Test-Path -LiteralPath $SeedSpecPath -PathType Leaf)) {
    throw "Seed-spec file not found: $SeedSpecPath"
}

$copyScaffold = Join-Path $PSScriptRoot 'Copy-ArchScaffold.ps1'
foreach ($s in @($copyScaffold)) {
    if (-not (Test-Path -LiteralPath $s -PathType Leaf)) {
        throw "Required sibling script missing: $s"
    }
}

if (-not $AssetRoot) {
    # Dual-layout probe, matching the sibling scripts. Require the templates/ subtree so a
    # stray same-named directory cannot win.
    $candidates = @(
        (Join-Path $PSScriptRoot '..' 'skills' 'architecture-notes' 'assets'),
        (Join-Path $PSScriptRoot '..' 'assets')
    )
    foreach ($candidate in $candidates) {
        if ((Test-Path -LiteralPath $candidate -PathType Container) -and
            (Test-Path -LiteralPath (Join-Path $candidate 'templates') -PathType Container)) {
            $AssetRoot = (Resolve-Path -LiteralPath $candidate).Path
            break
        }
    }
    if (-not $AssetRoot) {
        throw "Could not locate architecture-notes assets relative to $PSScriptRoot; pass -AssetRoot explicitly."
    }
}
if (-not (Test-Path -LiteralPath $AssetRoot -PathType Container)) {
    throw "AssetRoot not found or not a directory: $AssetRoot"
}

$noteTemplate = Join-Path $AssetRoot 'templates/architecture-note.template.md'
foreach ($t in @($noteTemplate)) {
    if (-not (Test-Path -LiteralPath $t -PathType Leaf)) { throw "Required template missing: $t" }
}

$spec = Get-Content -LiteralPath $SeedSpecPath -Raw | ConvertFrom-Json
function Get-SpecProp {
    param($Object, [string]$Name, $Default = $null)
    if ($Object -and $Object.PSObject.Properties.Name -contains $Name) { return $Object.$Name }
    return $Default
}

$rawBoundaries = Get-SpecProp $spec 'boundaries'
if ($null -eq $rawBoundaries) {
    throw "Seed-spec must declare 1..$MaxBoundaries boundaries (no big design upfront); 'boundaries' is missing or null."
}
$boundaries = @($rawBoundaries)
if ($boundaries.Count -lt 1 -or $boundaries.Count -gt $MaxBoundaries) {
    throw "Seed-spec must declare 1..$MaxBoundaries boundaries (no big design upfront); got $($boundaries.Count)."
}
$idPattern = '^[A-Za-z0-9][A-Za-z0-9._-]*$'
$reservedIdBasenames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($n in @(
        '.architecture-notes', 'architecture.human',
        'CON', 'PRN', 'AUX', 'NUL',
        'COM1', 'COM2', 'COM3', 'COM4', 'COM5', 'COM6', 'COM7', 'COM8', 'COM9',
        'LPT1', 'LPT2', 'LPT3', 'LPT4', 'LPT5', 'LPT6', 'LPT7', 'LPT8', 'LPT9')) {
    [void]$reservedIdBasenames.Add($n)
}

# Validate the complete request before scaffolding so bad input cannot leave partial output.
$seenIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($b in $boundaries) {
    $id = [string](Get-SpecProp $b 'id')
    $title = [string](Get-SpecProp $b 'title')
    $prose = [string](Get-SpecProp $b 'prose')
    $scope = [string](Get-SpecProp $b 'scope' '**')
    if ($id -notmatch $idPattern) { throw "Boundary id '$id' does not match $idPattern." }
    if ($reservedIdBasenames.Contains($id)) {
        throw "Boundary id '$id' is reserved (shadows a scaffolded artifact or a Windows device name); choose another id."
    }
    if (-not $seenIds.Add($id)) {
        throw "Seed-spec has a duplicate (case-insensitive) boundary id: '$id'."
    }
    if ([string]::IsNullOrWhiteSpace($title)) { throw "Boundary '$id' is missing a title." }
    if ([string]::IsNullOrWhiteSpace($prose)) { throw "Boundary '$id' is missing prose." }
    if ($title -match '[\r\n|:#]' -or $scope -match '[\r\n"|]') {
        throw "Boundary '$id' title and scope must be single-line Markdown/YAML-safe text."
    }
}
$project = [string](Get-SpecProp $spec 'project' 'Project')
if ([string]::IsNullOrWhiteSpace($project)) { $project = 'Project' }

function Write-SeedFile {
    param(
        [Parameter(Mandatory)][string]$DestPath,
        [Parameter(Mandatory)][string]$Content,
        [Parameter(Mandatory)]$Cmdlet
    )
    if (Test-Path -LiteralPath $DestPath -PathType Leaf) {
        return 'skipped'
    }
    $action = 'whatif'
    if ($Cmdlet.ShouldProcess($DestPath, 'Write seed file')) {
        $destDir = Split-Path -Parent $DestPath
        if ($destDir -and -not (Test-Path -LiteralPath $destDir -PathType Container)) {
            [void][System.IO.Directory]::CreateDirectory($destDir)
        }
        Set-Content -LiteralPath $DestPath -Value $Content -NoNewline
        $action = 'created'
    }
    return $action
}

# 1) Scaffold the tier index, no-overwrite.
$scaffoldResult = & $copyScaffold -TargetRoot $TargetRoot -AssetRoot $AssetRoot

$notesDir = Join-Path $TargetRoot 'docs/architecture-notes'

$noteTemplateText = Get-Content -LiteralPath $noteTemplate -Raw

$contracts = [System.Collections.Generic.List[object]]::new()
$notes = [System.Collections.Generic.List[object]]::new()

foreach ($b in $boundaries) {
    $id = [string](Get-SpecProp $b 'id')
    $title = [string](Get-SpecProp $b 'title')
    $prose = [string](Get-SpecProp $b 'prose')
    $scope = [string](Get-SpecProp $b 'scope' '**')
    # 2) One terse Markdown note is both the human-readable contract and its scoped context.
    $safeProse = ($prose -replace '[\r\n]+', ' ' -replace '\|', '\|').Trim()
    $noteText = $noteTemplateText.
        Replace('<SUBSYSTEM>', $title).
        Replace('<SCOPE_GLOB>', $scope).
        Replace('<CONTRACT_ID>', $id).
        Replace('<BOUNDARY_PROSE>', $safeProse)
    $noteSlug = ($id.ToLowerInvariant() -replace '[^a-z0-9._-]', '-')
    $notePath = Join-Path $notesDir ($noteSlug + '.md')
    $noteAction = Write-SeedFile -DestPath $notePath -Content $noteText -Cmdlet $PSCmdlet
    $notes.Add([pscustomobject]@{
            Path = $notePath; File = Split-Path -Leaf $notePath; Scope = $scope; Action = $noteAction
        })
    $contracts.Add([pscustomobject]@{
            Path = $notePath; Id = $id; Title = $title; Scope = $scope
            Note = Split-Path -Leaf $notePath; Maturity = 'draft'; Action = $noteAction; Valid = $true
        })
}

$indexPath = Join-Path $notesDir '.architecture-notes.md'
$indexAction = 'skipped'
if (Test-Path -LiteralPath $indexPath -PathType Leaf) {
    $indexText = Get-Content -LiteralPath $indexPath -Raw
    $contractRows = @($contracts | ForEach-Object {
            "| ``$($_.Id)`` | $($_.Title) | draft | ``$($_.Scope)`` | [$($_.Note)]($($_.Note)) |"
        })
    $noteRows = @($contracts | ForEach-Object {
            "| [$($_.Note)]($($_.Note)) | ``$($_.Scope)`` | ``$($_.Id)`` |"
        })
    function Set-SectionPlaceholder {
        param(
            [Parameter(Mandatory)][string]$Text,
            [Parameter(Mandatory)][string]$Heading,
            [Parameter(Mandatory)][string]$NextHeading,
            [Parameter(Mandatory)][string]$Placeholder,
            [Parameter(Mandatory)][string]$Rows
        )
        $start = $Text.IndexOf($Heading, [System.StringComparison]::Ordinal)
        $end = $Text.IndexOf($NextHeading, $start + $Heading.Length, [System.StringComparison]::Ordinal)
        if ($start -lt 0 -or $end -lt 0) { throw "Architecture index is missing '$Heading'." }
        $section = $Text.Substring($start, $end - $start)
        if (-not $section.Contains($Placeholder, [System.StringComparison]::Ordinal)) {
            return $Text
        }
        $section = $section.Replace($Placeholder, $Rows, [System.StringComparison]::Ordinal)
        return $Text.Substring(0, $start) + $section + $Text.Substring($end)
    }

    $newIndexText = Set-SectionPlaceholder -Text $indexText -Heading '## Contracts' `
        -NextHeading '## Architecture Notes' -Placeholder '| _none yet_ | | | | |' `
        -Rows ($contractRows -join "`n")
    $newIndexText = Set-SectionPlaceholder -Text $newIndexText -Heading '## Architecture Notes' `
        -NextHeading '## Decision Records (active)' -Placeholder '| _none yet_ | | |' `
        -Rows ($noteRows -join "`n")
    if ($newIndexText -ne $indexText) {
        $indexAction = 'whatif'
        if ($PSCmdlet.ShouldProcess($indexPath, 'Index seeded architecture notes')) {
            Set-Content -LiteralPath $indexPath -Value $newIndexText -NoNewline
            $indexAction = 'updated'
        }
    }
}

[pscustomobject]@{
    Project   = $project
    Scaffold  = @($scaffoldResult)
    Contracts = @($contracts)
    Notes     = @($notes)
    Index     = [pscustomobject]@{ Path = $indexPath; Action = $indexAction }
}
