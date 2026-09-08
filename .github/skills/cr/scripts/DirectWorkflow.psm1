#requires -Version 7.0

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'PlanState.psm1') -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'SecretGuard.psm1') -DisableNameChecking

$script:Utf8NoBom = [System.Text.UTF8Encoding]::new($false, $true)
$script:PathComparison = if ($IsWindows) {
    [System.StringComparison]::OrdinalIgnoreCase
}
else {
    [System.StringComparison]::Ordinal
}

function Invoke-DirectGit {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string[]]$Argument,
        [switch]$AllowFailure
    )

    $output = & git -C $RepoRoot @Argument 2>&1
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0 -and -not $AllowFailure) {
        throw "git $($Argument -join ' ') failed: $($output -join "`n")"
    }
    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = @($output | ForEach-Object { [string]$_ })
    }
}

function Get-GitBlobId {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$Revision,
        [Parameter(Mandatory)][string]$RelativePath
    )

    $blob = Invoke-DirectGit -RepoRoot $RepoRoot -Argument @(
        'rev-parse', '--verify', "$Revision`:$RelativePath"
    ) -AllowFailure
    if ($blob.ExitCode -ne 0 -or $blob.Output.Count -ne 1 -or
        $blob.Output[0] -cnotmatch '^[0-9a-f]{40,64}$') {
        throw "Confirmed criteria file '$RelativePath' is missing from baseline commit '$Revision'."
    }

    return $blob.Output[0]
}

function Get-ConfinedPlanText {
    param(
        [Parameter(Mandatory)][object]$Context,
        [Parameter(Mandatory)][string]$Path
    )

    $stream = Open-ConfinedPlanFile -Context $Context -Path $Path
    try {
        $reader = [System.IO.StreamReader]::new(
            $stream,
            [System.Text.UTF8Encoding]::new($false, $true),
            $true
        )
        try {
            return $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Resolve-DirectPlan {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$PlanReference
    )

    $root = (Resolve-Path -LiteralPath ([System.IO.Path]::GetFullPath($RepoRoot))).Path
    $plan = Resolve-Plan -Reference $PlanReference -RepoRoot $root
    $context = New-PlanConfinementContext -PlanDir $plan.Path -RepoRoot $root
    return [pscustomobject]@{
        RepoRoot = $root
        Plan = $plan
        Context = $context
    }
}

function Test-PlanCriteriaBaseline {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$PlanReference
    )

    $resolved = Resolve-DirectPlan -RepoRoot $RepoRoot -PlanReference $PlanReference
    $planPath = Join-Path $resolved.Plan.Path 'plan.md'
    $planContent = Get-ConfinedPlanText -Context $resolved.Context -Path $planPath
    $marker = (Get-PlanHeaderMarkers -Content $planContent).PlanningConfirmed
    if ([string]::IsNullOrWhiteSpace($marker) -or
        $marker -cnotmatch '^sha256:[0-9a-f]{64}$') {
        throw "Plan '$($resolved.Plan.Id)' has no valid planning-confirmed marker."
    }

    $relativePlanPath = [System.IO.Path]::GetRelativePath(
        $resolved.RepoRoot,
        $planPath
    ).Replace('\', '/')
    $headPlan = Invoke-DirectGit -RepoRoot $resolved.RepoRoot -Argument @(
        'show', "HEAD`:$relativePlanPath"
    ) -AllowFailure
    if ($headPlan.ExitCode -ne 0) {
        throw "Plan '$($resolved.Plan.Id)' is not committed at HEAD."
    }
    $headMarker = (Get-PlanHeaderMarkers -Content ($headPlan.Output -join "`n")).PlanningConfirmed
    if ($headMarker -cne $marker) {
        throw "Plan '$($resolved.Plan.Id)' has an uncommitted planning-confirmed marker."
    }
    $indexPlan = Invoke-DirectGit -RepoRoot $resolved.RepoRoot -Argument @(
        'show', ":$relativePlanPath"
    ) -AllowFailure
    if ($indexPlan.ExitCode -ne 0) {
        throw "Plan '$($resolved.Plan.Id)' is missing from the Git index."
    }
    $indexMarker = (Get-PlanHeaderMarkers -Content ($indexPlan.Output -join "`n")).PlanningConfirmed
    if ($indexMarker -cne $marker) {
        throw "Plan '$($resolved.Plan.Id)' has a staged planning-confirmed marker change."
    }

    $needle = "<!-- planning-confirmed: $marker -->"
    $planPathspec = ':(glob)docs/implementation-plans/**/plan.md'
    $history = Invoke-DirectGit -RepoRoot $resolved.RepoRoot -Argument @(
        'log', '--format=%H', "-S$needle", '--', $planPathspec
    )
    $candidates = [System.Collections.Generic.List[string]]::new()
    $candidatePlanPaths = @{}
    foreach ($commit in @($history.Output | Where-Object { $_ -cmatch '^[0-9a-f]{40,64}$' })) {
        $markerPaths = Invoke-DirectGit -RepoRoot $resolved.RepoRoot -Argument @(
            'grep', '-l', '-F', $needle, $commit, '--', $planPathspec
        ) -AllowFailure
        $markerPathPrefix = "$commit`:"
        if ($markerPaths.ExitCode -eq 0 -and $markerPaths.Output.Count -eq 1 -and
            $markerPaths.Output[0].StartsWith(
                $markerPathPrefix,
                [System.StringComparison]::Ordinal
            )) {
            $candidates.Add($commit)
            $candidatePlanPaths[$commit] = $markerPaths.Output[0].Substring(
                $markerPathPrefix.Length
            )
        }
    }
    if ($candidates.Count -eq 0) {
        throw "No committed baseline introduces planning-confirmed marker '$marker'."
    }
    if ($candidates.Count -ne 1) {
        throw "Planning-confirmed marker '$marker' has an ambiguous baseline: $($candidates -join ', ')."
    }
    $baselineCommit = $candidates[0]
    $baselinePlanPath = $candidatePlanPaths[$baselineCommit]
    $currentPlanDirectory = Split-Path -Path $planPath -Parent
    $baselinePlanDirectory = Split-Path -Path $baselinePlanPath -Parent

    $criteria = [ordered]@{
        Intent = Resolve-PlanAssetPath -PlanDir $resolved.Plan.Path -Kind Intent `
            -RepoRoot $resolved.RepoRoot
        Requirements = Resolve-PlanAssetPath -PlanDir $resolved.Plan.Path -Kind Requirements `
            -RepoRoot $resolved.RepoRoot
        Risks = Resolve-PlanAssetPath -PlanDir $resolved.Plan.Path -Kind Risks `
            -RepoRoot $resolved.RepoRoot
        Decisions = Resolve-PlanAssetPath -PlanDir $resolved.Plan.Path -Kind Decisions `
            -RepoRoot $resolved.RepoRoot
    }
    foreach ($entry in $criteria.GetEnumerator()) {
        $confined = Resolve-ConfinedPlanPath -Context $resolved.Context -Path $entry.Value -PathType Leaf
        $relativePath = [System.IO.Path]::GetRelativePath(
            $resolved.RepoRoot,
            $confined.Item.FullName
        ).Replace('\', '/')
        $planRelativePath = [System.IO.Path]::GetRelativePath(
            $currentPlanDirectory,
            $confined.Item.FullName
        ).Replace('\', '/')
        $baselineRelativePath = "$baselinePlanDirectory/$planRelativePath".Replace('\', '/')
        $baselineBlob = Get-GitBlobId -RepoRoot $resolved.RepoRoot `
            -Revision $baselineCommit -RelativePath $baselineRelativePath
        $indexBlob = Invoke-DirectGit -RepoRoot $resolved.RepoRoot -Argument @(
            'rev-parse', '--verify', ":$relativePath"
        ) -AllowFailure
        $worktreeBlob = Invoke-DirectGit -RepoRoot $resolved.RepoRoot -Argument @(
            'hash-object', "--path=$relativePath", '--', $confined.Item.FullName
        ) -AllowFailure
        if ($indexBlob.ExitCode -ne 0 -or $indexBlob.Output.Count -ne 1 -or
            $worktreeBlob.ExitCode -ne 0 -or $worktreeBlob.Output.Count -ne 1) {
            throw "Unable to compare confirmed $($entry.Key.ToLowerInvariant()) with baseline commit '$baselineCommit'."
        }
        if ($indexBlob.Output[0] -cne $baselineBlob) {
            throw "Confirmed $($entry.Key.ToLowerInvariant()) differs from staged Git-filtered baseline commit '$baselineCommit'. Return to /cip."
        }
        if ($worktreeBlob.Output[0] -cne $baselineBlob) {
            throw "Confirmed $($entry.Key.ToLowerInvariant()) differs from Git-filtered baseline commit '$baselineCommit'. Return to /cip."
        }
    }

    return [pscustomobject]@{
        Status = 'ready'
        PlanId = $resolved.Plan.Id
        BaselineCommit = $baselineCommit
        Marker = $marker
    }
}

function Resolve-DirectReviewReportPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$PlanReference,
        [Parameter(Mandatory)]
        [ValidatePattern('^(?:phase-[1-9][0-9]*|final)$')]
        [string]$Stage
    )

    $resolved = Resolve-DirectPlan -RepoRoot $RepoRoot -PlanReference $PlanReference
    $assetsPath = Join-Path $resolved.Plan.Path 'assets'
    [void](Resolve-ConfinedPlanPath -Context $resolved.Context -Path $assetsPath -PathType Container)
    $reviewsPath = Join-Path $assetsPath 'reviews'
    if (Test-Path -LiteralPath $reviewsPath) {
        [void](Resolve-ConfinedPlanPath -Context $resolved.Context -Path $reviewsPath -PathType Container)
    }
    else {
        $physical = Resolve-PhysicalRepoPath -Path $reviewsPath
        $prefix = $resolved.Context.PhysicalPlanPath.TrimEnd('\', '/') +
            [System.IO.Path]::DirectorySeparatorChar
        if (-not $physical.StartsWith($prefix, $script:PathComparison)) {
            throw "Review directory '$reviewsPath' escapes canonical plan '$($resolved.Plan.Path)'."
        }
        [void][System.IO.Directory]::CreateDirectory($reviewsPath)
        [void](Resolve-ConfinedPlanPath -Context $resolved.Context -Path $reviewsPath -PathType Container)
    }

    return [pscustomobject]@{
        PlanId = $resolved.Plan.Id
        Context = $resolved.Context
        ReviewsPath = $reviewsPath
        Path = Join-Path $reviewsPath "$Stage.md"
    }
}

function Write-AtomicUtf8File {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Content
    )

    $directory = Split-Path -Parent $Path
    $temporary = Join-Path $directory (
        ".$([System.IO.Path]::GetFileName($Path)).$([guid]::NewGuid().ToString('N')).tmp"
    )
    try {
        $stream = [System.IO.File]::Open(
            $temporary,
            [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::Write,
            [System.IO.FileShare]::None
        )
        try {
            $bytes = $script:Utf8NoBom.GetBytes($Content)
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Flush($true)
        }
        finally {
            $stream.Dispose()
        }
        [System.IO.File]::Move($temporary, $Path, $true)
    }
    finally {
        if (Test-Path -LiteralPath $temporary -PathType Leaf) {
            Remove-Item -LiteralPath $temporary -Force
        }
    }
}

function ConvertTo-DirectTextList {
    param(
        [Parameter(Mandatory)][object[]]$Value,
        [Parameter(Mandatory)][string]$Label
    )

    $result = [System.Collections.Generic.List[string]]::new()
    foreach ($item in $Value) {
        $text = [string]$item
        if ([string]::IsNullOrWhiteSpace($text) -or $text -match "[`r`n]") {
            throw "$Label entries must be non-empty single lines."
        }
        $result.Add((Protect-HighConfidenceSecret -Value $text.Trim()))
    }
    return $result.ToArray()
}

function Write-DirectReviewReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$PlanReference,
        [Parameter(Mandatory)]
        [ValidatePattern('^(?:phase-[1-9][0-9]*|final)$')]
        [string]$Stage,
        [Parameter(Mandatory)][ValidateSet('cr', 'dr')][string]$ReviewType,
        [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40,64}$')][string]$Source,
        [Parameter(Mandatory)][ValidateCount(1, 256)][string[]]$Scope,
        [Parameter(Mandatory)][ValidateCount(1, 5)][object[]]$Task,
        [AllowEmptyCollection()][object[]]$Finding = @(),
        [Parameter(Mandatory)]
        [ValidateSet('clean', 'findings', 'incomplete')]
        [string]$Verdict
    )

    $commit = (Invoke-DirectGit -RepoRoot $RepoRoot -Argument @(
            'rev-parse', '--verify', "$Source^{commit}"
        )).Output
    if ($commit.Count -ne 1 -or $commit[0] -cne $Source) {
        throw "Source '$Source' is not the full reviewed commit SHA."
    }
    $scopeLines = ConvertTo-DirectTextList -Value $Scope -Label 'Scope'

    $taskLines = [System.Collections.Generic.List[string]]::new()
    $taskRecords = [System.Collections.Generic.List[object]]::new()
    foreach ($record in $Task) {
        if ($record.PSObject.Properties.Name -notcontains 'Name' -or
            $record.PSObject.Properties.Name -notcontains 'Status') {
            throw 'Each selected task requires Name and Status.'
        }
        $name = @(ConvertTo-DirectTextList -Value @($record.Name) -Label 'Task name')[0]
        $status = [string]$record.Status
        if ($status -cnotin @('complete', 'failed', 'interrupted', 'stuck')) {
            throw "Task '$name' has invalid status '$status'."
        }
        $taskRecords.Add([pscustomobject]@{ Name = $name; Status = $status })
        $taskLines.Add("- [$($(if ($status -eq 'complete') { 'x' } else { ' ' }))] $name — $status")
    }

    $findingLines = [System.Collections.Generic.List[string]]::new()
    foreach ($record in @($Finding)) {
        if ($record -is [string]) {
            $findingLines.Add(@(ConvertTo-DirectTextList -Value @($record) -Label 'Finding')[0])
            continue
        }
        $kind = if ($record.PSObject.Properties.Name -contains 'Kind') {
            [string]$record.Kind
        }
        else {
            ''
        }
        if ($kind -ceq 'security') {
            $fields = @('AttackerOrInput', 'ReachableCapability', 'AffectedAsset', 'PlausibleImpact')
            $parts = [System.Collections.Generic.List[string]]::new()
            foreach ($field in $fields) {
                if ($record.PSObject.Properties.Name -notcontains $field) {
                    throw "Security finding requires $field."
                }
                $parts.Add(@(ConvertTo-DirectTextList -Value @($record.$field) -Label $field)[0])
            }
            $findingLines.Add(
                "Security — attacker/input: $($parts[0]); capability: $($parts[1]); asset: $($parts[2]); impact: $($parts[3])"
            )
            continue
        }
        if ($record.PSObject.Properties.Name -notcontains 'Text') {
            throw 'A finding requires Text or the complete security finding shape.'
        }
        $findingLines.Add(@(ConvertTo-DirectTextList -Value @($record.Text) -Label 'Finding')[0])
    }

    $allComplete = @($taskRecords | Where-Object { $_.Status -ne 'complete' }).Count -eq 0
    if ($Verdict -eq 'clean' -and (-not $allComplete -or $findingLines.Count -ne 0)) {
        throw 'A clean verdict requires every selected task complete and no findings.'
    }
    if ($Verdict -eq 'findings' -and $findingLines.Count -eq 0) {
        throw 'A findings verdict requires at least one finding.'
    }

    $target = Resolve-DirectReviewReportPath -RepoRoot $RepoRoot `
        -PlanReference $PlanReference -Stage $Stage
    if (Test-Path -LiteralPath $target.Path) {
        [void](Resolve-ConfinedPlanPath -Context $target.Context -Path $target.Path -PathType Leaf)
    }
    $findingsBody = if ($findingLines.Count -eq 0) {
        'None.'
    }
    else {
        @($findingLines | ForEach-Object { "- $_" }) -join "`n"
    }
    $content = @(
        '## Source'
        ''
        $Source
        ''
        '## Scope'
        ''
        @($scopeLines | ForEach-Object { "- $_" })
        ''
        '## Completed tasks'
        ''
        $taskLines.ToArray()
        ''
        '## Findings'
        ''
        $findingsBody
        ''
        '## Verdict'
        ''
        $Verdict
        ''
    ) -join "`n"
    Write-AtomicUtf8File -Path $target.Path -Content $content
    [void](Resolve-ConfinedPlanPath -Context $target.Context -Path $target.Path -PathType Leaf)

    return [pscustomobject]@{
        Kind = $ReviewType
        Status = if ($allComplete) { 'complete' } else { 'incomplete' }
        PlanId = $target.PlanId
        Stage = $Stage
        Source = $Source
        Scope = $scopeLines
        Tasks = $taskRecords.ToArray()
        Findings = $findingLines.ToArray()
        Verdict = $Verdict
        ReportPath = $target.Path
    }
}

function ConvertTo-UntrustedReviewBlock {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Content,
        [ValidatePattern('^[A-Za-z0-9 ._-]{1,80}$')]
        [string]$Label = 'review input'
    )

    $redacted = Protect-HighConfidenceSecret -Value $Content
    $digest = [Convert]::ToHexString(
        [System.Security.Cryptography.SHA256]::HashData(
            [System.Text.Encoding]::UTF8.GetBytes($redacted)
        )
    ).ToLowerInvariant()
    $suffix = 0
    do {
        $token = "UNTRUSTED_INPUT_$($digest.Substring(0, 16))_$suffix"
        $suffix++
    } while ($redacted.Contains($token, [System.StringComparison]::Ordinal))
    return "<$token label=`"$Label`">`n$redacted`n</$token>"
}

function Resolve-DirectReviewStandards {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [AllowEmptyCollection()][object[]]$BaseStandard = @()
    )

    $root = (Resolve-Path -LiteralPath ([System.IO.Path]::GetFullPath($RepoRoot))).Path
    $standards = [System.Collections.Generic.Dictionary[string, object]]::new(
        [System.StringComparer]::Ordinal
    )
    foreach ($record in $BaseStandard) {
        foreach ($field in @('Id', 'Guidance', 'Localizable')) {
            if ($record.PSObject.Properties.Name -notcontains $field) {
                throw "Base standard requires $field."
            }
        }
        $id = [string]$record.Id
        if ($id -cnotmatch '^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$' -or
            $standards.ContainsKey($id)) {
            throw "Base review standard id '$id' is malformed or duplicated."
        }
        $baseGuidance = [string]$record.Guidance
        if ([string]::IsNullOrWhiteSpace($baseGuidance) -or
            $baseGuidance.Length -gt 512 -or $baseGuidance -match "[`r`n]") {
            throw "Base review standard '$id' has malformed guidance."
        }
        $standards.Add($id, [pscustomobject]@{
                Id = $id
                Guidance = $baseGuidance
                Localizable = [bool]$record.Localizable
                Source = 'base'
            })
    }

    $path = Join-Path $root 'docs/review-standards.md'
    if (-not (Test-Path -LiteralPath $path)) {
        return $standards.Values
    }
    $item = Get-Item -LiteralPath $path -Force
    if ($item -isnot [System.IO.FileInfo] -or
        ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 -or
        $item.Length -gt 16384) {
        throw 'Local review standards must be a regular file no larger than 16384 bytes.'
    }
    $current = $root
    foreach ($segment in @('docs', 'review-standards.md')) {
        $current = Join-Path $current $segment
        $component = Get-Item -LiteralPath $current -Force
        if (($component.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'Local review standards path contains a reparse point.'
        }
    }
    $physicalRoot = Resolve-PhysicalRepoPath -Path $root
    $physicalPath = Resolve-PhysicalRepoPath -Path $path
    $prefix = $physicalRoot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    if (-not $physicalPath.StartsWith($prefix, $script:PathComparison)) {
        throw 'Local review standards escape the repository.'
    }
    $raw = $script:Utf8NoBom.GetString([System.IO.File]::ReadAllBytes($path))
    $lines = $raw -split '\r?\n'
    if ($lines.Count -eq 0 -or $lines[0] -cne '# Review standards') {
        throw "Local review standards must start with '# Review standards'."
    }
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $localCount = 0
    for ($index = 1; $index -lt $lines.Count; $index++) {
        if ([string]::IsNullOrWhiteSpace($lines[$index])) { continue }
        $match = [regex]::Match(
            $lines[$index],
            '^- (?<mode>extend|replace) `(?<id>[a-z][a-z0-9]*(?:-[a-z0-9]+)*)`: (?<guidance>.+)$'
        )
        if (-not $match.Success) {
            throw "Malformed local review standard at docs/review-standards.md line $($index + 1)."
        }
        $id = $match.Groups['id'].Value
        $guidance = $match.Groups['guidance'].Value
        if (-not $seen.Add($id)) {
            throw "Local review standard '$id' is duplicate."
        }
        if (-not $standards.ContainsKey($id)) {
            if ($id -notin @('focus', 'exceptions')) {
                throw "Local review standard '$id' has no direct base standard."
            }
            $standards.Add($id, [pscustomobject]@{
                    Id = $id
                    Guidance = ''
                    Localizable = $true
                    Source = 'local'
                })
        }
        if (-not $standards[$id].Localizable) {
            throw "Local review standard '$id' targets non-localizable guidance."
        }
        if ($guidance.Length -gt 512 -or
            $guidance -match '[\r\n@{}<>]' -or
            $guidance -match '(?i)UNTRUSTED_INPUT' -or
            $guidance -match '^(?: {4,}| {0,3}\t| {0,3}(?:#{1,6}(?:[ \t]|$)|```|~~~|>|[-+*](?:[ \t]|$)|\d{1,9}[.)](?:[ \t]|$)))' -or
            @(Find-HighConfidenceSecret -Value $guidance).Count -gt 0) {
            throw "Local review standard '$id' contains unsafe or malformed guidance."
        }
        $mode = $match.Groups['mode'].Value
        $base = $standards[$id]
        $resolvedGuidance = if ($mode -eq 'extend') {
            "$($base.Guidance) $guidance"
        }
        else {
            $guidance
        }
        if ($resolvedGuidance.Length -gt 512) {
            throw "Resolved local review standard '$id' exceeds 512 characters."
        }
        $standards[$id] = [pscustomobject]@{
            Id = $id
            Guidance = $resolvedGuidance
            Localizable = $true
            Source = "local-$mode"
        }
        $localCount++
        if ($localCount -gt 32) {
            throw 'Local review standards exceed the 32-entry limit.'
        }
    }
    return $standards.Values
}

function Test-DirectReviewResult {
    param(
        [Parameter(Mandatory)][object]$Result,
        [Parameter(Mandatory)][ValidateSet('cr', 'dr')][string]$Kind,
        [Parameter(Mandatory)][string]$CurrentSource,
        [Parameter(Mandatory)][string[]]$RequestedScope
    )

    $required = @('Kind', 'Status', 'Source', 'Scope', 'Tasks', 'Findings', 'Verdict')
    foreach ($field in $required) {
        if ($Result.PSObject.Properties.Name -notcontains $field) { return $false }
    }
    if ([string]$Result.Kind -cne $Kind -or
        [string]$Result.Status -cne 'complete' -or
        [string]$Result.Verdict -cne 'clean' -or
        [string]$Result.Source -cne $CurrentSource) {
        return $false
    }
    if (@($Result.Tasks).Count -eq 0 -or
        @($Result.Tasks | Where-Object { [string]$_.Status -cne 'complete' }).Count -ne 0 -or
        @($Result.Findings).Count -ne 0) {
        return $false
    }
    $actualScope = @($Result.Scope | ForEach-Object { [string]$_ })
    if ($actualScope.Count -ne $RequestedScope.Count) { return $false }
    for ($index = 0; $index -lt $RequestedScope.Count; $index++) {
        if ($actualScope[$index] -cne $RequestedScope[$index]) { return $false }
    }
    return $true
}

function Test-DirectFileEvidence {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$Marker
    )

    if ($Marker -cnotmatch '^file:(?<path>[^#]+)#(?<assertion>exists|contains:.+|count>=\d+|dircount>=\d+)$') {
        throw "Invalid direct file evidence marker '$Marker'."
    }
    $relative = $Matches.path.Trim()
    if ([string]::IsNullOrWhiteSpace($relative) -or
        [System.IO.Path]::IsPathRooted($relative) -or $relative -match '^[A-Za-z]:' -or
        $relative -match '\\\\') {
        throw "Evidence path '$relative' must be repository-relative."
    }
    $assertion = $Matches.assertion
    $root = (Resolve-Path -LiteralPath ([System.IO.Path]::GetFullPath($RepoRoot))).Path
    $path = [System.IO.Path]::GetFullPath(
        (Join-Path $root ($relative -replace '/', [System.IO.Path]::DirectorySeparatorChar))
    )
    $prefix = $root.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    if (-not $path.StartsWith($prefix, $script:PathComparison)) {
        throw "Evidence path '$relative' escapes the repository."
    }
    if (-not (Test-Path -LiteralPath $path)) { return $false }
    $physicalRoot = Resolve-PhysicalRepoPath -Path $root
    $physicalPath = Resolve-PhysicalRepoPath -Path $path
    $physicalPrefix = $physicalRoot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    if (-not $physicalPath.StartsWith($physicalPrefix, $script:PathComparison)) {
        throw "Evidence path '$relative' escapes the repository through a link."
    }
    if ($assertion -eq 'exists') {
        return Test-Path -LiteralPath $path -PathType Leaf
    }
    if ($assertion -match '^dircount>=(?<count>\d+)$') {
        if (-not (Test-Path -LiteralPath $path -PathType Container)) { return $false }
        $requiredCount = [int]$Matches.count
        $count = 0
        $queue = [System.Collections.Generic.Queue[string]]::new()
        $queue.Enqueue($path)
        while ($queue.Count -gt 0) {
            foreach ($child in @(Get-ChildItem -LiteralPath $queue.Dequeue() -Force)) {
                if (($child.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                    continue
                }
                $count++
                if ($count -ge $requiredCount) { return $true }
                if ($child.PSIsContainer) { $queue.Enqueue($child.FullName) }
            }
        }
        return $false
    }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $false }
    if ((Get-Item -LiteralPath $path -Force).Length -gt 1048576) {
        throw "Evidence file '$relative' exceeds the 1048576-byte limit."
    }
    if ($assertion -match '^count>=(?<count>\d+)$') {
        return @((Get-Content -LiteralPath $path -Force)).Count -ge [int]$Matches.count
    }
    $pattern = $assertion.Substring('contains:'.Length)
    $regex = [regex]::new(
        $pattern,
        [System.Text.RegularExpressions.RegexOptions]::None,
        [TimeSpan]::FromMilliseconds(100)
    )
    return $regex.IsMatch((Get-Content -LiteralPath $path -Raw -Force))
}

function Invoke-DirectEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string[]]$Marker,
        [hashtable]$TestResult = @{},
        [AllowNull()][object]$ActiveReviewResult,
        [string]$CurrentSource,
        [string[]]$RequestedScope = @()
    )

    $results = foreach ($item in $Marker) {
        $passed = if ($item -match '^test:(?<id>[A-Za-z0-9][A-Za-z0-9._-]*)$') {
            if (-not $TestResult.ContainsKey($Matches.id)) {
                $false
            }
            else {
                $testValue = $TestResult[$Matches.id]
                if ($testValue -is [bool]) {
                    $testValue
                }
                elseif ($null -ne $testValue -and
                    $testValue.PSObject.Properties.Name -contains 'Status') {
                    [string]$testValue.Status -ceq 'passed'
                }
                else {
                    $false
                }
            }
        }
        elseif ($item.StartsWith('file:', [System.StringComparison]::Ordinal)) {
            Test-DirectFileEvidence -RepoRoot $RepoRoot -Marker $item
        }
        elseif ($item -match '^review:(?<kind>cr|dr)$') {
            $null -ne $ActiveReviewResult -and
                (Test-DirectReviewResult -Result $ActiveReviewResult -Kind $Matches.kind `
                    -CurrentSource $CurrentSource -RequestedScope $RequestedScope)
        }
        else {
            throw "Unsupported direct evidence marker '$item'."
        }
        [pscustomobject]@{
            Marker = $item
            Status = if ($passed) { 'passed' } else { 'failed' }
            Success = $passed
        }
    }
    return @($results)
}

Export-ModuleMember -Function @(
    'ConvertTo-UntrustedReviewBlock'
    'Invoke-DirectEvidence'
    'Resolve-DirectReviewReportPath'
    'Resolve-DirectReviewStandards'
    'Test-DirectReviewResult'
    'Test-PlanCriteriaBaseline'
    'Write-DirectReviewReport'
)
