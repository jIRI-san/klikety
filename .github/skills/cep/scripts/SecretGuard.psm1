#requires -Version 7.0

Set-StrictMode -Version Latest

$script:SecretBlockPatterns = @(
    [pscustomobject]@{ Type = 'github-pat-classic'; Pattern = 'gh[pousr]_[0-9A-Za-z]{36}' }
    [pscustomobject]@{ Type = 'github-pat-fine-grained'; Pattern = 'github_pat_[0-9A-Za-z_]{22,}' }
    [pscustomobject]@{ Type = 'aws-access-key-id'; Pattern = '\b(?:AKIA|ASIA)[0-9A-Z]{16}\b' }
    [pscustomobject]@{ Type = 'google-api-key'; Pattern = '\bAIza[0-9A-Za-z_\-]{35}\b' }
    [pscustomobject]@{ Type = 'slack-token'; Pattern = 'xox[baprs]-[0-9A-Za-z-]{10,}' }
    [pscustomobject]@{ Type = 'stripe-secret-key'; Pattern = '\bsk_(?:live|test)_[0-9A-Za-z]{24,}\b' }
    [pscustomobject]@{ Type = 'npm-token'; Pattern = '\bnpm_[0-9A-Za-z]{36}\b' }
)
$script:PrivateKeyLabels = @(
    'PRIVATE KEY'
    'RSA PRIVATE KEY'
    'EC PRIVATE KEY'
    'DSA PRIVATE KEY'
    'OPENSSH PRIVATE KEY'
    'ENCRYPTED PRIVATE KEY'
    'PGP PRIVATE KEY BLOCK'
)
$script:PrivateKeyBeginPrefix = '-----BEGIN '
$script:SecretAllowLiterals = @('AKIAIOSFODNN7EXAMPLE')
$script:SecretPrefixPattern = '^(?:gh[pousr]_|github_pat_|AKIA|ASIA|AIza|xox[baprs]-|sk_(?:live|test)_|npm_)'
$script:SecretMaskPattern = '^(?:X+|x+|\*+|0+|\.+|#+|_+|-+)$'
$script:SecretSyntheticMarkers = @('REDACTED', 'EXAMPLE', 'PLACEHOLDER', 'DUMMY', 'SAMPLE', 'NOTAREALTOKEN')

function Get-PrivateKeyBlockSpan {
    param([Parameter(Mandatory)][string]$Value)

    $spans = [System.Collections.Generic.List[object]]::new()
    $cursor = 0
    while ($cursor -lt $Value.Length) {
        $begin = $Value.IndexOf(
            $script:PrivateKeyBeginPrefix,
            $cursor,
            [System.StringComparison]::Ordinal
        )
        if ($begin -lt 0) { break }

        $label = $null
        $headerLength = 0
        foreach ($candidate in $script:PrivateKeyLabels) {
            $header = "$($script:PrivateKeyBeginPrefix)$candidate-----"
            if (($begin + $header.Length) -le $Value.Length -and
                [string]::CompareOrdinal($Value, $begin, $header, 0, $header.Length) -eq 0) {
                $label = $candidate
                $headerLength = $header.Length
                break
            }
        }
        if ($null -eq $label) {
            $cursor = $begin + $script:PrivateKeyBeginPrefix.Length
            continue
        }

        $endMarker = "-----END $label-----"
        $end = $Value.IndexOf(
            $endMarker,
            $begin + $headerLength,
            [System.StringComparison]::Ordinal
        )
        $spanEnd = if ($end -lt 0) { $Value.Length } else { $end + $endMarker.Length }
        $spans.Add([pscustomobject]@{
                Start  = $begin
                Length = $spanEnd - $begin
            })
        $cursor = $spanEnd
    }
    return $spans.ToArray()
}

function Test-HighConfidenceSecretAllowed {
    param([Parameter(Mandatory)][string]$Token)

    foreach ($literal in $script:SecretAllowLiterals) {
        if ($Token -ceq $literal) { return $true }
    }

    $prefix = [regex]::Match($Token, $script:SecretPrefixPattern)
    if (-not $prefix.Success) { return $false }
    $body = $Token.Substring($prefix.Length)
    if ($body.Length -lt 8) { return $false }
    if ([regex]::IsMatch($body, $script:SecretMaskPattern)) { return $true }

    foreach ($marker in $script:SecretSyntheticMarkers) {
        $repeats = [int][Math]::Ceiling($body.Length / [double]$marker.Length)
        $expanded = ($marker * $repeats).Substring(0, $body.Length)
        if ($body -ceq $expanded) { return $true }
    }
    return $false
}

function Find-HighConfidenceSecret {
    [CmdletBinding()]
    param([AllowEmptyString()][AllowNull()][string]$Value)

    if ([string]::IsNullOrEmpty($Value)) { return @() }
    $hits = [System.Collections.Generic.List[string]]::new()
    foreach ($rule in $script:SecretBlockPatterns) {
        foreach ($match in [regex]::Matches($Value, $rule.Pattern)) {
            if (Test-HighConfidenceSecretAllowed -Token $match.Value) { continue }
            $hits.Add($rule.Type)
        }
    }
    if (@(Get-PrivateKeyBlockSpan -Value $Value).Count -gt 0) {
        $hits.Add('private-key-block')
    }
    return @($hits | Select-Object -Unique)
}

function Protect-HighConfidenceSecret {
    [CmdletBinding()]
    param([AllowEmptyString()][AllowNull()][string]$Value)

    if ([string]::IsNullOrEmpty($Value)) { return $Value }

    $spans = @(Get-PrivateKeyBlockSpan -Value $Value)
    if ($spans.Count -gt 0) {
        $builder = [System.Text.StringBuilder]::new()
        $cursor = 0
        foreach ($span in $spans) {
            [void]$builder.Append($Value, $cursor, $span.Start - $cursor)
            [void]$builder.Append('[REDACTED:private-key-block]')
            $cursor = $span.Start + $span.Length
        }
        [void]$builder.Append($Value, $cursor, $Value.Length - $cursor)
        $protected = $builder.ToString()
    }
    else {
        $protected = $Value
    }
    foreach ($rule in $script:SecretBlockPatterns) {
        $protected = [regex]::Replace(
            $protected,
            $rule.Pattern,
            [System.Text.RegularExpressions.MatchEvaluator] {
                param($match)

                if (Test-HighConfidenceSecretAllowed -Token $match.Value) {
                    return $match.Value
                }
                return "[REDACTED:$($rule.Type)]"
            }
        )
    }
    return $protected
}

Export-ModuleMember -Function Find-HighConfidenceSecret, Protect-HighConfidenceSecret
