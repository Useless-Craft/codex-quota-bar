$ErrorActionPreference = 'Stop'
$quotaFramework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$quotaCompiler = Join-Path $quotaFramework 'csc.exe'
if (-not (Test-Path -LiteralPath $quotaCompiler)) {
    throw 'The 64-bit .NET Framework compiler was not found. Windows with .NET Framework 4.8 is required.'
}
$quotaReferences = @('System.dll', 'System.Core.dll', 'System.Net.Http.dll', 'System.Web.Extensions.dll', 'System.Xaml.dll') |
    ForEach-Object { '/reference:' + (Join-Path $quotaFramework $_) }
$quotaReferences += @('WindowsBase.dll', 'PresentationCore.dll', 'PresentationFramework.dll', 'UIAutomationClient.dll', 'UIAutomationTypes.dll') |
    ForEach-Object { '/reference:' + (Join-Path $quotaFramework ('WPF\' + $_)) }
& $quotaCompiler /nologo /target:winexe /platform:x64 /optimize+ @quotaReferences `
    ('/win32manifest:' + (Join-Path $PSScriptRoot 'quota.manifest')) `
    ('/out:' + (Join-Path $PSScriptRoot 'CodexQuotaBar.exe')) `
    (Join-Path $PSScriptRoot 'QuotaBar.cs')
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output (Join-Path $PSScriptRoot 'CodexQuotaBar.exe')
