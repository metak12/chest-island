param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'),
    [ValidatePattern('^[a-fA-F0-9]{40}$')][string]$SigningThumbprint,
    [string]$SignToolPath = 'signtool.exe'
)
$ErrorActionPreference = 'Stop'
$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $fw 'csc.exe'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($OutputDirectory)
function Sign-Executable([string]$Path) {
    if (!$SigningThumbprint) { return }
    & $SignToolPath sign /fd SHA256 /sha1 $SigningThumbprint /tr https://timestamp.digicert.com /td SHA256 $Path
    if ($LASTEXITCODE) { throw "Authenticode signing failed for $Path." }
    & $SignToolPath verify /pa /v $Path
    if ($LASTEXITCODE) { throw "Authenticode verification failed for $Path." }
}

Push-Location $PSScriptRoot
try {
    $app = Join-Path $OutputDirectory 'Chest Island.exe'
    & $csc /nologo /target:winexe /optimize+ /win32icon:assets\ChestIsland.ico "/out:$app" "/reference:$fw\WPF\PresentationFramework.dll" "/reference:$fw\WPF\PresentationCore.dll" "/reference:$fw\WPF\WindowsBase.dll" "/reference:$fw\System.Xaml.dll" /reference:System.Management.dll /reference:System.Web.Extensions.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll .\ChestIsland.cs .\Settings.cs .\DashboardUpdates.cs .\ReleaseUpdate.cs
    if ($LASTEXITCODE) { throw 'Chest Island compilation failed.' }
    Sign-Executable $app
    $setup = Join-Path $OutputDirectory 'Chest-Island-Setup.exe'
    & $csc /nologo /target:winexe /optimize+ /win32icon:assets\ChestIsland.ico "/out:$setup" "/resource:$app,Chest Island.exe" '/resource:ChestIsland.xaml,ChestIsland.xaml' '/resource:Live.ps1,Live.ps1' '/resource:Overlay.ps1,Overlay.ps1' /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:Microsoft.CSharp.dll .\Setup.cs
    if ($LASTEXITCODE) { throw 'Installer compilation failed.' }
    Sign-Executable $setup
    Get-Item $setup
} finally { Pop-Location }
