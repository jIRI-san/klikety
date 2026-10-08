param(
    [string]$InputDirectory = 'C:\DemoInput',
    [string]$OutputDirectory = 'C:\DemoOutput'
)
$ErrorActionPreference = 'Stop'
if ($env:USERNAME -ne 'WDAGUtilityAccount') { throw 'Screen capture and input run only inside Windows Sandbox.' }
Add-Type -AssemblyName System.Windows.Forms, System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class DemoNative {
    [StructLayout(LayoutKind.Sequential)] struct Keyboard {
        public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra;
    }
    [StructLayout(LayoutKind.Sequential)] struct Mouse {
        public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra;
    }
    [StructLayout(LayoutKind.Explicit)] struct Union {
        [FieldOffset(0)] public Keyboard Keyboard;
        [FieldOffset(0)] public Mouse Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] struct Input { public uint Type; public Union Data; }
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll", SetLastError=true)] static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", SetLastError=true)] static extern uint SendInput(uint count, Input[] input, int size);
    public static void Key(ushort key, bool up) {
        var input = new Input { Type = 1, Data = new Union {
            Keyboard = new Keyboard { Key = key, Flags = up ? 2u : 0u }
        }};
        if (SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Input))) != 1)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    public static void ActivateFixture(IntPtr hwnd, uint expectedPid) {
        uint pid;
        uint targetThread = GetWindowThreadProcessId(hwnd, out pid);
        if (pid != expectedPid || targetThread == 0) throw new InvalidOperationException("Fixture identity changed.");
        if (GetForegroundWindow() == hwnd) return;
        SetForegroundWindow(hwnd);
        if (GetForegroundWindow() == hwnd) return;
        uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out pid);
        uint currentThread = GetCurrentThreadId();
        if (!AttachThreadInput(currentThread, foregroundThread, true))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try { BringWindowToTop(hwnd); SetForegroundWindow(hwnd); }
        finally {
            if (!AttachThreadInput(currentThread, foregroundThread, false))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        if (GetForegroundWindow() != hwnd) throw new InvalidOperationException("Owned guest fixture could not acquire foreground.");
    }
}
'@
[void][DemoNative]::SetProcessDPIAware()

$fixture = $null; $app = $null
function Assert-GuestForeground {
    [uint32]$owner = 0
    [void][DemoNative]::GetWindowThreadProcessId([DemoNative]::GetForegroundWindow(), [ref]$owner)
    if ($owner -ne $fixture.Id -and ($null -eq $app -or $owner -ne $app.Id)) {
        throw "Guest foreground changed to unrelated process $owner; input and capture refused."
    }
}
function Press-Key([int]$Key) {
    Assert-GuestForeground
    [DemoNative]::Key($Key, $false)
    try { Start-Sleep -Milliseconds 60 } finally { [DemoNative]::Key($Key, $true) }
    Start-Sleep -Milliseconds 350
}
function Open-Overlay {
    [DemoNative]::ActivateFixture([IntPtr]$fixtureInfo.Hwnd, $fixture.Id)
    Start-Sleep -Milliseconds 300
    Assert-GuestForeground
    foreach ($key in @(0x11, 0x12, 0x10)) { [DemoNative]::Key($key, $false) }
    try { Press-Key 0x7A } finally {
        foreach ($key in @(0x10, 0x12, 0x11)) { [DemoNative]::Key($key, $true) }
    }
    Start-Sleep -Milliseconds 650
}
function Save-Capture([string]$Name) {
    Assert-GuestForeground
    $bounds = [Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap = New-Object Drawing.Bitmap($bounds.Width, $bounds.Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($bounds.Location, [Drawing.Point]::Empty, $bounds.Size)
        $bitmap.Save((Join-Path $OutputDirectory ($Name + '.png')), [Drawing.Imaging.ImageFormat]::Png)
        "Captured $Name : $($bounds.Width)x$($bounds.Height)" | Add-Content (Join-Path $OutputDirectory 'capture.log')
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
}
function Close-Overlay {
    # Several modes have a level/axis stack. No action keys are sent.
    1..5 | ForEach-Object { Press-Key 0x1B }
}
function Wait-Until([scriptblock]$Condition, [string]$Failure, [int]$Seconds = 20) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    while (-not (& $Condition)) {
        if ([DateTime]::UtcNow -gt $deadline) { throw $Failure }
        Start-Sleep -Milliseconds 250
    }
}
function Wait-HintLevel([int]$Level) {
    $processCondition = New-Object Windows.Automation.PropertyCondition(
        [Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$app.Id)
    $textCondition = New-Object Windows.Automation.PropertyCondition(
        [Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Text)
    Wait-Until {
        Assert-GuestForeground
        $windows = [Windows.Automation.AutomationElement]::RootElement.FindAll(
            [Windows.Automation.TreeScope]::Children, $processCondition)
        foreach ($window in $windows) {
            $texts = $window.FindAll([Windows.Automation.TreeScope]::Descendants, $textCondition)
            foreach ($text in $texts) {
                if ($text.Current.Name -match "\bL$Level\b" -and $text.Current.Name -notmatch 'Finding controls|unavailable|failed') {
                    return $true
                }
            }
        }
        return $false
    } "Production hint footer did not reach L$Level."
}

try {
    if (-not (Test-Path $OutputDirectory -PathType Container)) { throw 'Capture output directory must already exist.' }
    if (Test-Path (Join-Path $OutputDirectory 'fixture.json')) { throw 'Use an empty output directory for each capture run.' }
    $configDirectory = Join-Path $env:APPDATA 'Klikety'
    [void](New-Item -ItemType Directory -Path $configDirectory -Force)
    $json = Get-Content (Join-Path $InputDirectory 'config.json') -Raw
    $config = ([regex]::Replace($json, '(?m)^\s*//.*$', '')) | ConvertFrom-Json
    $config.hotKey.modifiers = 'Control, Alt, Shift'; $config.hotKey.key = 'F11'
    $config.macros.enabled = $false
    $config.modes.elementHints.enabled = $true
    $config.modes.elementHints.discoveryTimeoutMs = 10000
    $config | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $configDirectory 'config.json') -Encoding UTF8

    $fixture = Start-Process powershell.exe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File $InputDirectory\DemoApplication.ps1 -OutputDirectory $OutputDirectory" -PassThru
    Wait-Until { Test-Path (Join-Path $OutputDirectory 'fixture.json') } 'Demo application did not become ready.'
    $fixtureInfo = Get-Content (Join-Path $OutputDirectory 'fixture.json') -Raw | ConvertFrom-Json
    if ($fixtureInfo.Pid -ne $fixture.Id) { throw 'Demo window identity mismatch.' }
    [DemoNative]::ActivateFixture([IntPtr]$fixtureInfo.Hwnd, $fixture.Id)
    Start-Sleep -Seconds 1

    # Warm the guest provider before illustrative captures; this is not a cold-start test.
    $element = [Windows.Automation.AutomationElement]::FromHandle([IntPtr]$fixtureInfo.Hwnd)
    $controls = $element.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition)
    "Warm fixture: $($controls.Count) UIA nodes" | Set-Content (Join-Path $OutputDirectory 'capture.log')

    $app = Start-Process (Join-Path $InputDirectory 'App\Klikety.exe') -PassThru
    Start-Sleep -Seconds 3
    if ($app.HasExited) { throw 'Guest Klikety exited during startup.' }
    $probe = Start-Process (Join-Path $InputDirectory 'Probe\HintLabelsProbe.exe') -ArgumentList @(
        (Join-Path $InputDirectory 'App\uia-worker\Klikety.UiaWorker.exe'),
        $fixtureInfo.Hwnd, $app.Id, (Join-Path $OutputDirectory 'hierarchy.json')
    ) -PassThru -Wait -WindowStyle Hidden -RedirectStandardError (Join-Path $OutputDirectory 'probe-error.txt')
    if ($probe.ExitCode -ne 0) { throw "Read-only hierarchy probe failed: $(Get-Content (Join-Path $OutputDirectory 'probe-error.txt') -Raw)" }
    $hierarchy = Get-Content (Join-Path $OutputDirectory 'hierarchy.json') -Raw | ConvertFrom-Json

    [DemoNative]::ActivateFixture([IntPtr]$fixtureInfo.Hwnd, $fixture.Id)
    Start-Sleep -Milliseconds 400
    Assert-GuestForeground
    $screen = [Windows.Forms.Screen]::PrimaryScreen.Bounds
    [void][DemoNative]::SetCursorPos([int]($screen.Width * 0.61), [int]($screen.Height * 0.42))
    Save-Capture 'demo'
    Open-Overlay
    Save-Capture 'uniform-grid'
    Save-Capture 'frame-01'
    Press-Key 0x4A
    Save-Capture 'frame-02'
    Press-Key 0x54
    Save-Capture 'uniform-grid-zoom'
    Save-Capture 'frame-03'
    Press-Key 0x47
    Save-Capture 'frame-04'
    Press-Key 0x59
    Save-Capture 'frame-05'
    Press-Key 0x1B
    Save-Capture 'frame-06'
    Close-Overlay

    foreach ($mode in @(
        @{ Name = 'crosshair'; Key = 0x4E },
        @{ Name = 'log-crosshair'; Key = 0x4D },
        @{ Name = 'log-grid'; Key = 0xBC }
    )) {
        [void][DemoNative]::SetCursorPos([int]($screen.Width * 0.61), [int]($screen.Height * 0.42))
        Open-Overlay
        Press-Key $mode.Key
        Save-Capture $mode.Name
        Close-Overlay
    }

    Open-Overlay
    Press-Key 0x09
    Wait-HintLevel 1
    Start-Sleep -Milliseconds 500
    Save-Capture 'element-hints'
    foreach ($key in $hierarchy.GroupKeys) { Press-Key $key }
    Wait-HintLevel 2
    Save-Capture 'element-hints-children'
    Close-Overlay

    $logs = Get-ChildItem (Join-Path $configDirectory 'logs') -Filter '*.log'
    foreach ($log in $logs) { Copy-Item $log.FullName (Join-Path $OutputDirectory $log.Name) }
    'Complete: guest-only production navigation captures; no clicks or drags sent.' |
        Set-Content (Join-Path $OutputDirectory 'done.txt')
} catch {
    $_ | Out-String | Set-Content (Join-Path $OutputDirectory 'error.txt')
    throw
} finally {
    if ($null -ne $app -and -not $app.HasExited) { Stop-Process -Id $app.Id -ErrorAction Continue }
    if ($null -ne $fixture -and -not $fixture.HasExited) { Stop-Process -Id $fixture.Id -ErrorAction Continue }
}
