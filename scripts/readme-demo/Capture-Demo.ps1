[CmdletBinding(DefaultParameterSetName = 'Capture')]
param(
    [string]$OutputDirectory = (Join-Path ([IO.Path]::GetTempPath()) ("KliketyDemo-" + [Guid]::NewGuid().ToString('N'))),
    [Parameter(Mandatory = $true, ParameterSetName = 'Existing')][string]$CaptureDirectory,
    [string]$BrowserPath,
    [switch]$NoPublish
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path $OutputDirectory) { throw 'Choose an unused output directory; previous evidence is never overwritten.' }
if ($OutputDirectory.StartsWith($repository + '\', [StringComparison]::OrdinalIgnoreCase) -or $OutputDirectory -eq $repository) {
    throw 'Capture/build evidence must live outside the repository.'
}
foreach ($name in @('node', 'git', 'dotnet')) { [void](Get-Command $name -ErrorAction Stop) }
if ($PSCmdlet.ParameterSetName -eq 'Capture') { [void](Get-Command wsb.exe -ErrorAction Stop) }
$verifier = Join-Path $PSScriptRoot 'Verify-Demo.mjs'
$preflight = node $verifier --preflight $BrowserPath
if ($LASTEXITCODE -ne 0) { throw 'Demo preflight failed; no captures or gallery files were changed.' }
$tools = $preflight | ConvertFrom-Json
$BrowserPath = $tools.browser
$assets = @($tools.imageNames) + @('capture-info.json')
$gallery = Join-Path $repository 'docs\screenshots'
$baseline = @{}
foreach ($name in $assets) {
    $path = Join-Path $gallery $name
    $baseline[$name] = if (Test-Path $path) { (Get-FileHash $path -Algorithm SHA256).Hash } else { $null }
}

if ($PSCmdlet.ParameterSetName -eq 'Existing') {
    $CaptureDirectory = [IO.Path]::GetFullPath($CaptureDirectory)
    [void](New-Item -ItemType Directory -Path $OutputDirectory)
} else {
    $runner = Join-Path $PSScriptRoot 'Capture-ReadmeDemo.ps1'
    & $runner -OutputDirectory $OutputDirectory
    $CaptureDirectory = Join-Path $OutputDirectory 'captures'
}
$verification = Join-Path $OutputDirectory 'verification'
node $verifier --capture $CaptureDirectory $verification $BrowserPath
if ($LASTEXITCODE -ne 0) { throw "Demo verification failed; gallery unchanged. Evidence: $OutputDirectory" }
if ($NoPublish) {
    Write-Output "Verified captures: $CaptureDirectory"
    Write-Output "Verification: $verification"
    return
}

$manifestPath = Join-Path $verification 'capture-info.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$verifiedHashes = @{}
foreach ($image in $manifest.images) { $verifiedHashes[$image.name] = $image.sha256 }
$verifiedHashes['capture-info.json'] = (Get-FileHash $manifestPath -Algorithm SHA256).Hash
foreach ($name in $assets) {
    $source = if ($name -eq 'capture-info.json') { $manifestPath } else { Join-Path $CaptureDirectory $name }
    if ((Get-FileHash $source -Algorithm SHA256).Hash -ne $verifiedHashes[$name]) {
        throw "Capture changed after verification; publication refused: $name"
    }
    $path = Join-Path $gallery $name
    $current = if (Test-Path $path) { (Get-FileHash $path -Algorithm SHA256).Hash } else { $null }
    if ($current -ne $baseline[$name]) { throw "Gallery changed during capture; publication refused: $name" }
}
$backup = Join-Path $OutputDirectory 'previous-gallery'
[void](New-Item -ItemType Directory -Path $backup)
foreach ($name in $assets) {
    $path = Join-Path $gallery $name
    if (Test-Path $path) { Copy-Item -LiteralPath $path -Destination (Join-Path $backup $name) }
}
[void](New-Item -ItemType Directory -Path $gallery -Force)
foreach ($name in $assets) {
    $source = if ($name -eq 'capture-info.json') { Join-Path $verification $name } else { Join-Path $CaptureDirectory $name }
    Copy-Item -LiteralPath $source -Destination (Join-Path $gallery $name) -Force
    if ((Get-FileHash (Join-Path $gallery $name) -Algorithm SHA256).Hash -ne $verifiedHashes[$name]) {
        throw "Published bytes differ from verified capture: $name"
    }
}
Write-Output "Published gallery: $gallery"
Write-Output "Previous gallery: $backup"
Write-Output "Capture and verification evidence: $OutputDirectory"
