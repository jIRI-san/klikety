#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RepoRoot,
    [Parameter(Mandatory)][string]$ChainId,
    [Parameter(Mandatory)][string]$WorkItemId,
    [Parameter(Mandatory)][string]$PlanReference,
    [Parameter(Mandatory)][string]$Branch,
    [Parameter(Mandatory)][string]$SourceSha
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'FactoryLoop.Runtime.psm1') -Force -DisableNameChecking
Initialize-FactoryLoopChain -RepoRoot $RepoRoot -ChainId $ChainId -WorkItemId $WorkItemId `
    -PlanReference $PlanReference -Branch $Branch -SourceSha $SourceSha |
    ConvertTo-Json -Depth 20
