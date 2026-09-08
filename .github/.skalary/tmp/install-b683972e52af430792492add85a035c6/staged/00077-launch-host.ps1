<#
.SYNOPSIS
    Host-mode orchestrator for autonomous plan execution.
.DESCRIPTION
    Creates a git worktree on a feature branch and invokes Copilot CLI once per phase
    with live output streaming. The host call is a synchronous operator boundary.
.PARAMETER PlanSlug
    The plan folder name (e.g. '002-persistent-storage-for-job-data').
.PARAMETER Mode
    Execution scope: 'whole-plan' or 'next-phase'.
.PARAMETER Config
    Parsed .autopilot.json object.
.PARAMETER Token
    GitHub token for Copilot CLI.
#>
param(
    [Parameter(Mandatory)]
    [string]$PlanSlug,

    [Parameter(Mandatory)]
    [ValidateSet('whole-plan', 'next-phase')]
    [string]$Mode,

    [Parameter(Mandatory)]
    [PSCustomObject]$Config,

    [Parameter(Mandatory)]
    [string]$Token,

    [string]$Branch,

    [string]$StartBranch = (git branch --show-current)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'host-command.ps1')

$BranchName = if ($Branch) { $Branch } else { "feature/$PlanSlug" }
$RepoRoot = git rev-parse --show-toplevel
$WorktreeRoot = Join-Path (Split-Path $RepoRoot -Parent) "$((Split-Path $RepoRoot -Leaf)).worktrees"
$WorktreePath = Join-Path $WorktreeRoot $BranchName.Replace('/', '-')
$PlanPath = "docs/implementation-plans/$PlanSlug/plan.md"

# --- Worktree setup ---
if (-not (Test-Path $WorktreeRoot)) {
    New-Item -ItemType Directory -Path $WorktreeRoot -Force | Out-Null
}

$remoteBranch = "refs/remotes/origin/$BranchName"
& git fetch origin $BranchName 2>$null
$remoteBranchExists = $LASTEXITCODE -eq 0 -and
    $null -ne (& git show-ref --verify --hash $remoteBranch 2>$null)

if (Test-Path $WorktreePath) {
    Write-Host "Worktree already exists at $WorktreePath - resuming."
}
else {
    Write-Host "Creating worktree: $WorktreePath (branch: $BranchName)"
    # Check if branch exists
    $branchExists = git branch --list $BranchName
    if ($branchExists) {
        git worktree add $WorktreePath $BranchName
    }
    elseif ($remoteBranchExists) {
        git worktree add $WorktreePath -b $BranchName $remoteBranch
    }
    else {
        git worktree add $WorktreePath -b $BranchName $StartBranch
    }
}

# Validate plan exists in worktree
$fullPlanPath = Join-Path $WorktreePath $PlanPath
if (-not (Test-Path $fullPlanPath)) {
    throw "Plan not found at: $fullPlanPath"
}

# Configure git identity in worktree
Push-Location $WorktreePath
try {
    git config user.name $Config.git.name
    git config user.email $Config.git.email
}
finally {
    Pop-Location
}

# --- Phase detection ---
# Parse the actual phase numbers from the headings so plans that start at
# Phase 0 (or skip numbers) are executed faithfully. A blind 1..count loop
# would skip Phase 0 and chase a nonexistent trailing phase.
$planContent = Get-Content $fullPlanPath -Raw
$phaseMatches = [regex]::Matches($planContent, '## Phase (\d+)')
$phaseNumbers = @($phaseMatches | ForEach-Object { [int]$_.Groups[1].Value })
$totalPhases = $phaseNumbers.Count
$phaseList = $phaseNumbers -join ', '
Write-Host "Plan has $totalPhases phases (numbers: $phaseList)."

# --- Per-phase execution loop ---
function ConvertTo-CmdQuotedToken {
    param(
        [Parameter(Mandatory)]
        [string]$Token
    )

    '"' + ($Token -replace '"', '""') + '"'
}

function Get-CanonicalPhaseState {
    param(
        [Parameter(Mandatory)][string]$StateScript,
        [Parameter(Mandatory)][string]$Plan,
        [Parameter(Mandatory)][int]$PhaseNumber,
        [Parameter(Mandatory)][string]$Root
    )

    $output = & pwsh -NoProfile -File $StateScript -PlanPath $Plan -Phase $PhaseNumber `
        -RepoRoot $Root 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Phase $PhaseNumber state check failed: $(($output -join ' ').Trim())"
    }
    $state = (($output | ForEach-Object { $_.ToString() }) -join '').Trim()
    if ($state -notin @('execution-required', 'close-pending', 'closed')) {
        throw "Phase $PhaseNumber state checker returned invalid result '$state'."
    }
    return $state
}

function ConvertTo-PowerShellQuotedToken {
    param(
        [Parameter(Mandatory)]
        [string]$Token
    )

    '"' + ($Token -replace '"', '""') + '"'
}

function Invoke-CopilotPhase {
    param(
        [int]$PhaseNumber,
        [string]$CopilotToken,
        [string]$Cwd,
        [string]$PlanRelPath,
        [string]$CopilotPath,
        [ValidateSet('exe', 'bat', 'cmd', 'ps1')]
        [string]$CopilotType,
        [string[]]$ExtraArgs,
        [string]$Model,
        [string]$ContextTier,
        [string]$ReasoningEffort,
        [string]$ModelAlias,
        [string]$LedgerPlanFolder,

        [switch]$Finalization
    )

    $transcriptName = if ($Finalization) {
        'session-transcript-finalization.md'
    } else {
        "session-transcript-phase$PhaseNumber.md"
    }
    $usageName = if ($Finalization) {
        'session-usage-finalization.json'
    } else {
        "session-usage-phase-$PhaseNumber.json"
    }
    $usagePath = Join-Path ([System.IO.Path]::GetTempPath()) (
        "autopilot-$([guid]::NewGuid().ToString('N'))-$usageName"
    )
    $prompt = if ($Finalization) {
        "Finalize completed plan $PlanRelPath. Do not execute checklist phases. Run the explicit completion target, and do not duplicate an unchanged terminal review."
    } else {
        "Execute $PlanRelPath, phase $PhaseNumber only. Do not run plan finalization; the launcher has a separate completion target."
    }

    Write-Host ""
    $displayTarget = if ($Finalization) { 'plan finalization' } else { "Phase $PhaseNumber" }
    Write-Host "=== Invoking Copilot CLI for $displayTarget ==="

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    if ($CopilotType -eq 'bat' -or $CopilotType -eq 'cmd') {
        $cmdTokens = @(
            '/c',
            (ConvertTo-CmdQuotedToken -Token $CopilotPath),
            '-p',
            (ConvertTo-CmdQuotedToken -Token $prompt),
            '--agent',
            'autopilot',
            '--no-ask-user',
            '--allow-all',
            '--context',
            (ConvertTo-CmdQuotedToken -Token $ContextTier),
            '--effort',
            (ConvertTo-CmdQuotedToken -Token $ReasoningEffort),
            (ConvertTo-CmdQuotedToken -Token "--share=./$transcriptName"),
            '--usage-output-file',
            (ConvertTo-CmdQuotedToken -Token $usagePath)
        )
        foreach ($arg in $ExtraArgs) {
            $cmdTokens += ConvertTo-CmdQuotedToken -Token $arg
        }

        $psi.FileName = 'cmd.exe'
        # cmd.exe /c strips only the outermost quote pair before parsing. Wrap the
        # whole command in an extra pair so a quoted executable path plus quoted
        # args survive intact (without this, a quoted copilot.cmd path is corrupted).
        $innerCmd = ($cmdTokens | Select-Object -Skip 1) -join ' '
        $psi.Arguments = '/c "' + $innerCmd + '"'
    }
    elseif ($CopilotType -eq 'ps1') {
        $pwshTokens = @(
            '-ExecutionPolicy',
            'Bypass',
            '-File',
            (ConvertTo-PowerShellQuotedToken -Token $CopilotPath),
            '-p',
            (ConvertTo-PowerShellQuotedToken -Token $prompt),
            '--agent',
            'autopilot',
            '--no-ask-user',
            '--allow-all',
            '--context',
            (ConvertTo-PowerShellQuotedToken -Token $ContextTier),
            '--effort',
            (ConvertTo-PowerShellQuotedToken -Token $ReasoningEffort),
            (ConvertTo-PowerShellQuotedToken -Token "--share=./$transcriptName"),
            '--usage-output-file',
            (ConvertTo-PowerShellQuotedToken -Token $usagePath)
        )
        foreach ($arg in $ExtraArgs) {
            $pwshTokens += ConvertTo-PowerShellQuotedToken -Token $arg
        }

        $psi.FileName = 'powershell.exe'
        $psi.Arguments = $pwshTokens -join ' '
    }
    else {
        $psi.FileName = $CopilotPath
        $psi.ArgumentList.Add('-p')
        $psi.ArgumentList.Add($prompt)
        $psi.ArgumentList.Add('--agent')
        $psi.ArgumentList.Add('autopilot')
        $psi.ArgumentList.Add('--no-ask-user')
        $psi.ArgumentList.Add('--allow-all')
        $psi.ArgumentList.Add('--context')
        $psi.ArgumentList.Add($ContextTier)
        $psi.ArgumentList.Add('--effort')
        $psi.ArgumentList.Add($ReasoningEffort)
        $psi.ArgumentList.Add("--share=./$transcriptName")
        $psi.ArgumentList.Add('--usage-output-file')
        $psi.ArgumentList.Add($usagePath)
        foreach ($arg in $ExtraArgs) {
            $psi.ArgumentList.Add($arg)
        }
    }
    $psi.WorkingDirectory = $Cwd
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $psi.EnvironmentVariables['COPILOT_GITHUB_TOKEN'] = $CopilotToken
    $psi.EnvironmentVariables['GH_TOKEN'] = $CopilotToken
    $psi.EnvironmentVariables['COPILOT_ALLOW_ALL'] = 'true'
    $psi.EnvironmentVariables['COPILOT_MODEL'] = $Model

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $psi
    $process.EnableRaisingEvents = $true

    # Live output streaming via events
    $outputHandler = {
        if (-not [string]::IsNullOrEmpty($EventArgs.Data)) {
            Write-Host $EventArgs.Data
        }
    }
    $errorHandler = {
        if (-not [string]::IsNullOrEmpty($EventArgs.Data)) {
            Write-Host "ERR: $($EventArgs.Data)" -ForegroundColor Yellow
        }
    }

    Register-ObjectEvent -InputObject $process -EventName OutputDataReceived -Action $outputHandler | Out-Null
    Register-ObjectEvent -InputObject $process -EventName ErrorDataReceived -Action $errorHandler | Out-Null

    $process.Start() | Out-Null
    $process.BeginOutputReadLine()
    $process.BeginErrorReadLine()

    $process.WaitForExit()
    Get-EventSubscriber | Where-Object SourceObject -eq $process | Unregister-Event

    $copilotExitCode = $process.ExitCode
    $target = if ($Finalization) { 'finalization' } else { "phase-$PhaseNumber" }
    try {
        $ledger = & (Join-Path $PSScriptRoot 'Record-AiCreditUsage.ps1') `
            -PlanFolder $LedgerPlanFolder `
            -UsagePath $usagePath `
            -Target $target `
            -Runtime host `
            -ModelAlias $ModelAlias `
            -ContextTier $ContextTier
        Remove-Item -LiteralPath $usagePath -Force
        Write-Host "AI credits recorded: $($ledger.totalAiCredits) plan total."
    }
    catch {
        if ($copilotExitCode -eq 0) {
            throw
        }
        Write-Warning "AI-credit recording failed after Copilot exit ${copilotExitCode}: $_"
        if (Test-Path -LiteralPath $usagePath -PathType Leaf) {
            Write-Warning "Usage sidecar retained at: $usagePath"
        }
    }

    return @{ ExitCode = $copilotExitCode }
}

# --- Main execution ---
$hostCommand = Resolve-HostCommand
Write-Host "Using Copilot launcher: $($hostCommand.Path) [$($hostCommand.Type)]"
$phaseStateScript = Join-Path $PSScriptRoot 'Get-PhaseExecutionState.ps1'
$ledgerPlanFolder = Join-Path $RepoRoot "docs/implementation-plans/$PlanSlug"

$phasesExecuted = 0
$executionExitCode = 0
foreach ($phase in $phaseNumbers) {
    try {
        $phaseState = Get-CanonicalPhaseState -StateScript $phaseStateScript `
            -Plan $fullPlanPath -PhaseNumber $phase -Root $WorktreePath
    }
    catch {
        Write-Warning $_
        $executionExitCode = 3
        break
    }
    if ($phaseState -eq 'closed') {
        Write-Host "Phase ${phase}: checklist and phase close complete - skipping."
        continue
    }

    Write-Host "Phase ${phase}: $phaseState."
    $result = Invoke-CopilotPhase `
        -PhaseNumber $phase `
        -CopilotToken $Token `
        -Cwd $WorktreePath `
        -PlanRelPath $PlanPath `
        -CopilotPath $hostCommand.Path `
        -CopilotType $hostCommand.Type `
        -ExtraArgs $hostCommand.ExtraArgs `
        -Model $Config.model `
        -ContextTier $Config.context `
        -ReasoningEffort $Config.reasoningEffort `
        -ModelAlias $Config.modelAlias `
        -LedgerPlanFolder $ledgerPlanFolder

    $phasesExecuted++

    if ($result.ExitCode -eq 42) {
        Write-Host "@human step encountered in Phase $phase. Stopping."
        $executionExitCode = 42
        break
    }
    if ($result.ExitCode -ne 0) {
        Write-Warning "Phase $phase exited with code $($result.ExitCode). Stopping."
        $executionExitCode = $result.ExitCode
        break
    }

    try {
        $closeState = Get-CanonicalPhaseState -StateScript $phaseStateScript `
            -Plan $fullPlanPath -PhaseNumber $phase -Root $WorktreePath
    }
    catch {
        Write-Warning $_
        $executionExitCode = 3
        break
    }
    if ($closeState -ne 'closed') {
        Write-Warning "Phase $phase exited zero without a valid phase close ($closeState). Stopping."
        $executionExitCode = 1
        break
    }

    if ($Mode -eq 'next-phase') {
        Write-Host "Mode is 'next-phase' - stopping after Phase ${phase}."
        break
    }
}

if ($executionExitCode -eq 0 -and $Mode -eq 'whole-plan') {
    foreach ($phase in $phaseNumbers) {
        try {
            $closeState = Get-CanonicalPhaseState -StateScript $phaseStateScript `
                -Plan $fullPlanPath -PhaseNumber $phase -Root $WorktreePath
        }
        catch {
            Write-Warning $_
            $executionExitCode = 3
            break
        }
        if ($closeState -ne 'closed') {
            Write-Warning "Plan finalization refused because Phase $phase is '$closeState'."
            $executionExitCode = 1
            break
        }
    }
    if ($executionExitCode -eq 0) {
        $result = Invoke-CopilotPhase -PhaseNumber 0 -Finalization `
            -CopilotToken $Token -Cwd $WorktreePath -PlanRelPath $PlanPath `
            -CopilotPath $hostCommand.Path -CopilotType $hostCommand.Type `
            -ExtraArgs $hostCommand.ExtraArgs -Model $Config.model `
            -ContextTier $Config.context -ReasoningEffort $Config.reasoningEffort `
            -ModelAlias $Config.modelAlias -LedgerPlanFolder $ledgerPlanFolder
        if ($result.ExitCode -eq 42) {
            Write-Host '@human step encountered during plan finalization. Stopping.'
            $executionExitCode = 42
        }
        elseif ($result.ExitCode -ne 0) {
            Write-Warning "Plan finalization exited with code $($result.ExitCode). Stopping."
            $executionExitCode = $result.ExitCode
        }
    }
}

# --- Copy transcripts ---
$transcriptsDir = Join-Path $RepoRoot "docs/implementation-plans/$PlanSlug/transcripts"
if (-not (Test-Path $transcriptsDir)) {
    New-Item -ItemType Directory -Path $transcriptsDir -Force | Out-Null
}

Get-ChildItem -Path $WorktreePath -Filter 'session-transcript-*.md' -ErrorAction SilentlyContinue |
    ForEach-Object { Copy-Item $_.FullName $transcriptsDir -Force }

Write-Host ""
Write-Host "=== Host-mode execution complete ==="
Write-Host "Phases executed: $phasesExecuted"
Write-Host "Worktree: $WorktreePath"
Write-Host "Transcripts: $transcriptsDir"
exit $executionExitCode
