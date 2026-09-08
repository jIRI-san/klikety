#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path,

    [string]$RegistryPath,

    [switch]$Installed
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '_Common.ps1')

function Get-ReceiptMap {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$RepoRootPath
    )

    $map = @{}
    $receiptsRoot = Join-Path $RepoRootPath '.github/.skalary/receipts'
    if (-not (Test-Path -LiteralPath $receiptsRoot -PathType Container)) {
        return $map
    }
    Assert-GithubStatePathSafe -RepoRoot $RepoRootPath -Path $receiptsRoot

    foreach ($receiptPath in (Get-ChildItem -LiteralPath $receiptsRoot -File -Filter '*.json' | Sort-Object Name)) {
        $receipt = Read-PluginReceipt -RepoRoot $RepoRootPath -PluginName $receiptPath.BaseName
        $name = [string]$receipt.name
        if (-not [string]::IsNullOrWhiteSpace($name)) {
            $map[$name] = $receipt
        }
    }

    return $map
}

$repoRootPath = Resolve-RepoRoot -StartPath $RepoRoot
$registryPath = Resolve-RegistryPath -RepoRoot $repoRootPath -RegistryPath $RegistryPath

$registry = Read-JsonFile -Path $registryPath
$receiptByName = Get-ReceiptMap -RepoRootPath $repoRootPath

$results = @()
foreach ($plugin in @($registry.plugins | Sort-Object name)) {
    $name = [string]$plugin.name
    $receipt = if ($receiptByName.ContainsKey($name)) { $receiptByName[$name] } else { $null }
    $isInstalled = $null -ne $receipt

    if ($Installed -and -not $isInstalled) {
        continue
    }

    $isOutdated = if ($isInstalled) { [string]$receipt.version -ne [string]$plugin.version } else { $false }
    $status = if ($plugin.PSObject.Properties.Name -contains 'status' -and -not [string]::IsNullOrWhiteSpace([string]$plugin.status)) { [string]$plugin.status } else { 'stable' }

    $results += [pscustomobject]@{
        name = $name
        version = [string]$plugin.version
        installed = $isInstalled
        outdated = $isOutdated
        status = $status
        description = [string]$plugin.description
    }
}

$results | Sort-Object name
