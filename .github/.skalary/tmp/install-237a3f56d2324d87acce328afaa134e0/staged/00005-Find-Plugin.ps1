#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Query,

    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path,

    [string]$RegistryPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '_Common.ps1')

$repoRootPath = Resolve-RepoRoot -StartPath $RepoRoot
$registryPath = Resolve-RegistryPath -RepoRoot $repoRootPath -RegistryPath $RegistryPath

$needle = $Query.Trim()
if ([string]::IsNullOrWhiteSpace($needle)) {
    throw 'Query must not be empty.'
}
$needle = $needle.ToLowerInvariant()

$registry = Read-JsonFile -Path $registryPath
$retiredMatch = @(
    @(if ($registry.PSObject.Properties.Name -contains 'retiredPlugins') { $registry.retiredPlugins }) |
        Where-Object { [string]$_.name -eq $needle }
)
if ($retiredMatch.Count -gt 0) {
    $record = $retiredMatch[0]
    [pscustomobject]@{
        name = [string]$record.name
        version = $null
        status = 'retired'
        description = [string]$record.reason
        tags = @('retired')
        dependencies = @()
        retired = $true
        retiredAt = $record.retiredAt
    }
    return
}

$searchResults = @()
foreach ($plugin in @($registry.plugins)) {
    $name = [string]$plugin.name
    $description = [string]$plugin.description
    $tags = @($plugin.tags | ForEach-Object { [string]$_ })
    $status = if ($plugin.PSObject.Properties.Name -contains 'status' -and -not [string]::IsNullOrWhiteSpace([string]$plugin.status)) { [string]$plugin.status } else { 'stable' }

    $isMatch = $name.ToLowerInvariant().Contains($needle) -or $description.ToLowerInvariant().Contains($needle) -or (@($tags | Where-Object { $_.ToLowerInvariant().Contains($needle) }).Count -gt 0)
    if (-not $isMatch) {
        continue
    }

    $searchResults += [pscustomobject]@{
        name = $name
        version = [string]$plugin.version
        status = $status
        description = $description
        tags = $tags
        dependencies = @($plugin.dependencies | ForEach-Object { [string]$_ })
    }
}

$searchResults | Sort-Object name
