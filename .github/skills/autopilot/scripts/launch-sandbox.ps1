<#
.SYNOPSIS
    Sandbox-mode orchestrator for autonomous plan execution.
.DESCRIPTION
    Launches Windows Sandbox with mapped repo folder, installs toolchain,
    and runs the autopilot. Provides isolation while retaining full Win32/WPF support.
    Results are written back to the mapped repo folder so they survive sandbox teardown.
.PARAMETER PlanSlug
    The plan folder name (e.g. '021-keyboard-layout-refresh').
.PARAMETER Mode
    Execution scope: 'whole-plan' or 'next-phase'.
.PARAMETER Config
    Parsed .autopilot.json object.
.PARAMETER Token
    GitHub token for Copilot CLI.
.PARAMETER Branch
    Target branch name.
.PARAMETER StartBranch
    Validated branch from which a new target branch is created.
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

    [string]$Branch = "feature/$PlanSlug",

    [string]$StartBranch = (git branch --show-current),

    # When set, map this host package-feed read-only at C:\feed so the sandbox
    # bootstrap restores fully offline (see prepare-packages.ps1).
    [string]$FeedPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Toolchain versions (bump here to upgrade; cache auto-invalidates) ---
$NodeVersion = '24.16.0'
$DotnetChannel = '10.0'
$GhCliVersion = '2.92.0'
$PowerShellVersion = '7.5.3'

$RepoRoot = git rev-parse --show-toplevel
$SandboxDir = Join-Path $env:TEMP "autopilot-sandbox-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
$PlanFolder = Join-Path $RepoRoot "docs/implementation-plans/$PlanSlug"

# --- Verify Windows Sandbox is available ---
if (-not (Test-Path 'C:\Windows\System32\WindowsSandbox.exe')) {
    throw @"
Windows Sandbox not available. Enable it (requires Windows Pro/Enterprise):
  Enable-WindowsOptionalFeature -Online -FeatureName 'Containers-DisposableClientVM' -All
Then restart.
"@
}

# --- Create sandbox session directory ---
New-Item -ItemType Directory -Path $SandboxDir -Force | Out-Null

# --- Toolchain cache (persists between runs, mounted directly into sandbox) ---
# Each tool uses a version-specific subfolder. Bumping version = auto re-download.
# Run clean-sandbox-cache.ps1 to remove old versions.
$CacheDir = Join-Path $env:LOCALAPPDATA 'autopilot-sandbox-cache'
New-Item -ItemType Directory -Path $CacheDir -Force | Out-Null

# Node.js -- extract once, mount as C:\nodejs
$NodeDir = Join-Path $CacheDir "nodejs-$NodeVersion"
if (-not (Test-Path (Join-Path $NodeDir 'node.exe'))) {
    Write-Host "Preparing cache: Node.js $NodeVersion..."
    $nodeZip = Join-Path $env:TEMP 'node-cache.zip'
    curl.exe -sSL -o $nodeZip "https://nodejs.org/dist/v$NodeVersion/node-v$NodeVersion-win-x64.zip"
    $extractDir = Join-Path $env:TEMP 'node-extract'
    Expand-Archive -Path $nodeZip -DestinationPath $extractDir -Force
    if (Test-Path $NodeDir) { Remove-Item $NodeDir -Recurse -Force }
    Move-Item (Join-Path $extractDir "node-v$NodeVersion-win-x64") $NodeDir
    Remove-Item $nodeZip, $extractDir -Recurse -Force -ErrorAction SilentlyContinue
}

# .NET SDK -- install once, mount as C:\dotnet
$DotnetDir = Join-Path $CacheDir "dotnet-$DotnetChannel"
if (-not (Test-Path (Join-Path $DotnetDir 'dotnet.exe'))) {
    Write-Host "Preparing cache: .NET SDK ($DotnetChannel)..."
    $installer = Join-Path $env:TEMP 'dotnet-install.ps1'
    curl.exe -sSL -o $installer 'https://dot.net/v1/dotnet-install.ps1'
    & $installer -Channel $DotnetChannel -InstallDir $DotnetDir
    Remove-Item $installer -Force -ErrorAction SilentlyContinue
}

# GitHub CLI -- extract once, mount alongside nodejs/dotnet
$GhDir = Join-Path $CacheDir "gh-$GhCliVersion"
if (-not (Test-Path (Join-Path $GhDir 'bin\gh.exe'))) {
    Write-Host "Preparing cache: GitHub CLI $GhCliVersion..."
    $ghZip = Join-Path $env:TEMP 'gh-cache.zip'
    curl.exe -sSL -o $ghZip "https://github.com/cli/cli/releases/download/v$GhCliVersion/gh_${GhCliVersion}_windows_amd64.zip"
    if (Test-Path $GhDir) { Remove-Item $GhDir -Recurse -Force }
    Expand-Archive -Path $ghZip -DestinationPath $GhDir -Force
    Remove-Item $ghZip -Force -ErrorAction SilentlyContinue
}

# PowerShell 7 is required by the canonical plan and receipt validators.
$PwshDir = Join-Path $CacheDir "powershell-$PowerShellVersion"
if (-not (Test-Path (Join-Path $PwshDir 'pwsh.exe'))) {
    Write-Host "Preparing cache: PowerShell $PowerShellVersion..."
    $pwshZip = Join-Path $env:TEMP 'powershell-cache.zip'
    curl.exe -sSL -o $pwshZip "https://github.com/PowerShell/PowerShell/releases/download/v$PowerShellVersion/PowerShell-$PowerShellVersion-win-x64.zip"
    if (Test-Path $PwshDir) { Remove-Item $PwshDir -Recurse -Force }
    Expand-Archive -Path $pwshZip -DestinationPath $PwshDir -Force
    Remove-Item $pwshZip -Force -ErrorAction SilentlyContinue
}

# Git for Windows -- mount host installation directly
$GitDir = Split-Path (Split-Path (Get-Command git).Source)
if (-not (Test-Path (Join-Path $GitDir 'cmd\git.exe'))) {
    throw "Cannot find Git installation at $GitDir"
}

Write-Host "Toolchain cache ready: $CacheDir"

# --- Write token to a file the sandbox can read (deleted after launch) ---
$tokenFile = Join-Path $SandboxDir 'token.txt'
Set-Content -Path $tokenFile -Value $Token -NoNewline -Encoding UTF8

# Restrict ACL to current user only
$acl = Get-Acl $tokenFile
$acl.SetAccessRuleProtection($true, $false)
$currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule($currentUser, 'FullControl', 'Allow')
$acl.AddAccessRule($rule)
Set-Acl -Path $tokenFile -AclObject $acl

# --- Generate the bootstrap script that runs inside the sandbox ---
$bootstrapContent = @"
# Autopilot sandbox bootstrap - runs inside Windows Sandbox
`$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

`$SessionPath = 'C:\sandbox-session'
for (`$i = 0; `$i -lt 30; `$i++) {
    if (Test-Path `$SessionPath) { break }
    Start-Sleep -Seconds 2
}
if (-not (Test-Path `$SessionPath)) { exit 1 }

`$RepoPath = 'C:\repo'
`$LogFile = Join-Path `$SessionPath 'sandbox-log.txt'

function Log(`$msg) {
    `$ts = Get-Date -Format 'HH:mm:ss'
    `$line = "[`$ts] `$msg"
    Write-Host `$line
    Add-Content -Path `$LogFile -Value `$line -Force
}

function Complete-Bootstrap([int]`$Code) {
    try {
        Set-Content -Path (Join-Path `$SessionPath '.autopilot-exit-code') `
            -Value `$Code -NoNewline -Encoding ASCII
    } finally {
        New-Item -ItemType File -Path (Join-Path `$SessionPath '.bootstrap-complete') -Force | Out-Null
    }
}

Log 'Bootstrap started'
`$runExitCode = 0

# --- Set up toolchain (pre-mounted from host cache) ---
`$failed = @()

# 0) Git (mounted at C:\git)
`$env:PATH = "C:\git\cmd;C:\git\usr\bin;`$env:PATH"

# 1) .NET SDK (mounted at C:\dotnet)
try {
    Log 'SETUP: .NET SDK...'
    `$env:PATH = "C:\dotnet;`$env:PATH"
    `$env:DOTNET_ROOT = 'C:\dotnet'
    `$env:DOTNET_CLI_HOME = "`$env:TEMP\.dotnet"
    `$v = & dotnet --version 2>&1
    Log "OK: .NET SDK `$v"
} catch {
    Log "FAIL: .NET SDK - `$_"
    `$failed += '.NET SDK'
}

# 2) Node.js (mounted at C:\nodejs)
try {
    Log 'SETUP: Node.js...'
    `$env:PATH = "C:\nodejs;`$env:PATH"
    # npm global installs go to writable location (for Copilot CLI)
    `$npmPrefix = 'C:\npm-global'
    New-Item -ItemType Directory -Path `$npmPrefix -Force | Out-Null
    `$env:NPM_CONFIG_PREFIX = `$npmPrefix
    `$env:PATH = "`$npmPrefix;`$env:PATH"
    `$v = & node --version 2>&1
    Log "OK: Node.js `$v"
} catch {
    Log "FAIL: Node.js - `$_"
    `$failed += 'Node.js'
}

# 3) GitHub CLI (mounted at C:\gh)
try {
    Log 'SETUP: GitHub CLI...'
    `$env:PATH = "C:\gh\bin;`$env:PATH"
    `$v = & gh --version 2>&1 | Select-Object -First 1
    Log "OK: GitHub CLI `$v"
} catch {
    Log "FAIL: GitHub CLI - `$_"
    `$failed += 'GitHub CLI'
}

# 4) PowerShell 7 (mounted at C:\pwsh)
try {
    Log 'SETUP: PowerShell 7...'
    `$env:PATH = "C:\pwsh;`$env:PATH"
    `$v = & pwsh --version 2>&1
    Log "OK: PowerShell `$v"
} catch {
    Log "FAIL: PowerShell 7 - `$_"
    `$failed += 'PowerShell 7'
}

# 5) Copilot CLI (npm install to writable prefix)
try {
    Log 'INSTALL: Copilot CLI...'
    `$npmOut = & npm install -g @github/copilot 2>&1
    if (`$LASTEXITCODE -ne 0) { throw "npm exit `$LASTEXITCODE : `$npmOut" }
    `$v = & copilot --version 2>&1
    Log "OK: Copilot CLI `$v"
} catch {
    Log "FAIL: Copilot CLI - `$_"
    `$failed += 'Copilot CLI'
}

# --- Summary ---
if (`$failed.Count -gt 0) {
    Log "INSTALL FAILURES: `$(`$failed -join ', ')"
    Log 'Stopping - fix failures before proceeding.'
    Complete-Bootstrap -Code 1
    Start-Sleep -Seconds 5
    shutdown /s /t 0; exit 1
}

Log 'All dependencies installed successfully.'

# --- Read token ---
`$Token = Get-Content (Join-Path `$SessionPath 'token.txt') -Raw
Remove-Item (Join-Path `$SessionPath 'token.txt') -Force -ErrorAction SilentlyContinue

try {

# --- Set environment ---
`$env:COPILOT_GITHUB_TOKEN = `$Token
`$env:GH_TOKEN = `$Token
`$env:COPILOT_ALLOW_ALL = 'true'
`$env:COPILOT_MODEL = '$($Config.model)'

# --- Configure git and gh auth ---
git config --global --add safe.directory '*'
git config --global user.name '$($Config.git.name)'
git config --global user.email '$($Config.git.email)'
gh auth setup-git
Log "gh auth configured"

# --- Clone repo (local mount as source for speed, then set real remote) ---
`$RepoRemote = '$(git remote get-url origin)' -replace 'git@github\.com:', 'https://github.com/' -replace '\.git$', ''
`$RepoRemote = `$RepoRemote + '.git'
Log "Cloning from local mount..."
git clone C:\repo C:\work 2>&1 | ForEach-Object { Log `$_ }
Set-Location C:\work
git remote set-url origin `$RepoRemote
Log "Remote: `$RepoRemote"

# --- Checkout/create feature branch ---
`$BranchName = '$Branch'
`$StartBranchName = '$StartBranch'
`$remoteRef = git ls-remote --heads origin `$BranchName 2>&1
if (`$remoteRef -and `$remoteRef -notmatch 'fatal') {
    Log "Remote branch exists - checking out..."
    git fetch origin `$BranchName 2>&1 | Out-Null
    git checkout `$BranchName
} else {
    Log "Creating new branch..."
    git fetch origin `$StartBranchName 2>&1 | ForEach-Object { Log `$_ }
    if (`$LASTEXITCODE -ne 0) {
        throw "Unable to fetch start branch '`$StartBranchName'."
    }
    git checkout -b `$BranchName "origin/`$StartBranchName"
}
Log "On branch: `$(git branch --show-current)"

# --- Offline package feed setup ---
# A read-only C:\feed mount means the host bundled a package feed; copy it to a
# writable cache and emit OUT-OF-TREE restore config so restores resolve only
# from the cache with no network access and without writing into C:\work.
`$AutopilotOffline = Test-Path 'C:\feed'
if (`$AutopilotOffline) {
    Log 'Offline mode: copying C:\feed to writable cache...'
    `$CacheRoot = Join-Path `$env:TEMP 'autopilot-cache'
    New-Item -ItemType Directory -Path `$CacheRoot -Force | Out-Null
    Copy-Item -Path 'C:\feed\*' -Destination `$CacheRoot -Recurse -Force

    `$nugetCache = Join-Path `$CacheRoot 'nuget'
    if (Test-Path `$nugetCache) {
        `$nugetConfig = Join-Path `$CacheRoot 'nuget.config'
        `$nugetXml = @(
            '<?xml version="1.0" encoding="utf-8"?>'
            '<configuration>'
            '  <packageSources>'
            '    <clear />'
            '  </packageSources>'
            '  <fallbackPackageFolders>'
            '    <clear />'
            '  </fallbackPackageFolders>'
            '  <config>'
            ('    <add key="globalPackagesFolder" value="' + `$nugetCache + '" />')
            '  </config>'
            '</configuration>'
        )
        Set-Content -Path `$nugetConfig -Value `$nugetXml -Encoding UTF8
        `$env:NUGET_CONFIG = `$nugetConfig
        Log "NuGet offline config: `$nugetConfig (globalPackagesFolder=`$nugetCache)"
    }

    `$npmCache = Join-Path `$CacheRoot 'npm'
    if (Test-Path `$npmCache) {
        `$env:npm_config_cache = `$npmCache
        `$env:npm_config_offline = 'true'
        Log "npm offline config: cache=`$npmCache offline=true"
    }
}

# --- Execute phases ---
`$ErrorActionPreference = 'Stop'
`$PlanPath = 'docs/implementation-plans/$PlanSlug/plan.md'
if (-not (Test-Path `$PlanPath)) {
    Log "Plan not found: `$PlanPath"
    Complete-Bootstrap -Code 1
    Start-Sleep -Seconds 5
    shutdown /s /t 0; exit 1
}

`$planContent = Get-Content `$PlanPath -Raw
`$phaseMatches = [regex]::Matches(`$planContent, '## Phase (\d+)')
`$phaseNumbers = @(`$phaseMatches | ForEach-Object { [int]`$_.Groups[1].Value })
`$totalPhases = `$phaseNumbers.Count
`$phaseList = `$phaseNumbers -join ', '
Log "Plan has `$totalPhases phases (numbers: `$phaseList)."

`$rebundleRequested = `$false
`$phaseStateScript = 'C:\autopilot-runtime\Get-PhaseExecutionState.ps1'
foreach (`$phase in `$phaseNumbers) {
    Log "=== Phase `$phase of `$totalPhases ==="

    `$phaseStateOutput = & pwsh -NoProfile -File `$phaseStateScript -PlanPath `$PlanPath `
        -Phase `$phase -RepoRoot . 2>&1
    if (`$LASTEXITCODE -ne 0) {
        Log "Phase `${phase}: state check failed: `$(`$phaseStateOutput -join ' ')"
        `$runExitCode = 3
        break
    }
    `$phaseState = (`$phaseStateOutput -join '').Trim()
    if (`$phaseState -eq 'closed') {
        Log "Phase `${phase}: checklist and phase close complete - skipping."
        continue
    }
    if (`$phaseState -notin @('execution-required', 'close-pending')) {
        Log "Phase `${phase}: invalid state result '`$phaseState'. Stopping."
        `$runExitCode = 3
        break
    }

    `$transcriptName = "session-transcript-phase`$phase.md"
    `$usageName = Join-Path `$SessionPath "session-usage-phase-`$phase.json"
    `$prompt = "Execute `$PlanPath, phase `$phase only. Do not run plan finalization; the launcher has a separate completion target."

    Log "Invoking Copilot CLI for Phase `${phase}..."
    & copilot -p "`$prompt" --model '$($Config.model)' --context '$($Config.context)' --effort '$($Config.reasoningEffort)' --agent autopilot --no-ask-user --allow-all --usage-output-file="`$usageName" --share="./`$transcriptName"
    `$exitCode = `$LASTEXITCODE

    if (`$exitCode -eq 42) {
        Log "@human step encountered in Phase `${phase}. Stopping."
        `$runExitCode = 42
        break
    }
    if (`$exitCode -eq 43) {
        Log "Offline rebundle requested in Phase `${phase} - pushing manifest and signaling host."
        `$pushOutput = @(git push origin `$BranchName 2>&1)
        `$pushExitCode = `$LASTEXITCODE
        `$pushOutput | ForEach-Object { Log `$_ }
        if (`$pushExitCode -ne 0) {
            throw "Rebundle publication failed with exit code `$pushExitCode."
        }
        New-Item -ItemType File -Path (Join-Path `$SessionPath '.autopilot-rebundle-needed') -Force | Out-Null
        `$rebundleRequested = `$true
        `$runExitCode = 43
        break
    }
    if (`$exitCode -ne 0) {
        Log "Phase `${phase} exited with code `${exitCode}."
        `$runExitCode = `$exitCode
        break
    }

    `$closeStateOutput = & pwsh -NoProfile -File `$phaseStateScript -PlanPath `$PlanPath `
        -Phase `$phase -RepoRoot . 2>&1
    if (`$LASTEXITCODE -ne 0) {
        Log "Phase `${phase}: close state check failed: `$(`$closeStateOutput -join ' ')"
        `$runExitCode = 3
        break
    }
    `$closeState = (`$closeStateOutput -join '').Trim()
    if (`$closeState -ne 'closed') {
        Log "Phase `${phase} exited zero without a valid phase close (`$closeState). Stopping."
        `$runExitCode = 1
        break
    }

    if ('$Mode' -eq 'next-phase') {
        Log "Mode is 'next-phase' - stopping after Phase `${phase}."
        break
    }
}

if (`$runExitCode -eq 0 -and '$Mode' -eq 'whole-plan') {
    foreach (`$phase in `$phaseNumbers) {
        `$closeStateOutput = & pwsh -NoProfile -File `$phaseStateScript -PlanPath `$PlanPath `
            -Phase `$phase -RepoRoot . 2>&1
        if (`$LASTEXITCODE -ne 0 -or (`$closeStateOutput -join '').Trim() -ne 'closed') {
            Log "Plan finalization refused because Phase `${phase} is not closed."
            `$runExitCode = 1
            break
        }
    }
    if (`$runExitCode -eq 0) {
        `$prompt = "Finalize completed plan `$PlanPath. Do not execute checklist phases. Run the explicit completion target, and do not duplicate an unchanged terminal review."
        Log 'Invoking Copilot CLI for plan finalization...'
        `$usageName = Join-Path `$SessionPath 'session-usage-finalization.json'
        & copilot -p "`$prompt" --model '$($Config.model)' --context '$($Config.context)' --effort '$($Config.reasoningEffort)' --agent autopilot --no-ask-user --allow-all --usage-output-file="`$usageName" --share='./session-transcript-finalization.md'
        `$runExitCode = `$LASTEXITCODE
        if (`$runExitCode -eq 42) {
            Log '@human step encountered during plan finalization. Stopping.'
        } elseif (`$runExitCode -eq 43) {
            Log 'Offline rebundle requested during plan finalization - pushing manifest and signaling host.'
            `$pushOutput = @(git push origin `$BranchName 2>&1)
            `$pushExitCode = `$LASTEXITCODE
            `$pushOutput | ForEach-Object { Log `$_ }
            if (`$pushExitCode -ne 0) {
                throw "Rebundle publication failed with exit code `$pushExitCode."
            }
            New-Item -ItemType File -Path (Join-Path `$SessionPath '.autopilot-rebundle-needed') -Force | Out-Null
            `$rebundleRequested = `$true
        } elseif (`$runExitCode -ne 0) {
            Log "Plan finalization exited with code `$runExitCode."
        }
    }
}

# --- Push results ---
if (`$rebundleRequested) {
    Log '=== Offline rebundle requested - manifest pushed, deferring PR to the post-rebundle run. ==='
} else {
    Log 'Pushing results...'
    `$pushOutput = @(git push origin `$BranchName 2>&1)
    `$pushExitCode = `$LASTEXITCODE
    `$pushOutput | ForEach-Object { Log `$_ }
    if (`$pushExitCode -ne 0) {
        throw "Final publication failed with exit code `$pushExitCode."
    }

    Log '=== Sandbox execution complete ==='
}

} catch {
    Log "FATAL: `$_"
    `$runExitCode = 1
} finally {
    # Copy transcripts and any useful debug output to session dir (survives sandbox teardown)
    Get-ChildItem -Path C:\work -Filter 'session-transcript-*.md' -ErrorAction SilentlyContinue |
        Copy-Item -Destination `$SessionPath -Force -ErrorAction SilentlyContinue
    # Copy copilot CLI logs if they exist
    if (Test-Path "`$env:TEMP\.copilot") {
        Copy-Item -Path "`$env:TEMP\.copilot" -Destination (Join-Path `$SessionPath 'copilot-logs') -Recurse -Force -ErrorAction SilentlyContinue
    }
    Complete-Bootstrap -Code `$runExitCode
    Start-Sleep -Seconds 3
    shutdown /s /t 0
}
"@

$bootstrapPath = Join-Path $SandboxDir 'bootstrap.ps1'
Set-Content -Path $bootstrapPath -Value $bootstrapContent -Encoding UTF8

# --- Generate .wsb configuration ---
# Read-only feed mount (offline mode); empty string when no feed was bundled.
$feedMapping = ''
if ($FeedPath) {
    $feedMapping = @"

    <MappedFolder>
      <HostFolder>$FeedPath</HostFolder>
      <SandboxFolder>C:\feed</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
"@
}
$wsbContent = @"
<Configuration>
  <VGpu>Enable</VGpu>
  <Networking>Enable</Networking>
  <MemoryInMB>8192</MemoryInMB>
  <MappedFolders>
    <MappedFolder>
      <HostFolder>$PSScriptRoot</HostFolder>
      <SandboxFolder>C:\autopilot-runtime</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>$RepoRoot</HostFolder>
      <SandboxFolder>C:\repo</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>$SandboxDir</HostFolder>
      <SandboxFolder>C:\sandbox-session</SandboxFolder>
      <ReadOnly>false</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>$NodeDir</HostFolder>
      <SandboxFolder>C:\nodejs</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>$DotnetDir</HostFolder>
      <SandboxFolder>C:\dotnet</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>$GhDir</HostFolder>
      <SandboxFolder>C:\gh</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>$PwshDir</HostFolder>
      <SandboxFolder>C:\pwsh</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>$GitDir</HostFolder>
      <SandboxFolder>C:\git</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>$CacheDir</HostFolder>
      <SandboxFolder>C:\cache</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>$feedMapping
  </MappedFolders>
  <LogonCommand>
    <Command>cmd /c start "" /max powershell -ExecutionPolicy Bypass -NoExit -Command "while (-not (Test-Path 'C:\sandbox-session\bootstrap.ps1')) { Start-Sleep -Seconds 2 }; &amp; 'C:\sandbox-session\bootstrap.ps1'"</Command>
  </LogonCommand>
</Configuration>
"@

$wsbPath = Join-Path $SandboxDir 'autopilot.wsb'
Set-Content -Path $wsbPath -Value $wsbContent -Encoding UTF8

# --- Clear any stale completion sentinel / rebundle marker before launch ---
$SentinelPath = Join-Path $SandboxDir '.bootstrap-complete'
$RebundleMarker = Join-Path $SandboxDir '.autopilot-rebundle-needed'
$ExitCodeMarker = Join-Path $SandboxDir '.autopilot-exit-code'
Remove-Item -Path $SentinelPath, $RebundleMarker, $ExitCodeMarker -Force -ErrorAction SilentlyContinue

# --- Launch sandbox ---
Write-Host ""
Write-Host "=== Launching Windows Sandbox ==="
Write-Host "Config: $wsbPath"
Write-Host "Repo mapped: $RepoRoot -> C:\repo (read-only)"
Write-Host "Session: $SandboxDir -> C:\sandbox-session"
Write-Host ""
Write-Host "NOTE: Sandbox is interactive. It will:"
Write-Host "  1. Install .NET SDK, Node.js, GitHub CLI, Copilot CLI"
Write-Host "  2. Execute plan phases"
Write-Host "  3. Push results to remote"
Write-Host "  4. Write transcripts to $SandboxDir"
Write-Host ""
Write-Host "Close the sandbox window when done (or it will auto-exit after completion)."
Write-Host ""

$sandboxProcess = Start-Process -FilePath 'C:\Windows\System32\WindowsSandbox.exe' `
    -ArgumentList $wsbPath -PassThru

Write-Host "Sandbox launched. Monitor progress in the sandbox window."

# --- Block until the bootstrap signals completion or the sandbox exits ---
Write-Host "Waiting for sandbox bootstrap to complete..."
while (-not (Test-Path $SentinelPath)) {
    if ($sandboxProcess.HasExited) {
        Write-Warning "Sandbox exited without signaling completion."
        break
    }
    Start-Sleep -Seconds 5
}

$exitCode = if (-not (Test-Path -LiteralPath $SentinelPath -PathType Leaf)) {
    1
}
elseif (Test-Path -LiteralPath $ExitCodeMarker -PathType Leaf) {
    $rawExitCode = (Get-Content -LiteralPath $ExitCodeMarker -Raw).Trim()
    if ($rawExitCode -notmatch '^(?:0|[1-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])$') {
        Write-Warning "Sandbox returned invalid exit marker '$rawExitCode'."
        1
    }
    else {
        [int]$rawExitCode
    }
}
else {
    Write-Warning 'Sandbox completed without a valid exit marker.'
    1
}
if (Test-Path $RebundleMarker) {
    Write-Host "Offline rebundle requested by sandbox (marker present) - signaling host launcher."
    $exitCode = 43
}

try {
    foreach ($usageFile in @(Get-ChildItem -LiteralPath $SandboxDir -Filter 'session-usage-*.json')) {
        $target = $usageFile.BaseName.Substring('session-usage-'.Length)
        $ledger = & (Join-Path $PSScriptRoot 'Record-AiCreditUsage.ps1') `
            -PlanFolder $PlanFolder `
            -UsagePath $usageFile.FullName `
            -Target $target `
            -Runtime sandbox `
            -ModelAlias $Config.modelAlias `
            -ContextTier $Config.context
        Remove-Item -LiteralPath $usageFile.FullName -Force
        Write-Host "AI credits recorded: $($ledger.totalAiCredits) plan total."
    }
}
catch {
    if ($exitCode -eq 0) {
        throw
    }
    Write-Warning "AI-credit recording failed after sandbox exit ${exitCode}: $_"
}

Write-Host ""
Write-Host "Session output: $SandboxDir"
Write-Host "  Log:         $SandboxDir\sandbox-log.txt"
Write-Host "  Transcripts: $SandboxDir\session-transcript-phase*.md"

exit $exitCode
