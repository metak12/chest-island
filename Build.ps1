param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference = 'Stop'
$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $fw 'csc.exe'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($OutputDirectory)
Push-Location $PSScriptRoot
try {
    $app = Join-Path $OutputDirectory 'Chest Island.exe'
    & $csc /nologo /target:winexe /optimize+ "/out:$app" "/reference:$fw\WPF\PresentationFramework.dll" "/reference:$fw\WPF\PresentationCore.dll" "/reference:$fw\WPF\WindowsBase.dll" "/reference:$fw\System.Xaml.dll" /reference:System.Management.dll /reference:System.Web.Extensions.dll .\ChestIsland.cs .\ReleaseUpdate.cs
    if ($LASTEXITCODE) { throw 'Chest Island compilation failed.' }
    $setup = Join-Path $OutputDirectory 'Chest-Island-Setup.exe'
    & $csc /nologo /target:winexe /optimize+ "/out:$setup" "/resource:$app,Chest Island.exe" '/resource:ChestIsland.xaml,ChestIsland.xaml' '/resource:Live.ps1,Live.ps1' '/resource:Overlay.ps1,Overlay.ps1' /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:Microsoft.CSharp.dll .\Setup.cs
    if ($LASTEXITCODE) { throw 'Installer compilation failed.' }
    Get-Item $setup
} finally { Pop-Location }
