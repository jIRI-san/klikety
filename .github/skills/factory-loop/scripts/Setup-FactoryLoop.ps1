#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('preview', 'apply')][string]$Action,
    [Parameter(Mandatory)][string]$RepoRoot,
    [string]$ExpectedDigest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'FactoryLoop.psm1') -Force -DisableNameChecking
$result = Invoke-FactoryLoopSetup -Action $Action -RepoRoot $RepoRoot -ExpectedDigest $ExpectedDigest
$result | ConvertTo-Json -Depth 12
