param([Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if ($env:GITHUB_RUN_ID -notmatch '^\d+$') { throw 'A numeric CI run ID is required.' }
$root = [IO.Path]::GetFullPath($Destination).TrimEnd('\') + '\'
$output = [IO.Path]::GetFullPath((Join-Path $root $env:GITHUB_RUN_ID))
if (-not $output.StartsWith($root,[StringComparison]::OrdinalIgnoreCase)) { throw 'Artifact path escaped its destination.' }
$version = (Get-Content (Join-Path $repo 'build/version.txt') -Raw).Trim()
$installer = "dist/BilibiliBehaviors-$version-win-x64-setup.exe"
$files = @($installer,($installer+'.sha256'),'package/checksums.json')
$source = Join-Path $repo 'build/out'
$expected = (Get-Content (Join-Path $source ($installer+'.sha256')) -Raw).Split()[0]
if ((Get-FileHash (Join-Path $source $installer)).Hash -ne $expected) { throw 'Installer checksum mismatch before export.' }
foreach ($relative in $files) {
    $from = Join-Path $source $relative
    $to = Join-Path $output $relative
    New-Item -ItemType Directory -Path (Split-Path $to -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $from -Destination $to -Force
    if ((Get-FileHash -LiteralPath $to).Hash -ne (Get-FileHash -LiteralPath $from).Hash) { throw "Exported artifact differs: $relative" }
}
Write-Output "Exported $($files.Count) verified installer artifacts: $output"
