param([Parameter(Mandatory = $true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path $OutputDirectory) { throw 'Choose an unused output directory; existing captures are never overwritten.' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$inputDirectory = Join-Path $OutputDirectory 'input'
$captureDirectory = Join-Path $OutputDirectory 'captures'
[void](New-Item -ItemType Directory -Path $inputDirectory, $captureDirectory)
$sandboxId = $null
try {
    dotnet publish (Join-Path $repository 'src\Klikety\Klikety.csproj') -r win-x64 --self-contained -c Release `
        "-p:BaseOutputPath=$OutputDirectory\build\" -o "$inputDirectory\App" --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Production app publish failed.' }
    dotnet publish (Join-Path $PSScriptRoot 'HintLabelsProbe\HintLabelsProbe.csproj') -r win-x64 --self-contained -c Release `
        "-p:BaseOutputPath=$OutputDirectory\probe-build\" -o "$inputDirectory\Probe" --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Read-only hierarchy probe publish failed.' }
    Copy-Item (Join-Path $PSScriptRoot 'DemoApplication.ps1'), (Join-Path $PSScriptRoot 'CaptureGuest.ps1'), `
        (Join-Path $repository 'src\Klikety\Resources\config.json') -Destination $inputDirectory

    $inputXml = [Security.SecurityElement]::Escape($inputDirectory)
    $outputXml = [Security.SecurityElement]::Escape($captureDirectory)
    $configuration = @"
<Configuration>
  <Networking>Disable</Networking><ClipboardRedirection>Disable</ClipboardRedirection>
  <AudioInput>Disable</AudioInput><VideoInput>Disable</VideoInput>
  <PrinterRedirection>Disable</PrinterRedirection><MemoryInMB>6144</MemoryInMB>
  <MappedFolders>
    <MappedFolder><HostFolder>$inputXml</HostFolder><SandboxFolder>C:\DemoInput</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$outputXml</HostFolder><SandboxFolder>C:\DemoOutput</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>
  </MappedFolders>
  <LogonCommand><Command>powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File C:\DemoInput\CaptureGuest.ps1</Command></LogonCommand>
</Configuration>
"@
    $result = wsb.exe start --config $configuration --raw
    if ($LASTEXITCODE -ne 0) { throw "Sandbox start failed: $result" }
    $sandboxId = [Guid](($result | ConvertFrom-Json).Id)
    wsb.exe connect --id $sandboxId --raw
    if ($LASTEXITCODE -ne 0) { throw 'Sandbox connection failed.' }
    $deadline = [DateTime]::UtcNow.AddMinutes(3)
    while (-not (Test-Path "$captureDirectory\done.txt")) {
        if (Test-Path "$captureDirectory\error.txt") {
            throw (Get-Content "$captureDirectory\error.txt" -Raw)
        }
        if ([DateTime]::UtcNow -gt $deadline) { throw 'Guest capture did not finish within three minutes.' }
        Start-Sleep -Seconds 1
    }
    foreach ($name in @(
        'uniform-grid', 'uniform-grid-zoom', 'crosshair', 'log-crosshair', 'log-grid',
        'element-hints', 'element-hints-children', 'frame-01', 'frame-02', 'frame-03',
        'frame-04', 'frame-05', 'frame-06'
    )) {
        if (-not (Test-Path "$captureDirectory\$name.png")) { throw "Missing demo capture: $name" }
    }
    $encoder = Join-Path $PSScriptRoot 'Encode-Apng.ps1'
    & $encoder -CaptureDirectory $captureDirectory -OutputFile "$captureDirectory\navigation-demo.png"
    Write-Output "Captures and APNG: $captureDirectory"
} finally {
    if ($null -ne $sandboxId) {
        wsb.exe stop --id $sandboxId
        if ($LASTEXITCODE -ne 0) { Write-Error "Could not stop owned Sandbox $sandboxId." }
    }
}
