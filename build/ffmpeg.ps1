param([switch]$Rebuild, [switch]$PrepareOnly, [switch]$CollectOnly, [string]$Bash, [string]$Make)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$lock = (Get-Content (Join-Path $PSScriptRoot 'dependencies.lock.json') -Raw | ConvertFrom-Json).ffmpeg
$cache = Join-Path $repo '.local/dependencies'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$archive = Join-Path $cache "ffmpeg-$($lock.version).tar.xz"
if (-not (Test-Path -LiteralPath $archive)) { Invoke-WebRequest -Uri $lock.url -OutFile $archive }
if ((Get-FileHash -LiteralPath $archive).Hash -ne $lock.sha256) { throw 'FFmpeg source checksum mismatch.' }
$source = Join-Path $cache "ffmpeg-$($lock.version)"
if (-not (Test-Path -LiteralPath (Join-Path $source 'configure'))) {
    & tar.exe -xf $archive -C $cache
    if ($LASTEXITCODE -ne 0) { throw 'Cannot extract FFmpeg source.' }
}
if ($PrepareOnly) { return }
$destination = Join-Path $repo 'third_party/FFmpeg/win-x64'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$configureHash = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'ffmpeg.sh')).Hash
$stamp = Join-Path $source '.bilibili-configure.sha256'
$sourceBuilt = Test-Path -LiteralPath (Join-Path $source 'libavformat/avformat-62.dll')
$sameConfiguration = (Test-Path -LiteralPath $stamp) -and ((Get-Content -LiteralPath $stamp -Raw).Trim() -eq $configureHash)
if (-not $CollectOnly -and ($Rebuild -or -not $sourceBuilt -or -not $sameConfiguration)) {
    if (-not $Bash) {
        $candidates = @()
        $gcc = Get-Command gcc.exe -ErrorAction SilentlyContinue
        if ($gcc) { $candidates += Join-Path (Split-Path (Split-Path (Split-Path $gcc.Source -Parent) -Parent) -Parent) 'usr/bin/bash.exe' }
        $git = Get-Command git.exe -ErrorAction SilentlyContinue
        if ($git) { $candidates += Join-Path (Split-Path (Split-Path $git.Source -Parent) -Parent) 'bin/bash.exe' }
        $Bash = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if (-not $Bash) { throw 'MSYS2 or Git Bash is required. Supply its executable using -Bash; WSL bash is not supported.' }
    }
    if (-not $Make) {
        $makeCommand = Get-Command make.exe,mingw32-make.exe -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $makeCommand) { throw 'GNU Make is required to build FFmpeg.' }
        $Make = $makeCommand.Source
    }
    $oldMake = $env:MAKE
    try {
        $env:MAKE = $Make.Replace('\','/')
        Push-Location $source
        & $Bash --noprofile --norc '../../../build/ffmpeg.sh'
        if ($LASTEXITCODE -ne 0) { throw 'FFmpeg shared-library build failed.' }
    } finally { Pop-Location; $env:MAKE = $oldMake }
}
foreach ($library in @('avcodec','avformat','avutil')) {
    $dll = Get-ChildItem -LiteralPath (Join-Path $source "lib$library") -Filter "$library-*.dll" | Select-Object -First 1
    if (-not $dll) { throw "FFmpeg library missing: $library" }
    Copy-Item -LiteralPath $dll.FullName -Destination $destination -Force
    Copy-Item -LiteralPath (Join-Path $source "lib$library/$library.lib") -Destination $destination -Force
}
Copy-Item -LiteralPath (Join-Path $source 'COPYING.LGPLv2.1') -Destination (Join-Path $repo 'LICENSES/LICENSE.LGPL-2.1.txt') -Force
Copy-Item -LiteralPath (Join-Path $source 'config.h') -Destination (Join-Path $repo 'third_party/FFmpeg/config.h') -Force
Copy-Item -LiteralPath (Join-Path $source 'ffbuild/config.mak') -Destination (Join-Path $repo 'third_party/FFmpeg/config.mak') -Force
$configureHash | Set-Content -LiteralPath $stamp -Encoding ASCII
Write-Output "Prepared LGPL FFmpeg libraries: $destination"
