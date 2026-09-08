#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$RepoRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = [System.IO.Path]::GetFullPath($RepoRoot)
if (-not (Test-Path -LiteralPath $root -PathType Container)) {
    throw "Repository root not found: $root"
}

function Assert-DesignNotesDestination {
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $relative = [System.IO.Path]::GetRelativePath($root, $fullPath)
    if ([System.IO.Path]::IsPathRooted($relative) -or
        $relative -eq '..' -or
        $relative.StartsWith("../", [System.StringComparison]::Ordinal) -or
        $relative.StartsWith("..\", [System.StringComparison]::Ordinal)) {
        throw "Design-notes destination '$fullPath' escapes repository root '$root'."
    }

    $current = $root
    foreach ($segment in ($relative -split '[\\/]')) {
        if ([string]::IsNullOrWhiteSpace($segment) -or $segment -eq '.') { continue }
        $current = Join-Path $current $segment
        if ((Test-Path -LiteralPath $current) -and (Get-Item -LiteralPath $current -Force).LinkType) {
            throw "Design-notes destination contains a linked segment: $current"
        }
    }
    return $fullPath
}

$templateRoot = Join-Path $PSScriptRoot '..' 'assets' 'templates'
$indexPath = Assert-DesignNotesDestination -Path (Join-Path $root 'docs/design-notes/.design-notes.md')
$mappings = @(
    [pscustomobject]@{
        Source      = Join-Path $templateRoot 'design-notes-index.template.md'
        Destination = $indexPath
    }
    [pscustomobject]@{
        Source      = Join-Path $templateRoot 'design-note-writing-style.template.md'
        Destination = Assert-DesignNotesDestination -Path (
            Join-Path $root 'docs/design-notes/project/design-note-writing-style.design.md'
        )
    }
)

foreach ($mapping in $mappings) {
    if (-not (Test-Path -LiteralPath $mapping.Source -PathType Leaf)) {
        throw "Installed design-notes template not found: $($mapping.Source)"
    }
}

$created = [System.Collections.Generic.List[string]]::new()
foreach ($mapping in $mappings) {
    if (Test-Path -LiteralPath $mapping.Destination) {
        if (-not (Test-Path -LiteralPath $mapping.Destination -PathType Leaf)) {
            throw "Design-notes destination exists but is not a file: $($mapping.Destination)"
        }
        continue
    }

    $directory = Split-Path -Parent $mapping.Destination
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        [void][System.IO.Directory]::CreateDirectory($directory)
    }
    [System.IO.File]::Copy($mapping.Source, $mapping.Destination, $false)
    $created.Add($mapping.Destination)
}

return [pscustomobject]@{
    Action  = if ($created.Count -gt 0) { 'created' } else { 'skipped' }
    Path    = $indexPath
    Created = $created.ToArray()
    Message = if ($created.Count -gt 0) {
        'design-notes initialized'
    }
    else {
        'design-notes already initialized'
    }
}
