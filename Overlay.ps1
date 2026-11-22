param([switch]$Move)

$ErrorActionPreference = 'Stop'
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

$positionPath = Join-Path $env:APPDATA 'FortniteChestCounter/overlay-position.txt'

function Get-OverlayPosition([Drawing.Rectangle]$screen, [Drawing.Size]$size) {
    $x = [int]($screen.Width * 0.57)
    $y = 65
    try {
        if ([IO.File]::ReadAllText($positionPath) -match '^(\d+),(\d+)$') {
            $x = [int]$Matches[1]
            $y = [int]$Matches[2]
        }
    } catch [IO.IOException] {
        # Use the default position until one has been saved.
    }
    $x = [Math]::Max(0, [Math]::Min($x, $screen.Width - $size.Width))
    $y = [Math]::Max(0, [Math]::Min($y, $screen.Height - $size.Height))
    [Drawing.Point]::new(($screen.Left + $x), ($screen.Top + $y))
}

if ($Move) {
    $editor = [Windows.Forms.Form]::new()
    try {
        $editor.Text = 'Перемещение Chest Island'
        $editor.TopMost = $true
        $editor.KeyPreview = $true
        $editor.StartPosition = 'Manual'
        $editor.FormBorderStyle = 'None'
        $editor.ClientSize = [Drawing.Size]::new(218, 72)
        $editor.BackColor = [Drawing.Color]::FromArgb(31, 25, 47)
        $game = Get-Process FortniteClient-Win64-Shipping -ErrorAction SilentlyContinue |
            Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
        $target = [Windows.Forms.Screen]::FromPoint([Windows.Forms.Cursor]::Position)
        if ($game) { $target = [Windows.Forms.Screen]::FromHandle($game.MainWindowHandle) }
        $editor.Location = Get-OverlayPosition $target.Bounds ([Drawing.Size]::new(218,42))
        $data = Join-Path $env:APPDATA 'FortniteChestCounter/live-total.txt'
        $value = '0'
        if ([IO.File]::Exists($data)) { $value = [IO.File]::ReadAllText($data).Trim() }

        $preview = [Windows.Forms.Label]::new()
        $preview.Bounds = [Drawing.Rectangle]::new(0,0,218,42)
        $preview.TextAlign = 'MiddleCenter'
        $preview.Text = "⠿   Сундуки: $value"
        $preview.ForeColor = [Drawing.Color]::White
        $preview.Font = [Drawing.Font]::new('Segoe UI Semibold', 14)
        $preview.Cursor = [Windows.Forms.Cursors]::SizeAll
        $editor.Controls.Add($preview)

        $done = [Windows.Forms.Button]::new()
        $done.Text = 'Готово'
        $done.Bounds = [Drawing.Rectangle]::new(112,43,99,24)
        $done.FlatStyle = 'Flat'
        $done.ForeColor = [Drawing.Color]::White
        $editor.Controls.Add($done)
        $cancel = [Windows.Forms.Button]::new()
        $cancel.Text = 'Отмена'
        $cancel.Bounds = [Drawing.Rectangle]::new(7,43,99,24)
        $cancel.FlatStyle = 'Flat'
        $cancel.ForeColor = [Drawing.Color]::White
        $editor.Controls.Add($cancel)
        $cancel.Add_Click({ $editor.Close() })
        $editor.AcceptButton = $done
        $editor.CancelButton = $cancel

        $save = {
            $bounds = [Windows.Forms.Screen]::FromControl($editor).Bounds
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
    } finally { $editor.Dispose() }
    exit
}

$created = $false
$instance = [Threading.Mutex]::new($true, 'Local\FortniteChestCounterOverlay', [ref]$created)
if (!$created) {
    $instance.Dispose()
    Write-Host 'Оверлей уже запущен.'
    exit
}

$form = [ChestOverlay]::new()
$timer = [Windows.Forms.Timer]::new()
$font = [Drawing.Font]::new('Segoe UI Semibold', 15)
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
    $label.Dock = 'Fill'
    $label.TextAlign = 'MiddleCenter'
    $label.Font = $font
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
        if (!$game) { $form.Hide(); return }

        try {
            $value = [IO.File]::ReadAllText($totalPath).Trim()
            if ($value -match '^\d+$') { $label.Text = "Сундуки: $value" }
        }
        catch [IO.IOException] {
            # Keep showing the last count while the counter updates the file.
        }
        $screen = [Windows.Forms.Screen]::FromHandle($foreground).Bounds
        $form.Location = Get-OverlayPosition $screen $form.Size
        if (!$form.Visible) { $form.Show() }
        $form.KeepAboveGame()
    })
    Write-Host 'Оверлей запущен. Вернись в Fortnite. Для выхода закрой это окно.'
    $timer.Start()
    [Windows.Forms.Application]::Run()
}
finally {
    $timer.Dispose()
    $form.Dispose()
    $font.Dispose()
    $instance.ReleaseMutex()
    $instance.Dispose()
}
