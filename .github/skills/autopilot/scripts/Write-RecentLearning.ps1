#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RepoRoot = (Get-Location).Path,
    [Parameter(Mandatory)][string]$PlanReference,
    [Parameter(Mandatory)]
    [ValidatePattern('^(?:[0-9a-f]{40}|[0-9a-f]{64})$')]
    [string]$SourceCommit,
    [AllowEmptyCollection()][string[]]$Lesson = @(),
    [AllowEmptyCollection()][string[]]$Citation = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'PlanState.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'SecretGuard.psm1') -Force

$utf8 = [System.Text.UTF8Encoding]::new($false, $true)
$root = (Resolve-Path -LiteralPath ([System.IO.Path]::GetFullPath($RepoRoot))).Path
$relativePath = 'docs/feedback/recent-learning.md'
$target = Join-Path $root ($relativePath -replace '/', [System.IO.Path]::DirectorySeparatorChar)

function Assert-RecentLearningWritePath {
    param(
        [Parameter(Mandatory)][string]$Path,
        [switch]$AllowMissing
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $comparison = if ($IsWindows) {
        [System.StringComparison]::OrdinalIgnoreCase
    } else {
        [System.StringComparison]::Ordinal
    }
    $relative = [System.IO.Path]::GetRelativePath($root, $fullPath)
    if ($relative.StartsWith('..') -or [System.IO.Path]::IsPathRooted($relative)) {
        throw "Recent-learning write path '$Path' escapes the repository root."
    }
    $current = $root
    foreach ($segment in @($relative -split '[\\/]' | Where-Object { $_.Length -gt 0 })) {
        $current = Join-Path $current $segment
        if (-not (Test-Path -LiteralPath $current)) {
            if ($AllowMissing) { continue }
            throw "Recent-learning write path '$current' does not exist."
        }
        $item = Get-Item -LiteralPath $current -Force
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 -or
            ($item.PSObject.Properties.Name -contains 'LinkType' -and
                -not [string]::IsNullOrWhiteSpace([string]$item.LinkType))) {
            throw "Recent-learning write path refuses link or reparse point '$current'."
        }
    }

    $physicalRoot = Resolve-PhysicalRepoPath -Path $root
    $physicalPath = Resolve-PhysicalRepoPath -Path $fullPath
    $rootPrefix = $physicalRoot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    if (-not $physicalPath.StartsWith($rootPrefix, $comparison)) {
        throw "Recent-learning write path '$Path' escapes the physical repository root."
    }
}

function Invoke-GitText {
    param([Parameter(Mandatory)][string[]]$Argument)
    $output = @(& git -C $root @Argument 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Git command failed: git $($Argument -join ' '): $(($output -join ' ').Trim())"
    }
    return ($output -join "`n").Trim()
}

function Get-GitBlobText {
    param(
        [Parameter(Mandatory)][string]$Commit,
        [Parameter(Mandatory)][string]$Path
    )
    $text = @(& git -C $root show "${Commit}:${Path}" 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Source commit '$Commit' does not contain '$Path'."
    }
    return ($text -join "`n")
}

function Assert-RepoRelativeCitation {
    param([Parameter(Mandatory)][string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or
        [System.IO.Path]::IsPathRooted($Path) -or
        $Path.Contains('\') -or
        $Path.Contains("`r") -or
        $Path.Contains("`n") -or
        $Path.StartsWith('/') -or
        @($Path.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count -gt 0 -or
        $Path.Contains('`')) {
        throw "Recent-learning citation '$Path' is not a confined repo-relative path."
    }
    & git -C $root cat-file -e "${SourceCommit}:${Path}" 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "Recent-learning citation '$Path' does not exist at source commit '$SourceCommit'."
    }
}

$inventory = @(Get-PlanInventory -RepoRoot $root)
$plan = Resolve-Plan -Reference $PlanReference -RepoRoot $root -Inventory $inventory
$planId = [string]$plan.Id
$slug = [string]$plan.Slug
$planPath = [System.IO.Path]::GetRelativePath($root, (Join-Path ([string]$plan.Path) 'plan.md')).Replace('\', '/')

$resolvedSource = Invoke-GitText -Argument @('rev-parse', '--verify', "${SourceCommit}^{commit}")
if ($resolvedSource -cne $SourceCommit) {
    throw 'Source commit must be the full lowercase commit ID.'
}
$head = Invoke-GitText -Argument @('rev-parse', 'HEAD')
if ($head -cne $SourceCommit) {
    throw 'Source commit must equal the current HEAD before the handoff replacement.'
}

$sourcePlanText = Get-GitBlobText -Commit $SourceCommit -Path $planPath
$sourceId = [regex]::Match($sourcePlanText, '(?m)^<!-- plan-id: (?<id>[0-9a-f]{6}) -->$')
$steps = @([regex]::Matches(
        $sourcePlanText,
        '(?m)^\s*-\s\[(?<mark>[ xX~])\]\s+\d+\.\d+[a-z]?\s'
    ))
if (-not $sourceId.Success -or $sourceId.Groups['id'].Value -cne $planId -or
    $steps.Count -eq 0 -or
    @($steps | Where-Object { $_.Groups['mark'].Value -in @(' ', '~') }).Count -gt 0) {
    throw "Source commit '$SourceCommit' does not contain the completed source plan '$planId $slug'."
}

if ($Lesson.Count -ne $Citation.Count) {
    throw 'Each recent-learning lesson requires exactly one citation.'
}
if ($Lesson.Count -gt 10) {
    throw 'Recent-learning handoff exceeds the 10-lesson limit.'
}

$lines = [System.Collections.Generic.List[string]]::new()
for ($i = 0; $i -lt $Lesson.Count; $i++) {
    $lessonText = $Lesson[$i].Trim()
    $citationPath = $Citation[$i].Trim()
    if ([string]::IsNullOrWhiteSpace($lessonText) -or $lessonText.Contains("`n") -or
        $lessonText.Contains("`r") -or $lessonText.Contains('`') -or $lessonText.Length -gt 500) {
        throw "Recent-learning lesson $($i + 1) must be one concise non-empty line without backticks."
    }
    Assert-RepoRelativeCitation -Path $citationPath
    $lines.Add("- $lessonText — ``$citationPath``")
}

$body = if ($lines.Count -eq 0) { 'None.' } else { $lines -join "`n" }
$content = @"
# Recent learning

Source plan: ``$planId $slug``
Source commit: ``$SourceCommit``

## Lessons

$body
"@ + "`n"

$secretTypes = @(Find-HighConfidenceSecret -Value $content)
if ($secretTypes.Count -gt 0) {
    throw "Recent-learning handoff contains secret material ($($secretTypes -join ', ')); replacement refused."
}
$bytes = $utf8.GetBytes($content)
if ($bytes.Length -gt 16KB) {
    throw 'Recent-learning handoff exceeds 16 KiB UTF-8.'
}

$parent = Split-Path -Parent $target
[void](Assert-RecentLearningWritePath -Path $parent -AllowMissing)
[void](New-Item -ItemType Directory -Path $parent -Force)
[void](Assert-RecentLearningWritePath -Path $parent)
[void](Assert-RecentLearningWritePath -Path $target -AllowMissing)
$temp = Join-Path $parent ('.recent-learning.' + [guid]::NewGuid().ToString('N') + '.tmp')
try {
    [void](Assert-RecentLearningWritePath -Path $temp -AllowMissing)
    [System.IO.File]::WriteAllBytes($temp, $bytes)
    [void](Assert-RecentLearningWritePath -Path $temp)
    [void](Assert-RecentLearningWritePath -Path $target -AllowMissing)
    [System.IO.File]::Move($temp, $target, $true)
    [void](Assert-RecentLearningWritePath -Path $target)
}
finally {
    if (Test-Path -LiteralPath $temp) {
        Remove-Item -LiteralPath $temp -Force
    }
}

[pscustomobject][ordered]@{
    Path = $relativePath
    Plan = "$planId $slug"
    SourceCommit = $SourceCommit
    LessonCount = $lines.Count
    ByteCount = $bytes.Length
}
