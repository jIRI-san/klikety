#requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:AtomicStoreStatus = [ordered]@{
    Complete = 'complete'
    LockTimeout = 'lock-timeout'
    CasConflict = 'cas-conflict'
    CasExhausted = 'cas-exhausted'
    CapacityBlocked = 'capacity-blocked'
    Invalid = 'invalid'
}

function Get-AtomicStoreStatus {
    [CmdletBinding()]
    param()

    return [pscustomobject]$script:AtomicStoreStatus
}

function Get-AtomicStoreGeneration {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return 'absent'
    }

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    return [Convert]::ToHexString(
        [System.Security.Cryptography.SHA256]::HashData($bytes)
    ).ToLowerInvariant()
}

function Get-AtomicStoreLockName {
    param([Parameter(Mandatory)][string]$Scope)

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Scope)
    $digest = [System.Security.Cryptography.SHA256]::HashData($bytes)
    return 'skalary-atomic-' + [Convert]::ToHexString($digest).ToLowerInvariant().Substring(0, 32)
}

function Invoke-WithAtomicStoreLock {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Scope,
        [Parameter(Mandatory)][scriptblock]$Action,
        [ValidateRange(1, 30)][int]$TimeoutSeconds = 30
    )

    $baseName = Get-AtomicStoreLockName -Scope $Scope
    $mutex = $null
    foreach ($prefix in @('Global\', 'Local\', '')) {
        try {
            $mutex = [System.Threading.Mutex]::new($false, "$prefix$baseName")
            break
        }
        catch {
            $mutex = $null
        }
    }
    if ($null -eq $mutex) {
        throw "Unable to create atomic-store lock '$baseName'."
    }

    $hasLock = $false
    try {
        try {
            $hasLock = $mutex.WaitOne([TimeSpan]::FromSeconds($TimeoutSeconds))
        }
        catch [System.Threading.AbandonedMutexException] {
            $hasLock = $true
        }
        if (-not $hasLock) {
            throw [System.TimeoutException]::new("Timed out acquiring atomic-store lock '$baseName'.")
        }
        return & $Action
    }
    finally {
        if ($hasLock) {
            [void]$mutex.ReleaseMutex()
        }
        $mutex.Dispose()
    }
}

function Set-AtomicStoreContent {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Content,
        [string]$ExpectedGeneration,
        [scriptblock]$Validate
    )

    $parameters = @{
        Path = $Path
        Bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($Content)
        Validate = $Validate
    }
    if ($PSBoundParameters.ContainsKey('ExpectedGeneration')) {
        $parameters.ExpectedGeneration = $ExpectedGeneration
    }
    return Set-AtomicStoreBytes @parameters
}

function Set-AtomicStoreBytes {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][AllowEmptyCollection()][byte[]]$Bytes,
        [string]$ExpectedGeneration,
        [scriptblock]$Validate
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $parent = [System.IO.Path]::GetDirectoryName($fullPath)
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        [void](New-Item -ItemType Directory -Path $parent -Force)
    }

    $tempPath = Join-Path $parent ('.atomic-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        $stream = [System.IO.FileStream]::new(
            $tempPath,
            [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::Write,
            [System.IO.FileShare]::None,
            4096,
            [System.IO.FileOptions]::WriteThrough
        )
        try {
            $stream.Write($Bytes, 0, $Bytes.Length)
            $stream.Flush($true)
        }
        finally {
            $stream.Dispose()
        }

        if ($Validate) {
            & $Validate $tempPath
        }

        $actualGeneration = Get-AtomicStoreGeneration -Path $fullPath
        if ($PSBoundParameters.ContainsKey('ExpectedGeneration') -and
            -not [string]::Equals($actualGeneration, $ExpectedGeneration, [System.StringComparison]::Ordinal)) {
            return [pscustomobject]@{
                Status = $script:AtomicStoreStatus.CasConflict
                Path = $fullPath
                ExpectedGeneration = $ExpectedGeneration
                ActualGeneration = $actualGeneration
            }
        }

        [System.IO.File]::Move($tempPath, $fullPath, $true)
        return [pscustomobject]@{
            Status = $script:AtomicStoreStatus.Complete
            Path = $fullPath
            Generation = Get-AtomicStoreGeneration -Path $fullPath
        }
    }
    finally {
        if (Test-Path -LiteralPath $tempPath -PathType Leaf) {
            Remove-Item -LiteralPath $tempPath -Force
        }
    }
}

function Invoke-AtomicStoreUpdate {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][scriptblock]$Transform,
        [string]$LockScope = ([System.IO.Path]::GetFullPath($Path)),
        [ValidateRange(1, 30)][int]$TimeoutSeconds = 30,
        [ValidateRange(1, 3)][int]$MaxAttempts = 3,
        [scriptblock]$Validate
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $lastConflict = $null
    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        $generation = Get-AtomicStoreGeneration -Path $fullPath
        $current = if ($generation -eq 'absent') { $null } else { [System.IO.File]::ReadAllText($fullPath) }
        $next = & $Transform $current $generation $attempt
        if ($null -eq $next) {
            throw 'Atomic-store transform returned no content.'
        }
        $content = if ($next -is [string]) { $next } else { [string]$next.Content }
        $value = if ($next -is [string] -or $next.PSObject.Properties.Name -notcontains 'Value') { $null } else { $next.Value }

        try {
            $write = Invoke-WithAtomicStoreLock -Scope $LockScope -TimeoutSeconds $TimeoutSeconds -Action {
                Set-AtomicStoreContent -Path $fullPath -Content $content -ExpectedGeneration $generation -Validate $Validate
            }
        }
        catch [System.TimeoutException] {
            return [pscustomobject]@{
                Status = $script:AtomicStoreStatus.LockTimeout
                Path = $fullPath
                Attempts = $attempt
                Value = $null
            }
        }

        if ($write.Status -eq $script:AtomicStoreStatus.Complete) {
            return [pscustomobject]@{
                Status = $script:AtomicStoreStatus.Complete
                Path = $fullPath
                Generation = $write.Generation
                Attempts = $attempt
                Value = $value
            }
        }
        $lastConflict = $write
    }

    return [pscustomobject]@{
        Status = $script:AtomicStoreStatus.CasExhausted
        Path = $fullPath
        Attempts = $MaxAttempts
        ExpectedGeneration = $lastConflict.ExpectedGeneration
        ActualGeneration = $lastConflict.ActualGeneration
        Value = $null
    }
}

Export-ModuleMember -Function Get-AtomicStoreStatus, Get-AtomicStoreGeneration,
    Invoke-WithAtomicStoreLock, Set-AtomicStoreContent, Set-AtomicStoreBytes,
    Invoke-AtomicStoreUpdate
