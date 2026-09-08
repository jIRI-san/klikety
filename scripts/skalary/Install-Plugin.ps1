#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Name,

    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path,

    [string]$Source,

    [string]$Ref,

    [string]$Repository,

    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '_Common.ps1')

function Get-ResolvedSourceContext {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$TargetRepoRoot,

        [string]$SourcePath,

        [string]$SourceRef,

        [string]$RemoteRepository
    )

    if (-not [string]::IsNullOrWhiteSpace($SourcePath)) {
        $sourceRepoRoot = Resolve-RepoRoot -StartPath $SourcePath
        $resolvedRef = if ([string]::IsNullOrWhiteSpace($SourceRef)) { 'HEAD' } else { $SourceRef }
        $resolvedSha = (git -C $sourceRepoRoot rev-parse $resolvedRef).Trim()
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($resolvedSha)) {
            throw "Unable to resolve ref '$resolvedRef' in source repository '$sourceRepoRoot'."
        }

        return [pscustomobject]@{
            IsRemote = $false
            Ref = $resolvedRef
            Sha = $resolvedSha
            SourceIdentity = New-PluginSourceIdentity -LocalPath $sourceRepoRoot
            SourceRepoRoot = $sourceRepoRoot
            TempPath = $null
        }
    }

    $remote = $RemoteRepository
    if ([string]::IsNullOrWhiteSpace($remote)) {
        $remote = (git -C $TargetRepoRoot remote get-url origin).Trim()
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($remote)) {
            throw "Unable to resolve git remote 'origin' for '$TargetRepoRoot'."
        }
    }
    else {
        $remote = $remote.Trim()
        if ($remote -match '^[^/\s:@]+/[^/\s:@]+$') {
            $remote = "https://github.com/$remote.git"
        }
    }

    $resolvedRef = if ([string]::IsNullOrWhiteSpace($SourceRef)) { 'HEAD' } else { $SourceRef }
    $sourceIdentity = $null
    try {
        $sourceIdentity = New-PluginSourceIdentity -Repository $remote
    }
    catch {
        if (-not (Test-Path -LiteralPath $remote)) {
            throw
        }
        $sourceIdentity = New-PluginSourceIdentity -LocalPath (Resolve-RepoRoot -StartPath $remote)
    }

    $resolvedRefs = @(git ls-remote $remote $resolvedRef 2>$null)
    $resolvedLine = $null
    foreach ($line in $resolvedRefs) {
        if ($line -match '\^\{\}\s*$') {
            $resolvedLine = $line
            break
        }
    }
    if ($null -eq $resolvedLine -and $resolvedRefs.Count -gt 0) {
        $resolvedLine = $resolvedRefs[0]
    }
    $resolvedSha = if ($null -ne $resolvedLine) { ($resolvedLine -split '\s+')[0] } else { $null }
    if ([string]::IsNullOrWhiteSpace($resolvedSha)) {
        throw "Unable to resolve remote ref '$resolvedRef' for source '$([string]$sourceIdentity.identity)'."
    }

    $sourceTempPath = Join-Path ([System.IO.Path]::GetTempPath()) ("skalary-install-" + [System.Guid]::NewGuid().ToString('N'))
    [void](New-Item -ItemType Directory -Path $sourceTempPath -Force)

    if ($resolvedRef -eq 'HEAD') {
        git -c core.longpaths=true clone -c core.autocrlf=false -c core.eol=lf --depth 1 $remote $sourceTempPath 2>$null | Out-Null
    }
    else {
        git -c core.longpaths=true clone -c core.autocrlf=false -c core.eol=lf --depth 1 --branch $resolvedRef $remote $sourceTempPath 2>$null | Out-Null
    }
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to clone source '$([string]$sourceIdentity.identity)' (ref '$resolvedRef')."
    }

    $clonedSha = (git -C $sourceTempPath rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($clonedSha)) {
        throw "Unable to resolve cloned SHA in '$sourceTempPath'."
    }
    if ($clonedSha -ne $resolvedSha) {
        throw "Remote ref '$resolvedRef' resolved to '$resolvedSha' but clone checked out '$clonedSha'."
    }

    return [pscustomobject]@{
        IsRemote = $true
        Ref = $resolvedRef
        Sha = $resolvedSha
        SourceIdentity = $sourceIdentity
        SourceRepoRoot = $sourceTempPath
        TempPath = $sourceTempPath
    }
}

function Assert-RegistryParityAtCommit {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$LocalRepoRoot,

        [Parameter(Mandatory)]
        [string]$Sha,

        [Parameter(Mandatory)]
        $SourceRegistry
    )

    git -C $LocalRepoRoot cat-file -e "$Sha`^{commit}" 2>$null
    if ($LASTEXITCODE -ne 0) {
        return
    }

    $registryJsonAtCommit = git -C $LocalRepoRoot show "$Sha`:registry.json" 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($registryJsonAtCommit)) {
        throw "Unable to read registry.json at local commit '$Sha' for parity check."
    }

    $localRegistry = $registryJsonAtCommit | ConvertFrom-Json -Depth 100
    $sourcePluginByName = @{}
    foreach ($plugin in @($SourceRegistry.plugins)) {
        $sourcePluginByName[[string]$plugin.name] = $plugin
    }

    foreach ($localPlugin in @($localRegistry.plugins)) {
        $pluginName = [string]$localPlugin.name
        if (-not $sourcePluginByName.ContainsKey($pluginName)) {
            throw "Registry parity mismatch at '$Sha': plugin '$pluginName' missing from source snapshot registry."
        }

        $sourcePlugin = $sourcePluginByName[$pluginName]
        $localFileByKey = @{}
        foreach ($localFile in @($localPlugin.files)) {
            $key = "$([string]$localFile.src)|$([string]$localFile.dest)"
            $localFileByKey[$key] = [string]$localFile.sha256
        }

        foreach ($sourceFile in @($sourcePlugin.files)) {
            $key = "$([string]$sourceFile.src)|$([string]$sourceFile.dest)"
            if (-not $localFileByKey.ContainsKey($key)) {
                throw "Registry parity mismatch at '$Sha': file '$key' missing for plugin '$pluginName'."
            }
            if ($localFileByKey[$key] -ne [string]$sourceFile.sha256) {
                throw "Registry parity mismatch at '$Sha': hash mismatch for '$pluginName' file '$key'."
            }
        }
    }
}

function Get-PluginSourceRoot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$SourceRepoRoot,

        [Parameter(Mandatory)]
        [string]$PluginName
    )

    $pluginRoot = Join-Path $SourceRepoRoot "plugins/$PluginName"
    if (-not (Test-Path -LiteralPath $pluginRoot -PathType Container)) {
        throw "Plugin '$PluginName' not found under '$SourceRepoRoot/plugins/'."
    }
    return $pluginRoot
}

function Get-ReceiptOwnerMap {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$RepoRoot
    )

    $receiptsRoot = Join-Path $RepoRoot '.github/.skalary/receipts'
    if (-not (Test-Path -LiteralPath $receiptsRoot -PathType Container)) {
        return @{}
    }
    Assert-GithubStatePathSafe -RepoRoot $RepoRoot -Path $receiptsRoot
    foreach ($receiptFile in Get-ChildItem -LiteralPath $receiptsRoot -File -Filter '*.json') {
        [void](Read-PluginReceipt -RepoRoot $RepoRoot -PluginName $receiptFile.BaseName)
    }
    return @{}
}

function Get-InstallOperationPlan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$TargetRepoRoot,

        [Parameter(Mandatory)]
        [string]$SourceRepoRoot,

        [Parameter(Mandatory)]
        [object[]]$PendingPlugins,

        [Parameter(Mandatory)]
        [string]$StageRoot,

        [Parameter(Mandatory)]
        [hashtable]$OwnerByDest,

        [switch]$Force
    )

    $pendingNames = @{}
    foreach ($plugin in $PendingPlugins) {
        $pendingNames[[string]$plugin.name] = $true
    }

    $operations = [System.Collections.Generic.List[object]]::new()
    $destinations = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $index = 0
    foreach ($plugin in $PendingPlugins) {
        $pluginName = [string]$plugin.name
        $pluginRoot = Get-PluginSourceRoot -SourceRepoRoot $SourceRepoRoot -PluginName $pluginName
        foreach ($file in @($plugin.files)) {
            $src = [string]$file.src
            if ($src -match '^evals(?:/|$)') {
                continue
            }

            $dest = [string]$file.dest
            $targetPath = Resolve-GithubConstrainedPath -RepoRoot $TargetRepoRoot -RelativePath $dest
            Assert-GithubStatePathSafe -RepoRoot $TargetRepoRoot -Path $targetPath
            $destKey = [System.IO.Path]::GetFullPath($targetPath).ToLowerInvariant()
            if (-not $destinations.Add($destKey)) {
                throw "Plugin manifests map multiple pending payloads to destination '$dest'."
            }
            if (Test-Path -LiteralPath $targetPath -PathType Container) {
                throw "Refusing overwrite of directory destination '$dest'."
            }
            if ((Test-Path -LiteralPath $targetPath -PathType Leaf) -and -not $Force) {
                throw "Refusing overwrite of existing unowned path '$dest'. Use -Force to overwrite."
            }

            $sourcePath = Resolve-PluginConstrainedPath -PluginRoot $pluginRoot -RelativePath $src
            if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
                throw "Plugin '$pluginName' source '$src' is missing from source snapshot."
            }

            $relativeStageName = '{0:d5}-{1}' -f $index, ([System.IO.Path]::GetFileName($dest))
            $stagePath = Join-Path $StageRoot $relativeStageName
            Copy-GitCanonicalFile -RepoRoot $SourceRepoRoot -Path $sourcePath `
                -Destination $stagePath

            $expectedHash = [string]$file.sha256
            $actualHash = Get-FileSha256 -Path $stagePath
            if ($actualHash -ne $expectedHash) {
                throw "Staged hash mismatch for '$pluginName' file '$src': expected '$expectedHash', got '$actualHash'."
            }

            $operation = [pscustomobject]@{
                Dest = $dest
                DestKey = $destKey
                PluginName = $pluginName
                Sha256 = $expectedHash
                SourcePath = $sourcePath
                StagePath = $stagePath
                TargetPath = $targetPath
            }
            $operations.Add($operation)
            $index++
        }
    }

    return @($operations | Sort-Object Dest, PluginName)
}

function Write-InstallOperations {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [object[]]$Operations
    )

    $applied = [System.Collections.Generic.List[object]]::new()
    foreach ($operation in $Operations) {
        $targetDir = Split-Path -Parent $operation.TargetPath
        if (-not (Test-Path -LiteralPath $targetDir -PathType Container)) {
            [void](New-Item -ItemType Directory -Path $targetDir -Force)
        }

        $appliedEntry = [pscustomobject]@{
            Operation = $operation
            TargetPath = $operation.TargetPath
        }
        $applied.Add($appliedEntry)
        Copy-Item -LiteralPath $operation.StagePath -Destination $operation.TargetPath -Force
        if ((Get-FileSha256 -Path $operation.TargetPath) -cne [string]$operation.Sha256) {
            throw "Write verification failed for '$([string]$operation.Dest)'."
        }
    }

    return @($applied)
}

function Get-PluginReceiptContent {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        $Plugin,

        [Parameter(Mandatory)]
        $SourceIdentity,

        [Parameter(Mandatory)]
        [string]$RefSha
    )

    return [pscustomobject]@{
        name = [string]$Plugin.name
        ref = $RefSha
        sourceIdentity = $SourceIdentity
        version = [string]$Plugin.version
    }
}

function Write-ReceiptSet {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$RepoRoot,

        [Parameter(Mandatory)]
        [object[]]$PendingPlugins,

        [Parameter(Mandatory)]
        $SourceIdentity,

        [Parameter(Mandatory)]
        [string]$RefSha,

        [Parameter(Mandatory)]
        [string]$OperationRoot
    )

    $entries = [System.Collections.Generic.List[object]]::new()
    foreach ($plugin in $PendingPlugins) {
        $pluginName = [string]$plugin.name
        $receipt = Get-PluginReceiptContent -Plugin $plugin -SourceIdentity $SourceIdentity -RefSha $RefSha
        $receiptPath = Write-PluginReceipt -RepoRoot $RepoRoot -Receipt $receipt
        $entries.Add([pscustomobject]@{ ReceiptPath = $receiptPath; Committed = $true })
    }

    return @($entries)
}

$targetRepoRoot = Resolve-RepoRoot -StartPath $RepoRoot
$sourceContext = $null
$operationRoot = $null
$appliedEntries = @()
$receiptEntries = @()

try {
    $sourceContext = Get-ResolvedSourceContext -TargetRepoRoot $targetRepoRoot -SourcePath $Source -SourceRef $Ref -RemoteRepository $Repository
    $sourceRepoRoot = [string]$sourceContext.SourceRepoRoot
    $resolvedSha = [string]$sourceContext.Sha

    $sourceRegistryPath = Join-Path $sourceRepoRoot 'registry.json'
    if (-not (Test-Path -LiteralPath $sourceRegistryPath -PathType Leaf)) {
        throw "registry.json not found at source '$sourceRepoRoot'."
    }
    $registry = Read-JsonFile -Path $sourceRegistryPath
    if ($sourceContext.IsRemote) {
        Assert-RegistryParityAtCommit -LocalRepoRoot $targetRepoRoot -Sha $resolvedSha -SourceRegistry $registry
    }
    if (@($registry.retiredPlugins | Where-Object { [string]$_.name -ceq $Name }).Count -gt 0) {
        throw "Plugin '$Name' is retired. Run Remove-Plugin.ps1 -Name $Name."
    }

    $pluginsByName = @{}
    foreach ($plugin in @($registry.plugins)) {
        $pluginName = [string]$plugin.name
        if ($pluginsByName.ContainsKey($pluginName)) {
            throw "Duplicate plugin '$pluginName' in source registry."
        }
        $pluginsByName[$pluginName] = $plugin
    }

    $resolvedOrder = Resolve-PluginDependencyOrder -PluginsByName $pluginsByName -RootPluginName $Name -RepoRoot $targetRepoRoot
    $orderedPlugins = @($resolvedOrder.Ordered)
    $pendingPlugins = @($resolvedOrder.Pending)

    if ($pendingPlugins.Count -eq 0) {
        Write-Host "Plugin '$Name' is already up to date at '$resolvedSha'."
        exit 0
    }

    $skalaryRoot = Resolve-GithubConstrainedPath -RepoRoot $targetRepoRoot -RelativePath '.skalary'
    if (-not (Test-Path -LiteralPath $skalaryRoot -PathType Container)) {
        [void](New-Item -ItemType Directory -Path $skalaryRoot -Force)
    }
    Assert-GithubStatePathSafe -RepoRoot $targetRepoRoot -Path $skalaryRoot

    $operationRoot = Resolve-GithubConstrainedPath -RepoRoot $targetRepoRoot -RelativePath (".skalary/tmp/install-" + [System.Guid]::NewGuid().ToString('N'))
    $stagedRoot = Resolve-GithubConstrainedPath -RepoRoot $targetRepoRoot -RelativePath (([System.IO.Path]::GetRelativePath((Join-Path $targetRepoRoot '.github'), $operationRoot).Replace('\', '/')) + '/staged')
    [void](New-Item -ItemType Directory -Path $stagedRoot -Force)
    Assert-GithubStatePathSafe -RepoRoot $targetRepoRoot -Path $stagedRoot

    $ownerByDest = Get-ReceiptOwnerMap -RepoRoot $targetRepoRoot
    $operations = Get-InstallOperationPlan -TargetRepoRoot $targetRepoRoot -SourceRepoRoot $sourceRepoRoot -PendingPlugins $pendingPlugins -StageRoot $stagedRoot -OwnerByDest $ownerByDest -Force:$Force
    if ($operations.Count -eq 0) {
        throw "No installable payload files found for '$Name'."
    }

    [void](Write-InstallOperations -Operations $operations)

    $receiptEntries = Write-ReceiptSet -RepoRoot $targetRepoRoot -PendingPlugins $pendingPlugins -SourceIdentity $sourceContext.SourceIdentity -RefSha $resolvedSha -OperationRoot $operationRoot

    Write-Host "Installed plugin '$Name' with $($pendingPlugins.Count) plugin(s) from '$([string]$sourceContext.SourceIdentity.identity)' at '$resolvedSha'."
}
catch {
    if (-not [string]::IsNullOrWhiteSpace($operationRoot) -and (Test-Path -LiteralPath $operationRoot -PathType Container)) {
        Remove-Item -LiteralPath $operationRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    throw
}
finally {
    if ($null -ne $sourceContext -and -not [string]::IsNullOrWhiteSpace([string]$sourceContext.TempPath)) {
        Remove-Item -LiteralPath ([string]$sourceContext.TempPath) -Recurse -Force -ErrorAction SilentlyContinue
    }
}
exit 0

