#requires -Version 7.0

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'WorkHierarchy.psm1') -DisableNameChecking

$script:GhTimeoutMilliseconds = 30000
$script:GhMaxOutputBytes = 8MB
$script:GhMaxErrorBytes = 64KB

function Assert-GitHubWorkHierarchyRepository {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Repository
    )

    if ($Repository -notmatch '^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})/[A-Za-z0-9._-]+$') {
        throw "GitHub repository must use the 'owner/name' form."
    }
}

function Get-GitHubWorkHierarchyProperty {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        $InputObject,

        [Parameter(Mandatory)]
        [string]$Name
    )

    if ($InputObject.PSObject.Properties.Name -notcontains $Name) {
        throw "GitHub adapter input is missing required property '$Name'."
    }
    return $InputObject.$Name
}

function Invoke-GitHubWorkHierarchyCommand {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments,

        [scriptblock]$CommandRunner,

        [switch]$AllowNotFound
    )

    $result = if ($CommandRunner) {
        & $CommandRunner $Arguments
    }
    else {
        $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = 'gh'
        $startInfo.UseShellExecute = $false
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        foreach ($argument in $Arguments) {
            $startInfo.ArgumentList.Add($argument)
        }

        $process = [System.Diagnostics.Process]::new()
        $process.StartInfo = $startInfo
        try {
            if (-not $process.Start()) {
                throw "GitHub CLI process did not start."
            }
            $stdout = [System.Text.StringBuilder]::new()
            $stderr = [System.Text.StringBuilder]::new()
            $stdoutBytes = 0
            $stderrBytes = 0
            $stdoutDone = $false
            $stderrDone = $false
            $stdoutBuffer = [char[]]::new(4096)
            $stderrBuffer = [char[]]::new(4096)
            $stdoutRead = $process.StandardOutput.ReadAsync($stdoutBuffer, 0, $stdoutBuffer.Length)
            $stderrRead = $process.StandardError.ReadAsync($stderrBuffer, 0, $stderrBuffer.Length)
            $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

            while (-not ($process.HasExited -and $stdoutDone -and $stderrDone)) {
                if ($stopwatch.ElapsedMilliseconds -ge $script:GhTimeoutMilliseconds) {
                    $process.Kill($true)
                    $process.WaitForExit()
                    throw "GitHub CLI request exceeded $($script:GhTimeoutMilliseconds / 1000) seconds."
                }

                $pending = [System.Collections.Generic.List[System.Threading.Tasks.Task]]::new()
                if (-not $stdoutDone) { $pending.Add($stdoutRead) }
                if (-not $stderrDone) { $pending.Add($stderrRead) }
                if ($pending.Count -eq 0) {
                    [System.Threading.Thread]::Sleep(10)
                    continue
                }
                $completed = [System.Threading.Tasks.Task]::WaitAny($pending.ToArray(), 100)
                if ($completed -lt 0) { continue }
                $task = $pending[$completed]

                if (-not $stdoutDone -and [object]::ReferenceEquals($task, $stdoutRead)) {
                    $count = $stdoutRead.GetAwaiter().GetResult()
                    if ($count -eq 0) {
                        $stdoutDone = $true
                    }
                    else {
                        $chunk = [string]::new($stdoutBuffer, 0, $count)
                        $stdoutBytes += [System.Text.Encoding]::UTF8.GetByteCount($chunk)
                        if ($stdoutBytes -gt $script:GhMaxOutputBytes) {
                            $process.Kill($true)
                            $process.WaitForExit()
                            throw "GitHub CLI response exceeded the adapter byte limit."
                        }
                        [void]$stdout.Append($chunk)
                        $stdoutRead = $process.StandardOutput.ReadAsync($stdoutBuffer, 0, $stdoutBuffer.Length)
                    }
                }
                elseif (-not $stderrDone -and [object]::ReferenceEquals($task, $stderrRead)) {
                    $count = $stderrRead.GetAwaiter().GetResult()
                    if ($count -eq 0) {
                        $stderrDone = $true
                    }
                    else {
                        $chunk = [string]::new($stderrBuffer, 0, $count)
                        $stderrBytes += [System.Text.Encoding]::UTF8.GetByteCount($chunk)
                        if ($stderrBytes -gt $script:GhMaxErrorBytes) {
                            $process.Kill($true)
                            $process.WaitForExit()
                            throw "GitHub CLI response exceeded the adapter byte limit."
                        }
                        [void]$stderr.Append($chunk)
                        $stderrRead = $process.StandardError.ReadAsync($stderrBuffer, 0, $stderrBuffer.Length)
                    }
                }
            }
            $process.WaitForExit()
            [pscustomobject]@{
                ExitCode = $process.ExitCode
                Output   = $stdout.ToString()
                Error    = $stderr.ToString()
            }
        }
        finally {
            $process.Dispose()
        }
    }

    if (
        $null -eq $result -or
        $result.PSObject.Properties.Name -notcontains 'ExitCode' -or
        $result.PSObject.Properties.Name -notcontains 'Output'
    ) {
        throw 'GitHub command runner must return ExitCode and Output.'
    }
    $outputBytes = [System.Text.Encoding]::UTF8.GetByteCount([string]$result.Output)
    $errorBytes = if ($result.PSObject.Properties.Name -contains 'Error') {
        [System.Text.Encoding]::UTF8.GetByteCount([string]$result.Error)
    }
    else {
        0
    }
    if ($outputBytes -gt $script:GhMaxOutputBytes -or $errorBytes -gt $script:GhMaxErrorBytes) {
        throw "GitHub CLI response exceeded the adapter byte limit."
    }
    if ([int]$result.ExitCode -ne 0) {
        $diagnostics = @()
        if ($result.PSObject.Properties.Name -contains 'Error') {
            $diagnostics += ([string]$result.Error).Trim()
        }
        $diagnostics += ([string]$result.Output).Trim()
        $detail = ($diagnostics | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join "`n"
        if ($AllowNotFound -and $detail -match '(?i)(HTTP\s+404\b|status(?:\s+code)?\s*[:=]?\s*404\b)') {
            return $null
        }
        throw $(if ($detail) { "GitHub CLI request failed: $detail" } else { 'GitHub CLI request failed with no diagnostic output.' })
    }
    return [string]$result.Output
}

function ConvertFrom-GitHubWorkHierarchyJson {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string]$Json,

        [Parameter(Mandatory)]
        [string]$Operation
    )

    if ([string]::IsNullOrWhiteSpace($Json)) {
        throw "GitHub CLI returned no JSON for $Operation."
    }
    try {
        return $Json | ConvertFrom-Json
    }
    catch {
        throw "GitHub CLI returned invalid JSON for ${Operation}: $($_.Exception.Message)"
    }
}

function ConvertFrom-GitHubWorkHierarchyIssue {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        $Issue
    )

    if ($Issue.PSObject.Properties.Name -contains 'pull_request') {
        throw 'GitHub CLI returned a pull request where an issue was required.'
    }

    $id = Get-GitHubWorkHierarchyProperty -InputObject $Issue -Name id
    $nodeId = [string](Get-GitHubWorkHierarchyProperty -InputObject $Issue -Name node_id)
    $number = [int](Get-GitHubWorkHierarchyProperty -InputObject $Issue -Name number)
    $title = [string](Get-GitHubWorkHierarchyProperty -InputObject $Issue -Name title)
    $body = Get-GitHubWorkHierarchyProperty -InputObject $Issue -Name body
    $state = ([string](Get-GitHubWorkHierarchyProperty -InputObject $Issue -Name state)).ToLowerInvariant()
    $url = [string](Get-GitHubWorkHierarchyProperty -InputObject $Issue -Name html_url)
    $providerId = 0L
    if (
        -not [long]::TryParse([string]$id, [ref]$providerId) -or
        $providerId -le 0 -or
        $number -le 0 -or
        [string]::IsNullOrWhiteSpace($nodeId) -or
        [string]::IsNullOrWhiteSpace($title) -or
        $state -notin @('open', 'closed') -or
        [string]::IsNullOrWhiteSpace($url)
    ) {
        throw 'GitHub CLI returned an invalid issue shape.'
    }

    return [pscustomobject][ordered]@{
        kind       = 'issue'
        providerId = [string]$providerId
        nodeId     = $nodeId
        number     = $number
        title      = $title
        body       = [string]$body
        state      = $state
        url        = $url
    }
}

function Invoke-GitHubWorkHierarchyRead {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        $Request,

        [scriptblock]$CommandRunner
    )

    $kind = [string](Get-GitHubWorkHierarchyProperty -InputObject $Request -Name kind)
    $repository = [string](Get-GitHubWorkHierarchyProperty -InputObject $Request -Name repository)
    Assert-GitHubWorkHierarchyRepository -Repository $repository
    if ($kind -eq 'managed-issues') {
        $itemKind = [string](Get-GitHubWorkHierarchyProperty -InputObject $Request -Name itemKind)
        $localId = [string](Get-GitHubWorkHierarchyProperty -InputObject $Request -Name localId)
        if ($itemKind -notin @('epic', 'plan') -or $localId -notmatch '^[A-Za-z0-9-]+$') {
            throw 'GitHub managed-issue lookup identity is invalid.'
        }
        $marker = "<!-- skalary:work-hierarchy:${itemKind}:${localId}:start -->"
        $matches = [System.Collections.Generic.List[object]]::new()
        for ($page = 1; $page -le 10; $page++) {
            $path = "repos/$repository/issues?state=all&per_page=100&page=$page&sort=created&direction=desc"
            $json = Invoke-GitHubWorkHierarchyCommand -Arguments @('api', $path) -CommandRunner $CommandRunner
            $pageItems = @(ConvertFrom-GitHubWorkHierarchyJson -Json $json -Operation "managed issue lookup '$localId' page $page")
            foreach ($item in $pageItems) {
                if ($item.PSObject.Properties.Name -contains 'pull_request') {
                    continue
                }
                $issue = ConvertFrom-GitHubWorkHierarchyIssue -Issue $item
                if ($issue.body.Contains($marker, [System.StringComparison]::Ordinal)) {
                    $matches.Add($issue)
                    if ($matches.Count -eq 2) {
                        return $matches.ToArray()
                    }
                }
            }
            if ($pageItems.Count -lt 100) {
                return $matches.ToArray()
            }
        }

        $overflowJson = Invoke-GitHubWorkHierarchyCommand `
            -Arguments @('api', "repos/$repository/issues?state=all&per_page=1&page=1001&sort=created&direction=desc") `
            -CommandRunner $CommandRunner
        $overflow = @(ConvertFrom-GitHubWorkHierarchyJson -Json $overflowJson -Operation "managed issue lookup '$localId' overflow probe")
        if ($overflow.Count -gt 0) {
            throw "GitHub managed-issue lookup for '$localId' exceeded 1000 repository items."
        }
        return $matches.ToArray()
    }

    $number = [int](Get-GitHubWorkHierarchyProperty -InputObject $Request -Name number)
    if ($number -le 0) {
        throw 'GitHub issue number must be positive.'
    }

    $path = switch ($kind) {
        'issue' { "repos/$repository/issues/$number" }
        'sub-issues' { "repos/$repository/issues/$number/sub_issues?per_page=100&page=1" }
        'blocked-by' { "repos/$repository/issues/$number/dependencies/blocked_by?per_page=100&page=1" }
        default { throw "GitHub adapter does not support read kind '$kind'." }
    }

    $json = Invoke-GitHubWorkHierarchyCommand `
        -Arguments @('api', $path) `
        -CommandRunner $CommandRunner `
        -AllowNotFound:($kind -eq 'issue')
    if ($null -eq $json) {
        $repositoryIdText = Invoke-GitHubWorkHierarchyCommand `
            -Arguments @('api', "repos/$repository", '--jq', '.id') `
            -CommandRunner $CommandRunner
        $repositoryId = 0L
        if (-not [long]::TryParse($repositoryIdText.Trim(), [ref]$repositoryId) -or $repositoryId -le 0) {
            throw "GitHub CLI returned an invalid repository identity for '$repository'."
        }
        return $null
    }
    $result = ConvertFrom-GitHubWorkHierarchyJson -Json $json -Operation "$kind read '$repository#$number'"
    if ($kind -eq 'issue') {
        return ConvertFrom-GitHubWorkHierarchyIssue -Issue $result
    }
    $issues = @($result)
    if ($issues.Count -eq 100) {
        $overflowPath = switch ($kind) {
            'sub-issues' { "repos/$repository/issues/$number/sub_issues?per_page=1&page=101" }
            'blocked-by' { "repos/$repository/issues/$number/dependencies/blocked_by?per_page=1&page=101" }
        }
        $overflowJson = Invoke-GitHubWorkHierarchyCommand -Arguments @('api', $overflowPath) -CommandRunner $CommandRunner
        $overflow = @(ConvertFrom-GitHubWorkHierarchyJson -Json $overflowJson -Operation "$kind overflow probe '$repository#$number'")
        if ($overflow.Count -gt 0) {
            throw "GitHub adapter relation read '$kind' returned more than 100 issues."
        }
    }
    if ($issues.Count -gt 100) {
        throw "GitHub adapter relation read '$kind' returned more than 100 issues."
    }
    return @($issues | ForEach-Object { ConvertFrom-GitHubWorkHierarchyIssue -Issue $_ })
}

function Invoke-GitHubWorkHierarchyWrite {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        $Operation,

        [scriptblock]$CommandRunner
    )

    $kind = [string](Get-GitHubWorkHierarchyProperty -InputObject $Operation -Name kind)
    $repository = [string](Get-GitHubWorkHierarchyProperty -InputObject $Operation -Name repository)
    Assert-GitHubWorkHierarchyRepository -Repository $repository

    switch ($kind) {
        'create-issue' {
            $title = [string](Get-GitHubWorkHierarchyProperty -InputObject $Operation -Name title)
            $body = [string](Get-GitHubWorkHierarchyProperty -InputObject $Operation -Name body)
            $arguments = @(
                'api', '--method', 'POST', "repos/$repository/issues",
                '-f', "title=$title", '-f', "body=$body"
            )
        }
        'update-issue' {
            $number = [int](Get-GitHubWorkHierarchyProperty -InputObject $Operation -Name number)
            if ($number -le 0) { throw 'GitHub issue number must be positive.' }
            $title = [string](Get-GitHubWorkHierarchyProperty -InputObject $Operation -Name title)
            $body = [string](Get-GitHubWorkHierarchyProperty -InputObject $Operation -Name body)
            $arguments = @(
                'api', '--method', 'PATCH', "repos/$repository/issues/$number",
                '-f', "title=$title", '-f', "body=$body"
            )
        }
        'link-child' {
            $parentNumber = [int](Get-GitHubWorkHierarchyProperty -InputObject $Operation -Name parentNumber)
            $childProviderId = [long](Get-GitHubWorkHierarchyProperty -InputObject $Operation -Name childProviderId)
            if ($parentNumber -le 0 -or $childProviderId -le 0) {
                throw 'GitHub parent issue number and child provider id must be positive.'
            }
            $arguments = @(
                'api', '--method', 'POST', "repos/$repository/issues/$parentNumber/sub_issues",
                '-F', "sub_issue_id=$childProviderId"
            )
        }
        'link-blocked-by' {
            $blockedNumber = [int](Get-GitHubWorkHierarchyProperty -InputObject $Operation -Name blockedNumber)
            $blockingProviderId = [long](Get-GitHubWorkHierarchyProperty -InputObject $Operation -Name blockingProviderId)
            if ($blockedNumber -le 0 -or $blockingProviderId -le 0) {
                throw 'GitHub blocked issue number and blocking provider id must be positive.'
            }
            $arguments = @(
                'api', '--method', 'POST', "repos/$repository/issues/$blockedNumber/dependencies/blocked_by",
                '-F', "issue_id=$blockingProviderId"
            )
        }
        default {
            throw "GitHub adapter does not support write kind '$kind'."
        }
    }

    $json = Invoke-GitHubWorkHierarchyCommand -Arguments $arguments -CommandRunner $CommandRunner
    $issue = ConvertFrom-GitHubWorkHierarchyJson -Json $json -Operation $kind
    return ConvertFrom-GitHubWorkHierarchyIssue -Issue $issue
}

function New-GitHubWorkHierarchyProvider {
    [CmdletBinding()]
    param(
        [scriptblock]$CommandRunner
    )

    $runner = $CommandRunner
    $read = {
        param($Request)
        GitHubWorkHierarchy\Invoke-GitHubWorkHierarchyRead -Request $Request -CommandRunner $runner
    }.GetNewClosure()
    $write = {
        param($Operation)
        GitHubWorkHierarchy\Invoke-GitHubWorkHierarchyWrite -Operation $Operation -CommandRunner $runner
    }.GetNewClosure()
    return New-WorkHierarchyProvider -Name github -Read $read -Write $write
}

Export-ModuleMember -Function `
    New-GitHubWorkHierarchyProvider, `
    Invoke-GitHubWorkHierarchyRead, Invoke-GitHubWorkHierarchyWrite
