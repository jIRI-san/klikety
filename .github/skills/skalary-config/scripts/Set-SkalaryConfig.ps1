#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('preview', 'bootstrap', 'edit', 'reset', 'apply', 'cancel')]
    [string]$Action,

    [Parameter(Mandatory)]
    [ValidateSet('autopilot', 'local-review-standards', 'models-reviews')]
    [string]$Category,

    [string]$RepoRoot = (Get-Location).Path,
    [string]$ExpectedDigest,
    [string]$ChangesJson = '{}',
    [string]$Key,
    [ValidateSet('bootstrap', 'edit', 'reset')]
    [string]$ProposedAction = 'edit',
    [switch]$AcknowledgeExecutableSettings,
    [switch]$AcknowledgeLongContextCost
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = [System.IO.Path]::GetFullPath($RepoRoot)
if (-not (Test-Path -LiteralPath $root -PathType Container)) {
    throw "Repository root does not exist: $root"
}
$physicalRoot = (Resolve-Path -LiteralPath $root).Path

$reader = Join-Path $PSScriptRoot 'Read-SkalaryConfig.ps1'
function Get-CategoryState {
    param([string]$StateCategory)
    return & $reader -Action preview -Category $StateCategory -RepoRoot $root | ConvertFrom-Json
}
function Get-CanonicalPath {
    param([string]$RelativePath)
    $path = Join-Path $root $RelativePath
    $existing = $path
    while (-not (Test-Path -LiteralPath $existing)) {
        $parent = Split-Path -Path $existing -Parent
        if ($parent -eq $existing) { throw "Configuration path has no existing parent: $RelativePath" }
        $existing = $parent
    }
    while ($true) {
        $item = Get-Item -LiteralPath $existing -Force
        if ($item.LinkType) { throw "Refusing linked configuration path: $RelativePath" }
        if ($existing -eq $physicalRoot) { break }
        $existing = Split-Path -Path $existing -Parent
    }
    return $path
}
function ConvertTo-CanonicalJson {
    param([object]$Value)
    return ($Value | ConvertTo-Json -Depth 20)
}
function Test-NoSecret {
    param([string]$Text)
    if ($Text -match '(?i)(?:github_pat_|ghp_|gho_|ghu_|pat\s*[:=]|token\s*[:=]|password\s*[:=])') {
        throw 'Credential values are not accepted in configuration changes.'
    }
}
function Get-ChangeMap {
    Test-NoSecret -Text $ChangesJson
    try { $map = $ChangesJson | ConvertFrom-Json -AsHashtable }
    catch { throw "ChangesJson must be a JSON object: $($_.Exception.Message)" }
    if ($null -eq $map) { return @{} }
    return $map
}
function Get-TextDiff {
    param([string]$Path, [string]$Before, [string]$After)
    if ($Before -ceq $After) { return 'No changes.' }
    $beforePath = [System.IO.Path]::GetTempFileName()
    $afterPath = [System.IO.Path]::GetTempFileName()
    try {
        [System.IO.File]::WriteAllText($beforePath, $Before, [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText($afterPath, $After, [System.Text.UTF8Encoding]::new($false))
        $diff = (& git diff --no-index --no-ext-diff -- $beforePath $afterPath 2>$null) -join "`n"
        # A changed diff exits 1 by design, not because the preview failed.
        $global:LASTEXITCODE = 0
        return $diff.Replace($beforePath, "a/$Path").Replace($afterPath, "b/$Path")
    }
    finally {
        Remove-Item -LiteralPath $beforePath, $afterPath -Force -ErrorAction SilentlyContinue
    }
}
function Get-RecoveryCommand {
    param([string]$StateCategory)
    switch ($StateCategory) {
        'models-reviews' {
            return 'pwsh -NoProfile -File scripts/skalary/Sync-ModelBindings.ps1 -RepoRoot .; pwsh -NoProfile -File scripts/skalary/Test-ModelAllowlist.ps1 -RepoRoot .'
        }
        'autopilot' {
            return 'pwsh -NoProfile -Command "Get-Content .autopilot.json -Raw | Test-Json -SchemaFile .github/skills/autopilot/schemas/autopilot.schema.json"'
        }
        'local-review-standards' {
            return 'git diff -- docs/review-standards.md'
        }
        default {
            throw "No recovery command is defined for category '$StateCategory'."
        }
    }
}
function Get-ShippedModelPolicy {
    $content = & git -C $root show 'HEAD:tools/model-allowlist.psd1' 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'Cannot derive shipped model defaults from HEAD.' }
    $path = [System.IO.Path]::GetTempFileName()
    try {
        [System.IO.File]::WriteAllText($path, ($content -join "`n"), [System.Text.UTF8Encoding]::new($false))
        return Import-PowerShellDataFile -LiteralPath $path
    }
    finally { Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue }
}
function Update-Autopilot {
    param([string]$Operation, [hashtable]$Changes)
    $path = Get-CanonicalPath '.autopilot.json'
    $before = if (Test-Path -LiteralPath $path) { [System.IO.File]::ReadAllText($path) } else { '' }
    $example = Get-CanonicalPath '.github/skills/autopilot/.autopilot.json.example'
    if (-not (Test-Path -LiteralPath $example -PathType Leaf)) { throw 'Autopilot shipped example is unavailable.' }
    if ($Operation -eq 'bootstrap') {
        if (Test-Path -LiteralPath $path) { throw '.autopilot.json already exists; use edit or reset.' }
        return @{ Path = '.autopilot.json'; Before = ''; After = [System.IO.File]::ReadAllText($example) }
    }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw '.autopilot.json is missing; use bootstrap first.' }
    try { $config = $before | ConvertFrom-Json -AsHashtable }
    catch { throw ".autopilot.json is malformed: $($_.Exception.Message)" }
    $defaults = [System.IO.File]::ReadAllText($example) | ConvertFrom-Json -AsHashtable
    $allowed = @('runtime', 'copilotAuth', 'gitProvider', 'gitAuth', 'model', 'context', 'reasoningEffort', 'maxIterationsPerStep', 'build', 'test', 'adoOrg', 'adoProject', 'dockerfileExtensions', 'offlinePackages')
    $requested = if ($Operation -eq 'reset') { if (-not $Key) { throw 'reset requires -Key.' }; @{$Key = $defaults[$Key]} } else { $Changes }
    foreach ($entry in $requested.GetEnumerator()) {
        if ($entry.Key -notin $allowed) { throw "Unsupported autopilot key: $($entry.Key)" }
        if ($Operation -eq 'reset' -and -not $defaults.ContainsKey($entry.Key)) { throw "No shipped default exists for '$($entry.Key)'." }
        if ($entry.Key -in @('runtime', 'build', 'test', 'dockerfileExtensions') -and -not $AcknowledgeExecutableSettings) {
            throw "Changing '$($entry.Key)' can execute code. Re-run with -AcknowledgeExecutableSettings."
        }
        if ($entry.Key -eq 'context' -and $entry.Value -eq 'long_context' -and -not $AcknowledgeLongContextCost) {
            throw 'long_context costs more. Re-run with -AcknowledgeLongContextCost.'
        }
        $config[$entry.Key] = $entry.Value
    }
    $after = ConvertTo-CanonicalJson -Value $config
    $schema = Get-CanonicalPath '.github/skills/autopilot/schemas/autopilot.schema.json'
    if (-not (($after | Test-Json -SchemaFile $schema -ErrorAction SilentlyContinue))) {
        throw 'Autopilot configuration fails .github/skills/autopilot/schemas/autopilot.schema.json.'
    }
    return @{ Path = '.autopilot.json'; Before = $before; After = $after }
}
function Update-ReviewStandards {
    param([string]$Operation, [hashtable]$Changes)
    $path = Get-CanonicalPath 'docs/review-standards.md'
    $before = if (Test-Path -LiteralPath $path) { [System.IO.File]::ReadAllText($path) } else { '' }
    if ($Operation -eq 'bootstrap') {
        if (Test-Path -LiteralPath $path) { throw 'docs/review-standards.md already exists; use edit or reset.' }
        return @{ Path = 'docs/review-standards.md'; Before = ''; After = "# Review standards`n" }
    }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'docs/review-standards.md is missing; use bootstrap first.' }
    $requested = if ($Operation -eq 'reset') { if (-not $Key) { throw 'reset requires -Key.' }; @{$Key = $null} } else { $Changes }
    $after = $before
    foreach ($entry in $requested.GetEnumerator()) {
        if ($entry.Key -notin @('focus', 'exceptions')) { throw "Unsupported local review standards key: $($entry.Key)" }
        $mode = if ($entry.Key -eq 'focus') { 'extend' } else { 'replace' }
        $pattern = "(?m)^- (?:extend|replace) ``$([regex]::Escape($entry.Key))``: .+\r?\n?"
        if ($Operation -eq 'reset') {
            if ($after -notmatch $pattern) { throw "No managed '$($entry.Key)' entry exists to reset." }
            $after = [regex]::Replace($after, $pattern, '')
            continue
        }
        $value = [string]$entry.Value
        Test-NoSecret -Text $value
        if ($value -match '[\r\n]') { throw 'Local review standards guidance must be one line.' }
        $replacement = "- $mode ``$($entry.Key)``: $value`n"
        if ($after -match $pattern) { $after = [regex]::Replace($after, $pattern, [System.Text.RegularExpressions.MatchEvaluator]{ param($m) $replacement }) }
        else { $after = $after.TrimEnd() + "`n`n$replacement" }
    }
    return @{ Path = 'docs/review-standards.md'; Before = $before; After = $after }
}
function Update-ModelPolicy {
    param([string]$Operation, [hashtable]$Changes)
    $path = Get-CanonicalPath 'tools/model-allowlist.psd1'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'Model routing requires maintainer source and tools.' }
    $before = [System.IO.File]::ReadAllText($path)
    $policy = Import-PowerShellDataFile -LiteralPath $path
    $defaults = Get-ShippedModelPolicy
    $requested = if ($Operation -eq 'reset') { if (-not $Key) { throw 'reset requires -Key.' }; @{$Key = $null} } else { $Changes }
    $after = $before
    foreach ($entry in $requested.GetEnumerator()) {
        $parts = $entry.Key -split '\.'
        $value = $entry.Value
        if ($parts.Count -eq 3 -and $parts[0] -eq 'Aliases' -and $policy.Aliases.ContainsKey($parts[1]) -and $parts[2] -in @('Cli', 'VSCode')) {
            if ($Operation -eq 'reset') { $value = $defaults.Aliases[$parts[1]][$parts[2]] }
            $binding = [string]$value
            $validBinding = if ($parts[2] -eq 'Cli') {
                $binding -match '^[a-z0-9][a-z0-9.-]*$'
            }
            else {
                $binding -match '^[^\r\n()]+\s\([^\r\n()]+\)$'
            }
            if (-not $validBinding) { throw "Model binding '$($entry.Key)' has an invalid host-specific format." }
        }
        elseif ($parts.Count -eq 3 -and $parts[0] -eq 'Roles' -and $parts[1] -in @('Routine', 'Standard', 'Deep', 'Independent') -and $parts[2] -in @('Primary', 'Fallback', 'ReasoningEffort')) {
            if ($Operation -eq 'reset') { $value = $defaults.Roles[$parts[1]][$parts[2]] }
            if ($parts[2] -in @('Primary', 'Fallback') -and -not $policy.Aliases.ContainsKey([string]$value)) { throw "Role '$($entry.Key)' must reference a known alias." }
            if ($parts[2] -eq 'ReasoningEffort' -and $value -notin @('low', 'medium', 'high', 'xhigh', 'max')) { throw "Invalid reasoning effort for '$($entry.Key)'." }
        }
        elseif ($parts.Count -eq 2 -and $parts[0] -eq 'Roles' -and $parts[1] -in @('WazaExecutor', 'WazaJudge')) {
            if ($Operation -eq 'reset') { $value = $defaults.Roles[$parts[1]] }
            if (-not $policy.Aliases.ContainsKey([string]$value)) { throw "Role '$($entry.Key)' must reference a known alias." }
        }
        elseif ($parts.Count -eq 2 -and $parts[0] -eq 'Fallback' -and $parts[1] -in @('VSCode', 'Cli')) {
            if ($Operation -eq 'reset') { $value = $defaults.Fallback[$parts[1]] }
            if (-not $policy.Aliases.ContainsKey([string]$value)) { throw "Fallback '$($entry.Key)' must reference a known alias." }
        }
        else { throw "Unsupported model routing key: $($entry.Key)" }
        $leaf = [regex]::Escape($parts[-1])
        if ($parts[0] -eq 'Aliases') {
            $section = "(?ms)(?<block>^[ \t]*'$([regex]::Escape($parts[1]))'[ \t]*=[ \t]*@\{.*?^[ \t]*\})"
        }
        elseif ($parts.Count -eq 3) {
            $section = "(?ms)(?<block>^[ \t]*$([regex]::Escape($parts[1]))[ \t]*=[ \t]*@\{.*?^[ \t]*\})"
        }
        elseif ($parts[0] -eq 'Fallback') {
            $section = "(?ms)(?<block>^[ \t]*Fallback[ \t]*=[ \t]*@\{.*?^[ \t]*\})"
        }
        else {
            $section = "(?m)(?<block>^[ \t]*$leaf[ \t]*=[ \t]*'[^']*')"
        }
        $field = if ($parts.Count -eq 2) {
            "(?m)^(?<prefix>[ \t]*$leaf[ \t]*=[ \t]*)'[^']*'"
        }
        else {
            "(?m)^(?<prefix>[ \t]*$leaf[ \t]*=[ \t]*)'[^']*'"
        }
        $sectionMatch = [regex]::Match($after, $section)
        if (-not $sectionMatch.Success -or $sectionMatch.Groups['block'].Value -notmatch $field) {
            throw "Cannot change '$($entry.Key)' without rewriting unrelated model policy."
        }
        $after = [regex]::Replace($after, $section, [System.Text.RegularExpressions.MatchEvaluator]{
                param($sectionMatch)
                $block = $sectionMatch.Groups['block'].Value
                return [regex]::Replace($block, $field, [System.Text.RegularExpressions.MatchEvaluator]{
                        param($fieldMatch)
                        "$($fieldMatch.Groups['prefix'].Value)'$(([string]$value).Replace("'", "''"))'"
                    }, 1)
            }, 1)
    }
    $temporary = [System.IO.Path]::GetTempFileName()
    try {
        [System.IO.File]::WriteAllText($temporary, $after, [System.Text.UTF8Encoding]::new($false))
        $null = Import-PowerShellDataFile -LiteralPath $temporary
    }
    finally { Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue }
    return @{ Path = 'tools/model-allowlist.psd1'; Before = $before; After = $after }
}

$state = Get-CategoryState -StateCategory $Category
if ($Action -eq 'cancel') {
    [ordered]@{ Action = 'cancel'; Category = $Category; Result = 'Cancelled. No files changed.' } | ConvertTo-Json
    return
}
if ($Action -eq 'apply' -and [string]::IsNullOrWhiteSpace($ExpectedDigest)) {
    throw 'Apply requires the SourceDigest returned by preview.'
}
if ($ExpectedDigest -and $ExpectedDigest -ne $state.SourceDigest) {
    throw "SourceChanged: $Category canonical inputs changed; preview again before Apply."
}
$operation = if ($Action -eq 'apply') { $ProposedAction } else { $Action }
$changes = Get-ChangeMap
$proposal = switch ($Category) {
    'autopilot' { Update-Autopilot -Operation $operation -Changes $changes }
    'local-review-standards' { Update-ReviewStandards -Operation $operation -Changes $changes }
    'models-reviews' { Update-ModelPolicy -Operation $operation -Changes $changes }
}
$diff = Get-TextDiff -Path $proposal.Path -Before $proposal.Before -After $proposal.After
if ($Action -in @('preview', 'bootstrap', 'edit', 'reset')) {
    [ordered]@{ Action = $Action; Category = $Category; SourceDigest = $state.SourceDigest; Diff = $diff; Synchronizer = if ($Category -eq 'models-reviews') { 'scripts/skalary/Sync-ModelBindings.ps1' } else { $null }; Validator = if ($Category -eq 'models-reviews') { 'scripts/skalary/Test-ModelAllowlist.ps1' } else { 'Autopilot schema or CR/DR local standards resolver' }; Risks = 'Apply is category-bounded and refuses a stale digest. Cancel is byte-clean.' } | ConvertTo-Json -Depth 5
    return
}
try {
    $proposalDirectory = Split-Path -Parent (Join-Path $root $proposal.Path)
    if (-not (Test-Path -LiteralPath $proposalDirectory -PathType Container)) {
        [void](New-Item -ItemType Directory -Path $proposalDirectory -Force)
    }
    [System.IO.File]::WriteAllText((Join-Path $root $proposal.Path), $proposal.After, [System.Text.UTF8Encoding]::new($false))
}
catch {
    throw "Write failed for $($proposal.Path). No rollback was attempted; inspect the visible diff. Recover with: $(Get-RecoveryCommand -StateCategory $Category). $($_.Exception.Message)"
}
if ($Category -eq 'models-reviews') {
    try {
        Push-Location -LiteralPath $root
        try {
            & 'scripts/skalary/Sync-ModelBindings.ps1' -RepoRoot $root
            if ($LASTEXITCODE -ne 0) {
                throw "Model binding synchronization exited with code $LASTEXITCODE."
            }
        }
        catch {
            throw "Model synchronization failed. No rollback was attempted; inspect the visible diff. Recover with: $(Get-RecoveryCommand -StateCategory $Category). $($_.Exception.Message)"
        }
        try {
            & 'scripts/skalary/Test-ModelAllowlist.ps1' -RepoRoot $root
            if ($LASTEXITCODE -ne 0) {
                throw "Model allowlist validation exited with code $LASTEXITCODE."
            }
        }
        catch {
            throw "Model validation failed. No rollback was attempted; inspect the visible diff. Recover with: $(Get-RecoveryCommand -StateCategory $Category). $($_.Exception.Message)"
        }
    }
    finally {
        Pop-Location
    }
}
$finalDiff = if (Test-Path -LiteralPath (Join-Path $root '.git')) { (& git -C $root diff --no-ext-diff -- $proposal.Path) -join "`n" } else { $diff }
[ordered]@{
    Action = 'apply'
    Category = $Category
    FinalDiff = $finalDiff
    RecoveryCommand = Get-RecoveryCommand -StateCategory $Category
    Result = 'Applied canonical changes. No rollback was attempted.'
} | ConvertTo-Json -Depth 5
