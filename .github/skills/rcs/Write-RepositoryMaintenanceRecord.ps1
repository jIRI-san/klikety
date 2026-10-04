#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet('Read', 'Publish', 'RecordDisposition')]
    [string]$Action = 'Read',
    [string]$RepoRoot = (Get-Location).Path,
    [AllowEmptyString()][string]$PayloadJson = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'scripts/PlanState.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'scripts/SecretGuard.psm1') -Force

$script:RelativePath = 'docs/repository-maintenance.md'
$script:Limit = 128KB
$script:Utf8 = [System.Text.UTF8Encoding]::new($false, $true)
$script:Root = [System.IO.Path]::GetFullPath($RepoRoot)
$script:Target = Join-Path $script:Root (
    $script:RelativePath -replace '/', [System.IO.Path]::DirectorySeparatorChar
)
$script:Compare = if ($IsWindows) {
    [System.StringComparison]::OrdinalIgnoreCase
}
else {
    [System.StringComparison]::Ordinal
}
$script:Sections = @('Source and scope', 'Survey', 'Coverage', 'Findings', 'Operator decisions')

function Get-Value {
    param([Parameter(Mandatory)][object]$Object, [Parameter(Mandatory)][string]$Name)
    if ($Object -is [System.Collections.IDictionary]) {
        if ($Object.Contains($Name)) { return $Object[$Name] }
    }
    elseif ($null -ne $Object.PSObject.Properties[$Name]) {
        return $Object.PSObject.Properties[$Name].Value
    }
    throw "Payload is missing '$Name'."
}

function Get-OptionalValue {
    param([Parameter(Mandatory)][object]$Object, [Parameter(Mandatory)][string]$Name, [object]$Default = $null)
    if ($Object -is [System.Collections.IDictionary]) {
        if ($Object.Contains($Name)) { return $Object[$Name] }
    }
    elseif ($null -ne $Object.PSObject.Properties[$Name]) {
        return $Object.PSObject.Properties[$Name].Value
    }
    return $Default
}

function Get-LineValue {
    param([Parameter(Mandatory)][object]$Value, [Parameter(Mandatory)][string]$Name)
    if ($Value -isnot [string] -or [string]::IsNullOrWhiteSpace($Value) -or
        $Value -match '[\r\n\x00-\x1f`]') {
        throw "'$Name' must be a non-empty single line without controls or backticks."
    }
    return $Value.Trim()
}

function Get-StringArray {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object]$Value,
        [Parameter(Mandatory)][string]$Name,
        [int]$Maximum = 100,
        [switch]$AllowEmpty
    )
    $items = @($Value)
    if ($items.Count -gt $Maximum -or (-not $AllowEmpty -and $items.Count -eq 0)) {
        throw "'$Name' has an invalid item count (maximum $Maximum)."
    }
    $result = [System.Collections.Generic.List[string]]::new()
    foreach ($item in $items) {
        $result.Add((Get-LineValue -Value $item -Name $Name))
    }
    return $result.ToArray()
}

function Get-CitationArray {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object]$Value,
        [Parameter(Mandatory)][string]$Name,
        [int]$Maximum = 20
    )
    $citations = @(Get-StringArray -Value $Value -Name $Name -Maximum $Maximum)
    foreach ($citation in $citations) {
        if ($citation -notmatch '^[A-Za-z0-9_.-][A-Za-z0-9_./-]*(?::\d+(?:-\d+)?|#[A-Za-z0-9_.:-]+)$' -or
            @($citation.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count) {
            throw "Citation '$citation' is not a repo-relative path and line/symbol."
        }
    }
    return $citations
}

function Assert-Path {
    param([Parameter(Mandatory)][string]$Path, [switch]$AllowMissing)

    if (-not (Test-Path -LiteralPath $script:Root -PathType Container)) {
        throw "Repository root '$($script:Root)' is not an existing directory."
    }
    $rootItem = Get-Item -LiteralPath $script:Root -Force
    if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        ($rootItem.PSObject.Properties.Name -contains 'LinkType' -and $rootItem.LinkType)) {
        throw 'Maintenance record refuses a linked repository root.'
    }

    $docs = Join-Path $script:Root 'docs'
    foreach ($part in @($docs, $Path)) {
        $item = Get-Item -LiteralPath $part -Force -ErrorAction SilentlyContinue
        if ($null -eq $item) {
            if ($AllowMissing) { continue }
            throw "Maintenance record component '$part' does not exist."
        }
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
            ($item.PSObject.Properties.Name -contains 'LinkType' -and $item.LinkType)) {
            throw "Maintenance record refuses link or reparse point '$part'."
        }
    }
    if ((Test-Path -LiteralPath $docs -PathType Leaf)) {
        throw "Maintenance record parent '$docs' is not a directory."
    }
    if ((Test-Path -LiteralPath $Path) -and -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Maintenance record target '$Path' is not a file."
    }

    $physicalRoot = Resolve-PhysicalRepoPath -Path $script:Root
    $physicalPath = Resolve-PhysicalRepoPath -Path $Path
    $prefix = $physicalRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $physicalPath.StartsWith($prefix, $script:Compare)) {
        throw "Maintenance record path '$Path' escapes the physical repository root."
    }
}

function Get-SectionHeaders {
    param([Parameter(Mandatory)][string]$Text)
    $headers = [System.Collections.Generic.List[object]]::new()
    foreach ($match in [regex]::Matches($Text, '(?m)^## (?<name>[^\r\n]+)\r?$')) {
        $headers.Add([pscustomobject]@{
                Name = $match.Groups['name'].Value
                Start = $match.Index
                BodyStart = $match.Index + $match.Length
            })
    }
    return $headers.ToArray()
}

function Get-SectionBody {
    param([Parameter(Mandatory)][string]$Text, [Parameter(Mandatory)][string]$Name)
    $headers = @(Get-SectionHeaders -Text $Text)
    $found = @($headers | Where-Object Name -CEQ $Name)
    if ($found.Count -ne 1) { throw "Maintenance record needs exactly one '## $Name' section." }
    $next = @($headers | Where-Object Start -GT $found[0].Start | Sort-Object Start | Select-Object -First 1)
    $end = if ($next.Count) { $next[0].Start } else { $Text.Length }
    return $Text.Substring($found[0].BodyStart, $end - $found[0].BodyStart)
}

function Assert-Structure {
    param([Parameter(Mandatory)][string]$Text)
    if (-not $Text.StartsWith('# Repository maintenance', [StringComparison]::Ordinal)) {
        throw 'Maintenance record has an unsupported title.'
    }
    $headers = @(Get-SectionHeaders -Text $Text)
    if ($headers.Count -ne $script:Sections.Count) { throw 'Maintenance record has malformed sections.' }
    for ($i = 0; $i -lt $script:Sections.Count; $i++) {
        if ($headers[$i].Name -cne $script:Sections[$i]) {
            throw 'Maintenance record sections are malformed or out of order.'
        }
    }
    foreach ($name in @('source', 'survey', 'coverage')) {
        $sectionName = switch ($name) {
            source { 'Source and scope' }
            survey { 'Survey' }
            coverage { 'Coverage' }
        }
        $body = Get-SectionBody -Text $Text -Name $sectionName
        $start = "<!-- rcs:$name`:start -->"
        $end = "<!-- rcs:$name`:end -->"
        if ([regex]::Matches($body, [regex]::Escape($start)).Count -ne 1 -or
            [regex]::Matches($body, [regex]::Escape($end)).Count -ne 1 -or
            $body.IndexOf($start, [StringComparison]::Ordinal) -ge $body.IndexOf($end, [StringComparison]::Ordinal)) {
            throw "Maintenance record has malformed managed '$name' content."
        }
    }
    $findings = Get-SectionBody -Text $Text -Name 'Findings'
    $ids = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($match in [regex]::Matches($findings, '(?m)^<!-- rcs-finding: (?<id>RCS-[A-Za-z0-9-]{1,48}) -->$')) {
        if (-not $ids.Add($match.Groups['id'].Value)) {
            throw "Maintenance record contains duplicate finding ID '$($match.Groups['id'].Value)'."
        }
    }
    [void](Get-SectionBody -Text $Text -Name 'Operator decisions')
}

function Read-Record {
    Assert-Path -Path $script:Target -AllowMissing
    if (-not (Test-Path -LiteralPath $script:Target -PathType Leaf)) { return $null }
    $bytes = [IO.File]::ReadAllBytes($script:Target)
    if ($bytes.Length -gt $script:Limit) { throw 'Maintenance record exceeds the 128-KiB limit.' }
    try { $text = $script:Utf8.GetString($bytes) }
    catch { throw 'Maintenance record is invalid UTF-8; no content was changed.' }
    if (@(Find-HighConfidenceSecret -Value $text).Count) {
        throw 'Maintenance record contains a high-confidence secret; no content was changed.'
    }
    Assert-Structure -Text $text
    return $text
}

function New-Record {
    return @'
# Repository maintenance

This is an advisory record. Current source and current gates remain authoritative.

## Source and scope

<!-- rcs:source:start -->
No source snapshot published.
<!-- rcs:source:end -->

## Survey

<!-- rcs:survey:start -->
No survey published.
<!-- rcs:survey:end -->

## Coverage

<!-- rcs:coverage:start -->
No coverage published.
<!-- rcs:coverage:end -->

## Findings

<!-- Findings are append/preserve only; unseen entries are never resolved or removed. -->

## Operator decisions

No operator decisions recorded.
'@
}

function Set-ManagedSection {
    param(
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][string]$Section,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Value,
        [Parameter(Mandatory)][string]$NewLine
    )
    $body = Get-SectionBody -Text $Text -Name $Section
    $pattern = "(?s)(<!-- rcs:$Name`:start -->)$NewLine.*?(<!-- rcs:$Name`:end -->)"
    if ([regex]::Matches($body, $pattern).Count -ne 1) {
        throw "Maintenance record could not update '$Name'."
    }
    $newBody = [regex]::Replace($body, $pattern, "`$1$NewLine$Value$NewLine`$2", 1)
    $headers = @(Get-SectionHeaders -Text $Text)
    $sectionHeader = @($headers | Where-Object Name -CEQ $Section)[0]
    $next = @($headers | Where-Object Start -GT $sectionHeader.Start | Sort-Object Start | Select-Object -First 1)
    $end = if ($next.Count) { $next[0].Start } else { $Text.Length }
    return $Text.Substring(0, $sectionHeader.BodyStart) + $newBody + $Text.Substring($end)
}

function Get-FindingBlocks {
    param([Parameter(Mandatory)][string]$Text)
    $body = Get-SectionBody -Text $Text -Name 'Findings'
    $result = [System.Collections.Generic.List[object]]::new()
    foreach ($match in [regex]::Matches(
            $body,
            '(?ms)^<!-- rcs-finding: (?<id>RCS-[A-Za-z0-9-]{1,48}) -->\r?\n(?<body>.*?)(?=^<!-- rcs-finding:|\z)'
        )) {
        $result.Add([pscustomobject]@{
                Id = $match.Groups['id'].Value
                Block = $match.Value.TrimEnd("`r", "`n")
            })
    }
    return $result.ToArray()
}

function Get-FindingIdentity {
    param([Parameter(Mandatory)][string]$Block)
    $identity = @{}
    foreach ($field in @('Subject', 'Scope', 'Citations')) {
        $match = [regex]::Match($Block, "(?m)^\*\*$field`:\*\* (?<value>[^\r\n]*)$")
        if (-not $match.Success) { throw "Existing finding is malformed; missing '$field'." }
        $identity[$field] = $match.Groups['value'].Value
    }
    return $identity
}

function New-FindingBlock {
    param([Parameter(Mandatory)][object]$Finding, [Parameter(Mandatory)][string]$NewLine)
    $id = Get-LineValue -Value (Get-Value $Finding 'Id') -Name 'finding ID'
    if ($id -cnotmatch '^RCS-[A-Za-z0-9][A-Za-z0-9-]{0,47}$') { throw "Invalid finding ID '$id'." }
    $category = Get-LineValue -Value (Get-Value $Finding 'Category') -Name 'Category'
    if ($category -cnotin @('drift', 'alignment-question', 'unfinished-work',
            'design-architecture', 'coding-standard', 'dead-code', 'coverage-gap')) {
        throw "Finding '$id' has unsupported category '$category'."
    }
    $subject = Get-LineValue -Value (Get-Value $Finding 'Subject') -Name 'Subject'
    $scope = Get-LineValue -Value (Get-Value $Finding 'Scope') -Name 'Scope'
    $citations = @(Get-CitationArray -Value (Get-Value $Finding 'Citations') -Name 'Citations')
    $fields = [ordered]@{
        'Expectation or rationale' = Get-Value $Finding 'Expectation'
        'Current behavior / reachability' = Get-Value $Finding 'CurrentBehavior'
        'Impact' = Get-Value $Finding 'Impact'
        'Exceptions / counter-evidence' = Get-Value $Finding 'CounterEvidence'
        'Uncertainty and coverage limits' = Get-Value $Finding 'Uncertainty'
        'Recommended action' = Get-Value $Finding 'Action'
        'Benefits' = Get-Value $Finding 'Benefits'
        'Tradeoffs' = Get-Value $Finding 'Tradeoffs'
    }
    $effort = [int](Get-Value $Finding 'Effort')
    $complexity = [int](Get-Value $Finding 'Complexity')
    if ($effort -notin 1..10 -or $complexity -notin 1..10) {
        throw "Finding '$id' effort and complexity must be 1 through 10."
    }
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("### $id - $subject")
    $lines.Add("**Category:** $category")
    $lines.Add("**Subject:** $subject")
    $lines.Add("**Scope:** $scope")
    $lines.Add("**Citations:** $($citations -join '; ')")
    foreach ($field in $fields.GetEnumerator()) {
        $value = Get-LineValue -Value $field.Value -Name "$($field.Key) for $id"
        $lines.Add("**$($field.Key):** $value")
    }
    $lines.Add("**Effort:** $effort/10")
    $lines.Add("**Complexity:** $complexity/10")
    return "<!-- rcs-finding: $id -->$NewLine$($lines -join $NewLine)"
}

function Get-FindingNotes {
    param([Parameter(Mandatory)][string]$Block)

    $managedLine = [regex]::new(
        '^(?:<!-- rcs-finding: RCS-[A-Za-z0-9-]{1,48} -->|### RCS-[A-Za-z0-9-]{1,48} - .+|\*\*(?:Category|Subject|Scope|Citations|Expectation or rationale|Current behavior / reachability|Impact|Exceptions / counter-evidence|Uncertainty and coverage limits|Recommended action|Benefits|Tradeoffs|Effort|Complexity):\*\* .*)$'
    )
    $notes = [System.Collections.Generic.List[string]]::new()
    foreach ($line in ($Block -split "`r?`n")) {
        if (-not $managedLine.IsMatch($line)) { $notes.Add($line) }
    }
    while ($notes.Count -and [string]::IsNullOrWhiteSpace($notes[0])) { $notes.RemoveAt(0) }
    while ($notes.Count -and [string]::IsNullOrWhiteSpace($notes[$notes.Count - 1])) {
        $notes.RemoveAt($notes.Count - 1)
    }
    return $notes.ToArray()
}

function Add-Findings {
    param([Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Findings,
        [Parameter(Mandatory)][string]$NewLine)
    $blocks = @(Get-FindingBlocks -Text $Text)
    $byId = [System.Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($block in $blocks) {
        $byId.Add($block.Id, $block)
        [void](Get-FindingIdentity -Block $block.Block)
    }
    $updated = $Text
    $additions = [System.Collections.Generic.List[string]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($finding in $Findings) {
        $candidate = New-FindingBlock -Finding $finding -NewLine $NewLine
        $id = [regex]::Match($candidate, '(?m)^<!-- rcs-finding: (?<id>[^ ]+) -->').Groups['id'].Value
        if (-not $seen.Add($id)) { throw "Publication contains duplicate finding ID '$id'." }
        if (-not $byId.ContainsKey($id)) {
            $additions.Add($candidate)
            continue
        }
        $old = $byId[$id].Block
        $oldIdentity = Get-FindingIdentity -Block $old
        $newIdentity = Get-FindingIdentity -Block $candidate
        foreach ($field in @('Subject', 'Scope', 'Citations')) {
            if (-not [string]::Equals($oldIdentity[$field], $newIdentity[$field], [StringComparison]::Ordinal)) {
                throw "Finding ID '$id' has ambiguous identity: '$field' changed. Ask the operator whether to reuse it or choose a new ID."
            }
        }
        $notes = @(Get-FindingNotes -Block $old)
        $replacement = if ($notes.Count) {
            $candidate + "$NewLine$NewLine" + ($notes -join $NewLine)
        }
        else {
            $candidate
        }
        if ($old -cne $replacement.TrimEnd("`r", "`n")) {
            $at = $updated.IndexOf($old, [StringComparison]::Ordinal)
            if ($at -lt 0) { throw "Existing finding '$id' could not be safely updated." }
            $updated = $updated.Remove($at, $old.Length).Insert($at, $replacement)
        }
    }
    if (-not $additions.Count) { return $updated }
    $headers = @(Get-SectionHeaders -Text $updated)
    $header = @($headers | Where-Object Name -CEQ 'Findings')[0]
    $next = @($headers | Where-Object Start -GT $header.Start | Sort-Object Start | Select-Object -First 1)[0]
    $end = if ($null -eq $next) { $updated.Length } else { $next.Start }
    $body = $updated.Substring($header.BodyStart, $end - $header.BodyStart).TrimEnd("`r", "`n")
    $newBody = $body + "$NewLine$NewLine" + ($additions -join "$NewLine$NewLine") + $NewLine
    return $updated.Substring(0, $header.BodyStart) + $newBody + $updated.Substring($end)
}

function Assert-VerifiedHandoff {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][ValidateSet('corrective-plan', 'archived')][string]$Disposition
    )

    $planMatch = [regex]::Match(
        $Path,
        '^docs/implementation-plans/(?<archive>archived/)?(?<folder>[^/]+)/plan\.md$'
    )
    $epicMatch = [regex]::Match(
        $Path,
        '^docs/implementation-plans/archived/epics/(?<folder>[^/]+)/epic\.md$'
    )
    if (-not $planMatch.Success -and -not $epicMatch.Success) {
        throw 'Successful handoffs must reference a current plan.md or archived epic.md under docs/implementation-plans.'
    }
    if ($epicMatch.Success -and $Disposition -ne 'archived') {
        throw 'Corrective-plan handoffs must reference an active plan.md.'
    }

    $segments = $Path.Split('/')
    $current = $script:Root
    for ($index = 0; $index -lt $segments.Count; $index++) {
        $current = Join-Path $current $segments[$index]
        $item = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
        if ($null -eq $item) {
            throw "Handoff path '$Path' does not resolve to the required current repository state."
        }
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
            ($item.PSObject.Properties.Name -contains 'LinkType' -and $item.LinkType)) {
            throw "Handoff path '$Path' resolves through a link or reparse point."
        }
        $isFile = $index -eq ($segments.Count - 1)
        if (($isFile -and $item.PSIsContainer) -or
            (-not $isFile -and -not $item.PSIsContainer)) {
            throw "Handoff path '$Path' contains an invalid path component."
        }
    }

    $fullPath = [System.IO.Path]::GetFullPath($current)
    $physicalRoot = Resolve-PhysicalRepoPath -Path $script:Root
    $physicalPath = Resolve-PhysicalRepoPath -Path $fullPath
    $prefix = $physicalRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $physicalPath.StartsWith($prefix, $script:Compare)) {
        throw "Handoff path '$Path' escapes the physical repository root."
    }

    $expectedArchived = $Disposition -eq 'archived'
    if ($planMatch.Success) {
        $folderPath = Split-Path -Parent $fullPath
        $folderMetadata = ConvertFrom-PlanFolderName -FolderName $planMatch.Groups['folder'].Value
        if ($null -eq $folderMetadata) {
            throw "Handoff path '$Path' does not identify a recognized plan folder."
        }
        $matches = @(
            Get-PlanInventory -RepoRoot $script:Root `
                -CanonicalIdFilter @([string]$folderMetadata.FolderId) |
                Where-Object {
                    [string]::Equals(
                        [System.IO.Path]::GetFullPath([string]$_.Path),
                        $folderPath,
                        $script:Compare
                    ) -and [bool]$_.IsArchived -eq $expectedArchived
                }
        )
        if ($matches.Count -ne 1) {
            $state = if ($expectedArchived) { 'archived' } else { 'active' }
            throw "Handoff path '$Path' does not resolve to a current $state plan."
        }
        if ($expectedArchived -ne $planMatch.Groups['archive'].Success) {
            throw "Handoff path '$Path' does not match the requested archive state."
        }
        if ($Disposition -eq 'corrective-plan') {
            $context = Get-PlanningContextState -PlanDir $folderPath -RepoRoot $script:Root
            if (-not $context.IsEnrolled -or -not $context.IsConfirmed -or
                $context.Status -cne 'confirmed') {
                throw "Handoff path '$Path' does not resolve to a confirmed active plan."
            }
        }
        return
    }

    $epicFolder = $epicMatch.Groups['folder'].Value
    $epicInventory = @(
        Get-EpicInventory -RepoRoot $script:Root |
            Where-Object {
                [string]::Equals(
                    [System.IO.Path]::GetFullPath([string]$_.Path),
                    (Split-Path -Parent $fullPath),
                    $script:Compare
                ) -and [bool]$_.IsArchived
            }
    )
    if ($epicInventory.Count -ne 1 -or
        -not [string]::Equals($epicInventory[0].FolderName, $epicFolder, [StringComparison]::Ordinal)) {
        throw "Handoff path '$Path' does not resolve to a current archived epic."
    }
}

function Add-Decision {
    param([Parameter(Mandatory)][string]$Text, [Parameter(Mandatory)][object]$Decision,
        [Parameter(Mandatory)][string]$NewLine)
    $id = Get-LineValue -Value (Get-Value $Decision 'FindingId') -Name 'FindingId'
    if ($id -cnotmatch '^RCS-[A-Za-z0-9][A-Za-z0-9-]{0,47}$' -or
        @((Get-FindingBlocks -Text $Text) | Where-Object Id -CEQ $id).Count -ne 1) {
        throw "Decision refers to unknown or ambiguous finding '$id'."
    }
    $disposition = Get-LineValue -Value (Get-Value $Decision 'Disposition') -Name 'Disposition'
    if ($disposition -cnotin @('accepted-intentional-drift', 'wont-fix', 'corrective-plan', 'archived')) {
        throw "Unsupported disposition '$disposition'."
    }
    $date = Get-LineValue -Value (Get-Value $Decision 'DecidedOn') -Name 'DecidedOn'
    $parsed = [datetime]::MinValue
    if (-not [datetime]::TryParseExact($date, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::None, [ref]$parsed)) {
        throw "Decision date '$date' must be a valid ISO date."
    }
    $rationale = Get-LineValue -Value (Get-Value $Decision 'Rationale') -Name 'Rationale'
    $scope = Get-LineValue -Value (Get-Value $Decision 'Scope') -Name 'Scope'
    $assumptions = Get-LineValue -Value (Get-Value $Decision 'Assumptions') -Name 'Assumptions'
    $revisit = Get-LineValue -Value (Get-Value $Decision 'RevisitWhen') -Name 'RevisitWhen'
    $citations = @(Get-CitationArray -Value (Get-Value $Decision 'Citations') -Name 'Decision citations')
    $outcomeValue = Get-OptionalValue -Object $Decision -Name 'ActionResult'
    $outcome = if ($null -eq $outcomeValue -or
        ($outcomeValue -is [string] -and [string]::IsNullOrWhiteSpace($outcomeValue))) {
        ''
    }
    else {
        Get-LineValue -Value $outcomeValue -Name 'ActionResult'
    }
    if ($disposition -eq 'corrective-plan') {
        if ($outcome -notmatch '^(created|updated|reused): (?<path>.+)$') {
            throw 'A corrective-plan disposition requires a successfully completed plan handoff.'
        }
        Assert-VerifiedHandoff -Path $Matches.path -Disposition $disposition
    }
    if ($disposition -eq 'archived') {
        if ($outcome -notmatch '^archived: (?<path>.+)$') {
            throw 'An archived disposition requires a verified successful archive result.'
        }
        Assert-VerifiedHandoff -Path $Matches.path -Disposition $disposition
    }
    $body = @(
        "**Finding:** $id"
        "**Disposition:** $disposition"
        "**Date:** $date"
        "**Rationale:** $rationale"
        "**Affected scope:** $scope"
        "**Assumptions:** $assumptions"
        "**Revisit when:** $revisit"
        "**Evidence:** $($citations -join '; ')"
    )
    if ($outcome) { $body += "**Successful handoff:** $outcome" }
    $sectionBody = Get-SectionBody -Text $Text -Name 'Operator decisions'
    $normalized = ($body -join $NewLine)
    foreach ($existing in [regex]::Matches($sectionBody,
            '(?ms)^<!-- rcs-decision: RCS-D-\d{4} -->\r?\n(?<body>.*?)(?=^<!-- rcs-decision:|\z)')) {
        $expected = "### $id - $date - $disposition$NewLine$normalized"
        if ($existing.Groups['body'].Value.TrimEnd("`r", "`n") -ceq $expected) { return $Text }
    }
    $number = 1
    foreach ($match in [regex]::Matches($sectionBody, '(?m)^<!-- rcs-decision: RCS-D-(?<n>\d{4}) -->$')) {
        $number = [Math]::Max($number, [int]$match.Groups['n'].Value + 1)
    }
    $block = "<!-- rcs-decision: RCS-D-$('{0:D4}' -f $number) -->$NewLine### $id - $date - $disposition$NewLine$normalized"
    $headers = @(Get-SectionHeaders -Text $Text)
    $section = @($headers | Where-Object Name -CEQ 'Operator decisions')[0]
    $tail = $Text.Substring($section.BodyStart).TrimEnd("`r", "`n")
    return $Text.Substring(0, $section.BodyStart) + $tail + "$NewLine$NewLine$block$NewLine"
}

function Write-Content {
    param([Parameter(Mandatory)][string]$Text)
    $bytes = $script:Utf8.GetBytes($Text)
    if ($bytes.Length -gt $script:Limit) { throw 'Maintenance record would exceed the 128-KiB limit; no content was changed.' }
    if (@(Find-HighConfidenceSecret -Value $Text).Count) {
        throw 'Maintenance record publication contains a high-confidence secret; no content was changed.'
    }
    Assert-Path -Path $script:Target -AllowMissing
    $parent = Split-Path -Parent $script:Target
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        [void][IO.Directory]::CreateDirectory($parent)
    }
    Assert-Path -Path $script:Target -AllowMissing
    $temporary = "$($script:Target).$([guid]::NewGuid().ToString('N')).tmp"
    try {
        Assert-Path -Path $temporary -AllowMissing
        [IO.File]::WriteAllBytes($temporary, $bytes)
        $staged = [IO.File]::ReadAllBytes($temporary)
        if ([Convert]::ToBase64String($staged) -cne [Convert]::ToBase64String($bytes)) {
            throw 'Maintenance record staging did not verify; original content was not changed.'
        }
        if (@(Find-HighConfidenceSecret -Value $script:Utf8.GetString($staged)).Count) {
            throw 'Maintenance record staging failed secret screening; original content was not changed.'
        }
        Assert-Path -Path $temporary
        Assert-Path -Path $script:Target -AllowMissing
        [IO.File]::Move($temporary, $script:Target, $true)
    }
    finally {
        if (Test-Path -LiteralPath $temporary -PathType Leaf) {
            Remove-Item -LiteralPath $temporary -Force
        }
    }
}

if ($Action -eq 'Read') {
    if ($PayloadJson) { throw 'Read does not accept a payload.' }
    $record = Read-Record
    [pscustomobject][ordered]@{
        status = if ($null -eq $record) { 'missing' } else { 'present' }
        path = $script:RelativePath
        byteCount = if ($null -eq $record) { 0 } else { $script:Utf8.GetByteCount($record) }
        content = $record
    } | ConvertTo-Json -Depth 5
    return
}

if (-not $PayloadJson) { throw "$Action requires PayloadJson." }
if ($script:Utf8.GetByteCount($PayloadJson) -gt $script:Limit) {
    throw 'Maintenance payload exceeds the 128-KiB input limit.'
}
if (@(Find-HighConfidenceSecret -Value $PayloadJson).Count) {
    throw 'Maintenance payload contains a high-confidence secret; no content was changed.'
}
try { $payload = ConvertFrom-Json -InputObject $PayloadJson -AsHashtable -Depth 20 }
catch { throw "Maintenance payload is not valid JSON: $($_.Exception.Message)" }

$text = Read-Record
$nl = if ($null -ne $text -and $text.Contains("`r`n")) { "`r`n" } else { "`n" }
if ($null -eq $text) { $text = (New-Record).Replace("`r`n", "`n").Replace("`r", "`n") }
if ($Action -eq 'Publish') {
    $source = Get-LineValue -Value (Get-Value $payload 'SourceCommit') -Name 'SourceCommit'
    if ($source -cnotmatch '^(?:[0-9a-f]{40}|[0-9a-f]{64})$') {
        throw 'SourceCommit must be a full lowercase commit ID.'
    }
    $dirtyValue = Get-OptionalValue -Object $payload -Name 'DirtyPaths'
    if ($null -eq $dirtyValue) { $dirtyValue = [object[]]::new(0) }
    $dirty = @(Get-StringArray -Value $dirtyValue -Name 'DirtyPaths' -Maximum 500 -AllowEmpty)
    foreach ($path in $dirty) {
        if ([IO.Path]::IsPathRooted($path) -or
            @($path.Replace('\', '/') -split '/' | Where-Object { $_ -in @('', '.', '..') }).Count) {
            throw "Dirty path '$path' is not repository-relative."
        }
    }
    $survey = Get-Value $payload 'Survey'
    $coverage = Get-Value $payload 'Coverage'
    foreach ($entry in @(@{ Name = 'Survey'; Text = $survey }, @{ Name = 'Coverage'; Text = $coverage })) {
        if ($entry.Text -isnot [string] -or [string]::IsNullOrWhiteSpace($entry.Text) -or
            $entry.Text -match '(?m)^## ' -or $entry.Text.Contains('<!-- rcs:') -or
            $entry.Text -match '[\x00-\x08\x0b\x0c\x0e-\x1f]') {
            throw "$($entry.Name) must be non-empty Markdown without level-two headings or managed markers."
        }
    }
    $sourceText = @(
        "Source commit: $source"
        "Dirty worktree paths: $(if ($dirty.Count) { $dirty -join '; ' } else { 'none' })"
    ) -join $nl
    $text = Set-ManagedSection -Text $text -Section 'Source and scope' -Name source -Value $sourceText -NewLine $nl
    $text = Set-ManagedSection -Text $text -Section Survey -Name survey -Value $survey.Trim() -NewLine $nl
    $text = Set-ManagedSection -Text $text -Section Coverage -Name coverage -Value $coverage.Trim() -NewLine $nl
    $text = Add-Findings -Text $text -Findings @(Get-Value $payload 'Findings') -NewLine $nl
}
else {
    $text = Add-Decision -Text $text -Decision $payload -NewLine $nl
}

Assert-Structure -Text $text
$prior = Read-Record
if ($null -ne $prior -and $prior -ceq $text) {
    [pscustomobject]@{ status = 'unchanged'; path = $script:RelativePath } | ConvertTo-Json -Compress
    return
}
Write-Content -Text $text
[pscustomobject]@{
    status = 'written'
    path = $script:RelativePath
    byteCount = $script:Utf8.GetByteCount($text)
} | ConvertTo-Json -Compress
