#requires -Version 7.0

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-FactoryLoopConsumerPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$RelativePath
    )

    $root = [System.IO.Path]::GetFullPath($RepoRoot)
    if (-not (Test-Path -LiteralPath $root -PathType Container)) {
        throw "Consumer repository does not exist: $root"
    }
    if (((Get-Item -LiteralPath $root -Force).Attributes -band
            [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Consumer repository root cannot be a link or reparse point: $root"
    }
    if ([System.IO.Path]::IsPathRooted($RelativePath) -or
        $RelativePath -match '(^|[\\/])\.\.([\\/]|$)' -or
        $RelativePath -match '^[A-Za-z]:' -or
        $RelativePath -match '[<>:"|?*]') {
        throw "Consumer path must be a confined repository-relative factory-loop path: $RelativePath"
    }
    $normalized = $RelativePath.Replace('\', '/').Trim('/')
    if (-not ($normalized.StartsWith('.factory-loop/', [System.StringComparison]::Ordinal) -or
            $normalized.StartsWith('scripts/factory-loop/', [System.StringComparison]::Ordinal) -or
            $normalized.StartsWith('.github/skills/ci/scripts/', [System.StringComparison]::Ordinal) -or
            $normalized.StartsWith('docs/factory-loop/evidence/', [System.StringComparison]::Ordinal))) {
        throw "Consumer path is outside factory-loop setup ownership: $RelativePath"
    }

    $candidate = [System.IO.Path]::GetFullPath(
        (Join-Path $root ($normalized.Replace('/', [System.IO.Path]::DirectorySeparatorChar)))
    )
    $prefix = $root.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    if (-not $candidate.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Consumer path escapes the selected repository: $RelativePath"
    }

    $cursor = $root
    $relativeSegments = $normalized -split '/'
    foreach ($segment in $relativeSegments) {
        $cursor = Join-Path $cursor $segment
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Consumer path traverses a link or reparse point: $RelativePath"
            }
        }
    }
    return $candidate
}

function Get-FactoryLoopTemplateRoot {
    [CmdletBinding()]
    param()

    $root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\templates'))
    if (-not (Test-Path -LiteralPath $root -PathType Container)) {
        throw "Factory-loop templates are missing from the installed plugin payload: $root"
    }
    return $root
}

function Get-FactoryLoopSetupFiles {
    [CmdletBinding()]
    param()

    $templateRoot = Get-FactoryLoopTemplateRoot
    $dependencyRoots = @(
        (Join-Path $PSScriptRoot '..\..\ci\scripts'),
        (Join-Path $PSScriptRoot '..\dependencies\ci\scripts')
    )
    $dependencyRoot = $dependencyRoots |
        Where-Object { Test-Path -LiteralPath $_ -PathType Container } |
        Select-Object -First 1
    if (-not $dependencyRoot) {
        throw 'The bundled continue-implementation baseline dependency is missing from the factory-loop plugin.'
    }
    $files = @(
        [pscustomobject]@{
            RelativePath = '.factory-loop/factory-loop.json'
            SourcePath = Join-Path $templateRoot 'factory-loop.json'
        }
        [pscustomobject]@{
            RelativePath = 'scripts/factory-loop/FactoryLoop.Adapter.ps1'
            SourcePath = Join-Path $templateRoot 'FactoryLoop.Adapter.ps1'
        }
        [pscustomobject]@{
            RelativePath = 'scripts/factory-loop/acceptance/Acceptance.ps1'
            SourcePath = Join-Path $templateRoot 'demo/Invoke-Acceptance.ps1'
        }
    )
    foreach ($name in @('DirectWorkflow.psm1', 'PlanState.psm1', 'SecretGuard.psm1')) {
        $files += [pscustomobject]@{
            RelativePath = ".github/skills/ci/scripts/$name"
            SourcePath = Join-Path $dependencyRoot $name
        }
    }
    return $files
}

function Get-FactoryLoopSetupPreview {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot
    )

    $root = [System.IO.Path]::GetFullPath($RepoRoot)
    $entries = [System.Collections.Generic.List[object]]::new()
    foreach ($file in Get-FactoryLoopSetupFiles) {
        $destination = Resolve-FactoryLoopConsumerPath -RepoRoot $root -RelativePath $file.RelativePath
        $desired = [System.IO.File]::ReadAllBytes($file.SourcePath)
        $desiredHash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($desired)).ToLowerInvariant()
        $currentHash = $null
        if (Test-Path -LiteralPath $destination -PathType Leaf) {
            $currentHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        elseif (Test-Path -LiteralPath $destination) {
            throw "Setup target is not a regular file: $($file.RelativePath)"
        }
        $entries.Add([pscustomobject]@{
                path = $file.RelativePath
                action = if ($null -eq $currentHash) { 'create' } else { 'preserve' }
                desiredSha256 = $desiredHash
                currentSha256 = $currentHash
            })
    }
    $ordered = @($entries | Sort-Object -Property path)
    $canonical = $ordered | ConvertTo-Json -Depth 8 -Compress
    $digest = [Convert]::ToHexString(
        [System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($canonical))
    ).ToLowerInvariant()
    return [pscustomobject]@{
        repoRoot = $root
        digest = $digest
        files = $ordered
    }
}

function Invoke-FactoryLoopSetup {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('preview', 'apply')][string]$Action,
        [Parameter(Mandatory)][string]$RepoRoot,
        [string]$ExpectedDigest
    )

    $preview = Get-FactoryLoopSetupPreview -RepoRoot $RepoRoot
    if ($Action -eq 'preview') { return $preview }
    if ([string]::IsNullOrWhiteSpace($ExpectedDigest) -or
        $ExpectedDigest -cne $preview.digest) {
        throw "Setup approval does not match the current preview. Expected digest: $($preview.digest)"
    }

    foreach ($entry in $preview.files) {
        if ($entry.action -eq 'preserve') { continue }
        $file = Get-FactoryLoopSetupFiles | Where-Object RelativePath -CEQ $entry.path
        $destination = Resolve-FactoryLoopConsumerPath -RepoRoot $preview.repoRoot -RelativePath $entry.path
        [void](New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force)
        $bytes = [System.IO.File]::ReadAllBytes($file.SourcePath)
        [System.IO.File]::WriteAllBytes($destination, $bytes)
        $actualHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -cne $entry.desiredSha256) {
            throw "Setup verification failed for '$($entry.path)'."
        }
    }
    return Get-FactoryLoopSetupPreview -RepoRoot $preview.repoRoot
}

function ConvertFrom-FactoryLoopAdapterResult {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$Result,
        [Parameter(Mandatory)][ValidateSet(
            'work-item', 'pull-request', 'deployment', 'version', 'acceptance', 'telemetry', 'evidence', 'approval'
        )][string]$Domain,
        [Parameter(Mandatory)][ValidateSet(
            'read', 'find', 'create', 'update', 'open', 'checks', 'merge', 'trigger', 'run', 'query',
            'publish', 'close', 'reconcile', 'approve', 'decline'
        )][string]$Action,
        [Parameter(Mandatory)][string]$OperationId
    )

    if ($Result -is [string]) {
        if ($Result.Length -gt 65536) { throw 'Adapter output exceeds the 65536-character limit.' }
        try {
            $Result = ConvertFrom-Json -InputObject $Result -AsHashtable -Depth 32 -ErrorAction Stop
        }
        catch {
            throw 'Adapter returned malformed JSON.'
        }
    }
    if ($Result -is [System.Management.Automation.PSCustomObject]) {
        $Result = ConvertFrom-Json -InputObject ($Result | ConvertTo-Json -Depth 32 -Compress) `
            -AsHashtable -Depth 32 -ErrorAction Stop
    }
    if ($Result -isnot [System.Collections.IDictionary]) {
        throw 'Adapter result must be one JSON object.'
    }
    foreach ($name in @('schemaVersion', 'domain', 'action', 'operationId', 'status', 'providerId', 'data')) {
        if (-not $Result.Contains($name)) { throw "Adapter result is missing required field '$name'." }
    }
    if ([int]$Result.schemaVersion -ne 1 -or
        [string]$Result.domain -cne $Domain -or
        [string]$Result.action -cne $Action -or
        [string]$Result.operationId -cne $OperationId) {
        throw 'Adapter result does not match the requested schema, domain, action, and operation ID.'
    }
    if ([string]$Result.status -cnotin @('ok', 'waiting', 'blocked', 'not-found', 'failed')) {
        throw "Adapter returned an unsupported status '$($Result.status)'."
    }
    if ([string]::IsNullOrWhiteSpace([string]$Result.providerId)) {
        throw 'Adapter result must identify its provider object or simulation.'
    }
    if ($Result.status -ceq 'failed') {
        $code = if ($Result.Contains('errorCode')) { [string]$Result['errorCode'] } else { '' }
        if ($code -notmatch '^[A-Za-z0-9._-]{1,64}$') { $code = 'AdapterFailure' }
        throw "Adapter operation failed: $code"
    }
    return [pscustomobject]$Result
}

function Invoke-FactoryLoopAdapter {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet(
            'work-item', 'pull-request', 'deployment', 'version', 'acceptance', 'telemetry', 'evidence', 'approval'
        )][string]$Domain,
        [Parameter(Mandatory)][ValidateSet(
            'read', 'find', 'create', 'update', 'open', 'checks', 'merge', 'trigger', 'run', 'query',
            'publish', 'close', 'reconcile', 'approve', 'decline'
        )][string]$Action,
        [Parameter(Mandatory)][string]$OperationId,
        [hashtable]$Payload = @{},
        [Parameter(Mandatory)][scriptblock]$Runner
    )

    if ($OperationId.Length -gt 200 -or $OperationId -notmatch '^[A-Za-z0-9][A-Za-z0-9._:-]{0,199}$') {
        throw 'Operation ID must be a stable, bounded identifier.'
    }
    $raw = & $Runner $Domain $Action $OperationId $Payload
    return ConvertFrom-FactoryLoopAdapterResult -Result $raw -Domain $Domain -Action $Action `
        -OperationId $OperationId
}

function Get-FactoryLoopApplicationDigest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ApplicationRoot
    )

    $root = [System.IO.Path]::GetFullPath($ApplicationRoot)
    if (-not (Test-Path -LiteralPath $root -PathType Container)) {
        throw "Application root does not exist: $root"
    }
    $rootItem = Get-Item -LiteralPath $root -Force
    if (($rootItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Application root cannot be a link or reparse point: $root"
    }
    $tree = @(Get-ChildItem -LiteralPath $root -Recurse -Force)
    foreach ($item in $tree) {
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Application contains a link or reparse point: $($item.FullName)"
        }
    }
    $files = @($tree | Where-Object { -not $_.PSIsContainer })
    [Array]::Sort($files, [System.Comparison[System.IO.FileInfo]]{
            param($left, $right)
            [System.StringComparer]::Ordinal.Compare($left.FullName, $right.FullName)
        })
    if ($files.Count -eq 0) { throw 'Application snapshot cannot be empty.' }
    $records = [System.Collections.Generic.List[string]]::new()
    foreach ($file in $files) {
        if (($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Application contains a link or reparse point: $($file.FullName)"
        }
        $relative = [System.IO.Path]::GetRelativePath($root, $file.FullName).Replace('\', '/')
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $records.Add("$relative`0$hash")
    }
    $canonical = [string]::Join("`n", $records)
    return [Convert]::ToHexString(
        [System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($canonical))
    ).ToLowerInvariant()
}

function New-FactoryLoopArtifactSnapshot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ApplicationRoot,
        [Parameter(Mandatory)][string]$ArtifactStore,
        [string]$SourceSha,
        [string]$MergeCommit
    )

    $source = [System.IO.Path]::GetFullPath($ApplicationRoot)
    $digest = Get-FactoryLoopApplicationDigest -ApplicationRoot $source
    $store = [System.IO.Path]::GetFullPath($ArtifactStore)
    [void](New-Item -ItemType Directory -Path $store -Force)
    $target = Join-Path $store $digest
    if (Test-Path -LiteralPath $target) {
        $targetItem = Get-Item -LiteralPath $target -Force
        if (-not $targetItem.PSIsContainer -or
            ($targetItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'Content-addressed artifact target is not a regular directory.'
        }
    }
    if (-not (Test-Path -LiteralPath $target -PathType Container)) {
        $staging = Join-Path $store ('.staging-' + [guid]::NewGuid().ToString('N'))
        [void](New-Item -ItemType Directory -Path $staging)
        try {
            foreach ($sourceItem in Get-ChildItem -LiteralPath $source -Force) {
                Copy-Item -LiteralPath $sourceItem.FullName -Destination $staging -Recurse -Force
            }
            $copiedDigest = Get-FactoryLoopApplicationDigest -ApplicationRoot $staging
            if ($copiedDigest -cne $digest) { throw 'Copied artifact does not match its source digest.' }
            foreach ($item in Get-ChildItem -LiteralPath $staging -File -Recurse -Force) {
                $item.IsReadOnly = $true
            }
            Move-Item -LiteralPath $staging -Destination $target
        }
        finally {
            if (Test-Path -LiteralPath $staging) {
                Remove-Item -LiteralPath $staging -Recurse -Force
            }
        }
    }
    $snapshotDigest = Get-FactoryLoopApplicationDigest -ApplicationRoot $target
    if ($snapshotDigest -cne $digest) { throw 'Existing content-addressed artifact failed integrity verification.' }
    return [pscustomobject]@{
        artifactDigest = $digest
        artifactPath = $target
        sourceSha = $SourceSha
        mergeCommit = $MergeCommit
    }
}

function Test-FactoryLoopArtifactAcceptance {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ArtifactPath,
        [Parameter(Mandatory)][string]$ExpectedDigest,
        [ValidateSet('test', 'preprod', 'prod')][string]$Environment = 'test'
    )

    $actualDigest = Get-FactoryLoopApplicationDigest -ApplicationRoot $ArtifactPath
    if ($actualDigest -cne $ExpectedDigest) {
        return [pscustomobject]@{
            status = 'blocked'
            reason = 'artifact-digest-mismatch'
            expectedDigest = $ExpectedDigest
            actualDigest = $actualDigest
            environment = $Environment
        }
    }
    $acceptance = Join-Path $ArtifactPath 'Invoke-Acceptance.ps1'
    if (-not (Test-Path -LiteralPath $acceptance -PathType Leaf)) {
        throw "Artifact acceptance command is missing: $acceptance"
    }
    $resultText = & $acceptance -ArtifactRoot $ArtifactPath -Environment $Environment
    $result = ConvertFrom-Json -InputObject ($resultText -join "`n") -AsHashtable -Depth 16 -ErrorAction Stop
    if ([int]$result.schemaVersion -ne 1 -or [string]$result.environment -cne $Environment -or
        [string]$result.status -cnotin @('passed', 'failed')) {
        throw 'Artifact acceptance returned a malformed result.'
    }
    return [pscustomobject]@{
        status = if ($result.status -ceq 'passed') { 'passed' } else { 'failed' }
        artifactDigest = $actualDigest
        environment = $Environment
        scenario = [string]$result.scenario
        expected = [string]$result.expected
        actual = [string]$result.actual
    }
}

function New-FactoryLoopDemoProject {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$DemoRoot,
        [string]$ConsumerRoot
    )

    $root = [System.IO.Path]::GetFullPath($DemoRoot)
    if (-not [string]::IsNullOrWhiteSpace($ConsumerRoot)) {
        $consumer = [System.IO.Path]::GetFullPath($ConsumerRoot).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
        if ($root.StartsWith($consumer, [System.StringComparison]::OrdinalIgnoreCase) -or
            $consumer.StartsWith($root.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw 'The disposable demo project must be outside the selected consumer repository.'
        }
    }
    if (Test-Path -LiteralPath $root) {
        if (((Get-Item -LiteralPath $root -Force).Attributes -band
                [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'Demo destination cannot be a link or reparse point.'
        }
        $existing = @(Get-ChildItem -LiteralPath $root -Force)
        if ($existing.Count -gt 0) { throw "Demo destination must be empty: $root" }
    }
    else {
        [void](New-Item -ItemType Directory -Path $root -Force)
    }

    $templates = Join-Path (Get-FactoryLoopTemplateRoot) 'demo'
    $application = Join-Path $root 'application'
    [void](New-Item -ItemType Directory -Path $application -Force)
    [void](New-Item -ItemType Directory -Path (Join-Path $root '.factory-loop') -Force)
    Copy-Item -LiteralPath (Join-Path $templates 'Invoke-DemoApp.ps1') -Destination $application
    Copy-Item -LiteralPath (Join-Path $templates 'Invoke-Acceptance.ps1') -Destination $application
    Copy-Item -LiteralPath (Join-Path (Get-FactoryLoopTemplateRoot) 'factory-loop.json') `
        -Destination (Join-Path $root '.factory-loop/factory-loop.json') -Force
    Set-Content -LiteralPath (Join-Path $root '.gitignore') -Value ".factory-loop/`n" `
        -Encoding utf8NoBOM -NoNewline
    [void](Initialize-FactoryLoopLoopback -RepoRoot $root)
    git -C $root init --initial-branch=main --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Unable to initialize the disposable demo Git repository.' }
    git -C $root -c user.name='Factory Loop Demo' -c user.email='factory-loop-demo@localhost' add --all
    if ($LASTEXITCODE -ne 0) { throw 'Unable to stage the disposable demo application.' }
    git -C $root -c user.name='Factory Loop Demo' -c user.email='factory-loop-demo@localhost' commit --quiet -m 'Seed factory-loop demo with planted defect'
    if ($LASTEXITCODE -ne 0) { throw 'Unable to create the demo source commit.' }
    return [pscustomobject]@{
        demoRoot = $root
        applicationRoot = $application
        sourceSha = (git -C $root rev-parse HEAD).Trim()
        acceptanceStatus = (Test-FactoryLoopArtifactAcceptance -ArtifactPath $application `
                -ExpectedDigest (Get-FactoryLoopApplicationDigest -ApplicationRoot $application)).status
    }
}

function Merge-FactoryLoopDemoPullRequest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$DemoRoot,
        [Parameter(Mandatory)][string]$Branch,
        [Parameter(Mandatory)][string]$ExpectedSourceSha,
        [switch]$ConfirmMerge
    )

    if (-not $ConfirmMerge) { throw 'A demo pull-request merge requires explicit -ConfirmMerge approval.' }
    $isFeatureBranch = $Branch -match '^feature/[A-Za-z0-9][A-Za-z0-9._/-]{0,80}$'
    $isRepairBranch = $Branch -match '^factory-repair/[A-Za-z0-9][A-Za-z0-9._-]{0,127}/[12]$'
    if ((-not $isFeatureBranch -and -not $isRepairBranch) -or $Branch -match '\.\.') {
        throw 'Demo merge accepts only a bounded feature/* or factory-repair/* branch name.'
    }
    $root = [System.IO.Path]::GetFullPath($DemoRoot)
    $currentBranch = (git -C $root branch --show-current).Trim()
    if ($LASTEXITCODE -ne 0 -or $currentBranch -cne 'main') {
        throw 'Demo pull-request merge must start on the local main branch.'
    }
    $status = @(git -C $root status --porcelain)
    if ($LASTEXITCODE -ne 0 -or $status.Count -gt 0) {
        throw 'Demo pull-request merge requires a clean working tree.'
    }
    $sourceSha = (git -C $root rev-parse $Branch).Trim()
    if ($LASTEXITCODE -ne 0 -or $sourceSha -cne $ExpectedSourceSha) {
        throw 'Demo pull-request source head changed; refresh the reviewed source SHA.'
    }
    $targetSha = (git -C $root rev-parse HEAD).Trim()
    git -C $root merge --no-ff --no-edit $Branch --quiet
    if ($LASTEXITCODE -ne 0) { throw 'The real local Git merge failed.' }
    $mergeCommit = (git -C $root rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $mergeCommit -ceq $sourceSha -or $mergeCommit -ceq $targetSha) {
        throw 'The demo merge did not produce a distinct merge commit.'
    }
    $parents = (git -C $root rev-list --parents -n 1 HEAD).Trim() -split '\s+'
    if ($parents.Count -ne 3 -or $parents[1] -cne $targetSha -or $parents[2] -cne $sourceSha) {
        throw 'The demo merge commit does not preserve the reviewed source and target parents.'
    }
    return [pscustomobject]@{
        sourceSha = $sourceSha
        targetBeforeMerge = $targetSha
        mergeCommit = $mergeCommit
    }
}

function Get-FactoryLoopLoopbackStatePath {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepoRoot)

    return Resolve-FactoryLoopConsumerPath -RepoRoot $RepoRoot -RelativePath '.factory-loop/loopback.json'
}

function Save-FactoryLoopLoopbackState {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][System.Collections.IDictionary]$State
    )

    $path = Get-FactoryLoopLoopbackStatePath -RepoRoot $RepoRoot
    [void](New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force)
    $temporary = "$path.$([guid]::NewGuid().ToString('N')).tmp"
    try {
        $json = ConvertTo-Json -InputObject $State -Depth 50
        [System.IO.File]::WriteAllText($temporary, $json, [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::Move($temporary, $path, $true)
    }
    finally {
        if (Test-Path -LiteralPath $temporary) {
            Remove-Item -LiteralPath $temporary -Force
        }
    }
}

function Initialize-FactoryLoopLoopback {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepoRoot)

    $path = Get-FactoryLoopLoopbackStatePath -RepoRoot $RepoRoot
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        $existing = Get-Item -LiteralPath $path -Force
        if (($existing.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'Loopback state cannot be a link or reparse point.'
        }
        return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable -Depth 50
    }
    $state = [ordered]@{
        schemaVersion = 1
        provider = 'loopback'
        simulated = $true
        counters = [ordered]@{
            workItem = 0
            pullRequest = 0
            deployment = 0
            approval = 0
        }
        workItems = @()
        pullRequests = @()
        deployments = @()
        approvals = @()
        acceptances = @()
        telemetry = @()
        evidence = @()
        operations = @()
    }
    Save-FactoryLoopLoopbackState -RepoRoot $RepoRoot -State $state
    return $state
}

function Read-FactoryLoopLoopbackState {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepoRoot)

    $path = Get-FactoryLoopLoopbackStatePath -RepoRoot $RepoRoot
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        return Initialize-FactoryLoopLoopback -RepoRoot $RepoRoot
    }
    $item = Get-Item -LiteralPath $path -Force
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Loopback state cannot be a link or reparse point.'
    }
    $state = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable -Depth 50 -ErrorAction Stop
    if ($state -isnot [System.Collections.IDictionary] -or
        [int]$state.schemaVersion -ne 1 -or [string]$state.provider -cne 'loopback' -or
        $state.simulated -ne $true) {
        throw 'Loopback state has an unsupported or malformed schema.'
    }
    foreach ($name in @('counters', 'workItems', 'pullRequests', 'deployments', 'approvals',
            'acceptances', 'telemetry', 'evidence', 'operations')) {
        if (-not $state.Contains($name)) { throw "Loopback state is missing '$name'." }
    }
    return $state
}

function New-FactoryLoopLoopbackResult {
    param(
        [Parameter(Mandatory)][string]$Domain,
        [Parameter(Mandatory)][string]$Action,
        [Parameter(Mandatory)][string]$OperationId,
        [Parameter(Mandatory)][ValidateSet('ok', 'waiting', 'blocked', 'not-found')][string]$Status,
        [Parameter(Mandatory)][string]$ProviderId,
        [hashtable]$Data = @{}
    )

    return [ordered]@{
        schemaVersion = 1
        domain = $Domain
        action = $Action
        operationId = $OperationId
        status = $Status
        providerId = $ProviderId
        data = $Data
    }
}

function Find-FactoryLoopLoopbackRecord {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Records,
        [string]$Id,
        [string]$OperationId,
        [string]$DedupeKey,
        [string]$Title
    )

    if (-not [string]::IsNullOrWhiteSpace($Id)) {
        return $Records | Where-Object { [string]$_.id -ceq $Id } | Select-Object -First 1
    }
    if (-not [string]::IsNullOrWhiteSpace($DedupeKey)) {
        return $Records | Where-Object { [string]$_.dedupeKey -ceq $DedupeKey } | Select-Object -First 1
    }
    if (-not [string]::IsNullOrWhiteSpace($OperationId)) {
        return $Records | Where-Object { [string]$_.operationId -ceq $OperationId } | Select-Object -First 1
    }
    if (-not [string]::IsNullOrWhiteSpace($Title)) {
        return $Records | Where-Object { [string]$_.title -ceq $Title } | Select-Object -First 1
    }
    return $null
}

function Invoke-FactoryLoopDemoBuildCheck {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$DemoRoot,
        [Parameter(Mandatory)][string]$Branch,
        [Parameter(Mandatory)][string]$SourceSha,
        [Parameter(Mandatory)][string]$BuildLineageId
    )

    $root = [System.IO.Path]::GetFullPath($DemoRoot)
    $branchHead = (git -C $root rev-parse $Branch).Trim()
    if ($LASTEXITCODE -ne 0 -or $branchHead -cne $SourceSha) {
        return [pscustomobject]@{
            status = 'blocked'
            reason = 'pull-request-head-changed'
            expectedHead = $SourceSha
            observedHead = $branchHead
        }
    }
    $worktree = Join-Path ([System.IO.Path]::GetTempPath()) ('factory-loop-check-' + [guid]::NewGuid().ToString('N'))
    try {
        git -C $root worktree add --quiet --detach $worktree $SourceSha
        if ($LASTEXITCODE -ne 0) { throw 'Unable to materialize the PR source for the local build check.' }
        $application = Join-Path $worktree 'application'
        $digest = Get-FactoryLoopApplicationDigest -ApplicationRoot $application
        $acceptance = Test-FactoryLoopArtifactAcceptance -ArtifactPath $application `
            -ExpectedDigest $digest -Environment test
        return [pscustomobject]@{
            status = [string]$acceptance.status
            headSha = $SourceSha
            buildLineageId = $BuildLineageId
            artifactDigest = $digest
            scenario = [string]$acceptance.scenario
            expected = [string]$acceptance.expected
            actual = [string]$acceptance.actual
        }
    }
    finally {
        if (Test-Path -LiteralPath $worktree) {
            git -C $root worktree remove --force $worktree
            if ($LASTEXITCODE -ne 0) { throw "Unable to remove temporary PR-check worktree '$worktree'." }
        }
    }
}

function Invoke-FactoryLoopLoopback {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet(
            'work-item', 'pull-request', 'deployment', 'version', 'acceptance', 'telemetry', 'evidence', 'approval'
        )][string]$Domain,
        [Parameter(Mandatory)][ValidateSet(
            'read', 'find', 'create', 'update', 'open', 'checks', 'merge', 'trigger', 'run', 'query',
            'publish', 'close', 'reconcile', 'approve', 'decline'
        )][string]$Action,
        [Parameter(Mandatory)][string]$OperationId,
        [Parameter(Mandatory)][string]$RepoRoot,
        [hashtable]$Payload = @{}
    )

    $state = Read-FactoryLoopLoopbackState -RepoRoot $RepoRoot
    $normalizedPayload = @{} + $Payload
    foreach ($name in @(
            'itemId', 'dedupeKey', 'title', 'requiredEnvironments', 'artifactDigest', 'status',
            'sourceSha', 'branch', 'pullRequestId', 'confirmMerge', 'expectedSourceSha', 'demoRoot',
            'environment', 'mergeCommit', 'deploymentId', 'targetOperationId', 'artifactPath',
            'accessible', 'coverage', 'cursor', 'evaluate', 'confirm', 'sanitized', 'commit'
        )) {
        if (-not $normalizedPayload.ContainsKey($name)) { $normalizedPayload[$name] = $null }
    }
    $Payload = $normalizedPayload
    $writeActions = @('create', 'update', 'open', 'merge', 'trigger', 'run', 'publish', 'close', 'approve', 'decline')
    $isWrite = $writeActions -contains $Action
    if ($isWrite) {
        $prior = @($state.operations | Where-Object { [string]$_.operationId -ceq $OperationId })
        if ($prior.Count -gt 1) { throw "Loopback operation key '$OperationId' is not unique." }
        if ($prior.Count -eq 1) {
            if ([string]$prior[0].domain -cne $Domain -or [string]$prior[0].action -cne $Action) {
                throw "Loopback operation key '$OperationId' was already used for another action."
            }
            return $prior[0].result
        }
    }

    $provider = "loopback:$Domain"
    $status = 'ok'
    $data = @{}
    switch ($Domain) {
        'work-item' {
            if ($Action -in @('read', 'update', 'close')) {
                $record = Find-FactoryLoopLoopbackRecord -Records @($state.workItems) -Id ([string]$Payload.itemId)
                if ($null -eq $record) { $status = 'not-found'; break }
                if ($Action -eq 'read') { $data = @{ item = $record }; break }
                if ($Action -eq 'close') {
                    $required = if ($Payload.requiredEnvironments) { @($Payload.requiredEnvironments) } else { @('test', 'prod') }
                    foreach ($environment in $required) {
                        $passed = @($state.acceptances | Where-Object {
                                [string]$_.environment -ceq [string]$environment -and
                                [string]$_.artifactDigest -ceq [string]$Payload.artifactDigest -and
                                [string]$_.status -ceq 'passed'
                            }).Count -gt 0
                        if (-not $passed) {
                            $status = 'blocked'
                            $data = @{ reason = 'required-acceptance-missing'; environment = [string]$environment }
                            break
                        }
                    }
                    if ($status -eq 'blocked') { break }
                    $published = @($state.evidence | Where-Object {
                            [string]$_.itemId -ceq [string]$Payload.itemId -and
                            [string]$_.artifactDigest -ceq [string]$Payload.artifactDigest -and
                            [string]$_.status -ceq 'published'
                        }).Count -gt 0
                    if (-not $published) {
                        $status = 'blocked'
                        $data = @{ reason = 'sanitized-evidence-not-published' }
                        break
                    }
                    $record.status = 'closed'
                }
                else {
                    if ($Payload.ContainsKey('title')) { $record.title = [string]$Payload.title }
                    if ($Payload.ContainsKey('status') -and [string]$Payload.status -in @('open', 'closed')) {
                        $record.status = [string]$Payload.status
                    }
                }
                $data = @{ item = $record }
                $provider = "loopback:work-item:$($record.id)"
            }
            elseif ($Action -eq 'find') {
                $record = Find-FactoryLoopLoopbackRecord -Records @($state.workItems) `
                    -DedupeKey ([string]$Payload.dedupeKey) -Title ([string]$Payload.title)
                if ($null -eq $record) { $status = 'not-found' } else {
                    $data = @{ item = $record }
                    $provider = "loopback:work-item:$($record.id)"
                }
            }
            elseif ($Action -eq 'create') {
                $record = Find-FactoryLoopLoopbackRecord -Records @($state.workItems) `
                    -DedupeKey ([string]$Payload.dedupeKey)
                if ($null -eq $record) {
                    if ([string]::IsNullOrWhiteSpace([string]$Payload.title)) { throw 'Work item title is required.' }
                    $state.counters.workItem = [int]$state.counters.workItem + 1
                    $id = 'WI-{0:d4}' -f [int]$state.counters.workItem
                    $record = [ordered]@{
                        id = $id
                        title = [string]$Payload.title
                        status = 'open'
                        dedupeKey = [string]$Payload.dedupeKey
                        operationId = $OperationId
                        simulated = $true
                    }
                    $state.workItems = @($state.workItems) + @($record)
                }
                $data = @{ item = $record }
                $provider = "loopback:work-item:$($record.id)"
            }
            else { throw "Unsupported loopback work-item action '$Action'." }
        }
        'pull-request' {
            if ($Action -eq 'open') {
                $sourceSha = [string]$Payload.sourceSha
                if ($sourceSha -notmatch '^[0-9a-f]{40,64}$' -or
                    [string]::IsNullOrWhiteSpace([string]$Payload.branch)) {
                    throw 'Pull-request open requires a full source SHA and branch.'
                }
                $state.counters.pullRequest = [int]$state.counters.pullRequest + 1
                $record = [ordered]@{
                    id = 'PR-{0:d4}' -f [int]$state.counters.pullRequest
                    itemId = [string]$Payload.itemId
                    buildLineageId = "loopback:$($Payload.itemId)"
                    branch = [string]$Payload.branch
                    sourceSha = $sourceSha
                    mergeCommit = $null
                    status = 'open'
                    checks = @{ status = 'pending'; headSha = $sourceSha }
                    operationId = $OperationId
                    simulated = $true
                }
                $state.pullRequests = @($state.pullRequests) + @($record)
                $data = @{ pullRequest = $record }
                $provider = "loopback:pull-request:$($record.id)"
            }
            elseif ($Action -in @('read', 'checks', 'reconcile', 'merge')) {
                $record = Find-FactoryLoopLoopbackRecord -Records @($state.pullRequests) `
                    -Id ([string]$Payload.pullRequestId) -OperationId ([string]$Payload.targetOperationId)
                if ($null -eq $record) { $status = 'not-found'; break }
                if ($Action -eq 'merge') {
                    if ($Payload.confirmMerge -ne $true) { $status = 'blocked'; $data = @{ reason = 'human-merge-required' }; break }
                    $check = Invoke-FactoryLoopDemoBuildCheck -DemoRoot ([string]$Payload.demoRoot) `
                        -Branch ([string]$record.branch) -SourceSha ([string]$record.sourceSha) `
                        -BuildLineageId ([string]$record.buildLineageId)
                    if ([string]$record.status -cne 'open' -or
                        [string]$check.status -cne 'passed' -or
                        [string]$Payload.expectedSourceSha -cne [string]$record.sourceSha) {
                        $status = 'blocked'
                        $data = @{ reason = 'pull-request-not-ready-for-merge'; pullRequest = $record; checks = $check }
                        break
                    }
                    $merge = Merge-FactoryLoopDemoPullRequest -DemoRoot ([string]$Payload.demoRoot) `
                        -Branch ([string]$record.branch) -ExpectedSourceSha ([string]$record.sourceSha) -ConfirmMerge
                    $record.mergeCommit = $merge.mergeCommit
                    $record.status = 'merged'
                }
                elseif ($Action -eq 'checks') {
                    if ($Payload.evaluate -ne $true) {
                        $status = 'waiting'
                        $data = @{ checks = $record.checks; pullRequestId = [string]$record.id }
                        break
                    }
                    $check = Invoke-FactoryLoopDemoBuildCheck -DemoRoot ([string]$Payload.demoRoot) `
                        -Branch ([string]$record.branch) -SourceSha ([string]$record.sourceSha) `
                        -BuildLineageId ([string]$record.buildLineageId)
                    $status = if ($check.status -eq 'blocked') { 'blocked' } else { 'ok' }
                    $data = @{ checks = $check; pullRequestId = [string]$record.id }
                    break
                }
                $provider = "loopback:pull-request:$($record.id)"
                $data = @{ pullRequest = $record }
            }
            else { throw "Unsupported loopback pull-request action '$Action'." }
        }
        'deployment' {
            if ($Action -eq 'trigger') {
                $environment = [string]$Payload.environment
                if ($environment -notin @('test', 'preprod', 'prod') -or
                    [string]$Payload.artifactDigest -notmatch '^[0-9a-f]{64}$') {
                    throw 'Deployment trigger requires a known environment and SHA-256 artifact digest.'
                }
                $pr = Find-FactoryLoopLoopbackRecord -Records @($state.pullRequests) `
                    -Id ([string]$Payload.pullRequestId)
                if ($null -eq $pr -or [string]$pr.status -cne 'merged') {
                    $status = 'blocked'; $data = @{ reason = 'human-merged-pull-request-required' }; break
                }
                if ($environment -eq 'prod') {
                    $approved = @($state.approvals | Where-Object {
                            [string]$_.artifactDigest -ceq [string]$Payload.artifactDigest -and
                            [string]$_.decision -ceq 'approved'
                        }).Count -gt 0
                    if (-not $approved) {
                        $status = 'blocked'; $data = @{ reason = 'artifact-specific-production-approval-required' }; break
                    }
                }
                if ([string]$Payload.sourceSha -cne [string]$pr.sourceSha -or
                    [string]$Payload.mergeCommit -cne [string]$pr.mergeCommit -or
                    [string]$Payload.artifactDigest -notmatch '^[0-9a-f]{64}$') {
                    $status = 'blocked'; $data = @{ reason = 'deployment-source-or-artifact-identity-mismatch' }; break
                }
                foreach ($old in @($state.deployments | Where-Object {
                            [string]$_.environment -ceq $environment -and
                            [string]$_.status -ceq 'succeeded'
                        })) { $old.status = 'superseded' }
                $state.counters.deployment = [int]$state.counters.deployment + 1
                $record = [ordered]@{
                    id = 'DEP-{0:d4}' -f [int]$state.counters.deployment
                    environment = $environment
                    status = 'succeeded'
                    sourceSha = [string]$Payload.sourceSha
                    mergeCommit = [string]$Payload.mergeCommit
                    artifactDigest = [string]$Payload.artifactDigest
                    version = [string]$Payload.artifactDigest
                    pullRequestId = [string]$Payload.pullRequestId
                    operationId = $OperationId
                    simulated = $true
                }
                $state.deployments = @($state.deployments) + @($record)
                $data = @{ deployment = $record }
                $provider = "loopback:deployment:$($record.id)"
            }
            elseif ($Action -in @('read', 'reconcile')) {
                $record = Find-FactoryLoopLoopbackRecord -Records @($state.deployments) `
                    -Id ([string]$Payload.deploymentId) -OperationId ([string]$Payload.targetOperationId)
                if ($null -eq $record -and $Payload.environment) {
                    $record = @($state.deployments | Where-Object {
                            [string]$_.environment -ceq [string]$Payload.environment
                        } | Select-Object -Last 1)
                    if (@($record).Count -eq 0) { $record = $null }
                }
                if ($null -eq $record) { $status = 'not-found' } else {
                    $data = @{ deployment = $record }
                    $provider = "loopback:deployment:$($record.id)"
                }
            }
            else { throw "Unsupported loopback deployment action '$Action'." }
        }
        'version' {
            if ($Action -ne 'read') { throw "Unsupported loopback version action '$Action'." }
            $deployment = @($state.deployments | Where-Object {
                    [string]$_.environment -ceq [string]$Payload.environment -and
                    [string]$_.status -ceq 'succeeded'
                } | Select-Object -Last 1)
            if ($deployment.Count -eq 0) { $status = 'not-found' } else {
                $record = $deployment[0]
                $data = @{ environment = [string]$Payload.environment; version = [string]$record.version; artifactDigest = [string]$record.artifactDigest }
                $provider = "loopback:deployment:$($record.id)"
            }
        }
        'acceptance' {
            if ($Action -ne 'run') { throw "Unsupported loopback acceptance action '$Action'." }
            $deployment = @($state.deployments | Where-Object {
                    [string]$_.environment -ceq [string]$Payload.environment -and
                    [string]$_.artifactDigest -ceq [string]$Payload.artifactDigest -and
                    [string]$_.status -ceq 'succeeded'
                } | Select-Object -Last 1)
            if ($deployment.Count -eq 0) {
                $status = 'blocked'; $data = @{ reason = 'matching-successful-deployment-required' }; break
            }
            $result = Test-FactoryLoopArtifactAcceptance -ArtifactPath ([string]$Payload.artifactPath) `
                -ExpectedDigest ([string]$Payload.artifactDigest) -Environment ([string]$Payload.environment)
            $acceptance = [ordered]@{
                environment = [string]$Payload.environment
                artifactDigest = [string]$Payload.artifactDigest
                status = [string]$result.status
                operationId = $OperationId
                simulated = $true
            }
            $state.acceptances = @($state.acceptances) + @($acceptance)
            $data = @{ acceptance = $acceptance }
            $provider = "loopback:acceptance:$($Payload.environment):$($Payload.artifactDigest)"
        }
        'telemetry' {
            if ($Action -ne 'query') { throw "Unsupported loopback telemetry action '$Action'." }
            if ($Payload.accessible -eq $false -or $Payload.coverage -eq $false) {
                $status = 'blocked'
                $data = @{ reason = if ($Payload.accessible -eq $false) { 'access-unavailable' } else { 'query-coverage-unavailable' } }
                break
            }
            $events = @($state.telemetry | Where-Object {
                    [string]$_.environment -ceq [string]$Payload.environment
                })
            $data = @{
                environment = [string]$Payload.environment
                covered = $true
                cursor = if ($Payload.cursor) { [string]$Payload.cursor } else { '0' }
                observedFromUtc = [string]$Payload.fromUtc
                observedThroughUtc = [string]$Payload.throughUtc
                events = $events
            }
        }
        'approval' {
            if ($Action -eq 'create') {
                $digest = [string]$Payload.artifactDigest
                if ($digest -notmatch '^[0-9a-f]{64}$') { throw 'Approval request requires a SHA-256 artifact digest.' }
                $record = @($state.approvals | Where-Object { [string]$_.artifactDigest -ceq $digest } |
                    Select-Object -Last 1)
                if ($record.Count -eq 0) {
                    $state.counters.approval = [int]$state.counters.approval + 1
                    $record = [ordered]@{
                        id = 'APR-{0:d4}' -f [int]$state.counters.approval
                        artifactDigest = $digest
                        decision = 'pending'
                        operationId = $OperationId
                        simulated = $true
                    }
                    $state.approvals = @($state.approvals) + @($record)
                    $status = 'waiting'
                }
                else { $record = $record[0]; $status = if ($record.decision -eq 'pending') { 'waiting' } else { 'ok' } }
                $data = @{ approval = $record; artifactDigest = $digest }
                $provider = "loopback:approval:$($record.id)"
            }
            elseif ($Action -in @('read', 'approve', 'decline')) {
                $digest = [string]$Payload.artifactDigest
                $record = @($state.approvals | Where-Object { [string]$_.artifactDigest -ceq $digest } |
                    Select-Object -Last 1)
                if ($record.Count -eq 0) { $status = 'not-found'; break }
                $record = $record[0]
                if ($Action -eq 'approve') {
                    if ($Payload.confirm -ne $true) { $status = 'blocked'; $data = @{ reason = 'explicit-operator-confirmation-required' }; break }
                    $record.decision = 'approved'
                }
                elseif ($Action -eq 'decline') {
                    if ($Payload.confirm -ne $true) { $status = 'blocked'; $data = @{ reason = 'explicit-operator-confirmation-required' }; break }
                    $record.decision = 'declined'
                }
                elseif ($record.decision -eq 'pending') { $status = 'waiting' }
                $data = @{ approval = $record; artifactDigest = $digest }
                $provider = "loopback:approval:$($record.id)"
            }
            else { throw "Unsupported loopback approval action '$Action'." }
        }
        'evidence' {
            if ($Action -eq 'publish') {
                if ($Payload.sanitized -ne $true -or
                    [string]$Payload.branch -notmatch '^factory-loop-evidence(?:[-/][A-Za-z0-9._-]+)?$') {
                    $status = 'blocked'; $data = @{ reason = 'sanitized-separate-evidence-branch-required' }; break
                }
                $record = [ordered]@{
                    itemId = [string]$Payload.itemId
                    artifactDigest = [string]$Payload.artifactDigest
                    branch = [string]$Payload.branch
                    commit = [string]$Payload.commit
                    status = 'published'
                    operationId = $OperationId
                    simulated = $true
                }
                $state.evidence = @($state.evidence) + @($record)
                $data = @{ evidence = $record }
                $provider = "loopback:evidence:$($Payload.itemId):$($Payload.artifactDigest)"
            }
            elseif ($Action -eq 'read') {
                $record = @($state.evidence | Where-Object {
                        [string]$_.itemId -ceq [string]$Payload.itemId -and
                        [string]$_.artifactDigest -ceq [string]$Payload.artifactDigest
                    } | Select-Object -Last 1)
                if ($record.Count -eq 0) { $status = 'not-found' } else {
                    $data = @{ evidence = $record[0] }
                    $provider = "loopback:evidence:$($Payload.itemId):$($Payload.artifactDigest)"
                }
            }
            else { throw "Unsupported loopback evidence action '$Action'." }
        }
    }

    $result = New-FactoryLoopLoopbackResult -Domain $Domain -Action $Action `
        -OperationId $OperationId -Status $status -ProviderId $provider -Data $data
    if ($isWrite -and $status -in @('ok', 'waiting')) {
        $state.operations = @($state.operations) + @([ordered]@{
                operationId = $OperationId
                domain = $Domain
                action = $Action
                result = $result
            })
        Save-FactoryLoopLoopbackState -RepoRoot $RepoRoot -State $state
    }
    return $result
}

Export-ModuleMember -Function @(
    'Resolve-FactoryLoopConsumerPath',
    'Get-FactoryLoopSetupPreview',
    'Invoke-FactoryLoopSetup',
    'ConvertFrom-FactoryLoopAdapterResult',
    'Invoke-FactoryLoopAdapter',
    'Get-FactoryLoopApplicationDigest',
    'New-FactoryLoopArtifactSnapshot',
    'Test-FactoryLoopArtifactAcceptance',
    'New-FactoryLoopDemoProject',
    'Merge-FactoryLoopDemoPullRequest',
    'Initialize-FactoryLoopLoopback',
    'Invoke-FactoryLoopLoopback'
)
