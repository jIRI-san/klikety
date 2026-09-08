#requires -Version 7.0
<#
.SYNOPSIS
Emits a deterministic cross-plan index of REQ / RISK / decision records.

.DESCRIPTION
`/cip` reconciles a new plan against what earlier plans already required, risked, and decided. Parsing
every plan for that is expensive and grows with the archive, so this script aggregates the records once
into an addressable index — markdown for reading, JSON for tooling.

Coverage is the whole corpus: active *and* archived plans, in both the `plan.md` + `assets/` layout and
the legacy in-`plan.md` layout. Layout resolution is delegated to `Get-PlanMetadata`/`Resolve-PlanSection`
so the index sees exactly what the validator sees.

The output is deterministic by construction: plans and records are ordered by their own ids (ordinal, not
culture-sensitive), paths are repo-relative with forward slashes, and nothing timestamped or
environment-derived is emitted. Two runs over the same tree produce byte-identical text.

A plan that cannot be parsed is reported in an `errors` list rather than aborting the whole index: one
malformed archived plan must not make the index unusable mid-interview. The failure stays visible.
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path,

    [ValidateSet('Markdown', 'Json')]
    [string]$Format = 'Markdown',

    # Regex (case-insensitive) applied to plan titles and record text. The full index across an aged
    # archive is large; reconciling one topic should not mean reading all of it.
    [string]$Filter
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'PlanState.psm1') -Force -DisableNameChecking

$repoRootPath = [System.IO.Path]::GetFullPath($RepoRoot)

# A mis-rooted invocation must not read as "no prior art". The `prior-art` gate treats an empty index as
# "nothing to reconcile", so resolving a wrong -RepoRoot to a clean empty result would silently unblock
# drafting. A repo that genuinely has no plans still has the corpus folder.
$plansRoot = Join-Path $repoRootPath 'docs/implementation-plans'
if (-not (Test-Path -LiteralPath $plansRoot -PathType Container)) {
    throw "No plan corpus at '$plansRoot'. Pass -RepoRoot pointing at the repository root; an unresolvable root must not read as an empty index."
}

function ConvertTo-IndexText {
    param([AllowNull()][AllowEmptyString()][string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) { return '' }
    return ((($Text -replace "`r`n", ' ') -replace "[`r`n`t]+", ' ') -replace '\s+', ' ').Trim()
}

function ConvertTo-RepoRelativePath {
    param([Parameter(Mandatory)][string]$Path)

    $full = [System.IO.Path]::GetFullPath($Path)
    $prefix = $repoRootPath.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    if ($full.StartsWith($prefix, [System.StringComparison]::Ordinal)) {
        $full = $full.Substring($prefix.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    }
    return ($full -replace '\\', '/')
}

function Get-PlanTitle {
    param([Parameter(Mandatory)][AllowEmptyCollection()][AllowEmptyString()][string[]]$Line)

    foreach ($candidate in $Line) {
        if ($candidate -match '^\s*#\s+(?<title>.+?)\s*$') {
            return (ConvertTo-IndexText $Matches.title)
        }
    }
    return ''
}

$errors = [System.Collections.Generic.List[string]]::new()
$planRecords = [System.Collections.Generic.List[object]]::new()

foreach ($entry in @(Get-PlanInventory -RepoRoot $repoRootPath)) {
    $planFile = Join-Path $entry.Path 'plan.md'
    if (-not (Test-Path -LiteralPath $planFile -PathType Leaf)) {
        $errors.Add("$(ConvertTo-RepoRelativePath $entry.Path): no plan.md")
        continue
    }

    try {
        $metadata = Get-PlanMetadata -Path $planFile -RepoRoot $repoRootPath
    }
    catch {
        # Repo-relative so the message is identical on any machine — the index stays byte-deterministic
        # even when it is reporting a failure.
        $errors.Add("$(ConvertTo-RepoRelativePath $planFile): $(ConvertTo-IndexText ($_.Exception.Message.Replace($repoRootPath, '.')))")
        continue
    }

    $requirements = [System.Collections.Generic.List[object]]::new()
    foreach ($requirement in @($metadata.Requirements.Values | Sort-Object Number)) {
        $requirements.Add([ordered]@{
            id         = $requirement.Id
            text       = (ConvertTo-IndexText $requirement.Text)
            acceptance = (ConvertTo-IndexText $requirement.AcceptanceCriteria)
            steps      = (ConvertTo-IndexText $requirement.Steps)
        })
    }

    # Risks carry only their number in $metadata.Risks (the contiguity check consumes it as an int), so the
    # prose is read back off the resolved section lines rather than re-resolving the layout here.
    $riskRows = [System.Collections.Generic.List[object]]::new()
    foreach ($line in @($metadata.Sections['Risks'].Lines)) {
        if ($null -eq $line -or -not $line.Trim().StartsWith('|')) { continue }
        $cells = Split-MarkdownTableCells -Row $line
        if ($cells.Count -lt 2 -or $cells[0] -notmatch '^RISK-(?<num>\d+)$') { continue }
        $riskRows.Add([pscustomobject]@{
            Number     = [int]$Matches.num
            Record     = [ordered]@{
                id         = $cells[0]
                text       = (ConvertTo-IndexText $cells[1])
                mitigation = if ($cells.Count -ge 5) { (ConvertTo-IndexText $cells[4]) } else { '' }
            }
        })
    }
    $risks = @($riskRows | Sort-Object Number | ForEach-Object { $_.Record })

    $planRecords.Add([pscustomobject]@{
        Id      = $entry.Id
        Folder  = $entry.FolderName
        Record  = [ordered]@{
            id           = $entry.Id
            folderName   = $entry.FolderName
            planFile     = (ConvertTo-RepoRelativePath $planFile)
            title        = (Get-PlanTitle -Line $metadata.AllLines)
            scheme       = $entry.Scheme
            layout       = $metadata.Layout
            isArchived   = [bool]$entry.IsArchived
            requirements = @($requirements)
            risks        = $risks
            decisions    = @($metadata.Decisions | ForEach-Object { ConvertTo-IndexText $_ })
        }
    })
}

# Ordinal ordering, not Sort-Object: culture-aware comparison would let the same tree index differently on
# a different machine, which is exactly what the deterministic contract forbids.
$planRecords.Sort([System.Comparison[object]] {
    param($a, $b)
    $byId = [string]::CompareOrdinal($a.Id, $b.Id)
    if ($byId -ne 0) { return $byId }
    return [string]::CompareOrdinal($a.Folder, $b.Folder)
})
$errors.Sort([System.Comparison[string]] { param($a, $b) [string]::CompareOrdinal($a, $b) })

$plans = @($planRecords | ForEach-Object { $_.Record })

if (-not [string]::IsNullOrWhiteSpace($Filter)) {
    $pattern = [regex]::new($Filter, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    $matched = [System.Collections.Generic.List[object]]::new()
    foreach ($plan in $plans) {
        $titleHit = $pattern.IsMatch([string]$plan.title)
        $plan.requirements = @($plan.requirements | Where-Object { $titleHit -or $pattern.IsMatch("$($_.id) $($_.text) $($_.acceptance)") })
        $plan.risks = @($plan.risks | Where-Object { $titleHit -or $pattern.IsMatch("$($_.id) $($_.text) $($_.mitigation)") })
        $plan.decisions = @($plan.decisions | Where-Object { $titleHit -or $pattern.IsMatch($_) })
        if ($titleHit -or @($plan.requirements).Count -gt 0 -or @($plan.risks).Count -gt 0 -or @($plan.decisions).Count -gt 0) {
            $matched.Add($plan)
        }
    }
    $plans = @($matched)
}

$activeCount = @($plans | Where-Object { -not $_.isArchived }).Count
$archivedCount = @($plans | Where-Object { $_.isArchived }).Count
$reqCount = ($plans | ForEach-Object { @($_.requirements).Count } | Measure-Object -Sum).Sum
$riskCount = ($plans | ForEach-Object { @($_.risks).Count } | Measure-Object -Sum).Sum
$decisionCount = ($plans | ForEach-Object { @($_.decisions).Count } | Measure-Object -Sum).Sum

if ($Format -eq 'Json') {
    $document = [ordered]@{
        schema  = 'plan-index/v1'
        filter  = if ($Filter) { $Filter } else { '' }
        totals  = [ordered]@{
            plans        = $plans.Count
            active       = $activeCount
            archived     = $archivedCount
            requirements = [int]$reqCount
            risks        = [int]$riskCount
            decisions    = [int]$decisionCount
        }
        plans   = $plans
        errors  = @($errors)
    }
    return ($document | ConvertTo-Json -Depth 8)
}

$dash = [char]0x2014
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('# Plan Index')
$lines.Add('')
$lines.Add('<!-- Generated by scripts/skalary/Get-PlanIndex.ps1. Deterministic and untimestamped — do not hand-edit. -->')
$lines.Add('')
$lines.Add("Plans: $($plans.Count) (active $activeCount, archived $archivedCount) $dash requirements $([int]$reqCount), risks $([int]$riskCount), decisions $([int]$decisionCount)")
if ($Filter) {
    $lines.Add('')
    $lines.Add("Filtered by ``$Filter`` $dash this is a subset, not the full corpus.")
}

foreach ($plan in $plans) {
    $state = if ($plan.isArchived) { 'archived' } else { 'active' }
    $lines.Add('')
    $lines.Add("## $($plan.id) $dash $($plan.title)")
    $lines.Add('')
    $lines.Add("``$($plan.planFile)`` $dash $state $dash $($plan.layout) layout")

    if (@($plan.requirements).Count -gt 0) {
        $lines.Add('')
        $lines.Add('### Requirements')
        $lines.Add('')
        foreach ($requirement in $plan.requirements) {
            $lines.Add("- $($requirement.id) $dash $($requirement.text)")
        }
    }

    if (@($plan.risks).Count -gt 0) {
        $lines.Add('')
        $lines.Add('### Risks')
        $lines.Add('')
        foreach ($risk in $plan.risks) {
            $lines.Add("- $($risk.id) $dash $($risk.text)")
        }
    }

    if (@($plan.decisions).Count -gt 0) {
        $lines.Add('')
        $lines.Add('### Decisions')
        $lines.Add('')
        foreach ($decision in $plan.decisions) {
            $lines.Add("- $decision")
        }
    }
}

if ($errors.Count -gt 0) {
    $lines.Add('')
    $lines.Add('## Unindexed plans')
    $lines.Add('')
    foreach ($planError in $errors) {
        $lines.Add("- $planError")
    }
}

return ($lines -join "`n")
