$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$testOutput = Join-Path $PSScriptRoot 'bin\tests'
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $testOutput
$arguments = @('/nologo','/target:exe','/platform:x64','/codepage:65001','/main:CodexQuotaMini.Tests.RadarTests',('/out:' + (Join-Path $testOutput 'RadarTests.exe')))
foreach ($reference in @('System.dll','System.Core.dll','System.Net.Http.dll','System.Xaml.dll','System.Web.Extensions.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','WPF\UIAutomationTypes.dll')) { $arguments += '/reference:' + (Join-Path $framework $reference) }
foreach ($source in @('QuotaMini.cs','Companion.cs','ResetRadar.cs','ResetCredits.cs','LocalRadar.cs','PublicX.cs','XMessageCollector.cs','tests\RadarTests.cs')) { $arguments += Join-Path $PSScriptRoot $source }
& (Join-Path $framework 'csc.exe') @arguments
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
& (Join-Path $testOutput 'RadarTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Radar tests failed' }
