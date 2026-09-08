#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RepoRoot = (Get-Location).Path,
    [Parameter(Mandatory)][string]$PlanReference,
    [Parameter(Mandatory)]
    [ValidatePattern('^(?:[0-9a-f]{40}|[0-9a-f]{64})$')]
    [string]$PinnedBaseOid
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'PlanState.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'SecretGuard.psm1') -Force

$utf8 = [System.Text.UTF8Encoding]::new($false, $true)
$root = (Resolve-Path -LiteralPath ([System.IO.Path]::GetFullPath($RepoRoot))).Path
$relativePath = 'docs/feedback/recent-learning.md'

function Invoke-GitBytes {
    param([Parameter(Mandatory)][string[]]$Argument)

    $start = [System.Diagnostics.ProcessStartInfo]::new()
    $start.FileName = 'git'
    $start.WorkingDirectory = $root
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($item in @('-C', $root) + $Argument) {
        [void]$start.ArgumentList.Add($item)
    }
    $process = [System.Diagnostics.Process]::Start($start)
    $memory = [System.IO.MemoryStream]::new()
    try {
        $process.StandardOutput.BaseStream.CopyTo($memory)
        $errorText = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            Bytes = $memory.ToArray()
            Error = $errorText.Trim()
        }
    }
    finally {
        $memory.Dispose()
        $process.Dispose()
    }
}

function Test-GitObject {
    param([Parameter(Mandatory)][string[]]$Argument)
    & git -C $root @Argument 2>$null
    return $LASTEXITCODE -eq 0
}

function New-UntrustedLearningBlock {
    param([Parameter(Mandatory)][string]$Text)
    do {
        $token = 'UNTRUSTED_INPUT_' + [guid]::NewGuid().ToString('N')
    } while ($Text.Contains($token, [System.StringComparison]::Ordinal))
    return "<$token>`n$Text`n</$token>"
}

function New-ClosedResult {
    param([Parameter(Mandatory)][ValidateSet('missing', 'empty', 'valid', 'stale')][string]$Status)
    return [pscustomobject][ordered]@{
        Status = $Status
        PlanId = $planId
        PlanSlug = $slug
        PinnedBaseOid = $PinnedBaseOid
        Items = @()
    }
}

$inventory = @(Get-PlanInventory -RepoRoot $root)
$plan = Resolve-Plan -Reference $PlanReference -RepoRoot $root -Inventory $inventory
$planId = [string]$plan.Id
$slug = [string]$plan.Slug

if (-not (Test-GitObject -Argument @('cat-file', '-e', "${PinnedBaseOid}^{commit}"))) {
    throw "Pinned base '$PinnedBaseOid' is not a readable commit."
}
$blob = Invoke-GitBytes -Argument @('show', "${PinnedBaseOid}:${relativePath}")
if ($blob.ExitCode -ne 0) {
    return New-ClosedResult -Status missing
}
if ($blob.Bytes.Length -gt 16KB) {
    throw 'Recent-learning input exceeds 16 KiB UTF-8.'
}
try {
    $content = $utf8.GetString($blob.Bytes)
}
catch {
    throw 'Recent-learning input is not valid UTF-8.'
}
$content = $content.Replace("`r`n", "`n")

$pattern = '(?s)\A# Recent learning\n\nSource plan: `(?<id>[0-9a-f]{6}) (?<slug>[a-z0-9][a-z0-9-]*)`\nSource commit: `(?<oid>[0-9a-f]{40}|[0-9a-f]{64})`\n\n## Lessons\n\n(?<body>.+?)\n?\z'
$document = [regex]::Match($content, $pattern)
if (-not $document.Success) {
    throw 'Recent-learning input is malformed; expected strict Markdown metadata and an explicit ## Lessons section.'
}
if (@(Find-HighConfidenceSecret -Value $content).Count -gt 0) {
    throw 'Recent-learning input contains secret material; consumption refused.'
}

$sourceId = $document.Groups['id'].Value
$sourceSlug = $document.Groups['slug'].Value
$sourceCommit = $document.Groups['oid'].Value
if ($sourceId -cne $planId -or $sourceSlug -cne $slug) {
    return New-ClosedResult -Status stale
}
if (-not (Test-GitObject -Argument @('cat-file', '-e', "${sourceCommit}^{commit}"))) {
    return New-ClosedResult -Status stale
}
& git -C $root merge-base --is-ancestor $sourceCommit $PinnedBaseOid 2>$null
if ($LASTEXITCODE -ne 0) {
    return New-ClosedResult -Status stale
}

$sourcePlanPaths = @(
    & git -C $root grep -l -F "<!-- plan-id: $planId -->" $sourceCommit -- `
        'docs/implementation-plans/**/plan.md' 2>$null |
        ForEach-Object {
            $prefix = "${sourceCommit}:"
            if ([string]$_ -like "$prefix*") {
                ([string]$_).Substring($prefix.Length)
            }
        } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Sort-Object -Unique
)
if ($LASTEXITCODE -notin @(0, 1) -or $sourcePlanPaths.Count -ne 1) {
    return New-ClosedResult -Status stale
}
$sourcePlan = Invoke-GitBytes -Argument @('show', "${sourceCommit}:$($sourcePlanPaths[0])")
if ($sourcePlan.ExitCode -ne 0) { return New-ClosedResult -Status stale }
$sourcePlanText = $utf8.GetString($sourcePlan.Bytes).Replace("`r`n", "`n")
$sourcePlanId = [regex]::Match($sourcePlanText, '(?m)^<!-- plan-id: (?<id>[0-9a-f]{6}) -->$')
$sourceSteps = @([regex]::Matches($sourcePlanText, '(?m)^- \[(?<mark>[ xX])\] \d+\.\d+\b'))
if (-not $sourcePlanId.Success -or $sourcePlanId.Groups['id'].Value -cne $planId -or
    $sourceSteps.Count -eq 0 -or
    @($sourceSteps | Where-Object { $_.Groups['mark'].Value -eq ' ' }).Count -gt 0) {
    return New-ClosedResult -Status stale
}

$body = $document.Groups['body'].Value
if ($body -ceq 'None.') {
    return New-ClosedResult -Status empty
}

$lessonRows = @($body -split "`n")
if ($lessonRows.Count -gt 10) {
    throw 'Recent-learning input exceeds the 10-lesson limit.'
}
$lessons = [System.Collections.Generic.List[object]]::new()
foreach ($row in $lessonRows) {
    $item = [regex]::Match($row, '^- (?<lesson>[^`\r\n]+?) — `(?<citation>[^`\r\n]+)`$')
    if (-not $item.Success -or [string]::IsNullOrWhiteSpace($item.Groups['lesson'].Value)) {
        throw 'Recent-learning input contains a malformed or empty lesson item.'
    }
    $citation = $item.Groups['citation'].Value
    if ([System.IO.Path]::IsPathRooted($citation) -or $citation.Contains('\') -or
        $citation.StartsWith('/') -or
        @($citation.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count -gt 0 -or
        -not (Test-GitObject -Argument @('cat-file', '-e', "${sourceCommit}:${citation}"))) {
        throw "Recent-learning citation '$citation' is missing or is not a confined source-commit path."
    }
    $lessons.Add([pscustomobject][ordered]@{
            lesson = $item.Groups['lesson'].Value.Trim()
            citation = $citation
        })
}

$wrapped = New-UntrustedLearningBlock -Text $body

[pscustomobject][ordered]@{
    Status = 'valid'
    PlanId = $planId
    PlanSlug = $slug
    PinnedBaseOid = $PinnedBaseOid
    SourceCommit = $sourceCommit
    LessonCount = $lessons.Count
    Items = @(
        [pscustomobject][ordered]@{
            source = $relativePath
            wrappedContent = $wrapped
            lessons = $lessons.ToArray()
        }
    )
}
