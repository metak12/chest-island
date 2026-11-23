function New-WorkerStatus([string]$file, [string]$scriptPath) {
    $version = 'development'
    try {
        $version = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Version.txt')).Trim()
    } catch [IO.IOException] {}
    return @{ File = $file; Script = $scriptPath; Version = $version; Last = [DateTime]::MinValue; State = '' }
}

function Write-WorkerStatus([hashtable]$status, [string]$state) {
    $now = [DateTime]::UtcNow
    if ($status.State -eq $state -and ($now - $status.Last).TotalSeconds -lt 2) { return }
    $path = $status.File
    try {
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
        $stamp = $now.ToString('o',[Globalization.CultureInfo]::InvariantCulture)
        $content = @($status.Version,[string]$PID,$status.Script,$state,$stamp)
        [IO.File]::WriteAllLines("$path.tmp",[string[]]$content)
        if ([IO.File]::Exists($path)) { [IO.File]::Replace("$path.tmp",$path,"$path.bak") }
        else { [IO.File]::Move("$path.tmp",$path) }
        $status.State = $state
        $status.Last = $now
    } catch [IO.IOException] {}
}

function Clear-WorkerStatus([hashtable]$status) {
    try { [IO.File]::Delete($status.File) }
    catch [IO.IOException] {}
}
