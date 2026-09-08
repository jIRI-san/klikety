#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$RepoRoot,
    [string]$BaseRef,
    [string[]]$ChangedPath,
    [string[]]$CandidatePath = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)
if (-not (Test-Path -LiteralPath $RepoRoot -PathType Container)) {
    throw "RepoRoot '$RepoRoot' is not a directory."
}

function ConvertTo-RepoPath {
    param([Parameter(Mandatory)][string]$Path)

    return $Path.Trim().Replace('\', '/').TrimStart('./')
}

$indexRelative = 'docs/design-notes/.design-notes.md'
$indexPath = Join-Path $RepoRoot $indexRelative
if (-not (Test-Path -LiteralPath $indexPath -PathType Leaf)) {
    throw "Design-note index not found: $indexPath"
}

if (-not $PSBoundParameters.ContainsKey('ChangedPath')) {
    if ([string]::IsNullOrWhiteSpace($BaseRef)) {
        throw 'BaseRef is required when ChangedPath is not supplied.'
    }
    $trackedChanges = @(& git -C $RepoRoot diff --name-only $BaseRef --)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to inventory implementation changes from '$BaseRef'."
    }
    $untrackedChanges = @(& git -C $RepoRoot ls-files --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to inventory untracked implementation changes.'
    }
    $ChangedPath = @($trackedChanges) + @($untrackedChanges)
}

$changed = @(
    $ChangedPath |
        ForEach-Object { ConvertTo-RepoPath -Path $_ } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Sort-Object -Unique
)
$touchedNotes = @($changed | Where-Object {
        $_ -like 'docs/design-notes/*.design.md'
    })
$shouldRun = @($changed | Where-Object { $_ -like 'docs/design-notes/*' }).Count -gt 0

$activeNotes = [System.Collections.Generic.List[object]]::new()
foreach ($line in Get-Content -LiteralPath $indexPath) {
    $match = [regex]::Match(
        $line,
        '^\|\s*\[[^\]]+\]\((?<path>[^)]+)\)\s*\|\s*(?<scope>[^|]+?)\s*\|\s*(?<patterns>[^|]+?)\s*\|$'
    )
    if (-not $match.Success) { continue }

    $relative = ConvertTo-RepoPath -Path ("docs/design-notes/" + $match.Groups['path'].Value)
    $activeNotes.Add([pscustomobject]@{
            Path = $relative
            Scope = $match.Groups['scope'].Value.Trim()
            KeyPatterns = $match.Groups['patterns'].Value.Trim()
        })
}

$activePaths = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase
)
foreach ($note in $activeNotes) {
    [void]$activePaths.Add($note.Path)
}

$candidates = [System.Collections.Generic.List[string]]::new()
$seen = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase
)
foreach ($path in @($touchedNotes) + @($CandidatePath | ForEach-Object {
            ConvertTo-RepoPath -Path $_
        })) {
    if ($path -notlike 'docs/design-notes/*.design.md') { continue }
    if (-not $activePaths.Contains($path) -and $touchedNotes -notcontains $path) {
        throw "Candidate is not an active or touched design note: '$path'."
    }
    if ($seen.Add($path)) {
        $candidates.Add($path)
    }
}

$batches = [System.Collections.Generic.List[object]]::new()
for ($offset = 0; $offset -lt $candidates.Count; $offset += 5) {
    $last = [Math]::Min($offset + 4, $candidates.Count - 1)
    $batches.Add([pscustomobject]@{
            Number = $batches.Count + 1
            Notes = @($candidates[$offset..$last])
        })
}

[pscustomobject]@{
    ShouldRun = $shouldRun
    ChangedPaths = $changed
    TouchedNotes = $touchedNotes
    ActiveNotes = @($activeNotes)
    CandidateBatches = @($batches)
}
