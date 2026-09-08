#requires -Version 7.0
<#
.SYNOPSIS
Scaffolds the architecture-notes index without overwriting an existing file.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][string]$TargetRoot,
    [string]$AssetRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $TargetRoot -PathType Container)) {
    throw "TargetRoot not found or not a directory: $TargetRoot"
}
if (-not $AssetRoot) {
    foreach ($candidate in @(
            (Join-Path $PSScriptRoot '..' 'skills' 'architecture-notes' 'assets'),
            (Join-Path $PSScriptRoot '..' 'assets'))) {
        if (Test-Path -LiteralPath (Join-Path $candidate 'templates') -PathType Container) {
            $AssetRoot = (Resolve-Path -LiteralPath $candidate).Path
            break
        }
    }
}
if (-not $AssetRoot) {
    throw "Could not locate architecture-notes assets relative to $PSScriptRoot; pass -AssetRoot explicitly."
}

$source = Join-Path $AssetRoot 'templates/architecture-notes-index.template.md'
if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
    throw "Architecture-notes index template not found: $source"
}
$destination = Join-Path $TargetRoot 'docs/architecture-notes/.architecture-notes.md'
if (Test-Path -LiteralPath $destination -PathType Leaf) {
    return , @([pscustomobject]@{ Path = $destination; Action = 'skipped' })
}

$action = 'whatif'
if ($PSCmdlet.ShouldProcess($destination, "Scaffold from '$source'")) {
    [void][System.IO.Directory]::CreateDirectory((Split-Path -Parent $destination))
    Copy-Item -LiteralPath $source -Destination $destination
    $action = 'created'
}
return , @([pscustomobject]@{ Path = $destination; Action = $action })
