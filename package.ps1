param([Parameter(Mandatory = $true)][string]$Version)

$ErrorActionPreference = 'Stop'
$executable = Join-Path $PSScriptRoot 'CodexQuotaBar.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw 'Build CodexQuotaBar.exe before packaging.' }

$distribution = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $distribution -Force | Out-Null
$archiveName = 'CodexQuotaBar-windows-x64.zip'
$archivePath = Join-Path $distribution $archiveName
if (Test-Path -LiteralPath $archivePath) { Remove-Item -LiteralPath $archivePath }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($archivePath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($name in @('CodexQuotaBar.exe', 'README.md', 'README.zh-CN.md', 'LICENSE', 'create-shortcut.ps1')) {
        $path = Join-Path $PSScriptRoot $name
        if (-not (Test-Path -LiteralPath $path)) { throw "Missing package file: $name" }
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive, $path, $name, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally { $archive.Dispose() }

$checksumPath = Join-Path $distribution "SHA256SUMS-v$Version.txt"
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksumPath -Encoding ASCII -Value "$hash  $archiveName"
Write-Output $archivePath
Write-Output $checksumPath
