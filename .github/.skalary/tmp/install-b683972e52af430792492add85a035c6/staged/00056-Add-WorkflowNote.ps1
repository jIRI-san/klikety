#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('CrLog', 'Learnings', 'Capture')]
    [string]$Kind,
    [Parameter(Mandatory)][string]$PlanDir,
    [Parameter(Mandatory)][ValidateRange(0, 999)][int]$Phase,
    [string]$Step,
    [string]$Message,
    [ValidateSet('Critical', 'High', 'Med', 'Low')][string]$Sev,
    [ValidateSet('code-review', 'discovery', 'note')][string]$Src,
    [ValidateSet('rework>1', 'plan-contradiction', 'reusable-pattern')][string]$Trigger,
    [ValidateSet(
        'security',
        'correctness-reliability',
        'architecture-patterns',
        'performance',
        'testing-evidence',
        'maintainability-consistency',
        'operability-observability'
    )][string]$Concern,
    [ValidatePattern('^REQ-[1-9][0-9]*$')][string[]]$Requirement = @(),
    [ValidateSet('cr', 'dr', 'none')][string]$ReviewType = 'none',
    [string]$RepoRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'PlanState.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
}
$root = (Resolve-Path -LiteralPath ([System.IO.Path]::GetFullPath($RepoRoot))).Path
$planDirFull = (Resolve-Path -LiteralPath ([System.IO.Path]::GetFullPath($PlanDir))).Path
$inventory = @(Get-PlanInventory -RepoRoot $root)
$record = @($inventory | Where-Object {
        [string]::Equals(
            [System.IO.Path]::GetFullPath([string]$_.Path),
            $planDirFull,
            [System.StringComparison]::OrdinalIgnoreCase
        )
    })
if ($record.Count -ne 1) {
    throw "Plan folder '$planDirFull' is not a unique member of the repository plan inventory."
}
$requirements = [string[]]@($Requirement | Select-Object -Unique)
[Array]::Sort($requirements, [System.StringComparer]::Ordinal)
if ($requirements.Count -gt 0) {
    $planMetadata = Get-PlanMetadata -Path (Join-Path $planDirFull 'plan.md') -RepoRoot $root
    $knownRequirements = @($planMetadata.Requirements.Values | ForEach-Object { [string]$_.Id })
    foreach ($requirementId in $requirements) {
        if ($knownRequirements -notcontains $requirementId) {
            throw "Requirement '$requirementId' does not belong to plan '$($record[0].Id)'."
        }
    }
}
if ($Message -and -not $Concern) {
    throw 'Workflow-note entries require -Concern.'
}
if ($Message -and $Kind -eq 'CrLog' -and (-not $Sev -or $ReviewType -eq 'none')) {
    throw 'CrLog notes require -Sev and -ReviewType cr or dr.'
}
if ($Message -and $Kind -eq 'Learnings' -and (-not $Step -or -not $Trigger)) {
    throw 'Learnings notes require -Step and -Trigger.'
}
if ($Message -and $Kind -eq 'Learnings' -and ($Src -or $Sev)) {
    throw 'Learnings notes do not accept -Src or -Sev.'
}
if ($Message -and $Kind -ne 'Learnings' -and $Trigger) {
    throw "$Kind notes do not accept -Trigger."
}
if ($Step -and ($Step -notmatch '^[0-9]+\.[0-9]+[a-z]?$' -or
        [int]($Step.Split('.')[0]) -ne $Phase)) {
    throw "Workflow-note step '$Step' does not belong to phase $Phase."
}

$kindConfig = @{
    CrLog = @{ Header = '## CR Capture'; Asset = 'CrLog' }
    Learnings = @{ Header = '## Learnings Capture'; Asset = 'Learnings' }
    Capture = @{ Header = '## Capture'; Asset = 'Capture' }
}[$Kind]
$filePath = Resolve-PlanAssetPath -PlanDir $planDirFull -Kind $kindConfig.Asset `
    -RepoRoot $root -Inventory $inventory

$clean = if ($Message) {
    [regex]::Replace($Message, '[\u0000-\u001F\u007F\u0085\u2028\u2029]+', ' ')
}
else {
    $null
}
if ($clean) {
    $clean = [regex]::Replace(
        $clean.Replace('[', '(').Replace(']', ')').Replace('`', "'").Replace('|', '/'),
        '\s+',
        ' '
    ).Trim()
}
if ($Message -and [string]::IsNullOrWhiteSpace($clean)) {
    throw 'Workflow-note body is empty after sanitization.'
}

$stepToken = if ($Step) { $Step } else { '-' }
$requirementToken = if ($requirements.Count -eq 0) { '-' } else { $requirements -join ',' }
$metadata = @("[concern:$Concern]", "[req:$requirementToken]", "[review:$ReviewType]")
if ($Kind -ne 'Learnings') {
    $metadata = @(
        "[src:$(if ($Src) { $Src } else { 'note' })]",
        "[sev:$(if ($Sev) { $Sev } else { 'Med' })]"
    ) + $metadata
}
elseif ($Trigger) {
    $metadata = @("[trigger:$Trigger]") + $metadata
}
$entry = if ($clean) { "- [$stepToken] $($metadata -join ' ') $clean" } else { $null }
if ($entry -and [System.Text.Encoding]::UTF8.GetByteCount($entry) -gt 16KB) {
    throw 'Workflow-note record exceeds 16 KiB.'
}

$existing = if (Test-Path -LiteralPath $filePath -PathType Leaf) {
    [System.IO.File]::ReadAllText($filePath).Replace("`r`n", "`n").TrimEnd("`n")
}
else {
    ''
}
$section = "$($kindConfig.Header)`nPhase: $Phase"
$appended = $false
$content = if (-not $existing.Contains($section, [System.StringComparison]::Ordinal)) {
    $appended = [bool]$entry
    (@($existing, $section, '', $(if ($entry) { $entry } else { 'No entries for this phase.' })) |
        Where-Object { $null -ne $_ }) -join "`n"
}
elseif ($entry -and -not $existing.Contains($entry, [System.StringComparison]::Ordinal)) {
    $lines = [System.Collections.Generic.List[string]]::new([string[]]$existing.Split("`n"))
    $start = -1
    for ($i = 0; $i -lt ($lines.Count - 1); $i++) {
        if ($lines[$i] -ceq $kindConfig.Header -and $lines[$i + 1] -ceq "Phase: $Phase") {
            $start = $i
            break
        }
    }
    if ($start -lt 0) { throw "Workflow-note section '$section' could not be located." }
    $end = $lines.Count
    for ($i = $start + 2; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^##\s') {
            $end = $i
            break
        }
    }
    $placeholder = -1
    $lastEntry = -1
    for ($i = $start + 2; $i -lt $end; $i++) {
        if ($lines[$i] -ceq 'No entries for this phase.') { $placeholder = $i }
        if ($lines[$i] -match '^\s*-\s') { $lastEntry = $i }
    }
    if ($placeholder -ge 0) {
        $lines[$placeholder] = $entry
    }
    elseif ($lastEntry -ge 0) {
        $lines.Insert($lastEntry + 1, $entry)
    }
    else {
        $lines.Insert($end, $entry)
    }
    $appended = $true
    $lines -join "`n"
}
else {
    $existing
}
$content = $content.TrimStart("`n").TrimEnd("`n") + "`n"
if ([System.Text.Encoding]::UTF8.GetByteCount($content) -gt 4MB) {
    throw 'Workflow note exceeds 4 MiB.'
}

$parent = Split-Path -Parent $filePath
[void](New-Item -ItemType Directory -Path $parent -Force)
$temp = Join-Path $parent ('.workflow-note.' + [guid]::NewGuid().ToString('N') + '.tmp')
try {
    [System.IO.File]::WriteAllText($temp, $content, [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::Move($temp, $filePath, $true)
}
finally {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force }
}

[pscustomobject]@{
    Kind = $Kind
    File = $filePath
    Phase = $Phase
    Appended = $appended
    Status = 'complete'
}
