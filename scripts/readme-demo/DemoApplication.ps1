param([Parameter(Mandatory = $true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
if ($env:USERNAME -ne 'WDAGUtilityAccount') { throw 'Demo application runs only inside Windows Sandbox.' }
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class DemoApplicationNative {
    [DllImport("user32.dll", SetLastError=true)] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll", SetLastError=true)] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
}
'@
if (-not [DemoApplicationNative]::SetProcessDpiAwarenessContext([IntPtr](-4))) {
    $nativeError = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
    if ($nativeError -ne 5) { throw "Demo process DPI setup failed: $nativeError" }
}
if ([DemoApplicationNative]::SetThreadDpiAwarenessContext([IntPtr](-4)) -eq [IntPtr]::Zero) {
    throw 'Demo application could not use physical-pixel DPI coordinates.'
}
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
[Windows.Forms.Application]::EnableVisualStyles()

$form = New-Object Windows.Forms.Form
$form.Text = 'Klikety Demo - Project dashboard'
$form.WindowState = 'Maximized'
$form.BackColor = [Drawing.Color]::FromArgb(245, 247, 251)
$form.Font = New-Object Drawing.Font('Segoe UI', 11)
$form.AutoScaleMode = 'Dpi'

$header = New-Object Windows.Forms.Panel
$header.Dock = 'Top'; $header.Height = 110
$header.BackColor = [Drawing.Color]::FromArgb(24, 39, 67)
$form.Controls.Add($header)
$title = New-Object Windows.Forms.Label
$title.Text = 'Northstar / Project dashboard'
$title.ForeColor = [Drawing.Color]::White
$title.Font = New-Object Drawing.Font('Segoe UI', 22, [Drawing.FontStyle]::Bold)
$title.AutoSize = $true; $title.Location = New-Object Drawing.Point(30, 22)
$header.Controls.Add($title)
$subtitle = New-Object Windows.Forms.Label
$subtitle.Text = 'Demo data only - keyboard navigation, without touching the mouse'
$subtitle.ForeColor = [Drawing.Color]::FromArgb(187, 204, 231)
$subtitle.AutoSize = $true; $subtitle.Location = New-Object Drawing.Point(33, 72)
$header.Controls.Add($subtitle)

$sidebar = New-Object Windows.Forms.Panel
$sidebar.Dock = 'Left'; $sidebar.Width = 230
$sidebar.BackColor = [Drawing.Color]::FromArgb(232, 237, 246)
$form.Controls.Add($sidebar)
for ($i = 0; $i -lt 5; $i++) {
    $button = New-Object Windows.Forms.Button
    $button.Text = @('Overview', 'Projects', 'Team', 'Reports', 'Settings')[$i]
    $button.Size = New-Object Drawing.Size(190, 44)
    $button.Location = New-Object Drawing.Point(20, (35 + $i * 58))
    $button.FlatStyle = 'Flat'; $button.FlatAppearance.BorderSize = 0
    $button.TextAlign = 'MiddleLeft'
    if ($i -eq 1) { $button.BackColor = [Drawing.Color]::FromArgb(209, 222, 246) }
    $sidebar.Controls.Add($button)
}

$workspace = New-Object Windows.Forms.Panel
$workspace.Dock = 'Fill'; $workspace.Padding = New-Object Windows.Forms.Padding(28)
$form.Controls.Add($workspace)
$sidebar.BringToFront(); $workspace.BringToFront()

$toolbar = New-Object Windows.Forms.FlowLayoutPanel
$toolbar.Dock = 'Top'; $toolbar.Height = 95
$toolbar.Padding = New-Object Windows.Forms.Padding(0, 15, 0, 0)
$workspace.Controls.Add($toolbar)
$search = New-Object Windows.Forms.TextBox
$search.Text = 'Search projects'; $search.Width = 260; $search.Margin = New-Object Windows.Forms.Padding(0, 4, 20, 0)
$toolbar.Controls.Add($search)
$filter = New-Object Windows.Forms.ComboBox
$filter.Width = 210; $filter.DropDownStyle = 'DropDown'
[void]$filter.Items.AddRange(@('All projects', 'In progress', 'Completed'))
$filter.SelectedIndex = 0; $filter.Margin = New-Object Windows.Forms.Padding(0, 4, 20, 0)
$toolbar.Controls.Add($filter)
foreach ($text in @('New project', 'Export')) {
    $button = New-Object Windows.Forms.Button
    $button.Text = $text; $button.Size = New-Object Drawing.Size(140, 36)
    $button.FlatStyle = 'Flat'; $button.BackColor = [Drawing.Color]::White
    $button.Margin = New-Object Windows.Forms.Padding(0, 0, 12, 0)
    $toolbar.Controls.Add($button)
}

$table = New-Object Windows.Forms.DataGridView
$table.Dock = 'Top'; $table.Height = 330
$table.BackgroundColor = [Drawing.Color]::White
$table.BorderStyle = 'None'; $table.RowHeadersVisible = $false
$table.AllowUserToAddRows = $false; $table.AllowUserToDeleteRows = $false
$table.AutoSizeColumnsMode = 'Fill'; $table.SelectionMode = 'FullRowSelect'
$table.MultiSelect = $false; $table.RowTemplate.Height = 50
$table.ColumnHeadersHeight = 46; $table.EnableHeadersVisualStyles = $false
$table.ColumnHeadersDefaultCellStyle.BackColor = [Drawing.Color]::FromArgb(222, 230, 242)
$table.ColumnHeadersDefaultCellStyle.Font = New-Object Drawing.Font('Segoe UI', 11, [Drawing.FontStyle]::Bold)
$table.DefaultCellStyle.SelectionBackColor = [Drawing.Color]::FromArgb(219, 231, 252)
$table.DefaultCellStyle.SelectionForeColor = [Drawing.Color]::FromArgb(24, 39, 67)
$table.GridColor = [Drawing.Color]::FromArgb(229, 234, 243)
foreach ($name in @('Project', 'Owner', 'Status', 'Due date', 'Progress')) { [void]$table.Columns.Add($name, $name) }
[void]$table.Rows.Add('Website refresh', 'Alex Morgan', 'In progress', 'Oct 24', '72%')
[void]$table.Rows.Add('Desktop client', 'Jordan Lee', 'Review', 'Nov 02', '88%')
[void]$table.Rows.Add('Design system', 'Taylor Quinn', 'In progress', 'Nov 08', '54%')
[void]$table.Rows.Add('Help center', 'Sam Rivera', 'Planning', 'Nov 15', '25%')
$workspace.Controls.Add($table); $table.BringToFront(); $toolbar.BringToFront()

$details = New-Object Windows.Forms.GroupBox
$details.Text = 'Project preferences'; $details.Dock = 'Fill'
$workspace.Controls.Add($details); $details.BringToFront()
$options = New-Object Windows.Forms.FlowLayoutPanel
$options.Dock = 'Top'; $options.Height = 65; $options.Padding = New-Object Windows.Forms.Padding(20, 12, 0, 0)
$details.Controls.Add($options)
foreach ($text in @('Notify team', 'Include archived projects')) {
    $check = New-Object Windows.Forms.CheckBox
    $check.Text = $text; $check.Checked = $true; $check.AutoSize = $true
    $check.Margin = New-Object Windows.Forms.Padding(0, 0, 30, 0)
    $options.Controls.Add($check)
}
$note = New-Object Windows.Forms.Label
$note.Text = "Navigate with label keys or arrows.`r`nZoom into a grid cell, or select an accessible control. Action keys click only when you are ready."
$note.AutoSize = $true; $note.ForeColor = [Drawing.Color]::FromArgb(65, 83, 112)
$note.Location = New-Object Drawing.Point(22, 85)
$details.Controls.Add($note)
$table.BringToFront(); $details.BringToFront()
$form.Add_Shown({
    $table.ClearSelection()
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'fixture.json'),
        ('{"Pid":' + $PID + ',"Hwnd":' + $form.Handle.ToInt64() +
        ',"Dpi":' + [DemoApplicationNative]::GetDpiForWindow($form.Handle) + '}'))
})
$form.AutoScaleDimensions = New-Object Drawing.SizeF(96, 96)
$form.PerformAutoScale()
[Windows.Forms.Application]::Run($form)
