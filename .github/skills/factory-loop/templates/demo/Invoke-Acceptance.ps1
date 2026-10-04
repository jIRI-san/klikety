#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ArtifactRoot,
    [ValidateSet('test', 'preprod', 'prod')][string]$Environment = 'test'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$app = Join-Path $ArtifactRoot 'Invoke-DemoApp.ps1'
if (-not (Test-Path -LiteralPath $app -PathType Leaf)) {
    throw "Demo artifact is missing application entry point: $app"
}
$actual = & $app -Subtotal 120 -DiscountRate 0.25
$passed = [decimal]$actual -eq [decimal]90
[pscustomobject]@{
    schemaVersion = 1
    status = if ($passed) { 'passed' } else { 'failed' }
    environment = $Environment
    scenario = 'discounted-total'
    expected = '90.00'
    actual = ([decimal]$actual).ToString('0.00', [System.Globalization.CultureInfo]::InvariantCulture)
    cleanup = 'none'
} | ConvertTo-Json -Depth 5 -Compress
