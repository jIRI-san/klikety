#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Name,
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path,
    [string]$Source,
    [string]$Ref,
    [string]$Repository,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_Common.ps1')

function Get-SourceSnapshot {
    param([string]$TargetRepoRoot, [string]$SourcePath, [string]$SourceRef, [string]$RemoteRepository)

    $source = if (-not [string]::IsNullOrWhiteSpace($SourcePath)) { Resolve-RepoRoot -StartPath $SourcePath } else { $RemoteRepository }
    if ([string]::IsNullOrWhiteSpace($source)) {
        $source = (git -C $TargetRepoRoot remote get-url origin).Trim()
        if ($LASTEXITCODE -ne 0) { throw "Unable to resolve git remote 'origin' for '$TargetRepoRoot'." }
    }
    elseif ($source -match '^[^/\s:@]+/[^/\s:@]+$') {
        $source = "https://github.com/$source.git"
    }
    $identity = try { New-PluginSourceIdentity -Repository $source } catch {
        if (-not (Test-Path -LiteralPath $source)) { throw }
        New-PluginSourceIdentity -LocalPath (Resolve-RepoRoot -StartPath $source)
    }
    $refToResolve = if ([string]::IsNullOrWhiteSpace($SourceRef)) { 'HEAD' } else { $SourceRef }
    if (Test-Path -LiteralPath $source) {
        $sha = (git -C $source rev-parse $refToResolve).Trim()
    }
    elseif ($refToResolve -cmatch '^[a-f0-9]{40,64}$') {
        $sha = $refToResolve
    }
    else {
        $remoteRefs = @(git ls-remote $source $refToResolve)
        $sha = if ($remoteRefs.Count -gt 0) {
            ([string]$remoteRefs[0] -split '\s+')[0]
        }
        else {
            $null
        }
    }
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sha) -or $sha -cnotmatch '^[a-f0-9]{40,64}$') {
        throw "Unable to resolve immutable ref '$refToResolve'."
    }
    $snapshot = Join-Path ([System.IO.Path]::GetTempPath()) ("skalary-update-" + [guid]::NewGuid().ToString('N'))
    [void](New-Item -ItemType Directory -Path $snapshot -Force)
    if (Test-Path -LiteralPath $source) {
        git -C $source archive $sha | tar -xf - -C $snapshot
    } else {
        git -c core.longpaths=true clone -c core.autocrlf=false -c core.eol=lf --no-checkout $source $snapshot 2>$null | Out-Null
        git -c core.longpaths=true -C $snapshot checkout --quiet $sha
    }
    if ($LASTEXITCODE -ne 0) { throw "Failed to materialize source '$sha'." }
    return [pscustomobject]@{ Root = $snapshot; Sha = $sha; Identity = $identity; TempPath = $snapshot }
}

function Get-ManifestFiles {
    param([string]$SnapshotRoot, [string]$PluginName)
    $manifestPath = Join-Path $SnapshotRoot "plugins/$PluginName/plugin.json"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Plugin '$PluginName' manifest is unavailable from its immutable source."
    }
    $manifest = Read-JsonFile -Path $manifestPath
    if ([string]$manifest.name -cne $PluginName) { throw "Plugin manifest name does not match '$PluginName'." }
    return [pscustomobject]@{
        Manifest = $manifest
        Files = @($manifest.files | Where-Object { [string]$_.src -notmatch '^evals(?:/|$)' })
    }
}

$targetRoot = Resolve-RepoRoot -StartPath $RepoRoot
$target = $null
$old = $null
try {
    $receipt = Read-PluginReceipt -RepoRoot $targetRoot -PluginName $Name
    if ($null -eq $receipt) { throw "Plugin '$Name' is not installed (receipt missing)." }
    $target = Get-SourceSnapshot -TargetRepoRoot $targetRoot -SourcePath $Source -SourceRef $Ref -RemoteRepository $Repository
    if (-not (Test-PluginSourceIdentityEqual -Left $receipt.sourceIdentity -Right $target.Identity)) {
        throw "Plugin '$Name' receipt source identity does not match the requested source."
    }
    $registry = Read-JsonFile -Path (Join-Path $target.Root 'registry.json')
    if (@($registry.retiredPlugins | Where-Object { [string]$_.name -ceq $Name }).Count -gt 0) {
        throw "Plugin '$Name' is retired. Run Remove-Plugin.ps1 -Name $Name."
    }
    $new = Get-ManifestFiles -SnapshotRoot $target.Root -PluginName $Name
    $old = Get-SourceSnapshot -TargetRepoRoot $targetRoot -SourcePath $Source -SourceRef ([string]$receipt.ref) -RemoteRepository $Repository
    $previous = Get-ManifestFiles -SnapshotRoot $old.Root -PluginName $Name
    if ([string]$receipt.version -ceq [string]$new.Manifest.version -and [string]$receipt.ref -ceq [string]$target.Sha) {
        Write-Output "Plugin '$Name' is already up to date at '$($target.Sha)'."
        exit 0
    }

    $newByDest = @{}
    foreach ($file in $new.Files) { $newByDest[[string]$file.dest] = $file }
    $destinations = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($file in @($previous.Files) + @($new.Files)) { [void]$destinations.Add([string]$file.dest) }
    foreach ($dest in $destinations) {
        $path = Resolve-GithubConstrainedPath -RepoRoot $targetRoot -RelativePath $dest
        Assert-GithubStatePathSafe -RepoRoot $targetRoot -Path $path
        if (Test-Path -LiteralPath $path -PathType Container) {
            throw "Managed destination '$dest' is a directory."
        }
    }
    foreach ($dest in $destinations | Sort-Object) {
        $path = Resolve-GithubConstrainedPath -RepoRoot $targetRoot -RelativePath $dest
        if ($newByDest.ContainsKey($dest)) {
            $file = $newByDest[$dest]
            $sourcePath = Resolve-PluginConstrainedPath -PluginRoot (Join-Path $target.Root "plugins/$Name") -RelativePath ([string]$file.src)
            if ((Get-FileSha256 -Path $sourcePath) -cne [string]$file.sha256) { throw "Source hash mismatch for '$dest'." }
            [void](New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force)
            Copy-Item -LiteralPath $sourcePath -Destination $path -Force
            if ((Get-FileSha256 -Path $path) -cne [string]$file.sha256) { throw "Write verification failed for '$dest'." }
        } elseif (Test-Path -LiteralPath $path -PathType Leaf) {
            Remove-Item -LiteralPath $path -Force
            if (Test-Path -LiteralPath $path -PathType Leaf) { throw "Delete verification failed for '$dest'." }
        }
    }
    Write-PluginReceipt -RepoRoot $targetRoot -Receipt ([pscustomobject][ordered]@{
            name = $Name; version = [string]$new.Manifest.version
            sourceIdentity = $target.Identity; ref = $target.Sha
        }) | Out-Null
    Write-Output "Updated plugin '$Name' to version '$($new.Manifest.version)' at '$($target.Sha)'."
}
finally {
    foreach ($context in @($target, $old)) {
        if ($null -ne $context -and (Test-Path -LiteralPath $context.TempPath)) {
            Remove-Item -LiteralPath $context.TempPath -Recurse -Force
        }
    }
}

