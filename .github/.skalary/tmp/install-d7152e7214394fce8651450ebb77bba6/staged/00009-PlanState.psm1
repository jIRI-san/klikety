#requires -Version 7.0

Set-StrictMode -Version Latest

function Remove-FencedCodeBlocks {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [AllowEmptyString()]
        [string[]]$Lines
    )

    $output = [System.Collections.Generic.List[string]]::new()
    $inFence = $false
    foreach ($line in $Lines) {
        if ($line -match '^\s*```') {
            $inFence = -not $inFence
            $output.Add('')
            continue
        }

        if ($inFence) {
            $output.Add('')
            continue
        }

        $output.Add($line)
    }

    return , $output.ToArray()
}

function Split-MarkdownTableCells {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Row
    )

    $trimmed = $Row.Trim()
    $withoutPipes = $trimmed.Trim('|')
    $rawCells = $withoutPipes.Split('|')
    $cells = @()
    foreach ($cell in $rawCells) {
        $cells += , $cell.Trim()
    }

    return , $cells
}

$script:PlanAssetMap = [ordered]@{
    Intent = [pscustomobject]@{ Asset = 'intent.md'; Legacy = 'intent.md' }
    Domain = [pscustomobject]@{ Asset = 'domain.md'; Legacy = 'domain.md' }
    Design = [pscustomobject]@{ Asset = 'design.md'; Legacy = 'design.md' }
    Requirements = [pscustomobject]@{ Asset = 'requirements.md'; Legacy = $null }
    Risks = [pscustomobject]@{ Asset = 'risks.md'; Legacy = $null }
    Decisions = [pscustomobject]@{ Asset = 'decisions.md'; Legacy = $null }
    References = [pscustomobject]@{ Asset = 'references.md'; Legacy = $null }
    Evidence = [pscustomobject]@{ Asset = 'evidence.md'; Legacy = 'evidence.md' }
    EvidenceWaivers = [pscustomobject]@{ Asset = 'evidence-waivers.json'; Legacy = 'evidence-waivers.json' }
    EvolutionLog = [pscustomobject]@{ Asset = 'evolution-log.md'; Legacy = 'evolution-log.md' }
    DecisionRecords = [pscustomobject]@{ Asset = 'decisions'; Legacy = 'decisions' }
    CrLog = [pscustomobject]@{ Asset = 'logs/cr-log.md'; Legacy = 'cr-log.md' }
    Learnings = [pscustomobject]@{ Asset = 'logs/learnings.md'; Legacy = 'learnings.md' }
    Capture = [pscustomobject]@{ Asset = 'logs/capture.md'; Legacy = 'capture.md' }
    Reviews = [pscustomobject]@{ Asset = 'reviews'; Legacy = 'reviews' }
}

function Normalize-PhysicalPathRoot {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not $IsWindows) {
        return $fullPath
    }

    $root = [System.IO.Path]::GetPathRoot($fullPath)
    return $root.ToUpperInvariant() + $fullPath.Substring($root.Length)
}

function Resolve-PhysicalRepoPath {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = Normalize-PhysicalPathRoot -Path $Path
    $root = [System.IO.Path]::GetPathRoot($fullPath)
    $relative = $fullPath.Substring($root.Length)
    $segments = @($relative -split '[\\/]' | Where-Object { $_.Length -gt 0 })
    $current = $root
    foreach ($segment in $segments) {
        $candidate = Join-Path $current $segment
        if (Test-Path -LiteralPath $candidate) {
            $children = @(Get-ChildItem -LiteralPath $current -Force)
            $matches = @($children | Where-Object {
                    [string]::Equals($_.Name, $segment, [System.StringComparison]::Ordinal)
                })
            if ($matches.Count -eq 0) {
                $matches = @($children | Where-Object {
                        [string]::Equals($_.Name, $segment, [System.StringComparison]::OrdinalIgnoreCase)
                    })
            }
            if ($matches.Count -ne 1) {
                throw "Cannot resolve a unique physical path component '$segment' under '$current'."
            }

            $item = $matches[0]
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                $target = $item.ResolveLinkTarget($true)
                if ($null -eq $target) {
                    throw "Cannot resolve reparse-point target '$candidate'."
                }
                $current = Normalize-PhysicalPathRoot -Path $target.FullName
                continue
            }
            $current = Normalize-PhysicalPathRoot -Path $item.FullName
            continue
        }
        $current = Normalize-PhysicalPathRoot -Path $candidate
    }
    return $current
}

function Assert-PhysicalPlanConfinement {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$PlanDir,
        [Parameter(Mandatory)][string]$Path
    )

    $physicalPlan = Resolve-PhysicalRepoPath -Path $PlanDir
    $physicalPath = Resolve-PhysicalRepoPath -Path $Path
    $prefix = $physicalPlan.TrimEnd([char[]]@(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar
        )) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $physicalPath.StartsWith($prefix, [System.StringComparison]::Ordinal)) {
        throw "Resolved plan asset path '$Path' escapes inventoried plan folder '$PlanDir' through a link or reparse point."
    }
}

function Initialize-PlanFileHandle {
    if ('SkalaryPlanFileHandle' -as [type]) {
        return
    }

    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

public static class SkalaryPlanFileHandle
{
    public sealed class Identity
    {
        public string Value { get; set; }
        public ulong LinkCount { get; set; }
        public bool IsRegular { get; set; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle handle,
        StringBuilder path,
        uint pathLength,
        uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle handle,
        out ByHandleFileInformation information);

    [DllImport("libc", SetLastError = true)]
    private static extern int fstat(int fd, byte[] buffer);

    private static void AssertSupportedArchitecture(OSPlatform platform, Architecture architecture)
    {
        var supported = platform == OSPlatform.Windows
            ? architecture == Architecture.X86 ||
                architecture == Architecture.X64 ||
                architecture == Architecture.Arm64
            : platform == OSPlatform.Linux &&
                (architecture == Architecture.X64 || architecture == Architecture.Arm64);
        if (!BitConverter.IsLittleEndian || !supported)
        {
            throw new PlatformNotSupportedException(
                "Opened plan-file identity is not supported on " + platform + "/" + architecture + ".");
        }
    }

    private static Identity ParseLinuxIdentity(byte[] stat, Architecture architecture)
    {
        if (stat == null || stat.Length < 28)
        {
            throw new IOException("Linux fstat buffer is shorter than the supported layout.");
        }

        var device = BitConverter.ToUInt64(stat, 0);
        var inode = BitConverter.ToUInt64(stat, 8);
        ulong links;
        uint mode;
        if (architecture == Architecture.X64)
        {
            links = BitConverter.ToUInt64(stat, 16);
            mode = BitConverter.ToUInt32(stat, 24);
        }
        else if (architecture == Architecture.Arm64)
        {
            mode = BitConverter.ToUInt32(stat, 16);
            links = BitConverter.ToUInt32(stat, 20);
        }
        else
        {
            throw new PlatformNotSupportedException("Linux fstat layout is unsupported on " + architecture + ".");
        }
        return new Identity
        {
            Value = "linux:" + device.ToString("x") + ":" + inode.ToString("x"),
            LinkCount = links,
            IsRegular = (mode & 0xF000) == 0x8000
        };
    }

    public static string GetPath(SafeFileHandle handle)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var path = new StringBuilder(512);
            var length = GetFinalPathNameByHandle(handle, path, (uint)path.Capacity, 0);
            if (length == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            if (length >= path.Capacity)
            {
                path = new StringBuilder((int)length + 1);
                length = GetFinalPathNameByHandle(handle, path, (uint)path.Capacity, 0);
                if (length == 0 || length >= path.Capacity)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }

            var value = path.ToString();
            if (value.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            {
                return @"\\" + value.Substring(8);
            }
            if (value.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
            {
                return value.Substring(4);
            }
            return value;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var fdPath = "/proc/self/fd/" + handle.DangerousGetHandle().ToInt64();
            var target = new FileInfo(fdPath).ResolveLinkTarget(true);
            if (target == null)
            {
                throw new IOException("Could not resolve the opened plan-file handle.");
            }
            return target.FullName;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            throw new PlatformNotSupportedException(
                "Opened plan-file handle validation is disabled on macOS because variadic F_GETPATH interop is not ABI-safe.");
        }

        throw new PlatformNotSupportedException(
            "Opened plan-file handle validation supports Windows and Linux only.");
    }

    public static Identity GetIdentity(SafeFileHandle handle)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            AssertSupportedArchitecture(OSPlatform.Windows, RuntimeInformation.ProcessArchitecture);
            ByHandleFileInformation information;
            if (!GetFileInformationByHandle(handle, out information))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            var fileIndex = ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow;
            return new Identity
            {
                Value = "windows:" + information.VolumeSerialNumber.ToString("x") + ":" + fileIndex.ToString("x"),
                LinkCount = information.NumberOfLinks,
                IsRegular = (information.FileAttributes & 0x10) == 0
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            AssertSupportedArchitecture(OSPlatform.Linux, RuntimeInformation.ProcessArchitecture);
        }
        else
        {
            throw new PlatformNotSupportedException(
                "Opened plan-file identity supports Windows and Linux only.");
        }

        var stat = new byte[256];
        if (fstat(handle.DangerousGetHandle().ToInt32(), stat) != 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return ParseLinuxIdentity(stat, RuntimeInformation.ProcessArchitecture);
        }
        throw new PlatformNotSupportedException(
            "Opened plan-file identity supports Windows and Linux only.");
    }
}
'@
}

function Test-PhysicalPathWithin {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Path,
        [switch]$AllowEqual
    )

    $comparison = if ($IsWindows) {
        [System.StringComparison]::OrdinalIgnoreCase
    }
    else {
        [System.StringComparison]::Ordinal
    }
    if ($AllowEqual -and [string]::Equals($Root, $Path, $comparison)) {
        return $true
    }
    $prefix = $Root.TrimEnd([char[]]@(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar
        )) + [System.IO.Path]::DirectorySeparatorChar
    return $Path.StartsWith($prefix, $comparison)
}

function New-PlanCorpusConfinementContext {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepoRoot)

    $logicalRepoPath = Normalize-PhysicalPathRoot -Path $RepoRoot
    $repoItem = Get-Item -LiteralPath $logicalRepoPath -Force -ErrorAction Stop
    if ($repoItem -isnot [System.IO.DirectoryInfo]) {
        throw "Repository root '$logicalRepoPath' is not a directory."
    }

    $physicalRepoPath = Resolve-PhysicalRepoPath -Path $logicalRepoPath
    $logicalPlansPath = Join-Path $logicalRepoPath 'docs/implementation-plans'
    $current = $logicalRepoPath
    foreach ($segment in @('docs', 'implementation-plans')) {
        $current = Join-Path $current $segment
        $item = Get-Item -LiteralPath $current -Force -ErrorAction Stop
        if ($item -isnot [System.IO.DirectoryInfo] -or
            ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Plan corpus ancestor '$current' is not a regular directory."
        }
    }

    $physicalPlansPath = Resolve-PhysicalRepoPath -Path $logicalPlansPath
    if (-not (Test-PhysicalPathWithin `
                -Root $physicalRepoPath `
                -Path $physicalPlansPath)) {
        throw "Physical plan corpus '$physicalPlansPath' escapes physical repository root '$physicalRepoPath'."
    }

    $context = [pscustomobject]@{
        RepoPath = $logicalRepoPath
        PhysicalRepoPath = $physicalRepoPath
        PlansPath = $logicalPlansPath
        PhysicalPlansPath = $physicalPlansPath
    }
    $context.PSObject.TypeNames.Insert(0, 'Skalary.PlanCorpusConfinementContext')
    return $context
}

function Assert-PlanCorpusConfinementContext {
    param([Parameter(Mandatory)][object]$Context)

    if ($Context.PSObject.TypeNames -cnotcontains 'Skalary.PlanCorpusConfinementContext' -or
        [string]::IsNullOrWhiteSpace([string]$Context.RepoPath) -or
        [string]::IsNullOrWhiteSpace([string]$Context.PhysicalRepoPath) -or
        [string]::IsNullOrWhiteSpace([string]$Context.PlansPath) -or
        [string]::IsNullOrWhiteSpace([string]$Context.PhysicalPlansPath)) {
        throw 'A PlanState-created plan corpus confinement context is required.'
    }
    if (-not (Test-PhysicalPathWithin `
                -Root $Context.PhysicalRepoPath `
                -Path $Context.PhysicalPlansPath)) {
        throw 'Plan corpus confinement context contains a physical plan root outside its physical repository root.'
    }
    $expectedPlansPath = Normalize-PhysicalPathRoot -Path (
        Join-Path $Context.RepoPath 'docs/implementation-plans'
    )
    $comparison = if ($IsWindows) {
        [System.StringComparison]::OrdinalIgnoreCase
    }
    else {
        [System.StringComparison]::Ordinal
    }
    if (-not [string]::Equals($expectedPlansPath, $Context.PlansPath, $comparison)) {
        throw 'Plan corpus confinement context contains an invalid logical plan root.'
    }
}

function New-PlanConfinementContext {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$PlanDir,
        [string]$RepoRoot,
        [object]$CorpusContext
    )

    $logicalPath = Normalize-PhysicalPathRoot -Path $PlanDir
    if ($null -eq $CorpusContext) {
        if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
            throw 'RepoRoot or a PlanState-created plan corpus confinement context is required.'
        }
        $CorpusContext = New-PlanCorpusConfinementContext -RepoRoot $RepoRoot
    }
    elseif (-not [string]::IsNullOrWhiteSpace($RepoRoot)) {
        throw 'Specify RepoRoot or CorpusContext, not both.'
    }
    Assert-PlanCorpusConfinementContext -Context $CorpusContext

    $relativePath = [System.IO.Path]::GetRelativePath($CorpusContext.PlansPath, $logicalPath)
    if ($relativePath -eq '..' -or
        $relativePath.StartsWith(
            '..' + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::Ordinal
        ) -or
        [System.IO.Path]::IsPathRooted($relativePath)) {
        throw "Plan folder '$logicalPath' escapes repository plan corpus '$($CorpusContext.PlansPath)'."
    }

    $current = $CorpusContext.PlansPath
    foreach ($segment in @($relativePath -split '[\\/]' | Where-Object { $_.Length -gt 0 })) {
        $current = Join-Path $current $segment
        $item = Get-Item -LiteralPath $current -Force -ErrorAction Stop
        if ($item -isnot [System.IO.DirectoryInfo] -or
            ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Plan corpus ancestor '$current' is not a regular directory."
        }
    }

    $physicalPlanPath = Resolve-PhysicalRepoPath -Path $logicalPath
    if (-not (Test-PhysicalPathWithin `
                -Root $CorpusContext.PhysicalPlansPath `
                -Path $physicalPlanPath)) {
        throw "Physical plan folder '$physicalPlanPath' escapes physical plan corpus '$($CorpusContext.PhysicalPlansPath)'."
    }
    $context = [pscustomobject]@{
        PlanPath = $logicalPath
        PhysicalPlanPath = $physicalPlanPath
        PlansPath = $CorpusContext.PlansPath
        PhysicalPlansPath = $CorpusContext.PhysicalPlansPath
        RepoPath = $CorpusContext.RepoPath
        PhysicalRepoPath = $CorpusContext.PhysicalRepoPath
    }
    $context.PSObject.TypeNames.Insert(0, 'Skalary.PlanConfinementContext')
    return $context
}

function Assert-PlanConfinementContext {
    param([Parameter(Mandatory)][object]$Context)

    if ($Context.PSObject.TypeNames -cnotcontains 'Skalary.PlanConfinementContext' -or
        [string]::IsNullOrWhiteSpace([string]$Context.PlanPath) -or
        [string]::IsNullOrWhiteSpace([string]$Context.PhysicalPlanPath) -or
        [string]::IsNullOrWhiteSpace([string]$Context.PhysicalPlansPath) -or
        [string]::IsNullOrWhiteSpace([string]$Context.PhysicalRepoPath)) {
        throw 'A PlanState-created plan confinement context is required.'
    }
    if (-not (Test-PhysicalPathWithin `
                -Root $Context.PhysicalRepoPath `
                -Path $Context.PhysicalPlansPath) -or
        -not (Test-PhysicalPathWithin `
                -Root $Context.PhysicalPlansPath `
                -Path $Context.PhysicalPlanPath)) {
        throw 'Plan confinement context contains a physical plan root outside its repository corpus.'
    }
}

function Assert-PhysicalPlanChildPath {
    param(
        [Parameter(Mandatory)][object]$Context,
        [Parameter(Mandatory)][string]$PhysicalPath,
        [Parameter(Mandatory)][string]$DisplayPath
    )

    Assert-PlanConfinementContext -Context $Context
    $prefix = $Context.PhysicalPlanPath.TrimEnd([char[]]@(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar
        )) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $PhysicalPath.StartsWith($prefix, [System.StringComparison]::Ordinal)) {
        throw "Resolved plan path '$DisplayPath' escapes canonical plan folder '$($Context.PlanPath)'."
    }
}

function Resolve-PhysicalPlanChildPath {
    param(
        [Parameter(Mandatory)][object]$Context,
        [Parameter(Mandatory)][string]$Path
    )

    Assert-PlanConfinementContext -Context $Context
    $fullPath = Normalize-PhysicalPathRoot -Path $Path
    $relativePath = [System.IO.Path]::GetRelativePath($Context.PlanPath, $fullPath)
    if ($relativePath -eq '..' -or
        $relativePath.StartsWith(
            '..' + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::Ordinal
        )) {
        throw "Resolved plan path '$Path' escapes canonical plan folder '$($Context.PlanPath)'."
    }

    $segments = @($relativePath -split '[\\/]' | Where-Object { $_.Length -gt 0 })
    $current = $Context.PhysicalPlanPath
    foreach ($segment in $segments) {
        $candidate = Join-Path $current $segment
        if (-not (Test-Path -LiteralPath $candidate)) {
            $current = Normalize-PhysicalPathRoot -Path $candidate
            continue
        }

        $children = @(Get-ChildItem -LiteralPath $current -Force)
        $matches = @($children | Where-Object {
                [string]::Equals($_.Name, $segment, [System.StringComparison]::Ordinal)
            })
        if ($matches.Count -eq 0) {
            $matches = @($children | Where-Object {
                    [string]::Equals($_.Name, $segment, [System.StringComparison]::OrdinalIgnoreCase)
                })
        }
        if ($matches.Count -ne 1) {
            throw "Cannot resolve a unique physical plan path component '$segment' under '$current'."
        }

        $item = $matches[0]
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            $target = $item.ResolveLinkTarget($true)
            if ($null -eq $target) {
                throw "Cannot resolve reparse-point target '$candidate'."
            }
            $current = Normalize-PhysicalPathRoot -Path $target.FullName
            continue
        }
        $current = Normalize-PhysicalPathRoot -Path $item.FullName
    }
    return $current
}

function Resolve-ConfinedPlanPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$Context,
        [Parameter(Mandatory)][string]$Path,
        [ValidateSet('Leaf', 'Container')][string]$PathType = 'Leaf'
    )

    Assert-PlanConfinementContext -Context $Context
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    $expectedType = if ($PathType -eq 'Leaf') { [System.IO.FileInfo] } else { [System.IO.DirectoryInfo] }
    if ($item -isnot $expectedType -or
        ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Resolved plan path '$Path' is not a regular $($PathType.ToLowerInvariant())."
    }

    $physicalPath = Resolve-PhysicalPlanChildPath -Context $Context -Path $item.FullName
    Assert-PhysicalPlanChildPath -Context $Context -PhysicalPath $physicalPath -DisplayPath $Path
    return [pscustomobject]@{
        Item = $item
        PhysicalPath = $physicalPath
    }
}

function Open-ConfinedPlanFileCore {
    param(
        [Parameter(Mandatory)][object]$Context,
        [Parameter(Mandatory)][string]$Path,
        [scriptblock]$BeforeFirstOpen,
        [scriptblock]$BeforeVerificationOpen
    )

    $validated = Resolve-ConfinedPlanPath -Context $Context -Path $Path -PathType Leaf
    if ($BeforeFirstOpen) {
        try {
            & $BeforeFirstOpen
        }
        catch {
            throw "Internal before-first-open test seam failed: $($_.Exception.Message)"
        }
    }

    Initialize-PlanFileHandle
    $stream = [System.IO.File]::Open(
        $validated.Item.FullName,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::Read
    )
    try {
        $handlePath = [SkalaryPlanFileHandle]::GetPath($stream.SafeFileHandle)
        $handlePhysicalPath = Normalize-PhysicalPathRoot -Path $handlePath
        Assert-PhysicalPlanChildPath `
            -Context $Context `
            -PhysicalPath $handlePhysicalPath `
            -DisplayPath $handlePath
        if (-not [string]::Equals(
                $handlePhysicalPath,
                $validated.PhysicalPath,
                [System.StringComparison]::Ordinal
            )) {
            throw "Opened plan-file handle '$handlePath' does not match the confined file that was validated."
        }

        $identity = [SkalaryPlanFileHandle]::GetIdentity($stream.SafeFileHandle)
        if (-not $identity.IsRegular -or $identity.LinkCount -ne 1) {
            throw "Opened plan file '$Path' is not a single-link regular file."
        }

        if ($BeforeVerificationOpen) {
            try {
                & $BeforeVerificationOpen
            }
            catch {
                throw "Internal before-verification-open test seam failed: $($_.Exception.Message)"
            }
        }
        $verification = [System.IO.File]::Open(
            $Path,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read,
            [System.IO.FileShare]::Read
        )
        try {
            $verificationPath = Normalize-PhysicalPathRoot -Path (
                [SkalaryPlanFileHandle]::GetPath($verification.SafeFileHandle)
            )
            $verificationIdentity = [SkalaryPlanFileHandle]::GetIdentity($verification.SafeFileHandle)
            if (-not $verificationIdentity.IsRegular -or
                $verificationIdentity.LinkCount -ne 1 -or
                -not [string]::Equals(
                    $verificationPath,
                    $handlePhysicalPath,
                    [System.StringComparison]::Ordinal
                ) -or
                -not [string]::Equals(
                    $verificationIdentity.Value,
                    $identity.Value,
                    [System.StringComparison]::Ordinal
                ) -or
                $verification.Length -ne $stream.Length) {
                throw "Plan file '$Path' changed while its stable read handle was opened."
            }
        }
        finally {
            $verification.Dispose()
        }

        return $stream
    }
    catch {
        $stream.Dispose()
        throw
    }
}

function Open-ConfinedPlanFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$Context,
        [Parameter(Mandatory)][string]$Path
    )

    return Open-ConfinedPlanFileCore -Context $Context -Path $Path
}

function Get-PlanLayout {
    <#
    .SYNOPSIS
    Reports whether a plan folder uses the `plan.md` + `assets/` layout or the legacy flat layout.

    .DESCRIPTION
    The layout is anchored on `assets/requirements.md` — the one asset every plan has and that both
    `New-Plan` (scaffold) and migration write. Keying off mere `assets/` directory presence would flip a
    legacy plan the moment it grew any `assets/` subfolder, orphaning the logs and receipt already written
    at the plan root. An `assets/` folder that holds no recognized section file therefore stays `legacy`,
    which resolves per section rather than switching the whole plan into a broken mode.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$PlanDir
    )

    $anchor = Join-Path (Join-Path $PlanDir 'assets') 'requirements.md'
    if (Test-Path -LiteralPath $anchor -PathType Leaf) {
        return 'assets'
    }

    return 'legacy'
}

function Resolve-PlanAssetPath {
    <#
    .SYNOPSIS
    Resolves the on-disk path of a plan asset for the plan folder's layout.

    .DESCRIPTION
    Single source of truth so plan writers and readers
    (harvest, archival gate) never disagree about where logs and receipts live. Sections that only ever
    exist as assets (requirements/risks/decisions/references) always resolve under `assets/`; their legacy
    form lives inside `plan.md`, not in a sibling file.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$PlanDir,

        [Parameter(Mandatory)]
        [ValidateSet('Intent', 'Domain', 'Design', 'Requirements', 'Risks', 'Decisions', 'References', 'Evidence', 'EvidenceWaivers', 'EvolutionLog', 'DecisionRecords', 'CrLog', 'Learnings', 'Capture', 'Reviews')]
        [string]$Kind,

        [ValidateSet('assets', 'legacy')]
        [string]$Layout,

        [string]$RepoRoot,

        [object[]]$Inventory
    )

    $planDirFull = [System.IO.Path]::GetFullPath($PlanDir)
    $planConfinementContext = $null
    if ($RepoRoot) {
        $repoRootFull = [System.IO.Path]::GetFullPath($RepoRoot)
        $corpusContext = New-PlanCorpusConfinementContext -RepoRoot $repoRootFull
        if (-not (Test-PhysicalPathWithin `
                    -Root $corpusContext.PlansPath `
                    -Path $planDirFull)) {
            throw "Plan folder '$planDirFull' escapes repository plan corpus '$($corpusContext.PlansPath)'."
        }

        if (-not $PSBoundParameters.ContainsKey('Inventory')) {
            $Inventory = @(Get-PlanInventory -RepoRoot $repoRootFull)
        }
        $comparison = if ($IsWindows) {
            [System.StringComparison]::OrdinalIgnoreCase
        }
        else {
            [System.StringComparison]::Ordinal
        }
        $inventoryMatch = @($Inventory | Where-Object {
                $_.Path -and [string]::Equals(
                    (Normalize-PhysicalPathRoot -Path ([string]$_.Path)),
                    $planDirFull,
                    $comparison
                )
            })
        if ($inventoryMatch.Count -ne 1) {
            throw "Plan folder '$planDirFull' is not a unique member of the repository plan inventory."
        }

        $planConfinementContext = New-PlanConfinementContext `
            -PlanDir $planDirFull `
            -CorpusContext $corpusContext
        $planDirFull = $planConfinementContext.PlanPath
    }

    if (-not $Layout) {
        $Layout = Get-PlanLayout -PlanDir $planDirFull
    }

    $entry = $script:PlanAssetMap[$Kind]
    if (-not $entry.Legacy) {
        # Section assets have no sibling-file legacy form — their legacy home is inside plan.md.
        $resolvedPath = [System.IO.Path]::GetFullPath((Join-Path $planDirFull (Join-Path 'assets' $entry.Asset)))
        if ($RepoRoot) {
            $physicalPath = Resolve-PhysicalPlanChildPath `
                -Context $planConfinementContext `
                -Path $resolvedPath
            Assert-PhysicalPlanChildPath `
                -Context $planConfinementContext `
                -PhysicalPath $physicalPath `
                -DisplayPath $resolvedPath
        }
        return $resolvedPath
    }

    $assetPath = [System.IO.Path]::GetFullPath((Join-Path $planDirFull (Join-Path 'assets' $entry.Asset)))
    $legacyPath = [System.IO.Path]::GetFullPath((Join-Path $planDirFull $entry.Legacy))

    # Fail loud on genuine split-brain: the same logical file existing at both locations means writers and
    # readers have already diverged, and silently preferring one would orphan real content.
    if ((Test-Path -LiteralPath $assetPath) -and (Test-Path -LiteralPath $legacyPath)) {
        throw "Plan folder '$planDirFull' holds '$Kind' at both '$assetPath' and '$legacyPath'. Move the legacy copy under assets/ so writers and readers cannot disagree."
    }

    $resolvedPath = if ($Layout -eq 'assets') { $assetPath } else { $legacyPath }
    if ($RepoRoot) {
        $physicalPath = Resolve-PhysicalPlanChildPath `
            -Context $planConfinementContext `
            -Path $resolvedPath
        Assert-PhysicalPlanChildPath `
            -Context $planConfinementContext `
            -PhysicalPath $physicalPath `
            -DisplayPath $resolvedPath
    }
    return $resolvedPath
}

function Get-PlanSectionRecord {
    <#
    .SYNOPSIS
    Normalizes a plan section's lines into a comparable record set (not raw text), so divergence checks
    ignore cosmetic drift such as column padding, heading prose, blank lines, and row order.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [AllowEmptyString()]
        [AllowNull()]
        [string[]]$Lines,

        [Parameter(Mandatory)]
        [ValidateSet('Table', 'List')]
        [string]$Kind,

        [string]$IdPattern,

        [int]$MinCell = 2
    )

    $records = [System.Collections.Generic.List[string]]::new()
    foreach ($line in @($Lines)) {
        if ($null -eq $line) { continue }
        $trimmed = $line.Trim()

        if ($Kind -eq 'Table') {
            if (-not $trimmed.StartsWith('|')) { continue }
            $cells = Split-MarkdownTableCells -Row $trimmed
            # Match the consumer parsers' minimum column count exactly: a row they would discard must not
            # count as a well-formed record here, or a short-columned asset resolves to zero requirements.
            if ($cells.Count -lt $MinCell) { continue }
            if ($IdPattern -and $cells[0] -notmatch $IdPattern) { continue }
            $normalizedCells = @($cells | ForEach-Object { ($_ -replace '\s+', ' ').Trim() })
            $records.Add(($normalizedCells -join ' | '))
            continue
        }

        if ($trimmed -notmatch '^[-*]\s+') { continue }
        $body = (($trimmed -replace '^[-*]\s+', '') -replace '\s+', ' ').Trim()
        if (-not [string]::IsNullOrWhiteSpace($body)) {
            $records.Add($body)
        }
    }

    return , $records.ToArray()
}

function Get-PlanInlineSectionLine {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string]$Content,

        [Parameter(Mandatory)]
        [ValidateSet('Requirements', 'Risks', 'Decisions')]
        [string]$Section,

        [switch]$PreserveFencedContent
    )

    $rawLines = ($Content -replace "`r`n", "`n").Split("`n")
    $lines = Remove-FencedCodeBlocks -Lines $rawLines
    $sectionLines = [System.Collections.Generic.List[string]]::new()
    $inSection = $false
    for ($index = 0; $index -lt $lines.Count; $index++) {
        $line = $lines[$index]
        if ($line -match "^\s*##\s+$([regex]::Escape($Section))\b") {
            $inSection = $true
            continue
        }
        if ($inSection -and $line -match '^\s*##\s+') {
            break
        }
        if ($inSection) {
            $sectionLines.Add($(if ($PreserveFencedContent) { $rawLines[$index] } else { $line }))
        }
    }
    return , $sectionLines.ToArray()
}

function Resolve-PlanSection {
    <#
    .SYNOPSIS
    Resolves one plan section (Requirements / Risks / Decisions) from either `assets/<section>.md` or the
    legacy in-`plan.md` table, per the documented state table.

    .DESCRIPTION
    | Asset absent                          | fall back to the in-plan.md section |
    | Asset present, non-empty, well-formed | asset wins |
    | Asset present but empty or malformed  | fail loud (never silently resolve to zero records) |
    | Both present                          | asset wins; divergence over the normalized record set errors |

    Fenced code blocks are stripped from asset files exactly as they are from `plan.md`.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$PlanDir,

        [Parameter(Mandatory)]
        [ValidateSet('Requirements', 'Risks', 'Decisions')]
        [string]$Section,

        [AllowEmptyCollection()]
        [AllowEmptyString()]
        [AllowNull()]
        [string[]]$LegacyLine,

        [AllowEmptyString()]
        [AllowNull()]
        [string]$LegacyContent,

        [AllowEmptyString()]
        [AllowNull()]
        [string]$AssetContent,

        [AllowNull()]
        [string]$AssetPath,

        [switch]$AssetAbsent
    )

    $kind = if ($Section -eq 'Decisions') { 'List' } else { 'Table' }
    $idPattern = switch ($Section) {
        'Requirements' { '^REQ-\d+$' }
        'Risks' { '^RISK-\d+$' }
        default { $null }
    }
    # Mirror the consumer parsers' column requirements so "well-formed" here means "parseable there".
    $minCell = switch ($Section) {
        'Requirements' { 4 }
        default { 2 }
    }

    if ($PSBoundParameters.ContainsKey('LegacyLine') -and $PSBoundParameters.ContainsKey('LegacyContent')) {
        throw 'Resolve-PlanSection accepts LegacyLine or LegacyContent, not both.'
    }
    $legacyLines = if ($PSBoundParameters.ContainsKey('LegacyContent')) {
        @(Get-PlanInlineSectionLine -Content $LegacyContent -Section $Section)
    }
    else {
        @($LegacyLine)
    }
    $legacyRecords = Get-PlanSectionRecord -Lines $legacyLines -Kind $kind -IdPattern $idPattern -MinCell $minCell

    if ($PSBoundParameters.ContainsKey('AssetContent') -and $AssetAbsent) {
        throw 'Resolve-PlanSection accepts AssetContent or AssetAbsent, not both.'
    }
    $assetPath = if ($PSBoundParameters.ContainsKey('AssetPath')) {
        $AssetPath
    }
    else {
        Resolve-PlanAssetPath -PlanDir $PlanDir -Kind $Section
    }
    $assetPresent = if ($PSBoundParameters.ContainsKey('AssetContent')) {
        $true
    }
    elseif ($AssetAbsent) {
        $false
    }
    else {
        Test-Path -LiteralPath $assetPath -PathType Leaf
    }
    if (-not $assetPresent) {
        $source = if ($legacyRecords.Count -gt 0) { 'legacy' } else { 'none' }
        return [pscustomobject]@{
            Section = $Section
            Source = $source
            Path = $null
            Lines = $legacyLines
            Records = $legacyRecords
        }
    }

    $raw = if ($PSBoundParameters.ContainsKey('AssetContent')) {
        $AssetContent
    }
    else {
        Get-Content -LiteralPath $assetPath -Raw
    }
    if ([string]::IsNullOrWhiteSpace($raw)) {
        throw "Plan asset '$assetPath' is present but empty; $Section must never silently resolve to zero records. Author the file or delete it to fall back to plan.md."
    }

    $assetLines = Remove-FencedCodeBlocks -Lines (($raw -replace "`r`n", "`n").Split("`n"))
    $assetRecords = Get-PlanSectionRecord -Lines $assetLines -Kind $kind -IdPattern $idPattern -MinCell $minCell
    if ($assetRecords.Count -eq 0) {
        $expected = if ($kind -eq 'Table') { "table rows with at least $minCell columns whose first cell matches $idPattern" } else { 'bullet list entries' }
        throw "Plan asset '$assetPath' is malformed: no $Section records found (expected $expected)."
    }

    if ($legacyRecords.Count -gt 0) {
        $assetKey = (@($assetRecords) | Sort-Object) -join "`n"
        $legacyKey = (@($legacyRecords) | Sort-Object) -join "`n"
        if ($assetKey -cne $legacyKey) {
            throw "Plan asset '$assetPath' diverges from the legacy '## $Section' section in plan.md. Remove the legacy table (the asset is authoritative) or reconcile the two."
        }
    }

    return [pscustomobject]@{
        Section = $Section
        Source = 'asset'
        Path = $assetPath
        Lines = $assetLines
        Records = $assetRecords
    }
}

function ConvertFrom-PlanRequirementLine {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [AllowEmptyString()]
        [AllowNull()]
        [string[]]$Lines
    )

    $requirements = @{}
    foreach ($line in @($Lines)) {
        if ($null -eq $line -or -not $line.Trim().StartsWith('|')) { continue }
        $cells = Split-MarkdownTableCells -Row $line
        if ($cells.Count -lt 4 -or $cells[0] -eq 'ID' -or $cells[0] -eq '----') {
            continue
        }

        if ($cells[0] -match '^REQ-(?<id>\d+)$') {
            $requirements[$cells[0]] = [pscustomobject]@{
                Id = $cells[0]
                Number = [int]$Matches.id
                Text = $cells[1]
                AcceptanceCriteria = $cells[2]
                Steps = $cells[3]
            }
        }
    }

    return $requirements
}

function ConvertFrom-PlanRiskLine {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [AllowEmptyString()]
        [AllowNull()]
        [string[]]$Lines
    )

    $risks = @{}
    foreach ($line in @($Lines)) {
        if ($null -eq $line -or -not $line.Trim().StartsWith('|')) { continue }
        $cells = Split-MarkdownTableCells -Row $line
        if ($cells.Count -lt 2 -or $cells[0] -eq 'ID' -or $cells[0] -eq '----') {
            continue
        }

        if ($cells[0] -match '^RISK-(?<id>\d+)$') {
            $risks[$cells[0]] = [int]$Matches.id
        }
    }

    return $risks
}

function Get-PlanMetadata {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$RepoRoot
    )

    $fullPath = (Resolve-Path -LiteralPath $Path).Path
    $planDir = Split-Path -Parent $fullPath
    $repoRootPath = [System.IO.Path]::GetFullPath($RepoRoot)
    $content = Get-Content -LiteralPath $fullPath -Raw
    $normalized = $content -replace "`r`n", "`n"
    $allLines = $normalized.Split("`n")
    $lines = Remove-FencedCodeBlocks -Lines $allLines

    $currentPhase = ''
    $currentSection = $null
    $phaseSteps = @{}
    $sectionLines = @{
        Requirements = [System.Collections.Generic.List[string]]::new()
        Risks = [System.Collections.Generic.List[string]]::new()
        Decisions = [System.Collections.Generic.List[string]]::new()
    }
    $steps = [System.Collections.Generic.List[object]]::new()
    $warnings = [System.Collections.Generic.List[string]]::new()

    # `<details>` blocks under a step (the `@human` handoff detail) are captured here rather than
    # re-parsed by consumers, so the validator gate and the /ci handoff read the same text. Detail text
    # is taken from $allLines, not the fence-stripped $lines, because handoffs routinely contain fenced
    # operator commands and a stripped block would silently lose exactly the part a human needs.
    # Every `<details>` block under a step is collected: plans also carry Result/evidence blocks, and
    # keying on the first block (or on its <summary> text) would misread those as the handoff.
    $currentStep = $null
    $detailBlocks = $null
    $detailLines = $null
    $detailDepth = 0

    $flushDetail = {
        if ($currentStep) {
            if ($null -ne $detailLines -and $detailLines.Count -gt 0 -and $null -ne $detailBlocks) {
                # An unterminated block still carries the operator's text; keep it so the gate reports the
                # missing sections instead of a misleading "no details block at all".
                $detailBlocks.Add(($detailLines -join "`n"))
            }
            if ($null -ne $detailBlocks -and $detailBlocks.Count -gt 0) {
                $currentStep.Detail = ($detailBlocks -join "`n`n")
            }
        }
        $currentStep = $null
        $detailBlocks = $null
        $detailLines = $null
        $detailDepth = 0
    }

    for ($lineIndex = 0; $lineIndex -lt $lines.Length; $lineIndex++) {
        $line = $lines[$lineIndex]
        $rawLine = if ($lineIndex -lt $allLines.Length) { $allLines[$lineIndex] } else { $line }

        if ($line -match '^\s*##\s+(?<section>Requirements|Risks|Decisions)\b') {
            $currentSection = [string]$Matches.section
            . $flushDetail
            continue
        }

        if ($line -match '^\s*##\s+') {
            $currentSection = $null
            $currentPhase = $line.Trim()
            if ($currentPhase -match '^##\s+Phase\s+\d+:\s+') {
                $phaseSteps[$currentPhase] = [System.Collections.Generic.List[object]]::new()
            }
            . $flushDetail
            continue
        }

        if ($currentSection) {
            $sectionLines[$currentSection].Add($line)
            if ($line.Trim().StartsWith('|')) { continue }
        }

        # Inside an open block every line belongs to it, including step-shaped lines in example markup —
        # except an unindented step line, which is structural: an unterminated block must not silently
        # swallow the rest of the plan's steps. Depth is counted on the fence-stripped line so a fenced
        # example cannot close the real block, while the captured text comes from the raw line so fenced
        # operator commands survive.
        if ($currentStep -and $detailDepth -gt 0 -and $line -notmatch '^-\s\[[ x~]\]\s+\d+\.\d+[a-z]?\s') {
            $detailLines.Add($rawLine)
            $detailDepth += ([regex]::Matches($line, '<details\b')).Count
            $detailDepth -= ([regex]::Matches($line, '</details>')).Count
            if ($detailDepth -le 0) {
                $detailBlocks.Add(($detailLines -join "`n"))
                $detailLines = $null
                $detailDepth = 0
            }
            continue
        }

        if ($line -match '^\s*-\s\[(?<status>[ x~])\]\s+(?<step>\d+\.\d+[a-z]?)\s+(?<body>.+)$') {
            $stepId = $Matches.step
            $status = $Matches.status
            $body = $Matches.body
            $size = ''
            if ($body -match '`(?<size>[SML])`') {
                $size = [string]$Matches.size
            }

            $role = 'ai-agent'
            if ($body -match '\s@(?<role>human|ai-agent)\b') {
                $role = [string]$Matches.role
            }

            $afterIds = @()
            if ($body -match '\[after:\s*(?<after>[^\]]+)\]') {
                $afterList = $Matches.after.Split(',')
                foreach ($after in $afterList) {
                    $candidate = $after.Trim()
                    if (-not [string]::IsNullOrWhiteSpace($candidate)) {
                        $afterIds += , $candidate
                    }
                }
            }

            $refs = @()
            $parenMatches = [regex]::Matches($body, '\((?<refs>[^)]*)\)')
            foreach ($parenMatch in $parenMatches) {
                $candidateRefs = @($parenMatch.Groups['refs'].Value.Split(',') | ForEach-Object { $_.Trim() })
                foreach ($candidateRef in $candidateRefs) {
                    if ($candidateRef -match '^(?:REQ|RISK)-\d+$' -and $refs -notcontains $candidateRef) {
                        $refs += $candidateRef
                    }
                }
            }

            $step = [pscustomobject]@{
                Id = $stepId
                Status = $status
                Body = $body
                Role = $role
                Size = $size
                After = $afterIds
                Refs = $refs
                Phase = $currentPhase
                Detail = ''
            }

            . $flushDetail
            $steps.Add($step)
            if ($phaseSteps.ContainsKey($currentPhase)) {
                $phaseSteps[$currentPhase].Add($step)
            }
            $currentStep = $step
            $detailBlocks = [System.Collections.Generic.List[string]]::new()
            continue
        }

        if ($currentStep -and $line -match '<details\b') {
            $detailLines = [System.Collections.Generic.List[string]]::new()
            $detailLines.Add($rawLine)
            $detailDepth = ([regex]::Matches($line, '<details\b')).Count - ([regex]::Matches($line, '</details>')).Count
            if ($detailDepth -le 0) {
                $detailBlocks.Add(($detailLines -join "`n"))
                $detailLines = $null
                $detailDepth = 0
            }
        }
    }

    . $flushDetail

    $resolvedSections = [ordered]@{}
    foreach ($section in @('Requirements', 'Risks', 'Decisions')) {
        $resolvedSections[$section] = Resolve-PlanSection -PlanDir $planDir -Section $section -LegacyLine $sectionLines[$section].ToArray()
    }

    $requirements = ConvertFrom-PlanRequirementLine -Lines $resolvedSections['Requirements'].Lines
    $risks = ConvertFrom-PlanRiskLine -Lines $resolvedSections['Risks'].Lines

    $sectionSources = [ordered]@{}
    foreach ($section in $resolvedSections.Keys) {
        $sectionSources[$section] = $resolvedSections[$section].Source
    }

    $sizeBytes = [System.Text.Encoding]::UTF8.GetByteCount($content)
    if ($sizeBytes -ge 20480 -or $allLines.Length -ge 400) {
        $warnings.Add("Plan size warning: ${sizeBytes} bytes / $($allLines.Length) lines (warn threshold 20KB/400).")
    }
    if ($sizeBytes -ge 35840 -or $allLines.Length -ge 700) {
        $warnings.Add("Plan size warning: ${sizeBytes} bytes / $($allLines.Length) lines (block threshold 35KB/700 is advisory in this validator).")
    }

    # Archived plans are immutable historical records: they are still parsed and validated, but drafting
    # gates introduced after they were written must not retroactively fail them. Derived from the plan's
    # own resolved path (an `archived` folder directly under `implementation-plans`) rather than from
    # $RepoRoot, which may be relative and would then resolve against the wrong base.
    $isArchived = $false
    $ancestor = $planDir
    while ($ancestor) {
        if ([System.IO.Path]::GetFileName($ancestor) -eq 'archived') {
            $parentName = [System.IO.Path]::GetFileName((Split-Path -Parent $ancestor))
            if ($parentName -eq 'implementation-plans') {
                $isArchived = $true
                break
            }
        }
        $ancestor = Split-Path -Parent $ancestor
    }

    return [pscustomobject]@{
        PlanPath = $fullPath
        PlanDir = $planDir
        Layout = (Get-PlanLayout -PlanDir $planDir)
        IsArchived = $isArchived
        RepoRoot = $repoRootPath
        Content = $content
        Lines = $lines
        AllLines = $allLines
        Requirements = $requirements
        Risks = $risks
        Decisions = @($resolvedSections['Decisions'].Records)
        # The resolved section objects themselves (Source/Path/Lines/Records) so cross-plan readers such as
        # Get-PlanIndex see exactly what the resolver saw, instead of re-deciding layout for themselves.
        Sections = $resolvedSections
        SectionSources = $sectionSources
        Steps = @($steps)
        PhaseSteps = $phaseSteps
        Warnings = $warnings
    }
}

function ConvertFrom-PlanFolderName {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$FolderName
    )

    if ($FolderName -match '^(?:(?<prefix>standalone|[0-9a-f]{6})-)?(?<date>\d{4}-\d{2}-\d{2})-(?<hash>[0-9a-f]{6})-(?<slug>.+)$') {
        return [pscustomobject]@{
            Scheme = 'new'
            FolderId = $Matches.hash
            FolderPrefix = if ($Matches.ContainsKey('prefix')) {
                $Matches.prefix.ToLowerInvariant()
            }
            else {
                $null
            }
            Slug = $Matches.slug
            Date = $Matches.date
        }
    }
    if ($FolderName -match '^(?<num>\d{3})-(?<slug>.+)$') {
        return [pscustomobject]@{
            Scheme = 'legacy'
            FolderId = $Matches.num
            FolderPrefix = $null
            Slug = $Matches.slug
            Date = $null
        }
    }
    return $null
}

function Get-PlanInventory {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$RepoRoot,

        [string[]]$CanonicalIdFilter
    )

    $root = [System.IO.Path]::GetFullPath($RepoRoot)
    $plansRoot = Join-Path $root 'docs/implementation-plans'
    $inventory = [System.Collections.Generic.List[object]]::new()
    if (-not (Test-Path -LiteralPath $plansRoot)) {
        return $inventory.ToArray()
    }

    $archivedRoot = Join-Path $plansRoot 'archived'
    $folders = @()
    $folders += Get-ChildItem -LiteralPath $plansRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ne 'archived' } |
        ForEach-Object { [pscustomobject]@{ Dir = $_; IsArchived = $false } }
    if (Test-Path -LiteralPath $archivedRoot) {
        $folders += Get-ChildItem -LiteralPath $archivedRoot -Directory -ErrorAction SilentlyContinue |
            ForEach-Object { [pscustomobject]@{ Dir = $_; IsArchived = $true } }
    }

    $requestedIds = $null
    if ($PSBoundParameters.ContainsKey('CanonicalIdFilter')) {
        $requestedIds = [System.Collections.Generic.HashSet[string]]::new(
            [string[]]$CanonicalIdFilter,
            [System.StringComparer]::Ordinal
        )
    }
    foreach ($entry in $folders) {
        $name = $entry.Dir.Name
        $parsedFolder = ConvertFrom-PlanFolderName -FolderName $name
        if ($null -eq $parsedFolder) {
            continue
        }
        if ($null -ne $requestedIds -and -not $requestedIds.Contains($parsedFolder.FolderId)) {
            continue
        }

        $planFile = Join-Path $entry.Dir.FullName 'plan.md'
        if (-not (Test-Path -LiteralPath $planFile -PathType Leaf)) {
            continue
        }
        $anchorId = $null
        $epicId = $null
        $raw = Get-Content -LiteralPath $planFile -Raw
        if ($raw -match '<!--\s*plan-id:\s*(?<id>[0-9a-fA-F]{3,})\s*-->') {
            $anchorId = $Matches.id.ToLowerInvariant()
        }
        # Epic membership is carried by the child plan, never by the epic's own table: the marker
        # travels with the plan folder (including into archived/), so a rollup cannot go stale.
        # It is read through the same header-scoped view every other marker uses, so a plan that
        # merely documents the marker in its body is not silently enrolled in that epic.
        $headerEpicId = (Get-PlanHeaderMarkers -Content $raw).EpicId
        if ($headerEpicId -and $headerEpicId -match '^[0-9a-fA-F]{3,}$') {
            $epicId = $headerEpicId.ToLowerInvariant()
        }

        $canonicalId = if ($anchorId) { $anchorId } else { $parsedFolder.FolderId }

        $inventory.Add([pscustomobject]@{
                Id = $canonicalId
                FolderId = $parsedFolder.FolderId
                FolderPrefix = $parsedFolder.FolderPrefix
                AnchorId = $anchorId
                EpicId = $epicId
                Scheme = $parsedFolder.Scheme
                Slug = $parsedFolder.Slug
                Date = $parsedFolder.Date
                FolderName = $name
                Path = $entry.Dir.FullName
                IsArchived = $entry.IsArchived
            })
    }

    return $inventory.ToArray()
}

function New-PlanHexId {
    [CmdletBinding()]
    param()

    $bytes = [byte[]]::new(3)
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    return (($bytes | ForEach-Object { $_.ToString('x2') }) -join '')
}

function New-PlanId {
    [CmdletBinding()]
    param(
        [string]$RepoRoot,

        [string[]]$ExistingId,

        [scriptblock]$HexProvider = { New-PlanHexId },

        [int]$MaxAttempts = 1000
    )

    if (-not $PSBoundParameters.ContainsKey('ExistingId')) {
        if (-not $RepoRoot) {
            throw 'New-PlanId requires -RepoRoot when -ExistingId is not supplied.'
        }
        $ExistingId = @(Get-PlanInventory -RepoRoot $RepoRoot | ForEach-Object { $_.Id })
    }

    $existingSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($id in $ExistingId) {
        if (-not [string]::IsNullOrWhiteSpace($id)) {
            [void]$existingSet.Add($id.Trim().ToLowerInvariant())
        }
    }

    $candidate = $null
    for ($attempt = 0; $attempt -lt $MaxAttempts; $attempt++) {
        $generated = (& $HexProvider)
        if ($generated -isnot [string]) { $generated = [string]$generated }
        $generated = $generated.Trim().ToLowerInvariant()
        if ($generated -notmatch '^[0-9a-f]{6}$') {
            throw "New-PlanId generated an invalid id '$generated' (expected 6 hex chars)."
        }
        if (-not $existingSet.Contains($generated)) {
            $candidate = $generated
            break
        }
    }

    if (-not $candidate) {
        throw "New-PlanId could not generate a unique id after $MaxAttempts attempts."
    }

    $prefix = $candidate.Substring(0, 4)
    foreach ($existing in $existingSet) {
        if ($existing.Length -ge 4 -and $existing.Substring(0, 4) -eq $prefix) {
            Write-Warning "Plan id '$candidate' is not uniquely addressable at the 4-char prefix '$prefix' (collides with existing '$existing')."
            break
        }
    }

    return $candidate
}

function Resolve-Plan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Reference,

        [Parameter(Mandatory)]
        [string]$RepoRoot,

        [object[]]$Inventory
    )

    if ([string]::IsNullOrWhiteSpace($Reference)) {
        throw 'Resolve-Plan requires a non-empty -Reference.'
    }

    if (-not $PSBoundParameters.ContainsKey('Inventory')) {
        $Inventory = @(Get-PlanInventory -RepoRoot $RepoRoot)
    }

    $ref = $Reference.Trim()
    $refLower = $ref.ToLowerInvariant()
    $matches = @()
    $kind = $null

    if ($ref -match '^\d{4}-\d{2}-\d{2}$') {
        $kind = "date '$ref'"
        $matches = @($Inventory | Where-Object { $_.Date -eq $ref })
    }
    elseif ($ref -match '^\d{3}$') {
        $kind = "legacy number '$ref'"
        $matches = @($Inventory | Where-Object { $_.Scheme -eq 'legacy' -and $_.Id -eq $ref })
    }
    elseif ($refLower -match '^[0-9a-f]{4,6}$') {
        $kind = "hash prefix '$ref'"
        $matches = @($Inventory | Where-Object { $_.Id -and $_.Id.ToLowerInvariant().StartsWith($refLower) })
        if ($matches.Count -eq 0) {
            $kind = "slug '$ref'"
            $matches = @($Inventory | Where-Object { $_.Slug -eq $ref })
        }
    }
    else {
        $kind = "slug '$ref'"
        $matches = @($Inventory | Where-Object { $_.Slug -eq $ref })
        if ($matches.Count -eq 0) {
            $matches = @($Inventory | Where-Object { $_.Slug -and $_.Slug.ToLowerInvariant().Contains($refLower) })
        }
    }

    if ($matches.Count -eq 0) {
        throw "No plan matches $kind."
    }

    if ($matches.Count -gt 1) {
        $detail = ($matches | ForEach-Object { "$($_.Id) ($($_.FolderName))" }) -join ', '
        throw "Ambiguous plan reference: $kind matches multiple plans: $detail. Use a longer prefix or the full id."
    }

    return $matches[0]
}

function Get-EpicInventory {
    <#
    .SYNOPSIS
    Enumerates active and archived epic folders.

    .DESCRIPTION
    An epic is an index, not a plan: it holds `epic.md` and no `plan.md`, and its children stay ordinary
    sibling plan folders so every existing consumer keeps resolving them unchanged. Epic folders never
    carry the navigational plan prefix: their name remains `<yyyy-mm-dd>-<6hex>-<slug>`. The
    `<!-- epic-id: ... -->` anchor in `epic.md` is canonical when present, exactly as `plan-id` is for
    plans. Active indexes live directly under `epics/`; completed indexes live under
    `archived/epics/` and remain resolvable historical records.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$RepoRoot
    )

    $root = [System.IO.Path]::GetFullPath($RepoRoot)
    $plansRoot = Join-Path $root 'docs/implementation-plans'
    $epicsRoot = Join-Path $plansRoot 'epics'
    $archivedEpicsRoot = Join-Path $plansRoot 'archived/epics'
    $inventory = [System.Collections.Generic.List[object]]::new()
    $folders = @()
    if (Test-Path -LiteralPath $epicsRoot -PathType Container) {
        $folders += Get-ChildItem -LiteralPath $epicsRoot -Directory -ErrorAction SilentlyContinue |
            ForEach-Object { [pscustomobject]@{ Dir = $_; IsArchived = $false } }
    }
    if (Test-Path -LiteralPath $archivedEpicsRoot -PathType Container) {
        $folders += Get-ChildItem -LiteralPath $archivedEpicsRoot -Directory -ErrorAction SilentlyContinue |
            ForEach-Object { [pscustomobject]@{ Dir = $_; IsArchived = $true } }
    }

    foreach ($entry in ($folders | Sort-Object @{ Expression = { $_.Dir.Name } }, IsArchived)) {
        $dir = $entry.Dir
        if ($dir.Name -notmatch '^(?<date>\d{4}-\d{2}-\d{2})-(?<hash>[0-9a-f]{6})-(?<slug>.+)$') {
            continue
        }

        $folderId = $Matches.hash
        $slug = $Matches.slug
        $date = $Matches.date

        $epicFile = Join-Path $dir.FullName 'epic.md'
        $anchorId = $null
        $title = $null
        if (Test-Path -LiteralPath $epicFile -PathType Leaf) {
            $raw = Get-Content -LiteralPath $epicFile -Raw
            if ($raw -match '<!--\s*epic-id:\s*(?<id>[0-9a-fA-F]{3,})\s*-->') {
                $anchorId = $Matches.id.ToLowerInvariant()
            }
            if ($raw -match '(?m)^#\s+(?<title>.+?)\s*$') {
                $title = $Matches.title.Trim()
            }
        }

        $inventory.Add([pscustomobject]@{
                Id = if ($anchorId) { $anchorId } else { $folderId }
                FolderId = $folderId
                AnchorId = $anchorId
                Slug = $slug
                Date = $date
                Title = $title
                FolderName = $dir.Name
                Path = $dir.FullName
                EpicFile = $epicFile
                IsArchived = [bool]$entry.IsArchived
            })
    }

    return $inventory.ToArray()
}

function Resolve-Epic {
    <#
    .SYNOPSIS
    Resolves an epic reference (hash prefix, slug, or date) to exactly one epic record.

    .DESCRIPTION
    Same contract as `Resolve-Plan`: ambiguity is an error, never a silent first-match pick.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Reference,

        [Parameter(Mandatory)]
        [string]$RepoRoot,

        [object[]]$Inventory
    )

    if ([string]::IsNullOrWhiteSpace($Reference)) {
        throw 'Resolve-Epic requires a non-empty -Reference.'
    }

    if (-not $PSBoundParameters.ContainsKey('Inventory')) {
        $Inventory = @(Get-EpicInventory -RepoRoot $RepoRoot)
    }

    $ref = $Reference.Trim()
    $refLower = $ref.ToLowerInvariant()
    $epicMatches = @()
    $kind = $null

    if ($ref -match '^\d{4}-\d{2}-\d{2}$') {
        $kind = "date '$ref'"
        $epicMatches = @($Inventory | Where-Object { $_.Date -eq $ref })
    }
    elseif ($refLower -match '^[0-9a-f]{4,6}$') {
        $kind = "hash prefix '$ref'"
        $epicMatches = @($Inventory | Where-Object { $_.Id -and $_.Id.ToLowerInvariant().StartsWith($refLower) })
        if ($epicMatches.Count -eq 0) {
            $kind = "slug '$ref'"
            $epicMatches = @($Inventory | Where-Object { $_.Slug -eq $ref })
        }
    }
    else {
        $kind = "slug '$ref'"
        $epicMatches = @($Inventory | Where-Object { $_.Slug -eq $ref })
        if ($epicMatches.Count -eq 0) {
            $epicMatches = @($Inventory | Where-Object { $_.Slug -and $_.Slug.ToLowerInvariant().Contains($refLower) })
        }
    }

    if ($epicMatches.Count -eq 0) {
        throw "No epic matches $kind."
    }

    if ($epicMatches.Count -gt 1) {
        $detail = ($epicMatches | ForEach-Object { "$($_.Id) ($($_.FolderName))" }) -join ', '
        throw "Ambiguous epic reference: $kind matches multiple epics: $detail. Use a longer prefix or the full id."
    }

    return $epicMatches[0]
}

# The lifecycle stages a plan's `<!-- cip-stage: ... -->` anchor may carry, lowest first. The set is
# closed on purpose: a reader that treats "not `drafted`" as "skip validation" turns any typo (`draftd`)
# into a silent pass that exits 0 while checking nothing (RISK-6). Ordering lives here once so writers
# (`Set-PlanStage`) and readers (`Validate-Plan`) cannot disagree about what a stage means.
#
# `dr-round` is a family rather than a single value: design-review rounds are numbered and open-ended,
# but every round ranks at the same point in the lifecycle — after drafting, before completion.
$script:PlanStageOrder = @('scaffolded', 'drafted', 'dr-round', 'done')

# A plan with no anchor at all predates the anchor (RISK-7). It resolves to `drafted` so an older plan
# keeps being validated exactly as it is today, rather than silently dropping out of validation.
$script:PlanStageDefault = 'drafted'

# The stage a plan must reach before its content is worth validating. Below it a plan is a scaffold of
# placeholders, so validating it would keep the test command red for the whole drafting session.
$script:PlanValidationFloor = 'drafted'

function Get-PlanStageOrder {
    <#
    .SYNOPSIS
    The ordered, closed set of plan lifecycle stage families, lowest first.
    #>
    [CmdletBinding()]
    [OutputType([string[]])]
    param()

    return [string[]]$script:PlanStageOrder
}

function Resolve-PlanStage {
    <#
    .SYNOPSIS
    Resolves a `cip-stage` anchor value to its family and rank in the closed stage order.

    .DESCRIPTION
    Throws on anything outside the closed set — that loud failure is the whole point, because the
    alternative is an unrecognised stage quietly disabling every downstream check.

    A null or whitespace value is not an unrecognised stage: it is a plan written before the anchor
    existed, and resolves to the default (`drafted`) with `IsDefaulted` set so a caller can tell the two
    apart.
    #>
    [CmdletBinding()]
    param(
        [AllowNull()]
        [AllowEmptyString()]
        [string]$Stage
    )

    $isDefaulted = [string]::IsNullOrWhiteSpace($Stage)
    $value = if ($isDefaulted) { $script:PlanStageDefault } else { $Stage.Trim().ToLowerInvariant() }

    $family = $null
    $round = $null
    if ($value -match '^dr-round-(?<n>[1-9][0-9]*)$') {
        $family = 'dr-round'
        $round = [int]$Matches['n']
    }
    elseif ($value -ne 'dr-round') {
        # A bare `dr-round` names the family, not a stage: a plan under review is always in a numbered
        # round, so the unnumbered form is as much a typo as `draftd` and is rejected the same way.
        $family = $value
    }

    $rank = if ($family) { [array]::IndexOf([string[]]$script:PlanStageOrder, $family) } else { -1 }
    if ($rank -lt 0) {
        $known = (@('scaffolded', 'drafted', 'dr-round-<n>', 'done')) -join ', '
        throw "Unrecognised plan stage '$Stage'. Known stages, in order: $known."
    }

    return [pscustomobject]@{
        Stage = $value
        Family = $family
        Round = $round
        Rank = $rank
        IsDefaulted = $isDefaulted
    }
}

function Test-PlanStageAtLeast {
    <#
    .SYNOPSIS
    True when $Stage ranks at or above $Minimum in the closed stage order.

    .DESCRIPTION
    `-Stage` is an anchor value read from a plan and is resolved strictly, so a bad marker fails loudly.
    `-Minimum` is a caller-supplied floor and is ranked against the family list directly, so every value
    `Get-PlanStageOrder` publishes — including the bare family name `dr-round` — is a usable floor. A bad
    floor is the caller's bug and says so, rather than blaming the plan under validation.
    #>
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [AllowNull()]
        [AllowEmptyString()]
        [string]$Stage,

        [Parameter(Mandatory)]
        [string]$Minimum
    )

    $floorValue = $Minimum.Trim().ToLowerInvariant()
    $floorRank = [array]::IndexOf([string[]]$script:PlanStageOrder, $floorValue)
    if ($floorRank -lt 0) {
        if ($floorValue -match '^dr-round-[1-9][0-9]*$') {
            $floorRank = [array]::IndexOf([string[]]$script:PlanStageOrder, 'dr-round')
        }
        else {
            throw "Unknown stage floor '$Minimum'. Use one of: $(($script:PlanStageOrder) -join ', ')."
        }
    }

    return (Resolve-PlanStage -Stage $Stage).Rank -ge $floorRank
}

function Get-PlanValidationDecision {
    <#
    .SYNOPSIS
    Decides whether a plan file is far enough along to be worth validating, and says so out loud.

    .DESCRIPTION
    One home for the floor and for the signal printed by the explicit working-plan validator and direct
    full-repository validation. Keeping the decision shared prevents independently invoked routes from
    disagreeing about whether a plan is below the validation floor.

    An unrecognised stage propagates as a throw, with the offending plan named: a stage nobody recognises
    must never resolve to "skip", which is the failure the closed set exists to prevent.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $markers = Get-PlanHeaderMarkers -Path $Path
    try {
        $stage = Resolve-PlanStage -Stage $markers.CipStage
    }
    catch {
        throw "$($_.Exception.Message) Plan: $Path"
    }

    $shouldValidate = Test-PlanStageAtLeast -Stage $stage.Stage -Minimum $script:PlanValidationFloor
    $signal = if ($shouldValidate) {
        "PLAN-VALIDATION: VALIDATING stage=$($stage.Stage) plan=$Path"
    }
    else {
        "PLAN-VALIDATION: SKIPPED stage=$($stage.Stage) floor=$($script:PlanValidationFloor) plan=$Path"
    }

    return [pscustomobject]@{
        Path = $Path
        Stage = $stage.Stage
        Floor = $script:PlanValidationFloor
        ShouldValidate = $shouldValidate
        Signal = $signal
    }
}

function Assert-IntentReady {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Planning context asset not found: $Path"
    }
    $intent = (Get-Content -LiteralPath $Path -Raw) -replace "`r`n", "`n"
    foreach ($section in @('Goal', 'Desired outcome', 'Success signals', 'Non-goals', 'Definition of done')) {
        $body = [regex]::Match(
            $intent,
            "(?ms)^##\s+$([regex]::Escape($section))\s*`$(?<body>.*?)(?=^##\s|\z)"
        ).Groups['body'].Value
        if ([string]::IsNullOrWhiteSpace($body) -or $body -match '(?i)\bTBD\b') {
            throw "Intent section '$section' is missing or still contains a TBD placeholder."
        }
    }
}

function Get-PlanningContextDigest {
    <#
        .SYNOPSIS
        Returns the digest bound to confirmed intent, requirements, risks, and decisions.
        #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$PlanDir,

        [string]$RepoRoot,

        [object[]]$Inventory
    )

    $assetArgs = @{ PlanDir = $PlanDir }
    if ($RepoRoot) { $assetArgs.RepoRoot = $RepoRoot }
    if ($PSBoundParameters.ContainsKey('Inventory')) { $assetArgs.Inventory = $Inventory }
    $paths = [ordered]@{}
    foreach ($kind in @('Intent', 'Requirements', 'Risks', 'Decisions')) {
        $paths[$kind] = Resolve-PlanAssetPath @assetArgs -Kind $kind
    }
    foreach ($path in $paths.Values) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Planning context asset not found: $path"
        }
    }

    $parts = foreach ($kind in $paths.Keys) {
        $content = (Get-Content -LiteralPath $paths[$kind] -Raw) -replace "`r`n", "`n"
        "--$($kind.ToLowerInvariant()).md--`n$content"
    }
    $payload = $parts -join "`n"
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
    return [System.Convert]::ToHexString(
        [System.Security.Cryptography.SHA256]::HashData($bytes)
    ).ToLowerInvariant()
}

function Assert-PlanningContextReady {
    <#
        .SYNOPSIS
        Fails when the Markdown context is still a scaffold rather than confirmable planning input.
        #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$PlanDir,

        [string]$RepoRoot,

        [object[]]$Inventory
    )

    $assetArgs = @{ PlanDir = $PlanDir }
    if ($RepoRoot) { $assetArgs.RepoRoot = $RepoRoot }
    if ($PSBoundParameters.ContainsKey('Inventory')) { $assetArgs.Inventory = $Inventory }
    $intentPath = Resolve-PlanAssetPath @assetArgs -Kind Intent
    $designPath = Resolve-PlanAssetPath @assetArgs -Kind Design
    foreach ($path in @($intentPath, $designPath)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Planning context asset not found: $path"
        }
    }

    Assert-IntentReady -Path $intentPath

    $design = (Get-Content -LiteralPath $designPath -Raw) -replace "`r`n", "`n"
    foreach ($section in @('Components and boundaries', 'Program flow')) {
        $body = [regex]::Match(
            $design,
            "(?ms)^##\s+$([regex]::Escape($section))\s*`$(?<body>.*?)(?=^##\s|\z)"
        ).Groups['body'].Value
        if ([string]::IsNullOrWhiteSpace($body) -or $body -match '(?i)\bTBD\b') {
            throw "Design section '$section' is missing or still contains a TBD placeholder."
        }
        if ($section -eq 'Program flow') {
            $diagram = [regex]::Match($body, '(?ms)```mermaid[^\S\r\n]*\r?\n(?<body>.*?)```')
            if (-not $diagram.Success -or
                [string]::IsNullOrWhiteSpace($diagram.Groups['body'].Value)) {
                throw "Design section 'Program flow' must contain a non-empty Mermaid diagram."
            }
        }
    }
}

function Get-PlanningContextState {
    <#
        .SYNOPSIS
        Reports whether an enrolled plan's operator confirmation still matches its intent and design.
        #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$PlanDir,

        [string]$RepoRoot,

        [object[]]$Inventory
    )

    $planFile = Join-Path $PlanDir 'plan.md'
    if (-not (Test-Path -LiteralPath $planFile -PathType Leaf)) {
        throw "Plan file not found: $planFile"
    }

        $markers = Get-PlanHeaderMarkers -Path $planFile
        $marker = $markers.PlanningConfirmed
        if (-not $markers.All.Contains('planning-confirmed')) {
            return [pscustomobject]@{
                IsEnrolled  = $false
                Status      = 'legacy'
                IsConfirmed = $true
                CanProceed  = $true
                Marker      = $null
                Digest      = $null
                Reason      = $null
            }
        }

        if ([string]::IsNullOrWhiteSpace($marker)) {
            return [pscustomobject]@{
                IsEnrolled  = $true
                Status      = 'invalid'
                IsConfirmed = $false
                CanProceed  = $false
                Marker      = $marker
                Digest      = $null
                Reason      = 'The planning-confirmed marker is empty.'
            }
        }

        $normalized = $marker.Trim().ToLowerInvariant()
        if ($normalized -eq 'pending') {
            return [pscustomobject]@{
                IsEnrolled  = $true
                Status      = 'pending'
                IsConfirmed = $false
                CanProceed  = $false
                Marker      = $normalized
                Digest      = $null
                Reason      = 'Planning confirmation is pending.'
            }
        }

        if ($normalized -notmatch '^sha256:(?<digest>[0-9a-f]{64})$') {
            return [pscustomobject]@{
                IsEnrolled  = $true
                Status      = 'invalid'
                IsConfirmed = $false
                CanProceed  = $false
                Marker      = $normalized
                Digest      = $null
                Reason      = 'The planning-confirmed marker is malformed.'
            }
        }
        $expectedDigest = $Matches['digest']

        try {
            $assetArgs = @{ PlanDir = $PlanDir }
            if ($RepoRoot) { $assetArgs.RepoRoot = $RepoRoot }
            if ($PSBoundParameters.ContainsKey('Inventory')) { $assetArgs.Inventory = $Inventory }
            $digest = Get-PlanningContextDigest @assetArgs
        }
        catch {
            return [pscustomobject]@{
                IsEnrolled  = $true
                Status      = 'missing'
                IsConfirmed = $false
                CanProceed  = $false
                Marker      = $normalized
                Digest      = $null
                Reason      = $_.Exception.Message
            }
        }

        $confirmed = [string]::Equals($expectedDigest, $digest, [System.StringComparison]::Ordinal)
        return [pscustomobject]@{
            IsEnrolled  = $true
            Status      = if ($confirmed) { 'confirmed' } else { 'stale' }
            IsConfirmed = $confirmed
            CanProceed  = $confirmed
            Marker      = $normalized
            Digest      = $digest
            Reason      = if ($confirmed) { $null } else { 'Planning context changed after confirmation.' }
        }
    }

function Split-PlanHeader {
    <#
    .SYNOPSIS
    Splits plan content into the header region and everything below it.

    .DESCRIPTION
    The header is the run of lines above the first `##` heading, and it is the only region a marker
    anchor may live in. The boundary is defined here once because readers and writers have to agree on
    it: `Set-PlanStage` used to match anchors over the whole file while `Get-PlanHeaderMarkers` read
    only the header, so a step description quoting an anchor captured the write and the header kept
    none — a lost write that still reported success.

    `Header` and `Body` are the two line runs joined back with newlines, so `Header` + newline + `Body`
    reproduces the input whenever `HasBody` is true.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string]$Content
    )

    $normalized = ($Content ?? '') -replace "`r`n", "`n"
    $lines = $normalized.Split("`n")

    $boundary = 0
    while ($boundary -lt $lines.Count -and $lines[$boundary] -notmatch '^##\s') { $boundary++ }

    $headerLines = if ($boundary -gt 0) { $lines[0..($boundary - 1)] } else { @() }
    $bodyLines = if ($boundary -lt $lines.Count) { $lines[$boundary..($lines.Count - 1)] } else { @() }

    return [pscustomobject]@{
        Header = (@($headerLines) -join "`n")
        Body = (@($bodyLines) -join "`n")
        HeaderLineCount = @($headerLines).Count
        HasBody = @($bodyLines).Count -gt 0
    }
}

function Get-PlanHeaderMarkers {
    [CmdletBinding(DefaultParameterSetName = 'Path')]
    param(
        [Parameter(Mandatory, ParameterSetName = 'Path')]
        [string]$Path,

        [Parameter(Mandatory, ParameterSetName = 'Content')]
        [AllowEmptyString()]
        [string]$Content
    )

    if ($PSCmdlet.ParameterSetName -eq 'Path') {
        $Content = Get-Content -LiteralPath (Resolve-Path -LiteralPath $Path).Path -Raw
    }

    $header = (Split-PlanHeader -Content ($Content ?? '')).Header

    $all = [ordered]@{}
    foreach ($match in [regex]::Matches($header, '<!--\s*(?<key>[A-Za-z][\w-]*)\s*:\s*(?<value>.*?)\s*-->')) {
        $key = $match.Groups['key'].Value.ToLowerInvariant()
        if (-not $all.Contains($key)) {
            $all[$key] = $match.Groups['value'].Value.Trim()
        }
    }
    if (-not $all.Contains('planning-confirmed') -and
        $header -match '<!--\s*planning-confirmed(?![\w-])') {
        $all['planning-confirmed'] = ''
    }

    $getValue = { param($k) if ($all.Contains($k)) { $all[$k] } else { $null } }

    $dependsOn = @()
    $dependsRaw = & $getValue 'depends-on'
    if ($dependsRaw) {
        $dependsOn = @($dependsRaw.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    }

    return [pscustomobject]@{
        PlanId = & $getValue 'plan-id'
        EpicId = & $getValue 'epic'
        ExecutionMode = & $getValue 'execution-mode'
        Scope = & $getValue 'scope'
        CipStage = & $getValue 'cip-stage'
        PlanningConfirmed = & $getValue 'planning-confirmed'
        DependsOn = $dependsOn
        All = $all
    }
}

function Get-PlanProgress {
    [CmdletBinding(DefaultParameterSetName = 'Path')]
    param(
        [Parameter(Mandatory, ParameterSetName = 'Metadata')]
        [object]$Metadata,

        [Parameter(Mandatory, ParameterSetName = 'Path')]
        [string]$Path,

        [Parameter(Mandatory, ParameterSetName = 'Path')]
        [string]$RepoRoot
    )

    if ($PSCmdlet.ParameterSetName -eq 'Path') {
        $Metadata = Get-PlanMetadata -Path $Path -RepoRoot $RepoRoot
    }

    $steps = @($Metadata.Steps)
    $total = $steps.Count
    $completed = @($steps | Where-Object { $_.Status -eq 'x' }).Count
    $inProgress = @($steps | Where-Object { $_.Status -eq '~' }).Count
    $pending = @($steps | Where-Object { $_.Status -eq ' ' }).Count

    $lastCompleted = $null
    foreach ($step in $steps) {
        if ($step.Status -eq 'x') { $lastCompleted = $step.Id }
    }

    $currentPhase = $null
    foreach ($step in $steps) {
        if ($step.Status -ne 'x') { $currentPhase = $step.Phase; break }
    }
    if (-not $currentPhase -and $total -gt 0) {
        $currentPhase = $steps[$total - 1].Phase
    }

    $percent = if ($total -gt 0) { [math]::Round(($completed / $total) * 100, 1) } else { 0 }

    return [pscustomobject]@{
        Total = $total
        Completed = $completed
        InProgress = $inProgress
        Pending = $pending
        Percent = $percent
        CurrentPhase = $currentPhase
        LastCompleted = $lastCompleted
        IsComplete = ($total -gt 0 -and $completed -eq $total)
    }
}

function Get-NextStep {
    [CmdletBinding(DefaultParameterSetName = 'Metadata')]
    param(
        [Parameter(Mandatory, ParameterSetName = 'Metadata')]
        [object]$Metadata,

        [Parameter(Mandatory, ParameterSetName = 'Path')]
        [string]$Path,

        [Parameter(Mandatory, ParameterSetName = 'Path')]
        [string]$RepoRoot,

        [switch]$HasUncommittedChanges
    )

    if ($PSCmdlet.ParameterSetName -eq 'Path') {
        $Metadata = Get-PlanMetadata -Path $Path -RepoRoot $RepoRoot
    }

    $steps = @($Metadata.Steps)
    $completed = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($step in $steps) {
        if ($step.Status -eq 'x') { [void]$completed.Add($step.Id) }
    }

    $next = $null
    foreach ($step in $steps) {
        if ($step.Status -ne 'x') { $next = $step; break }
    }

    if (-not $next) {
        return [pscustomobject]@{
            Step = $null
            Id = $null
            Status = $null
            IsHuman = $false
            IsDiscovery = $false
            Detail = ''
            HasUncommittedChanges = [bool]$HasUncommittedChanges
            BlockedByAfter = $false
            UnmetAfter = @()
            IsComplete = $true
        }
    }

    $unmet = @()
    foreach ($afterId in @($next.After)) {
        if (-not $completed.Contains($afterId)) { $unmet += , $afterId }
    }

    $isDiscovery = ($next.Body -match '\[discovery\]')
    $detail = if ($next.PSObject.Properties['Detail']) { [string]$next.Detail } else { '' }

    return [pscustomobject]@{
        Step = $next
        Id = $next.Id
        Status = $next.Status
        IsHuman = ($next.Role -eq 'human')
        IsDiscovery = [bool]$isDiscovery
        Detail = $detail
        HasUncommittedChanges = [bool]$HasUncommittedChanges
        BlockedByAfter = ($unmet.Count -gt 0)
        UnmetAfter = $unmet
        IsComplete = $false
    }
}

function Get-PhaseAdmission {
    <#
    .SYNOPSIS
    Computes the closed, read-only admission decision for the first incomplete plan phase.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$Plan,
        [Parameter(Mandatory)][object]$Metadata,
        [Parameter(Mandatory)][object]$Markers,
        [Parameter(Mandatory)][object]$NextStep,
        [Parameter(Mandatory)][object]$PlanningContext,
        [Parameter(Mandatory)][object[]]$Inventory,
        [Parameter(Mandatory)][string]$RepoRoot
    )

    $status = 'ready'
    $reason = ''
    $unmetDependencies = [System.Collections.Generic.List[string]]::new()

    foreach ($token in @($Markers.DependsOn)) {
        try {
            $dependency = Resolve-Plan -Reference $token -RepoRoot $RepoRoot -Inventory $Inventory
            $dependencyPlanPath = Join-Path $dependency.Path 'plan.md'
            if (-not (Test-Path -LiteralPath $dependencyPlanPath -PathType Leaf)) {
                throw "Dependency plan file not found: $dependencyPlanPath"
            }
            $dependencyMetadata = Get-PlanMetadata `
                -Path $dependencyPlanPath -RepoRoot $RepoRoot
            $dependencyProgress = Get-PlanProgress -Metadata $dependencyMetadata
            if (-not ($dependency.IsArchived -or $dependencyProgress.IsComplete)) {
                $unmetDependencies.Add([string]$dependency.Id)
            }
        }
        catch {
            $message = $_.Exception.Message
            $status = if ($message.StartsWith('Ambiguous plan reference:', [System.StringComparison]::Ordinal)) {
                'ambiguous'
            }
            else {
                'missing'
            }
            $reason = "Dependency '$token' could not be resolved: $message"
            break
        }
    }

    if ($status -eq 'ready' -and $unmetDependencies.Count -gt 0) {
        $status = 'blocked'
        $reason = "Incomplete dependencies: $($unmetDependencies -join ', ')."
    }

    $phase = if ($NextStep.Step) { [string]$NextStep.Step.Phase } else { '' }
    $phaseNumber = 0
    if ($status -eq 'ready') {
        if ($NextStep.IsComplete -or [string]::IsNullOrWhiteSpace($phase) -or
            $phase -notmatch '^##\s+Phase\s+(?<phase>[1-9][0-9]*):') {
            $status = 'blocked'
            $reason = 'No admissible incomplete phase was found.'
        }
        else {
            $phaseNumber = [int]$Matches.phase
        }
    }

    if ($status -eq 'ready') {
        $incompleteEarlier = @($Metadata.Steps | Where-Object {
                $_.Phase -match '^##\s+Phase\s+(?<phase>[1-9][0-9]*):' -and
                [int]$Matches.phase -lt $phaseNumber -and $_.Status -ne 'x'
            })
        if ($incompleteEarlier.Count -gt 0) {
            $status = 'blocked'
            $reason = "Earlier phase step '$($incompleteEarlier[0].Id)' is incomplete."
        }
        elseif ($NextStep.BlockedByAfter) {
            $status = 'blocked'
            $reason = "Step '$($NextStep.Id)' has unmet prerequisites: $(@($NextStep.UnmetAfter) -join ', ')."
        }
    }

    if ($status -eq 'ready' -and -not $PlanningContext.CanProceed) {
        $status = if ($PlanningContext.Status -eq 'stale') { 'stale-input' } else { 'missing' }
        $contextReason = if ($PlanningContext.PSObject.Properties['Reason'] -and
            -not [string]::IsNullOrWhiteSpace([string]$PlanningContext.Reason)) {
            " $($PlanningContext.Reason)"
        }
        else { '' }
        $reason = "Planning context is '$($PlanningContext.Status)'.$contextReason"
    }

    if ($status -eq 'ready' -and $PlanningContext.Status -eq 'legacy') {
        try {
            $intentPath = Resolve-PlanAssetPath -PlanDir $Plan.Path -Kind Intent `
                -RepoRoot $RepoRoot -Inventory $Inventory
            Assert-IntentReady -Path $intentPath
        }
        catch {
            $status = 'missing'
            $reason = $_.Exception.Message
        }
    }

    $applicableRequirements = @(
        if ($phase -and $Metadata.PhaseSteps.ContainsKey($phase)) {
            $Metadata.PhaseSteps[$phase] |
                ForEach-Object { $_.Refs } |
                Where-Object { $_ -match '^REQ-\d+$' } |
                Sort-Object -Unique
        }
    )
    $unknownRequirements = @(
        $applicableRequirements | Where-Object { -not $Metadata.Requirements.ContainsKey($_) }
    )
    if ($status -eq 'ready' -and $unknownRequirements.Count -gt 0) {
        $status = 'missing'
        $reason = "Phase $phaseNumber references unknown requirements: $($unknownRequirements -join ', ')."
    }

    return [pscustomobject]@{
        Status = $status
        CanProceed = ($status -eq 'ready')
        Reason = $reason
        Phase = $phase
        PhaseNumber = $phaseNumber
        ApplicableRequirements = $applicableRequirements
        UnknownRequirements = $unknownRequirements
        UnmetDependencies = $unmetDependencies.ToArray()
    }
}

function Get-PhaseCheckpointOptions {
    <#
    .SYNOPSIS
    Returns the operator dispositions allowed by normalized phase evidence and uncertainty.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()]
        [ValidateSet('passed', 'failed', 'skipped', 'unrun', 'stale', 'degraded', 'waived')]
        [string[]]$EvidenceStatus,
        [switch]$HasHighImpactUncertainty
    )

    $canContinue = $EvidenceStatus.Count -gt 0 -and
        @($EvidenceStatus | Where-Object { $_ -notin @('passed', 'waived') }).Count -eq 0 -and
        -not $HasHighImpactUncertainty
    return [pscustomobject]@{
        CanContinue = $canContinue
        Options = if ($canContinue) { @('Continue', 'Revise', 'Stop') } else { @('Revise', 'Stop') }
    }
}

function Get-EpicRollup {
    <#
    .SYNOPSIS
    Rolls child-plan progress up to the epic and selects the next unblocked child.

    .DESCRIPTION
    Membership comes from the `<!-- epic: <id> -->` header marker in each child plan (active and
    archived), ordering from each child's `<!-- depends-on: ... -->` marker. A child counts as complete
    when every step is `[x]` or the plan has been archived — archival is the terminal state of the
    workflow, so a dependent must not stay blocked behind it.

    Dependency resolution is fail-closed: a `depends-on` token that resolves to no plan, or to more than
    one, blocks the child and is reported rather than silently ignored.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$EpicId,

        [Parameter(Mandatory)]
        [string]$RepoRoot,

        [object[]]$Inventory
    )

    $repoRootPath = [System.IO.Path]::GetFullPath($RepoRoot)
    if (-not $PSBoundParameters.ContainsKey('Inventory')) {
        $Inventory = @(Get-PlanInventory -RepoRoot $repoRootPath)
    }

    $epicIdLower = $EpicId.Trim().ToLowerInvariant()
    $members = @($Inventory |
            Where-Object { $_.EpicId -and $_.EpicId.ToLowerInvariant() -eq $epicIdLower } |
            Sort-Object @{ Expression = { $_.Date } }, @{ Expression = { $_.Id } })

    $records = [System.Collections.Generic.List[object]]::new()
    # Completion is cached per plan id because a `depends-on` may point at a plan outside the epic; the
    # dependency's own state decides, never its membership.
    $completionCache = @{}

    foreach ($member in $members) {
        $planFile = Join-Path $member.Path 'plan.md'
        $metadata = Get-PlanMetadata -Path $planFile -RepoRoot $repoRootPath
        $progress = Get-PlanProgress -Metadata $metadata
        $markers = Get-PlanHeaderMarkers -Path $planFile
        $next = Get-NextStep -Metadata $metadata
        $isComplete = ($progress.IsComplete -or $member.IsArchived)
        $completionCache[$member.Id.ToLowerInvariant()] = $isComplete

        $records.Add([pscustomobject]@{
                Id = $member.Id
                Slug = $member.Slug
                FolderName = $member.FolderName
                Path = $member.Path
                PlanFile = $planFile
                IsArchived = $member.IsArchived
                IsComplete = $isComplete
                DependsOn = @($markers.DependsOn)
                Progress = $progress
                NextStepId = $next.Id
                NextStepHuman = $next.IsHuman
            })
    }

    $resolved = [System.Collections.Generic.List[object]]::new()
    foreach ($record in $records) {
        $unmet = [System.Collections.Generic.List[string]]::new()
        $unknown = [System.Collections.Generic.List[string]]::new()
        foreach ($token in $record.DependsOn) {
            $dependency = $null
            try {
                $dependency = Resolve-Plan -Reference $token -RepoRoot $repoRootPath -Inventory $Inventory
            }
            catch {
                $unknown.Add($token)
                continue
            }
            $dependencyKey = $dependency.Id.ToLowerInvariant()
            if (-not $completionCache.ContainsKey($dependencyKey)) {
                $dependencyPlanFile = Join-Path $dependency.Path 'plan.md'
                if (-not (Test-Path -LiteralPath $dependencyPlanFile -PathType Leaf)) {
                    $unknown.Add($token)
                    continue
                }
                $dependencyProgress = Get-PlanProgress -Metadata (Get-PlanMetadata -Path $dependencyPlanFile -RepoRoot $repoRootPath)
                $completionCache[$dependencyKey] = ($dependencyProgress.IsComplete -or $dependency.IsArchived)
            }
            if (-not $completionCache[$dependencyKey]) {
                $unmet.Add($dependency.Id)
            }
        }

        $resolved.Add([pscustomobject]@{
                Id = $record.Id
                Slug = $record.Slug
                FolderName = $record.FolderName
                Path = $record.Path
                PlanFile = $record.PlanFile
                IsArchived = $record.IsArchived
                IsComplete = $record.IsComplete
                DependsOn = @($record.DependsOn)
                UnmetDependsOn = $unmet.ToArray()
                UnknownDependsOn = $unknown.ToArray()
                IsBlocked = (-not $record.IsComplete -and ($unmet.Count -gt 0 -or $unknown.Count -gt 0))
                Progress = $record.Progress
                NextStepId = $record.NextStepId
                NextStepHuman = $record.NextStepHuman
            })
    }

    $nextChild = $null
    foreach ($record in $resolved) {
        if (-not $record.IsComplete -and -not $record.IsBlocked) { $nextChild = $record; break }
    }

    $totalSteps = 0
    $completedSteps = 0
    foreach ($record in $resolved) {
        $totalSteps += $record.Progress.Total
        $completedSteps += $record.Progress.Completed
    }

    return [pscustomobject]@{
        EpicId = $epicIdLower
        Children = $resolved.ToArray()
        ChildCount = $resolved.Count
        CompleteCount = @($resolved | Where-Object { $_.IsComplete }).Count
        BlockedCount = @($resolved | Where-Object { $_.IsBlocked }).Count
        TotalSteps = $totalSteps
        CompletedSteps = $completedSteps
        Percent = if ($totalSteps -gt 0) { [math]::Round(($completedSteps / $totalSteps) * 100, 1) } else { 0 }
        NextChild = $nextChild
        IsComplete = ($resolved.Count -gt 0 -and @($resolved | Where-Object { $_.IsComplete }).Count -eq $resolved.Count)
    }
}

function Get-TypedEvidenceMarkers {
    <#
    .SYNOPSIS
    Extracts the closed-vocabulary typed evidence markers from an acceptance-criteria cell.

    .DESCRIPTION
    The closed vocabulary is `test:<TestId>`, `file:<path>#<assertion>`, and `review:cr|dr`.
    Pure string parsing (no execution). Every marker-shaped occurrence is tokenized independently;
    an unknown prefix is surfaced verbatim even when known markers share its segment, so retired
    markers cannot hide behind a recognized token and false-green.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$AcceptanceCriteria
    )

    $markers = [System.Collections.Generic.List[string]]::new()
    $nonEvidencePrefixes = @(
        'after', 'cip-stage', 'contains', 'depends-on', 'epic', 'evidence',
        'execution-mode', 'expected-packages', 'phase-budget-points', 'plan-id',
        'scope', 'sev', 'src', 'status', 'trigger'
    )
    $segments = $AcceptanceCriteria.Split('·')
    foreach ($segment in $segments) {
        $trimmed = $segment.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed)) {
            continue
        }

        $candidates = [System.Collections.Generic.List[object]]::new()
        $quotedSpans = [System.Collections.Generic.List[object]]::new()
        foreach ($quoted in [regex]::Matches($trimmed, '`(?<value>[^`]+)`')) {
            $quotedSpans.Add([pscustomobject]@{ Index = $quoted.Index; End = $quoted.Index + $quoted.Length })
            $value = $quoted.Groups['value'].Value.Trim()
            if ($value.StartsWith('file:')) {
                if ($value -eq 'file:') { continue }
                $candidates.Add([pscustomobject]@{ Index = $quoted.Index; Value = $value })
                continue
            }
            foreach ($inner in [regex]::Matches($value, '(?<![A-Za-z0-9_-])[a-z][a-z-]*:[^\s|·]+')) {
                $candidates.Add([pscustomobject]@{
                        Index = $quoted.Index + 1 + $inner.Index
                        Value = $inner.Value
                    })
            }
        }

        foreach ($unquoted in [regex]::Matches($trimmed, '(?<![A-Za-z0-9_-])[a-z][a-z-]*:[^\s`|·]+')) {
            $insideQuoted = $false
            foreach ($span in $quotedSpans) {
                if ($unquoted.Index -ge $span.Index -and $unquoted.Index -lt $span.End) {
                    $insideQuoted = $true
                    break
                }
            }
            if (-not $insideQuoted) {
                $candidates.Add([pscustomobject]@{ Index = $unquoted.Index; Value = $unquoted.Value })
            }
        }

        $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        foreach ($candidate in @($candidates | Sort-Object Index)) {
            $value = [string]$candidate.Value
            $prefix = $value.Substring(0, $value.IndexOf(':'))
            if ($nonEvidencePrefixes -contains $prefix) {
                continue
            }
            if ($value -match '^review:' -and $value -notmatch '^review:(?:cr|dr)$') {
                [void]$seen.Add("$($candidate.Index)|$value")
                $markers.Add($value)
                continue
            }
            if ($seen.Add("$($candidate.Index)|$value")) {
                $markers.Add($value)
            }
        }
    }

    return , $markers.ToArray()
}

Export-ModuleMember -Function Get-PlanMetadata, ConvertFrom-PlanFolderName, Get-PlanInventory, Get-EpicInventory, Resolve-Epic, Get-EpicRollup, New-PlanId, Resolve-Plan, Get-PlanProgress, Split-PlanHeader, Get-PlanHeaderMarkers, Get-NextStep, Get-PhaseAdmission, Get-PhaseCheckpointOptions, Get-TypedEvidenceMarkers, Get-PlanLayout, Resolve-PlanAssetPath, Resolve-PhysicalRepoPath, Resolve-PlanSection, Get-PlanInlineSectionLine, Get-PlanSectionRecord, Remove-FencedCodeBlocks, Split-MarkdownTableCells, Get-PlanStageOrder, Resolve-PlanStage, Test-PlanStageAtLeast, Get-PlanValidationDecision, Get-PlanningContextDigest, Assert-PlanningContextReady, Get-PlanningContextState, New-PlanCorpusConfinementContext, New-PlanConfinementContext, Resolve-ConfinedPlanPath, Open-ConfinedPlanFile
