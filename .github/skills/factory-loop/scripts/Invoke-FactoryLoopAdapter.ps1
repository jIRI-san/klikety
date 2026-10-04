#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet(
        'work-item', 'pull-request', 'deployment', 'version', 'acceptance',
        'telemetry', 'evidence', 'approval'
    )][string]$Domain,
    [Parameter(Mandatory)][ValidateSet(
        'read', 'find', 'create', 'update', 'open', 'checks', 'merge', 'trigger',
        'run', 'query', 'publish', 'close', 'reconcile', 'approve', 'decline'
    )][string]$Action,
    [Parameter(Mandatory)][string]$OperationId,
    [Parameter(Mandatory)][string]$RepoRoot,
    [string]$PayloadJson = '{}'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($PayloadJson.Length -gt 65536) { throw 'Payload exceeds the 65536-character limit.' }
try {
    $payload = ConvertFrom-Json -InputObject $PayloadJson -AsHashtable -Depth 32 -ErrorAction Stop
}
catch {
    throw 'PayloadJson must contain one valid JSON object.'
}
if ($payload -isnot [System.Collections.IDictionary]) {
    throw 'PayloadJson must contain one JSON object.'
}

Import-Module (Join-Path $PSScriptRoot 'FactoryLoop.psm1') -Force -DisableNameChecking
Invoke-FactoryLoopLoopback -Domain $Domain -Action $Action -OperationId $OperationId `
    -RepoRoot $RepoRoot -Payload $payload | ConvertTo-Json -Depth 32 -Compress
