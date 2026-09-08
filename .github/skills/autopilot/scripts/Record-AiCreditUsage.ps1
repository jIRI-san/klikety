<#
.SYNOPSIS
    Records one Copilot CLI execution in a plan-local AI-credit ledger.
.DESCRIPTION
    Normalizes the JSON written by Copilot CLI --usage-output-file into
    assets/ai-credits.json. Re-importing the same execution replaces its entry.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PlanFolder,

    [Parameter(Mandatory)]
    [string]$UsagePath,

    [Parameter(Mandatory)]
    [ValidatePattern('^[a-z0-9]+(?:-[a-z0-9]+)*$')]
    [string]$Target,

    [Parameter(Mandatory)]
    [ValidateSet('host', 'container', 'sandbox')]
    [string]$Runtime,

    [string]$ModelAlias,

    [Parameter(Mandatory)]
    [ValidateSet('default', 'long_context')]
    [string]$ContextTier
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$planFolderPath = [System.IO.Path]::GetFullPath($PlanFolder)
$usageFilePath = [System.IO.Path]::GetFullPath($UsagePath)
$planPath = Join-Path $planFolderPath 'plan.md'
if (-not (Test-Path -LiteralPath $planPath -PathType Leaf)) {
    throw "Plan not found: $planPath"
}
if (-not (Test-Path -LiteralPath $usageFilePath -PathType Leaf)) {
    throw "Copilot usage output not found: $usageFilePath"
}

$planText = [System.IO.File]::ReadAllText($planPath)
$planIdMatch = [regex]::Match($planText, '<!--\s*plan-id\s*:\s*(?<id>[0-9a-fA-F]{6})\s*-->')
if (-not $planIdMatch.Success) {
    throw "Plan has no valid plan-id marker: $planPath"
}
$epicIdMatch = [regex]::Match($planText, '<!--\s*epic\s*:\s*(?<id>[0-9a-fA-F]{6})\s*-->')

$usage = [System.IO.File]::ReadAllText($usageFilePath) | ConvertFrom-Json -Depth 100
$usageProperties = @($usage.PSObject.Properties.Name)
foreach ($required in @('totalNanoAiu', 'tokenDetails', 'sessionStartTime', 'modelMetrics')) {
    if ($required -cnotin $usageProperties) {
        throw "Copilot usage output is missing '$required': $usageFilePath"
    }
}

$totalNanoAiu = [int64]$usage.totalNanoAiu
if ($totalNanoAiu -lt 0) {
    throw "Copilot usage output has a negative totalNanoAiu: $usageFilePath"
}
$sessionStartTime = $usage.sessionStartTime
$startedAt = if ($sessionStartTime -is [datetime]) {
    $sessionStartTime.ToUniversalTime().ToString('o')
}
elseif ($sessionStartTime -is [datetimeoffset]) {
    $sessionStartTime.ToUniversalTime().ToString('o')
}
else {
    ([datetimeoffset]::Parse(
            [string]$sessionStartTime,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::RoundtripKind
        )).ToUniversalTime().ToString('o')
}
$recordKey = "$($planIdMatch.Groups['id'].Value.ToLowerInvariant())|$Target|$Runtime|$startedAt"
$recordIdBytes = [System.Security.Cryptography.SHA256]::HashData(
    [System.Text.Encoding]::UTF8.GetBytes($recordKey)
)
$recordId = [System.Convert]::ToHexString($recordIdBytes).ToLowerInvariant()

function Get-UsageTokenCount {
    param(
        [Parameter(Mandatory)][object]$TokenDetails,
        [Parameter(Mandatory)][string]$Name
    )

    $property = $TokenDetails.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) {
        return [int64]0
    }
    $countProperty = $property.Value.PSObject.Properties['tokenCount']
    if ($null -eq $countProperty) {
        return [int64]0
    }
    return [int64]$countProperty.Value
}

$models = @(
    foreach ($property in @($usage.modelMetrics.PSObject.Properties | Sort-Object Name)) {
        $metric = $property.Value
        $modelNanoAiu = [int64]$metric.totalNanoAiu
        [ordered]@{
            model = $property.Name
            totalNanoAiu = $modelNanoAiu
            aiCredits = [math]::Round($modelNanoAiu / 1000000000.0, 6)
        }
    }
)

$record = [ordered]@{
    id = $recordId
    target = $Target
    runtime = $Runtime
    startedAt = $startedAt
    modelAlias = if ([string]::IsNullOrWhiteSpace($ModelAlias)) { $null } else { $ModelAlias }
    context = $ContextTier
    totalNanoAiu = $totalNanoAiu
    aiCredits = [math]::Round($totalNanoAiu / 1000000000.0, 6)
    tokens = [ordered]@{
        input = Get-UsageTokenCount -TokenDetails $usage.tokenDetails -Name 'input'
        cacheRead = Get-UsageTokenCount -TokenDetails $usage.tokenDetails -Name 'cache_read'
        cacheWrite = Get-UsageTokenCount -TokenDetails $usage.tokenDetails -Name 'cache_write'
        output = Get-UsageTokenCount -TokenDetails $usage.tokenDetails -Name 'output'
    }
    models = $models
}

$assetsFolder = Join-Path $planFolderPath 'assets'
if (-not (Test-Path -LiteralPath $assetsFolder -PathType Container)) {
    [void](New-Item -ItemType Directory -Path $assetsFolder -Force)
}
$ledgerPath = Join-Path $assetsFolder 'ai-credits.json'
$ledgerPathHashBytes = [System.Security.Cryptography.SHA256]::HashData(
    [System.Text.Encoding]::UTF8.GetBytes($ledgerPath.ToLowerInvariant())
)
$ledgerLockPath = Join-Path ([System.IO.Path]::GetTempPath()) (
    "skalary-ai-credits-$([System.Convert]::ToHexString($ledgerPathHashBytes).ToLowerInvariant()).lock"
)
$ledgerLock = $null
$lockDeadline = [datetime]::UtcNow.AddSeconds(10)
while ($null -eq $ledgerLock) {
    try {
        $ledgerLock = [System.IO.File]::Open(
            $ledgerLockPath,
            [System.IO.FileMode]::OpenOrCreate,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None
        )
    }
    catch [System.IO.IOException] {
        if ([datetime]::UtcNow -ge $lockDeadline) {
            throw "Timed out waiting for the AI-credit ledger lock: $ledgerPath"
        }
        Start-Sleep -Milliseconds 100
    }
}

try {
    $executions = @()
    if (Test-Path -LiteralPath $ledgerPath -PathType Leaf) {
        $existing = [System.IO.File]::ReadAllText($ledgerPath) | ConvertFrom-Json -Depth 100
        if ([string]$existing.schema -cne 'skalary-ai-credits/v1') {
            throw "Unsupported AI-credit ledger schema in $ledgerPath"
        }
        $executions = @($existing.executions | Where-Object { [string]$_.id -cne $recordId })
    }
    $executions += [pscustomobject]$record
    $executions = @($executions | Sort-Object startedAt, target, id)
    $ledgerTotalNanoAiu = [int64](($executions | Measure-Object -Property totalNanoAiu -Sum).Sum)

    $ledger = [ordered]@{
        schema = 'skalary-ai-credits/v1'
        planId = $planIdMatch.Groups['id'].Value.ToLowerInvariant()
        epicId = if ($epicIdMatch.Success) { $epicIdMatch.Groups['id'].Value.ToLowerInvariant() } else { $null }
        totalNanoAiu = $ledgerTotalNanoAiu
        totalAiCredits = [math]::Round($ledgerTotalNanoAiu / 1000000000.0, 6)
        executions = $executions
    }

    $tempPath = "$ledgerPath.tmp-$PID-$([guid]::NewGuid().ToString('N'))"
    try {
        $json = ($ledger | ConvertTo-Json -Depth 20) + "`n"
        [System.IO.File]::WriteAllText(
            $tempPath,
            $json,
            [System.Text.UTF8Encoding]::new($false)
        )
        Move-Item -LiteralPath $tempPath -Destination $ledgerPath -Force
    }
    finally {
        Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue
    }
}
finally {
    $ledgerLock.Dispose()
}

[pscustomobject]$ledger
