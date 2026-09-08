#requires -Version 7.0
<#
.SYNOPSIS
Computes the canonical content digest for one architecture contract.

.DESCRIPTION
Canonicalizes parsed JSON by sorting object properties ordinally, preserving array order, and
serializing compact JSON as UTF-8 without a BOM. The root lockedContentSha256 property is the only
excluded value, so the digest binds every other contract field without becoming self-referential.
#>
[CmdletBinding()]
param(
    [string]$ContractPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-ArchCanonicalJsonElement {
    param(
        [Parameter(Mandatory)]
        [System.Text.Json.JsonElement]$Element,

        [Parameter(Mandatory)]
        [System.Text.Json.Utf8JsonWriter]$Writer,

        [switch]$ContractRoot
    )

    switch ($Element.ValueKind) {
        ([System.Text.Json.JsonValueKind]::Object) {
            $Writer.WriteStartObject()
            $properties = [System.Collections.Generic.List[System.Text.Json.JsonProperty]]::new()
            $names = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
            foreach ($property in $Element.EnumerateObject()) {
                if (-not $names.Add($property.Name)) {
                    throw "Contract JSON contains duplicate property '$($property.Name)'."
                }
                $properties.Add($property)
            }
            $properties.Sort([System.Comparison[System.Text.Json.JsonProperty]] {
                    param($left, $right)
                    [string]::CompareOrdinal($left.Name, $right.Name)
                })
            foreach ($property in $properties) {
                if ($ContractRoot -and
                    [string]::Equals($property.Name, 'lockedContentSha256', [System.StringComparison]::Ordinal)) {
                    continue
                }
                $Writer.WritePropertyName($property.Name)
                Write-ArchCanonicalJsonElement -Element $property.Value -Writer $Writer
            }
            $Writer.WriteEndObject()
            break
        }
        ([System.Text.Json.JsonValueKind]::Array) {
            $Writer.WriteStartArray()
            foreach ($item in $Element.EnumerateArray()) {
                Write-ArchCanonicalJsonElement -Element $item -Writer $Writer
            }
            $Writer.WriteEndArray()
            break
        }
        ([System.Text.Json.JsonValueKind]::String) { $Writer.WriteStringValue($Element.GetString()); break }
        ([System.Text.Json.JsonValueKind]::Number) { $Writer.WriteRawValue($Element.GetRawText(), $true); break }
        ([System.Text.Json.JsonValueKind]::True) { $Writer.WriteBooleanValue($true); break }
        ([System.Text.Json.JsonValueKind]::False) { $Writer.WriteBooleanValue($false); break }
        ([System.Text.Json.JsonValueKind]::Null) { $Writer.WriteNullValue(); break }
        default { throw "Unsupported JSON token kind '$($Element.ValueKind)'." }
    }
}

function Get-ArchContractContentHash {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$ContractPath
    )

    if (-not (Test-Path -LiteralPath $ContractPath -PathType Leaf)) {
        throw "Contract file not found: $ContractPath"
    }

    $document = $null
    $stream = $null
    $writer = $null
    try {
        $document = [System.Text.Json.JsonDocument]::Parse(
            [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $ContractPath).Path)
        )
        if ($document.RootElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
            throw 'Contract JSON root must be an object.'
        }
        $stream = [System.IO.MemoryStream]::new()
        $writer = [System.Text.Json.Utf8JsonWriter]::new(
            $stream,
            [System.Text.Json.JsonWriterOptions]@{ Indented = $false }
        )
        Write-ArchCanonicalJsonElement -Element $document.RootElement -Writer $writer -ContractRoot
        $writer.Flush()
        $canonicalBytes = $stream.ToArray()
    }
    catch {
        throw "Contract JSON is invalid: $($_.Exception.Message)"
    }
    finally {
        if ($writer) { $writer.Dispose() }
        if ($stream) { $stream.Dispose() }
        if ($document) { $document.Dispose() }
    }

    $digest = [System.Security.Cryptography.SHA256]::HashData($canonicalBytes)

    return [pscustomobject]@{
        Digest        = -join ($digest | ForEach-Object { $_.ToString('x2') })
        CanonicalJson = [System.Text.Encoding]::UTF8.GetString($canonicalBytes)
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    if (-not $ContractPath) {
        throw 'ContractPath is required when running this script directly.'
    }
    Get-ArchContractContentHash -ContractPath $ContractPath
}
