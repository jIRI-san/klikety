#requires -Version 7.0
<#
.SYNOPSIS
    Scaffolds an epic folder plus `epic.md` and stamps child plans into it.
.DESCRIPTION
    An epic is an index over ordinary sibling plan folders, never a container for them: child plans stay
    directly under `docs/implementation-plans/` so `Resolve-Plan`, `Get-PlanState`, and the validator keep
    resolving them unchanged, and each child stays independently executable.

    Membership lives in the child: `<!-- epic: <id> -->` in its `plan.md`. The child-plan table inside
    `epic.md` is a generated mirror of that marker set, rewritten on every run, so the two can never
    disagree. Ordering between children is expressed with the existing `<!-- depends-on: ... -->` marker.
.EXAMPLE
    New-Epic.ps1 -Title 'Payments rework' -Slug payments-rework
.EXAMPLE
    New-Epic.ps1 -Epic 9f2a1c -ChildPlan checkout-api -DependsOn payments-core
#>
[CmdletBinding(DefaultParameterSetName = 'Scaffold')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Scaffold')]
    [string]$Title,

    [Parameter(Mandatory, ParameterSetName = 'Scaffold')]
    [string]$Slug,

    [Parameter(Mandatory, ParameterSetName = 'Attach')]
    [string]$Epic,

    [Parameter(ParameterSetName = 'Attach')]
    [string[]]$ChildPlan = @(),

    [Parameter(ParameterSetName = 'Attach')]
    [string[]]$DependsOn = @(),

    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path,

    [Parameter(ParameterSetName = 'Scaffold')]
    [string]$Date = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd'),

    [Parameter(ParameterSetName = 'Scaffold')]
    [string]$EpicId,

    [Parameter(ParameterSetName = 'Scaffold')]
    [Parameter(ParameterSetName = 'Attach')]
    [switch]$Force
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'PlanState.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'PlanState.psm1') -Force -DisableNameChecking

function Write-PlanText {
    <#
    .SYNOPSIS
    Writes markdown back with exactly one trailing newline.

    .DESCRIPTION
    `Set-Content` appends a trailing newline of its own, so round-tripping raw content through
    split/join would grow the file by one blank line per rewrite and make repeat runs non-idempotent.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [AllowEmptyString()]
        [string[]]$Lines
    )

    Set-Content -LiteralPath $Path -Value (($Lines -join "`n").TrimEnd("`n")) -Encoding utf8NoBOM
}

function Test-DirectoryEntryExists {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $comparison = if ($IsWindows) {
        [System.StringComparison]::OrdinalIgnoreCase
    }
    else {
        [System.StringComparison]::Ordinal
    }
    $parent = Split-Path -Parent $Path
    $name = Split-Path -Leaf $Path
    return @(
        Get-ChildItem -LiteralPath $parent -Force -ErrorAction Stop |
            Where-Object { [string]::Equals($_.Name, $name, $comparison) }
    ).Count -gt 0
}

function Get-SanitizedSlug {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Value
    )

    $clean = $Value.ToLowerInvariant()
    $clean = $clean -replace '[^a-z0-9]+', '-'
    $clean = $clean.Trim('-')
    if ([string]::IsNullOrWhiteSpace($clean)) {
        throw "Slug '$Value' is empty after sanitization; supply alphanumeric characters."
    }

    return $clean
}

function Resolve-ConfinedFolder {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Root,

        [Parameter(Mandatory)]
        [string]$FolderName
    )

    $resolvedRoot = [System.IO.Path]::GetFullPath($Root)
    $candidate = [System.IO.Path]::GetFullPath((Join-Path $resolvedRoot $FolderName))
    $separator = [System.IO.Path]::DirectorySeparatorChar
    $rootWithSeparator = $resolvedRoot.TrimEnd($separator) + $separator
    if (-not $candidate.StartsWith($rootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Resolved epic folder '$candidate' escapes epics root '$resolvedRoot'."
    }

    return $candidate
}

function Get-EpicScaffold {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Id,

        [Parameter(Mandatory)]
        [string]$EpicTitle
    )

    return @"
# ${Id}: $EpicTitle
<!-- epic-id: $Id -->
<!-- Folder naming: epics/<yyyy-mm-dd>-<6hex>-<slug> · epic-id is the canonical handle. New-Epic.ps1 fills these in. -->

## Goal

TBD

## Child plans

$script:ChildBlockStart
| Plan | Slug | Depends on |
|---|---|---|
| _(none yet)_ | | |
$script:ChildBlockEnd

Membership is the ``<!-- epic: $Id -->`` marker in each child ``plan.md``; the table above is a generated
mirror that ``New-Epic.ps1`` rewrites. Run ``Get-PlanState $Id`` for live rollup and the next unblocked
child plan.

## Decomposition notes

TBD
"@
}

function Set-PlanHeaderMarker {
    <#
    .SYNOPSIS
    Inserts or replaces a single `<!-- key: value -->` header marker in a plan file.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [AllowEmptyString()]
        [string[]]$Lines,

        [Parameter(Mandatory)]
        [string]$Key,

        [Parameter(Mandatory)]
        [string]$Value
    )

    $list = [System.Collections.Generic.List[string]]::new()
    $list.AddRange([string[]]$Lines)
    $pattern = "^\s*<!--\s*$([regex]::Escape($Key))\s*:\s*.*?-->\s*$"

    for ($i = 0; $i -lt $list.Count; $i++) {
        if ($list[$i] -match '^##\s') { break }
        if ($list[$i] -match $pattern) {
            $list[$i] = "<!-- ${Key}: $Value -->"
            return , $list.ToArray()
        }
    }

    # Anchor the new marker directly under `plan-id` so the header block stays in a stable order.
    $insertAt = -1
    for ($i = 0; $i -lt $list.Count; $i++) {
        if ($list[$i] -match '^##\s') { break }
        if ($list[$i] -match '^\s*<!--\s*plan-id\s*:') { $insertAt = $i + 1 }
    }
    if ($insertAt -lt 0) {
        for ($i = 0; $i -lt $list.Count; $i++) {
            if ($list[$i] -match '^#\s+') { $insertAt = $i + 1; break }
        }
    }
    if ($insertAt -lt 0) { $insertAt = 0 }

    $list.Insert($insertAt, "<!-- ${Key}: $Value -->")
    return , $list.ToArray()
}

function Get-EpicMember {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$EpicId,

        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [object[]]$Inventory
    )

    return @($Inventory |
            Where-Object { $_.EpicId -and $_.EpicId.ToLowerInvariant() -eq $EpicId.ToLowerInvariant() } |
            Sort-Object @{ Expression = { $_.Date } }, @{ Expression = { $_.Id } })
}

function Get-PrefixedChildTarget {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [object]$Child,

        [Parameter(Mandatory)]
        [string]$TargetEpicId
    )

    # Legacy and rollout-era unprefixed hash plans keep their existing paths. Only a folder that
    # already participates in the prefix grammar must track later attachment or re-parenting.
    if ($Child.Scheme -ne 'new' -or [string]::IsNullOrWhiteSpace([string]$Child.FolderPrefix)) {
        return $null
    }

    $parent = Split-Path -Parent $Child.Path
    $folderName = "$TargetEpicId-$($Child.Date)-$($Child.Id)-$($Child.Slug)"
    return [pscustomobject]@{
        FolderName = $folderName
        Path       = Resolve-ConfinedFolder -Root $parent -FolderName $folderName
    }
}

function Get-EpicChildBlock {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$EpicFile
    )

    $epicRaw = Get-Content -LiteralPath $EpicFile -Raw
    $epicLines = ($epicRaw -replace "`r`n", "`n").Split("`n")
    $startIndexes = @()
    $endIndexes = @()
    for ($i = 0; $i -lt $epicLines.Count; $i++) {
        $trimmedLine = $epicLines[$i].Trim()
        if ($trimmedLine -eq $script:ChildBlockStart) { $startIndexes += $i }
        if ($trimmedLine -eq $script:ChildBlockEnd) { $endIndexes += $i }
    }
    if ($startIndexes.Count -ne 1 -or $endIndexes.Count -ne 1 -or $endIndexes[0] -le $startIndexes[0]) {
        throw "Epic file '$EpicFile' must contain exactly one ordered '$script:ChildBlockStart' / '$script:ChildBlockEnd' block."
    }

    return [pscustomobject]@{
        Lines      = $epicLines
        StartIndex = $startIndexes[0]
        EndIndex   = $endIndexes[0]
    }
}

function Update-EpicChildTable {
    <#
    .SYNOPSIS
    Rewrites the generated child-plan block of one `epic.md` from the supplied member set.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$EpicFile,

        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [object[]]$Members
    )

    $rows = [System.Collections.Generic.List[string]]::new()
    $rows.Add('| Plan | Slug | Depends on |')
    $rows.Add('|---|---|---|')
    if ($Members.Count -eq 0) {
        $rows.Add('| _(none yet)_ | | |')
    }
    else {
        foreach ($member in $Members) {
            $memberMarkers = Get-PlanHeaderMarkers -Path (Join-Path $member.Path 'plan.md')
            $deps = @($memberMarkers.DependsOn)
            $depText = if ($deps.Count -gt 0) { ($deps | ForEach-Object { "``$_``" }) -join ', ' } else { '—' }
            $archivedNote = if ($member.IsArchived) { ' _(archived)_' } else { '' }
            $rows.Add("| ``$($member.Id)`` | $($member.Slug)$archivedNote | $depText |")
        }
    }

    $block = Get-EpicChildBlock -EpicFile $EpicFile
    $epicLines = $block.Lines
    $startIndex = $block.StartIndex
    $endIndex = $block.EndIndex

    $rebuilt = [System.Collections.Generic.List[string]]::new()
    $rebuilt.AddRange([string[]]($epicLines[0..$startIndex]))
    $rebuilt.AddRange([string[]]$rows.ToArray())
    $rebuilt.AddRange([string[]]($epicLines[$endIndex..($epicLines.Count - 1)]))
    Write-PlanText -Path $EpicFile -Lines $rebuilt.ToArray()
}

$script:ChildBlockStart = '<!-- child-plans:start -->'
$script:ChildBlockEnd = '<!-- child-plans:end -->'

$repoRootPath = [System.IO.Path]::GetFullPath($RepoRoot)
$plansRoot = Join-Path $repoRootPath 'docs/implementation-plans'
$epicsRoot = Join-Path $plansRoot 'epics'

if ($DependsOn.Count -gt 0 -and $ChildPlan.Count -ne 1) {
    throw '-DependsOn applies to exactly one -ChildPlan; run New-Epic once per child so each dependency edge is explicit.'
}

$planInventory = @(Get-PlanInventory -RepoRoot $repoRootPath)
$epicInventory = @(Get-EpicInventory -RepoRoot $repoRootPath)

if ($PSCmdlet.ParameterSetName -eq 'Scaffold') {
    if ($Date -notmatch '^\d{4}-\d{2}-\d{2}$') {
        throw "Date '$Date' must be in yyyy-MM-dd format."
    }

    $slugClean = Get-SanitizedSlug -Value $Slug

    if ($EpicId) {
        $EpicId = $EpicId.Trim().ToLowerInvariant()
        if ($EpicId -notmatch '^[0-9a-f]{6}$') {
            throw "EpicId '$EpicId' must be exactly 6 hex chars."
        }
    }
    else {
        # Epic ids share the plan id space: `/ci` accepts either handle, so a collision would make the
        # reference ambiguous rather than merely ugly.
        $taken = @($planInventory | ForEach-Object { $_.Id }) + @($epicInventory | ForEach-Object { $_.Id })
        $EpicId = New-PlanId -ExistingId $taken
    }

    foreach ($existing in ($planInventory + $epicInventory)) {
        if ($existing.Id -and $existing.Id.ToLowerInvariant() -eq $EpicId) {
            throw "Epic id '$EpicId' is already taken by '$($existing.FolderName)'."
        }
    }

    if (-not (Test-Path -LiteralPath $epicsRoot -PathType Container)) {
        New-Item -ItemType Directory -Path $epicsRoot -Force | Out-Null
    }

    $folderName = "$Date-$EpicId-$slugClean"
    $epicDir = Resolve-ConfinedFolder -Root $epicsRoot -FolderName $folderName
    $epicFile = Join-Path $epicDir 'epic.md'

    if ((Test-Path -LiteralPath $epicFile -PathType Leaf) -and -not $Force) {
        throw "Epic already exists: $epicFile (use -Force to rewrite epic.md)."
    }

    New-Item -ItemType Directory -Path $epicDir -Force | Out-Null
    Set-Content -LiteralPath $epicFile -Value (Get-EpicScaffold -Id $EpicId -EpicTitle $Title) -Encoding utf8NoBOM
    $epicInventory = @(Get-EpicInventory -RepoRoot $repoRootPath)
}
else {
    $resolvedEpic = Resolve-Epic -Reference $Epic -RepoRoot $repoRootPath -Inventory $epicInventory
    if ($resolvedEpic.IsArchived) {
        throw "Epic '$($resolvedEpic.Id)' is archived and cannot be modified."
    }
    $EpicId = $resolvedEpic.Id
    $epicDir = $resolvedEpic.Path
    $epicFile = $resolvedEpic.EpicFile
    $folderName = $resolvedEpic.FolderName
    if (-not (Test-Path -LiteralPath $epicFile -PathType Leaf)) {
        throw "Epic '$EpicId' has no epic.md at $epicFile."
    }
}

$pathComparer = if ($IsWindows) {
    [System.StringComparer]::OrdinalIgnoreCase
}
else {
    [System.StringComparer]::Ordinal
}
$validatedEpicFiles = [System.Collections.Generic.HashSet[string]]::new($pathComparer)
$resolvedEpicFile = [System.IO.Path]::GetFullPath($epicFile)
if ($validatedEpicFiles.Add($resolvedEpicFile)) {
    Get-EpicChildBlock -EpicFile $resolvedEpicFile | Out-Null
}

$attachments = [System.Collections.Generic.List[object]]::new()
$targetPaths = [System.Collections.Generic.HashSet[string]]::new($pathComparer)
foreach ($reference in $ChildPlan) {
    $child = Resolve-Plan -Reference $reference -RepoRoot $repoRootPath -Inventory $planInventory
    $childPlanFile = Join-Path $child.Path 'plan.md'
    if (-not (Test-Path -LiteralPath $childPlanFile -PathType Leaf)) {
        throw "Child plan '$($child.Id)' has no plan.md at $childPlanFile."
    }
    if ($child.IsArchived) {
        Write-Warning "Child plan '$($child.Id)' is archived; stamping it into epic '$EpicId' anyway."
    }

    $raw = Get-Content -LiteralPath $childPlanFile -Raw
    $lines = ($raw -replace "`r`n", "`n").Split("`n")
    $markers = Get-PlanHeaderMarkers -Content ($raw)

    if ($markers.EpicId -and $markers.EpicId.ToLowerInvariant() -ne $EpicId) {
        if (-not $Force) {
            throw "Child plan '$($child.Id)' already belongs to epic '$($markers.EpicId)'. Re-parenting is a deliberate call — re-run with -Force."
        }

        $previousEpicId = $markers.EpicId.ToLowerInvariant()
        $previousEpic = @($epicInventory | Where-Object {
                $_.Id -and $_.Id.ToLowerInvariant() -eq $previousEpicId
            })
        if ($previousEpic.Count -ne 1 -or -not (Test-Path -LiteralPath $previousEpic[0].EpicFile -PathType Leaf)) {
            throw "Cannot re-parent child plan '$($child.Id)': previous epic '$previousEpicId' has no unique epic.md."
        }
        if ($previousEpic[0].IsArchived) {
            throw "Cannot re-parent child plan '$($child.Id)' from archived epic '$previousEpicId'."
        }
        $previousEpicFile = [System.IO.Path]::GetFullPath([string]$previousEpic[0].EpicFile)
        if ($validatedEpicFiles.Add($previousEpicFile)) {
            Get-EpicChildBlock -EpicFile $previousEpicFile | Out-Null
        }
    }

    $mergedDeps = [System.Collections.Generic.List[string]]::new()
    foreach ($existingDep in @($markers.DependsOn)) {
        $token = $existingDep.Trim().ToLowerInvariant()
        if ($token -and -not $mergedDeps.Contains($token)) { $mergedDeps.Add($token) }
    }
    foreach ($dep in $DependsOn) {
        $resolvedDep = Resolve-Plan -Reference $dep -RepoRoot $repoRootPath -Inventory $planInventory
        if ($resolvedDep.Id.ToLowerInvariant() -eq $child.Id.ToLowerInvariant()) {
            throw "Child plan '$($child.Id)' cannot depend on itself."
        }
        $token = $resolvedDep.Id.ToLowerInvariant()
        if (-not $mergedDeps.Contains($token)) { $mergedDeps.Add($token) }
    }

    $target = Get-PrefixedChildTarget -Child $child -TargetEpicId $EpicId
    if ($target -and -not $pathComparer.Equals($target.Path, $child.Path)) {
        if ((Test-DirectoryEntryExists -Path $target.Path) -or -not $targetPaths.Add($target.Path)) {
            throw "Cannot attach child plan '$($child.Id)': target folder '$($target.Path)' already exists or is selected by another child."
        }
    }

    $attachments.Add([pscustomobject]@{
            Child     = $child
            PlanFile  = $childPlanFile
            Lines     = $lines
            Markers   = $markers
            DependsOn = $mergedDeps
            Target    = $target
        })
}

$stamped = [System.Collections.Generic.List[object]]::new()
$reparentedFrom = [System.Collections.Generic.List[string]]::new()
foreach ($attachment in $attachments) {
    $child = $attachment.Child
    $requiresMove = $attachment.Target -and
    -not $pathComparer.Equals($attachment.Target.Path, $child.Path)
    if ($requiresMove -and (Test-DirectoryEntryExists -Path $attachment.Target.Path)) {
        throw "Cannot attach child plan '$($child.Id)': target folder '$($attachment.Target.Path)' already exists."
    }

    $lines = Set-PlanHeaderMarker -Lines $attachment.Lines -Key 'epic' -Value $EpicId
    if ($attachment.Markers.EpicId -and $attachment.Markers.EpicId.ToLowerInvariant() -ne $EpicId) {
        $reparentedFrom.Add($attachment.Markers.EpicId.ToLowerInvariant())
    }

    $mergedDeps = $attachment.DependsOn
    if ($mergedDeps.Count -gt 0) {
        $lines = Set-PlanHeaderMarker -Lines $lines -Key 'depends-on' -Value ($mergedDeps -join ', ')
    }

    Write-PlanText -Path $attachment.PlanFile -Lines $lines
    $childPath = $child.Path
    $childPlanFile = $attachment.PlanFile
    if ($requiresMove) {
        try {
            [System.IO.Directory]::Move($child.Path, $attachment.Target.Path)
        }
        catch {
            $moveError = $_
            try {
                Write-PlanText -Path $attachment.PlanFile -Lines $attachment.Lines
            }
            catch {
                throw "Moving child plan '$($child.Id)' failed: $($moveError.Exception.Message). Restoring its original plan header also failed: $($_.Exception.Message)"
            }
            throw $moveError
        }
        $childPath = $attachment.Target.Path
        $childPlanFile = Join-Path $childPath 'plan.md'
    }

    $stamped.Add([pscustomobject]@{
            Id        = $child.Id
            Slug      = $child.Slug
            Path      = $childPath
            PlanFile  = $childPlanFile
            DependsOn = $mergedDeps.ToArray()
        })
}

# Rebuild the mirror from the markers on disk (including children stamped by earlier runs) so the table
# is derived state, never a second source of truth that can drift from the plans themselves. A -Force
# re-parent rebuilds the losing epic too, otherwise its table would keep listing a child it no longer owns.
$currentInventory = @(Get-PlanInventory -RepoRoot $repoRootPath)
$members = @(Get-EpicMember -EpicId $EpicId -Inventory $currentInventory)
Update-EpicChildTable -EpicFile $epicFile -Members $members

$currentEpicInventory = @(Get-EpicInventory -RepoRoot $repoRootPath)
foreach ($orphanedEpicId in ($reparentedFrom | Sort-Object -Unique)) {
    $orphanedEpic = @($currentEpicInventory | Where-Object { $_.Id -and $_.Id.ToLowerInvariant() -eq $orphanedEpicId })
    if ($orphanedEpic.Count -ne 1 -or -not (Test-Path -LiteralPath $orphanedEpic[0].EpicFile -PathType Leaf)) {
        Write-Warning "Previous epic '$orphanedEpicId' has no resolvable epic.md; its child table was not refreshed."
        continue
    }
    Update-EpicChildTable -EpicFile $orphanedEpic[0].EpicFile -Members @(Get-EpicMember -EpicId $orphanedEpicId -Inventory $currentInventory)
}

$result = [pscustomobject]@{
    EpicId     = $EpicId
    FolderName = $folderName
    Path       = $epicDir
    EpicFile   = $epicFile
    Stamped    = $stamped.ToArray()
    Children   = @($members | ForEach-Object { $_.Id })
}

Write-Host "Epic '$folderName' (epic-id $EpicId) has $($members.Count) child plan(s) at $epicFile" -ForegroundColor Green
return $result
