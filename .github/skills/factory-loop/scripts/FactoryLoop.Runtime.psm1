#requires -Version 7.0

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'FactoryLoop.psm1') -DisableNameChecking

function Write-FactoryLoopCheckpoint {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][System.Collections.IDictionary]$Checkpoint
    )

    $path = Resolve-FactoryLoopConsumerPath -RepoRoot $RepoRoot -RelativePath '.factory-loop/checkpoint.json'
    [void](New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force)
    $temporary = "$path.$([guid]::NewGuid().ToString('N')).tmp"
    try {
        $Checkpoint.revision = [int]$Checkpoint.revision + 1
        $json = ConvertTo-Json -InputObject $Checkpoint -Depth 40
        [System.IO.File]::WriteAllText($temporary, $json, [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::Move($temporary, $path, $true)
    }
    finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    }
}

function Read-FactoryLoopCheckpoint {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepoRoot)

    $path = Resolve-FactoryLoopConsumerPath -RepoRoot $RepoRoot -RelativePath '.factory-loop/checkpoint.json'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw 'No active factory-loop chain. Start one explicitly before polling.'
    }
    $item = Get-Item -LiteralPath $path -Force
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 -or
        $item.Length -gt 1048576) {
        throw 'Factory-loop checkpoint is linked or exceeds the 1 MiB limit.'
    }
    $checkpoint = Get-Content -LiteralPath $path -Raw |
        ConvertFrom-Json -AsHashtable -Depth 40 -ErrorAction Stop
    if ($checkpoint -isnot [System.Collections.IDictionary] -or
        [int]$checkpoint.schemaVersion -ne 1 -or
        [string]$checkpoint.provider -cnotin @('loopback', 'live') -or
        [string]::IsNullOrWhiteSpace([string]$checkpoint.chainId) -or
        [string]::IsNullOrWhiteSpace([string]$checkpoint.stage)) {
        throw 'Factory-loop checkpoint has an unsupported or malformed schema.'
    }
    return $checkpoint
}

function Get-FactoryLoopObjectItem {
    [CmdletBinding()]
    param(
        [AllowNull()][object]$Value,
        [Parameter(Mandatory)][string]$Name
    )

    if ($Value -is [System.Collections.IDictionary]) {
        if ($Value.Contains($Name)) { return $Value[$Name] }
        return $null
    }
    if ($null -ne $Value -and $null -ne $Value.PSObject.Properties[$Name]) {
        return $Value.PSObject.Properties[$Name].Value
    }
    return $null
}

function Get-FactoryLoopAdapterDataItem {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$Result,
        [Parameter(Mandatory)][string]$Name
    )

    return Get-FactoryLoopObjectItem -Value $Result.data -Name $Name
}

function Get-FactoryLoopCriteriaBaseline {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$PlanReference
    )
    $modulePath = Join-Path $RepoRoot '.github/skills/ci/scripts/DirectWorkflow.psm1'
    if (-not (Test-Path -LiteralPath $modulePath -PathType Leaf)) {
        throw 'Plan criteria validator is missing from the selected repository. Run factory-loop setup to bootstrap local CI dependencies.'
    }
    Import-Module -Name $modulePath -Force -DisableNameChecking
    $baseline = Test-PlanCriteriaBaseline -RepoRoot $RepoRoot -PlanReference $PlanReference
    if ($null -eq $baseline -or [string]$baseline.Status -cne 'ready') {
        throw "Plan '$PlanReference' is not admitted by Test-PlanCriteriaBaseline."
    }
    return $baseline
}

function Test-FactoryLoopCriteriaBaselineUnchanged {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][System.Collections.IDictionary]$Checkpoint
    )

    $directWorkflowPath = Join-Path $RepoRoot '.github/skills/ci/scripts/DirectWorkflow.psm1'
    if (-not (Test-Path -LiteralPath $directWorkflowPath -PathType Leaf)) {
        throw 'Plan criteria validator is missing; run factory-loop setup to bootstrap local CI dependencies.'
    }
    $planStatePath = Join-Path (Split-Path -Parent $directWorkflowPath) 'PlanState.psm1'
    if (-not (Test-Path -LiteralPath $planStatePath -PathType Leaf)) {
        throw "Plan criteria dependency is missing: $planStatePath"
    }
    Import-Module -Name $planStatePath -Force -DisableNameChecking

    $root = [System.IO.Path]::GetFullPath($RepoRoot)
    $plan = Resolve-Plan -Reference ([string]$Checkpoint.planReference) -RepoRoot $root
    $planPath = Join-Path $plan.Path 'plan.md'
    $context = New-PlanConfinementContext -PlanDir $plan.Path -RepoRoot $root
    $confinedPlan = Resolve-ConfinedPlanPath -Context $context -Path $planPath -PathType Leaf
    $relativePlanPath = [System.IO.Path]::GetRelativePath($root, $confinedPlan.Item.FullName).Replace('\', '/')
    $planText = Get-Content -LiteralPath $confinedPlan.Item.FullName -Raw
    $marker = (Get-PlanHeaderMarkers -Content $planText).PlanningConfirmed
    if ([string]$marker -cne [string]$Checkpoint.criteriaMarker) { return $false }

    $relativeAssets = [System.Collections.Generic.List[string]]::new()
    foreach ($kind in @('Intent', 'Requirements', 'Risks', 'Decisions')) {
        $currentAsset = Resolve-PlanAssetPath -PlanDir $plan.Path -Kind $kind -RepoRoot $root
        $confinedAsset = Resolve-ConfinedPlanPath -Context $context -Path $currentAsset -PathType Leaf
        $relativeAssets.Add(
            [System.IO.Path]::GetRelativePath($root, $confinedAsset.Item.FullName).Replace('\', '/')
        )
    }

    $headPlan = @(& git -C $root show "HEAD`:$relativePlanPath" 2>$null)
    if ($LASTEXITCODE -ne 0 -or
        [string](Get-PlanHeaderMarkers -Content ($headPlan -join "`n")).PlanningConfirmed -cne
        [string]$Checkpoint.criteriaMarker) {
        return $false
    }
    $indexPlan = @(& git -C $root show ":$relativePlanPath" 2>$null)
    if ($LASTEXITCODE -ne 0 -or
        [string](Get-PlanHeaderMarkers -Content ($indexPlan -join "`n")).PlanningConfirmed -cne
        [string]$Checkpoint.criteriaMarker) {
        return $false
    }
    $criteriaDiff = & git -C $root diff --quiet ([string]$Checkpoint.criteriaBaselineCommit) -- `
        $relativeAssets.ToArray()
    $diffExitCode = $LASTEXITCODE
    if ($diffExitCode -gt 1) {
        throw "Unable to compare confirmed criteria with baseline commit '$($Checkpoint.criteriaBaselineCommit)'."
    }
    if ($diffExitCode -ne 0) { return $false }
    return $true
}

function Read-FactoryLoopConfiguration {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepoRoot)

    $path = Resolve-FactoryLoopConsumerPath -RepoRoot $RepoRoot -RelativePath '.factory-loop/factory-loop.json'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Factory-loop configuration is missing. Run Setup-FactoryLoop.ps1 for the selected repository."
    }
    $item = Get-Item -LiteralPath $path -Force
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 -or
        $item.Length -gt 65536) {
        throw 'Factory-loop configuration is linked or exceeds the 65536-byte limit.'
    }
    $config = Get-Content -LiteralPath $path -Raw |
        ConvertFrom-Json -AsHashtable -Depth 20 -ErrorAction Stop
    if ($config -isnot [System.Collections.IDictionary] -or
        [int]$config.schemaVersion -ne 1 -or
        [int]$config.pollSeconds -lt 1 -or [int]$config.pollSeconds -gt 86400 -or
        [int]$config.observationSeconds -lt 1 -or [int]$config.observationSeconds -gt 86400 -or
        [string]$config.mode -cnotin @('loopback', 'live')) {
        throw 'Factory-loop configuration has an unsupported schema, mode, or interval.'
    }
    $configJson = ConvertTo-Json -InputObject $config -Depth 20 -Compress
    if ($configJson -match '(?i)"[^"]*(?:credential|secret|token|password|passphrase)[^"]*"\s*:') {
        throw 'Factory-loop configuration cannot contain credential or secret fields.'
    }
    return $config
}

function Initialize-FactoryLoopChain {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$ChainId,
        [Parameter(Mandatory)][string]$WorkItemId,
        [Parameter(Mandatory)][string]$PlanReference,
        [Parameter(Mandatory)][string]$Branch,
        [Parameter(Mandatory)][string]$SourceSha,
        [scriptblock]$Clock = { [datetime]::UtcNow }
    )

    if ($ChainId -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$') {
        throw 'Chain ID must be a stable identifier of at most 80 characters.'
    }
    if ($Branch -notmatch '^feature/[A-Za-z0-9][A-Za-z0-9._/-]{0,100}$' -or
        $Branch.Contains('..') -or $Branch.Contains('//') -or $Branch.EndsWith('/')) {
        throw 'Chain branch must be a bounded feature/* Git branch.'
    }
    if ($SourceSha -cnotmatch '^(?:[0-9a-f]{40}|[0-9a-f]{64})$' -or
        [string]::IsNullOrWhiteSpace($WorkItemId)) {
        throw 'Chain requires a full source commit ID and work-item provider ID.'
    }
    $root = [System.IO.Path]::GetFullPath($RepoRoot)
    $config = Read-FactoryLoopConfiguration -RepoRoot $root
    if ([string]$config.mode -cne 'loopback') {
        throw 'Live adapter dispatch is not available yet; select loopback mode for the MVP demo.'
    }
    $criteria = Get-FactoryLoopCriteriaBaseline -RepoRoot $root -PlanReference $PlanReference
    $branchHead = (git -C $root rev-parse $Branch).Trim()
    if ($LASTEXITCODE -ne 0 -or $branchHead -cne $SourceSha) {
        throw 'The selected implementation branch no longer points at the reviewed source SHA.'
    }

    $path = Resolve-FactoryLoopConsumerPath -RepoRoot $root -RelativePath '.factory-loop/checkpoint.json'
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        $existing = Read-FactoryLoopCheckpoint -RepoRoot $root
        if ([string]$existing.chainId -ceq $ChainId -and
            [string]$existing.workItemId -ceq $WorkItemId -and
            [string]$existing.planReference -ceq $PlanReference) {
            return $existing
        }
        if ([string]$existing.status -cne 'completed') {
            throw "Project already has active factory-loop chain '$($existing.chainId)' at stage '$($existing.stage)'."
        }
    }

    $checkpoint = [ordered]@{
        schemaVersion = 1
        revision = 0
        provider = 'loopback'
        simulated = $true
        chainId = $ChainId
        status = 'active'
        stage = 'create-pr'
        workItemId = $WorkItemId
        planReference = $PlanReference
        criteriaBaselineCommit = [string]$criteria.BaselineCommit
        criteriaMarker = [string]$criteria.Marker
        branch = $Branch
        expectedSourceSha = $SourceSha
        originalSourceSha = $SourceSha
        pullRequestSequence = 1
        pullRequestId = $null
        pullRequestProviderId = $null
        mergeCommit = $null
        artifactDigest = $null
        artifactPath = $null
        testDeploymentId = $null
        testDeploymentProviderId = $null
        productionDeploymentId = $null
        productionDeploymentProviderId = $null
        productionApprovalId = $null
        productionApprovalProviderId = $null
        telemetryStartedAtUtc = $null
        telemetryCursor = $null
        evidenceBranch = $null
        evidenceCommit = $null
        evidencePath = $null
        evidenceProviderId = $null
        evidenceRecordedAtUtc = $null
        bugWorkItemId = $null
        bugProviderId = $null
        failureKind = $null
        repairIncidentId = [string]$WorkItemId
        repairPullRequestsReserved = 0
        repairPullRequests = @()
        repairBuildLineages = @()
        repairBuildLineageId = $null
        lastRepairBranch = $null
        pendingOperation = $null
        pollCount = 0
        pollSeconds = [int]$config.pollSeconds
        observationSeconds = [int]$config.observationSeconds
        createdAtUtc = ([datetime](& $Clock)).ToUniversalTime().ToString('o')
        updatedAtUtc = ([datetime](& $Clock)).ToUniversalTime().ToString('o')
        outcome = 'running'
        reason = $null
    }
    Write-FactoryLoopCheckpoint -RepoRoot $root -Checkpoint $checkpoint
    return $checkpoint
}

function Invoke-FactoryLoopRecordedMutation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][System.Collections.IDictionary]$Checkpoint,
        [Parameter(Mandatory)][string]$Domain,
        [Parameter(Mandatory)][string]$Action,
        [Parameter(Mandatory)][string]$OperationId,
        [Parameter(Mandatory)][hashtable]$Payload,
        [Parameter(Mandatory)][scriptblock]$AdapterRunner,
        [scriptblock]$Clock = { [datetime]::UtcNow },
        [scriptblock]$BeforeDispatch,
        [scriptblock]$AfterDispatch
    )

    if ($null -ne $Checkpoint.pendingOperation) {
        $pending = $Checkpoint.pendingOperation
        if ([string]$pending.domain -cne $Domain -or
            [string]$pending.action -cne $Action -or
            [string]$pending.operationId -cne $OperationId) {
            throw 'Checkpoint has an unresolved mutation for a different operation; reconcile it before proceeding.'
        }
        $Payload = $pending.payload
    }
    else {
        $Checkpoint.pendingOperation = [ordered]@{
            domain = $Domain
            action = $Action
            operationId = $OperationId
            payload = $Payload
            requestedAtUtc = ([datetime](& $Clock)).ToUniversalTime().ToString('o')
        }
        $Checkpoint.updatedAtUtc = ([datetime](& $Clock)).ToUniversalTime().ToString('o')
        Write-FactoryLoopCheckpoint -RepoRoot $RepoRoot -Checkpoint $Checkpoint
    }

    if ($BeforeDispatch) { & $BeforeDispatch $Checkpoint.pendingOperation }
    $result = & $AdapterRunner $Domain $Action $OperationId $Payload
    if ($AfterDispatch) { & $AfterDispatch $result }
    return ConvertFrom-FactoryLoopAdapterResult -Result $result -Domain $Domain `
        -Action $Action -OperationId $OperationId
}

function New-FactoryLoopMergedArtifactSnapshot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$MergeCommit,
        [Parameter(Mandatory)][string]$SourceSha
    )

    if ($MergeCommit -cnotmatch '^(?:[0-9a-f]{40}|[0-9a-f]{64})$') {
        throw 'Artifact snapshot requires the verified merge commit.'
    }
    $root = [System.IO.Path]::GetFullPath($RepoRoot)
    $worktree = Join-Path ([System.IO.Path]::GetTempPath()) (
        'factory-loop-artifact-' + [guid]::NewGuid().ToString('N')
    )
    try {
        git -C $root worktree add --quiet --detach $worktree $MergeCommit
        if ($LASTEXITCODE -ne 0) {
            throw 'Unable to materialize the verified merge commit for artifact snapshotting.'
        }
        $artifactStore = Resolve-FactoryLoopConsumerPath -RepoRoot $root `
            -RelativePath '.factory-loop/artifacts'
        return New-FactoryLoopArtifactSnapshot `
            -ApplicationRoot (Join-Path $worktree 'application') `
            -ArtifactStore $artifactStore `
            -SourceSha $SourceSha `
            -MergeCommit $MergeCommit
    }
    finally {
        if (Test-Path -LiteralPath $worktree) {
            git -C $root worktree remove --force $worktree
            if ($LASTEXITCODE -ne 0) {
                throw "Unable to remove temporary artifact worktree '$worktree'."
            }
        }
    }
}

function Set-FactoryLoopBlocked {
    param(
        [System.Collections.IDictionary]$Checkpoint,
        [string]$Reason,
        [scriptblock]$Clock
    )

    $Checkpoint.status = 'blocked'
    $Checkpoint.stage = 'blocked'
    $Checkpoint.outcome = 'blocked'
    $Checkpoint.reason = $Reason
    $Checkpoint.updatedAtUtc = ([datetime](& $Clock)).ToUniversalTime().ToString('o')
}

function Resolve-FactoryLoopEvidencePath {
    param(
        [Parameter(Mandatory)][string]$Worktree,
        [Parameter(Mandatory)][string]$EvidenceKey
    )

    if ($EvidenceKey -cnotmatch '^[0-9a-f]{64}$') {
        throw 'Evidence key must be a SHA-256 digest.'
    }

    $path = [System.IO.Path]::GetFullPath($Worktree)
    $segments = @('docs', 'factory-loop', 'evidence', "$EvidenceKey.json")
    for ($index = 0; $index -lt $segments.Count; $index++) {
        $path = Join-Path $path $segments[$index]
        $isFile = $index -eq ($segments.Count - 1)
        if (Test-Path -LiteralPath $path) {
            $item = Get-Item -LiteralPath $path -Force -ErrorAction Stop
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Evidence path cannot contain symbolic links or reparse points.'
            }
            if ($isFile -eq [bool]$item.PSIsContainer) {
                throw 'Evidence path contains a file/directory type mismatch.'
            }
            continue
        }
        if (-not $isFile) {
            [void](New-Item -ItemType Directory -Path $path -ErrorAction Stop)
            $item = Get-Item -LiteralPath $path -Force -ErrorAction Stop
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Evidence path cannot contain symbolic links or reparse points.'
            }
        }
    }
    return $path
}

function New-FactoryLoopEvidenceCommit {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][System.Collections.IDictionary]$Checkpoint,
        [Parameter(Mandatory)][System.Collections.IDictionary]$Configuration
    )

    if ($Configuration.evidenceBranchDeploymentExcluded -ne $true) {
        throw 'Evidence publication is blocked until the project owner verifies deployment-trigger exclusion.'
    }
    $branchBase = [string]$Configuration.evidenceBranch
    if ($branchBase -notmatch '^factory-loop-evidence(?:[-/][A-Za-z0-9._-]+)?$' -or
        $branchBase.Contains('..')) {
        throw 'Configured evidence branch is outside the factory-loop evidence namespace.'
    }
    $branch = "$branchBase/$($Checkpoint.chainId)"
    $root = [System.IO.Path]::GetFullPath($RepoRoot)
    $worktree = Join-Path ([System.IO.Path]::GetTempPath()) (
        'factory-loop-evidence-' + [guid]::NewGuid().ToString('N')
    )
    $secretGuardPath = Join-Path $root '.github/skills/ci/scripts/SecretGuard.psm1'
    if (-not (Test-Path -LiteralPath $secretGuardPath -PathType Leaf)) {
        throw 'Evidence publication requires the locally bootstrapped secret guard.'
    }
    Import-Module -Name $secretGuardPath -Force -DisableNameChecking

    try {
        $telemetryStarted = if ($Checkpoint.telemetryStartedAtUtc -is [datetime]) {
            $Checkpoint.telemetryStartedAtUtc.ToUniversalTime().ToString('o')
        } else {
            [datetime]::Parse(
                [string]$Checkpoint.telemetryStartedAtUtc,
                [System.Globalization.CultureInfo]::InvariantCulture,
                [System.Globalization.DateTimeStyles]::RoundtripKind
            ).ToUniversalTime().ToString('o')
        }
        if ([string]$Checkpoint.artifactDigest -cnotmatch '^[0-9a-f]{64}$' -or
            [string]::IsNullOrWhiteSpace([string]$Checkpoint.evidenceRecordedAtUtc)) {
            throw 'Evidence requires an immutable artifact digest and a persisted recording time.'
        }
        $recordedAt = [datetime]::Parse(
            [string]$Checkpoint.evidenceRecordedAtUtc,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::RoundtripKind
        ).ToUniversalTime().ToString('o')
        $existingBranch = git -C $root show-ref --verify --quiet "refs/heads/$branch" 2>$null
        $branchExists = $LASTEXITCODE -eq 0
        if ($branchExists) {
            git -C $root worktree add --quiet $worktree $branch
        }
        else {
            git -C $root worktree add --quiet -b $branch $worktree ([string]$Checkpoint.mergeCommit)
        }
        if ($LASTEXITCODE -ne 0) {
            throw 'Unable to create an isolated worktree on the evidence branch.'
        }
        git -C $worktree merge-base --is-ancestor ([string]$Checkpoint.mergeCommit) HEAD
        if ($LASTEXITCODE -ne 0) {
            throw 'Evidence branch does not descend from the validated merge commit.'
        }

        $evidenceIdentity = ConvertTo-Json -InputObject @(
            [string]$Checkpoint.chainId,
            [string]$Checkpoint.workItemId,
            [string]$Checkpoint.artifactDigest
        ) -Compress
        $evidenceKey = [Convert]::ToHexString(
            [System.Security.Cryptography.SHA256]::HashData(
                [System.Text.Encoding]::UTF8.GetBytes($evidenceIdentity)
            )
        ).ToLowerInvariant()
        $relativePath = "docs/factory-loop/evidence/$evidenceKey.json"
        $path = Resolve-FactoryLoopEvidencePath -Worktree $worktree -EvidenceKey $evidenceKey
        $record = [ordered]@{
            schemaVersion = 1
            simulated = [bool]$Checkpoint.simulated
            chainId = [string]$Checkpoint.chainId
            workItemId = [string]$Checkpoint.workItemId
            pullRequestId = [string]$Checkpoint.pullRequestId
            sourceSha = [string]$Checkpoint.expectedSourceSha
            mergeCommit = [string]$Checkpoint.mergeCommit
            artifactDigest = [string]$Checkpoint.artifactDigest
            testDeploymentId = [string]$Checkpoint.testDeploymentId
            productionDeploymentId = [string]$Checkpoint.productionDeploymentId
            productionApprovalId = [string]$Checkpoint.productionApprovalId
            testAcceptance = 'passed'
            productionAcceptance = 'passed'
            telemetryStartedAtUtc = $telemetryStarted
            evidenceRecordedAtUtc = $recordedAt
        }
        $json = ConvertTo-Json -InputObject $record -Depth 10
        if (@(Find-HighConfidenceSecret -Value $json).Count -gt 0) {
            throw 'Sanitized evidence failed the repository secret guard.'
        }
        [void](New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force)
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            if ((Get-Content -LiteralPath $path -Raw) -cne $json) {
                throw 'Evidence branch already contains a conflicting artifact record.'
            }
        }
        else {
            [System.IO.File]::WriteAllText($path, $json, [System.Text.UTF8Encoding]::new($false))
            git -C $worktree add -- $relativePath
            if ($LASTEXITCODE -ne 0) { throw 'Unable to stage sanitized factory-loop evidence.' }
            git -C $worktree -c user.name='Factory Loop Evidence' `
                -c user.email='factory-loop-evidence@localhost' commit --quiet -m `
                "Record sanitized factory-loop evidence for $($Checkpoint.workItemId)"
            if ($LASTEXITCODE -ne 0) { throw 'Unable to commit sanitized factory-loop evidence.' }
        }
        $commit = (git -C $worktree rev-parse HEAD).Trim()
        if ($LASTEXITCODE -ne 0 -or $commit -cnotmatch '^(?:[0-9a-f]{40}|[0-9a-f]{64})$') {
            throw 'Evidence commit identity could not be verified.'
        }
        return [pscustomobject]@{
            branch = $branch
            commit = $commit
            relativePath = $relativePath
        }
    }
    finally {
        if (Test-Path -LiteralPath $worktree) {
            git -C $root worktree remove --force $worktree
            if ($LASTEXITCODE -ne 0) {
                throw 'Unable to remove the isolated evidence worktree.'
            }
        }
    }
}

function New-FactoryLoopBugIncident {
    param(
        [string]$RepoRoot,
        [System.Collections.IDictionary]$Checkpoint,
        [scriptblock]$AdapterRunner,
        [scriptblock]$Clock,
        [string]$FailureKind
    )

    $operationId = "$($Checkpoint.chainId):bug:$($Checkpoint.artifactDigest)"
    $bug = Invoke-FactoryLoopRecordedMutation -RepoRoot $RepoRoot -Checkpoint $Checkpoint `
        -Domain work-item -Action create -OperationId $operationId `
        -Payload @{
            title = "Factory-loop $FailureKind for artifact $($Checkpoint.artifactDigest)"
            dedupeKey = $operationId
        } -AdapterRunner $AdapterRunner -Clock $Clock
    $Checkpoint.pendingOperation = $null
    if ([string]$bug.status -cnotin @('ok', 'waiting') -or
        [string]::IsNullOrWhiteSpace([string]$bug.data.item.id) -or
        [string]::IsNullOrWhiteSpace([string]$bug.providerId)) {
        Set-FactoryLoopBlocked -Checkpoint $Checkpoint `
            -Reason 'bug-incident-identity-unavailable' -Clock $Clock
    }
    else {
        $Checkpoint.bugWorkItemId = [string]$bug.data.item.id
        $Checkpoint.bugProviderId = [string]$bug.providerId
        $Checkpoint.failureKind = $FailureKind
        $Checkpoint.stage = 'repair-needed'
        $Checkpoint.outcome = 'bug-opened'
        $Checkpoint.reason = 'acceptance-or-telemetry-failed'
        $Checkpoint.status = 'active'
        $Checkpoint.updatedAtUtc = ([datetime](& $Clock)).ToUniversalTime().ToString('o')
    }
    Write-FactoryLoopCheckpoint -RepoRoot $RepoRoot -Checkpoint $Checkpoint
    return [pscustomobject]@{
        outcome = [string]$Checkpoint.outcome
        status = [string]$Checkpoint.status
        stage = [string]$Checkpoint.stage
        reason = [string]$Checkpoint.reason
        bugWorkItemId = [string]$Checkpoint.bugWorkItemId
        aiCalls = 0
    }
}

function Invoke-FactoryLoopTick {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [scriptblock]$Clock = { [datetime]::UtcNow },
        [scriptblock]$AdapterRunner,
        [scriptblock]$BeforeDispatch,
        [scriptblock]$AfterDispatch
    )

    $root = [System.IO.Path]::GetFullPath($RepoRoot)
    $lockPath = Resolve-FactoryLoopConsumerPath -RepoRoot $root -RelativePath '.factory-loop/loop.lock'
    [void](New-Item -ItemType Directory -Path (Split-Path -Parent $lockPath) -Force)
    $lockStream = $null
    try {
        try {
            $lockStream = [System.IO.File]::Open(
                $lockPath,
                [System.IO.FileMode]::OpenOrCreate,
                [System.IO.FileAccess]::ReadWrite,
                [System.IO.FileShare]::None
            )
        }
        catch [System.IO.IOException] {
            return [pscustomobject]@{ outcome = 'locked'; status = 'waiting'; aiCalls = 0 }
        }
        $lockStream.SetLength(0)
        $lockBytes = [System.Text.Encoding]::UTF8.GetBytes(
            "pid=$PID`nstarted=$(([datetime](& $Clock)).ToUniversalTime().ToString('o'))"
        )
        $lockStream.Write($lockBytes, 0, $lockBytes.Length)
        $lockStream.Flush($true)

        $checkpoint = Read-FactoryLoopCheckpoint -RepoRoot $root
        if ([string]$checkpoint.status -ceq 'completed') {
            return [pscustomobject]@{ outcome = 'completed'; status = 'completed'; stage = [string]$checkpoint.stage; aiCalls = 0 }
        }

        $baselineRequired = [string]$checkpoint.stage -ceq 'create-pr' -or
            $null -ne $checkpoint.pendingOperation
        if ($baselineRequired -and
            -not (Test-FactoryLoopCriteriaBaselineUnchanged -RepoRoot $root -Checkpoint $checkpoint)) {
            $checkpoint.status = 'blocked'
            $checkpoint.outcome = 'blocked'
            $checkpoint.reason = 'confirmed-criteria-baseline-changed'
            $checkpoint.updatedAtUtc = ([datetime](& $Clock)).ToUniversalTime().ToString('o')
            Write-FactoryLoopCheckpoint -RepoRoot $root -Checkpoint $checkpoint
            return [pscustomobject]@{ outcome = 'blocked'; status = 'blocked'; reason = [string]$checkpoint.reason; aiCalls = 0 }
        }

        if (-not $AdapterRunner) {
            $AdapterRunner = {
                param($Domain, $Action, $OperationId, $Payload)
                Invoke-FactoryLoopLoopback -RepoRoot $root -Domain $Domain -Action $Action `
                    -OperationId $OperationId -Payload $Payload
            }.GetNewClosure()
        }
        $checkpoint.pollCount = [int]$checkpoint.pollCount + 1
        $checkpoint.updatedAtUtc = ([datetime](& $Clock)).ToUniversalTime().ToString('o')
        switch ([string]$checkpoint.stage) {
            'create-pr' {
                $operationId = "$($checkpoint.chainId):pull-request:open:$([int]$checkpoint.pullRequestSequence)"
                $payload = @{
                    itemId = [string]$checkpoint.workItemId
                    branch = [string]$checkpoint.branch
                    sourceSha = [string]$checkpoint.expectedSourceSha
                }
                $result = Invoke-FactoryLoopRecordedMutation -RepoRoot $root `
                    -Checkpoint $checkpoint -Domain pull-request -Action open `
                    -OperationId $operationId -Payload $payload -AdapterRunner $AdapterRunner `
                    -Clock $Clock -BeforeDispatch $BeforeDispatch -AfterDispatch $AfterDispatch
                if ([string]$result.status -cne 'ok' -or
                    [string]$result.data.pullRequest.sourceSha -cne [string]$checkpoint.expectedSourceSha) {
                    return [pscustomobject]@{
                        outcome = 'blocked'
                        status = [string]$result.status
                        reason = 'pull-request-open-not-reconciled'
                        aiCalls = 0
                    }
                }
                $checkpoint.pullRequestId = [string]$result.data.pullRequest.id
                $checkpoint.pullRequestProviderId = [string]$result.providerId
                $checkpoint.pendingOperation = $null
                $checkpoint.stage = 'awaiting-checks'
                $checkpoint.updatedAtUtc = ([datetime](& $Clock)).ToUniversalTime().ToString('o')
                Write-FactoryLoopCheckpoint -RepoRoot $root -Checkpoint $checkpoint
                return [pscustomobject]@{
                    outcome = 'progressed'
                    status = 'waiting'
                    stage = [string]$checkpoint.stage
                    pullRequestId = [string]$checkpoint.pullRequestId
                    providerId = [string]$checkpoint.pullRequestProviderId
                    aiCalls = 0
                }
            }
            'awaiting-checks' {
                $operationId = "$($checkpoint.chainId):pull-request:checks:$($checkpoint.pollCount)"
                $result = & $AdapterRunner 'pull-request' 'checks' $operationId @{
                    pullRequestId = [string]$checkpoint.pullRequestId
                    demoRoot = $root
                    evaluate = $true
                }
                $result = ConvertFrom-FactoryLoopAdapterResult -Result $result -Domain pull-request `
                    -Action checks -OperationId $operationId
                $checks = Get-FactoryLoopAdapterDataItem -Result $result -Name 'checks'
                $checksStatus = [string](Get-FactoryLoopObjectItem -Value $checks -Name 'status')
                $checksHeadSha = [string](Get-FactoryLoopObjectItem -Value $checks -Name 'headSha')
                $checksReason = [string](Get-FactoryLoopObjectItem -Value $checks -Name 'reason')
                $lineageId = [string](Get-FactoryLoopObjectItem -Value $checks -Name 'buildLineageId')
                if ($null -eq $checks) {
                    $checkpoint.status = 'blocked'
                    $checkpoint.stage = 'blocked'
                    $checkpoint.outcome = 'blocked'
                    $checkpoint.reason = 'pull-request-check-inconclusive'
                }
                elseif ([string]$result.status -ceq 'blocked' -or $checksStatus -ceq 'blocked') {
                    $checkpoint.status = 'blocked'
                    $checkpoint.stage = 'blocked'
                    $checkpoint.outcome = 'blocked'
                    $checkpoint.reason = if ($checksReason) { $checksReason } else { 'pull-request-check-inconclusive' }
                }
                elseif ($checksHeadSha -cne [string]$checkpoint.expectedSourceSha) {
                    $checkpoint.status = 'blocked'
                    $checkpoint.stage = 'blocked'
                    $checkpoint.outcome = 'blocked'
                    $checkpoint.reason = 'pull-request-head-changed'
                }
                elseif ($checksStatus -ceq 'failed') {
                    if ($lineageId -notmatch '^[A-Za-z0-9][A-Za-z0-9._:/-]{0,127}$') {
                        $checkpoint.status = 'blocked'
                        $checkpoint.stage = 'blocked'
                        $checkpoint.outcome = 'blocked'
                        $checkpoint.reason = 'build-lineage-identity-missing'
                    }
                    else {
                        $lineages = @($checkpoint.repairBuildLineages)
                        $lineage = @($lineages | Where-Object { [string]$_.id -ceq $lineageId } |
                            Select-Object -First 1)
                        if ($lineage.Count -eq 0) {
                            $lineages += [ordered]@{ id = $lineageId; correctiveCalls = 0 }
                            $checkpoint.repairBuildLineages = $lineages
                        }
                        $checkpoint.repairBuildLineageId = $lineageId
                        $checkpoint.stage = 'build-failed'
                        $checkpoint.outcome = 'build-failed'
                        $checkpoint.reason = 'checks-failed'
                    }
                }
                elseif ($checksStatus -cin @('cancelled', 'canceled', 'superseded')) {
                    $checkpoint.status = 'blocked'
                    $checkpoint.stage = 'blocked'
                    $checkpoint.outcome = 'blocked'
                    $checkpoint.reason = 'checks-cancelled'
                }
                elseif ($checksStatus -ceq 'passed') {
                    if ($lineageId -notmatch '^[A-Za-z0-9][A-Za-z0-9._:/-]{0,127}$') {
                        Set-FactoryLoopBlocked -Checkpoint $checkpoint `
                            -Reason 'build-lineage-identity-missing' -Clock $Clock
                    }
                    else {
                        $checkpoint.stage = 'awaiting-merge'
                        $checkpoint.outcome = 'waiting-for-human-merge'
                        $checkpoint.reason = $null
                    }
                }
                elseif ($checksStatus -ceq 'pending') {
                    $checkpoint.outcome = 'waiting-for-checks'
                }
                else {
                    $checkpoint.status = 'blocked'
                    $checkpoint.stage = 'blocked'
                    $checkpoint.outcome = 'blocked'
                    $checkpoint.reason = 'checks-status-unknown'
                }
                if ($checksStatus -in @('failed', 'passed', 'pending') -and
                    $lineageId -match '^[A-Za-z0-9][A-Za-z0-9._:/-]{0,127}$') {
                    $checkpoint.repairBuildLineageId = $lineageId
                    $lineages = @($checkpoint.repairBuildLineages)
                    if (@($lineages | Where-Object { [string]$_.id -ceq $lineageId }).Count -eq 0) {
                        $lineages += [ordered]@{ id = $lineageId; correctiveCalls = 0 }
                        $checkpoint.repairBuildLineages = $lineages
                    }
                }
                $checkpoint.updatedAtUtc = ([datetime](& $Clock)).ToUniversalTime().ToString('o')
                Write-FactoryLoopCheckpoint -RepoRoot $root -Checkpoint $checkpoint
                return [pscustomobject]@{
                    outcome = [string]$checkpoint.outcome
                    status = $checksStatus
                    stage = [string]$checkpoint.stage
                    reason = [string]$checkpoint.reason
                    incidentId = [string]$checkpoint.repairIncidentId
                    buildLineageId = [string]$checkpoint.repairBuildLineageId
                    artifactDigest = [string](Get-FactoryLoopObjectItem -Value $checks -Name 'artifactDigest')
                    nextPollAfterSeconds = [int]$checkpoint.pollSeconds
                    aiCalls = 0
                }
            }
            'awaiting-merge' {
                $operationId = "$($checkpoint.chainId):pull-request:read:$($checkpoint.pollCount)"
                $result = & $AdapterRunner 'pull-request' 'read' $operationId @{
                    pullRequestId = [string]$checkpoint.pullRequestId
                }
                $result = ConvertFrom-FactoryLoopAdapterResult -Result $result -Domain pull-request `
                    -Action read -OperationId $operationId
                $pr = $result.data.pullRequest
                if ([string]$result.providerId -cne [string]$checkpoint.pullRequestProviderId -or
                    [string]$pr.sourceSha -cne [string]$checkpoint.expectedSourceSha) {
                    $checkpoint.status = 'blocked'
                    $checkpoint.stage = 'blocked'
                    $checkpoint.outcome = 'blocked'
                    $checkpoint.reason = 'pull-request-identity-or-head-changed'
                }
                elseif ([string]$pr.status -ceq 'closed') {
                    $checkpoint.status = 'blocked'
                    $checkpoint.stage = 'blocked'
                    $checkpoint.outcome = 'blocked'
                    $checkpoint.reason = 'pull-request-closed-unmerged'
                }
                elseif ([string]$pr.status -ceq 'merged') {
                    if ([string]$pr.mergeCommit -cnotmatch '^(?:[0-9a-f]{40}|[0-9a-f]{64})$') {
                        throw 'Merged pull request is missing its actual merge commit identity.'
                    }
                    $checkpoint.mergeCommit = [string]$pr.mergeCommit
                    $checkpoint.stage = 'test-deployment'
                    $checkpoint.outcome = 'progressed'
                    $checkpoint.reason = $null
                }
                else {
                    $checkpoint.outcome = 'waiting-for-human-merge'
                }
                $checkpoint.updatedAtUtc = ([datetime](& $Clock)).ToUniversalTime().ToString('o')
                Write-FactoryLoopCheckpoint -RepoRoot $root -Checkpoint $checkpoint
                return [pscustomobject]@{
                    outcome = [string]$checkpoint.outcome
                    status = [string]$pr.status
                    stage = [string]$checkpoint.stage
                    mergeCommit = [string]$checkpoint.mergeCommit
                    reason = [string]$checkpoint.reason
                    nextPollAfterSeconds = [int]$checkpoint.pollSeconds
                    aiCalls = 0
                }
            }
            'test-deployment' {
                $snapshot = New-FactoryLoopMergedArtifactSnapshot -RepoRoot $root `
                    -MergeCommit ([string]$checkpoint.mergeCommit) `
                    -SourceSha ([string]$checkpoint.expectedSourceSha)
                $checkpoint.artifactDigest = [string]$snapshot.artifactDigest
                $checkpoint.artifactPath = [string]$snapshot.artifactPath
                $operationId = "$($checkpoint.chainId):deployment:test:$($snapshot.artifactDigest)"
                $deploymentResult = Invoke-FactoryLoopRecordedMutation `
                    -RepoRoot $root -Checkpoint $checkpoint -Domain deployment -Action trigger `
                    -OperationId $operationId -Payload @{
                        environment = 'test'
                        pullRequestId = [string]$checkpoint.pullRequestId
                        sourceSha = [string]$checkpoint.expectedSourceSha
                        mergeCommit = [string]$checkpoint.mergeCommit
                        artifactDigest = [string]$snapshot.artifactDigest
                        artifactPath = [string]$snapshot.artifactPath
                    } -AdapterRunner $AdapterRunner -Clock $Clock `
                    -BeforeDispatch $BeforeDispatch -AfterDispatch $AfterDispatch
                $checkpoint.pendingOperation = $null
                $deployment = Get-FactoryLoopAdapterDataItem -Result $deploymentResult -Name 'deployment'
                if ([string]$deploymentResult.status -cne 'ok' -or
                    [string]::IsNullOrWhiteSpace([string]$deployment.id) -or
                    [string]$deployment.environment -cne 'test' -or
                    [string]$deployment.artifactDigest -cne [string]$snapshot.artifactDigest -or
                    [string]$deployment.sourceSha -cne [string]$checkpoint.expectedSourceSha -or
                    [string]$deployment.mergeCommit -cne [string]$checkpoint.mergeCommit) {
                    Set-FactoryLoopBlocked -Checkpoint $checkpoint `
                        -Reason 'test-deployment-identity-unavailable' -Clock $Clock
                }
                else {
                    $checkpoint.testDeploymentId = [string]$deployment.id
                    $checkpoint.testDeploymentProviderId = [string]$deploymentResult.providerId
                    $checkpoint.stage = 'test-deployment-check'
                    $checkpoint.outcome = 'progressed'
                    $checkpoint.reason = $null
                }
                $checkpoint.updatedAtUtc = ([datetime](& $Clock)).ToUniversalTime().ToString('o')
                Write-FactoryLoopCheckpoint -RepoRoot $root -Checkpoint $checkpoint
                return [pscustomobject]@{
                    outcome = [string]$checkpoint.outcome
                    status = [string]$checkpoint.status
                    stage = [string]$checkpoint.stage
                    artifactDigest = [string]$checkpoint.artifactDigest
                    mergeCommit = [string]$checkpoint.mergeCommit
                    reason = [string]$checkpoint.reason
                    nextPollAfterSeconds = [int]$checkpoint.pollSeconds
                    aiCalls = 0
                }
            }
            'test-deployment-check' {
                $operationId = "$($checkpoint.chainId):deployment:test:verify:$($checkpoint.pollCount)"
                $deploymentResult = & $AdapterRunner 'deployment' 'read' $operationId @{
                    deploymentId = [string]$checkpoint.testDeploymentId
                    environment = 'test'
                }
                $deploymentResult = ConvertFrom-FactoryLoopAdapterResult -Result $deploymentResult `
                    -Domain deployment -Action read -OperationId $operationId
                $deployment = Get-FactoryLoopAdapterDataItem -Result $deploymentResult -Name 'deployment'
                if ([string]$deploymentResult.status -cne 'ok' -or
                    [string]$deploymentResult.providerId -cne [string]$checkpoint.testDeploymentProviderId -or
                    [string]$deployment.id -cne [string]$checkpoint.testDeploymentId -or
                    [string]$deployment.status -cne 'succeeded' -or
                    [string]$deployment.environment -cne 'test' -or
                    [string]$deployment.artifactDigest -cne [string]$checkpoint.artifactDigest -or
                    [string]$deployment.sourceSha -cne [string]$checkpoint.expectedSourceSha -or
                    [string]$deployment.mergeCommit -cne [string]$checkpoint.mergeCommit) {
                    Set-FactoryLoopBlocked -Checkpoint $checkpoint `
                        -Reason 'test-deployment-identity-or-status-mismatch' -Clock $Clock
                }
                else {
                    $checkpoint.stage = 'test-version'
                    $checkpoint.outcome = 'progressed'
                    $checkpoint.reason = $null
                }
                $checkpoint.updatedAtUtc = ([datetime](& $Clock)).ToUniversalTime().ToString('o')
                Write-FactoryLoopCheckpoint -RepoRoot $root -Checkpoint $checkpoint
                return [pscustomobject]@{
                    outcome = [string]$checkpoint.outcome
                    status = [string]$checkpoint.status
                    stage = [string]$checkpoint.stage
                    artifactDigest = [string]$checkpoint.artifactDigest
                    reason = [string]$checkpoint.reason
                    aiCalls = 0
                }
            }
            'test-version' {
                $operationId = "$($checkpoint.chainId):version:test:$($checkpoint.pollCount)"
                $versionResult = & $AdapterRunner 'version' 'read' $operationId @{ environment = 'test' }
                $versionResult = ConvertFrom-FactoryLoopAdapterResult -Result $versionResult `
                    -Domain version -Action read -OperationId $operationId
                if ([string]$versionResult.status -cne 'ok' -or
                    [string]$versionResult.providerId -cne [string]$checkpoint.testDeploymentProviderId -or
                    [string]$versionResult.data.version -cne [string]$checkpoint.artifactDigest -or
                    [string]$versionResult.data.artifactDigest -cne [string]$checkpoint.artifactDigest) {
                    Set-FactoryLoopBlocked -Checkpoint $checkpoint `
                        -Reason 'test-running-version-does-not-match-artifact' -Clock $Clock
                }
                else {
                    $checkpoint.stage = 'test-acceptance'
                    $checkpoint.outcome = 'progressed'
                    $checkpoint.reason = $null
                }
                $checkpoint.updatedAtUtc = ([datetime](& $Clock)).ToUniversalTime().ToString('o')
                Write-FactoryLoopCheckpoint -RepoRoot $root -Checkpoint $checkpoint
                return [pscustomobject]@{
                    outcome = [string]$checkpoint.outcome
                    status = [string]$checkpoint.status
                    stage = [string]$checkpoint.stage
                    artifactDigest = [string]$checkpoint.artifactDigest
                    reason = [string]$checkpoint.reason
                    aiCalls = 0
                }
            }
            'test-acceptance' {
                $operationId = "$($checkpoint.chainId):acceptance:test:$($checkpoint.artifactDigest)"
                $acceptanceResult = Invoke-FactoryLoopRecordedMutation `
                    -RepoRoot $root -Checkpoint $checkpoint -Domain acceptance -Action run `
                    -OperationId $operationId -Payload @{
                        environment = 'test'
                        artifactDigest = [string]$checkpoint.artifactDigest
                        artifactPath = [string]$checkpoint.artifactPath
                        deploymentId = [string]$checkpoint.testDeploymentId
                    } -AdapterRunner $AdapterRunner -Clock $Clock `
                    -BeforeDispatch $BeforeDispatch -AfterDispatch $AfterDispatch
                $checkpoint.pendingOperation = $null
                if ([string]$acceptanceResult.status -cne 'ok' -or
                    [string]$acceptanceResult.data.acceptance.artifactDigest -cne [string]$checkpoint.artifactDigest -or
                    [string]$acceptanceResult.data.acceptance.environment -cne 'test' -or
                    [string]$acceptanceResult.data.acceptance.status -cnotin @('passed', 'failed')) {
                    Set-FactoryLoopBlocked -Checkpoint $checkpoint `
                        -Reason 'test-acceptance-result-inconclusive' -Clock $Clock
                    Write-FactoryLoopCheckpoint -RepoRoot $root -Checkpoint $checkpoint
                    return [pscustomobject]@{
                        outcome = 'blocked'
                        status = 'blocked'
                        stage = [string]$checkpoint.stage
                        reason = [string]$checkpoint.reason
                        aiCalls = 0
                    }
                }
                if ([string]$acceptanceResult.data.acceptance.status -ceq 'failed') {
                    return New-FactoryLoopBugIncident -RepoRoot $root -Checkpoint $checkpoint `
                        -AdapterRunner $AdapterRunner -Clock $Clock -FailureKind 'test-acceptance'
                }
                $checkpoint.telemetryStartedAtUtc = ([datetime](& $Clock)).ToUniversalTime().ToString('o')
                $checkpoint.stage = 'telemetry-observation'
                $checkpoint.outcome = 'test-acceptance-passed'
                $checkpoint.reason = $null
                $checkpoint.updatedAtUtc = ([datetime](& $Clock)).ToUniversalTime().ToString('o')
                Write-FactoryLoopCheckpoint -RepoRoot $root -Checkpoint $checkpoint
                return [pscustomobject]@{
                    outcome = [string]$checkpoint.outcome
                    status = 'passed'
                    stage = [string]$checkpoint.stage
                    artifactDigest = [string]$checkpoint.artifactDigest
                    aiCalls = 0
                }
            }
            'telemetry-observation' {
                $now = ([datetime](& $Clock)).ToUniversalTime()
                $startedAtValue = $checkpoint.telemetryStartedAtUtc
                $startedAt = if ($startedAtValue -is [datetime]) {
                    $startedAtValue.ToUniversalTime()
                } else {
                    [datetime]::Parse(
                        [string]$startedAtValue,
                        [System.Globalization.CultureInfo]::InvariantCulture,
                        [System.Globalization.DateTimeStyles]::RoundtripKind
                    ).ToUniversalTime()
                }
                $windowEnd = $startedAt.AddSeconds([int]$checkpoint.observationSeconds)
                $operationId = "$($checkpoint.chainId):telemetry:test:$($checkpoint.pollCount)"
                $telemetryResult = & $AdapterRunner 'telemetry' 'query' $operationId @{
                    environment = 'test'
                    artifactDigest = [string]$checkpoint.artifactDigest
                    fromUtc = $startedAt.ToString('o')
                    throughUtc = $now.ToString('o')
                    cursor = [string]$checkpoint.telemetryCursor
                    accessible = $true
                    coverage = $true
                }
                $telemetryResult = ConvertFrom-FactoryLoopAdapterResult -Result $telemetryResult `
                    -Domain telemetry -Action query -OperationId $operationId
                $data = $telemetryResult.data
                if ([string]$telemetryResult.status -cne 'ok' -or $data.covered -ne $true -or
                    [string]::IsNullOrWhiteSpace([string]$data.observedFromUtc) -or
                    [string]::IsNullOrWhiteSpace([string]$data.observedThroughUtc)) {
                    Set-FactoryLoopBlocked -Checkpoint $checkpoint `
                        -Reason 'telemetry-access-or-coverage-inconclusive' -Clock $Clock
                }
                else {
                    $observedFrom = [datetime]::MinValue
                    $observedThrough = [datetime]::MinValue
                    $fromValid = [datetime]::TryParse([string]$data.observedFromUtc, [ref]$observedFrom)
                    $throughValid = [datetime]::TryParse([string]$data.observedThroughUtc, [ref]$observedThrough)
                    if (-not $fromValid -or -not $throughValid -or
                        $observedFrom.ToUniversalTime() -gt $startedAt -or
                        $observedThrough.ToUniversalTime() -lt $startedAt) {
                        Set-FactoryLoopBlocked -Checkpoint $checkpoint `
                            -Reason 'telemetry-window-coverage-incomplete' -Clock $Clock
                    }
                    elseif (@($data.events | Where-Object {
                                [string]$_.status -cin @('failed', 'error') -or
                                [string]$_.severity -ceq 'critical'
                            }).Count -gt 0) {
                        $checkpoint.telemetryCursor = [string]$data.cursor
                        $checkpoint.pendingOperation = $null
                        return New-FactoryLoopBugIncident -RepoRoot $root -Checkpoint $checkpoint `
                            -AdapterRunner $AdapterRunner -Clock $Clock -FailureKind 'test-telemetry'
                    }
                    elseif ($now -lt $windowEnd -or $observedThrough.ToUniversalTime() -lt $windowEnd) {
                        $checkpoint.telemetryCursor = [string]$data.cursor
                        $checkpoint.outcome = 'observing-test-telemetry'
                        $checkpoint.updatedAtUtc = $now.ToString('o')
                    }
                    else {
                        $checkpoint.telemetryCursor = [string]$data.cursor
                        $checkpoint.stage = 'awaiting-production-approval'
                        $checkpoint.outcome = 'test-accepted'
                        $checkpoint.reason = $null
                        $checkpoint.updatedAtUtc = $now.ToString('o')
                    }
                }
                Write-FactoryLoopCheckpoint -RepoRoot $root -Checkpoint $checkpoint
                return [pscustomobject]@{
                    outcome = [string]$checkpoint.outcome
                    status = [string]$checkpoint.status
                    stage = [string]$checkpoint.stage
                    artifactDigest = [string]$checkpoint.artifactDigest
                    reason = [string]$checkpoint.reason
                    nextPollAfterSeconds = [int]$checkpoint.pollSeconds
                    aiCalls = 0
                }
            }
            'awaiting-production-approval' {
                if ([string]::IsNullOrWhiteSpace([string]$checkpoint.productionApprovalId)) {
                    $operationId = "$($checkpoint.chainId):approval:create:prod:$($checkpoint.artifactDigest)"
                    $approvalResult = Invoke-FactoryLoopRecordedMutation `
                        -RepoRoot $root -Checkpoint $checkpoint -Domain approval -Action create `
                        -OperationId $operationId -Payload @{
                            artifactDigest = [string]$checkpoint.artifactDigest
                        } -AdapterRunner $AdapterRunner -Clock $Clock `
                        -BeforeDispatch $BeforeDispatch -AfterDispatch $AfterDispatch
                    $checkpoint.pendingOperation = $null
                    if ([string]$approvalResult.status -cnotin @('ok', 'waiting') -or
                        [string]$approvalResult.data.approval.artifactDigest -cne [string]$checkpoint.artifactDigest -or
                        [string]::IsNullOrWhiteSpace([string]$approvalResult.data.approval.id)) {
                        Set-FactoryLoopBlocked -Checkpoint $checkpoint `
                            -Reason 'artifact-specific-production-approval-unavailable' -Clock $Clock
                    }
                    else {
                        $checkpoint.productionApprovalId = [string]$approvalResult.data.approval.id
                        $checkpoint.productionApprovalProviderId = [string]$approvalResult.providerId
                        $checkpoint.outcome = if ([string]$approvalResult.data.approval.decision -ceq 'approved') {
                            'production-approved'
                        } else { 'waiting-for-production-approval' }
                    }
                }
                else {
                    $operationId = "$($checkpoint.chainId):approval:read:prod:$($checkpoint.pollCount)"
                    $approvalResult = & $AdapterRunner 'approval' 'read' $operationId @{
                        artifactDigest = [string]$checkpoint.artifactDigest
                    }
                    $approvalResult = ConvertFrom-FactoryLoopAdapterResult -Result $approvalResult `
                        -Domain approval -Action read -OperationId $operationId
                    $approval = $approvalResult.data.approval
                    if ([string]$approvalResult.status -cne 'ok' -or
                        [string]$approval.id -cne [string]$checkpoint.productionApprovalId -or
                        [string]$approval.artifactDigest -cne [string]$checkpoint.artifactDigest -or
                        [string]$approvalResult.providerId -cne [string]$checkpoint.productionApprovalProviderId) {
                        Set-FactoryLoopBlocked -Checkpoint $checkpoint `
                            -Reason 'production-approval-identity-unavailable' -Clock $Clock
                    }
                    elseif ([string]$approval.decision -ceq 'declined') {
                        Set-FactoryLoopBlocked -Checkpoint $checkpoint `
                            -Reason 'production-promotion-declined' -Clock $Clock
                    }
                    elseif ([string]$approval.decision -ceq 'approved') {
                        $checkpoint.stage = 'production-deployment'
                        $checkpoint.outcome = 'production-approved'
                        $checkpoint.reason = $null
                    }
                    elseif ([string]$approval.decision -ceq 'pending') {
                        $checkpoint.outcome = 'waiting-for-production-approval'
                    }
                    else {
                        Set-FactoryLoopBlocked -Checkpoint $checkpoint `
                            -Reason 'production-approval-decision-unknown' -Clock $Clock
                    }
                }
                $checkpoint.updatedAtUtc = ([datetime](& $Clock)).ToUniversalTime().ToString('o')
                Write-FactoryLoopCheckpoint -RepoRoot $root -Checkpoint $checkpoint
                return [pscustomobject]@{
                    outcome = [string]$checkpoint.outcome
                    status = [string]$checkpoint.status
                    stage = [string]$checkpoint.stage
                    artifactDigest = [string]$checkpoint.artifactDigest
                    approvalId = [string]$checkpoint.productionApprovalId
                    reason = [string]$checkpoint.reason
                    aiCalls = 0
                }
            }
            'production-deployment' {
                $operationId = "$($checkpoint.chainId):deployment:prod:$($checkpoint.artifactDigest)"
                $deploymentResult = Invoke-FactoryLoopRecordedMutation `
                    -RepoRoot $root -Checkpoint $checkpoint -Domain deployment -Action trigger `
                    -OperationId $operationId -Payload @{
                        environment = 'prod'
                        pullRequestId = [string]$checkpoint.pullRequestId
                        sourceSha = [string]$checkpoint.expectedSourceSha
                        mergeCommit = [string]$checkpoint.mergeCommit
                        artifactDigest = [string]$checkpoint.artifactDigest
                        artifactPath = [string]$checkpoint.artifactPath
                    } -AdapterRunner $AdapterRunner -Clock $Clock `
                    -BeforeDispatch $BeforeDispatch -AfterDispatch $AfterDispatch
                $checkpoint.pendingOperation = $null
                $deployment = Get-FactoryLoopAdapterDataItem -Result $deploymentResult -Name 'deployment'
                if ([string]$deploymentResult.status -cne 'ok' -or
                    [string]::IsNullOrWhiteSpace([string]$deployment.id) -or
                    [string]$deployment.environment -cne 'prod' -or
                    [string]$deployment.artifactDigest -cne [string]$checkpoint.artifactDigest -or
                    [string]$deployment.sourceSha -cne [string]$checkpoint.expectedSourceSha -or
                    [string]$deployment.mergeCommit -cne [string]$checkpoint.mergeCommit) {
                    Set-FactoryLoopBlocked -Checkpoint $checkpoint `
                        -Reason 'production-deployment-identity-unavailable' -Clock $Clock
                }
                else {
                    $checkpoint.productionDeploymentId = [string]$deployment.id
                    $checkpoint.productionDeploymentProviderId = [string]$deploymentResult.providerId
                    $checkpoint.stage = 'production-deployment-check'
                    $checkpoint.outcome = 'progressed'
                    $checkpoint.reason = $null
                }
                $checkpoint.updatedAtUtc = ([datetime](& $Clock)).ToUniversalTime().ToString('o')
                Write-FactoryLoopCheckpoint -RepoRoot $root -Checkpoint $checkpoint
                return [pscustomobject]@{
                    outcome = [string]$checkpoint.outcome
                    status = [string]$checkpoint.status
                    stage = [string]$checkpoint.stage
                    artifactDigest = [string]$checkpoint.artifactDigest
                    reason = [string]$checkpoint.reason
                    aiCalls = 0
                }
            }
            'production-deployment-check' {
                $operationId = "$($checkpoint.chainId):deployment:prod:verify:$($checkpoint.pollCount)"
                $deploymentResult = & $AdapterRunner 'deployment' 'read' $operationId @{
                    deploymentId = [string]$checkpoint.productionDeploymentId
                    environment = 'prod'
                }
                $deploymentResult = ConvertFrom-FactoryLoopAdapterResult -Result $deploymentResult `
                    -Domain deployment -Action read -OperationId $operationId
                $deployment = Get-FactoryLoopAdapterDataItem -Result $deploymentResult -Name 'deployment'
                if ([string]$deploymentResult.status -cne 'ok' -or
                    [string]$deploymentResult.providerId -cne [string]$checkpoint.productionDeploymentProviderId -or
                    [string]$deployment.id -cne [string]$checkpoint.productionDeploymentId -or
                    [string]$deployment.status -cne 'succeeded' -or
                    [string]$deployment.environment -cne 'prod' -or
                    [string]$deployment.artifactDigest -cne [string]$checkpoint.artifactDigest -or
                    [string]$deployment.sourceSha -cne [string]$checkpoint.expectedSourceSha -or
                    [string]$deployment.mergeCommit -cne [string]$checkpoint.mergeCommit) {
                    Set-FactoryLoopBlocked -Checkpoint $checkpoint `
                        -Reason 'production-deployment-identity-or-status-mismatch' -Clock $Clock
                }
                else {
                    $checkpoint.stage = 'production-version'
                    $checkpoint.outcome = 'progressed'
                    $checkpoint.reason = $null
                }
                $checkpoint.updatedAtUtc = ([datetime](& $Clock)).ToUniversalTime().ToString('o')
                Write-FactoryLoopCheckpoint -RepoRoot $root -Checkpoint $checkpoint
                return [pscustomobject]@{
                    outcome = [string]$checkpoint.outcome
                    status = [string]$checkpoint.status
                    stage = [string]$checkpoint.stage
                    artifactDigest = [string]$checkpoint.artifactDigest
                    reason = [string]$checkpoint.reason
                    aiCalls = 0
                }
            }
            'production-version' {
                $operationId = "$($checkpoint.chainId):version:prod:$($checkpoint.pollCount)"
                $versionResult = & $AdapterRunner 'version' 'read' $operationId @{ environment = 'prod' }
                $versionResult = ConvertFrom-FactoryLoopAdapterResult -Result $versionResult `
                    -Domain version -Action read -OperationId $operationId
                if ([string]$versionResult.status -cne 'ok' -or
                    [string]$versionResult.providerId -cne [string]$checkpoint.productionDeploymentProviderId -or
                    [string]$versionResult.data.version -cne [string]$checkpoint.artifactDigest -or
                    [string]$versionResult.data.artifactDigest -cne [string]$checkpoint.artifactDigest) {
                    Set-FactoryLoopBlocked -Checkpoint $checkpoint `
                        -Reason 'production-running-version-does-not-match-artifact' -Clock $Clock
                }
                else {
                    $checkpoint.stage = 'production-acceptance'
                    $checkpoint.outcome = 'progressed'
                    $checkpoint.reason = $null
                }
                $checkpoint.updatedAtUtc = ([datetime](& $Clock)).ToUniversalTime().ToString('o')
                Write-FactoryLoopCheckpoint -RepoRoot $root -Checkpoint $checkpoint
                return [pscustomobject]@{
                    outcome = [string]$checkpoint.outcome
                    status = [string]$checkpoint.status
                    stage = [string]$checkpoint.stage
                    artifactDigest = [string]$checkpoint.artifactDigest
                    reason = [string]$checkpoint.reason
                    aiCalls = 0
                }
            }
            'production-acceptance' {
                $operationId = "$($checkpoint.chainId):acceptance:prod:$($checkpoint.artifactDigest)"
                $acceptanceResult = Invoke-FactoryLoopRecordedMutation `
                    -RepoRoot $root -Checkpoint $checkpoint -Domain acceptance -Action run `
                    -OperationId $operationId -Payload @{
                        environment = 'prod'
                        artifactDigest = [string]$checkpoint.artifactDigest
                        artifactPath = [string]$checkpoint.artifactPath
                        deploymentId = [string]$checkpoint.productionDeploymentId
                    } -AdapterRunner $AdapterRunner -Clock $Clock `
                    -BeforeDispatch $BeforeDispatch -AfterDispatch $AfterDispatch
                $checkpoint.pendingOperation = $null
                if ([string]$acceptanceResult.status -cne 'ok' -or
                    [string]$acceptanceResult.data.acceptance.artifactDigest -cne [string]$checkpoint.artifactDigest -or
                    [string]$acceptanceResult.data.acceptance.environment -cne 'prod' -or
                    [string]$acceptanceResult.data.acceptance.status -cnotin @('passed', 'failed')) {
                    Set-FactoryLoopBlocked -Checkpoint $checkpoint `
                        -Reason 'production-acceptance-result-inconclusive' -Clock $Clock
                }
                elseif ([string]$acceptanceResult.data.acceptance.status -ceq 'failed') {
                    return New-FactoryLoopBugIncident -RepoRoot $root -Checkpoint $checkpoint `
                        -AdapterRunner $AdapterRunner -Clock $Clock -FailureKind 'production-acceptance'
                }
                else {
                    $checkpoint.stage = 'awaiting-evidence'
                    $checkpoint.outcome = 'production-accepted'
                    $checkpoint.reason = $null
                }
                $checkpoint.updatedAtUtc = ([datetime](& $Clock)).ToUniversalTime().ToString('o')
                Write-FactoryLoopCheckpoint -RepoRoot $root -Checkpoint $checkpoint
                return [pscustomobject]@{
                    outcome = [string]$checkpoint.outcome
                    status = [string]$checkpoint.status
                    stage = [string]$checkpoint.stage
                    artifactDigest = [string]$checkpoint.artifactDigest
                    reason = [string]$checkpoint.reason
                    aiCalls = 0
                }
            }
            'awaiting-evidence' {
                $configuration = Read-FactoryLoopConfiguration -RepoRoot $root
                if ($configuration.evidenceBranchDeploymentExcluded -ne $true) {
                    Set-FactoryLoopBlocked -Checkpoint $checkpoint `
                        -Reason 'evidence-branch-deployment-exclusion-unverified' -Clock $Clock
                    Write-FactoryLoopCheckpoint -RepoRoot $root -Checkpoint $checkpoint
                    return [pscustomobject]@{
                        outcome = 'blocked'
                        status = 'blocked'
                        stage = [string]$checkpoint.stage
                        reason = [string]$checkpoint.reason
                        aiCalls = 0
                    }
                }
                if ([string]::IsNullOrWhiteSpace([string]$checkpoint.evidenceRecordedAtUtc)) {
                    $checkpoint.evidenceRecordedAtUtc = ([datetime](& $Clock)).ToUniversalTime().ToString('o')
                    $checkpoint.updatedAtUtc = $checkpoint.evidenceRecordedAtUtc
                    Write-FactoryLoopCheckpoint -RepoRoot $root -Checkpoint $checkpoint
                }
                $evidenceCommit = New-FactoryLoopEvidenceCommit -RepoRoot $root `
                    -Checkpoint $checkpoint -Configuration $configuration
                $checkpoint.evidenceBranch = [string]$evidenceCommit.branch
                $checkpoint.evidenceCommit = [string]$evidenceCommit.commit
                $checkpoint.evidencePath = [string]$evidenceCommit.relativePath
                $checkpoint.stage = 'publishing-evidence'
                $checkpoint.outcome = 'evidence-ready'
                $checkpoint.updatedAtUtc = ([datetime](& $Clock)).ToUniversalTime().ToString('o')
                Write-FactoryLoopCheckpoint -RepoRoot $root -Checkpoint $checkpoint
                return [pscustomobject]@{
                    outcome = [string]$checkpoint.outcome
                    status = [string]$checkpoint.status
                    stage = [string]$checkpoint.stage
                    artifactDigest = [string]$checkpoint.artifactDigest
                    evidenceBranch = [string]$checkpoint.evidenceBranch
                    evidenceCommit = [string]$checkpoint.evidenceCommit
                    aiCalls = 0
                }
            }
            'publishing-evidence' {
                $evidenceOperationId = "$($checkpoint.chainId):evidence:publish:$($checkpoint.artifactDigest)"
                $evidenceResult = Invoke-FactoryLoopRecordedMutation `
                    -RepoRoot $root -Checkpoint $checkpoint -Domain evidence -Action publish `
                    -OperationId $evidenceOperationId -Payload @{
                        itemId = [string]$checkpoint.workItemId
                        artifactDigest = [string]$checkpoint.artifactDigest
                        branch = [string]$checkpoint.evidenceBranch
                        commit = [string]$checkpoint.evidenceCommit
                        path = [string]$checkpoint.evidencePath
                        sanitized = $true
                    } -AdapterRunner $AdapterRunner -Clock $Clock `
                    -BeforeDispatch $BeforeDispatch -AfterDispatch $AfterDispatch
                $checkpoint.pendingOperation = $null
                $evidence = Get-FactoryLoopAdapterDataItem -Result $evidenceResult -Name 'evidence'
                if ([string]$evidenceResult.status -cne 'ok' -or
                    [string]$evidence.branch -cne [string]$checkpoint.evidenceBranch -or
                    [string]$evidence.commit -cne [string]$checkpoint.evidenceCommit -or
                    [string]$evidence.artifactDigest -cne [string]$checkpoint.artifactDigest) {
                    Set-FactoryLoopBlocked -Checkpoint $checkpoint `
                        -Reason 'sanitized-evidence-publication-inconclusive' -Clock $Clock
                    Write-FactoryLoopCheckpoint -RepoRoot $root -Checkpoint $checkpoint
                    return [pscustomobject]@{
                        outcome = 'blocked'
                        status = 'blocked'
                        stage = [string]$checkpoint.stage
                        reason = [string]$checkpoint.reason
                        aiCalls = 0
                    }
                }
                $checkpoint.evidenceProviderId = [string]$evidenceResult.providerId
                $checkpoint.stage = if ([string]::IsNullOrWhiteSpace([string]$checkpoint.bugWorkItemId)) {
                    'closing-work-item'
                } else { 'closing-bug' }
                $checkpoint.outcome = 'evidence-published'
                $checkpoint.updatedAtUtc = ([datetime](& $Clock)).ToUniversalTime().ToString('o')
                Write-FactoryLoopCheckpoint -RepoRoot $root -Checkpoint $checkpoint
                return [pscustomobject]@{
                    outcome = [string]$checkpoint.outcome
                    status = [string]$checkpoint.status
                    stage = [string]$checkpoint.stage
                    artifactDigest = [string]$checkpoint.artifactDigest
                    evidenceBranch = [string]$checkpoint.evidenceBranch
                    evidenceCommit = [string]$checkpoint.evidenceCommit
                    aiCalls = 0
                }
            }
            'closing-bug' {
                $bugCloseOperationId = "$($checkpoint.chainId):bug-close:$($checkpoint.bugWorkItemId):$($checkpoint.artifactDigest)"
                $bugClose = Invoke-FactoryLoopRecordedMutation `
                    -RepoRoot $root -Checkpoint $checkpoint -Domain work-item -Action update `
                    -OperationId $bugCloseOperationId -Payload @{
                        itemId = [string]$checkpoint.bugWorkItemId
                        status = 'closed'
                        resolvedByArtifactDigest = [string]$checkpoint.artifactDigest
                    } -AdapterRunner $AdapterRunner -Clock $Clock `
                    -BeforeDispatch $BeforeDispatch -AfterDispatch $AfterDispatch
                $checkpoint.pendingOperation = $null
                $bug = Get-FactoryLoopAdapterDataItem -Result $bugClose -Name 'item'
                if ([string]$bugClose.status -cne 'ok' -or [string]$bug.status -cne 'closed') {
                    Set-FactoryLoopBlocked -Checkpoint $checkpoint `
                        -Reason 'repaired-bug-closure-inconclusive' -Clock $Clock
                }
                else {
                    $checkpoint.stage = 'closing-work-item'
                    $checkpoint.outcome = 'bug-resolved'
                    $checkpoint.reason = $null
                }
                $checkpoint.updatedAtUtc = ([datetime](& $Clock)).ToUniversalTime().ToString('o')
                Write-FactoryLoopCheckpoint -RepoRoot $root -Checkpoint $checkpoint
                return [pscustomobject]@{
                    outcome = [string]$checkpoint.outcome
                    status = [string]$checkpoint.status
                    stage = [string]$checkpoint.stage
                    bugWorkItemId = [string]$checkpoint.bugWorkItemId
                    reason = [string]$checkpoint.reason
                    aiCalls = 0
                }
            }
            'closing-work-item' {
                $closeOperationId = "$($checkpoint.chainId):work-item:close:$($checkpoint.workItemId):$($checkpoint.artifactDigest)"
                $closeResult = Invoke-FactoryLoopRecordedMutation `
                    -RepoRoot $root -Checkpoint $checkpoint -Domain work-item -Action close `
                    -OperationId $closeOperationId -Payload @{
                        itemId = [string]$checkpoint.workItemId
                        artifactDigest = [string]$checkpoint.artifactDigest
                        requiredEnvironments = @('test', 'prod')
                    } -AdapterRunner $AdapterRunner -Clock $Clock `
                    -BeforeDispatch $BeforeDispatch -AfterDispatch $AfterDispatch
                $checkpoint.pendingOperation = $null
                $closedItem = Get-FactoryLoopAdapterDataItem -Result $closeResult -Name 'item'
                if ([string]$closeResult.status -cne 'ok' -or [string]$closedItem.status -cne 'closed') {
                    Set-FactoryLoopBlocked -Checkpoint $checkpoint `
                        -Reason 'work-item-closure-inconclusive' -Clock $Clock
                }
                else {
                    $checkpoint.status = 'completed'
                    $checkpoint.stage = 'completed'
                    $checkpoint.outcome = 'completed'
                    $checkpoint.reason = $null
                }
                $checkpoint.updatedAtUtc = ([datetime](& $Clock)).ToUniversalTime().ToString('o')
                Write-FactoryLoopCheckpoint -RepoRoot $root -Checkpoint $checkpoint
                return [pscustomobject]@{
                    outcome = [string]$checkpoint.outcome
                    status = [string]$checkpoint.status
                    stage = [string]$checkpoint.stage
                    artifactDigest = [string]$checkpoint.artifactDigest
                    evidenceBranch = [string]$checkpoint.evidenceBranch
                    evidenceCommit = [string]$checkpoint.evidenceCommit
                    reason = [string]$checkpoint.reason
                    aiCalls = 0
                }
            }
            'repair-needed' {
                return [pscustomobject]@{
                    outcome = 'repair-needed'
                    status = 'waiting'
                    stage = [string]$checkpoint.stage
                    bugWorkItemId = [string]$checkpoint.bugWorkItemId
                    artifactDigest = [string]$checkpoint.artifactDigest
                    buildLineageId = [string]$checkpoint.repairBuildLineageId
                    nextPollAfterSeconds = [int]$checkpoint.pollSeconds
                    aiCalls = 0
                }
            }
            'build-failed' {
                return [pscustomobject]@{
                    outcome = 'build-failed'
                    status = 'blocked'
                    stage = [string]$checkpoint.stage
                    reason = [string]$checkpoint.reason
                    aiCalls = 0
                }
            }
            'blocked' {
                return [pscustomobject]@{
                    outcome = 'blocked'
                    status = 'blocked'
                    stage = [string]$checkpoint.stage
                    reason = [string]$checkpoint.reason
                    aiCalls = 0
                }
            }
            default { throw "Unsupported factory-loop checkpoint stage '$($checkpoint.stage)'." }
        }
    }
    finally {
        if ($null -ne $lockStream) { $lockStream.Dispose() }
    }
}

function Start-FactoryLoopRepairInvocationCore {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$PlanReference
    )

    $root = [System.IO.Path]::GetFullPath($RepoRoot)
    $checkpoint = Read-FactoryLoopCheckpoint -RepoRoot $root
    if ([string]$checkpoint.status -cne 'active' -or
        [string]$checkpoint.stage -cnotin @('build-failed', 'repair-needed') -or
        [string]::IsNullOrWhiteSpace([string]$checkpoint.repairIncidentId) -or
        [string]::IsNullOrWhiteSpace([string]$checkpoint.repairBuildLineageId)) {
        throw 'Factory repair is allowed only for an active chain stopped on an identified failed build.'
    }

    $ciScripts = Join-Path $root '.github/skills/ci/scripts'
    Import-Module (Join-Path $ciScripts 'DirectWorkflow.psm1') -Force -DisableNameChecking
    Import-Module (Join-Path $ciScripts 'PlanState.psm1') -Force -DisableNameChecking
    $requestedPlan = Resolve-Plan -RepoRoot $root -Reference $PlanReference
    $chainPlan = Resolve-Plan -RepoRoot $root -Reference ([string]$checkpoint.planReference)
    if ([string]$requestedPlan.Id -cne [string]$chainPlan.Id) {
        throw 'Factory repair plan does not match the active chain.'
    }
    $baseline = Test-PlanCriteriaBaseline -RepoRoot $root -PlanReference ([string]$checkpoint.planReference)
    if ([string]$baseline.Status -cne 'ready' -or
        [string]$baseline.BaselineCommit -cne [string]$checkpoint.criteriaBaselineCommit -or
        [string]$baseline.Marker -cne [string]$checkpoint.criteriaMarker) {
        throw 'Factory repair is blocked because the confirmed criteria baseline is not unchanged.'
    }

    $lineages = @($checkpoint.repairBuildLineages)
    $lineage = @($lineages | Where-Object {
            [string]$_.id -ceq [string]$checkpoint.repairBuildLineageId
        } | Select-Object -First 1)
    if ($lineage.Count -ne 1) {
        throw 'Factory repair checkpoint has no unique record for the failed build lineage.'
    }
    if ([int]$checkpoint.repairPullRequestsReserved -ge 2 -or
        [int]$lineage[0].correctiveCalls -ge 2) {
        throw 'Factory repair budget exhausted: at most two repair PRs per incident and two corrective calls per build lineage.'
    }

    $attempt = [int]$lineage[0].correctiveCalls + 1
    $incidentSlug = [regex]::Replace(
        [string]$checkpoint.repairIncidentId,
        '[^A-Za-z0-9._-]',
        '-'
    )
    $incidentSlug = [regex]::Replace($incidentSlug, '\.{2,}', '-').TrimEnd('.')
    if ([string]::IsNullOrWhiteSpace($incidentSlug)) { $incidentSlug = 'incident' }
    if ($incidentSlug -notmatch '^[A-Za-z0-9]') { $incidentSlug = "incident-$incidentSlug" }
    if ($incidentSlug.Length -gt 60) { $incidentSlug = $incidentSlug.Substring(0, 60) }
    $branch = "factory-repair/$incidentSlug/$attempt"
    $branchHead = git -C $root rev-parse --verify --quiet $branch 2>$null
    if ($LASTEXITCODE -eq 0) {
        if ([string]$branchHead -cne [string]$checkpoint.expectedSourceSha) {
            throw 'Existing factory repair branch does not match the reserved repair source SHA.'
        }
    }
    else {
        git -C $root branch $branch ([string]$checkpoint.expectedSourceSha)
        if ($LASTEXITCODE -ne 0) {
            throw 'Unable to create a separate factory repair branch from the failed source SHA.'
        }
    }
    $lineage[0].correctiveCalls = $attempt
    $checkpoint.repairBuildLineages = $lineages
    $checkpoint.repairPullRequestsReserved = [int]$checkpoint.repairPullRequestsReserved + 1
    $checkpoint.lastRepairBranch = $branch
    $checkpoint.lastRepairAttemptId = "$($checkpoint.repairIncidentId):$($checkpoint.repairBuildLineageId):$attempt"
    $checkpoint.updatedAtUtc = [datetime]::UtcNow.ToString('o')
    Write-FactoryLoopCheckpoint -RepoRoot $root -Checkpoint $checkpoint

    return [pscustomobject]@{
        incidentId = [string]$checkpoint.repairIncidentId
        buildLineageId = [string]$checkpoint.repairBuildLineageId
        attempt = $attempt
        branch = $branch
        sourceSha = [string]$checkpoint.expectedSourceSha
        planReference = [string]$checkpoint.planReference
    }
}

function Register-FactoryLoopRepairPullRequest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$IncidentId,
        [Parameter(Mandatory)][string]$BuildLineageId,
        [Parameter(Mandatory)][string]$Branch,
        [Parameter(Mandatory)][string]$SourceSha
    )

    $root = [System.IO.Path]::GetFullPath($RepoRoot)
    $checkpoint = Read-FactoryLoopCheckpoint -RepoRoot $root
    if ([string]$checkpoint.stage -ceq 'create-pr') {
        if ([string]$checkpoint.repairIncidentId -cne $IncidentId -or
            [string]$checkpoint.repairBuildLineageId -cne $BuildLineageId -or
            [string]$checkpoint.branch -cne $Branch -or
            [string]$checkpoint.expectedSourceSha -cne $SourceSha) {
            throw 'Repair PR registration retry does not match the registered incident, lineage, branch, and source SHA.'
        }
        return [pscustomobject]@{
            incidentId = [string]$checkpoint.repairIncidentId
            buildLineageId = [string]$checkpoint.repairBuildLineageId
            pullRequestSequence = [int]$checkpoint.pullRequestSequence
            branch = [string]$checkpoint.branch
            sourceSha = [string]$checkpoint.expectedSourceSha
            outcome = 'repair-pr-registered'
        }
    }
    if ([string]$checkpoint.status -cne 'active' -or
        [string]$checkpoint.stage -cnotin @('build-failed', 'repair-needed') -or
        [string]$checkpoint.repairIncidentId -cne $IncidentId -or
        [string]$checkpoint.repairBuildLineageId -cne $BuildLineageId -or
        [int]$checkpoint.repairPullRequestsReserved -lt 1 -or
        $null -ne $checkpoint.pendingOperation) {
        throw 'Repair PR registration does not match a reserved active repair incident.'
    }
    if ($Branch -notmatch '^factory-repair/[A-Za-z0-9][A-Za-z0-9._-]{0,127}/[12]$' -or
        $Branch -cne [string]$checkpoint.lastRepairBranch -or
        $Branch.Contains('..') -or $SourceSha -cnotmatch '^(?:[0-9a-f]{40}|[0-9a-f]{64})$') {
        throw 'Repair PR registration has an invalid branch or source SHA.'
    }
    $branchHead = (git -C $root rev-parse --verify --quiet $Branch 2>$null).Trim()
    if ($LASTEXITCODE -ne 0 -or $branchHead -cne $SourceSha) {
        throw 'Repair PR branch does not point at its registered source SHA.'
    }
    $baseline = Get-FactoryLoopCriteriaBaseline -RepoRoot $root `
        -PlanReference ([string]$checkpoint.planReference)
    if ([string]$baseline.BaselineCommit -cne [string]$checkpoint.criteriaBaselineCommit -or
        [string]$baseline.Marker -cne [string]$checkpoint.criteriaMarker) {
        throw 'Repair PR registration is blocked because confirmed criteria changed.'
    }

    $checkpoint.branch = $Branch
    $checkpoint.expectedSourceSha = $SourceSha
    $checkpoint.pullRequestSequence = [int]$checkpoint.pullRequestSequence + 1
    $checkpoint.pullRequestId = $null
    $checkpoint.pullRequestProviderId = $null
    $checkpoint.mergeCommit = $null
    $checkpoint.artifactDigest = $null
    $checkpoint.artifactPath = $null
    $checkpoint.testDeploymentId = $null
    $checkpoint.testDeploymentProviderId = $null
    $checkpoint.productionDeploymentId = $null
    $checkpoint.productionDeploymentProviderId = $null
    $checkpoint.productionApprovalId = $null
    $checkpoint.productionApprovalProviderId = $null
    $checkpoint.telemetryStartedAtUtc = $null
    $checkpoint.telemetryCursor = $null
    $checkpoint.evidenceBranch = $null
    $checkpoint.evidenceCommit = $null
    $checkpoint.evidencePath = $null
    $checkpoint.evidenceProviderId = $null
    $checkpoint.evidenceRecordedAtUtc = $null
    $checkpoint.stage = 'create-pr'
    $checkpoint.outcome = 'repair-pr-registered'
    $checkpoint.reason = $null
    $checkpoint.updatedAtUtc = [datetime]::UtcNow.ToString('o')
    Write-FactoryLoopCheckpoint -RepoRoot $root -Checkpoint $checkpoint
    return [pscustomobject]@{
        incidentId = [string]$checkpoint.repairIncidentId
        buildLineageId = [string]$checkpoint.repairBuildLineageId
        pullRequestSequence = [int]$checkpoint.pullRequestSequence
        branch = [string]$checkpoint.branch
        sourceSha = [string]$checkpoint.expectedSourceSha
        outcome = [string]$checkpoint.outcome
    }
}

function Start-FactoryLoopRepairInvocation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$PlanReference
    )

    $root = [System.IO.Path]::GetFullPath($RepoRoot)
    $lockPath = Resolve-FactoryLoopConsumerPath -RepoRoot $root -RelativePath '.factory-loop/loop.lock'
    $lockStream = $null
    try {
        $lockStream = [System.IO.File]::Open(
            $lockPath,
            [System.IO.FileMode]::OpenOrCreate,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None
        )
    }
    catch [System.IO.IOException] {
        throw 'Factory-loop chain is currently locked by another process.'
    }

    try {
        $lockStream.SetLength(0)
        $lockBytes = [System.Text.Encoding]::UTF8.GetBytes(
            "pid=$PID`nstarted=$([datetime]::UtcNow.ToString('o'))"
        )
        $lockStream.Write($lockBytes, 0, $lockBytes.Length)
        $lockStream.Flush($true)
        return Start-FactoryLoopRepairInvocationCore -RepoRoot $root -PlanReference $PlanReference
    }
    finally {
        $lockStream.Dispose()
    }
}

Export-ModuleMember -Function @(
    'Initialize-FactoryLoopChain',
    'Invoke-FactoryLoopTick',
    'Start-FactoryLoopRepairInvocation',
    'Register-FactoryLoopRepairPullRequest'
)
