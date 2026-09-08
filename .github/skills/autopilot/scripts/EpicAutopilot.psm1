#requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'AtomicStore.psm1') -Force

$script:StateFields = @('epic', 'target', 'currentChild', 'branch', 'run', 'outcome')
$script:PortableExitCodePattern = '^(?:0|[1-9][0-9]?|1[0-9]{2}|2[0-4][0-9]|25[0-5])$'

function ConvertFrom-EpicAutopilotStateJson {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Json)

    try {
        $document = [System.Text.Json.JsonDocument]::Parse($Json)
    }
    catch {
        throw "Epic autopilot state is malformed JSON: $($_.Exception.Message)"
    }

    try {
        if ($document.RootElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
            throw 'Epic autopilot state must be one JSON object.'
        }

        $properties = @($document.RootElement.EnumerateObject())
        if ($properties.Count -ne $script:StateFields.Count) {
            throw "Epic autopilot state must contain exactly: $($script:StateFields -join ', ')."
        }

        $seen = [System.Collections.Generic.HashSet[string]]::new(
            [System.StringComparer]::Ordinal
        )
        $values = [ordered]@{}
        foreach ($property in $properties) {
            if (-not $seen.Add($property.Name) -or $script:StateFields -cnotcontains $property.Name) {
                throw "Epic autopilot state has an unexpected or duplicate field '$($property.Name)'."
            }
            if ($property.Value.ValueKind -ne [System.Text.Json.JsonValueKind]::String) {
                throw "Epic autopilot state field '$($property.Name)' must be a string."
            }
            $values[$property.Name] = $property.Value.GetString()
        }

        foreach ($field in $script:StateFields) {
            if (-not $seen.Contains($field)) {
                throw "Epic autopilot state is missing field '$field'."
            }
        }

        if ($values.epic -cnotmatch '^[0-9a-f]{6}$') {
            throw "Epic autopilot state field 'epic' must be a canonical six-character id."
        }
        if ($values.target -cnotmatch '^(?:[0-9a-f]{40}|[0-9a-f]{64})$') {
            throw "Epic autopilot state field 'target' must be a full Git commit id."
        }
        if ($values.currentChild -cnotmatch '^[0-9a-f]{6}$') {
            throw "Epic autopilot state field 'currentChild' must be a canonical six-character id."
        }
        if ($values.branch -cnotmatch '^feature/[a-z0-9][a-z0-9-]*$') {
            throw "Epic autopilot state field 'branch' is invalid."
        }
        $parsedRun = [guid]::Empty
        if (-not [guid]::TryParseExact($values.run, 'D', [ref]$parsedRun) -or
            $parsedRun.ToString('D') -cne $values.run) {
            throw "Epic autopilot state field 'run' must be a canonical GUID."
        }
        if ($values.outcome -cnotin @(
                'selected', 'running', 'awaiting-merge', 'invocation-failed'
            )) {
            if (-not $values.outcome.StartsWith('exit:', [System.StringComparison]::Ordinal) -or
                $values.outcome.Substring(5) -cnotmatch $script:PortableExitCodePattern) {
                throw "Epic autopilot state field 'outcome' is invalid."
            }
        }

        return [pscustomobject]$values
    }
    finally {
        $document.Dispose()
    }
}

function Read-EpicAutopilotState {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }
    return ConvertFrom-EpicAutopilotStateJson -Json ([System.IO.File]::ReadAllText($Path))
}

function ConvertFrom-EpicRollupJson {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Json)

    try {
        $rollup = $Json | ConvertFrom-Json -Depth 20
    }
    catch {
        throw "Get-PlanState returned malformed epic JSON: $($_.Exception.Message)"
    }
    if ($null -eq $rollup -or $rollup -is [array] -or
        $rollup.PSObject.Properties.Name -notcontains 'Kind' -or $rollup.Kind -cne 'epic') {
        throw "Get-PlanState did not return an epic rollup (Kind must be 'epic')."
    }
    if ($rollup.PSObject.Properties.Name -notcontains 'EpicId' -or
        $rollup.EpicId -isnot [string] -or
        [string]$rollup.EpicId -cnotmatch '^[0-9a-f]{6}$') {
        throw 'Get-PlanState returned an invalid canonical EpicId.'
    }
    if ($rollup.PSObject.Properties.Name -notcontains 'NextChild') {
        throw 'Get-PlanState epic rollup is missing NextChild.'
    }
    if ($rollup.PSObject.Properties.Name -notcontains 'Rollup' -or
        $null -eq $rollup.Rollup -or
        $rollup.Rollup -isnot [pscustomobject]) {
        throw 'Get-PlanState epic rollup is missing Rollup.'
    }
    foreach ($field in @('ChildCount', 'CompleteCount', 'BlockedCount')) {
        if ($rollup.Rollup.PSObject.Properties.Name -notcontains $field -or
            $rollup.Rollup.$field -isnot [long] -or
            [long]$rollup.Rollup.$field -lt 0) {
            throw "Get-PlanState Rollup.$field must be a nonnegative integer."
        }
    }
    if ($rollup.Rollup.PSObject.Properties.Name -notcontains 'IsComplete' -or
        $rollup.Rollup.IsComplete -isnot [bool]) {
        throw 'Get-PlanState Rollup.IsComplete must be boolean.'
    }
    if ($rollup.PSObject.Properties.Name -notcontains 'Children') {
        throw 'Get-PlanState epic rollup is missing Children.'
    }
    $children = @($rollup.Children)
    $childIds = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::Ordinal
    )
    $completeCount = 0
    $blockedCount = 0
    $expectedNextChild = $null
    foreach ($child in $children) {
        if ($null -eq $child -or $child -isnot [pscustomobject]) {
            throw 'Get-PlanState Children must contain child objects.'
        }
        foreach ($field in @('Id', 'IsComplete', 'IsBlocked')) {
            if ($child.PSObject.Properties.Name -notcontains $field) {
                throw "Get-PlanState child is missing '$field'."
            }
        }
        if ($child.Id -isnot [string] -or
            [string]$child.Id -cnotmatch '^[0-9a-f]{6}$') {
            throw 'Get-PlanState child has an invalid canonical Id.'
        }
        if (-not $childIds.Add([string]$child.Id)) {
            throw "Get-PlanState Children contains duplicate Id '$($child.Id)'."
        }
        if ($child.IsComplete -isnot [bool] -or $child.IsBlocked -isnot [bool]) {
            throw 'Get-PlanState child completion and block fields must be boolean.'
        }
        if ([bool]$child.IsComplete -and [bool]$child.IsBlocked) {
            throw "Get-PlanState child '$($child.Id)' cannot be both complete and blocked."
        }
        if ([bool]$child.IsComplete) {
            $completeCount++
        }
        elseif ([bool]$child.IsBlocked) {
            $blockedCount++
        }
        elseif ($null -eq $expectedNextChild) {
            $expectedNextChild = $child
        }
    }

    $derivedCounts = [ordered]@{
        ChildCount = $children.Count
        CompleteCount = $completeCount
        BlockedCount = $blockedCount
    }
    foreach ($field in $derivedCounts.Keys) {
        if ([long]$rollup.Rollup.$field -ne [long]$derivedCounts[$field]) {
            throw "Get-PlanState Rollup.$field does not match Children (expected $($derivedCounts[$field]))."
        }
    }
    $expectedIsComplete = $children.Count -gt 0 -and
    $completeCount -eq $children.Count
    if ([bool]$rollup.Rollup.IsComplete -ne $expectedIsComplete) {
        throw "Get-PlanState Rollup.IsComplete does not match Children (expected $expectedIsComplete)."
    }

    if ($null -ne $rollup.NextChild) {
        if ($rollup.NextChild -isnot [pscustomobject]) {
            throw 'Get-PlanState NextChild must be one child object or null.'
        }
        foreach ($field in @('Id', 'FolderName')) {
            if ($rollup.NextChild.PSObject.Properties.Name -notcontains $field) {
                throw "Get-PlanState NextChild is missing '$field'."
            }
        }
        if ($rollup.NextChild.Id -isnot [string] -or
            [string]$rollup.NextChild.Id -cnotmatch '^[0-9a-f]{6}$') {
            throw 'Get-PlanState NextChild has an invalid canonical Id.'
        }
        if ($rollup.NextChild.FolderName -isnot [string] -or
            [string]$rollup.NextChild.FolderName -cnotmatch '^[a-z0-9][a-z0-9-]*$') {
            throw 'Get-PlanState NextChild has an invalid FolderName.'
        }
        if ($null -eq $expectedNextChild) {
            throw 'Get-PlanState NextChild is non-null but no child is eligible.'
        }
        if ([string]$rollup.NextChild.Id -cne [string]$expectedNextChild.Id) {
            throw "Get-PlanState NextChild must be the first incomplete, unblocked child '$($expectedNextChild.Id)'."
        }
        if ($expectedNextChild.PSObject.Properties.Name -contains 'FolderName' -and (
                $expectedNextChild.FolderName -isnot [string] -or
                [string]$expectedNextChild.FolderName -cne
                [string]$rollup.NextChild.FolderName
            )) {
            throw "Get-PlanState NextChild FolderName does not match child '$($expectedNextChild.Id)'."
        }
    }
    elseif ($null -ne $expectedNextChild) {
        throw "Get-PlanState NextChild is null but child '$($expectedNextChild.Id)' is eligible."
    }
    return $rollup
}

function Resolve-EpicAutopilotStatePath {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [string]$StatePath
    )

    if ($StatePath) {
        return [System.IO.Path]::GetFullPath($StatePath)
    }

    $gitCommonDir = @(& git -C $RepoRoot rev-parse --git-common-dir 2>&1)
    if ($LASTEXITCODE -ne 0 -or $gitCommonDir.Count -ne 1) {
        throw "Unable to resolve the Git common directory: $(($gitCommonDir -join ' ').Trim())"
    }
    $common = [string]$gitCommonDir[0]
    if (-not [System.IO.Path]::IsPathRooted($common)) {
        $common = Join-Path $RepoRoot $common
    }
    return [System.IO.Path]::GetFullPath(
        (Join-Path $common 'skalary/epic-autopilot.json')
    )
}

function New-EpicAutopilotState {
    param(
        [Parameter(Mandatory)]$State,
        [Parameter(Mandatory)][string]$Outcome
    )

    return [pscustomobject][ordered]@{
        epic = [string]$State.epic
        target = [string]$State.target
        currentChild = [string]$State.currentChild
        branch = [string]$State.branch
        run = [string]$State.run
        outcome = $Outcome
    }
}

function Set-EpicAutopilotState {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)]$State,
        [Parameter(Mandatory)][string]$ExpectedGeneration,
        [Parameter(Mandatory)][string]$Operation
    )

    $content = $State | ConvertTo-Json -Compress
    [void](ConvertFrom-EpicAutopilotStateJson -Json $content)
    $write = Set-AtomicStoreContent -Path $Path `
        -Content $content `
        -ExpectedGeneration $ExpectedGeneration -Validate {
        param($candidatePath)
        [void](ConvertFrom-EpicAutopilotStateJson -Json (
                [System.IO.File]::ReadAllText($candidatePath)
            ))
    }
    if ($write.Status -ne 'complete') {
        throw "Epic autopilot $Operation failed with status '$($write.Status)'."
    }
    return $write
}

function Remove-EpicAutopilotState {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$ExpectedGeneration
    )

    $actualGeneration = Get-AtomicStoreGeneration -Path $Path
    if (-not [string]::Equals(
            $actualGeneration,
            $ExpectedGeneration,
            [System.StringComparison]::Ordinal
        )) {
        return [pscustomobject]@{
            Status = 'cas-conflict'
            Path = $Path
            ExpectedGeneration = $ExpectedGeneration
            ActualGeneration = $actualGeneration
        }
    }

    Remove-Item -LiteralPath $Path -Force
    return [pscustomobject]@{
        Status = 'complete'
        Path = $Path
    }
}

function Test-EpicAutopilotAncestor {
    param(
        [Parameter(Mandatory)][string]$Ancestor,
        [Parameter(Mandatory)][string]$Descendant,
        [Parameter(Mandatory)][string]$RepoRoot
    )

    & git -C $RepoRoot merge-base --is-ancestor $Ancestor $Descendant 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) {
        return $true
    }
    if ($LASTEXITCODE -eq 1) {
        return $false
    }
    throw "Unable to prove target ancestry from '$Ancestor' to '$Descendant'."
}

function Test-EpicAutopilotStateIdentity {
    param(
        [Parameter(Mandatory)]$Actual,
        [Parameter(Mandatory)]$Expected
    )

    foreach ($field in @('epic', 'target', 'currentChild', 'branch', 'run')) {
        if ([string]$Actual.$field -cne [string]$Expected.$field) {
            return $false
        }
    }
    return $true
}

function Set-EpicAutopilotTerminalState {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)]$RunningState,
        [Parameter(Mandatory)][string]$RunningGeneration,
        [Parameter(Mandatory)][string]$Outcome
    )

    return Invoke-WithAtomicStoreLock -Scope $Path -Action {
        $generation = Get-AtomicStoreGeneration -Path $Path
        $current = Read-EpicAutopilotState -Path $Path
        if ($null -eq $current -or
            $generation -cne $RunningGeneration -or
            -not (Test-EpicAutopilotStateIdentity -Actual $current -Expected $RunningState) -or
            $current.outcome -cne 'running') {
            throw 'Epic autopilot running state changed before its terminal result could be persisted.'
        }

        $terminal = New-EpicAutopilotState -State $current -Outcome $Outcome
        [void](Set-EpicAutopilotState -Path $Path -State $terminal `
                -ExpectedGeneration $RunningGeneration -Operation 'terminal state write')
        return $terminal
    }
}

function New-EpicAutopilotRunMutex {
    param(
        [Parameter(Mandatory)][string]$StatePath,
        [Parameter(Mandatory)][string]$Run
    )

    $scope = "$([System.IO.Path]::GetFullPath($StatePath))`n$Run"
    $digest = [System.Security.Cryptography.SHA256]::HashData(
        [System.Text.Encoding]::UTF8.GetBytes($scope)
    )
    $name = 'skalary-epic-run-' +
    [Convert]::ToHexString($digest).ToLowerInvariant().Substring(0, 32)
    foreach ($prefix in @('Global\', 'Local\', '')) {
        try {
            return [System.Threading.Mutex]::new($false, "$prefix$name")
        }
        catch {
            continue
        }
    }
    throw "Unable to create epic autopilot run lease '$name'."
}

function Enter-EpicAutopilotRunLease {
    param(
        [Parameter(Mandatory)][string]$StatePath,
        [Parameter(Mandatory)][string]$Run
    )

    $mutex = New-EpicAutopilotRunMutex -StatePath $StatePath -Run $Run
    $acquired = $false
    try {
        try {
            $acquired = $mutex.WaitOne([TimeSpan]::Zero)
        }
        catch [System.Threading.AbandonedMutexException] {
            $acquired = $true
        }
        if (-not $acquired) {
            throw "Epic autopilot run '$Run' already has an active host launcher."
        }
        return $mutex
    }
    catch {
        if ($acquired) {
            [void]$mutex.ReleaseMutex()
        }
        $mutex.Dispose()
        throw
    }
}

function Test-EpicAutopilotRunLeaseActive {
    param(
        [Parameter(Mandatory)][string]$StatePath,
        [Parameter(Mandatory)][string]$Run
    )

    $mutex = New-EpicAutopilotRunMutex -StatePath $StatePath -Run $Run
    $acquired = $false
    try {
        try {
            $acquired = $mutex.WaitOne([TimeSpan]::Zero)
        }
        catch [System.Threading.AbandonedMutexException] {
            $acquired = $true
        }
        return -not $acquired
    }
    finally {
        if ($acquired) {
            [void]$mutex.ReleaseMutex()
        }
        $mutex.Dispose()
    }
}

function Exit-EpicAutopilotRunLease {
    param([Parameter(Mandatory)][System.Threading.Mutex]$Lease)

    try {
        [void]$Lease.ReleaseMutex()
    }
    finally {
        $Lease.Dispose()
    }
}

function Invoke-EpicChildLauncher {
    param(
        [Parameter(Mandatory)][string]$LaunchScript,
        [Parameter(Mandatory)][string[]]$Argument,
        [Parameter(Mandatory)][string]$WorkingDirectory
    )

    if (-not (Test-Path -LiteralPath $LaunchScript -PathType Leaf)) {
        throw "Per-plan launcher not found: $LaunchScript"
    }

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = (Get-Process -Id $PID).Path
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.ArgumentList.Add('-NoProfile')
    $startInfo.ArgumentList.Add('-File')
    $startInfo.ArgumentList.Add($LaunchScript)
    foreach ($item in $Argument) {
        $startInfo.ArgumentList.Add($item)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "Unable to start per-plan launcher '$LaunchScript'."
        }
        $process.WaitForExit()
        return $process.ExitCode
    }
    finally {
        $process.Dispose()
    }
}

function Get-EpicAutopilotContainerName {
    param([Parameter(Mandatory)][string]$Run)

    $parsedRun = [guid]::Empty
    if (-not [guid]::TryParseExact($Run, 'D', [ref]$parsedRun) -or
        $parsedRun.ToString('D') -cne $Run) {
        throw "Cannot derive an epic container name from noncanonical run '$Run'."
    }
    return "autopilot-run-$Run"
}

function Assert-EpicAutopilotWorktree {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$TargetCommit
    )

    $headOutput = @(& git -C $RepoRoot rev-parse --verify 'HEAD^{commit}' 2>&1)
    if ($LASTEXITCODE -ne 0 -or $headOutput.Count -ne 1) {
        throw "Unable to resolve the repository worktree HEAD: $(($headOutput -join ' ').Trim())"
    }
    $headCommit = ([string]$headOutput[0]).Trim().ToLowerInvariant()
    if ($headCommit -cne $TargetCommit) {
        throw "Repository worktree HEAD '$headCommit' does not equal resolved target '$TargetCommit'."
    }

    $statusOutput = @(
        & git -C $RepoRoot status --porcelain=v1 --untracked-files=normal 2>&1
    )
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to inspect the repository worktree: $(($statusOutput -join ' ').Trim())"
    }
    if ($statusOutput.Count -ne 0) {
        throw 'Repository worktree must be clean before epic child selection.'
    }
}

function ConvertFrom-EpicAutopilotContainerState {
    param(
        [Parameter(Mandatory)][string]$ContainerName,
        [Parameter(Mandatory)][string]$State
    )

    switch -CaseSensitive ($State) {
        { $_ -in @('running', 'restarting', 'paused', 'removing') } { return $true }
        { $_ -in @('created', 'exited', 'dead') } { return $false }
        default {
            throw "Container probe returned unknown state '$State' for '$ContainerName'."
        }
    }
}

function Test-EpicAutopilotContainerActive {
    param([Parameter(Mandatory)][string]$ContainerName)

    $output = @(
        & docker container ls --all --filter "name=^/$ContainerName$" `
            --format '{{.Names}}|{{.State}}' 2>&1
    )
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to inspect epic container '$ContainerName': $(($output -join ' ').Trim())"
    }
    if ($output.Count -eq 0) {
        return $false
    }
    if ($output.Count -ne 1) {
        throw "Container probe returned multiple records for '$ContainerName'."
    }

    $parts = ([string]$output[0]).Split('|', 2)
    if ($parts.Count -ne 2 -or $parts[0] -cne $ContainerName) {
        throw "Container probe returned an unexpected record for '$ContainerName'."
    }
    return ConvertFrom-EpicAutopilotContainerState -ContainerName $ContainerName `
        -State $parts[1]
}

function Resolve-EpicAutopilotTrustedFile {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$ExpectedName,
        [Parameter(Mandatory)][string]$Label
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "$Label path is missing."
    }
    $root = [System.IO.Path]::GetFullPath($RepoRoot)
    $candidate = if ([System.IO.Path]::IsPathRooted($Path)) {
        [System.IO.Path]::GetFullPath($Path)
    }
    else {
        [System.IO.Path]::GetFullPath((Join-Path $root $Path))
    }
    $relative = [System.IO.Path]::GetRelativePath($root, $candidate)
    if ([System.IO.Path]::IsPathRooted($relative) -or
        $relative -eq '..' -or
        $relative.StartsWith(
            "..$([System.IO.Path]::DirectorySeparatorChar)",
            [System.StringComparison]::Ordinal
        )) {
        throw "$Label path is outside the repository."
    }
    if ([System.IO.Path]::GetFileName($candidate) -cne $ExpectedName) {
        throw "$Label path must name '$ExpectedName'."
    }

    $cursor = $root
    foreach ($segment in $relative.Split(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.StringSplitOptions]::RemoveEmptyEntries
        )) {
        $cursor = Join-Path $cursor $segment
        $item = Get-Item -LiteralPath $cursor -Force -ErrorAction Stop
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 -or
            ($item.PSObject.Properties.Name -contains 'LinkType' -and $item.LinkType)) {
            throw "$Label path must not contain links or reparse points."
        }
    }
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "$Label path must be a regular file."
    }
    return $candidate
}

function Test-EpicAutopilotIntentSection {
    param(
        [Parameter(Mandatory)][string]$Content,
        [Parameter(Mandatory)][string]$Heading
    )

    $pattern = '(?ms)^##[ \t]+' + [regex]::Escape($Heading) +
    '[ \t]*\r?\n(?<body>.*?)(?=^##[ \t]+|\z)'
    $match = [regex]::Match(
        $Content,
        $pattern,
        [System.Text.RegularExpressions.RegexOptions]::None,
        [TimeSpan]::FromSeconds(1)
    )
    if (-not $match.Success) {
        return $false
    }
    $body = [regex]::Replace(
        $match.Groups['body'].Value,
        '<!--.*?-->',
        '',
        [System.Text.RegularExpressions.RegexOptions]::Singleline,
        [TimeSpan]::FromSeconds(1)
    ).Trim()
    return -not [string]::IsNullOrWhiteSpace($body) -and
    $body -cnotmatch '^(?:[_*(]*\s*)?(?:TBD|TODO|not yet defined)(?:\s*[_*)]*)?[.!]?$'
}

function Get-EpicAutopilotFinalPlanFile {
    param(
        [Parameter(Mandatory)]$Rollup,
        $State
    )

    $children = @($Rollup.Children)
    $selected = if ($null -ne $State) {
        @($children | Where-Object { [string]$_.Id -ceq [string]$State.currentChild })
    }
    elseif ($children.Count -gt 0) {
        @($children[$children.Count - 1])
    }
    else {
        @()
    }
    if ($selected.Count -ne 1 -or
        $selected[0].PSObject.Properties.Name -notcontains 'PlanFile' -or
        $selected[0].PlanFile -isnot [string]) {
        throw 'Complete epic rollup does not identify exactly one final child plan file.'
    }
    return [string]$selected[0].PlanFile
}

function Assert-EpicAutopilotCheckedOutTarget {
    param(
        [Parameter(Mandatory)][string]$TargetBranch,
        [Parameter(Mandatory)][string]$TargetCommit,
        [Parameter(Mandatory)][string]$RepoRoot
    )

    $expectedRef = "refs/heads/$TargetBranch"
    $headRef = @(& git -C $RepoRoot symbolic-ref --quiet HEAD 2>&1)
    if ($LASTEXITCODE -ne 0 -or $headRef.Count -ne 1 -or
        ([string]$headRef[0]).Trim() -cne $expectedRef) {
        throw "Repository worktree HEAD must be attached to checked-out target ref '$expectedRef'."
    }
    $refCommit = @(
        & git -C $RepoRoot rev-parse --verify "$expectedRef`^{commit}" 2>&1
    )
    if ($LASTEXITCODE -ne 0 -or $refCommit.Count -ne 1 -or
        ([string]$refCommit[0]).Trim().ToLowerInvariant() -cne $TargetCommit) {
        throw "Checked-out target ref '$expectedRef' moved from expected commit '$TargetCommit'."
    }
}

function ConvertTo-EpicAutopilotRepoRelativePath {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$Label
    )

    $root = [System.IO.Path]::GetFullPath($RepoRoot)
    $candidate = [System.IO.Path]::GetFullPath($Path)
    $relative = [System.IO.Path]::GetRelativePath($root, $candidate)
    if ([System.IO.Path]::IsPathRooted($relative) -or
        $relative -eq '..' -or
        $relative.StartsWith(
            "..$([System.IO.Path]::DirectorySeparatorChar)",
            [System.StringComparison]::Ordinal
        )) {
        throw "$Label path is outside the repository."
    }
    return $relative.Replace(
        [System.IO.Path]::DirectorySeparatorChar,
        [char]'/'
    )
}

function Get-EpicAutopilotGitPaths {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string[]]$Argument,
        [Parameter(Mandatory)][string]$Label
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'git'
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($item in @('-C', $RepoRoot) + $Argument) {
        [void]$startInfo.ArgumentList.Add($item)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $stream = [System.IO.MemoryStream]::new()
    try {
        if (-not $process.Start()) {
            throw "Unable to start Git while $Label."
        }
        $errorRead = $process.StandardError.ReadToEndAsync()
        $process.StandardOutput.BaseStream.CopyTo($stream)
        $process.WaitForExit()
        $errorText = $errorRead.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            throw "Unable to $Label`: $($errorText.Trim())"
        }

        $bytes = $stream.ToArray()
        if ($bytes.Length -eq 0) { return }
        if ($bytes[$bytes.Length - 1] -ne 0) {
            throw "Git returned malformed path output while $Label."
        }
        $text = [System.Text.UTF8Encoding]::new($false, $true).GetString($bytes)
        $records = $text.Split([char]0)
        for ($index = 0; $index -lt $records.Length - 1; $index++) {
            if ($records[$index].Length -eq 0) {
                throw "Git returned an empty path while $Label."
            }
            Write-Output $records[$index]
        }
    }
    finally {
        $stream.Dispose()
        $process.Dispose()
    }
}

function Get-EpicAutopilotCapturePath {
    param(
        [Parameter(Mandatory)][string]$PlanFile,
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$Commit
    )

    $planDirectory = Split-Path -Parent $PlanFile
    $captureMatches = [System.Collections.Generic.List[object]]::new()
    foreach ($candidate in @(
            (Join-Path $planDirectory 'capture.md'),
            (Join-Path $planDirectory 'assets/logs/capture.md')
        )) {
        $relative = ConvertTo-EpicAutopilotRepoRelativePath -Path $candidate `
            -RepoRoot $RepoRoot -Label 'Final crosscheck Capture'
        $treeEntry = @(
            Get-EpicAutopilotGitPaths -RepoRoot $RepoRoot -Argument @(
                'ls-tree', '--name-only', '-z', $Commit, '--', $relative
            ) -Label "inspect Capture path '$relative' at '$Commit'"
        )
        if ($treeEntry.Count -eq 1 -and [string]$treeEntry[0] -ceq $relative) {
            $captureMatches.Add([pscustomobject]@{
                    FullPath = [System.IO.Path]::GetFullPath($candidate)
                    RelativePath = $relative
                })
        }
        elseif ($treeEntry.Count -ne 0) {
            throw "Capture path lookup returned an unexpected result for '$relative'."
        }
    }
    if ($captureMatches.Count -ne 1) {
        throw "Final child plan must have exactly one tracked Capture path; found $($captureMatches.Count)."
    }
    [void](Resolve-EpicAutopilotTrustedFile -Path $captureMatches[0].FullPath `
            -RepoRoot $RepoRoot -ExpectedName 'capture.md' `
            -Label 'Final crosscheck Capture')
    return $captureMatches[0]
}

function Get-EpicAutopilotTreeEntry {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$Commit,
        [Parameter(Mandatory)][string]$Path,
        [switch]$Optional
    )

    $output = @(& git -C $RepoRoot ls-tree $Commit -- $Path 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to inspect canonical target path '$Path' at '$Commit'."
    }
    if ($output.Count -eq 0 -and $Optional) { return $null }
    if ($output.Count -ne 1 -or
        [string]$output[0] -cnotmatch
        '^(?<mode>[0-9]{6}) (?<type>[a-z]+) (?<oid>[0-9a-f]+)\t(?<path>.+)$' -or
        $Matches.path -cne $Path) {
        throw "Canonical target path '$Path' must resolve to exactly one tree entry."
    }
    if ($Matches.mode -cne '100644' -or $Matches.type -cne 'blob') {
        throw "Canonical target path '$Path' must be a regular non-executable file."
    }
    return [pscustomobject]@{
        Path = $Path
        Oid = $Matches.oid
    }
}

function Read-EpicAutopilotTreeBlob {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)]$TreeEntry,
        [Parameter(Mandatory)][long]$MaxBytes,
        [Parameter(Mandatory)][string]$Label
    )

    $sizeOutput = @(& git -C $RepoRoot cat-file -s $TreeEntry.Oid 2>&1)
    $size = 0L
    if ($LASTEXITCODE -ne 0 -or $sizeOutput.Count -ne 1 -or
        -not [long]::TryParse(
            ([string]$sizeOutput[0]).Trim(),
            [System.Globalization.NumberStyles]::None,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [ref]$size
        ) -or $size -lt 0) {
        throw "Unable to determine the canonical $Label blob size."
    }
    if ($size -gt $MaxBytes) {
        throw "Canonical $Label exceeds the $MaxBytes-byte recovery identity bound."
    }

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'git'
    $startInfo.WorkingDirectory = $RepoRoot
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.ArgumentList.Add('cat-file')
    $startInfo.ArgumentList.Add('blob')
    $startInfo.ArgumentList.Add([string]$TreeEntry.Oid)
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $stream = [System.IO.MemoryStream]::new()
    try {
        if (-not $process.Start()) {
            throw "Unable to read the canonical $Label blob."
        }
        $errorRead = $process.StandardError.ReadToEndAsync()
        $process.StandardOutput.BaseStream.CopyTo($stream)
        $process.WaitForExit()
        $errorText = $errorRead.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            throw "Unable to read the canonical $Label blob: $($errorText.Trim())"
        }
        $bytes = $stream.ToArray()
        if ($bytes.Length -ne $size) {
            throw "Canonical $Label blob length changed while reading."
        }
        return , $bytes
    }
    finally {
        $stream.Dispose()
        $process.Dispose()
    }
}

function Assert-EpicAutopilotWorktreeFileMatchesTree {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)]$TreeEntry
    )

    $output = @(
        & git -C $RepoRoot hash-object "--path=$($TreeEntry.Path)" -- `
            $TreeEntry.Path 2>&1
    )
    if ($LASTEXITCODE -ne 0 -or $output.Count -ne 1 -or
        ([string]$output[0]).Trim().ToLowerInvariant() -cne
        ([string]$TreeEntry.Oid).ToLowerInvariant()) {
        throw "Worktree source '$($TreeEntry.Path)' does not match the canonical target blob."
    }
}

function Get-EpicAutopilotRecoveryDescriptor {
    param(
        [Parameter(Mandatory)]$State,
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$TargetCommit,
        [Parameter(Mandatory)][string]$ReviewScriptPath
    )

    $branchPrefix = 'feature/'
    if (-not ([string]$State.branch).StartsWith(
            $branchPrefix,
            [System.StringComparison]::Ordinal
        )) {
        throw 'Retained epic checkpoint has no canonical final-child folder.'
    }
    $folder = ([string]$State.branch).Substring($branchPrefix.Length)
    if ($folder -cmatch '^\d{3}-[a-z0-9][a-z0-9-]*$') {
        throw 'Retained epic checkpoint uses a legacy three-digit plan folder, which cannot carry the required six-character epic child identity.'
    }
    $childPattern = '^(?:(?<prefix>standalone|[0-9a-f]{6})-)?' +
    '\d{4}-\d{2}-\d{2}-(?<child>[0-9a-f]{6})-' +
    '[a-z0-9][a-z0-9-]*$'
    $childMatch = [regex]::Match($folder, $childPattern)
    if (-not $childMatch.Success -or
        $childMatch.Groups['child'].Value -cne [string]$State.currentChild) {
        throw 'Retained epic checkpoint branch does not identify its canonical final child.'
    }
    $folderPrefix = $childMatch.Groups['prefix'].Value
    if ($folderPrefix -and $folderPrefix -cne [string]$State.epic) {
        throw "Retained epic checkpoint final-child folder prefix '$folderPrefix' does not match epic '$($State.epic)'."
    }

    $planPath = "docs/implementation-plans/$folder/plan.md"
    $planEntry = Get-EpicAutopilotTreeEntry -RepoRoot $RepoRoot `
        -Commit $TargetCommit -Path $planPath
    $planDirectory = [System.IO.Path]::GetFullPath(
        (Join-Path $RepoRoot "docs/implementation-plans/$folder")
    )
    [void](Resolve-EpicAutopilotTrustedFile -Path (Join-Path $planDirectory 'plan.md') `
            -RepoRoot $RepoRoot -ExpectedName 'plan.md' -Label 'Final child plan')
    Assert-EpicAutopilotWorktreeFileMatchesTree -RepoRoot $RepoRoot `
        -TreeEntry $planEntry
    $planBytes = Read-EpicAutopilotTreeBlob -RepoRoot $RepoRoot `
        -TreeEntry $planEntry -MaxBytes 1MB -Label 'final child plan'
    $planText = [System.Text.UTF8Encoding]::new($false, $true).GetString($planBytes)
    $header = ($planText -split '(?m)^##[ \t]+', 2)[0]
    $planIds = @(
        [regex]::Matches(
            $header,
            '(?m)^[ \t]*<!--[ \t]*plan-id:[ \t]*(?<id>[0-9a-fA-F]{3,})[ \t]*-->[ \t]*$'
        ) | ForEach-Object { $_.Groups['id'].Value.ToLowerInvariant() }
    )
    $epicIds = @(
        [regex]::Matches(
            $header,
            '(?m)^[ \t]*<!--[ \t]*epic:[ \t]*(?<id>[0-9a-fA-F]{3,})[ \t]*-->[ \t]*$'
        ) | ForEach-Object { $_.Groups['id'].Value.ToLowerInvariant() }
    )
    if ($planIds.Count -ne 1 -or $planIds[0] -cne [string]$State.currentChild) {
        throw "Canonical final child plan must carry exactly one header plan-id '$($State.currentChild)'."
    }
    if ($epicIds.Count -ne 1 -or $epicIds[0] -cne [string]$State.epic) {
        throw "Canonical final child plan must carry exactly one header epic membership '$($State.epic)'."
    }

    $epicTree = @(Get-EpicAutopilotGitPaths -RepoRoot $RepoRoot -Argument @(
            'ls-tree', '-r', '--name-only', '-z', $TargetCommit, '--',
            'docs/implementation-plans/epics'
        ) -Label "inspect canonical epic sources at '$TargetCommit'")
    $epicPattern = '^docs/implementation-plans/epics/' +
    '\d{4}-\d{2}-\d{2}-' + [regex]::Escape([string]$State.epic) +
    '(?:-[^/]+)?/epic\.md$'
    $epicPaths = @($epicTree | Where-Object { [string]$_ -cmatch $epicPattern })
    if ($epicPaths.Count -ne 1) {
        throw "Canonical target must contain exactly one epic source for '$($State.epic)'."
    }
    $epicEntry = Get-EpicAutopilotTreeEntry -RepoRoot $RepoRoot `
        -Commit $TargetCommit -Path ([string]$epicPaths[0])
    [void](Resolve-EpicAutopilotTrustedFile `
            -Path (Join-Path $RepoRoot ([string]$epicPaths[0])) `
            -RepoRoot $RepoRoot -ExpectedName 'epic.md' -Label 'Canonical epic')
    Assert-EpicAutopilotWorktreeFileMatchesTree -RepoRoot $RepoRoot `
        -TreeEntry $epicEntry

    $capturePaths = @(
        "$($planPath.Substring(0, $planPath.Length - 'plan.md'.Length))capture.md"
        "$($planPath.Substring(0, $planPath.Length - 'plan.md'.Length))assets/logs/capture.md"
    )
    $captureEntries = @(
        foreach ($capturePath in $capturePaths) {
            $entry = Get-EpicAutopilotTreeEntry -RepoRoot $RepoRoot `
                -Commit $TargetCommit -Path $capturePath -Optional
            if ($null -ne $entry) { $entry }
        }
    )
    if ($captureEntries.Count -ne 1) {
        throw "Canonical final child must contain exactly one tracked Capture path; found $($captureEntries.Count)."
    }
    $capturePath = [string]$captureEntries[0].Path
    [void](Resolve-EpicAutopilotTrustedFile -Path (Join-Path $RepoRoot $capturePath) `
            -RepoRoot $RepoRoot -ExpectedName 'capture.md' `
            -Label 'Final crosscheck Capture')

    $reviewRelative = ConvertTo-EpicAutopilotRepoRelativePath `
        -Path $ReviewScriptPath -RepoRoot $RepoRoot `
        -Label 'Installed epic coherency review'
    $reviewEntry = Get-EpicAutopilotTreeEntry -RepoRoot $RepoRoot `
        -Commit $TargetCommit -Path $reviewRelative -Optional
    if ($null -ne $reviewEntry) {
        [void](Resolve-EpicAutopilotTrustedFile -Path $ReviewScriptPath `
                -RepoRoot $RepoRoot -ExpectedName 'Invoke-EpicCoherencyReview.ps1' `
                -Label 'Installed epic coherency review')
        Assert-EpicAutopilotWorktreeFileMatchesTree -RepoRoot $RepoRoot `
            -TreeEntry $reviewEntry
    }

    return [pscustomobject]@{
        CapturePath = $capturePath
        PlanDirectory = $planDirectory
        Message = if ($null -ne $reviewEntry) {
            'Epic final crosscheck passed via installed simplified epic coherency review.'
        }
        else {
            'Epic final crosscheck passed via fallback: complete merged rollup and non-empty canonical Goal and Definition of done.'
        }
        ReviewType = if ($null -ne $reviewEntry) { 'dr' } else { 'none' }
    }
}

function Restore-EpicAutopilotRecoveryResidue {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$TargetCommit,
        [Parameter(Mandatory)][string]$CapturePath,
        [Parameter(Mandatory)][byte[]]$Bytes,
        [Parameter(Mandatory)][bool]$Staged
    )

    & git -C $RepoRoot restore --source=$TargetCommit --staged --worktree -- `
        $CapturePath 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to restore canonical Capture path '$CapturePath'."
    }
    [System.IO.File]::WriteAllBytes((Join-Path $RepoRoot $CapturePath), $Bytes)
    if ($Staged) {
        & git -C $RepoRoot add -- $CapturePath 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to restore staged Capture residue '$CapturePath'."
        }
    }
    Assert-EpicAutopilotEvidenceStatus -RepoRoot $RepoRoot `
        -CapturePath $CapturePath -Staged:$Staged
}

function Get-EpicAutopilotLfByteIdentity {
    param([Parameter(Mandatory)][AllowEmptyCollection()][byte[]]$Bytes)

    $normalized = [System.IO.MemoryStream]::new($Bytes.Length)
    try {
        for ($index = 0; $index -lt $Bytes.Length; $index++) {
            if ($Bytes[$index] -eq 13 -and $index + 1 -lt $Bytes.Length -and
                $Bytes[$index + 1] -eq 10) {
                $normalized.WriteByte(10)
                $index++
            }
            else {
                $normalized.WriteByte($Bytes[$index])
            }
        }
        return [Convert]::ToBase64String($normalized.ToArray())
    }
    finally {
        $normalized.Dispose()
    }
}

function Repair-EpicAutopilotFinalEvidenceResidue {
    param(
        [Parameter(Mandatory)]$State,
        [Parameter(Mandatory)][string]$TargetBranch,
        [Parameter(Mandatory)][string]$TargetCommit,
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$ReviewScriptPath,
        [Parameter(Mandatory)][string]$EvidenceScriptPath,
        [Parameter(Mandatory)][scriptblock]$EvidenceRecorder
    )

    $unstaged = @(Get-EpicAutopilotGitPaths -RepoRoot $RepoRoot -Argument @(
            'diff', '--name-only', '--no-renames', '-z'
        ) -Label 'inspect recovery worktree changes')
    $cached = @(Get-EpicAutopilotGitPaths -RepoRoot $RepoRoot -Argument @(
            'diff', '--cached', '--name-only', '--no-renames', '-z'
        ) -Label 'inspect recovery index changes')
    $untracked = @(Get-EpicAutopilotGitPaths -RepoRoot $RepoRoot -Argument @(
            'ls-files', '--others', '--exclude-standard', '-z'
        ) -Label 'inspect recovery untracked files')
    if ($unstaged.Count -eq 0 -and $cached.Count -eq 0 -and
        $untracked.Count -eq 0) {
        return
    }
    if ($untracked.Count -ne 0) {
        return
    }
    if ([string]$State.target -ceq $TargetCommit) {
        return
    }
    & git -C $RepoRoot merge-base --is-ancestor ([string]$State.target) `
        $TargetCommit 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Abrupt final evidence recovery target '$TargetCommit' is not a forward descendant of retained target '$($State.target)'."
    }
    Assert-EpicAutopilotCheckedOutTarget -TargetBranch $TargetBranch `
        -TargetCommit $TargetCommit -RepoRoot $RepoRoot

    $descriptor = Get-EpicAutopilotRecoveryDescriptor -State $State `
        -RepoRoot $RepoRoot -TargetCommit $TargetCommit `
        -ReviewScriptPath $ReviewScriptPath
    $capturePath = [string]$descriptor.CapturePath
    $isUnstaged = $unstaged.Count -eq 1 -and $cached.Count -eq 0 -and
    [string]$unstaged[0] -ceq $capturePath
    $isStaged = $unstaged.Count -eq 0 -and $cached.Count -eq 1 -and
    [string]$cached[0] -ceq $capturePath
    if (-not $isUnstaged -and -not $isStaged) {
        throw "Abrupt final evidence recovery permits only sole unstaged or staged Capture residue '$capturePath'."
    }

    $residueBytes = [System.IO.File]::ReadAllBytes(
        (Join-Path $RepoRoot $capturePath)
    )
    try {
        & git -C $RepoRoot restore --source=$TargetCommit --staged --worktree -- `
            $capturePath 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to restore Capture path '$capturePath' for recovery verification."
        }
        Assert-EpicAutopilotCheckedOutTarget -TargetBranch $TargetBranch `
            -TargetCommit $TargetCommit -RepoRoot $RepoRoot
        Assert-EpicAutopilotEvidenceStatus -RepoRoot $RepoRoot `
            -CapturePath $capturePath -Clean

        $recordOutput = @(
            & $EvidenceRecorder $EvidenceScriptPath $descriptor.PlanDirectory `
                $descriptor.Message $descriptor.ReviewType $RepoRoot
        )
        $recordExit = ConvertTo-EpicAutopilotExitCode -Output $recordOutput `
            -Label 'Epic final evidence recovery writer'
        if ($recordExit -ne 0) {
            throw "Epic final evidence recovery writer failed with exit code '$recordExit'."
        }
        Assert-EpicAutopilotCheckedOutTarget -TargetBranch $TargetBranch `
            -TargetCommit $TargetCommit -RepoRoot $RepoRoot
        Assert-EpicAutopilotEvidenceStatus -RepoRoot $RepoRoot `
            -CapturePath $capturePath
        $expectedBytes = [System.IO.File]::ReadAllBytes(
            (Join-Path $RepoRoot $capturePath)
        )
        if ((Get-EpicAutopilotLfByteIdentity -Bytes $expectedBytes) -cne
            (Get-EpicAutopilotLfByteIdentity -Bytes $residueBytes)) {
            throw "Abrupt final evidence Capture residue '$capturePath' does not match deterministic writer output."
        }

        & git -C $RepoRoot restore --source=$TargetCommit --staged --worktree -- `
            $capturePath 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to finish Capture recovery for '$capturePath'."
        }
        Assert-EpicAutopilotCheckedOutTarget -TargetBranch $TargetBranch `
            -TargetCommit $TargetCommit -RepoRoot $RepoRoot
        Assert-EpicAutopilotEvidenceStatus -RepoRoot $RepoRoot `
            -CapturePath $capturePath -Clean
    }
    catch {
        $failure = $_
        try {
            Restore-EpicAutopilotRecoveryResidue -RepoRoot $RepoRoot `
                -TargetCommit $TargetCommit -CapturePath $capturePath `
                -Bytes $residueBytes -Staged:$isStaged
        }
        catch {
            throw "$($failure.Exception.Message) Recovery residue restoration also failed: $($_.Exception.Message)"
        }
        throw $failure
    }
}

function Get-EpicAutopilotEvidenceMessage {
    param(
        [Parameter(Mandatory)][string]$EpicId,
        [Parameter(Mandatory)][string]$ReviewedTarget,
        [Parameter(Mandatory)][string]$CapturePath
    )

    return @(
        "chore(autopilot): record epic $EpicId final crosscheck"
        ''
        'Epic-Autopilot-Evidence: v1'
        "Epic-Autopilot-Reviewed-Target: $ReviewedTarget"
        "Epic-Autopilot-Capture-Path: $CapturePath"
    ) -join "`n"
}

function Resolve-EpicAutopilotEvidenceCommit {
    param(
        [Parameter(Mandatory)][string]$TargetBranch,
        [Parameter(Mandatory)][string]$TargetCommit,
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$PlanFile,
        [Parameter(Mandatory)][string]$EpicId
    )

    Assert-EpicAutopilotCheckedOutTarget -TargetBranch $TargetBranch `
        -TargetCommit $TargetCommit -RepoRoot $RepoRoot
    $capture = Get-EpicAutopilotCapturePath -PlanFile $PlanFile `
        -RepoRoot $RepoRoot -Commit $TargetCommit
    $messageOutput = @(& git -C $RepoRoot show -s --format=%B $TargetCommit 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to inspect target commit '$TargetCommit' for final evidence."
    }
    $message = (($messageOutput | ForEach-Object { [string]$_ }) -join "`n").TrimEnd()
    $hasMarker = $message -cmatch '(?m)^Epic-Autopilot-Evidence:'
    $expectedSubject = "chore(autopilot): record epic $EpicId final crosscheck"
    if (-not $hasMarker -and -not $message.StartsWith(
            $expectedSubject,
            [System.StringComparison]::Ordinal
        )) {
        return [pscustomobject]@{
            IsReplay = $false
            ReviewedTarget = $TargetCommit
            EvidenceCommit = $null
            Capture = $capture
        }
    }

    $parentOutput = @(& git -C $RepoRoot show -s --format=%P $TargetCommit 2>&1)
    if ($LASTEXITCODE -ne 0 -or $parentOutput.Count -ne 1) {
        throw "Epic final evidence commit '$TargetCommit' has invalid parent metadata."
    }
    $parents = @(
        ([string]$parentOutput[0]).Trim().Split(
            ' ',
            [System.StringSplitOptions]::RemoveEmptyEntries
        )
    )
    if ($parents.Count -ne 1 -or
        $parents[0] -cnotmatch '^(?:[0-9a-f]{40}|[0-9a-f]{64})$') {
        throw "Epic final evidence commit '$TargetCommit' must have exactly one parent."
    }
    $reviewedTarget = $parents[0].ToLowerInvariant()
    $expectedMessage = Get-EpicAutopilotEvidenceMessage -EpicId $EpicId `
        -ReviewedTarget $reviewedTarget -CapturePath $capture.RelativePath
    if ($message -cne $expectedMessage) {
        throw "Epic final evidence commit '$TargetCommit' has invalid metadata."
    }
    $changedPaths = @(Get-EpicAutopilotGitPaths -RepoRoot $RepoRoot -Argument @(
            'diff-tree', '--no-commit-id', '--name-only', '-r', '-z',
            $reviewedTarget, $TargetCommit
        ) -Label "inspect final evidence commit '$TargetCommit'")
    if ($changedPaths.Count -ne 1 -or
        [string]$changedPaths[0] -cne $capture.RelativePath) {
        throw "Epic final evidence commit '$TargetCommit' must change only '$($capture.RelativePath)'."
    }
    return [pscustomobject]@{
        IsReplay = $true
        ReviewedTarget = $reviewedTarget
        EvidenceCommit = $TargetCommit
        Capture = $capture
    }
}

function Restore-EpicAutopilotEvidenceWorktree {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$CapturePath
    )

    & git -C $RepoRoot restore --source=HEAD --staged --worktree -- `
        $CapturePath 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to restore Capture/index path '$CapturePath' after final evidence failure."
    }
}

function Assert-EpicAutopilotEvidenceStatus {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$CapturePath,
        [switch]$Staged,
        [switch]$Clean
    )

    $unstaged = @(Get-EpicAutopilotGitPaths -RepoRoot $RepoRoot -Argument @(
            'diff', '--name-only', '--no-renames', '-z'
        ) -Label 'inspect unstaged final evidence changes')
    $cached = @(Get-EpicAutopilotGitPaths -RepoRoot $RepoRoot -Argument @(
            'diff', '--cached', '--name-only', '--no-renames', '-z'
        ) -Label 'inspect staged final evidence changes')
    $untracked = @(Get-EpicAutopilotGitPaths -RepoRoot $RepoRoot -Argument @(
            'ls-files', '--others', '--exclude-standard', '-z'
        ) -Label 'inspect untracked final evidence changes')

    if ($Clean) {
        if ($unstaged.Count -ne 0 -or $cached.Count -ne 0 -or
            $untracked.Count -ne 0) {
            throw 'Repository worktree must be clean after final evidence publication.'
        }
        return
    }
    $expectedUnstaged = if ($Staged) { 0 } else { 1 }
    $expectedCached = if ($Staged) { 1 } else { 0 }
    if ($unstaged.Count -ne $expectedUnstaged -or
        ($expectedUnstaged -eq 1 -and
        [string]$unstaged[0] -cne $CapturePath) -or
        $cached.Count -ne $expectedCached -or
        ($expectedCached -eq 1 -and
        [string]$cached[0] -cne $CapturePath) -or
        $untracked.Count -ne 0) {
        throw "Final crosscheck must change exactly tracked Capture path '$CapturePath'."
    }
}

function Publish-EpicAutopilotFinalEvidence {
    param(
        [Parameter(Mandatory)]$Descriptor,
        [Parameter(Mandatory)][string]$TargetBranch,
        [Parameter(Mandatory)][string]$TargetCommit,
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$EpicId,
        [Parameter(Mandatory)][string]$EvidenceScriptPath,
        [Parameter(Mandatory)][string]$PlanDirectory,
        [Parameter(Mandatory)][string]$Message,
        [Parameter(Mandatory)][string]$ReviewType,
        [Parameter(Mandatory)][scriptblock]$EvidenceRecorder
    )

    $capturePath = [string]$Descriptor.Capture.RelativePath
    try {
        $recordOutput = @(
            & $EvidenceRecorder $EvidenceScriptPath $PlanDirectory `
                $Message $ReviewType $RepoRoot
        )
        $recordExit = ConvertTo-EpicAutopilotExitCode -Output $recordOutput `
            -Label 'Epic final crosscheck evidence writer'
        if ($recordExit -ne 0) {
            throw "Epic final crosscheck evidence writer failed with exit code '$recordExit'."
        }

        if ([bool]$Descriptor.IsReplay) {
            Assert-EpicAutopilotCheckedOutTarget -TargetBranch $TargetBranch `
                -TargetCommit $TargetCommit -RepoRoot $RepoRoot
            Assert-EpicAutopilotEvidenceStatus -RepoRoot $RepoRoot `
                -CapturePath $capturePath -Clean
            return [string]$Descriptor.EvidenceCommit
        }

        Assert-EpicAutopilotCheckedOutTarget -TargetBranch $TargetBranch `
            -TargetCommit $TargetCommit -RepoRoot $RepoRoot
        Assert-EpicAutopilotEvidenceStatus -RepoRoot $RepoRoot `
            -CapturePath $capturePath
        & git -C $RepoRoot add -- $capturePath 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to stage final Capture path '$capturePath'."
        }
        Assert-EpicAutopilotEvidenceStatus -RepoRoot $RepoRoot `
            -CapturePath $capturePath -Staged
        $treeOutput = @(& git -C $RepoRoot write-tree 2>&1)
        if ($LASTEXITCODE -ne 0 -or $treeOutput.Count -ne 1) {
            throw 'Unable to write the final evidence commit tree.'
        }
        $commitMessage = Get-EpicAutopilotEvidenceMessage -EpicId $EpicId `
            -ReviewedTarget $TargetCommit -CapturePath $capturePath
        $messageParts = $commitMessage.Split("`n", 3)
        $commitOutput = @(
            & git -C $RepoRoot commit-tree ([string]$treeOutput[0]).Trim() `
                -p $TargetCommit -m $messageParts[0] -m $messageParts[2] 2>&1
        )
        if ($LASTEXITCODE -ne 0 -or $commitOutput.Count -ne 1 -or
            ([string]$commitOutput[0]).Trim() -cnotmatch
            '^(?:[0-9a-f]{40}|[0-9a-f]{64})$') {
            throw 'Unable to create the final evidence commit.'
        }
        $evidenceCommit = ([string]$commitOutput[0]).Trim().ToLowerInvariant()
        Assert-EpicAutopilotCheckedOutTarget -TargetBranch $TargetBranch `
            -TargetCommit $TargetCommit -RepoRoot $RepoRoot
        & git -C $RepoRoot update-ref "refs/heads/$TargetBranch" `
            $evidenceCommit $TargetCommit 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Checked-out target ref 'refs/heads/$TargetBranch' moved before final evidence publication."
        }
        Assert-EpicAutopilotCheckedOutTarget -TargetBranch $TargetBranch `
            -TargetCommit $evidenceCommit -RepoRoot $RepoRoot
        Assert-EpicAutopilotEvidenceStatus -RepoRoot $RepoRoot `
            -CapturePath $capturePath -Clean
        return $evidenceCommit
    }
    catch {
        $failure = $_
        try {
            Restore-EpicAutopilotEvidenceWorktree -RepoRoot $RepoRoot `
                -CapturePath $capturePath
        }
        catch {
            throw "$($failure.Exception.Message) Cleanup also failed: $($_.Exception.Message)"
        }
        Assert-EpicAutopilotEvidenceStatus -RepoRoot $RepoRoot `
            -CapturePath $capturePath -Clean
        throw $failure
    }
}

function ConvertTo-EpicAutopilotExitCode {
    param(
        [Parameter(Mandatory)][object[]]$Output,
        [Parameter(Mandatory)][string]$Label
    )

    if ($Output.Count -ne 1) {
        throw "$Label must return exactly one exit code."
    }
    $text = [string]$Output[0]
    if ($text -cnotmatch $script:PortableExitCodePattern) {
        throw "$Label returned invalid exit code '$text'."
    }
    return [int]::Parse(
        $text,
        [System.Globalization.NumberStyles]::None,
        [System.Globalization.CultureInfo]::InvariantCulture
    )
}

function Invoke-EpicAutopilotFinalCrosscheck {
    param(
        [Parameter(Mandatory)]$Rollup,
        $State,
        [Parameter(Mandatory)][string]$TargetBranch,
        [Parameter(Mandatory)][string]$TargetCommit,
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$ReviewScriptPath,
        [Parameter(Mandatory)][string]$EvidenceScriptPath,
        [Parameter(Mandatory)][scriptblock]$ReviewInvoker,
        [Parameter(Mandatory)][scriptblock]$EvidenceRecorder,
        [Parameter(Mandatory)][scriptblock]$EvidenceCommitResolver,
        [Parameter(Mandatory)][scriptblock]$EvidenceManager,
        [switch]$RequireTargetBoundSources
    )

    if (-not [bool]$Rollup.Rollup.IsComplete -or $null -ne $Rollup.NextChild) {
        throw 'Epic final crosscheck requires a complete rollup with no NextChild.'
    }
    $epicFile = if ($Rollup.PSObject.Properties.Name -contains 'EpicFile' -and
        $Rollup.EpicFile -is [string]) {
        Resolve-EpicAutopilotTrustedFile -Path $Rollup.EpicFile -RepoRoot $RepoRoot `
            -ExpectedName 'epic.md' -Label 'Canonical epic'
    }
    else {
        throw 'Complete epic rollup is missing its canonical EpicFile.'
    }
    $planFile = Resolve-EpicAutopilotTrustedFile `
        -Path (Get-EpicAutopilotFinalPlanFile -Rollup $Rollup -State $State) `
        -RepoRoot $RepoRoot -ExpectedName 'plan.md' -Label 'Final child plan'
    $descriptor = & $EvidenceCommitResolver $TargetBranch $TargetCommit `
        $RepoRoot $planFile ([string]$Rollup.EpicId)
    if ($null -eq $descriptor -or
        $descriptor.PSObject.Properties.Name -notcontains 'ReviewedTarget' -or
        [string]$descriptor.ReviewedTarget -cnotmatch
        '^(?:[0-9a-f]{40}|[0-9a-f]{64})$') {
        throw 'Epic final evidence resolver returned an invalid result.'
    }
    $reviewedTarget = [string]$descriptor.ReviewedTarget
    $reviewAvailable = Test-Path -LiteralPath $ReviewScriptPath -PathType Any
    if ($RequireTargetBoundSources) {
        foreach ($source in @(
                @{
                    Path = $epicFile
                    Label = 'Canonical epic'
                },
                @{
                    Path = $planFile
                    Label = 'Final child plan'
                }
            )) {
            $relative = ConvertTo-EpicAutopilotRepoRelativePath `
                -Path $source.Path -RepoRoot $RepoRoot -Label $source.Label
            $entry = Get-EpicAutopilotTreeEntry -RepoRoot $RepoRoot `
                -Commit $reviewedTarget -Path $relative
            Assert-EpicAutopilotWorktreeFileMatchesTree -RepoRoot $RepoRoot `
                -TreeEntry $entry
        }
        $reviewRelative = ConvertTo-EpicAutopilotRepoRelativePath `
            -Path $ReviewScriptPath -RepoRoot $RepoRoot `
            -Label 'Installed epic coherency review'
        $reviewEntry = Get-EpicAutopilotTreeEntry -RepoRoot $RepoRoot `
            -Commit $reviewedTarget -Path $reviewRelative -Optional
        $reviewAvailable = $null -ne $reviewEntry
        if ($reviewAvailable) {
            [void](Resolve-EpicAutopilotTrustedFile -Path $ReviewScriptPath `
                    -RepoRoot $RepoRoot `
                    -ExpectedName 'Invoke-EpicCoherencyReview.ps1' `
                    -Label 'Installed epic coherency review')
            Assert-EpicAutopilotWorktreeFileMatchesTree -RepoRoot $RepoRoot `
                -TreeEntry $reviewEntry
        }
    }

    $mode = 'fallback'
    $reviewType = 'none'
    $message = 'Epic final crosscheck passed via fallback: complete merged rollup and non-empty canonical Goal and Definition of done.'
    if ($reviewAvailable) {
        $reviewScript = Resolve-EpicAutopilotTrustedFile -Path $ReviewScriptPath `
            -RepoRoot $RepoRoot -ExpectedName 'Invoke-EpicCoherencyReview.ps1' `
            -Label 'Installed epic coherency review'
        $reviewOutput = @(& $ReviewInvoker $reviewScript $epicFile $reviewedTarget $RepoRoot)
        $reviewExit = ConvertTo-EpicAutopilotExitCode -Output $reviewOutput `
            -Label 'Installed epic coherency review'
        if ($reviewExit -ne 0) {
            throw "Installed epic coherency review failed with exit code '$reviewExit'; fallback is not permitted."
        }
        $mode = 'review'
        $reviewType = 'dr'
        $message = 'Epic final crosscheck passed via installed simplified epic coherency review.'
    }
    else {
        $epicBytes = [System.IO.File]::ReadAllBytes($epicFile)
        if ($epicBytes.Length -gt 1MB) {
            throw 'Canonical epic exceeds the 1 MiB fallback crosscheck bound.'
        }
        $epicText = [System.Text.UTF8Encoding]::new($false, $true).GetString($epicBytes)
        foreach ($heading in @('Goal', 'Definition of done')) {
            if (-not (Test-EpicAutopilotIntentSection -Content $epicText -Heading $heading)) {
                throw "Canonical epic fallback crosscheck requires a non-empty '$heading' section."
            }
        }
    }

    if (-not (Test-Path -LiteralPath $EvidenceScriptPath -PathType Leaf)) {
        throw "Epic final crosscheck evidence writer not found: $EvidenceScriptPath"
    }
    [void](& $EvidenceManager $descriptor $TargetBranch $TargetCommit $RepoRoot `
        ([string]$Rollup.EpicId) $EvidenceScriptPath `
        (Split-Path -Parent $planFile) $message $reviewType $EvidenceRecorder)
    return $mode
}

function Resolve-EpicAutopilotTargetBranch {
    param(
        [Parameter(Mandatory)][string]$Target,
        [Parameter(Mandatory)][string]$RepoRoot
    )

    $branch = $Target
    if ($branch -ceq 'HEAD') {
        $branchOutput = @(& git -C $RepoRoot symbolic-ref --quiet --short HEAD 2>&1)
        if ($LASTEXITCODE -ne 0 -or $branchOutput.Count -ne 1) {
            throw 'Target HEAD does not name a local branch.'
        }
        $branch = ([string]$branchOutput[0]).Trim()
    }
    elseif ($branch.StartsWith('refs/heads/', [System.StringComparison]::Ordinal)) {
        $branch = $branch.Substring('refs/heads/'.Length)
    }

    if ($branch -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._/-]*$' -or
        $branch.Contains('..') -or $branch.Contains('//') -or
        $branch.EndsWith('/') -or $branch.EndsWith('.') -or
        $branch.EndsWith('.lock', [System.StringComparison]::OrdinalIgnoreCase) -or
        $branch.Contains('@{')) {
        throw "Target '$Target' must name a valid local branch or refs/heads/<branch> ref."
    }
    & git -C $RepoRoot check-ref-format --branch $branch 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Target '$Target' must name a valid local branch or refs/heads/<branch> ref."
    }
    return $branch
}

function Get-EpicAutopilotTerminalReplay {
    param(
        [Parameter(Mandatory)]$State,
        [Parameter(Mandatory)][string]$StatePath
    )

    $isInvocationFailure = $State.outcome -ceq 'invocation-failed'
    $isNonzeroExit = $State.outcome.StartsWith(
        'exit:',
        [System.StringComparison]::Ordinal
    ) -and $State.outcome -cne 'exit:0'
    if (-not $isInvocationFailure -and -not $isNonzeroExit) {
        return $null
    }

    $storedExit = if ($isNonzeroExit) {
        [int]$State.outcome.Substring(5)
    }
    else {
        1
    }
    return [pscustomobject]@{
        State = $State
        NextChild = $null
        Resumed = $true
        StatePath = $StatePath
        Launch = $false
        LaunchAttempted = $false
        Replayed = $true
        ExitCode = $storedExit
        Failed = $true
        Completed = $false
        Blocked = $false
        Message = if ($isInvocationFailure) {
            "Epic child '$($State.currentChild)' on branch '$($State.branch)' in run '$($State.run)' has terminal outcome 'invocation-failed'."
        }
        elseif ($storedExit -eq 42) {
            "Epic child '$($State.currentChild)' on branch '$($State.branch)' in run '$($State.run)' stopped for operator action with exit code 42."
        }
        else {
            "Epic child '$($State.currentChild)' on branch '$($State.branch)' in run '$($State.run)' failed with exit code $storedExit."
        }
    }
}

function Invoke-EpicAutopilotHostLoopCore {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Epic,
        [string]$Target = 'HEAD',
        [string]$RepoRoot,
        [string]$StatePath,
        [string]$PlanStateScript = (Join-Path $PSScriptRoot 'Get-PlanState.ps1'),
        [scriptblock]$PlanStateInvoker,
        [scriptblock]$TargetResolver,
        [scriptblock]$WorktreeValidator,
        [scriptblock]$RunFactory,
        [scriptblock]$LauncherInvoker,
        [scriptblock]$ContainerProbe,
        [scriptblock]$RunLeaseProbe,
        [scriptblock]$AncestorTester,
        [scriptblock]$StateDeleteInvoker,
        [string]$ReviewScriptPath,
        [scriptblock]$ReviewInvoker,
        [scriptblock]$FinalEvidenceRecorder,
        [scriptblock]$FinalEvidenceCommitResolver,
        [scriptblock]$FinalEvidenceManager
    )

    if ($env:AUTOPILOT_CONTAINER -ceq 'true') {
        throw 'Epic autopilot is host-owned and cannot run inside an autopilot container.'
    }

    if (-not $RepoRoot) {
        $rootOutput = @(& git rev-parse --show-toplevel 2>&1)
        if ($LASTEXITCODE -ne 0 -or $rootOutput.Count -ne 1) {
            throw "Unable to resolve the repository root: $(($rootOutput -join ' ').Trim())"
        }
        $RepoRoot = [string]$rootOutput[0]
    }
    $repoRootPath = [System.IO.Path]::GetFullPath($RepoRoot)
    $stateFile = Resolve-EpicAutopilotStatePath -RepoRoot $repoRootPath -StatePath $StatePath
    $targetBranch = Resolve-EpicAutopilotTargetBranch -Target $Target -RepoRoot $repoRootPath

    if (-not $PlanStateInvoker) {
        $PlanStateInvoker = {
            param($EpicReference, $Root, $ScriptPath)
            if (-not (Test-Path -LiteralPath $ScriptPath -PathType Leaf)) {
                throw "Get-PlanState script not found: $ScriptPath"
            }
            $output = @(
                & $ScriptPath -Reference $EpicReference -RepoRoot $Root -Epic -Json
            )
            return ($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
        }
    }
    if (-not $TargetResolver) {
        $TargetResolver = {
            param($BranchName, $Root)
            $output = @(
                & git -C $Root rev-parse --verify "refs/heads/$BranchName`^{commit}" 2>&1
            )
            if ($LASTEXITCODE -ne 0 -or $output.Count -ne 1) {
                throw "Unable to resolve target branch '$BranchName': $(($output -join ' ').Trim())"
            }
            return ([string]$output[0]).Trim().ToLowerInvariant()
        }
    }
    $usesDefaultWorktreeValidator = -not [bool]$WorktreeValidator
    if (-not $WorktreeValidator) {
        $WorktreeValidator = {
            param($Root, $Commit)
            Assert-EpicAutopilotWorktree -RepoRoot $Root -TargetCommit $Commit
        }
    }
    if (-not $RunFactory) {
        $RunFactory = { [guid]::NewGuid().ToString('D') }
    }
    if (-not $LauncherInvoker) {
        $LauncherInvoker = {
            param($LaunchScript, $Argument, $Root)
            Invoke-EpicChildLauncher -LaunchScript $LaunchScript -Argument $Argument `
                -WorkingDirectory $Root
        }
    }
    if (-not $ContainerProbe) {
        $ContainerProbe = {
            param($ContainerName)
            Test-EpicAutopilotContainerActive -ContainerName $ContainerName
        }
    }
    if (-not $RunLeaseProbe) {
        $RunLeaseProbe = {
            param($Path, $Run)
            Test-EpicAutopilotRunLeaseActive -StatePath $Path -Run $Run
        }
    }
    if (-not $AncestorTester) {
        $AncestorTester = {
            param($Ancestor, $Descendant, $Root)
            Test-EpicAutopilotAncestor -Ancestor $Ancestor -Descendant $Descendant `
                -RepoRoot $Root
        }
    }
    if (-not $StateDeleteInvoker) {
        $StateDeleteInvoker = {
            param($Path, $ExpectedGeneration)
            Remove-EpicAutopilotState -Path $Path `
                -ExpectedGeneration $ExpectedGeneration
        }
    }
    if (-not $ReviewScriptPath) {
        $ReviewScriptPath = [System.IO.Path]::Combine(
            $repoRootPath,
            '.github',
            'skills',
            'cep',
            'scripts',
            'Invoke-EpicCoherencyReview.ps1'
        )
    }
    $evidenceScriptPath = Join-Path $PSScriptRoot 'Add-WorkflowNote.ps1'
    if (-not $ReviewInvoker) {
        $ReviewInvoker = {
            param($ScriptPath, $EpicFile, $TargetCommit, $Root)
            Invoke-EpicChildLauncher -LaunchScript $ScriptPath -Argument @(
                '-EpicFile', $EpicFile,
                '-TargetCommit', $TargetCommit,
                '-RepoRoot', $Root
            ) -WorkingDirectory $Root
        }
    }
    if (-not $FinalEvidenceRecorder) {
        $FinalEvidenceRecorder = {
            param($ScriptPath, $PlanDir, $Message, $ReviewType, $Root)
            Invoke-EpicChildLauncher -LaunchScript $ScriptPath -Argument @(
                '-Kind', 'Capture',
                '-PlanDir', $PlanDir,
                '-Phase', '0',
                '-Message', $Message,
                '-Src', 'note',
                '-Concern', 'architecture-patterns',
                '-ReviewType', $ReviewType,
                '-RepoRoot', $Root
            ) -WorkingDirectory $Root
        }
    }
    if (-not $FinalEvidenceCommitResolver) {
        $FinalEvidenceCommitResolver = {
            param($Branch, $Commit, $Root, $PlanFile, $EpicId)
            Resolve-EpicAutopilotEvidenceCommit -TargetBranch $Branch `
                -TargetCommit $Commit -RepoRoot $Root -PlanFile $PlanFile `
                -EpicId $EpicId
        }
    }
    if (-not $FinalEvidenceManager) {
        $FinalEvidenceManager = {
            param(
                $Descriptor,
                $Branch,
                $Commit,
                $Root,
                $EpicId,
                $ScriptPath,
                $PlanDirectory,
                $Message,
                $ReviewType,
                $Recorder
            )
            Publish-EpicAutopilotFinalEvidence -Descriptor $Descriptor `
                -TargetBranch $Branch -TargetCommit $Commit -RepoRoot $Root `
                -EpicId $EpicId -EvidenceScriptPath $ScriptPath `
                -PlanDirectory $PlanDirectory -Message $Message `
                -ReviewType $ReviewType -EvidenceRecorder $Recorder
        }
    }

    $runLease = [pscustomobject]@{ Value = $null }
    try {
        $admission = Invoke-WithAtomicStoreLock -Scope $stateFile -Action {
            $generation = Get-AtomicStoreGeneration -Path $stateFile
            $existing = Read-EpicAutopilotState -Path $stateFile

            if ($existing -and $Epic -cmatch '^[0-9a-f]{6}$') {
                if ($existing.epic -cne $Epic) {
                    throw "Existing epic autopilot state belongs to epic '$($existing.epic)', not requested epic '$Epic'."
                }
                $terminalReplay = Get-EpicAutopilotTerminalReplay `
                    -State $existing -StatePath $stateFile
                if ($null -ne $terminalReplay) {
                    return $terminalReplay
                }
            }

            $targetCommit = [string](& $TargetResolver $targetBranch $repoRootPath)
            $targetCommit = $targetCommit.Trim().ToLowerInvariant()
            if ($targetCommit -cnotmatch '^(?:[0-9a-f]{40}|[0-9a-f]{64})$') {
                throw "Target '$Target' resolved to invalid commit id '$targetCommit'."
            }
            $isRetainedSuccess = $null -ne $existing -and (
                $existing.outcome -ceq 'awaiting-merge' -or
                $existing.outcome -ceq 'exit:0'
            )
            if ($usesDefaultWorktreeValidator -and $isRetainedSuccess) {
                Repair-EpicAutopilotFinalEvidenceResidue -State $existing `
                    -TargetBranch $targetBranch -TargetCommit $targetCommit `
                    -RepoRoot $repoRootPath -ReviewScriptPath $ReviewScriptPath `
                    -EvidenceScriptPath $evidenceScriptPath `
                    -EvidenceRecorder $FinalEvidenceRecorder
            }
            try {
                & $WorktreeValidator $repoRootPath $targetCommit
            }
            catch {
                throw "Epic target worktree validation failed: $($_.Exception.Message)"
            }

            try {
                $rollupJson = & $PlanStateInvoker $Epic $repoRootPath $PlanStateScript
            }
            catch {
                throw "Get-PlanState epic rollup failed: $($_.Exception.Message)"
            }
            $rollup = ConvertFrom-EpicRollupJson -Json ([string]$rollupJson)
            if ($Epic -cmatch '^[0-9a-f]{6}$' -and $Epic -cne [string]$rollup.EpicId) {
                throw "Get-PlanState resolved epic '$($rollup.EpicId)', not requested epic '$Epic'."
            }

            if ($existing) {
                if ($existing.epic -cne [string]$rollup.EpicId) {
                    throw "Existing epic autopilot state belongs to epic '$($existing.epic)', not '$($rollup.EpicId)'."
                }

                $isSuccessfulCheckpoint = $existing.outcome -ceq 'awaiting-merge' -or
                $existing.outcome -ceq 'exit:0'
                $isTerminalFailure = $existing.outcome -ceq 'invocation-failed' -or (
                    $existing.outcome.StartsWith('exit:', [System.StringComparison]::Ordinal) -and
                    $existing.outcome -cne 'exit:0'
                )

                if ($isTerminalFailure) {
                    return Get-EpicAutopilotTerminalReplay `
                        -State $existing -StatePath $stateFile
                }

                if ($isSuccessfulCheckpoint) {
                    if ($existing.target -ceq $targetCommit) {
                        return [pscustomobject]@{
                            State = $existing
                            NextChild = $rollup.NextChild
                            Resumed = $true
                            StatePath = $stateFile
                            Launch = $false
                            LaunchAttempted = $false
                            Replayed = $true
                            ExitCode = 0
                            Failed = $false
                            Completed = $false
                            Blocked = $false
                            Message = $null
                        }
                    }

                    $ancestryOutput = @(
                        & $AncestorTester $existing.target $targetCommit $repoRootPath
                    )
                    if ($ancestryOutput.Count -ne 1 -or
                        $ancestryOutput[0] -isnot [bool]) {
                        throw 'Target ancestry probe returned an invalid result.'
                    }
                    if (-not [bool]$ancestryOutput[0]) {
                        throw "Refreshed target '$targetCommit' is not a forward descendant of prior target '$($existing.target)'."
                    }

                    $priorChildren = @(
                        $rollup.Children |
                            Where-Object { [string]$_.Id -ceq $existing.currentChild }
                    )
                    if ($priorChildren.Count -ne 1) {
                        throw "Refreshed epic graph must contain prior child '$($existing.currentChild)' exactly once; found $($priorChildren.Count)."
                    }
                    if (-not [bool]$priorChildren[0].IsComplete) {
                        throw "Refreshed epic graph still reports prior child '$($existing.currentChild)' incomplete."
                    }
                    if ($null -ne $rollup.NextChild -and
                        [string]$rollup.NextChild.Id -ceq $existing.currentChild) {
                        throw "Refreshed epic graph still selects prior child '$($existing.currentChild)' as NextChild."
                    }

                    if ($null -eq $rollup.NextChild) {
                        if (-not [bool]$rollup.Rollup.IsComplete) {
                            return [pscustomobject]@{
                                State = $existing
                                NextChild = $null
                                Resumed = $true
                                StatePath = $stateFile
                                Launch = $false
                                LaunchAttempted = $false
                                Replayed = $true
                                ExitCode = 42
                                Failed = $false
                                Completed = $false
                                Blocked = $true
                                Message = 'Epic graph is incomplete but has no eligible NextChild; prior success checkpoint is retained for explicit resume.'
                            }
                        }

                        $finalCrosscheck = Invoke-EpicAutopilotFinalCrosscheck `
                            -Rollup $rollup -State $existing `
                            -TargetBranch $targetBranch -TargetCommit $targetCommit `
                            -RepoRoot $repoRootPath -ReviewScriptPath $ReviewScriptPath `
                            -EvidenceScriptPath $evidenceScriptPath `
                            -ReviewInvoker $ReviewInvoker `
                            -EvidenceRecorder $FinalEvidenceRecorder `
                            -EvidenceCommitResolver $FinalEvidenceCommitResolver `
                            -EvidenceManager $FinalEvidenceManager `
                            -RequireTargetBoundSources:$usesDefaultWorktreeValidator
                        $deleteOutput = @(
                            & $StateDeleteInvoker $stateFile $generation
                        )
                        if ($deleteOutput.Count -ne 1 -or
                            $null -eq $deleteOutput[0] -or
                            $deleteOutput[0].PSObject.Properties.Name -notcontains 'Status' -or
                            [string]$deleteOutput[0].Status -cne 'complete') {
                            $status = if ($deleteOutput.Count -eq 1 -and
                                $null -ne $deleteOutput[0] -and
                                $deleteOutput[0].PSObject.Properties.Name -contains 'Status') {
                                [string]$deleteOutput[0].Status
                            }
                            else { 'invalid' }
                            throw "Epic autopilot completed-state delete failed with status '$status'."
                        }
                        return [pscustomobject]@{
                            State = $null
                            NextChild = $null
                            Resumed = $true
                            StatePath = $stateFile
                            Launch = $false
                            LaunchAttempted = $false
                            Replayed = $false
                            ExitCode = 0
                            Failed = $false
                            Completed = $true
                            Blocked = $false
                            FinalCrosscheck = $finalCrosscheck
                            Message = 'Epic graph is complete after the operator merge and final crosscheck.'
                        }
                    }

                    if ([bool]$rollup.Rollup.IsComplete) {
                        throw 'Get-PlanState reports a complete epic with a non-null NextChild.'
                    }

                    $selected = [pscustomobject][ordered]@{
                        epic = [string]$rollup.EpicId
                        target = $targetCommit
                        currentChild = [string]$rollup.NextChild.Id
                        branch = "feature/$($rollup.NextChild.FolderName)"
                        run = [string](& $RunFactory)
                        outcome = 'selected'
                    }
                    $selectedWrite = Set-EpicAutopilotState -Path $stateFile `
                        -State $selected -ExpectedGeneration $generation `
                        -Operation 'next-child selected state replacement'
                    $generation = $selectedWrite.Generation
                    $existing = $selected
                }

                if ($existing.target -cne $targetCommit) {
                    throw "Existing epic autopilot state targets '$($existing.target)', but '$Target' is now '$targetCommit'."
                }
                if ($null -eq $rollup.NextChild) {
                    throw "Existing epic autopilot state selects '$($existing.currentChild)', but Get-PlanState has no NextChild."
                }

                $expectedBranch = "feature/$($rollup.NextChild.FolderName)"
                if ($existing.currentChild -cne [string]$rollup.NextChild.Id -or
                    $existing.branch -cne $expectedBranch) {
                    throw "Existing epic autopilot state selects '$($existing.currentChild)' on '$($existing.branch)'; refusing conflicting NextChild '$($rollup.NextChild.Id)' on '$expectedBranch'."
                }

                if ($existing.outcome -ceq 'running') {
                    $containerName = Get-EpicAutopilotContainerName -Run $existing.run
                    try {
                        $leaseOutput = @(& $RunLeaseProbe $stateFile $existing.run)
                    }
                    catch {
                        throw "Unable to determine whether epic autopilot run '$($existing.run)' has an active host launcher: $($_.Exception.Message)"
                    }
                    if ($leaseOutput.Count -ne 1 -or $leaseOutput[0] -isnot [bool]) {
                        throw "Unable to determine whether epic autopilot run '$($existing.run)' has an active host launcher: run lease probe returned an invalid result."
                    }
                    if ([bool]$leaseOutput[0]) {
                        throw "Epic autopilot run '$($existing.run)' already has an active host launcher; refusing a second child launcher."
                    }
                    try {
                        $probeOutput = @(& $ContainerProbe $containerName)
                    }
                    catch {
                        throw "Unable to determine whether epic autopilot run '$($existing.run)' is active: $($_.Exception.Message)"
                    }
                    if ($probeOutput.Count -ne 1 -or $probeOutput[0] -isnot [bool]) {
                        throw "Unable to determine whether epic autopilot run '$($existing.run)' is active: container probe returned an invalid result."
                    }
                    if ([bool]$probeOutput[0]) {
                        throw "Epic autopilot run '$($existing.run)' is already running in container '$containerName'; refusing a second child launcher."
                    }

                    $reconciled = New-EpicAutopilotState -State $existing `
                        -Outcome 'invocation-failed'
                    [void](Set-EpicAutopilotState -Path $stateFile -State $reconciled `
                            -ExpectedGeneration $generation `
                            -Operation 'running-state reconciliation')
                    return [pscustomobject]@{
                        State = $reconciled
                        NextChild = $rollup.NextChild
                        Resumed = $true
                        StatePath = $stateFile
                        Launch = $false
                        LaunchAttempted = $false
                        Replayed = $true
                        ExitCode = 1
                        Failed = $true
                        Message = "Interrupted epic child '$($existing.currentChild)' on branch '$($existing.branch)' in run '$($existing.run)' has no active container '$containerName'; reconciled to invocation-failed without relaunch."
                    }
                }
            }
            elseif ($null -eq $rollup.NextChild) {
                $finalCrosscheck = if ([bool]$rollup.Rollup.IsComplete) {
                    Invoke-EpicAutopilotFinalCrosscheck -Rollup $rollup `
                        -TargetBranch $targetBranch -TargetCommit $targetCommit `
                        -RepoRoot $repoRootPath `
                        -ReviewScriptPath $ReviewScriptPath `
                        -EvidenceScriptPath $evidenceScriptPath `
                        -ReviewInvoker $ReviewInvoker `
                        -EvidenceRecorder $FinalEvidenceRecorder `
                        -EvidenceCommitResolver $FinalEvidenceCommitResolver `
                        -EvidenceManager $FinalEvidenceManager `
                        -RequireTargetBoundSources:$usesDefaultWorktreeValidator
                }
                else { $null }
                return [pscustomobject]@{
                    State = $null
                    NextChild = $null
                    Resumed = $false
                    StatePath = $stateFile
                    Launch = $false
                    LaunchAttempted = $false
                    Replayed = $false
                    ExitCode = if ([bool]$rollup.Rollup.IsComplete) { 0 } else { 42 }
                    Failed = $false
                    Completed = [bool]$rollup.Rollup.IsComplete
                    Blocked = -not [bool]$rollup.Rollup.IsComplete
                    FinalCrosscheck = $finalCrosscheck
                    Message = if ([bool]$rollup.Rollup.IsComplete) {
                        'Epic graph is complete; no child launch is needed.'
                    }
                    else {
                        'Epic graph is incomplete but has no eligible NextChild.'
                    }
                }
            }

            $resumed = $null -ne $existing
            $selected = $existing
            if (-not $selected) {
                $selected = [pscustomobject][ordered]@{
                    epic = [string]$rollup.EpicId
                    target = $targetCommit
                    currentChild = [string]$rollup.NextChild.Id
                    branch = "feature/$($rollup.NextChild.FolderName)"
                    run = [string](& $RunFactory)
                    outcome = 'selected'
                }
                $selectedWrite = Set-EpicAutopilotState -Path $stateFile -State $selected `
                    -ExpectedGeneration 'absent' -Operation 'selected state write'
                $generation = $selectedWrite.Generation
            }

            $runLease.Value = Enter-EpicAutopilotRunLease `
                -StatePath $stateFile -Run $selected.run
            $running = New-EpicAutopilotState -State $selected -Outcome 'running'
            $runningWrite = Set-EpicAutopilotState -Path $stateFile -State $running `
                -ExpectedGeneration $generation -Operation 'running state write'

            return [pscustomobject]@{
                State = $running
                NextChild = $rollup.NextChild
                Resumed = $resumed
                StatePath = $stateFile
                Launch = $true
                LaunchAttempted = $false
                Replayed = $false
                ExitCode = $null
                Failed = $false
                Message = $null
                Generation = $runningWrite.Generation
            }
        }

        if (-not $admission.Launch) {
            return $admission
        }

        $launchScript = Join-Path $repoRootPath '.github/skills/autopilot/scripts/launch.ps1'
        $launchArguments = @(
            '-PlanSlug', [string]$admission.NextChild.FolderName,
            '-Mode', 'whole-plan',
            '-Runtime', 'container',
            '-Branch', $targetBranch,
            '-ExpectedStartCommit', [string]$admission.State.target,
            '-ExpectedPullRequestBase', $targetBranch,
            '-Run', [string]$admission.State.run
        )
        $launchFailureKind = 'start-failed'
        try {
            $launcherOutput = @(
                & $LauncherInvoker $launchScript $launchArguments $repoRootPath
            )
            if ($launcherOutput.Count -ne 1) {
                $launchFailureKind = 'result-count-invalid'
                throw 'Per-plan launcher invoker must return exactly one exit code.'
            }
            $launcherExitText = [string]$launcherOutput[0]
            if ($launcherExitText -cnotmatch $script:PortableExitCodePattern) {
                $launchFailureKind = 'exit-code-invalid'
                throw "Per-plan launcher invoker returned invalid exit code '$($launcherOutput[0])'."
            }
            $launcherExit = [int]::Parse(
                $launcherExitText,
                [System.Globalization.NumberStyles]::None,
                [System.Globalization.CultureInfo]::InvariantCulture
            )
        }
        catch {
            $launchErrorType = $_.Exception.GetType().Name
            try {
                $failedState = Set-EpicAutopilotTerminalState -Path $stateFile `
                    -RunningState $admission.State -RunningGeneration $admission.Generation `
                    -Outcome 'invocation-failed'
            }
            catch {
                throw "Per-plan launcher invocation failed for child '$($admission.State.currentChild)' in run '$($admission.State.run)' ($launchErrorType), and its terminal checkpoint could not be persisted."
            }
            return [pscustomobject]@{
                State = $failedState
                NextChild = $admission.NextChild
                Resumed = $admission.Resumed
                StatePath = $stateFile
                Launch = $false
                LaunchAttempted = $true
                Replayed = $false
                ExitCode = 1
                Failed = $true
                Message = "Per-plan launcher invocation failed for child '$($admission.State.currentChild)' on branch '$($admission.State.branch)' in run '$($admission.State.run)' ($launchFailureKind/$launchErrorType); the invocation-failed checkpoint is immutable."
            }
        }

        $terminalOutcome = if ($launcherExit -eq 0) {
            'awaiting-merge'
        }
        else {
            "exit:$launcherExit"
        }
        $terminal = Set-EpicAutopilotTerminalState -Path $stateFile `
            -RunningState $admission.State -RunningGeneration $admission.Generation `
            -Outcome $terminalOutcome
        return [pscustomobject]@{
            State = $terminal
            NextChild = $admission.NextChild
            Resumed = $admission.Resumed
            StatePath = $stateFile
            Launch = $true
            LaunchAttempted = $true
            Replayed = $false
            ExitCode = $launcherExit
            Failed = $launcherExit -ne 0
            Message = if ($launcherExit -eq 0) {
                $null
            }
            elseif ($launcherExit -eq 42) {
                "Epic child '$($terminal.currentChild)' on branch '$($terminal.branch)' in run '$($terminal.run)' stopped for operator action with exit code 42."
            }
            else {
                "Epic child '$($terminal.currentChild)' on branch '$($terminal.branch)' in run '$($terminal.run)' failed with exit code $launcherExit."
            }
        }
    }
    finally {
        if ($null -ne $runLease.Value) {
            Exit-EpicAutopilotRunLease -Lease $runLease.Value
        }
    }
}

function Invoke-EpicAutopilotHostLoop {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Epic,
        [string]$Target = 'HEAD',
        [string]$RepoRoot
    )

    return Invoke-EpicAutopilotHostLoopCore @PSBoundParameters
}

Export-ModuleMember -Function Invoke-EpicAutopilotHostLoop
