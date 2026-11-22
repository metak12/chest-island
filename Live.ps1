param([string]$TestImage, [string]$OutputPath)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Runtime.WindowsRuntime
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class FortniteWindow {
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Rect rect);
}
"@
[void][FortniteWindow]::SetProcessDPIAware()

$null = [Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType=WindowsRuntime]
$null = [Windows.Globalization.Language, Windows.Foundation, ContentType=WindowsRuntime]
$null = [Windows.Graphics.Imaging.BitmapDecoder, Windows.Foundation, ContentType=WindowsRuntime]
$null = [Windows.Graphics.Imaging.SoftwareBitmap, Windows.Foundation, ContentType=WindowsRuntime]
$null = [Windows.Media.Ocr.OcrResult, Windows.Foundation, ContentType=WindowsRuntime]
$asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
    $_.Name -eq 'AsTask' -and $_.IsGenericMethod -and
    $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
} | Select-Object -First 1
$decoderTask = $asTask.MakeGenericMethod([Windows.Graphics.Imaging.BitmapDecoder])
$bitmapTask = $asTask.MakeGenericMethod([Windows.Graphics.Imaging.SoftwareBitmap])
$ocrTask = $asTask.MakeGenericMethod([Windows.Media.Ocr.OcrResult])
$ocr = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage([Windows.Globalization.Language]::new('ru'))
if ($null -eq $ocr) { throw 'Windows OCR должен поддерживать русский язык.' }

function Read-Reward([Drawing.Bitmap]$frame) {
    $scaled = [Drawing.Bitmap]::new($frame.Width * 3, $frame.Height * 3)
    $canvas = [Drawing.Graphics]::FromImage($scaled)
    $memory = [IO.MemoryStream]::new()
    $stream = $null
    $bitmap = $null
    try {
        $canvas.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $canvas.DrawImage($frame, 0, 0, $scaled.Width, $scaled.Height)
        $scaled.Save($memory, [Drawing.Imaging.ImageFormat]::Bmp)
        $memory.Position = 0
        $stream = [System.IO.WindowsRuntimeStreamExtensions]::AsRandomAccessStream($memory)
        $decoder = $decoderTask.Invoke($null, @([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream))).GetAwaiter().GetResult()
        $bitmap = $bitmapTask.Invoke($null, @($decoder.GetSoftwareBitmapAsync())).GetAwaiter().GetResult()
        return $ocrTask.Invoke($null, @($ocr.RecognizeAsync($bitmap))).GetAwaiter().GetResult()
    }
    finally {
        if ($bitmap) { $bitmap.Dispose() }
        if ($stream) { $stream.Dispose() }
        $memory.Dispose()
        $canvas.Dispose()
        $scaled.Dispose()
    }
}

function Get-ChestKind($result) {
    $ordinary = $false
    foreach ($line in $result.Lines) {
        $text = ($line.Text.Trim() -replace '\s+', ' ').ToUpperInvariant()
        switch -Regex ($text) {
            '^(СУНДУК (ДУХОВ|ДУХА|С ДУХОМ|С ДУХАМИ))$' { return 'spirit' }
            '^(РЕДКИЙ СУНДУК|СУНДУК РЕДКОЙ РЕДКОСТИ)$' { return 'rare' }
            '^СУНДУК$' { $ordinary = $true }
        }
    }
    if ($ordinary) { return 'ordinary' }
}

if ($TestImage) {
    $frame = [Drawing.Bitmap]::new($TestImage)
    try {
        $result = Read-Reward $frame
        Write-Host $result.Text
        Write-Output (Get-ChestKind $result)
    }
    finally { $frame.Dispose() }
    exit
}

if (!$OutputPath) { $OutputPath = Join-Path $env:APPDATA 'FortniteChestCounter/live-total.txt' }
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($OutputPath))
try {
    $lock = [IO.File]::Open("$OutputPath.lock", 'OpenOrCreate', 'ReadWrite', 'None')
}
catch [IO.IOException] {
    $errorCode = $_.Exception.HResult -band 0xffff
    if ($errorCode -notin 32, 33) { throw }
    Write-Host 'Счётчик уже запущен. Продолжай играть, открытая копия записывает результат.'
    Write-Host "Файл для OBS: $OutputPath"
    exit 0
}

function Write-Count([int]$count) {
    $temp = "$OutputPath.tmp"
    [IO.File]::WriteAllText($temp, [string]$count)
    if ([IO.File]::Exists($OutputPath)) { [IO.File]::Replace($temp, $OutputPath, "$OutputPath.bak") }
    else { [IO.File]::Move($temp, $OutputPath) }
}

$writeLock = [Threading.Mutex]::new($false, 'Local\FortniteChestTotalWrite')

function Update-Count([string]$kind) {
    try { [void]$writeLock.WaitOne() }
    catch [Threading.AbandonedMutexException] {}
    try {
        $value = 0
        $exists = [IO.File]::Exists($OutputPath)
        if ($exists) { $value = [int][IO.File]::ReadAllText($OutputPath) }
        if ($kind -and $value -lt [int]::MaxValue) {
            $value++
            if ($kind -in 'rare','spirit') {
                $typesPath = "$OutputPath.types"
                $rare = 0
                $spirit = 0
                if ([IO.File]::Exists($typesPath) -and [IO.File]::ReadAllText($typesPath) -match '^(\d+),(\d+)$') {
                    $rare = [long]$Matches[1]
                    $spirit = [long]$Matches[2]
                }
                if ($kind -eq 'rare') { $rare++ } else { $spirit++ }
                [IO.File]::WriteAllText("$typesPath.tmp", "$rare,$spirit")
                if ([IO.File]::Exists($typesPath)) { [IO.File]::Replace("$typesPath.tmp", $typesPath, "$typesPath.bak") }
                else { [IO.File]::Move("$typesPath.tmp", $typesPath) }
            }
            Write-Count $value
        } elseif (!$exists) {
            Write-Count $value
        }
        return $value
    }
    finally { $writeLock.ReleaseMutex() }
}

try {
    $total = Update-Count ''
    Write-Host "Всего сундуков: $total"
    Write-Host "Файл для OBS: $OutputPath"
    Write-Host 'Слежу за уведомлениями в активном окне Fortnite. Для выхода нажми Ctrl+C.'
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $lastSeen = -1000
    $game = $null
    while ($true) {
        if (!$game -or $game.HasExited) {
            $game = Get-Process FortniteClient-Win64-Shipping -ErrorAction SilentlyContinue | Select-Object -First 1
        }
        $window = [FortniteWindow]::GetForegroundWindow()
        [uint32]$owner = 0
        [void][FortniteWindow]::GetWindowThreadProcessId($window, [ref]$owner)
        if (!$game -or $owner -ne $game.Id) {
            Start-Sleep -Milliseconds 250
            continue
        }
        $rect = [FortniteWindow+Rect]::new()
        if (![FortniteWindow]::GetWindowRect($window, [ref]$rect)) {
            Start-Sleep -Milliseconds 250
            continue
        }
        $width = $rect.Right - $rect.Left
        $height = $rect.Bottom - $rect.Top
        # The XP toast in the observed Russian HUD is centered near 70% x, 33% y.
        $x = $rect.Left + [int]($width * 0.60)
        $y = $rect.Top + [int]($height * 0.24)
        $frame = [Drawing.Bitmap]::new([int]($width * 0.19), [int]($height * 0.18))
        $capture = [Drawing.Graphics]::FromImage($frame)
        try {
            $capture.CopyFromScreen($x, $y, 0, 0, $frame.Size)
            $kind = Get-ChestKind (Read-Reward $frame)
        }
        finally {
            $capture.Dispose()
            $frame.Dispose()
        }
        if ($kind) {
            $now = $watch.ElapsedMilliseconds
            if (($now - $lastSeen) -gt 650) {
                $total = Update-Count $kind
                Write-Host "Всего сундуков: $total"
            }
            $lastSeen = $now
        }
        Start-Sleep -Milliseconds 120
    }
}
finally { $writeLock.Dispose(); $lock.Dispose() }

