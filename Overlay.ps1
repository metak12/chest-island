param([switch]$Move)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'WorkerStatus.ps1')
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Windows.Forms,System.Drawing -TypeDefinition @"
using System.Runtime.InteropServices;
using System;
using System.Windows.Forms;

public sealed class ChestOverlay : Form {
    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams {
        get {
            var settings = base.CreateParams;
            // Click-through, tool window, and no keyboard focus.
            settings.ExStyle |= 0x20 | 0x80 | 0x08000000;
            return settings;
        }
    }

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr first, IntPtr second);

    public static void Drag(Form form) {
        ReleaseCapture();
        SendMessage(form.Handle, 0xA1, new IntPtr(2), IntPtr.Zero);
    }

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    public void KeepAboveGame() {
        // HWND_TOPMOST, without moving, resizing, or taking keyboard focus.
        SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0013);
    }
}
"@

$overlayData = Join-Path $env:APPDATA 'FortniteChestCounter'

function Get-OverlayProfile {
    try {
        if ([IO.File]::ReadAllText((Join-Path $overlayData 'overlay-profile.txt')).Trim() -eq '2') { return 2 }
    } catch [IO.IOException] {}
    return 1
}

function Get-OverlayPath([string]$name, [int]$profile) {
    if ($profile -eq 2) { $name += '-2' }
    return (Join-Path $overlayData ($name + '.txt'))
}

function Get-MonitorPositionPath([int]$profile, [string]$deviceName) {
    $device = $deviceName -replace '[^A-Za-z0-9]',''
    if (!$device) { return (Get-OverlayPath 'overlay-position' $profile) }
    return (Get-OverlayPath ("overlay-position-" + $device.ToLowerInvariant()) $profile)
}

function Get-OverlayPosition([Windows.Forms.Screen]$monitor, [Drawing.Size]$size, [int]$profile) {
    $screen = $monitor.Bounds
    $x = [int]($screen.Width * 0.57)
    $y = 65
    $candidates = @((Get-MonitorPositionPath $profile $monitor.DeviceName), (Get-OverlayPath 'overlay-position' $profile))
    foreach ($positionPath in $candidates) {
        try {
            if ([IO.File]::ReadAllText($positionPath).Trim() -match '^(\d+),(\d+)$') {
                $x = [int]$Matches[1]
                $y = [int]$Matches[2]
                break
            }
        } catch [IO.IOException] {}
    }
    $x = [Math]::Max(0, [Math]::Min($x, $screen.Width - $size.Width))
    $y = [Math]::Max(0, [Math]::Min($y, $screen.Height - $size.Height))
    [Drawing.Point]::new(($screen.Left + $x), ($screen.Top + $y))
}

$historyPath = Join-Path $overlayData 'live-total.txt.history'
$todayCache = @{ Key = ''; Count = 0 }
$display = @{ AppliedSize = -1 }

function Get-OverlaySettings([int]$profile) {
    $overlaySettingsPath = Get-OverlayPath 'overlay-settings' $profile
    $size = 1
    $opacity = 89
    $mode = 'goal'
    try {
        if ([IO.File]::ReadAllText($overlaySettingsPath).Trim() -match '^([0-2]),(\d{2,3}),(total|goal|today)$') {
            $size = [int]$Matches[1]
            $opacity = [Math]::Max(30, [Math]::Min(100, [int]$Matches[2]))
            $mode = $Matches[3]
        }
    } catch [IO.IOException] {}
    return @{
        Width = @(172,218,280)[$size]; Height = @(32,42,54)[$size]
        Font = @(12,15,19)[$size]; Size = $size; Opacity = $opacity; Mode = $mode
    }
}

function Get-TodayCount {
    $day = [DateTime]::Today.ToString('yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture)
    try {
        $info = [IO.FileInfo]::new($historyPath)
        if (!$info.Exists) { return 0 }
        $key = "$day/$($info.Length)/$($info.LastWriteTimeUtc.Ticks)"
        if ($todayCache.Key -ne $key) {
            $count = 0
            $stream = [IO.File]::Open($historyPath,'Open','Read',([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
            $reader = [IO.StreamReader]::new($stream)
            try {
                while ($null -ne ($line = $reader.ReadLine())) {
                    if ($line -ceq $day) { $count++ }
                }
                $todayCache.Key = $key
                $todayCache.Count = $count
            } finally { $reader.Dispose() }
        }
    } catch [IO.IOException] {}
    return $todayCache.Count
}

function Format-OverlayCount([string]$value, [string]$mode) {
    if ($mode -eq 'today') { return "Сегодня: $(Get-TodayCount)" }
    if ($mode -eq 'goal') {
        $goalPath = Join-Path $overlayData 'goal.txt'
        $goal = 0
        try { [void][int]::TryParse([IO.File]::ReadAllText($goalPath),[ref]$goal) }
        catch [IO.IOException] {}
        if ($goal -gt 0) { return "$value / $goal" }
    }
    return "Сундуки: $value"
}

if ($Move) {
    $editor = [Windows.Forms.Form]::new()
    try {
        $editor.Text = 'Перемещение Chest Island'
        $editor.TopMost = $true
        $editor.KeyPreview = $true
        $editor.StartPosition = 'Manual'
        $editor.FormBorderStyle = 'None'
        $profile = Get-OverlayProfile
        $settings = Get-OverlaySettings $profile
        $editor.ClientSize = [Drawing.Size]::new($settings.Width, ($settings.Height + 30))
        $editor.BackColor = [Drawing.Color]::FromArgb(31, 25, 47)
        $game = Get-Process FortniteClient-Win64-Shipping -ErrorAction SilentlyContinue |
            Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
        $target = [Windows.Forms.Screen]::FromPoint([Windows.Forms.Cursor]::Position)
        if ($game) { $target = [Windows.Forms.Screen]::FromHandle($game.MainWindowHandle) }
        $editor.Location = Get-OverlayPosition $target ([Drawing.Size]::new($settings.Width,$settings.Height)) $profile
        $data = Join-Path $env:APPDATA 'FortniteChestCounter/live-total.txt'
        $value = '0'
        if ([IO.File]::Exists($data)) { $value = [IO.File]::ReadAllText($data).Trim() }

        $preview = [Windows.Forms.Label]::new()
        $preview.UseCompatibleTextRendering = $false
        $preview.Bounds = [Drawing.Rectangle]::new(0,0,$settings.Width,$settings.Height)
        $preview.TextAlign = 'MiddleCenter'
        $preview.Text = "⠿   " + (Format-OverlayCount $value $settings.Mode)
        $preview.ForeColor = [Drawing.Color]::White
        $previewFont = [Drawing.Font]::new('Segoe UI Semibold', [single]$settings.Font)
        $preview.Font = $previewFont
        $preview.Cursor = [Windows.Forms.Cursors]::SizeAll
        $editor.Controls.Add($preview)

        $done = [Windows.Forms.Button]::new()
        $done.Text = 'Готово'
        $done.Bounds = [Drawing.Rectangle]::new([int]($settings.Width/2+3),($settings.Height+1),[int]($settings.Width/2-10),24)
        $done.FlatStyle = 'Flat'
        $done.ForeColor = [Drawing.Color]::White
        $editor.Controls.Add($done)
        $cancel = [Windows.Forms.Button]::new()
        $cancel.Text = 'Отмена'
        $cancel.Bounds = [Drawing.Rectangle]::new(7,($settings.Height+1),[int]($settings.Width/2-10),24)
        $cancel.FlatStyle = 'Flat'
        $cancel.ForeColor = [Drawing.Color]::White
        $editor.Controls.Add($cancel)
        $cancel.Add_Click({ $editor.Close() })
        $editor.AcceptButton = $done
        $editor.CancelButton = $cancel

        $save = {
            $monitor = [Windows.Forms.Screen]::FromControl($editor)
            $bounds = $monitor.Bounds
            $positionPath = Get-MonitorPositionPath $profile $monitor.DeviceName
            $x = [Math]::Max(0, $editor.Left - $bounds.Left)
            $y = [Math]::Max(0, $editor.Top - $bounds.Top)
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($positionPath))
            [IO.File]::WriteAllText($positionPath, "$x,$y")
            $editor.Close()
        }
        $preview.Add_MouseDown({
            if ($_.Button -eq [Windows.Forms.MouseButtons]::Left) {
                [ChestOverlay]::Drag($editor)
                & $save
            }
        })
        $done.Add_Click($save)
        [Windows.Forms.Application]::Run($editor)
    } finally {
        $editor.Dispose()
        if ($previewFont) { $previewFont.Dispose() }
    }
    exit
}

$created = $false
$instance = [Threading.Mutex]::new($true, 'Local\FortniteChestCounterOverlay', [ref]$created)
if (!$created) {
    $instance.Dispose()
    Write-Host 'Оверлей уже запущен.'
    exit
}

$workerStatus = New-WorkerStatus (Join-Path $overlayData 'overlay.worker') $PSCommandPath
$form = [ChestOverlay]::new()
$timer = [Windows.Forms.Timer]::new()
$fonts = @(12,15,19 | ForEach-Object { [Drawing.Font]::new('Segoe UI Semibold',[single]$_) })
try {
    $form.Text = 'Сундуки Fortnite'
    $form.TopMost = $true
    $form.ShowInTaskbar = $false
    $form.FormBorderStyle = 'None'
    $form.StartPosition = 'Manual'
    $form.ClientSize = [Drawing.Size]::new(218, 42)
    $form.BackColor = [Drawing.Color]::FromArgb(23, 25, 32)
    $form.Opacity = 0.89

    $label = [Windows.Forms.Label]::new()
    $label.UseCompatibleTextRendering = $false
    $label.Dock = 'Fill'
    $label.TextAlign = 'MiddleCenter'
    $label.Font = $fonts[1]
    $label.ForeColor = [Drawing.Color]::White
    $label.Text = 'Сундуки: —'
    $form.Controls.Add($label)

    $totalPath = Join-Path $env:APPDATA 'FortniteChestCounter/live-total.txt'
    $timer.Interval = 200
    $timer.Add_Tick({
        $foreground = [ChestOverlay]::GetForegroundWindow()
        [uint32]$ownerId = 0
        [void][ChestOverlay]::GetWindowThreadProcessId($foreground, [ref]$ownerId)
        $game = Get-Process FortniteClient-Win64-Shipping -ErrorAction SilentlyContinue |
            Where-Object { $_.Id -eq $ownerId } |
            Select-Object -First 1
        if (!$game) { Write-WorkerStatus $workerStatus 'waiting'; $form.Hide(); return }
        Write-WorkerStatus $workerStatus 'visible'

        $profile = Get-OverlayProfile
        $settings = Get-OverlaySettings $profile
        if ($display.AppliedSize -ne $settings.Size) {
            $form.ClientSize = [Drawing.Size]::new($settings.Width, $settings.Height)
            $label.Font = $fonts[$settings.Size]
            $display.AppliedSize = $settings.Size
        }
        $form.Opacity = $settings.Opacity / 100
        try {
            $value = '0'
            if ([IO.File]::Exists($totalPath)) { $value = [IO.File]::ReadAllText($totalPath).Trim() }
            if ($value -match '^\d+$') { $label.Text = Format-OverlayCount $value $settings.Mode }
        }
        catch [IO.IOException] {
            # Keep showing the last count while the counter updates the file.
        }
        $monitor = [Windows.Forms.Screen]::FromHandle($foreground)
        $form.Location = Get-OverlayPosition $monitor $form.Size $profile
        if (!$form.Visible) { $form.Show() }
        $form.KeepAboveGame()
    })
    Write-Host 'Оверлей запущен. Вернись в Fortnite. Для выхода закрой это окно.'
    $timer.Start()
    [Windows.Forms.Application]::Run()
}
finally {
    Clear-WorkerStatus $workerStatus
    $timer.Dispose()
    $form.Dispose()
    foreach ($font in $fonts) { $font.Dispose() }
    $instance.ReleaseMutex()
    $instance.Dispose()
}
