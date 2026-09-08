<#
.SYNOPSIS
    Generates a secure temporary .env file with tokens for Docker container injection.
.DESCRIPTION
    Creates a per-session random subdirectory under $env:TEMP, writes an .env file with
    restrictive ACL (current user only), and returns the file path.
    Caller MUST delete the file in a finally block after docker run completes.
.PARAMETER Config
    Parsed .autopilot.json object.
.PARAMETER Token
    The GitHub token (PAT or OAuth) for Copilot CLI and git operations.
.PARAMETER AdoToken
    Optional ADO access token (if gitProvider=ado).
.OUTPUTS
    [string] Path to the generated .env file.
#>
param(
    [Parameter(Mandatory)]
    [PSCustomObject]$Config,

    [Parameter(Mandatory)]
    [string]$Token,

    [string]$AdoToken,

    [string]$Branch,

    [string]$ExpectedStartCommit,

    [string]$ExpectedPullRequestBase,

    [switch]$TrustedInternalRetry,

    # When set, inject offline-restore env so the entrypoint restores from the
    # read-only /feed mount instead of the network.
    [switch]$Offline
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'autopilot-dispatch.ps1')

$expectedStartEnvironment = @(
    Get-AutopilotExpectedStartEnvironment `
        -ExpectedStartCommit $ExpectedStartCommit `
        -TrustedInternalRetry:$TrustedInternalRetry
)

# Create per-session random subdirectory
$sessionId = [System.IO.Path]::GetRandomFileName().Replace('.', '')
$sessionDir = Join-Path $env:TEMP "autopilot-$sessionId"
New-Item -ItemType Directory -Path $sessionDir -Force | Out-Null

$envFilePath = Join-Path $sessionDir '.env'

# Set restrictive ACL BEFORE writing sensitive data
# Remove inheritance and grant access only to current user
$acl = New-Object System.Security.AccessControl.FileSecurity
$acl.SetAccessRuleProtection($true, $false) # Disable inheritance, remove inherited rules
$currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    $currentUser,
    'FullControl',
    'Allow'
)
$acl.AddAccessRule($rule)

# Create file and apply ACL
New-Item -ItemType File -Path $envFilePath -Force | Out-Null
Set-Acl -Path $envFilePath -AclObject $acl

# Write env vars - never log token values
$envContent = @(
    "COPILOT_GITHUB_TOKEN=$Token"
    "GH_TOKEN=$Token"
    "COPILOT_ALLOW_ALL=true"
    "AUTOPILOT_CONTAINER=true"
    "COPILOT_MODEL=$($Config.model)"
    "COPILOT_CONTEXT=$($Config.context)"
    "COPILOT_REASONING_EFFORT=$($Config.reasoningEffort)"
    "GIT_USER_NAME=$($Config.git.name)"
    "GIT_USER_EMAIL=$($Config.git.email)"
)

if ($AdoToken) {
    $envContent += "ADO_TOKEN=$AdoToken"
    if ($Config.adoOrg) { $envContent += "ADO_ORG=$($Config.adoOrg)" }
    if ($Config.adoProject) { $envContent += "ADO_PROJECT=$($Config.adoProject)" }
}

# Get repo remote URL for container clone (convert SSH to HTTPS for token auth)
$remote = git remote get-url origin 2>$null
if ($remote) {
    if ($remote -match '^git@github\.com:(.+)$') {
        $remote = "https://github.com/$($Matches[1])"
    }
    Assert-AutopilotRepositoryRemote -Remote $remote
    $envContent += "REPO_REMOTE=$remote"
}

# Pass target branch if specified
if ($Branch) {
    $envContent += "REPO_BRANCH=$Branch"
}
if ($expectedStartEnvironment.Count -gt 0) {
    $envContent += $expectedStartEnvironment
}
if ($ExpectedPullRequestBase) {
    $envContent += "AUTOPILOT_EXPECTED_PR_BASE=$ExpectedPullRequestBase"
}

# Offline restore: point the entrypoint at the read-only feed mount.
if ($Offline) {
    $envContent += "AUTOPILOT_OFFLINE=true"
    $envContent += "AUTOPILOT_FEED=/feed"
}

$serializedEnvironment = ConvertTo-AutopilotEnvFileContent -Entry $envContent
Set-Content -Path $envFilePath -Value $serializedEnvironment -NoNewline -Encoding UTF8

Write-Host "Env file created: $envFilePath (ACL restricted to $currentUser)"
return $envFilePath
