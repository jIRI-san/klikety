#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('show', 'validate', 'diff', 'preview')]
    [string]$Action,

    [Parameter(Mandatory)]
    [ValidateSet(
        'autopilot',
        'models-reviews',
        'local-review-standards',
        'terminal-approvals',
        'evals',
        'design-architecture',
        'plugin-distribution',
        'repository-toolchain'
    )]
    [string]$Category,

    [string]$RepoRoot = (Get-Location).Path,
    [string]$ExpectedDigest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = [System.IO.Path]::GetFullPath($RepoRoot)
if (-not (Test-Path -LiteralPath $root -PathType Container)) {
    throw "Repository root does not exist: $root"
}

$categories = @{
    autopilot = @{
        Canonical = @('.autopilot.json')
        Default = '.github/skills/autopilot/.autopilot.json.example'
        Generated = @()
        Precedence = 'Root .autopilot.json overrides the shipped example.'
        Sensitivity = 'Executable settings; credentials are availability-only.'
        Bootstrap = 'Selected-only from the shipped example.'
        Owner = 'Autopilot launcher'
        Validator = 'tests/autopilot/ModelConfiguration.Tests.ps1'
        Installed = $true
    }
    'models-reviews' = @{
        Canonical = @('tools/model-allowlist.psd1')
        Default = 'Committed aliases and role assignments.'
        Generated = @('plugins/*/skills/*/assets/model-aliases.psd1', '.github/skills/*/assets/model-aliases.psd1')
        Precedence = 'The allowlist is authoritative; bindings are generated.'
        Sensitivity = 'Advanced maintainer policy.'
        Bootstrap = 'None.'
        Owner = 'Sync-ModelBindings.ps1'
        Validator = 'Test-ModelAllowlist.ps1'
        Installed = $false
    }
    'local-review-standards' = @{
        Canonical = @('docs/review-standards.md')
        Default = 'No shipped local file; base CR/DR rules remain active.'
        Generated = @()
        Precedence = 'Optional local standards refine supplied base rules.'
        Sensitivity = 'Non-secret Markdown.'
        Bootstrap = 'Selected-only strict Markdown scaffold.'
        Owner = 'CR/DR standards resolver'
        Validator = 'tests/skalary/ReviewPolicy.Tests.ps1'
        Installed = $true
    }
    'terminal-approvals' = @{
        Canonical = @('.vscode/settings.json')
        Default = 'No Skalary default.'
        Generated = @()
        Precedence = 'VS Code workspace settings are authoritative.'
        Sensitivity = 'Read-only focused scripts only.'
        Bootstrap = 'No automatic bootstrap.'
        Owner = 'Set-ScriptApproval.ps1'
        Validator = 'tests/skalary/SetScriptApproval.Tests.ps1'
        Installed = $true
    }
    evals = @{
        Canonical = @('.eval.config.json', '.eval.config.json.example')
        Default = '.eval.config.json.example'
        Generated = @('plugins/*/evals/waza/eval.yaml')
        Precedence = 'Per-plugin Waza specs own model and judge values; local config owns credential targets.'
        Sensitivity = 'Never reads credential values.'
        Bootstrap = 'Selected-only copy of the example.'
        Owner = 'Resolve-EvalToken.ps1 and Invoke-WazaEvals.ps1'
        Validator = 'tests/evals/EvalTools.Tests.ps1'
        Installed = $false
    }
    'design-architecture' = @{
        Canonical = @('docs/design-notes', 'docs/architecture-notes')
        Default = 'Owning plugin templates.'
        Generated = @()
        Precedence = 'Existing notes and contracts retain ownership.'
        Sensitivity = 'Architecture lock promotion is excluded.'
        Bootstrap = 'Use the owning scaffold command.'
        Owner = 'design-notes and architecture-notes plugins'
        Validator = 'Test-ArchContract.ps1 and architecture-notes plugin evals'
        Installed = $true
    }
    'plugin-distribution' = @{
        Canonical = @('plugins')
        Default = 'Plugin manifests.'
        Generated = @('registry.json', '.github/plugin/marketplace.json', '.github')
        Precedence = 'Plugin source is authoritative; catalogs and dogfood are generated.'
        Sensitivity = 'Advanced maintainer policy.'
        Bootstrap = 'None.'
        Owner = 'distribution generators'
        Validator = 'Test-Registry.ps1'
        Installed = $false
    }
    'repository-toolchain' = @{
        Canonical = @('package.json', 'tools/eval-tools.psd1', '.github/copilot-instructions.md')
        Default = 'Committed repository policy.'
        Generated = @('package-lock.json')
        Precedence = 'Owning package and tool policies are authoritative.'
        Sensitivity = 'Advanced and show-only by default.'
        Bootstrap = 'None.'
        Owner = 'package and tool owners'
        Validator = 'Owning focused validator'
        Installed = $false
    }
}

$entry = $categories[$Category]
$sourceLayout = (Test-Path -LiteralPath (Join-Path $root 'plugins') -PathType Container) -and
    (Test-Path -LiteralPath (Join-Path $root 'scripts/skalary') -PathType Container)
$layout = if ($sourceLayout) { 'source' } else { 'installed-consumer' }

$inputs = @()
foreach ($relative in $entry.Canonical) {
    $path = Join-Path $root $relative
    if (Test-Path -LiteralPath $path) {
        $item = Get-Item -LiteralPath $path -Force
        if ($item.LinkType) { throw "Refusing linked configuration path: $relative" }
        if ($item -is [System.IO.FileInfo]) { $inputs += $path }
    }
}
$digestInputs = @($inputs)
if ($Category -eq 'autopilot') {
    $defaultPath = Join-Path $root '.github/skills/autopilot/.autopilot.json.example'
    if (Test-Path -LiteralPath $defaultPath -PathType Leaf) {
        $defaultItem = Get-Item -LiteralPath $defaultPath -Force
        if ($defaultItem.LinkType) { throw 'Refusing linked configuration path: .github/skills/autopilot/.autopilot.json.example' }
        $digestInputs += $defaultPath
    }
}
$hasher = [System.Security.Cryptography.SHA256]::Create()
try {
    $digestMaterial = foreach ($path in @($digestInputs | Sort-Object -Unique)) {
        $relative = [System.IO.Path]::GetRelativePath($root, $path)
        "${relative}:$([Convert]::ToHexString($hasher.ComputeHash([System.IO.File]::ReadAllBytes($path))))"
    }
    if ($Category -eq 'models-reviews' -and $sourceLayout) {
        $headPolicy = & git -C $root rev-parse --verify 'HEAD:tools/model-allowlist.psd1' 2>$null
        if ($LASTEXITCODE -ne 0 -or $headPolicy -notmatch '^[0-9a-f]{40,64}$') {
            throw 'Cannot derive shipped model defaults from HEAD.'
        }
        $digestMaterial += "HEAD:tools/model-allowlist.psd1:$headPolicy"
    }
    $digest = [Convert]::ToHexString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes(($digestMaterial -join "`n")))).ToLowerInvariant()
}
finally {
    $hasher.Dispose()
}

if ($ExpectedDigest -and $ExpectedDigest -ne $digest) {
    throw "SourceChanged: $Category canonical inputs changed; preview again before Apply."
}

$result = [ordered]@{
    Action = $Action
    Category = $Category
    Layout = $layout
    CanonicalPaths = @($entry.Canonical)
    ExistingCanonicalPaths = @($inputs | ForEach-Object { [System.IO.Path]::GetRelativePath($root, $_) })
    Default = $entry.Default
    GeneratedPaths = @($entry.Generated)
    Precedence = $entry.Precedence
    Sensitivity = $entry.Sensitivity
    Bootstrap = $entry.Bootstrap
    Owner = $entry.Owner
    Validator = $entry.Validator
    InstalledConsumerAvailable = [bool]$entry.Installed
    SourceDigest = $digest
}

function Get-TerminalApprovalState {
    $settingsPath = Join-Path $root '.vscode/settings.json'
    if (-not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) {
        return [ordered]@{ SettingsPresent = $false; ReadOnlyApprovals = @() }
    }
    $options = [System.Text.Json.JsonDocumentOptions]::new()
    $options.CommentHandling = [System.Text.Json.JsonCommentHandling]::Skip
    $options.AllowTrailingCommas = $true
    try {
        $document = [System.Text.Json.JsonDocument]::Parse(
            [System.IO.File]::ReadAllText($settingsPath), $options
        )
        try {
            $approvals = [System.Collections.Generic.List[string]]::new()
            $property = @($document.RootElement.EnumerateObject() | Where-Object {
                    $_.Name -ceq 'chat.tools.terminal.autoApprove'
                }) | Select-Object -First 1
            if ($property -and $property.Value.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
                foreach ($approval in $property.Value.EnumerateObject()) {
                    if ($approval.Value.ValueKind -eq [System.Text.Json.JsonValueKind]::True -and
                        $approval.Name -match '^\.github/skills/[a-z0-9-]+/scripts/(?:Get|Find|Test|Validate)-[^/]+\.ps1$') {
                        $approvals.Add($approval.Name)
                    }
                }
            }
            return [ordered]@{
                SettingsPresent = $true
                ReadOnlyApprovals = @($approvals | Sort-Object -Unique)
            }
        }
        finally {
            $document.Dispose()
        }
    }
    catch {
        throw ".vscode/settings.json is malformed: $($_.Exception.Message)"
    }
}
function Get-EvalState {
    $configPath = Join-Path $root '.eval.config.json'
    $targets = @()
    if (Test-Path -LiteralPath $configPath -PathType Leaf) {
        try {
            $config = [System.IO.File]::ReadAllText($configPath) | ConvertFrom-Json -AsHashtable
        }
        catch {
            throw ".eval.config.json is malformed: $($_.Exception.Message)"
        }
        if ($config.ContainsKey('credentialTargets')) {
            $targets = @($config.credentialTargets | Where-Object { $_ -is [string] -and $_.Trim() })
        }
        elseif ($config.ContainsKey('credentialTarget') -and $config.credentialTarget -is [string] -and $config.credentialTarget.Trim()) {
            $targets = @($config.credentialTarget)
        }
    }
    $specs = @()
    if ($sourceLayout) {
        foreach ($path in @(Get-ChildItem -LiteralPath (Join-Path $root 'plugins') -Filter 'eval.yaml' -Recurse -File -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName.Replace('\', '/') -match '/evals/waza/eval\.yaml$' } | Sort-Object FullName)) {
            $content = [System.IO.File]::ReadAllText($path)
            $model = if ($content -match '(?m)^\s*model:\s*(?<value>\S+)') { $Matches.value } else { $null }
            $judgeModel = if ($content -match '(?m)^\s*judge_model:\s*(?<value>\S+)') { $Matches.value } else { $null }
            foreach ($binding in @($model, $judgeModel | Where-Object { $null -ne $_ })) {
                if ($binding -notmatch '^[a-z0-9][a-z0-9.-]*$') {
                    throw "Waza model binding in $([System.IO.Path]::GetRelativePath($root, $path.FullName)) has an invalid format."
                }
            }
            $specs += [ordered]@{
                Path = [System.IO.Path]::GetRelativePath($root, $path.FullName).Replace('\', '/')
                Model = $model
                JudgeModel = $judgeModel
            }
        }
    }
    return [ordered]@{
        ConfigPresent = (Test-Path -LiteralPath $configPath -PathType Leaf)
        CredentialTargets = @($targets | Sort-Object -Unique)
        WazaSpecs = @($specs)
    }
}

if ($Action -eq 'show' -and $Category -eq 'autopilot' -and $inputs) {
    try {
        $config = [System.IO.File]::ReadAllText($inputs[0]) | ConvertFrom-Json -AsHashtable
    }
    catch {
        throw ".autopilot.json is malformed: $($_.Exception.Message)"
    }
    $safeKeys = @(
        'runtime', 'copilotAuth', 'gitProvider', 'gitAuth', 'model', 'context',
        'reasoningEffort', 'maxIterationsPerStep', 'build', 'test'
    )
    $result.EffectiveValues = [ordered]@{}
    foreach ($key in $safeKeys) {
        if ($config.ContainsKey($key)) { $result.EffectiveValues[$key] = $config[$key] }
    }
}
if ($Action -eq 'show' -and $Category -eq 'models-reviews' -and $sourceLayout -and $inputs) {
    try {
        $policy = Import-PowerShellDataFile -LiteralPath $inputs[0]
    }
    catch {
        throw "tools/model-allowlist.psd1 is malformed: $($_.Exception.Message)"
    }
    $result.EffectiveValues = [ordered]@{
        Aliases = $policy.Aliases
        Roles = $policy.Roles
        Fallback = $policy.Fallback
    }
}
if ($Action -eq 'show' -and $Category -eq 'terminal-approvals') {
    $result.EffectiveValues = Get-TerminalApprovalState
    $result.OwnerCommand = 'scripts/skalary/Set-ScriptApproval.ps1 -Name <installed-plugin> -RepoRoot .'
}
if ($Action -eq 'show' -and $Category -eq 'evals') {
    $result.EffectiveValues = Get-EvalState
    $result.OwnerCommand = 'scripts/skalary/Resolve-EvalToken.ps1 -RepoRoot .; scripts/skalary/Invoke-WazaEvals.ps1 -Plugin <plugin>'
}
if ($Action -eq 'show' -and $Category -eq 'design-architecture') {
    $result.EffectiveValues = [ordered]@{
        DesignNotesPresent = (Test-Path -LiteralPath (Join-Path $root 'docs/design-notes/.design-notes.md') -PathType Leaf)
        ArchitectureNotesPresent = (Test-Path -LiteralPath (Join-Path $root 'docs/architecture-notes/.architecture-notes.md') -PathType Leaf)
    }
    $result.OwnerCommands = @(
        '.github/skills/design-notes/scripts/Initialize-DesignNotes.ps1 -RepoRoot .',
        '.github/skills/architecture-notes/scripts/Copy-ArchScaffold.ps1 -TargetRoot .'
    )
}
if ($Action -eq 'show' -and $Category -in @('plugin-distribution', 'repository-toolchain')) {
    $result.EffectiveValues = [ordered]@{
        MaintainerSourceAvailable = $sourceLayout
        CanonicalPathsPresent = @($entry.Canonical | Where-Object {
                Test-Path -LiteralPath (Join-Path $root $_)
            })
    }
    $result.OwnerCommand = if ($Category -eq 'plugin-distribution') {
        'scripts/skalary/Sync-PluginScripts.ps1, Build-Registry.ps1, Build-Marketplace.ps1, and Sync-Dogfood.ps1'
    }
    else {
        'Use the owning package or toolchain policy validator; this façade is show-only.'
    }
}
if ($Action -eq 'diff') {
    $result.CurrentDiff = if ($sourceLayout) {
        (& git -C $root diff --no-ext-diff -- @($entry.Canonical)) -join "`n"
    }
    else {
        'No source Git diff is available for this category.'
    }
}
if ($Action -eq 'preview') {
    $result.Proposal = if ($Category -in @('autopilot', 'local-review-standards', 'models-reviews')) {
        'No requested changes. Use Set-SkalaryConfig.ps1 for this category-scoped mutation preview.'
    }
    else {
        'This category has no generic façade writer. Use its owner command; credential values, generated files, and advanced policy remain excluded.'
    }
    $result.Redaction = 'Credential values are never read, rendered, or included in the digest.'
}
if ($Action -eq 'validate') {
    $result.Validation = if ($layout -eq 'installed-consumer' -and -not $entry.Installed) {
        'Unavailable: this advanced category requires maintainer source and tools.'
    }
    else {
        "Available focused validator: $($entry.Validator)"
    }
}

$result | ConvertTo-Json -Depth 5
