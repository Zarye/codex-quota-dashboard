param(
    [string]$OutputDirectory = $PSScriptRoot,
    [switch]$Package
)
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$outputRoot = (Resolve-Path -LiteralPath $OutputDirectory).Path
$references = @('System.dll','System.Core.dll','System.Net.Http.dll','System.Xaml.dll','System.Web.Extensions.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','WPF\UIAutomationTypes.dll')
$compilerArgs = @('/nologo','/target:winexe','/optimize+','/platform:x64','/codepage:65001',('/out:' + (Join-Path $outputRoot 'CodexQuotaMini.exe')))
foreach ($reference in $references) { $compilerArgs += '/reference:' + (Join-Path $framework $reference) }
foreach ($source in @('QuotaMini.cs','Companion.cs','ResetRadar.cs','ResetCredits.cs','LocalRadar.cs','PublicX.cs','XMessageCollector.cs')) { $compilerArgs += Join-Path $PSScriptRoot $source }
& (Join-Path $framework 'csc.exe') @compilerArgs
if ($LASTEXITCODE -ne 0) { throw '额度小窗编译失败' }
$utf8 = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $outputRoot 'CodexQuotaMini.exe.config'), '<?xml version="1.0"?><configuration><startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.6.2" /></startup></configuration>', $utf8)
if ($Package) {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $distribution = Join-Path $PSScriptRoot 'dist'
    New-Item -ItemType Directory -Path $distribution -Force | Out-Null
    $zipPath = Join-Path $distribution 'CodexQuotaDashboard-Windows-x64.zip'
    $packageFiles = @('CodexQuotaMini.exe','CodexQuotaMini.exe.config')
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath }
    $zip = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $packageFiles) { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $outputRoot $file), $file) | Out-Null }
        foreach ($file in @('README.md','使用说明.txt')) { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $PSScriptRoot $file), $file) | Out-Null }
    } finally { $zip.Dispose() }
    $setupArgs = @('/nologo','/target:winexe','/platform:x64','/optimize+','/codepage:65001',('/out:' + (Join-Path $distribution 'CodexQuotaDashboard-Setup.exe')))
    foreach ($reference in @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','Microsoft.CSharp.dll')) { $setupArgs += '/reference:' + (Join-Path $framework $reference) }
    $setupArgs += '/resource:' + $zipPath + ',payload.zip'
    $setupArgs += Join-Path $PSScriptRoot 'Installer.cs'
    & (Join-Path $framework 'csc.exe') @setupArgs
    if ($LASTEXITCODE -ne 0) { throw '安装包编译失败' }
    Write-Output (Join-Path $distribution 'CodexQuotaDashboard-Setup.exe')
}
