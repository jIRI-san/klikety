#requires -Version 7.0
<#
.SYNOPSIS
    Emits the list of files under review for a `cr` run.
.DESCRIPTION
    One emitter for every review scope. Reviewers read the files themselves, so this
    script never extracts diff content — it prints repo-relative paths, one per line,
    sorted and de-duplicated.

    Modes:
      smart        (default) feature branch -> uncommitted + commits vs the default branch;
                   on the default branch -> uncommitted + commits not yet on the remote.
      uncommitted  staged + unstaged + untracked (non-ignored) files.
      branch       commits on the current branch that are not in the default branch.
      commits      the last -N commits (no merges).
      paths        literal files/folders on disk (folders recurse; build output excluded).

    Deleted paths are dropped by default: a reviewer that reads files cannot read a file
    that is gone. Pass -IncludeDeleted to keep them for change-history questions.
.EXAMPLE
    Get-ReviewScope.ps1
.EXAMPLE
    Get-ReviewScope.ps1 -Mode commits -N 3
.EXAMPLE
    Get-ReviewScope.ps1 -Mode paths -Paths src/Foo, src/Bar/Baz.cs
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('smart', 'uncommitted', 'branch', 'commits', 'paths')]
    [string]$Mode = 'smart',

    [int]$N,

    [string[]]$Paths,

    [string]$RepoRoot,

    [switch]$IncludeDeleted
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:ExcludedDirectories = @('bin', 'obj', 'node_modules', '.git', '.vs', '.worktrees')
$script:TextExtensions = @(
    '.cs', '.xaml', '.json', '.xml', '.md', '.ps1', '.psm1', '.psd1', '.txt', '.yaml', '.yml',
    '.csproj', '.slnx', '.sln', '.props', '.targets', '.editorconfig', '.gitignore', '.sh',
    '.js', '.ts', '.css', '.html', '.sql'
)

function Invoke-Git {
    param(
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string[]]$Arguments,
        [switch]$AllowFailure,
        [switch]$NullSeparated
    )

    # git emits path bytes as UTF-8. PowerShell decodes a native command's stdout using
    # [Console]::OutputEncoding, which on a default Windows console is an OEM codepage
    # (ibm437) — 'café.md' would decode to 'caf├⌐.md', resolve to no file, and be dropped
    # from the review scope silently while the run still exits 0.
    $previousEncoding = [Console]::OutputEncoding
    try {
        [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
        $output = & git -C $WorkingDirectory @Arguments 2>$null
        $exitCode = $LASTEXITCODE
    }
    finally {
        [Console]::OutputEncoding = $previousEncoding
    }

    if ($exitCode -ne 0) {
        if ($AllowFailure) { return , @() }
        throw "git $($Arguments -join ' ') failed with exit code $exitCode in '$WorkingDirectory'."
    }

    if ($NullSeparated) {
        # File-list commands run with -z: git only C-quotes paths (non-ASCII, quotes,
        # backslashes) in the newline-separated form, and a quoted path resolves to nothing.
        $text = ($output -join "`n")
        $split = $text.Split([char]0, [char]10)
        return , @($split | Where-Object { -not [string]::IsNullOrEmpty($_) })
    }

    # Comma-wrapped so a single-line result stays an array instead of unrolling to a string,
    # where [0] would silently mean "first character".
    return , @($output)
}

function Resolve-ReviewRepoRoot {
    param([string]$StartPath)

    $start = if ([string]::IsNullOrWhiteSpace($StartPath)) { (Get-Location).Path } else { $StartPath }
    if (-not (Test-Path -LiteralPath $start -PathType Container)) {
        throw "Repository path not found: $start"
    }

    $top = Invoke-Git -WorkingDirectory $start -Arguments @('rev-parse', '--show-toplevel') -AllowFailure
    if (-not $top -or [string]::IsNullOrWhiteSpace([string]$top[0])) {
        throw "Not inside a git work tree: $start"
    }
    return (Resolve-Path -LiteralPath ([string]$top[0])).Path
}

function Test-HasCommit {
    param([Parameter(Mandatory)][string]$Root)

    $head = Invoke-Git -WorkingDirectory $Root -Arguments @('rev-parse', '--verify', '--quiet', 'HEAD') -AllowFailure
    return [bool]($head -and -not [string]::IsNullOrWhiteSpace([string]$head[0]))
}

function Resolve-DefaultBranch {
    <#
        Returns the local default-branch name and the ref to diff against. The remote ref is
        preferred when it exists so a stale local `main` cannot silently widen or narrow scope.
    #>
    param([Parameter(Mandatory)][string]$Root)

    $remoteHead = Invoke-Git -WorkingDirectory $Root -Arguments @('rev-parse', '--abbrev-ref', 'origin/HEAD') -AllowFailure
    if ($remoteHead -and -not [string]::IsNullOrWhiteSpace([string]$remoteHead[0])) {
        $qualified = ([string]$remoteHead[0]).Trim()
        return [pscustomobject]@{ Name = ($qualified -replace '^origin/', ''); Ref = $qualified }
    }

    foreach ($candidate in @('main', 'master')) {
        $remote = Invoke-Git -WorkingDirectory $Root -Arguments @('rev-parse', '--verify', '--quiet', "refs/remotes/origin/$candidate") -AllowFailure
        if ($remote -and -not [string]::IsNullOrWhiteSpace([string]$remote[0])) {
            return [pscustomobject]@{ Name = $candidate; Ref = "origin/$candidate" }
        }
    }

    foreach ($candidate in @('main', 'master')) {
        $local = Invoke-Git -WorkingDirectory $Root -Arguments @('rev-parse', '--verify', '--quiet', "refs/heads/$candidate") -AllowFailure
        if ($local -and -not [string]::IsNullOrWhiteSpace([string]$local[0])) {
            return [pscustomobject]@{ Name = $candidate; Ref = $candidate }
        }
    }

    throw "Cannot resolve a default branch in '$Root': no origin/HEAD, origin/main, origin/master, main, or master."
}

function Get-UncommittedFile {
    param([Parameter(Mandatory)][string]$Root)

    $files = [System.Collections.Generic.List[string]]::new()
    if (Test-HasCommit -Root $Root) {
        foreach ($line in (Invoke-Git -WorkingDirectory $Root -Arguments @('diff', 'HEAD', '--name-only', '-z') -NullSeparated)) { $files.Add([string]$line) }
    }
    else {
        foreach ($line in (Invoke-Git -WorkingDirectory $Root -Arguments @('diff', '--cached', '--name-only', '-z') -NullSeparated)) { $files.Add([string]$line) }
    }

    # Untracked-but-not-ignored files are part of the change under review; the reviewer has to
    # read a brand-new file, and a diff-only list would never mention it.
    foreach ($line in (Invoke-Git -WorkingDirectory $Root -Arguments @('ls-files', '--others', '--exclude-standard', '-z') -NullSeparated)) { $files.Add([string]$line) }

    return $files
}

function Get-BranchFile {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$BaseRef
    )

    return @(Invoke-Git -WorkingDirectory $Root -Arguments @('diff', "$BaseRef...HEAD", '--name-only', '-z') -NullSeparated)
}

function Get-CommitFile {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][int]$Count
    )

    return @(Invoke-Git -WorkingDirectory $Root -Arguments @('log', '--name-only', '--format=', '-z', "-$Count", '--no-merges') -NullSeparated)
}

function Get-RangeFile {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Range
    )

    return @(Invoke-Git -WorkingDirectory $Root -Arguments @('log', '--name-only', '--format=', '-z', '--no-merges', $Range) -NullSeparated)
}

function Get-PathFile {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string[]]$InputPaths
    )

    $result = [System.Collections.Generic.List[string]]::new()
    foreach ($inputPath in $InputPaths) {
        $candidate = if ([System.IO.Path]::IsPathRooted($inputPath)) { $inputPath } else { Join-Path $Root $inputPath }

        if (Test-Path -LiteralPath $candidate -PathType Container) {
            # -Force: on Unix every dot-prefixed file is hidden, and .editorconfig/.gitignore
            # are explicitly reviewable extensions.
            $children = Get-ChildItem -LiteralPath $candidate -Recurse -File -Force | Where-Object {
                $normalized = $_.FullName.Replace('\', '/')
                $excluded = $false
                foreach ($directory in $script:ExcludedDirectories) {
                    if ($normalized -match "/$([regex]::Escape($directory))/") { $excluded = $true; break }
                }
                (-not $excluded) -and ($script:TextExtensions -contains $_.Extension.ToLowerInvariant())
            }
            foreach ($child in $children) { $result.Add($child.FullName) }
        }
        elseif (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $result.Add((Resolve-Path -LiteralPath $candidate).Path)
        }
        else {
            throw "Path not found: $inputPath"
        }
    }

    # A path outside the repo would be emitted as a mangled relative path and then silently
    # dropped by the existence filter — a review of nothing that exits 0.
    $rootPrefix = $Root.Replace('\', '/').TrimEnd('/') + '/'
    foreach ($resolved in $result) {
        if (-not $resolved.Replace('\', '/').StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Path is outside the repository root '$Root': $resolved"
        }
    }
    return $result
}

function ConvertTo-RepoRelativePath {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Candidates
    )

    $rootNormalized = $Root.Replace('\', '/').TrimEnd('/') + '/'
    $relative = [System.Collections.Generic.List[string]]::new()
    foreach ($candidate in $Candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate)) { continue }
        $normalized = $candidate.Replace('\', '/')
        if ($normalized.StartsWith($rootNormalized, [System.StringComparison]::OrdinalIgnoreCase)) {
            $normalized = $normalized.Substring($rootNormalized.Length)
        }
        $relative.Add($normalized.TrimStart('/'))
    }
    return $relative
}

$root = Resolve-ReviewRepoRoot -StartPath $RepoRoot

switch ($Mode) {
    'commits' {
        if ($N -lt 1) { throw "Mode 'commits' requires -N with a commit count of 1 or more." }
    }
    'paths' {
        if (-not $Paths -or @($Paths).Count -eq 0) { throw "Mode 'paths' requires -Paths with at least one file or folder." }
    }
    default {
        if ($PSBoundParameters.ContainsKey('Paths')) { throw "-Paths is only valid with -Mode paths." }
    }
}
if ($Mode -ne 'commits' -and $PSBoundParameters.ContainsKey('N')) {
    throw "-N is only valid with -Mode commits."
}

$collected = [System.Collections.Generic.List[string]]::new()

switch ($Mode) {
    'uncommitted' {
        foreach ($file in (Get-UncommittedFile -Root $root)) { $collected.Add($file) }
    }
    'branch' {
        $default = Resolve-DefaultBranch -Root $root
        foreach ($file in (Get-BranchFile -Root $root -BaseRef $default.Ref)) { $collected.Add([string]$file) }
    }
    'commits' {
        foreach ($file in (Get-CommitFile -Root $root -Count $N)) { $collected.Add([string]$file) }
    }
    'paths' {
        foreach ($file in (Get-PathFile -Root $root -InputPaths $Paths)) { $collected.Add([string]$file) }
    }
    'smart' {
        foreach ($file in (Get-UncommittedFile -Root $root)) { $collected.Add($file) }

        if (-not (Test-HasCommit -Root $root)) {
            # A repo without a first commit has no branch ref to compare against; the
            # uncommitted set is the whole change.
            break
        }

        $default = Resolve-DefaultBranch -Root $root
        $branchOutput = @(Invoke-Git -WorkingDirectory $root -Arguments @('branch', '--show-current'))
        # Detached HEAD reports nothing; treat it as "not the default branch" so the wider
        # branch-vs-default scope is used rather than silently reviewing nothing.
        $currentBranch = if ($branchOutput.Count -gt 0) { ([string]$branchOutput[0]).Trim() } else { '' }

        if ($currentBranch -eq $default.Name) {
            # On the default branch the only committed work worth reviewing is what has not
            # reached the remote yet; everything else is already reviewed history.
            foreach ($file in (Get-RangeFile -Root $root -Range "$($default.Ref)..HEAD")) { $collected.Add([string]$file) }
        }
        else {
            foreach ($file in (Get-BranchFile -Root $root -BaseRef $default.Ref)) { $collected.Add([string]$file) }
        }
    }
}

$files = ConvertTo-RepoRelativePath -Root $root -Candidates $collected

if (-not $IncludeDeleted) {
    $files = @($files | Where-Object { Test-Path -LiteralPath (Join-Path $root $_) -PathType Leaf })
}

$unique = [System.Collections.Generic.SortedSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($file in $files) { [void]$unique.Add($file) }
$unique | ForEach-Object { $_ }
