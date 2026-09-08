#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RepoRoot = (Get-Location).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = [System.IO.Path]::GetFullPath($RepoRoot)
$configPath = Join-Path $root '.autopilot.json'
if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
    throw '.autopilot.json is required before authentication validation.'
}
$config = [System.IO.File]::ReadAllText($configPath) | ConvertFrom-Json
$autopilotScripts = Join-Path $root '.github/skills/autopilot/scripts'
if (-not (Test-Path -LiteralPath $autopilotScripts -PathType Container)) {
    throw 'Installed autopilot authentication scripts are unavailable. Install the autopilot plugin first.'
}
$credentialReader = Join-Path $autopilotScripts 'get-credential.ps1'
$validator = Join-Path $autopilotScripts 'validate-auth.ps1'
foreach ($path in @($credentialReader, $validator)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Installed autopilot auth dependency is missing: $path"
    }
}

$target = if ($config.copilotAuth -eq 'oauth') { 'copilot-cli' } else { 'copilot-autopilot' }
$token = $null
try {
    $token = & $credentialReader -Target $target
    $probe = & $validator -Config $config -Token $token 6>$null 4>$null 3>$null
    [ordered]@{
        Available = $true
        CredentialTarget = $target
        GitProvider = $config.gitProvider
        Capabilities = @('credential-read', 'authentication-validation')
        Result = if ($probe.Valid) { 'Authentication validation passed.' } else { 'Authentication validation did not report readiness.' }
    } | ConvertTo-Json -Depth 5
}
catch {
    $message = $_.Exception.Message
    if ($token) { $message = $message.Replace([string]$token, '***REDACTED***') }
    throw "Authentication validation failed for credential target '$target'. $message"
}
finally {
    $token = $null
}
