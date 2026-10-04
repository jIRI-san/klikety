#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RepoRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'FactoryLoop.Runtime.psm1') -Force -DisableNameChecking
Invoke-FactoryLoopTick -RepoRoot $RepoRoot | ConvertTo-Json -Depth 20 -Compress
