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

    [string]$Branch = "feature/$PlanSlug"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Toolchain versions (bump here to upgrade; cache auto-invalidates) ---
$NodeVersion = '24.16.0'
$DotnetChannel = '10.0'
$GhCliVersion = '2.92.0'

$RepoRoot = git rev-parse --show-toplevel
$ScriptDir = $PSScriptRoot
$SandboxDir = Join-Path $env:TEMP "autopilot-sandbox-$(Get-Date -Format 'yyyyMMdd-HHmmss')"

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

# Node.js — extract once, mount as C:\nodejs
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

# .NET SDK — install once, mount as C:\dotnet
$DotnetDir = Join-Path $CacheDir "dotnet-$DotnetChannel"
if (-not (Test-Path (Join-Path $DotnetDir 'dotnet.exe'))) {
    Write-Host "Preparing cache: .NET SDK ($DotnetChannel)..."
    $installer = Join-Path $env:TEMP 'dotnet-install.ps1'
    curl.exe -sSL -o $installer 'https://dot.net/v1/dotnet-install.ps1'
    & $installer -Channel $DotnetChannel -InstallDir $DotnetDir
    Remove-Item $installer -Force -ErrorAction SilentlyContinue
}

# GitHub CLI — extract once, mount alongside nodejs/dotnet
$GhDir = Join-Path $CacheDir "gh-$GhCliVersion"
if (-not (Test-Path (Join-Path $GhDir 'bin\gh.exe'))) {
    Write-Host "Preparing cache: GitHub CLI $GhCliVersion..."
    $ghZip = Join-Path $env:TEMP 'gh-cache.zip'
    curl.exe -sSL -o $ghZip "https://github.com/cli/cli/releases/download/v$GhCliVersion/gh_${GhCliVersion}_windows_amd64.zip"
    if (Test-Path $GhDir) { Remove-Item $GhDir -Recurse -Force }
    Expand-Archive -Path $ghZip -DestinationPath $GhDir -Force
    Remove-Item $ghZip -Force -ErrorAction SilentlyContinue
}

# Git for Windows — mount host installation directly
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

Log 'Bootstrap started'

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

# 4) Copilot CLI (npm install to writable prefix)
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
    Start-Sleep -Seconds 5
    exit 1
}

Log 'All dependencies installed successfully.'

# --- Read token ---
`$Token = Get-Content (Join-Path `$SessionPath 'token.txt') -Raw
Remove-Item (Join-Path `$SessionPath 'token.txt') -Force -ErrorAction SilentlyContinue

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
`$remoteRef = git ls-remote --heads origin `$BranchName 2>&1
if (`$remoteRef -and `$remoteRef -notmatch 'fatal') {
    Log "Remote branch exists - checking out..."
    git fetch origin `$BranchName 2>&1 | Out-Null
    git checkout `$BranchName
} else {
    Log "Creating new branch..."
    git checkout -b `$BranchName
}
Log "On branch: `$(git branch --show-current)"

# --- Execute phases ---
`$ErrorActionPreference = 'Stop'
`$PlanPath = 'docs/implementation-plans/$PlanSlug/plan.md'
if (-not (Test-Path `$PlanPath)) {
    Log "Plan not found: `$PlanPath"
    Start-Sleep -Seconds 5
    exit 1
}

`$planContent = Get-Content `$PlanPath -Raw
`$phaseMatches = [regex]::Matches(`$planContent, '## Phase (\d+)')
`$totalPhases = `$phaseMatches.Count
Log "Plan has `$totalPhases phases."

for (`$phase = 1; `$phase -le `$totalPhases; `$phase++) {
    Log "=== Phase `$phase of `$totalPhases ==="

    `$currentPlan = Get-Content `$PlanPath -Raw
    `$phasePattern = "## Phase `$phase.*?(?=## Phase `$(`$phase + 1)|## Known Constraints|`$)"
    `$phaseSection = [regex]::Match(`$currentPlan, `$phasePattern, [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not `$phaseSection.Success) { continue }

    `$hasIncomplete = `$phaseSection.Value -match '\- \[ \]|\- \[~\]'
    if (-not `$hasIncomplete) {
        Log "Phase `${phase}: all steps complete - skipping."
        continue
    }

    `$transcriptName = "session-transcript-phase`$phase.md"
    `$prompt = "Execute `$PlanPath, phase `$phase"

    Log "Invoking Copilot CLI for Phase `${phase}..."
    & copilot -p "`$prompt" --agent autopilot --no-ask-user --allow-all --share="./`$transcriptName"
    `$exitCode = `$LASTEXITCODE

    if (`$exitCode -eq 42) {
        Log "@human step encountered in Phase `${phase}. Stopping."
        break
    }
    if (`$exitCode -ne 0) {
        Log "Phase `${phase} exited with code `${exitCode}."
        break
    }

    if ('$Mode' -eq 'next-phase') {
        Log "Mode is 'next-phase' - stopping after Phase `${phase}."
        break
    }
}

# --- Push results and create PR ---
Log 'Pushing results...'
git push origin `$BranchName 2>&1 | ForEach-Object { Log `$_ }

Log 'Creating pull request...'
`$prOut = gh pr create --title "feat: $PlanSlug" --body "Autonomous implementation of plan $PlanSlug" --head `$BranchName 2>&1
if (`$LASTEXITCODE -eq 0) { Log "PR created: `$prOut" } else { Log "PR: `$prOut" }

Get-ChildItem -Filter 'session-transcript-phase*.md' -ErrorAction SilentlyContinue |
    Copy-Item -Destination `$SessionPath -Force

Log '=== Sandbox execution complete ==='
"@

$bootstrapPath = Join-Path $SandboxDir 'bootstrap.ps1'
Set-Content -Path $bootstrapPath -Value $bootstrapContent -Encoding UTF8

# --- Generate .wsb configuration ---
$wsbContent = @"
<Configuration>
  <VGpu>Enable</VGpu>
  <Networking>Enable</Networking>
  <MemoryInMB>8192</MemoryInMB>
  <MappedFolders>
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
      <HostFolder>$GitDir</HostFolder>
      <SandboxFolder>C:\git</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>$CacheDir</HostFolder>
      <SandboxFolder>C:\cache</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
  </MappedFolders>
  <LogonCommand>
    <Command>cmd /c start "" /max powershell -ExecutionPolicy Bypass -NoExit -Command "while (-not (Test-Path 'C:\sandbox-session\bootstrap.ps1')) { Start-Sleep -Seconds 2 }; &amp; 'C:\sandbox-session\bootstrap.ps1'"</Command>
  </LogonCommand>
</Configuration>
"@

$wsbPath = Join-Path $SandboxDir 'autopilot.wsb'
Set-Content -Path $wsbPath -Value $wsbContent -Encoding UTF8

# --- Launch sandbox ---
Write-Host ""
Write-Host "=== Launching Windows Sandbox ==="
Write-Host "Config: $wsbPath"
Write-Host "Repo mapped: $RepoRoot -> C:\repo (read-write)"
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

Start-Process -FilePath 'C:\Windows\System32\WindowsSandbox.exe' -ArgumentList $wsbPath

Write-Host "Sandbox launched. Monitor progress in the sandbox window."
Write-Host "Transcripts will appear in: $SandboxDir"
