#requires -Version 7.0

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-RepoRoot {
    [CmdletBinding()]
    param(
        [string]$StartPath = $PSScriptRoot
    )

    $candidateStartPath = [System.IO.Path]::GetFullPath($StartPath)
    if (Test-Path -LiteralPath $candidateStartPath -PathType Leaf) {
        $candidateStartPath = Split-Path -Parent $candidateStartPath
    }

    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $repoRoot = git -C $candidateStartPath rev-parse --show-toplevel 2>$null
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = $previous
    if ($exitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($repoRoot)) {
        return [System.IO.Path]::GetFullPath($repoRoot.Trim())
    }

    $current = $candidateStartPath
    while ($true) {
        if (Test-Path -LiteralPath (Join-Path $current '.git')) {
            return $current
        }

        $parent = Split-Path -Parent $current
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $current) {
            throw "Unable to resolve repository root from '$StartPath'."
        }
        $current = $parent
    }
}

function Resolve-RegistryPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$RepoRoot,

        [string]$RegistryPath
    )

    if (-not [string]::IsNullOrWhiteSpace($RegistryPath)) {
        $explicit = [System.IO.Path]::GetFullPath($RegistryPath)
        if (-not (Test-Path -LiteralPath $explicit -PathType Leaf)) {
            throw "registry.json not found at -RegistryPath '$RegistryPath'."
        }
        return $explicit
    }

    $rootRegistry = Join-Path $RepoRoot 'registry.json'
    if (Test-Path -LiteralPath $rootRegistry -PathType Leaf) {
        return $rootRegistry
    }

    # Bootstrapped repos have no root registry.json; bootstrap.ps1 writes it here.
    $bootstrapRegistry = Join-Path $RepoRoot 'scripts/skalary/registry.json'
    if (Test-Path -LiteralPath $bootstrapRegistry -PathType Leaf) {
        return $bootstrapRegistry
    }

    throw "No skalary registry found under '$RepoRoot' (looked for registry.json and scripts/skalary/registry.json). This is not a skalary-managed repo - run scripts/skalary/bootstrap.ps1 first, or pass -RegistryPath."
}

function Assert-PluginName {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$PluginName
    )

    if ($PluginName -notmatch '^[a-z0-9][a-z0-9-]*$') {
        throw 'Plugin name must contain only lowercase letters, digits, and hyphens, and must start with a letter or digit.'
    }
}

function Get-StringSha256 {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Value
    )

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
    return [System.Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function ConvertTo-CanonicalGithubRepository {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Repository
    )

    $value = $Repository.Trim()
    $owner = $null
    $name = $null

    if ($value -match '^github\.com/(?<owner>[A-Za-z0-9](?:[A-Za-z0-9-]{0,38}))/(?<name>[A-Za-z0-9._-]+?)(?:\.git)?$') {
        $owner = $Matches.owner
        $name = $Matches.name
    }
    elseif ($value -match '^(?<owner>[A-Za-z0-9](?:[A-Za-z0-9-]{0,38}))/(?<name>[A-Za-z0-9._-]+?)(?:\.git)?$') {
        $owner = $Matches.owner
        $name = $Matches.name
    }
    elseif ($value -match '^(?:[^@\s]+@)?github\.com:(?<owner>[^/\s]+)/(?<name>[^/\s]+?)(?:\.git)?$') {
        $owner = $Matches.owner
        $name = $Matches.name
    }
    else {
        $uri = $null
        if (-not [System.Uri]::TryCreate($value, [System.UriKind]::Absolute, [ref]$uri) -or
            $uri.Host -ne 'github.com') {
            throw 'Repository source is not a supported GitHub repository identity.'
        }

        $validTransport = switch ($uri.Scheme.ToLowerInvariant()) {
            'https' { $uri.IsDefaultPort -or $uri.Port -eq 443 }
            'http' { $uri.IsDefaultPort -or $uri.Port -eq 80 }
            'ssh' { $uri.Port -eq -1 -or $uri.Port -eq 22 }
            'git' { $uri.Port -eq -1 -or $uri.Port -eq 9418 }
            default { $false }
        }
        if (-not $validTransport) {
            throw 'Repository source is not a supported GitHub repository identity.'
        }

        $segments = @($uri.AbsolutePath.Trim('/').Split('/', [System.StringSplitOptions]::RemoveEmptyEntries))
        if ($segments.Count -ne 2) {
            throw 'Repository source is not a supported GitHub repository identity.'
        }
        $owner = $segments[0]
        $name = $segments[1] -replace '\.git$', ''
    }

    if ($owner -notmatch '^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})$' -or
        $name -notmatch '^[A-Za-z0-9._-]+$') {
        throw 'Repository source is not a supported GitHub repository identity.'
    }

    return "github.com/$($owner.ToLowerInvariant())/$($name.ToLowerInvariant())"
}

function New-PluginSourceIdentity {
    [CmdletBinding(DefaultParameterSetName = 'Github')]
    param(
        [Parameter(Mandatory, ParameterSetName = 'Github')]
        [string]$Repository,

        [Parameter(Mandatory, ParameterSetName = 'Local')]
        [string]$LocalPath
    )

    if ($PSCmdlet.ParameterSetName -eq 'Local') {
        $canonicalPath = [System.IO.Path]::GetFullPath($LocalPath)
        return [pscustomobject][ordered]@{
            version = 1
            kind = 'local'
            identity = "sha256:$(Get-StringSha256 -Value $canonicalPath)"
        }
    }

    return [pscustomobject][ordered]@{
        version = 1
        kind = 'github'
        identity = ConvertTo-CanonicalGithubRepository -Repository $Repository
    }
}

function Assert-PluginSourceIdentity {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        $SourceIdentity
    )

    $properties = @($SourceIdentity.PSObject.Properties.Name)
    $version = $SourceIdentity.version
    $isIntegerVersion = ($version -is [byte] -or
        $version -is [sbyte] -or
        $version -is [int16] -or
        $version -is [uint16] -or
        $version -is [int32] -or
        $version -is [uint32] -or
        $version -is [int64] -or
        $version -is [uint64])
    if ($properties.Count -ne 3 -or
        $properties -notcontains 'version' -or
        $properties -notcontains 'kind' -or
        $properties -notcontains 'identity' -or
        -not $isIntegerVersion -or
        $version -ne 1) {
        throw 'Plugin source identity has an unsupported or ambiguous shape.'
    }

    $kind = [string]$SourceIdentity.kind
    $identity = [string]$SourceIdentity.identity
    if ($kind -ceq 'github') {
        if ($identity -cne (ConvertTo-CanonicalGithubRepository -Repository $identity)) {
            throw 'GitHub plugin source identity is not canonical.'
        }
    }
    elseif ($kind -ceq 'local') {
        if ($identity -cnotmatch '^sha256:[a-f0-9]{64}$') {
            throw 'Local plugin source identity is invalid.'
        }
    }
    else {
        throw 'Plugin source identity kind is unsupported.'
    }
}

function Resolve-PluginReceiptSourceIdentity {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        $Receipt
    )

    if ($Receipt.PSObject.Properties.Name -contains 'sourceIdentity') {
        Assert-PluginSourceIdentity -SourceIdentity $Receipt.sourceIdentity
        return $Receipt.sourceIdentity
    }

    if ($Receipt.PSObject.Properties.Name -notcontains 'source' -or
        $Receipt.PSObject.Properties.Name -notcontains 'ref') {
        throw 'Legacy plugin receipt source identity is missing and cannot be reconciled safely.'
    }

    $legacy = [string]$Receipt.source
    $ref = [string]$Receipt.ref
    if ([string]::IsNullOrWhiteSpace($legacy) -or
        $ref -notmatch '^[a-f0-9]{40}([a-f0-9]{24})?$') {
        throw 'Legacy plugin receipt source identity is ambiguous and requires an explicit migration.'
    }

    $suffix = "@$ref"
    $label = if ($legacy.EndsWith($suffix, [System.StringComparison]::Ordinal) -and
        $legacy.Length -gt $suffix.Length) {
        $legacy.Substring(0, $legacy.Length - $suffix.Length)
    }
    else {
        $legacy
    }
    if ($label.StartsWith('remote:', [System.StringComparison]::Ordinal)) {
        return New-PluginSourceIdentity -Repository $label.Substring('remote:'.Length)
    }
    if ($label.StartsWith('local:', [System.StringComparison]::Ordinal)) {
        return New-PluginSourceIdentity -LocalPath $label.Substring('local:'.Length)
    }
    try {
        return New-PluginSourceIdentity -Repository $label
    }
    catch {
        throw 'Legacy plugin receipt source identity is ambiguous and requires an explicit migration.'
    }
}

function Test-PluginSourceIdentityEqual {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        $Left,

        [Parameter(Mandatory)]
        $Right
    )

    Assert-PluginSourceIdentity -SourceIdentity $Left
    Assert-PluginSourceIdentity -SourceIdentity $Right
    return ([int]$Left.version -eq [int]$Right.version -and
        [string]$Left.kind -ceq [string]$Right.kind -and
        [string]$Left.identity -ceq [string]$Right.identity)
}

function Test-GithubRelativePath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$RelativePath
    )

    if ([string]::IsNullOrWhiteSpace($RelativePath)) {
        return $false
    }

    if ($RelativePath.StartsWith('/') -or $RelativePath.StartsWith('\')) {
        return $false
    }

    if ($RelativePath -match '^[A-Za-z]:') {
        return $false
    }

    if ($RelativePath -match '\\\\') {
        return $false
    }

    if ($RelativePath.Contains(':')) {
        return $false
    }

    $segments = ($RelativePath -replace '\\', '/').Split('/', [System.StringSplitOptions]::RemoveEmptyEntries)
    $meaningfulSegments = @($segments | Where-Object { $_ -ne '.' })
    if ($meaningfulSegments.Count -gt 0 -and $meaningfulSegments[0] -ceq 'workflows') {
        return $false
    }
    foreach ($segment in $segments) {
        if ($segment -eq '..') {
            return $false
        }
    }

    return $true
}

function Resolve-GithubConstrainedPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$RepoRoot,

        [Parameter(Mandatory)]
        [string]$RelativePath
    )

    if (-not (Test-GithubRelativePath -RelativePath $RelativePath)) {
        throw "Path '$RelativePath' is not a valid .github-relative destination."
    }

    $githubRoot = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot '.github'))
    $relativeSystemPath = ($RelativePath -replace '/', [System.IO.Path]::DirectorySeparatorChar)
    $candidate = [System.IO.Path]::GetFullPath((Join-Path $githubRoot $relativeSystemPath))

    $separator = [System.IO.Path]::DirectorySeparatorChar
    $githubRootWithSeparator = $githubRoot.TrimEnd($separator) + $separator
    if (-not $candidate.StartsWith($githubRootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Resolved path '$candidate' escapes .github root '$githubRoot'."
    }

    return $candidate
}

function Resolve-PluginConstrainedPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$PluginRoot,

        [Parameter(Mandatory)]
        [string]$RelativePath
    )

    if ([string]::IsNullOrWhiteSpace($RelativePath)) {
        throw 'Source path is empty.'
    }

    if ($RelativePath.StartsWith('/') -or $RelativePath.StartsWith('\')) {
        throw "Source path '$RelativePath' must be relative."
    }

    if ($RelativePath -match '^[A-Za-z]:') {
        throw "Source path '$RelativePath' cannot be drive-relative or absolute."
    }

    if ($RelativePath -match '\\\\') {
        throw "Source path '$RelativePath' cannot be UNC."
    }

    if ($RelativePath.Contains(':')) {
        throw "Source path '$RelativePath' cannot contain ':'."
    }

    $segments = ($RelativePath -replace '\\', '/').Split('/', [System.StringSplitOptions]::RemoveEmptyEntries)
    foreach ($segment in $segments) {
        if ($segment -eq '..') {
            throw "Source path '$RelativePath' cannot traverse parent directories."
        }
    }

    $normalizedRoot = [System.IO.Path]::GetFullPath($PluginRoot)
    $normalizedRelativePath = ($RelativePath -replace '/', [System.IO.Path]::DirectorySeparatorChar)
    $candidate = [System.IO.Path]::GetFullPath((Join-Path $normalizedRoot $normalizedRelativePath))
    $separator = [System.IO.Path]::DirectorySeparatorChar
    $rootWithSeparator = $normalizedRoot.TrimEnd($separator) + $separator
    if (-not $candidate.StartsWith($rootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Source path '$RelativePath' resolves outside plugin root '$normalizedRoot'."
    }

    return $candidate
}

function Get-FileSha256 {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "File not found: $Path"
    }

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-GitCanonicalBlobObjectId {
    param([Parameter(Mandatory)][string]$RepoRoot, [Parameter(Mandatory)][string]$Path)
    $root = Resolve-RepoRoot -StartPath $RepoRoot
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "File not found: $Path"
    }
    $relativePath = [System.IO.Path]::GetRelativePath($root, $fullPath).Replace('\', '/')
    if ($relativePath.StartsWith('../', [System.StringComparison]::Ordinal) -or
        [System.IO.Path]::IsPathRooted($relativePath)) {
        throw "File '$Path' is outside repository '$root'."
    }

    # --path applies the repository's clean filters (notably text/eol) to the
    # current working-tree bytes. -w lets cat-file stream those canonical bytes
    # without touching the index, so uncommitted source edits remain buildable.
    $hashOutput = @(& git -C $root hash-object -w "--path=$relativePath" -- $fullPath 2>&1)
    $hashExitCode = $LASTEXITCODE
    $objectId = @($hashOutput | ForEach-Object { ([string]$_).Trim() } |
            Where-Object { $_ -cmatch '^[0-9a-f]{40,64}$' } | Select-Object -Last 1)
    if ($hashExitCode -ne 0 -or $objectId.Count -ne 1) {
        throw "Unable to canonicalize '$relativePath' through Git clean filters: $($hashOutput -join ' ')"
    }
    return [pscustomobject]@{ Root = $root; RelativePath = $relativePath; ObjectId = [string]$objectId[0] }
}

function Get-GitCanonicalFileSha256 {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$Path
    )

    $blob = Get-GitCanonicalBlobObjectId -RepoRoot $RepoRoot -Path $Path

    $start = [System.Diagnostics.ProcessStartInfo]::new()
    $start.FileName = 'git'
    $start.WorkingDirectory = $blob.Root
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @('-C', $blob.Root, 'cat-file', 'blob', $blob.ObjectId)) {
        [void]$start.ArgumentList.Add($argument)
    }
    $process = [System.Diagnostics.Process]::Start($start)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash($process.StandardOutput.BaseStream)
        $errorText = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) {
            throw "Unable to read canonical Git blob '$($blob.ObjectId)' for '$($blob.RelativePath)': $($errorText.Trim())"
        }
        return [System.Convert]::ToHexString($hash).ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
        $process.Dispose()
    }
}

function Copy-GitCanonicalFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Destination
    )

    $blob = Get-GitCanonicalBlobObjectId -RepoRoot $RepoRoot -Path $Path
    $start = [System.Diagnostics.ProcessStartInfo]::new()
    $start.FileName = 'git'
    $start.WorkingDirectory = $blob.Root
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @('-C', $blob.Root, 'cat-file', 'blob', $blob.ObjectId)) {
        [void]$start.ArgumentList.Add($argument)
    }
    $process = [System.Diagnostics.Process]::Start($start)
    $stream = [System.IO.File]::Open(
        $Destination,
        [System.IO.FileMode]::Create,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None
    )
    try {
        $process.StandardOutput.BaseStream.CopyTo($stream)
        $stream.Flush()
        $errorText = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) {
            throw "Unable to materialize canonical Git blob for '$($blob.RelativePath)': $($errorText.Trim())"
        }
    }
    finally {
        $stream.Dispose()
        $process.Dispose()
    }
}

function ConvertTo-SemVer {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Version
    )

    $semverPattern = '^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?:-(?<pre>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+(?<build>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$'
    if ($Version -notmatch $semverPattern) {
        throw "Invalid semantic version '$Version'."
    }

    return [pscustomobject]@{
        Original = $Version
        Major = [int]$Matches.major
        Minor = [int]$Matches.minor
        Patch = [int]$Matches.patch
        PreRelease = if ($Matches.ContainsKey('pre')) { $Matches.pre } else { $null }
        Build = if ($Matches.ContainsKey('build')) { $Matches.build } else { $null }
    }
}

function Compare-PreRelease {
    [CmdletBinding()]
    param(
        [AllowNull()]
        [string]$Left,

        [AllowNull()]
        [string]$Right
    )

    if ([string]::IsNullOrWhiteSpace($Left) -and [string]::IsNullOrWhiteSpace($Right)) {
        return 0
    }
    if ([string]::IsNullOrWhiteSpace($Left)) {
        return 1
    }
    if ([string]::IsNullOrWhiteSpace($Right)) {
        return -1
    }

    $leftParts = $Left.Split('.')
    $rightParts = $Right.Split('.')
    $max = [Math]::Max($leftParts.Count, $rightParts.Count)

    for ($index = 0; $index -lt $max; $index++) {
        if ($index -ge $leftParts.Count) { return -1 }
        if ($index -ge $rightParts.Count) { return 1 }

        $leftPart = $leftParts[$index]
        $rightPart = $rightParts[$index]

        $leftNumeric = $leftPart -match '^[0-9]+$'
        $rightNumeric = $rightPart -match '^[0-9]+$'

        if ($leftNumeric -and $rightNumeric) {
            $comparison = [int]$leftPart - [int]$rightPart
            if ($comparison -ne 0) {
                return [Math]::Sign($comparison)
            }
            continue
        }

        if ($leftNumeric -and -not $rightNumeric) { return -1 }
        if (-not $leftNumeric -and $rightNumeric) { return 1 }

        $stringComparison = [string]::CompareOrdinal($leftPart, $rightPart)
        if ($stringComparison -ne 0) {
            return [Math]::Sign($stringComparison)
        }
    }

    return 0
}

function Compare-SemVer {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Left,

        [Parameter(Mandatory)]
        [string]$Right
    )

    $leftVersion = ConvertTo-SemVer -Version $Left
    $rightVersion = ConvertTo-SemVer -Version $Right

    foreach ($part in 'Major', 'Minor', 'Patch') {
        if ($leftVersion.$part -lt $rightVersion.$part) { return -1 }
        if ($leftVersion.$part -gt $rightVersion.$part) { return 1 }
    }

    return Compare-PreRelease -Left $leftVersion.PreRelease -Right $rightVersion.PreRelease
}

function Sort-Ordinal {
    <#
    .SYNOPSIS
        Sorts strings, or objects by string properties, with an explicit comparer.
    .DESCRIPTION
        `Sort-Object` compares strings through the current culture, so the order it
        produces is a property of the host rather than of the input: cs-CZ reads the
        digraph `ch` as one letter placed after `h`, and sorts accented letters apart
        from the base letter en-US folds them onto. Generated catalogs are compared
        byte-for-byte by the drift gates, so any list that reaches a generated file has
        to be ordered by a comparer the build carries with it (REQ-7, D8).
    .PARAMETER Property
        Property names to sort by, most significant first. Omit to sort the inputs
        themselves as strings.
    .PARAMETER Comparer
        Defaults to ordinal. Callers pass it explicitly where the choice is part of the
        contract the file documents.
    #>
    [CmdletBinding()]
    [OutputType([object[]])]
    param(
        [Parameter(Mandatory)]
        [AllowNull()]
        [AllowEmptyCollection()]
        [object[]]$InputObject,

        [string[]]$Property,

        [switch]$Descending,

        [System.StringComparer]$Comparer = [System.StringComparer]::Ordinal
    )

    $items = @($InputObject | Where-Object { $null -ne $_ })
    if ($items.Count -le 1) {
        return $items
    }

    $decorated = [object[]]@(
        foreach ($item in $items) {
            # Assigned inside each branch rather than from the `if` statement's value:
            # statement output is enumerated, so a single-element key array would arrive
            # as a bare string and index by character.
            [string[]]$keys = @()
            if ($Property) {
                $keys = [string[]]@($Property | ForEach-Object { [string]$item.$_ })
            }
            else {
                $keys = [string[]]@([string]$item)
            }
            [pscustomobject]@{ Item = $item; Keys = $keys }
        }
    )

    $activeComparer = $Comparer
    $sign = if ($Descending) { -1 } else { 1 }
    # Sign-flipped rather than reversed: [array]::Sort with a Comparison is unstable, so
    # reversing its output would scramble entries whose keys compare equal.
    $comparison = [System.Comparison[object]] {
        param($left, $right)

        $leftKeys = $left.Keys
        $rightKeys = $right.Keys
        $sharedCount = [Math]::Min($leftKeys.Length, $rightKeys.Length)
        for ($index = 0; $index -lt $sharedCount; $index++) {
            $result = $activeComparer.Compare($leftKeys[$index], $rightKeys[$index])
            if ($result -ne 0) {
                return ($sign * [Math]::Sign($result))
            }
        }
        return ($sign * [Math]::Sign($leftKeys.Length - $rightKeys.Length))
    }

    [array]::Sort($decorated, $comparison)
    # Enumerated rather than returned as one array value: every call site wraps the result
    # in @(), which would otherwise nest the array inside a single-element array.
    return @($decorated | ForEach-Object { $_.Item })
}

function ConvertTo-SortedObject {
    [CmdletBinding()]
    param(
        [AllowNull()]
        $InputObject
    )

    if ($null -eq $InputObject) {
        return $null
    }

    if ($InputObject -is [System.Collections.IDictionary]) {
        $ordered = [ordered]@{}
        foreach ($key in (Sort-Ordinal -InputObject @($InputObject.Keys))) {
            $ordered[$key] = ConvertTo-SortedObject -InputObject $InputObject[$key]
        }
        return [pscustomobject]$ordered
    }

    if ($InputObject -is [pscustomobject] -and -not ($InputObject -is [string])) {
        $ordered = [ordered]@{}
        foreach ($property in (Sort-Ordinal -InputObject @($InputObject.PSObject.Properties.Name))) {
            $ordered[$property] = ConvertTo-SortedObject -InputObject $InputObject.$property
        }
        return [pscustomobject]$ordered
    }

    if ($InputObject -is [System.Collections.IEnumerable] -and -not ($InputObject -is [string])) {
        $items = @()
        foreach ($item in $InputObject) {
            $items += , (ConvertTo-SortedObject -InputObject $item)
        }
        return , ([object[]]$items)
    }

    return $InputObject
}

function Read-JsonFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "JSON file not found: $Path"
    }

    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -Depth 100
}

function Write-JsonFileStable {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        $InputObject
    )

    $sorted = ConvertTo-SortedObject -InputObject $InputObject
    $json = $sorted | ConvertTo-Json -Depth 100
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $directory = Split-Path -Parent $fullPath
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        throw "JSON destination directory not found: $directory"
    }
    $tempPath = Join-Path $directory (".$([System.IO.Path]::GetFileName($fullPath)).$([guid]::NewGuid().ToString('N')).tmp")
    $stream = $null
    try {
        $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes("$json`n")
        $stream = [System.IO.File]::Open(
            $tempPath,
            [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::Write,
            [System.IO.FileShare]::None)
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Flush($true)
        $stream.Dispose()
        $stream = $null
        [System.IO.File]::Move($tempPath, $fullPath, $true)
    }
    finally {
        if ($null -ne $stream) {
            $stream.Dispose()
        }
        if (Test-Path -LiteralPath $tempPath -PathType Leaf) {
            Remove-Item -LiteralPath $tempPath -Force
        }
    }
}

function Assert-GithubStatePathSafe {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$Path
    )

    $githubRoot = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot '.github'))
    $candidate = [System.IO.Path]::GetFullPath($Path)
    $relative = [System.IO.Path]::GetRelativePath($githubRoot, $candidate)
    if ($relative.StartsWith('..') -or [System.IO.Path]::IsPathRooted($relative)) {
        throw 'Managed state path escapes the .github root.'
    }

    $current = $githubRoot
    foreach ($segment in @($relative.Split(
                [System.IO.Path]::DirectorySeparatorChar,
                [System.StringSplitOptions]::RemoveEmptyEntries))) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 -or
                -not [string]::IsNullOrWhiteSpace([string]$item.LinkType)) {
                throw "Managed state path traverses a link or reparse point at '$current'."
            }
        }
        $current = Join-Path $current $segment
    }
    if (Test-Path -LiteralPath $current) {
        $item = Get-Item -LiteralPath $current -Force
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 -or
            -not [string]::IsNullOrWhiteSpace([string]$item.LinkType)) {
            throw "Managed state path resolves to a link or reparse point at '$current'."
        }
    }
}

function Test-PluginReceiptShape {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        $Receipt,

        [Parameter(Mandatory)]
        [string]$ExpectedPluginName
    )

    $properties = @($Receipt.PSObject.Properties.Name)
    $expectedProperties = @('name', 'version', 'sourceIdentity', 'ref')
    if ($properties.Count -ne $expectedProperties.Count -or
        @($properties | Where-Object { $_ -notin $expectedProperties }).Count -gt 0) {
        throw "Plugin receipt '$ExpectedPluginName' must contain exactly name, version, sourceIdentity, and ref."
    }
    if ([string]$Receipt.name -cne $ExpectedPluginName) {
        throw "Plugin receipt name does not match '$ExpectedPluginName'."
    }
    try {
        [void](ConvertTo-SemVer -Version ([string]$Receipt.version))
        Assert-PluginSourceIdentity -SourceIdentity $Receipt.sourceIdentity
    }
    catch {
        throw "Plugin receipt '$ExpectedPluginName' is invalid: $($_.Exception.Message)"
    }
    if ([string]$Receipt.ref -cnotmatch '^[a-f0-9]{40}(?:[a-f0-9]{24})?$') {
        throw "Plugin receipt '$ExpectedPluginName' has an invalid immutable ref."
    }

    return $true
}

function Get-PluginReceiptPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$RepoRoot,

        [Parameter(Mandatory)]
        [string]$PluginName
    )

    Assert-PluginName -PluginName $PluginName
    return Resolve-GithubConstrainedPath -RepoRoot $RepoRoot -RelativePath ".skalary/receipts/$PluginName.json"
}

function Read-PluginReceipt {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$RepoRoot,

        [Parameter(Mandatory)]
        [string]$PluginName
    )

    $receiptPath = Get-PluginReceiptPath -RepoRoot $RepoRoot -PluginName $PluginName
    Assert-GithubStatePathSafe -RepoRoot $RepoRoot -Path $receiptPath
    if (-not (Test-Path -LiteralPath $receiptPath -PathType Leaf)) {
        return $null
    }

    $receipt = Read-JsonFile -Path $receiptPath
    [void](Test-PluginReceiptShape -Receipt $receipt -ExpectedPluginName $PluginName)
    return $receipt
}

function Write-PluginReceipt {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$RepoRoot,

        [Parameter(Mandatory)]
        $Receipt
    )

    $pluginName = [string]$Receipt.name
    [void](Test-PluginReceiptShape -Receipt $Receipt -ExpectedPluginName $pluginName)
    $receiptPath = Get-PluginReceiptPath -RepoRoot $RepoRoot -PluginName $pluginName
    $receiptDirectory = Split-Path -Parent $receiptPath
    Assert-GithubStatePathSafe -RepoRoot $RepoRoot -Path $receiptPath
    if (-not (Test-Path -LiteralPath $receiptDirectory -PathType Container)) {
        [void](New-Item -ItemType Directory -Path $receiptDirectory -Force)
    }
    Assert-GithubStatePathSafe -RepoRoot $RepoRoot -Path $receiptPath
    Write-JsonFileStable -Path $receiptPath -InputObject $Receipt
    return $receiptPath
}

function Test-PluginReceiptUpToDate {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$RepoRoot,

        [Parameter(Mandatory)]
        $Plugin
    )

    $receipt = Read-PluginReceipt -RepoRoot $RepoRoot -PluginName ([string]$Plugin.name)
    if ($null -eq $receipt) {
        return $false
    }

    return [string]$receipt.version -ceq [string]$Plugin.version
}

function Invoke-PluginRemovalPrimitive {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$PluginName,
        [string]$Source,
        [switch]$Force
    )

    $receipt = Read-PluginReceipt -RepoRoot $RepoRoot -PluginName $PluginName
    if ($null -eq $receipt) { throw "Plugin '$PluginName' is not installed (receipt missing)." }
    $sourceRoot = $null
    $temporary = $null
    try {
        $candidate = if ([string]::IsNullOrWhiteSpace($Source)) {
            Resolve-RepoRoot -StartPath $RepoRoot
        } else {
            Resolve-RepoRoot -StartPath $Source
        }
        $candidateIdentity = New-PluginSourceIdentity -LocalPath $candidate
        if (Test-PluginSourceIdentityEqual -Left $receipt.sourceIdentity -Right $candidateIdentity) {
            $temporary = Join-Path ([System.IO.Path]::GetTempPath()) ("skalary-remove-" + [guid]::NewGuid().ToString('N'))
            [void](New-Item -ItemType Directory -Path $temporary -Force)
            git -C $candidate archive $receipt.ref | tar -xf - -C $temporary
            if ($LASTEXITCODE -ne 0) {
                throw "Unable to materialize installed ref '$($receipt.ref)' from local source."
            }
            $sourceRoot = $temporary
        } elseif ([string]$receipt.sourceIdentity.kind -ceq 'github') {
            $temporary = Join-Path ([System.IO.Path]::GetTempPath()) ("skalary-remove-" + [guid]::NewGuid().ToString('N'))
            git -c core.longpaths=true clone -c core.autocrlf=false -c core.eol=lf --no-checkout "https://$($receipt.sourceIdentity.identity).git" $temporary 2>$null | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "Unable to materialize installed source '$($receipt.sourceIdentity.identity)'." }
            git -c core.longpaths=true -C $temporary checkout --quiet $receipt.ref
            if ($LASTEXITCODE -ne 0) { throw "Unable to materialize installed ref '$($receipt.ref)'." }
            $sourceRoot = $temporary
        } else {
            throw "Installed local source for '$PluginName' cannot be materialized from its opaque source identity."
        }

        $manifestPath = Join-Path $sourceRoot "plugins/$PluginName/plugin.json"
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
            throw "Installed manifest for '$PluginName' is unavailable at ref '$($receipt.ref)'."
        }
        $manifest = Read-JsonFile -Path $manifestPath
        if ([string]$manifest.version -cne [string]$receipt.version) {
            throw "Installed manifest version does not match receipt version for '$PluginName'."
        }
        $files = @($manifest.files | Where-Object { [string]$_.src -notmatch '^evals(?:/|$)' })
        $modified = @()
        $missing = 0
        foreach ($file in $files) {
            $sourcePath = Resolve-PluginConstrainedPath -PluginRoot (Join-Path $sourceRoot "plugins/$PluginName") -RelativePath ([string]$file.src)
            $targetPath = Resolve-GithubConstrainedPath -RepoRoot $RepoRoot -RelativePath ([string]$file.dest)
            Assert-GithubStatePathSafe -RepoRoot $RepoRoot -Path $targetPath
            if (Test-Path -LiteralPath $targetPath -PathType Container) {
                $modified += [string]$file.dest
                continue
            }
            if (-not (Test-Path -LiteralPath $targetPath -PathType Leaf)) { $missing++; continue }
            if ((Get-FileSha256 -Path $targetPath) -cne (Get-FileSha256 -Path $sourcePath)) {
                $modified += [string]$file.dest
            }
        }
        if ($modified.Count -gt 0 -and -not $Force) {
            throw "Refusing removal of modified file(s): $($modified -join ', '). Use -Force to remove."
        }
        $removed = 0
        foreach ($file in $files) {
            $targetPath = Resolve-GithubConstrainedPath -RepoRoot $RepoRoot -RelativePath ([string]$file.dest)
            Assert-GithubStatePathSafe -RepoRoot $RepoRoot -Path $targetPath
            if (Test-Path -LiteralPath $targetPath -PathType Container) {
                throw "Managed destination '$([string]$file.dest)' is a directory."
            }
            if (Test-Path -LiteralPath $targetPath -PathType Leaf) {
                Remove-Item -LiteralPath $targetPath -Force
                if (Test-Path -LiteralPath $targetPath -PathType Leaf) { throw "Delete verification failed for '$([string]$file.dest)'." }
                $removed++
            }
        }
        $receiptPath = Get-PluginReceiptPath -RepoRoot $RepoRoot -PluginName $PluginName
        Remove-Item -LiteralPath $receiptPath -Force
        if (Test-Path -LiteralPath $receiptPath -PathType Leaf) { throw "Receipt deletion verification failed for '$PluginName'." }
        return [pscustomobject]@{ RemovedCount = $removed; ModifiedCount = 0; MissingCount = $missing }
    }
    finally {
        if ($null -ne $temporary -and (Test-Path -LiteralPath $temporary)) {
            Remove-Item -LiteralPath $temporary -Recurse -Force
        }
    }
}

function Resolve-PluginDependencyOrder {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [hashtable]$PluginsByName,

        [Parameter(Mandatory)]
        [string]$RootPluginName,

        [Parameter(Mandatory)]
        [string]$RepoRoot
    )

    if (-not $PluginsByName.ContainsKey($RootPluginName)) {
        throw "Plugin '$RootPluginName' is not present in registry.json."
    }

    $stateByName = @{}
    $topologicalNames = [System.Collections.Generic.List[string]]::new()

    function Search-DependencyNode {
        [CmdletBinding()]
        param(
            [Parameter(Mandatory)]
            [string]$Name,

            [string[]]$Stack
        )

        if (-not $PluginsByName.ContainsKey($Name)) {
            $parentName = if ($Stack.Count -gt 0) { $Stack[$Stack.Count - 1] } else { $RootPluginName }
            throw "Plugin '$parentName' depends on missing plugin '$Name'."
        }

        $state = if ($stateByName.ContainsKey($Name)) { [int]$stateByName[$Name] } else { 0 }
        if ($state -eq 1) {
            $cycle = @($Stack + $Name) -join ' -> '
            throw "Dependency cycle detected: $cycle"
        }
        if ($state -eq 2) {
            return
        }

        $stateByName[$Name] = 1
        $plugin = $PluginsByName[$Name]
        $dependencies = @($plugin.dependencies | ForEach-Object { [string]$_ } | Sort-Object)
        foreach ($dependencyName in $dependencies) {
            Search-DependencyNode -Name $dependencyName -Stack ($Stack + $Name)
        }

        $stateByName[$Name] = 2
        $topologicalNames.Add($Name)
    }

    Search-DependencyNode -Name $RootPluginName -Stack @()

    $ordered = @()
    $pending = @()
    foreach ($name in $topologicalNames) {
        $plugin = $PluginsByName[$name]
        $ordered += , $plugin
        if (-not (Test-PluginReceiptUpToDate -RepoRoot $RepoRoot -Plugin $plugin)) {
            $pending += , $plugin
        }
    }

    return [pscustomobject]@{
        Ordered = $ordered
        Pending = $pending
    }
}

