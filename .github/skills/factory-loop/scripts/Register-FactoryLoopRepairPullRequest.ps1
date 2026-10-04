[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RepoRoot,
    [Parameter(Mandatory)][string]$IncidentId,
    [Parameter(Mandatory)][string]$BuildLineageId,
    [Parameter(Mandatory)][string]$Branch,
    [Parameter(Mandatory)][string]$SourceSha
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'FactoryLoop.Runtime.psm1') -Force -DisableNameChecking
Register-FactoryLoopRepairPullRequest @PSBoundParameters | ConvertTo-Json -Depth 20 -Compress
