#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Name,

    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path,

    [string]$RegistryPath,

    [string]$Source,

    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '_Common.ps1')

function Get-InstalledPluginName {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$RepoRootPath
    )

    $receiptsRoot = Join-Path $RepoRootPath '.github/.skalary/receipts'
    if (-not (Test-Path -LiteralPath $receiptsRoot -PathType Container)) {
        return @()
    }
    Assert-GithubStatePathSafe -RepoRoot $RepoRootPath -Path $receiptsRoot

    $installed = @()
    foreach ($receiptPath in (Get-ChildItem -LiteralPath $receiptsRoot -File -Filter '*.json' | Sort-Object Name)) {
        $receipt = Read-PluginReceipt -RepoRoot $RepoRootPath -PluginName $receiptPath.BaseName
        if (-not [string]::IsNullOrWhiteSpace([string]$receipt.name)) {
            $installed += , ([string]$receipt.name)
        }
    }

    return @($installed | Sort-Object -Unique)
}

function Assert-NoInstalledDependent {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$RepoRootPath,

        [Parameter(Mandatory)]
        [string]$PluginName,

        [string]$RegistryPath
    )

    $registryPath = Resolve-RegistryPath -RepoRoot $RepoRootPath -RegistryPath $RegistryPath
    $registry = Read-JsonFile -Path $registryPath
    $registryByName = @{}
    foreach ($plugin in @($registry.plugins)) {
        $registryByName[[string]$plugin.name] = $plugin
    }

    $dependents = @()
    foreach ($installedPlugin in (Get-InstalledPluginName -RepoRootPath $RepoRootPath)) {
        if ($installedPlugin -eq $PluginName) {
            continue
        }
        if (-not $registryByName.ContainsKey($installedPlugin)) {
            continue
        }

        $dependencies = @($registryByName[$installedPlugin].dependencies | ForEach-Object { [string]$_ })
        if ($dependencies -contains $PluginName) {
            $dependents += , $installedPlugin
        }
    }

    if ($dependents.Count -gt 0) {
        $dependentList = ($dependents | Sort-Object) -join ', '
        throw "Cannot remove plugin '$PluginName': installed dependent plugin(s): $dependentList. Use -Force to override."
    }
}

$repoRootPath = Resolve-RepoRoot -StartPath $RepoRoot
if (-not $Force) {
    Assert-NoInstalledDependent -RepoRootPath $repoRootPath -PluginName $Name -RegistryPath $RegistryPath
}
$result = Invoke-PluginRemovalPrimitive -RepoRoot $repoRootPath -PluginName $Name -Source $Source -Force:$Force
Write-Output "Removed plugin '$Name'. Deleted file count: $($result.RemovedCount). Missing file count: $($result.MissingCount)."
