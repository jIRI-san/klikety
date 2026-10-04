#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('work-item', 'pull-request', 'deployment', 'version', 'acceptance', 'telemetry', 'evidence')]
    [string]$Domain,
    [Parameter(Mandatory)][ValidateSet('read', 'find', 'create', 'update', 'open', 'checks', 'trigger', 'run', 'query', 'publish', 'close', 'reconcile')]
    [string]$Action,
    [Parameter(Mandatory)][string]$OperationId,
    [string]$PayloadJson = '{}'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

throw "Project adapter '$Domain/$Action' is not configured. Replace this scaffold with a project-owned command that honors operation ID '$OperationId' and emits the documented JSON contract."
