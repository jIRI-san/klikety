#requires -Version 7.0
<#
.SYNOPSIS
Checks an architecture contract against the documented repository convention.
.DESCRIPTION
Validates the small JSON contract shape still used by transferred subsystems without a JSON Schema.
New repository-owned contracts should be written directly as Markdown architecture notes.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ContractPath,
    [switch]$NoExit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ContractPath -PathType Leaf)) {
    throw "Contract file not found: $ContractPath"
}

$errors = [System.Collections.Generic.List[string]]::new()
$contract = $null
try {
    $contract = Get-Content -LiteralPath $ContractPath -Raw | ConvertFrom-Json -Depth 100
}
catch {
    $errors.Add("invalid JSON: $($_.Exception.Message)")
}

if ($null -eq $contract -or $contract -isnot [psobject] -or
    $contract -is [string] -or $contract -is [ValueType]) {
    if ($errors.Count -eq 0) {
        $errors.Add('contract root must be a JSON object')
    }
}
else {
    function Get-ContractValue {
        param([Parameter(Mandatory)][string]$Name)
        if ($contract.PSObject.Properties.Name -contains $Name) { return $contract.$Name }
        return $null
    }

    $allowed = @(
        'id', 'title', 'description', 'owner', 'maturity', 'lockedContentSha256',
        'targets', 'rules', 'prose', 'interfaces'
    )
    $unknown = @($contract.PSObject.Properties.Name | Where-Object { $_ -notin $allowed })
    if ($unknown.Count -gt 0) {
        $errors.Add("unknown field(s): $($unknown -join ', ')")
    }
    if ([string](Get-ContractValue -Name 'id') -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') {
        $errors.Add('id must contain only letters, digits, dot, underscore, or hyphen')
    }
    if ([string]::IsNullOrWhiteSpace([string](Get-ContractValue -Name 'title'))) {
        $errors.Add('title is required')
    }
    if ([string](Get-ContractValue -Name 'maturity') -notin @('locked', 'draft', 'provisional')) {
        $errors.Add('maturity must be locked, draft, or provisional')
    }
    $hasBody = -not [string]::IsNullOrWhiteSpace([string](Get-ContractValue -Name 'prose')) -or
        @((Get-ContractValue -Name 'rules')).Count -gt 0 -or
        @((Get-ContractValue -Name 'interfaces')).Count -gt 0
    if (-not $hasBody) {
        $errors.Add('at least one of prose, rules, or interfaces is required')
    }
    if ([string](Get-ContractValue -Name 'maturity') -eq 'locked') {
        if ([string](Get-ContractValue -Name 'lockedContentSha256') -notmatch '^[a-f0-9]{64}$') {
            $errors.Add('locked contracts require a lowercase 64-character lockedContentSha256')
        }
        else {
            try {
                $hashScript = Join-Path $PSScriptRoot 'Get-ArchContractContentHash.ps1'
                $actualDigest = (& $hashScript -ContractPath $ContractPath).Digest
                if (-not [string]::Equals(
                        [string](Get-ContractValue -Name 'lockedContentSha256'),
                        $actualDigest,
                        [System.StringComparison]::Ordinal)) {
                    $errors.Add("lockedContentSha256 mismatch: expected $actualDigest.")
                }
            }
            catch {
                $errors.Add($_.Exception.Message)
            }
        }
    }
}

$result = [pscustomobject]@{
    Path = (Resolve-Path -LiteralPath $ContractPath).Path
    Valid = $errors.Count -eq 0
    Errors = @($errors)
}
$result

if (-not $result.Valid -and -not $NoExit) {
    [Console]::Error.WriteLine("Architecture contract invalid: $($result.Path)")
    foreach ($errorMessage in $result.Errors) {
        [Console]::Error.WriteLine("  - $errorMessage")
    }
    exit 1
}
