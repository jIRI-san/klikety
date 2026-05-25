<#
.SYNOPSIS
    Remove the sandbox toolchain cache. Next sandbox run will re-download everything.
.DESCRIPTION
    Deletes %LOCALAPPDATA%\autopilot-sandbox-cache which contains:
    - Pre-extracted Node.js
    - Pre-installed .NET SDK
    - GitHub CLI MSI

    Use this to:
    - Free disk space (~700MB)
    - Force upgrade to latest tool versions (edit launch-sandbox.ps1 URLs first)
    - Fix corrupted cache
.PARAMETER WhatIf
    Show what would be deleted without deleting.
#>
[CmdletBinding(SupportsShouldProcess)]
param()

$CacheDir = Join-Path $env:LOCALAPPDATA 'autopilot-sandbox-cache'

if (-not (Test-Path $CacheDir)) {
    Write-Host "No cache found at: $CacheDir"
    return
}

$size = (Get-ChildItem $CacheDir -Recurse -Force | Measure-Object -Property Length -Sum).Sum
$sizeMB = [math]::Round($size / 1MB, 1)

Write-Host "Cache: $CacheDir ($sizeMB MB)"
Get-ChildItem $CacheDir | ForEach-Object {
    $itemSize = if ($_.PSIsContainer) {
        [math]::Round((Get-ChildItem $_.FullName -Recurse -Force | Measure-Object -Property Length -Sum).Sum / 1MB, 1)
    } else {
        [math]::Round($_.Length / 1MB, 1)
    }
    Write-Host "  $($_.Name) ($itemSize MB)"
}

if ($PSCmdlet.ShouldProcess($CacheDir, "Remove sandbox toolchain cache")) {
    Remove-Item $CacheDir -Recurse -Force
    Write-Host "Cache deleted. Next sandbox run will rebuild it."
}
