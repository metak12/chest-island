param([string]$TestImage, [string]$OutputPath)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'WorkerStatus.ps1')
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
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
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
    $maximum = [Windows.Media.Ocr.OcrEngine]::MaxImageDimension
    $scale = [Math]::Min(3,[Math]::Min($maximum/[double]$frame.Width,$maximum/[double]$frame.Height))
    $scaled = [Drawing.Bitmap]::new([int][Math]::Floor($frame.Width*$scale),[int][Math]::Floor($frame.Height*$scale))
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

function Read-HudArea($rect, [double[]]$area) {
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -le 0 -or $height -le 0) { return }
    $x = $rect.Left + [int]($width * $area[0])
    $y = $rect.Top + [int]($height * $area[1])
    $frame = [Drawing.Bitmap]::new([Math]::Max(1,[int]($width*$area[2])),[Math]::Max(1,[int]($height*$area[3])))
    $canvas = [Drawing.Graphics]::FromImage($frame)
    try {
        $canvas.CopyFromScreen($x, $y, 0, 0, $frame.Size)
        return Read-Reward $frame
    } finally { $canvas.Dispose(); $frame.Dispose() }
}

function Get-RewardArea([string]$file) {
    $default = [double[]]@(.60,.24,.19,.18)
    try {
        $parts = [IO.File]::ReadAllText($file).Trim().Split(',')
        if ($parts.Length -ne 4) { return $default }
        $area = [double[]]@(0,0,0,0)
        for ($i=0;$i -lt 4;$i++) {
            $value = 0.0
            if (![double]::TryParse($parts[$i],[Globalization.NumberStyles]::Float,[Globalization.CultureInfo]::InvariantCulture,[ref]$value) -or
                [double]::IsNaN($value) -or $value -lt 0 -or $value -gt 1) { return $default }
            $area[$i] = $value
        }
        if ($area[2] -lt .008 -or $area[3] -lt .008 -or $area[0]+$area[2] -gt 1.00001 -or $area[1]+$area[3] -gt 1.00001) {
            return $default
        }
        return $area
    } catch [IO.IOException] { return $default }
}

function Has-SpectatorText([string]$text) {
    return (($text -replace '\s+',' ') -match '(?i)(?:\bВЫ НАБЛЮДАЕТЕ\b|\bНАБЛЮДЕНИЕ\s*(?::|ЗА\b|$)|\bSPECTATING\b)')
}

function Test-SpectatorHud($rect) {
    $top = Read-HudArea $rect @(0.27,0.025,0.46,0.14)
    if (Has-SpectatorText $top.Text) { return $true }
    $bottom = Read-HudArea $rect @(0.28,0.75,0.44,0.22)
    return (Has-SpectatorText $bottom.Text)
}

function Set-SpectatorMarker([string]$path, [bool]$active) {
    try {
        if ($active) { [IO.File]::WriteAllText($path, 'paused') }
        else { [IO.File]::Delete($path) }
    } catch [IO.IOException] {}
}

function Has-ChestReward($result) {
    return ($result.Text -match '\bСУНДУК\b')
}

function New-ToastState {
    return @{ Visible = $false; Blank = 0; Key = ''; PendingKey = ''; PendingFrames = 0 }
}

function Test-NewChestToast([hashtable]$state, [bool]$hasChest, [string]$text) {
    if (!$hasChest) {
        $state.Blank++
        $state.PendingKey = ''
        $state.PendingFrames = 0
        if ($state.Blank -ge 2) {
            $state.Visible = $false
            $state.Key = ''
            $state.Blank = 2
        }
        return $false
    }
    $state.Blank = 0
    $key = ''
    if ($text -match '(?i)\+\s*(\d[\d .\u00a0]*?)\s*(?:ОП|XP|ХР)\b') {
        $key = $Matches[1] -replace '\D',''
    }
    if (!$state.Visible) {
        $state.Visible = $true
        $state.Key = $key
        return $true
    }
    if (!$key -or $key -eq $state.Key) {
        $state.PendingKey = ''
        $state.PendingFrames = 0
        return $false
    }
    if (!$state.Key) { $state.Key = $key; return $false }
    if ($state.PendingKey -ne $key) {
        $state.PendingKey = $key
        $state.PendingFrames = 1
        return $false
    }
    $state.PendingFrames++
    if ($state.PendingFrames -lt 2) { return $false }
    $state.Key = $key
    $state.PendingKey = ''
    $state.PendingFrames = 0
    return $true
}

function Get-RewardDiagnosticText($result) {
    $normalized = ([string]$result.Text -replace '\s+', ' ').Trim()
    if ($normalized -notmatch '(?i)(?:СУНД|\+\s*[\d\s.,]+\s*(?:ОП|XP|ХР)\b)') { return }
    if ($normalized.Length -gt 240) { $normalized = $normalized.Substring(0,240) }
    return $normalized
}

function Write-RewardDiagnostic([string]$status, [string]$message) {
    $path = "$OutputPath.diagnostic"
    try {
        $time = [DateTime]::UtcNow.ToString('o',[Globalization.CultureInfo]::InvariantCulture)
        $content = "$time`n$status`n$message"
        [IO.File]::WriteAllText("$path.tmp", $content, [Text.Encoding]::UTF8)
        if ([IO.File]::Exists($path)) { [IO.File]::Replace("$path.tmp", $path, "$path.bak") }
        else { [IO.File]::Move("$path.tmp", $path) }
    } catch [IO.IOException] {
        # Diagnostics must not interrupt the counter.
    }
}

if ($TestImage) {
    $frame = [Drawing.Bitmap]::new($TestImage)
    try {
        $result = Read-Reward $frame
        Write-Host $result.Text
        Write-Output (Has-ChestReward $result)
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

function Open-GameSession([Diagnostics.Process]$process) {
    try { $started = $process.StartTime.ToUniversalTime() }
    catch [InvalidOperationException] { return }
    catch [ComponentModel.Win32Exception] { return }

    $folder = "$OutputPath.sessions"
    $path = Join-Path $folder "$($process.Id)-$($started.Ticks).txt"
    $session = @{
        Path = $path; StartTicks = [DateTime]::UtcNow.Ticks; Count = [long]0; PreviousSeconds = [long]0
        Clock = [Diagnostics.Stopwatch]::StartNew(); LastSaved = -5000
    }
    try {
        if ([IO.File]::Exists($path) -and [IO.File]::ReadAllText($path) -match '^(\d+),(\d+),(\d+)$') {
            $session.StartTicks = [long]$Matches[1]
            $session.PreviousSeconds = [long]$Matches[2]
            $session.Count = [long]$Matches[3]
        }
    } catch [IO.IOException] {
        Write-Warning "Не удалось прочитать статистику сессии: $($_.Exception.Message)"
        return
    }
    return $session
}

function Save-GameSession([hashtable]$session) {
    if (!$session) { return }
    $elapsed = $session.Clock.ElapsedMilliseconds
    $duration = $session.PreviousSeconds + [long][Math]::Floor($elapsed / 1000)
    $path = $session.Path
    try {
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
        [IO.File]::WriteAllText("$path.tmp", "$($session.StartTicks),$duration,$($session.Count)")
        if ([IO.File]::Exists($path)) { [IO.File]::Replace("$path.tmp", $path, "$path.bak") }
        else { [IO.File]::Move("$path.tmp", $path) }
    } catch [IO.IOException] {
        Write-Warning "Не удалось сохранить статистику сессии: $($_.Exception.Message)"
    }
    $session.LastSaved = $elapsed
}

function Update-Count([bool]$increment, [hashtable]$session) {
    try { [void]$writeLock.WaitOne() }
    catch [Threading.AbandonedMutexException] {}
    try {
        $value = 0
        $exists = [IO.File]::Exists($OutputPath)
        if ($exists) { $value = [int][IO.File]::ReadAllText($OutputPath) }
        if ($increment -and $value -lt [int]::MaxValue) {
            $value++
            Write-Count $value
            if ($session) { $session.Count++; Save-GameSession $session }
            try {
                $day = [DateTime]::Now.ToString('yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture)
                [IO.File]::AppendAllText("$OutputPath.history", $day + [Environment]::NewLine)
            } catch [IO.IOException] {
                Write-Warning "Не удалось сохранить событие в истории: $($_.Exception.Message)"
            }
        } elseif (!$exists) {
            Write-Count $value
        }
        return $value
    }
    finally { $writeLock.ReleaseMutex() }
}

$session = $null
$spectatorFile = "$OutputPath.spectator"
$workerStatus = New-WorkerStatus "$OutputPath.worker" $PSCommandPath
Set-SpectatorMarker $spectatorFile $false
try {
    $total = Update-Count $false
    Write-Host "Всего сундуков: $total"
    Write-Host "Файл для OBS: $OutputPath"
    Write-Host 'Слежу за уведомлениями в активном окне Fortnite. Для выхода нажми Ctrl+C.'
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $toast = New-ToastState
    $lastDiagnosticText = $null
    $spectating = $false
    $clearSamples = 0
    $lastSpectatorCheck = -1500
    $game = $null
    while ($true) {
        if (!$game -or $game.HasExited) {
            if ($session) { Save-GameSession $session; $session = $null }
            if ($game) {
                $game.Dispose()
                $spectating = $false
                $clearSamples = 0
                $lastSpectatorCheck = -1500
                Set-SpectatorMarker $spectatorFile $false
            }
            $game = Get-Process FortniteClient-Win64-Shipping -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($game) { $session = Open-GameSession $game; $toast = New-ToastState; $lastDiagnosticText = $null }
        }
        if ($session -and ($session.Clock.ElapsedMilliseconds - $session.LastSaved) -ge 5000) {
            Save-GameSession $session
        }
        $window = [FortniteWindow]::GetForegroundWindow()
        [uint32]$owner = 0
        [void][FortniteWindow]::GetWindowThreadProcessId($window, [ref]$owner)
        if (!$game -or $owner -ne $game.Id) {
            $status = 'game-closed'
            if ($game) {
                $status = 'background'
                if ($game.MainWindowHandle -ne [IntPtr]::Zero -and [FortniteWindow]::IsIconic($game.MainWindowHandle)) {
                    $status = 'minimized'
                }
            }
            Write-WorkerStatus $workerStatus $status
            Start-Sleep -Milliseconds 250
            continue
        }
        $rect = [FortniteWindow+Rect]::new()
        if (![FortniteWindow]::GetWindowRect($window, [ref]$rect)) {
            Write-WorkerStatus $workerStatus 'capture-error'
            Start-Sleep -Milliseconds 250
            continue
        }
        try { $reward = Read-HudArea $rect (Get-RewardArea "$OutputPath.region") }
        catch [Runtime.InteropServices.ExternalException] {
            Write-WorkerStatus $workerStatus 'capture-error'
            Start-Sleep -Milliseconds 250
            continue
        }
        $isChest = Has-ChestReward $reward
        $now = $watch.ElapsedMilliseconds
        if (($now - $lastSpectatorCheck) -ge 1200 -or ($isChest -and ($now - $lastSpectatorCheck) -ge 350)) {
            $lastSpectatorCheck = $now
            try {
                if (Test-SpectatorHud $rect) {
                    $clearSamples = 0
                    if (!$spectating) {
                        $spectating = $true
                        Set-SpectatorMarker $spectatorFile $true
                    }
                } elseif ($spectating) {
                    $clearSamples++
                    if ($clearSamples -ge 2) {
                        $spectating = $false
                        Set-SpectatorMarker $spectatorFile $false
                    }
                }
            } catch [Runtime.InteropServices.ExternalException] {}
        }
        $diagnosticText = Get-RewardDiagnosticText $reward
        $newToast = Test-NewChestToast $toast $isChest $reward.Text
        $status = 'waiting'
        if ($spectating) { $status = 'spectating' }
        elseif ($isChest) { $status = 'chest' }
        elseif ($diagnosticText) { $status = 'other-toast' }
        Write-WorkerStatus $workerStatus $status
        if ($isChest) {
            if ($spectating) {
                if ($diagnosticText -and $diagnosticText -ne $lastDiagnosticText) {
                    Write-RewardDiagnostic 'spectating' $diagnosticText
                }
            } elseif ($newToast) {
                $total = Update-Count $true $session
                Write-Host "Всего сундуков: $total"
                Write-RewardDiagnostic 'counted' $diagnosticText
            } elseif ($diagnosticText -and $diagnosticText -ne $lastDiagnosticText) {
                Write-RewardDiagnostic 'duplicate' $diagnosticText
            }
        } elseif ($diagnosticText -and $diagnosticText -ne $lastDiagnosticText) {
            Write-RewardDiagnostic 'no-chest' $diagnosticText
        }
        $lastDiagnosticText = $diagnosticText
        Start-Sleep -Milliseconds 120
    }
}
finally { Clear-WorkerStatus $workerStatus; Set-SpectatorMarker $spectatorFile $false; Save-GameSession $session; $writeLock.Dispose(); $lock.Dispose() }

