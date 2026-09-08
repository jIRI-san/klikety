#requires -Version 7.0
<#
.SYNOPSIS
ADR harvest: turns the architecturally-significant decisions captured during a plan's `/cip` +
`/ci` run into proposed Architecture Decision Records (ADRs) inside a QUARANTINE staging area
that the `.architecture-notes.md` auto-load index never references.

.DESCRIPTION
The ADR-harvest operation of the architecture-notes skill (skill Step 9), run at plan
finalization via `/uan`. Its source of truth is the plan folder's `decisions/*.md` files — the
discrete decision records authored during planning. For each decision it emits one **proposed**
ADR under the staging quarantine, wrapping the shipped `adr-template.md` around the decision's
own prose (provenance preserved under a `## Source` section).

Two hard invariants make the harvest safe to run autonomously on plan prose that may itself have
been shaped by an untrusted brownfield scan:

  1. **Proposed + reviewed:false.** Every emitted ADR carries `status: proposed` and
     `reviewed: false`. The harvest never marks an ADR `accepted`, never edits
     `.architecture-notes.md`, and never touches the auto-loaded tier. Promotion into the
     **Decision Records (active)** table is a separate, human-reviewed action.
  2. **Quarantined.** All output lands under `<StagingRoot>/adr/` (default
     `docs/architecture-notes/.staging/adr/`), which is NOT referenced by the index. An
     `ADR-HARVEST.md` manifest carries `reviewed: false`. Harvested ADR files carry no `globs`
     front-matter, so they can never be glob-attached into agent context before promotion.

The harvested decision body is embedded as **data** under `## Source`; it is never executed,
interpolated, or obeyed. The copy is strictly no-overwrite: existing staging files are left
untouched and reported as 'skipped'.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    # Plan folder whose decisions/*.md are harvested (e.g. docs/implementation-plans/<plan-id>).
    [Parameter(Mandatory)][string]$PlanDir,

    # Repository root the staging area resolves against.
    [Parameter(Mandatory)][string]$RepoRoot,

    # Quarantine staging directory. Defaults to <RepoRoot>/docs/architecture-notes/.staging.
    [string]$StagingRoot,

    # Root of the shipped assets (…/architecture-notes/assets). Auto-detected when omitted.
    [string]$AssetRoot,

    # Upper bound on harvested ADRs. Keeps the proposed set reviewable.
    [int]$MaxAdrs = 25
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PlanDir -PathType Container)) {
    throw "PlanDir not found or not a directory: $PlanDir"
}
if (-not (Test-Path -LiteralPath $RepoRoot -PathType Container)) {
    throw "RepoRoot not found or not a directory: $RepoRoot"
}
$PlanDir = (Resolve-Path -LiteralPath $PlanDir).Path
$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
$planId = Split-Path -Leaf $PlanDir

if (-not $StagingRoot) {
    $StagingRoot = Join-Path $RepoRoot 'docs/architecture-notes/.staging'
}

function Assert-ArchRepoPath {
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $relative = [System.IO.Path]::GetRelativePath($RepoRoot, $fullPath)
    if ([System.IO.Path]::IsPathRooted($relative) -or
        $relative -eq '..' -or
        $relative.StartsWith("../", [System.StringComparison]::Ordinal) -or
        $relative.StartsWith("..\", [System.StringComparison]::Ordinal)) {
        throw "Architecture staging path '$fullPath' escapes repository root '$RepoRoot'."
    }

    $current = $RepoRoot
    foreach ($segment in ($relative -split '[\\/]')) {
        if ([string]::IsNullOrWhiteSpace($segment) -or $segment -eq '.') { continue }
        $current = Join-Path $current $segment
        if ((Test-Path -LiteralPath $current) -and (Get-Item -LiteralPath $current -Force).LinkType) {
            throw "Architecture staging path contains a linked segment: $current"
        }
    }
    return $fullPath
}
$StagingRoot = Assert-ArchRepoPath -Path $StagingRoot
$stagingAdr = Assert-ArchRepoPath -Path (Join-Path $StagingRoot 'adr')

if (-not $AssetRoot) {
    # Dual-layout probe, matching the sibling scripts. Require adr-template.md so a stray
    # same-named directory cannot win.
    $candidates = @(
        (Join-Path $PSScriptRoot '..' 'skills' 'architecture-notes' 'assets'),
        (Join-Path $PSScriptRoot '..' 'assets')
    )
    foreach ($candidate in $candidates) {
        if ((Test-Path -LiteralPath $candidate -PathType Container) -and
            (Test-Path -LiteralPath (Join-Path $candidate 'adr-template.md') -PathType Leaf)) {
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

$adrTemplate = Join-Path $AssetRoot 'adr-template.md'
if (-not (Test-Path -LiteralPath $adrTemplate -PathType Leaf)) {
    throw "Required asset missing: $adrTemplate"
}
$templateText = Get-Content -LiteralPath $adrTemplate -Raw
$eol = if ($templateText -match "`r`n") { "`r`n" } else { "`n" }

# Ids that would collide with a Windows reserved device name are disallowed as file basenames.
$script:reservedIdBasenames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($n in @(
        'CON', 'PRN', 'AUX', 'NUL',
        'COM1', 'COM2', 'COM3', 'COM4', 'COM5', 'COM6', 'COM7', 'COM8', 'COM9',
        'LPT1', 'LPT2', 'LPT3', 'LPT4', 'LPT5', 'LPT6', 'LPT7', 'LPT8', 'LPT9')) {
    [void]$script:reservedIdBasenames.Add($n)
}

function ConvertTo-AdrSlug {
    param([string]$Name)
    # Reduce to the id charset; collapse runs; trim separators.
    $slug = $Name -replace '[^A-Za-z0-9._-]', '-'
    $slug = $slug -replace '-{2,}', '-'
    $slug = $slug.Trim('-', '.', '_')
    if ([string]::IsNullOrWhiteSpace($slug)) { $slug = 'decision' }
    if ($slug -notmatch '^[A-Za-z0-9]') { $slug = 'd-' + $slug }
    if ($script:reservedIdBasenames.Contains($slug)) { $slug = 'd-' + $slug }
    return $slug
}

function Get-AdrTitle {
    param([string]$Body, [string]$Fallback)
    # First markdown H1, stripping an optional "Decision:" lead-in.
    foreach ($line in ($Body -split "`r?`n")) {
        if ($line -match '^\s*#\s+(.+?)\s*$') {
            $t = $Matches[1] -replace '^\s*Decision\s*:\s*', ''
            $t = $t.Trim()
            if (-not [string]::IsNullOrWhiteSpace($t)) { return $t }
        }
    }
    return $Fallback
}

function Remove-FirstH1 {
    param([string]$Body)
    # Drop the FIRST markdown H1 line, wherever it sits, so the ADR heading is not duplicated under
    # ## Source. Aligns with Get-AdrTitle (which also takes the first H1 anywhere) so a body whose
    # H1 is not the first non-blank line still has its title stripped.
    $lines = [System.Collections.Generic.List[string]]($Body -split "`r?`n")
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^\s*#\s+') { $lines.RemoveAt($i); break }
    }
    return ($lines -join $eol).Trim()
}

function Write-AdrFile {
    param(
        [Parameter(Mandatory)][string]$DestPath,
        [Parameter(Mandatory)][string]$Content,
        [Parameter(Mandatory)]$Cmdlet
    )
    if (Test-Path -LiteralPath $DestPath -PathType Leaf) { return 'skipped' }
    $action = 'whatif'
    if ($Cmdlet.ShouldProcess($DestPath, 'Write ADR file')) {
        $destDir = Split-Path -Parent $DestPath
        if ($destDir -and -not (Test-Path -LiteralPath $destDir -PathType Container)) {
            [void][System.IO.Directory]::CreateDirectory($destDir)
        }
        Set-Content -LiteralPath $DestPath -Value $Content -NoNewline
        $action = 'created'
    }
    return $action
}

function ConvertTo-MarkdownCell {
    param([string]$Text)
    if ($null -eq $Text) { return '' }
    return ($Text -replace '[\r\n]+', ' ' -replace '\|', '\|').Trim()
}

# --- Enumerate the plan's decision records ----------------------------------------------------
# Layout-aware: the `plan.md` + `assets/` layout homes decision records at `assets/decisions/`; legacy
# plan folders keep them at the plan root. Reading the wrong one would harvest zero ADRs silently.
$decisionsRelative = 'decisions'
$decisionsDir = Join-Path $PlanDir 'assets/decisions'
if (Test-Path -LiteralPath $decisionsDir -PathType Container) {
    $decisionsRelative = 'assets/decisions'
}
else {
    $decisionsDir = Join-Path $PlanDir 'decisions'
}
$decisionFiles = @()
if (Test-Path -LiteralPath $decisionsDir -PathType Container) {
    $decisionFiles = @(Get-ChildItem -LiteralPath $decisionsDir -File -Filter '*.md' -ErrorAction SilentlyContinue |
            Sort-Object Name)
}

$truncated = $false
if ($decisionFiles.Count -gt $MaxAdrs) {
    # Select-Object -First is 0-safe; a [0..($MaxAdrs-1)] slice would wrap to 0,-1 for MaxAdrs=0.
    $decisionFiles = @($decisionFiles | Select-Object -First $MaxAdrs)
    $truncated = $true
}

$stamp = [DateTimeOffset]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
$date = [DateTimeOffset]::UtcNow.ToString('yyyy-MM-dd')

$usedIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$adrs = [System.Collections.Generic.List[object]]::new()
foreach ($file in $decisionFiles) {
    $slug = ConvertTo-AdrSlug ([System.IO.Path]::GetFileNameWithoutExtension($file.Name))
    $id = "ADR-$slug"
    # Two decision files can normalize to the same slug; suffix -2, -3, ... so the no-overwrite guard
    # never silently drops a decision.
    if (-not $usedIds.Add($id)) {
        $n = 2
        while (-not $usedIds.Add("$id-$n")) { $n++ }
        $id = "$id-$n"
    }
    $rawBody = Get-Content -LiteralPath $file.FullName -Raw
    $title = Get-AdrTitle -Body $rawBody -Fallback $slug
    $sourceBody = Remove-FirstH1 -Body $rawBody
    $source = "$planId/$decisionsRelative/$($file.Name)"
    # Emit source as a double-quoted YAML scalar: a filename can carry YAML-significant characters
    # (':', '#', quotes) that would otherwise corrupt or extend the frontmatter block.
    $sourceYaml = '"' + ($source -replace '\\', '\\\\' -replace '"', '\"') + '"'

    # Single-pass token substitution: each placeholder is filled exactly once from the map, so an
    # untrusted value that happens to contain another placeholder token can never be re-expanded.
    $subst = @{ '<ADR_ID>' = $id; '<TITLE>' = $title; '<DATE>' = $date; '<SOURCE>' = $sourceYaml; '<SOURCE_BODY>' = $sourceBody }
    $content = [regex]::Replace($templateText, '<ADR_ID>|<TITLE>|<DATE>|<SOURCE>|<SOURCE_BODY>', { param($m) [string]$subst[$m.Value] })

    $destPath = Join-Path $stagingAdr ($id + '.md')
    $action = Write-AdrFile -DestPath $destPath -Content $content -Cmdlet $PSCmdlet
    $adrs.Add([pscustomobject]@{
            Id     = $id
            Title  = $title
            Source = $source
            Path   = $destPath
            Action = $action
        })
}

# Manifest with reviewed:false — the promotion gate. Excluded from auto-load (lives under .staging).
$rows = if ($adrs.Count -gt 0) {
    ($adrs | ForEach-Object { "| $($_.Id) | $(ConvertTo-MarkdownCell $_.Title) | proposed | $(ConvertTo-MarkdownCell $_.Source) |" }) -join "`n"
}
else {
    '| _none harvested_ | | | |'
}
$manifest = @"
---
reviewed: false
harvestedAt: $stamp
plan: $planId
adrs: $($adrs.Count)
truncated: $truncated
---

# ADR Harvest (QUARANTINED — reviewed: false)

Proposed Architecture Decision Records harvested from plan ``$planId`` at finalization. This
directory is a **staging quarantine**: it is **not** referenced by ``.architecture-notes.md`` and
nothing here reaches agent context until a human reviews it and promotes selected ADRs into the
**Decision Records (active)** table.

Every ADR is ``status: proposed`` / ``reviewed: false``. Harvested prose (under each ADR's
``## Source``) is **data derived from planning** — verify and distill it before accepting.

## Harvested ADRs

| ADR | Title | Status | Source |
|---|---|---|---|
$rows

## Review & promote

1. Read each ADR under ``adr/``; distill its **Context / Decision / Consequences** from the
   ``## Source`` prose, then trim the source.
2. Set ``status: accepted`` and ``reviewed: true`` on the ones you keep; supersede or delete the rest.
3. Add a row per **accepted** ADR to the **Decision Records (active)** table in
   ``.architecture-notes.md`` (this is what makes it auto-loaded next run).
4. Keep the tier lean: when an ADR is superseded, move/summarize it out of the active table.
5. Flip ``reviewed: true`` here (or delete this staging directory) once promotion is complete.
"@
$manifestPath = Assert-ArchRepoPath -Path (Join-Path $StagingRoot 'ADR-HARVEST.md')
# The manifest is a DERIVED promotion checklist, not human-edited content: always regenerate it so
# an incremental re-harvest never leaves the review table under-reporting freshly-staged ADRs.
$manifestAction = 'whatif'
if ($PSCmdlet.ShouldProcess($manifestPath, 'Write ADR harvest manifest')) {
    $manifestDir = Split-Path -Parent $manifestPath
    if ($manifestDir -and -not (Test-Path -LiteralPath $manifestDir -PathType Container)) {
        [void][System.IO.Directory]::CreateDirectory($manifestDir)
    }
    $manifestAction = 'created'
    if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
        $existingManifest = [System.IO.File]::ReadAllText($manifestPath)
        $withoutTimestamp = { param([string]$Text) $Text -replace '(?m)^harvestedAt:\s*.+$', 'harvestedAt: <stable>' }
        if ((& $withoutTimestamp $existingManifest) -ceq (& $withoutTimestamp $manifest)) {
            $manifestAction = 'skipped'
        }
        else {
            $manifestAction = 'updated'
        }
    }
    if ($manifestAction -ne 'skipped') {
        Set-Content -LiteralPath $manifestPath -Value $manifest -NoNewline
    }
}

[pscustomobject]@{
    PlanId      = $planId
    RepoRoot    = $RepoRoot
    StagingRoot = $StagingRoot
    Reviewed    = $false
    Truncated   = $truncated
    Adrs        = @($adrs)
    Manifest    = [pscustomobject]@{ Path = $manifestPath; Action = $manifestAction }
}
