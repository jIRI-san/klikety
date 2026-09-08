#requires -Version 7.0
<#
.SYNOPSIS
Returns bounded current Markdown from explicitly selected plans as untrusted historical context.

.DESCRIPTION
Active direct-workflow adapter. It reads only retained Markdown through PlanState confinement, screens
secrets, and frames accepted content once as untrusted historical context.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateCount(1, 5)]
    [string[]]$PlanId,

    [Parameter(Mandatory)]
    [ValidateCount(1, 6)]
    [string[]]$ArtifactKind,

    [Parameter(Mandatory)]
    [ValidateCount(1, 5)]
    [string[]]$Relationship,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$RepoRoot,

    [ValidateSet('Object', 'Json')]
    [string]$Format = 'Object',

    [ValidateRange(1, 1MB)]
    [int]$MaxArtifactBytes = 128KB,

    [ValidateRange(1, 5MB)]
    [int]$MaxTotalBytes = 512KB,

    [ValidateRange(1, 5)]
    [int]$MaxCandidates = 5
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'PlanState.psm1') -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'SecretGuard.psm1') -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'DirectWorkflow.psm1') -DisableNameChecking

$utf8 = [System.Text.UTF8Encoding]::new($false, $true)
$root = (Resolve-Path -LiteralPath ([System.IO.Path]::GetFullPath($RepoRoot))).Path
$corpus = New-PlanCorpusConfinementContext -RepoRoot $root
$supportedKinds = @('Intent', 'Design', 'Decisions', 'Reviews', 'Learnings')
$supportedRelationships = @(
    'reuses', 'extends', 'supersedes', 'conflicts', 'dependency', 'sibling', 'operator-selected'
)

function New-PublicResult {
    param(
        [Parameter(Mandatory)][string]$Status,
        [AllowNull()][string]$Id,
        [AllowNull()][string]$Kind,
        [AllowNull()][string]$Path,
        [AllowNull()][string]$Relation,
        [AllowNull()][string]$Layout,
        [AllowNull()][object]$Archived,
        [AllowNull()][object]$ByteCount,
        [AllowNull()][string]$Content,
        [AllowNull()][string]$Reason
    )

    [pscustomobject][ordered]@{
        status = $Status
        planId = $Id
        artifactKind = $Kind
        path = $Path
        relationship = $Relation
        layout = $Layout
        isArchived = $Archived
        isUntrusted = $true
        authority = 'historical-context-only'
        byteCount = $ByteCount
        content = $Content
        reason = $Reason
    }
}

function ConvertTo-RelativePath {
    param([Parameter(Mandatory)][string]$Path)
    ([System.IO.Path]::GetRelativePath($root, $Path)).Replace('\', '/')
}

function Read-ConfinedMarkdown {
    param(
        [Parameter(Mandatory)][object]$Context,
        [Parameter(Mandatory)][string]$Path
    )

    $stream = Open-ConfinedPlanFile -Context $Context -Path $Path
    try {
        if ($stream.Length -eq 0) {
            return [pscustomobject]@{ Status = 'refused'; Bytes = 0; Content = $null; Reason = 'Artifact is empty.' }
        }
        if ($stream.Length -gt $MaxArtifactBytes) {
            return [pscustomobject]@{
                Status = 'oversized'
                Bytes = [int64]$stream.Length
                Content = $null
                Reason = "Artifact exceeds the $MaxArtifactBytes-byte limit."
            }
        }
        $bytes = [byte[]]::new([int]$stream.Length)
        $offset = 0
        while ($offset -lt $bytes.Length) {
            $read = $stream.Read($bytes, $offset, $bytes.Length - $offset)
            if ($read -eq 0) { throw "Artifact '$Path' changed while it was being read." }
            $offset += $read
        }
        $content = $utf8.GetString($bytes)
        if ([string]::IsNullOrWhiteSpace($content)) {
            return [pscustomobject]@{
                Status = 'refused'
                Bytes = [int64]$bytes.Length
                Content = $null
                Reason = 'Artifact is whitespace-only.'
            }
        }
        $secretTypes = @(Find-HighConfidenceSecret -Value $content)
        if ($secretTypes.Count -gt 0) {
            return [pscustomobject]@{
                Status = 'refused'
                Bytes = [int64]$bytes.Length
                Content = $null
                Reason = "Artifact contains high-confidence credential type(s): $($secretTypes -join ', ')."
            }
        }
        return [pscustomobject]@{
            Status = 'accepted'
            Bytes = [int64]$bytes.Length
            Content = $content
            Reason = $null
        }
    }
    finally {
        $stream.Dispose()
    }
}

$ids = @($PlanId | Sort-Object -Unique)
$kinds = @($ArtifactKind | Sort-Object -Unique)
if ([int64]$ids.Count * [int64]$kinds.Count -gt $MaxCandidates) {
    throw "Selection exceeds the $MaxCandidates-supporting-artifact limit."
}
if ($Relationship.Count -ne 1 -and $Relationship.Count -ne $PlanId.Count) {
    throw 'Relationship must contain one value for all plans or one value aligned with each PlanId.'
}

$relationshipById = @{}
for ($index = 0; $index -lt $PlanId.Count; $index++) {
    $id = [string]$PlanId[$index]
    $relationship = [string]$(if ($Relationship.Count -eq 1) {
            $Relationship[0]
        }
        else {
            $Relationship[$index]
        })
    if ($relationship -cnotin $supportedRelationships) {
        throw "Relationship '$relationship' is not supported."
    }
    if ($relationshipById.ContainsKey($id) -and
        [string]$relationshipById[$id] -cne $relationship) {
        throw "Plan ID '$id' has conflicting relationship values."
    }
    $relationshipById[$id] = $relationship
}

$inventory = @(Get-PlanInventory -RepoRoot $root -CanonicalIdFilter $ids)
$candidates = [System.Collections.Generic.List[object]]::new()
foreach ($id in $ids) {
    if ($id -cnotmatch '^(?:[0-9a-f]{6}|\d{3})$') {
        foreach ($kind in $kinds) {
            $candidates.Add((New-PublicResult -Status refused -Id $id -Kind $kind -Path $null `
                        -Relation ([string]$relationshipById[$id]) -Layout $null -Archived $null `
                        -ByteCount $null -Content $null -Reason 'Plan ID is not canonical.'))
        }
        continue
    }
    $matches = @($inventory | Where-Object Id -CEQ $id)
    if ($matches.Count -ne 1) {
        foreach ($kind in $kinds) {
            $candidates.Add((New-PublicResult -Status refused -Id $id -Kind $kind -Path $null `
                        -Relation ([string]$relationshipById[$id]) -Layout $null -Archived $null `
                        -ByteCount $null -Content $null -Reason 'Plan ID is not unique in the plan inventory.'))
        }
        continue
    }

    $plan = $matches[0]
    $context = New-PlanConfinementContext -PlanDir $plan.Path -CorpusContext $corpus
    $layout = Get-PlanLayout -PlanDir $plan.Path
    foreach ($kind in $kinds) {
        if ($kind -cnotin $supportedKinds) {
            $candidates.Add((New-PublicResult -Status refused -Id $id -Kind $kind -Path $null `
                        -Relation ([string]$relationshipById[$id]) -Layout $layout -Archived ([bool]$plan.IsArchived) `
                        -ByteCount $null -Content $null -Reason "Artifact kind '$kind' is not supported."))
            continue
        }

        $paths = @()
        if ($kind -eq 'Reviews') {
            $reviews = Resolve-PlanAssetPath -PlanDir $plan.Path -Kind Reviews -Layout $layout
            if (Test-Path -LiteralPath $reviews -PathType Container) {
                [void](Resolve-ConfinedPlanPath -Context $context -Path $reviews -PathType Container)
                $paths = @(
                    Get-ChildItem -LiteralPath $reviews -File -Force |
                        Where-Object Name -CMatch '^(?:phase-[1-9][0-9]*|final)\.md$' |
                        Sort-Object Name |
                        ForEach-Object FullName
                )
            }
        }
        else {
            $paths = @(Resolve-PlanAssetPath -PlanDir $plan.Path -Kind $kind -Layout $layout)
        }

        if ($paths.Count -eq 0) {
            $candidates.Add((New-PublicResult -Status missing -Id $id -Kind $kind `
                        -Path (ConvertTo-RelativePath (Resolve-PlanAssetPath -PlanDir $plan.Path `
                                -Kind $kind -Layout $layout)) `
                        -Relation ([string]$relationshipById[$id]) -Layout $layout `
                        -Archived ([bool]$plan.IsArchived) -ByteCount $null -Content $null `
                        -Reason 'Current Markdown artifact does not exist.'))
            continue
        }
        foreach ($path in $paths) {
            if ($candidates.Count -ge $MaxCandidates) {
                throw "Selection expands beyond the $MaxCandidates-supporting-artifact limit."
            }
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                $candidates.Add((New-PublicResult -Status missing -Id $id -Kind $kind `
                            -Path (ConvertTo-RelativePath $path) -Relation ([string]$relationshipById[$id]) `
                            -Layout $layout -Archived ([bool]$plan.IsArchived) -ByteCount $null `
                            -Content $null -Reason 'Current Markdown artifact does not exist.'))
                continue
            }
            $read = Read-ConfinedMarkdown -Context $context -Path $path
            $candidates.Add((New-PublicResult -Status $read.Status -Id $id -Kind $kind `
                        -Path (ConvertTo-RelativePath $path) -Relation ([string]$relationshipById[$id]) `
                        -Layout $layout -Archived ([bool]$plan.IsArchived) -ByteCount $read.Bytes `
                        -Content $read.Content -Reason $read.Reason))
        }
    }
}

$accepted = @($candidates | Where-Object status -CEQ 'accepted')
$totalBytes = [int64]0
foreach ($candidate in $accepted) {
    $totalBytes += [int64]$candidate.byteCount
}
if ($totalBytes -gt $MaxTotalBytes) {
    foreach ($candidate in $accepted) {
        $candidate.status = 'oversized'
        $candidate.content = $null
        $candidate.reason = "Accepted selection exceeds the $MaxTotalBytes-byte aggregate limit."
    }
    $accepted = @()
}

$acceptedPayload = @($accepted | ForEach-Object {
        [ordered]@{
            planId = $_.planId
            artifactKind = $_.artifactKind
            path = $_.path
            relationship = $_.relationship
            layout = $_.layout
            isArchived = $_.isArchived
            isUntrusted = $_.isUntrusted
            authority = $_.authority
            byteCount = $_.byteCount
            content = $_.content
        }
    })
$result = [pscustomobject][ordered]@{
    accepted = @($accepted | ForEach-Object {
            [ordered]@{
                planId = $_.planId
                artifactKind = $_.artifactKind
                path = $_.path
                relationship = $_.relationship
                layout = $_.layout
                isArchived = $_.isArchived
                isUntrusted = $_.isUntrusted
                authority = $_.authority
                byteCount = $_.byteCount
            }
        })
    diagnostics = @($candidates | Where-Object status -CNE 'accepted')
    provenance = @($accepted | ForEach-Object {
            [ordered]@{
                planId = $_.planId
                artifactKind = $_.artifactKind
                path = $_.path
                relationship = $_.relationship
            }
        })
    untrustedInput = if ($acceptedPayload.Count -gt 0) {
        ConvertTo-UntrustedReviewBlock `
            -Content (ConvertTo-Json -InputObject $acceptedPayload -Depth 10 -Compress) `
            -Label 'historical context'
    }
    else {
        $null
    }
}

if ($Format -eq 'Json') {
    $result | ConvertTo-Json -Depth 12
}
else {
    $result
}
