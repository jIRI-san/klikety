#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('create', 'merge')][string]$Action,
    [Parameter(Mandatory)][string]$DemoRoot,
    [string]$ConsumerRoot,
    [string]$Branch,
    [string]$ExpectedSourceSha,
    [switch]$ConfirmMerge
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'FactoryLoop.psm1') -Force -DisableNameChecking
$result = if ($Action -eq 'create') {
    New-FactoryLoopDemoProject -DemoRoot $DemoRoot -ConsumerRoot $ConsumerRoot
}
else {
    Merge-FactoryLoopDemoPullRequest -DemoRoot $DemoRoot -Branch $Branch `
        -ExpectedSourceSha $ExpectedSourceSha -ConfirmMerge:$ConfirmMerge
}
$result | ConvertTo-Json -Depth 8
