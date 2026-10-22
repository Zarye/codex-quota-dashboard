$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references = @('System.dll','System.Core.dll','System.Net.Http.dll','System.Xaml.dll','System.Web.Extensions.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','WPF\UIAutomationTypes.dll')
$compilerArgs = @('/nologo','/target:winexe','/optimize+','/platform:x64',('/out:' + (Join-Path $PSScriptRoot 'CodexQuotaMini.exe')))
foreach ($reference in $references) { $compilerArgs += '/reference:' + (Join-Path $framework $reference) }
$compilerArgs += Join-Path $PSScriptRoot 'QuotaMini.cs'
$compilerArgs += Join-Path $PSScriptRoot 'Companion.cs'
$compilerArgs += Join-Path $PSScriptRoot 'ResetRadar.cs'
$compilerArgs += Join-Path $PSScriptRoot 'ResetCredits.cs'
& (Join-Path $framework 'csc.exe') @compilerArgs
if ($LASTEXITCODE -ne 0) { throw '额度小窗编译失败' }
