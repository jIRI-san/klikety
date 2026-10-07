#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PublishDirectory)

$ErrorActionPreference = 'Stop'
$directory = (Resolve-Path -LiteralPath $PublishDirectory).Path
$worker = Join-Path $directory 'uia-worker\Klikety.UiaWorker.exe'
$appVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $directory 'Klikety.exe')).ProductVersion
$workerVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($worker).ProductVersion
if ($appVersion -ne $workerVersion) { throw "App/worker version mismatch: $appVersion / $workerVersion" }
foreach ($name in @('Klikety.UiaWorker.exe', 'Klikety.UiaWorker.dll', 'Klikety.UiaWorker.runtimeconfig.json', 'Klikety.UiaWorker.deps.json', 'coreclr.dll', 'UIAutomationClient.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $directory "uia-worker\$name"))) {
        throw "Published UIA worker is missing $name"
    }
}
$runtime = Get-Content -LiteralPath (Join-Path $directory 'uia-worker\Klikety.UiaWorker.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($runtime.runtimeOptions.framework -or $runtime.runtimeOptions.frameworks -or -not $runtime.runtimeOptions.includedFrameworks) {
    throw 'Published UIA worker is not self-contained'
}
$start = [System.Diagnostics.ProcessStartInfo]::new($worker)
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.WorkingDirectory = Split-Path -Parent $worker
$start.Environment['PATH'] = ''
$start.Environment['DOTNET_ROOT'] = Join-Path $directory 'no-installed-dotnet'
$start.Environment['DOTNET_ROOT_X64'] = $start.Environment['DOTNET_ROOT']
$start.Environment['DOTNET_MULTILEVEL_LOOKUP'] = '0'
$process = [System.Diagnostics.Process]::new()
$process.StartInfo = $start
$started = $false
try {
    $started = $process.Start()
    if (-not $started) { throw 'Worker did not start' }
    $deadline = [System.Diagnostics.Stopwatch]::StartNew()
    $session = [guid]::NewGuid().ToString()
    $request = [guid]::NewGuid().ToString()
    $json = @{ Version = 1; SessionId = $session; RequestId = $request; Command = 0 } | ConvertTo-Json -Compress
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    $process.StandardInput.BaseStream.Write([BitConverter]::GetBytes($bytes.Length))
    $process.StandardInput.BaseStream.Write($bytes)
    $process.StandardInput.BaseStream.Flush()
    function Read-FrameBytes([int]$length) {
        $buffer = [byte[]]::new($length)
        $offset = 0
        while ($offset -lt $length) {
            $read = $process.StandardOutput.BaseStream.ReadAsync($buffer, $offset, $length - $offset)
            $remaining = 1500 - [int]$deadline.ElapsedMilliseconds
            if ($remaining -le 0 -or -not $read.Wait($remaining)) { throw 'Worker handshake timed out' }
            $count = $read.GetAwaiter().GetResult()
            if ($count -eq 0) { throw 'Worker exited before handshake' }
            $offset += $count
        }
        return ,$buffer
    }
    $length = [BitConverter]::ToInt32((Read-FrameBytes 4), 0)
    if ($length -le 0 -or $length -gt 2097152) { throw 'Invalid worker response size' }
    $response = [System.Text.Encoding]::UTF8.GetString((Read-FrameBytes $length)) | ConvertFrom-Json
    if ($response.Version -ne 1 -or $response.SessionId -ne $session -or $response.RequestId -ne $request -or $response.Outcome -ne 0) {
        throw 'Worker handshake mismatch'
    }
    $process.StandardInput.Close()
    if (-not $process.WaitForExit(500)) { throw 'Worker did not exit after stdin closed' }
    if ($process.ExitCode -ne 0) { throw "Worker exited with code $($process.ExitCode)" }
    Write-Output 'Extracted self-contained UIA worker handshake passed with PATH empty and DOTNET_ROOT unavailable.'
} finally {
    if ($started -and -not $process.HasExited) { $process.Kill($true); $null = $process.WaitForExit(500) }
    $process.Dispose()
}
