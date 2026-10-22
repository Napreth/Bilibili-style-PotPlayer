param([string]$OutputPath)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $OutputPath) { $OutputPath = Join-Path $repo 'skin/BilibiliLite.dsf' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$base = Join-Path $repo 'skin/base/Default.dsf'
$manifest = Get-Content (Join-Path $repo 'skin/base/manifest.json') -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath $base).Hash -ne $manifest.sha256) { throw 'Default skin base checksum mismatch.' }
$staging = Join-Path $repo ('build/out/skin-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging -Force | Out-Null
[IO.Compression.ZipFile]::ExtractToDirectory($base,$staging)
Copy-Item -Path (Join-Path $repo 'skin/overrides/*') -Destination $staging -Force
$temporary = [IO.Path]::GetFullPath($OutputPath) + '.tmp'
if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
[IO.Compression.ZipFile]::CreateFromDirectory($staging,$temporary,[IO.Compression.CompressionLevel]::Optimal,$false)
Move-Item -LiteralPath $temporary -Destination $OutputPath -Force
# Every deletion is constrained to the newly created build staging directory.
$safeRoot = [IO.Path]::GetFullPath((Join-Path $repo 'build/out')).TrimEnd('\') + '\'
if (-not [IO.Path]::GetFullPath($staging).StartsWith($safeRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe skin staging path.' }
Remove-Item -LiteralPath $staging -Recurse
Get-Item -LiteralPath $OutputPath | Select-Object FullName,Length
