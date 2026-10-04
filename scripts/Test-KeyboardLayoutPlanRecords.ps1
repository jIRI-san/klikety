#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [ValidatePattern('^[0-9a-f]{6}$')]
    [string]$RetainedId = '36ef5d'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $RepoRoot).Path
$ciScripts = Join-Path $root '.github\skills\ci\scripts'
$indexScript = Join-Path $root '.github\skills\cip\scripts\Get-PlanIndex.ps1'
$stateScript = Join-Path $ciScripts 'Get-PlanState.ps1'
$expected = @(
    @{ Id = '000021'; Path = 'docs\implementation-plans\archived\021-keyboard-layout-refresh\plan.md'; Archived = $true },
    @{ Id = $RetainedId; Path = 'docs\implementation-plans\021-keyboard-layout-refresh\plan.md'; Archived = $false }
)
if ($RetainedId -eq '000021') {
    throw 'The retained copy must have a distinct canonical identity.'
}

function Assert-Check {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-Fields {
    param([object]$Value, [string[]]$Fields, [string]$Label)
    foreach ($field in $Fields) {
        Assert-Check ($null -ne $Value -and $Value.PSObject.Properties.Name -contains $field) "$Label missing field '$field'."
    }
}

$index = (& $indexScript -RepoRoot $root -Format Json -Filter 'Keyboard Layout Refresh') | ConvertFrom-Json
Assert-Check ($index.schema -ceq 'plan-index/v1') 'Unexpected index schema.'
Assert-Check (@($index.errors).Count -eq 0) "Plan index parse errors: $($index.errors -join '; ')"
foreach ($record in $expected) {
    $rows = @($index.plans | Where-Object id -CEQ $record.Id)
    Assert-Check ($rows.Count -eq 1) "Index must contain exactly one row for $($record.Id); got $($rows.Count)."
    Assert-Check (($rows[0].planFile -replace '/', '\') -ceq $record.Path) "Unexpected index path for $($record.Id)."
    Assert-Check ($rows[0].isArchived -eq $record.Archived) "Unexpected archive state for $($record.Id)."
    $state = (& $stateScript -Reference $record.Id -RepoRoot $root -Json) | ConvertFrom-Json
    Assert-Fields $state @('Kind', 'PlanId', 'PlanFile', 'FolderName', 'IsArchived', 'Markers', 'PlanningContext', 'Admission', 'Progress', 'NextStep') $record.Id
    Assert-Check ($state.Kind -ceq 'plan' -and $state.PlanId -ceq $record.Id) "Wrong supported state identity for $($record.Id)."
    Assert-Check ($state.PlanFile -ceq (Join-Path $root $record.Path)) "Wrong supported state path for $($record.Id)."
    Assert-Check ($state.IsArchived -eq $record.Archived) "Wrong supported archive state for $($record.Id)."
    Assert-Fields $state.Markers @('ExecutionMode', 'Scope', 'CipStage', 'PlanningConfirmed', 'DependsOn') "$($record.Id) markers"
    Assert-Check ($state.Markers.ExecutionMode -ceq 'manual' -and $state.Markers.Scope -ceq 'step' -and $state.Markers.CipStage -ceq 'drafted') "Historical markers changed for $($record.Id)."
    Assert-Check ($state.Markers.PlanningConfirmed -ceq 'sha256:9ce632d01bf74c5760aeea70dcb008538755e97656dcc3af95f65f8f6feffad5') "Historical confirmation changed for $($record.Id)."
    Assert-Check ($state.PlanningContext.IsConfirmed) "Restored criteria do not match confirmation for $($record.Id)."
    Assert-Fields $state.Progress @('Total', 'Completed', 'InProgress', 'Pending', 'Percent', 'CurrentPhase', 'LastCompleted', 'IsComplete') "$($record.Id) progress"
    Assert-Check ($state.Progress.Total -eq 12 -and $state.Progress.Completed -eq 12 -and $state.Progress.Pending -eq 0 -and $state.Progress.InProgress -eq 0 -and $state.Progress.Percent -eq 100 -and $state.Progress.IsComplete) "Historical completion evidence changed for $($record.Id)."
    Assert-Fields $state.NextStep @('Id', 'Status', 'IsHuman', 'IsDiscovery', 'Detail', 'BlockedByAfter', 'UnmetAfter', 'IsComplete') "$($record.Id) next step"
    Assert-Check ($state.NextStep.IsComplete -and $null -eq $state.NextStep.Id) "Unexpected next step for $($record.Id)."
}

$files = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($record in $expected) {
    $planPath = Join-Path $root $record.Path
    [void]$files.Add($planPath)
    $assets = Join-Path (Split-Path -Parent $planPath) 'assets'
    $assetFiles = @(Get-ChildItem -LiteralPath $assets -File -Recurse -Filter '*.md')
    Assert-Check ($assetFiles.Count -eq 8) "Expected eight preserved assets for $($record.Id)."
    foreach ($file in $assetFiles) { [void]$files.Add($file.FullName) }
}

# The affected incoming references are in the maintenance record; historical citations stay intact.
$maintenance = Join-Path $root 'docs\repository-maintenance.md'
[void]$files.Add($maintenance)
$linkCount = 0
foreach ($file in $files) {
    $text = Get-Content -LiteralPath $file -Raw
    $text = [regex]::Replace($text, '(?ms)^```[^\r\n]*\r?\n.*?^```\s*$', '')
    foreach ($link in [regex]::Matches($text, '\[[^\]]*\]\((?<target>[^)\s]+)(?:\s+"[^"]*")?\)')) {
        $target = [uri]::UnescapeDataString($link.Groups['target'].Value.Trim('<', '>'))
        if ($target -match '^[a-zA-Z][a-zA-Z0-9+.-]*:|^#') { continue }
        $path = ($target -split '#', 2)[0] -replace '/', '\'
        $resolved = [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $file) $path))
        Assert-Check ($resolved.StartsWith($root.TrimEnd('\') + '\', [System.StringComparison]::OrdinalIgnoreCase)) "Local link escapes repository: $file -> $target"
        Assert-Check (Test-Path -LiteralPath $resolved) "Broken local Markdown link: $file -> $target"
        $linkCount++
    }
}
foreach ($record in $expected) {
    $incoming = $record.Path.Substring('docs\'.Length).Replace('\', '/')
    Assert-Check ((Get-Content -LiteralPath $maintenance -Raw).Contains("]($incoming)")) "Missing affected incoming reference to $($record.Id)."
}

$activeAssets = Join-Path $root 'docs\implementation-plans\021-keyboard-layout-refresh\assets'
$archivedAssets = Join-Path $root 'docs\implementation-plans\archived\021-keyboard-layout-refresh\assets'
foreach ($file in Get-ChildItem -LiteralPath $activeAssets -File -Recurse) {
    $relative = [System.IO.Path]::GetRelativePath($activeAssets, $file.FullName)
    $archived = Join-Path $archivedAssets $relative
    Assert-Check ((Get-FileHash -LiteralPath $file.FullName).Hash -ceq (Get-FileHash -LiteralPath $archived).Hash) "Recovered asset differs from preserved archive: $relative"
}

[pscustomobject]@{
    Status = 'passed'
    Identities = @($expected.Id)
    IndexParseErrors = @($index.errors).Count
    FullStates = 2
    MarkdownFiles = $files.Count
    LocalLinks = $linkCount
    PreservedAssets = 8
}
