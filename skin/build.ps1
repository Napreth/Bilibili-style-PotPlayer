param(
    [string]$SourcePath = (Join-Path $PSScriptRoot 'BilibiliLite'),
    [string]$OutputPath = (Join-Path $PSScriptRoot 'BilibiliLite.dsf')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$sourceDirectory = (Resolve-Path -LiteralPath $SourcePath).Path
$destinationPath = [System.IO.Path]::GetFullPath($OutputPath)
$tempArchivePath = $destinationPath + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
try {
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $sourceDirectory, $tempArchivePath,
        [System.IO.Compression.CompressionLevel]::Optimal, $false
    )
    Move-Item -LiteralPath $tempArchivePath -Destination $destinationPath -Force
    Get-Item -LiteralPath $destinationPath | Select-Object FullName, Length
} finally {
    if (Test-Path -LiteralPath $tempArchivePath) {
        Remove-Item -LiteralPath $tempArchivePath
    }
}