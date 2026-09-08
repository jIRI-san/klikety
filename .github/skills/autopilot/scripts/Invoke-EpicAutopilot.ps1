#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [string]$Epic,

    [string]$Target = 'HEAD',

    [string]$RepoRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'EpicAutopilot.psm1') -Force

$parameters = @{
    Epic = $Epic
    Target = $Target
}
if ($RepoRoot) {
    $parameters.RepoRoot = $RepoRoot
}
$result = Invoke-EpicAutopilotHostLoop @parameters
if ($null -eq $result) {
    throw 'Epic autopilot returned no structured result.'
}
$isBlocked = $result.PSObject.Properties.Name -contains 'Blocked' -and
[bool]$result.Blocked
$isCompleted = $result.PSObject.Properties.Name -contains 'Completed' -and
[bool]$result.Completed
$hasExitCode = $result.PSObject.Properties.Name -contains 'ExitCode' -and
$null -ne $result.ExitCode
$exitCode = $null
if ($hasExitCode) {
    $exitText = [string]$result.ExitCode
    if ($exitText -cnotmatch '^(?:0|[1-9][0-9]?|1[0-9]{2}|2[0-4][0-9]|25[0-5])$') {
        throw "Epic autopilot returned invalid exit code '$exitText'."
    }
    $exitCode = [int]::Parse(
        $exitText,
        [System.Globalization.NumberStyles]::None,
        [System.Globalization.CultureInfo]::InvariantCulture
    )
}
if ($result.State) {
    Write-Output ($result.State | ConvertTo-Json -Compress)
    if ($isBlocked) {
        if (-not $hasExitCode -or $exitCode -ne 42) {
            throw 'Blocked epic autopilot result must carry exit code 42.'
        }
        Write-Error $result.Message -ErrorAction Continue
        exit 42
    }
    if ([string]$result.State.outcome -ceq 'invocation-failed') {
        if (-not $hasExitCode -or $exitCode -ne 1 -or -not [bool]$result.Failed) {
            throw 'Launcher invocation failure must carry exit code 1 and Failed=true.'
        }
        Write-Error $result.Message -ErrorAction Continue
        exit $exitCode
    }
    if ($result.State.outcome -ceq 'awaiting-merge') {
        if (-not $hasExitCode -or $exitCode -ne 0 -or [bool]$result.Failed) {
            throw 'Awaiting-merge epic autopilot result must carry exit code 0 and Failed=false.'
        }
        Write-Output "Child '$($result.State.currentChild)' passed launcher close proof and is awaiting operator merge."
        exit 0
    }
    if ($result.State.outcome.StartsWith('exit:', [System.StringComparison]::Ordinal)) {
        $outcomeText = $result.State.outcome.Substring(5)
        if ($outcomeText -cnotmatch '^(?:0|[1-9][0-9]?|1[0-9]{2}|2[0-4][0-9]|25[0-5])$' -or
            -not $hasExitCode -or $exitCode -ne [int]$outcomeText -or
            ([bool]$result.Failed) -ne ($exitCode -ne 0)) {
            throw 'Terminal epic autopilot result has inconsistent outcome, exit code, or Failed flag.'
        }
        if ($exitCode -eq 0) {
            Write-Output "Child '$($result.State.currentChild)' has legacy exit:0 close proof and is awaiting operator merge."
        }
        elseif (-not [string]::IsNullOrWhiteSpace([string]$result.Message)) {
            Write-Error $result.Message -ErrorAction Continue
        }
        exit $exitCode
    }
    throw "Epic autopilot returned unsupported state outcome '$($result.State.outcome)'."
}

if ($isCompleted) {
    if (-not $hasExitCode -or $exitCode -ne 0) {
        throw 'Completed epic autopilot result must carry exit code 0.'
    }
    $archiveScript = Join-Path $PSScriptRoot 'Archive-Epic.ps1'
    if (-not (Test-Path -LiteralPath $archiveScript -PathType Leaf)) {
        throw "Epic '$Epic' completed, but Archive-Epic.ps1 was not found at '$archiveScript'."
    }
    $archiveParameters = @{ Epic = $Epic }
    if ($RepoRoot) {
        $archiveParameters.RepoRoot = $RepoRoot
    }
    $archive = & $archiveScript @archiveParameters
    if ($null -eq $archive -or
        $archive.PSObject.Properties.Name -notcontains 'Status' -or
        [string]$archive.Status -cnotin @('archived', 'already-archived')) {
        throw "Epic '$Epic' completed, but archival returned an invalid result."
    }
    Write-Output "Epic '$Epic' is complete and archived at '$($archive.Path)'."
    exit 0
}
if ($isBlocked) {
    if (-not $hasExitCode -or $exitCode -ne 42) {
        throw 'Blocked epic autopilot result must carry exit code 42.'
    }
    Write-Error "Epic '$Epic' is incomplete with no eligible NextChild; no child was launched." `
        -ErrorAction Continue
    exit 42
}

throw 'Epic autopilot returned neither state, completion, nor a blocked stop.'
