#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(ParameterSetName = 'Named', Mandatory)]
    [string]$Name,

    [Parameter(ParameterSetName = 'All', Mandatory)]
    [switch]$All,

    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path,

    [string]$RegistryPath,

    [string]$SettingsPath,

    [switch]$Remove
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '_Common.ps1')

# Read-only script verbs whose scripts are safe to auto-approve. Anything that
# fetches remote payloads or mutates the repo (Install/Uninstall/Update/Remove/
# Set/New/Add/Build/Sync/Repair/bootstrap ...) is deliberately excluded so a
# prompt-injected argument cannot ride an approval into a silent install.
$script:ReadOnlyVerbs = @('Get', 'Find', 'Test', 'Validate')
# Never auto-approve a script whose name suggests it emits secrets, even if its
# verb is read-only (e.g. `get-credential.ps1`) — auto-running it unprompted could
# surface a token into the session.
$script:SensitiveNameFragments = @('credential', 'secret', 'token', 'password', 'passphrase')
$script:SettingKey = 'chat.tools.terminal.autoApprove'
$script:CanonicalPlanFolderPattern = '(?:(?:(?:standalone|[0-9a-f]{6})-)?[0-9]{4}-[0-9]{2}-[0-9]{2}-[0-9a-f]{6}|[0-9]{3})-[a-z0-9]+(?:-[a-z0-9]+)*'
$script:ReviewWriterRules = @{
    'skills/cr/scripts/Build-ReviewReport.ps1' = "/^\\.github\\/skills\\/cr\\/scripts\\/Build-ReviewReport\\.ps1 -Mode (?:Freeze|Publish) -RunId [0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}(?: -PlanDir docs\\/implementation-plans\\/$script:CanonicalPlanFolderPattern)?$/"
    'skills/dr/scripts/Build-ReviewReport.ps1' = "/^\\.github\\/skills\\/dr\\/scripts\\/Build-ReviewReport\\.ps1 -Mode (?:Freeze|Publish) -RunId [0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}(?: -PlanDir docs\\/implementation-plans\\/$script:CanonicalPlanFolderPattern)?$/"
}
$script:ExactCommandApproval = '{"approve":true,"matchCommandLine":true}'
$script:PlanArtifactConsumerName = 'Get-PlanArtifactConsumerContext.ps1'
$script:PlanArtifactResolverName = 'Get-PlanArtifactContext.ps1'

function Test-ReadOnlyScript {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$FileName
    )

    $base = [System.IO.Path]::GetFileNameWithoutExtension($FileName)
    foreach ($fragment in $script:SensitiveNameFragments) {
        if ($base -match [regex]::Escape($fragment)) {
            return $false
        }
    }
    $dashIndex = $base.IndexOf('-')
    if ($dashIndex -lt 1) {
        return $false
    }
    $verb = $base.Substring(0, $dashIndex)
    return $script:ReadOnlyVerbs -contains $verb
}

function Get-PluginApprovalEntry {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        $Plugin,

        [Parameter(Mandatory)]
        [string]$RepoRootPath
    )

    $entries = @()
    foreach ($file in @($Plugin.files)) {
        $dest = ([string]$file.dest) -replace '\\', '/'
        if (-not $dest.EndsWith('.ps1')) {
            continue
        }
        if ($dest -notmatch '^skills/[^/]+/scripts/') {
            continue
        }
        # Confine to .github/ and only approve scripts that are actually present.
        $fullPath = Resolve-GithubConstrainedPath -RepoRoot $RepoRootPath -RelativePath $dest
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            continue
        }

        $leafName = [System.IO.Path]::GetFileName($dest)
        if ($leafName -cin @($script:PlanArtifactConsumerName, $script:PlanArtifactResolverName)) {
            continue
        }
        if ($script:ReviewWriterRules.ContainsKey($dest)) {
            $entries += [pscustomobject]@{
                Key = $script:ReviewWriterRules[$dest]
                Value = $script:ExactCommandApproval
            }
            continue
        }
        if (-not (Test-ReadOnlyScript -FileName (Split-Path -Leaf $dest))) {
            continue
        }
        $entries += [pscustomobject]@{ Key = ".github/$dest"; Value = 'true' }
    }

    return @($entries | Sort-Object Key -Unique)
}

function Get-TargetPlugin {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        $Registry,

        [switch]$AllPlugins,

        [string]$PluginName
    )

    $plugins = @($Registry.plugins)
    if ($AllPlugins) {
        return @($plugins | Sort-Object name)
    }

    $match = @($plugins | Where-Object { [string]$_.name -eq $PluginName })
    if ($match.Count -eq 0) {
        throw "Plugin '$PluginName' not found in registry."
    }
    return , $match[0]
}

function Get-SettingsNewline {
    [CmdletBinding()]
    param([string]$Text)

    if ($Text -match "`r`n") {
        return "`r`n"
    }
    return "`n"
}

function Find-AutoApproveInnerSpan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Text)

    $keyMatch = [regex]::Match($Text, '"chat\.tools\.terminal\.autoApprove"\s*:\s*\{')
    if (-not $keyMatch.Success) {
        return $null
    }

    $openBrace = $Text.IndexOf('{', $keyMatch.Index + $keyMatch.Length - 1)
    $depth = 0
    $inString = $false
    $escaped = $false
    for ($i = $openBrace; $i -lt $Text.Length; $i++) {
        $ch = $Text[$i]
        if ($inString) {
            if ($escaped) { $escaped = $false }
            elseif ($ch -eq '\') { $escaped = $true }
            elseif ($ch -eq '"') { $inString = $false }
            continue
        }
        # Skip JSONC comments so braces inside them never miscount depth.
        if ($ch -eq '/' -and ($i + 1) -lt $Text.Length -and $Text[$i + 1] -eq '/') {
            $nl = $Text.IndexOf("`n", $i)
            if ($nl -lt 0) { break }
            $i = $nl
            continue
        }
        if ($ch -eq '/' -and ($i + 1) -lt $Text.Length -and $Text[$i + 1] -eq '*') {
            $end = $Text.IndexOf('*/', $i + 2)
            if ($end -lt 0) { throw 'Malformed .vscode/settings.json: unterminated block comment.' }
            $i = $end + 1
            continue
        }
        if ($ch -eq '"') { $inString = $true; continue }
        if ($ch -eq '{') { $depth++ }
        elseif ($ch -eq '}') {
            $depth--
            if ($depth -eq 0) {
                return [pscustomobject]@{ InnerStart = $openBrace + 1; InnerEnd = $i }
            }
        }
    }

    throw 'Malformed .vscode/settings.json: unbalanced braces in chat.tools.terminal.autoApprove.'
}

function Get-ApprovalEntrySpan {
    [CmdletBinding()]
    param([string]$Inner)

    $entries = [System.Collections.Generic.List[object]]::new()
    $i = 0
    while ($i -lt $Inner.Length) {
        if ([char]::IsWhiteSpace($Inner[$i]) -or $Inner[$i] -eq ',') { $i++; continue }
        if ($Inner[$i] -eq '/' -and ($i + 1) -lt $Inner.Length -and $Inner[$i + 1] -eq '/') {
            $nl = $Inner.IndexOf("`n", $i)
            $i = $(if ($nl -lt 0) { $Inner.Length } else { $nl + 1 })
            continue
        }
        if ($Inner[$i] -eq '/' -and ($i + 1) -lt $Inner.Length -and $Inner[$i + 1] -eq '*') {
            $endComment = $Inner.IndexOf('*/', $i + 2)
            if ($endComment -lt 0) { throw 'Malformed autoApprove block: unterminated block comment.' }
            $i = $endComment + 2
            continue
        }
        if ($Inner[$i] -ne '"') { $i++; continue }

        $entryStart = $i
        while ($entryStart -gt 0 -and $Inner[$entryStart - 1] -in @(' ', "`t")) { $entryStart-- }
        $keyStart = ++$i
        $escaped = $false
        while ($i -lt $Inner.Length) {
            $ch = $Inner[$i]
            if ($escaped) { $escaped = $false }
            elseif ($ch -eq '\') { $escaped = $true }
            elseif ($ch -eq '"') { break }
            $i++
        }
        if ($i -ge $Inner.Length) { throw 'Malformed autoApprove block: unterminated property key.' }
        $key = $Inner.Substring($keyStart, $i - $keyStart)
        $i++
        while ($i -lt $Inner.Length -and [char]::IsWhiteSpace($Inner[$i])) { $i++ }
        if ($i -ge $Inner.Length -or $Inner[$i] -ne ':') {
            throw "Malformed autoApprove block: property '$key' has no value separator."
        }
        $i++
        while ($i -lt $Inner.Length -and [char]::IsWhiteSpace($Inner[$i])) { $i++ }

        if ($i -lt $Inner.Length -and $Inner[$i] -in @('{', '[')) {
            $stack = [System.Collections.Generic.Stack[char]]::new()
            $stack.Push($Inner[$i])
            $i++
            $inString = $false
            $escaped = $false
            while ($i -lt $Inner.Length -and $stack.Count -gt 0) {
                $ch = $Inner[$i]
                if ($inString) {
                    if ($escaped) { $escaped = $false }
                    elseif ($ch -eq '\') { $escaped = $true }
                    elseif ($ch -eq '"') { $inString = $false }
                }
                elseif ($ch -eq '/' -and ($i + 1) -lt $Inner.Length -and $Inner[$i + 1] -eq '/') {
                    $nl = $Inner.IndexOf("`n", $i)
                    $i = $(if ($nl -lt 0) { $Inner.Length } else { $nl })
                    continue
                }
                elseif ($ch -eq '/' -and ($i + 1) -lt $Inner.Length -and $Inner[$i + 1] -eq '*') {
                    $endComment = $Inner.IndexOf('*/', $i + 2)
                    if ($endComment -lt 0) { throw 'Malformed autoApprove block: unterminated block comment.' }
                    $i = $endComment + 2
                    continue
                }
                elseif ($ch -eq '"') { $inString = $true }
                elseif ($ch -in @('{', '[')) { $stack.Push($ch) }
                elseif ($ch -in @('}', ']')) {
                    $open = $stack.Pop()
                    if (($open -eq '{' -and $ch -ne '}') -or ($open -eq '[' -and $ch -ne ']')) {
                        throw "Malformed autoApprove block: mismatched value delimiters for '$key'."
                    }
                }
                $i++
            }
            if ($stack.Count -gt 0) { throw "Malformed autoApprove block: unterminated object value for '$key'." }
        }
        else {
            while ($i -lt $Inner.Length -and $Inner[$i] -notin @(',', "`r", "`n")) { $i++ }
        }

        $valueEnd = $i
        while ($i -lt $Inner.Length -and $Inner[$i] -in @(' ', "`t")) { $i++ }
        $hasComma = $i -lt $Inner.Length -and $Inner[$i] -eq ','
        if ($hasComma) { $i++ }
        if ($i -lt $Inner.Length -and $Inner[$i] -eq "`r") { $i++ }
        if ($i -lt $Inner.Length -and $Inner[$i] -eq "`n") { $i++ }
        $entries.Add([pscustomobject]@{
                Key = $key
                Start = $entryStart
                End = $i
                ValueEnd = $valueEnd
                HasComma = $hasComma
            })
    }
    return $entries.ToArray()
}

function Get-InnerKeys {
    [CmdletBinding()]
    param([string]$Inner)

    return @(Get-ApprovalEntrySpan -Inner $Inner | ForEach-Object { $_.Key })
}

function Set-ApprovalKeys {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Text,
        [object[]]$Add = @(),
        [string[]]$RemoveKeys = @()
    )

    $newline = Get-SettingsNewline -Text $Text
    $span = Find-AutoApproveInnerSpan -Text $Text

    if ($null -eq $span) {
        # Nothing to remove when there is no block; never fabricate an empty one.
        if ($Add.Count -eq 0) {
            return $Text
        }
        # No autoApprove block yet: create one at the top of the root object.
        $rootBrace = $Text.IndexOf('{')
        if ($rootBrace -lt 0) {
            $Text = "{$newline}"
            $rootBrace = 0
        }
        $entries = ($Add | Sort-Object Key -Unique | ForEach-Object { "    `"$($_.Key)`": $($_.Value)," }) -join $newline
        $block = "$newline  `"$($script:SettingKey)`": {$newline$entries$newline  },"
        return $Text.Insert($rootBrace + 1, $block)
    }

    $inner = $Text.Substring($span.InnerStart, $span.InnerEnd - $span.InnerStart)
    $existing = Get-InnerKeys -Inner $inner

    $removeSpans = @(Get-ApprovalEntrySpan -Inner $inner |
            Where-Object { $RemoveKeys -contains $_.Key } |
            Sort-Object Start -Descending)
    foreach ($removeSpan in $removeSpans) {
        $inner = $inner.Remove($removeSpan.Start, $removeSpan.End - $removeSpan.Start)
    }

    $additions = @($Add | Sort-Object Key -Unique | Where-Object { $existing -notcontains $_.Key })
    if ($additions.Count -gt 0) {
        $remaining = @(Get-ApprovalEntrySpan -Inner $inner | Sort-Object Start)
        if ($remaining.Count -gt 0 -and -not $remaining[-1].HasComma) {
            $inner = $inner.Insert($remaining[-1].ValueEnd, ',')
        }
    }

    # Rebuild inner line-by-line: keep comments and drop blank lines so re-runs stay byte-idempotent.
    $lines = $inner -split "`n"
    $kept = [System.Collections.Generic.List[string]]::new()
    foreach ($line in $lines) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }
        $kept.Add($line) | Out-Null
    }

    # Normalize commas on entry lines so a trailing comma (JSONC-valid) is always safe.
    for ($i = 0; $i -lt $kept.Count; $i++) {
        $entryMatch = [regex]::Match($kept[$i], '^(?<body>\s*"(?:[^"\\]|\\.)*"\s*:\s*(?:true|false|\{[^{}\r\n]*\}))\s*,?\s*$')
        if ($entryMatch.Success) {
            $kept[$i] = "$($entryMatch.Groups['body'].Value),"
        }
    }

    $indent = '    '
    foreach ($entry in $additions) {
        $kept.Add("$indent`"$($entry.Key)`": $($entry.Value),") | Out-Null
    }

    # Reassemble, trimming leading/trailing blank lines inside the block.
    $body = ($kept -join "`n").Trim("`r", "`n")
    if ([string]::IsNullOrWhiteSpace($body)) {
        $rebuilt = ''
    }
    else {
        $rebuilt = "`n$body`n  "
    }
    if ($newline -eq "`r`n") {
        $rebuilt = $rebuilt -replace "`r?`n", "`r`n"
    }

    return $Text.Substring(0, $span.InnerStart) + $rebuilt + $Text.Substring($span.InnerEnd)
}

# --- Main ------------------------------------------------------------------

$repoRootPath = Resolve-RepoRoot -StartPath $RepoRoot
$registryFile = Resolve-RegistryPath -RepoRoot $repoRootPath -RegistryPath $RegistryPath
$registry = Read-JsonFile -Path $registryFile

$settingsFile = if (-not [string]::IsNullOrWhiteSpace($SettingsPath)) {
    [System.IO.Path]::GetFullPath($SettingsPath)
}
else {
    Join-Path $repoRootPath '.vscode/settings.json'
}

$targetPlugins = Get-TargetPlugin -Registry $registry -AllPlugins:$All -PluginName $Name

$approvalEntries = @()
foreach ($plugin in $targetPlugins) {
    $approvalEntries += Get-PluginApprovalEntry -Plugin $plugin -RepoRootPath $repoRootPath
}
$approvalEntries = @($approvalEntries | Sort-Object Key -Unique)
$approvalKeys = @($approvalEntries | ForEach-Object { $_.Key })
$legacyArtifactKeys = @(
    foreach ($plugin in $targetPlugins) {
        foreach ($file in @($plugin.files)) {
            $dest = ([string]$file.dest) -replace '\\', '/'
            if ([System.IO.Path]::GetFileName($dest) -cne $script:PlanArtifactConsumerName) {
                continue
            }
            ".github/$dest"
            '.github/' + $dest.Substring(
                0,
                $dest.Length - $script:PlanArtifactConsumerName.Length
            ) + $script:PlanArtifactResolverName
        }
    }
)
$legacyArtifactKeys = @($legacyArtifactKeys | Sort-Object -Unique)

if (Test-Path -LiteralPath $settingsFile -PathType Leaf) {
    $settingsText = [System.IO.File]::ReadAllText($settingsFile)
}
else {
    $settingsText = "{`n}"
    [void](New-Item -ItemType Directory -Path (Split-Path -Parent $settingsFile) -Force)
}

$before = if ($null -ne (Find-AutoApproveInnerSpan -Text $settingsText)) {
    $span = Find-AutoApproveInnerSpan -Text $settingsText
    Get-InnerKeys -Inner $settingsText.Substring($span.InnerStart, $span.InnerEnd - $span.InnerStart)
}
else {
    @()
}

$ownedArtifactApprovalKeys = @($legacyArtifactKeys | Where-Object { $before -contains $_ })
foreach ($legacyKey in $legacyArtifactKeys) {
    if ($legacyKey -notmatch '^\.github/skills/(?<skill>[a-z0-9-]+)/scripts/Get-PlanArtifactConsumerContext') {
        continue
    }
    $ownedFragment = "skills/$($Matches.skill)/scripts/Get-PlanArtifactConsumerContext"
    $ownedArtifactApprovalKeys += @($before | Where-Object {
            $_.Replace('\', '').Contains($ownedFragment)
        })
}
$ownedArtifactApprovalKeys = @($ownedArtifactApprovalKeys | Sort-Object -Unique)

$obsoleteApprovalKeys = @()
if (-not $Remove) {
    $ownedWriterSkills = @(
        foreach ($plugin in $targetPlugins) {
            switch ([string]$plugin.name) {
                'code-review' { 'cr' }
                'design-review' { 'dr' }
            }
        }
    )
    foreach ($skill in $ownedWriterSkills) {
        $ownedFragment = "skills\\/$skill\\/scripts\\/Build-ReviewReport"
        $obsoleteApprovalKeys += @($before | Where-Object {
                $_ -match [regex]::Escape($ownedFragment) -and $approvalKeys -cnotcontains $_
            })
    }
    $obsoleteApprovalKeys += $ownedArtifactApprovalKeys
    $obsoleteApprovalKeys = @($obsoleteApprovalKeys | Sort-Object -Unique)
}

if ($Remove) {
    $removeKeys = @($approvalKeys + $legacyArtifactKeys + $ownedArtifactApprovalKeys | Sort-Object -Unique)
    $updated = Set-ApprovalKeys -Text $settingsText -RemoveKeys $removeKeys
    $changed = @($removeKeys | Where-Object { $before -contains $_ })
    $verb = 'Removed'
}
else {
    $updated = Set-ApprovalKeys -Text $settingsText -Add $approvalEntries -RemoveKeys $obsoleteApprovalKeys
    $changed = @($approvalKeys | Where-Object { $before -notcontains $_ })
    $verb = 'Added'
}

if ($updated -eq $settingsText) {
    Write-Host "No changes to '$settingsFile' (already up to date)."
    return
}

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($settingsFile, $updated, $utf8NoBom)

Write-Host "Updated '$settingsFile'."
if ($changed.Count -gt 0) {
    Write-Host "$verb $($changed.Count) auto-approve key(s):"
    foreach ($key in ($changed | Sort-Object)) {
        Write-Host "  $key"
    }
}
else {
    Write-Host 'No key changes (settings already reflected the request).'
}
