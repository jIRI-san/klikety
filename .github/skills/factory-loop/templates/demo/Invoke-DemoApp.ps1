#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][decimal]$Subtotal,
    [Parameter(Mandatory)][decimal]$DiscountRate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# The planted defect intentionally adds the discount instead of subtracting it.
[decimal]::Round($Subtotal * (1 + $DiscountRate), 2)
