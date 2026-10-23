param([switch]$Rebuild)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$lock = (Get-Content (Join-Path $PSScriptRoot 'dependencies.lock.json') -Raw | ConvertFrom-Json).libass
$cache = Join-Path $repo '.local/dependencies'
$root = Join-Path $cache 'libass'
$source = Join-Path $root "libass-$($lock.version)"
$output = Join-Path $repo 'third_party/libass/win-x64'
New-Item -ItemType Directory -Path $cache,$root,$output -Force | Out-Null
foreach ($item in $lock.sources) {
    $archive = Join-Path $cache $item.archive
    if (-not (Test-Path -LiteralPath $archive)) { Invoke-WebRequest -Uri $item.url -OutFile $archive }
    if ((Get-FileHash -LiteralPath $archive).Hash -ne $item.sha256) { throw "Source checksum mismatch: $($item.name)" }
    $destination = if ($item.name -eq 'libass') { $source } else { Join-Path $source "subprojects/$($item.name -replace '^freetype$','freetype2')" }
    if (-not (Test-Path -LiteralPath (Join-Path $destination 'meson.build'))) {
        # Upstream developer-note symlinks are unrelated to the build and cannot
        # be created by Windows tar without symlink privileges.
        & tar.exe -xf $archive -C $root '--exclude=*/CLAUDE.md'
        if ($LASTEXITCODE -ne 0) { throw 'Source extraction failed.' }
        $extracted = Join-Path $root $item.directory
        if ($item.name -ne 'libass') {
            New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
            Copy-Item -LiteralPath $extracted -Destination $destination -Recurse
        }
    }
}
$build = Join-Path $root 'out'
$stamp = Join-Path $build 'bilibili-options.sha256'
$optionFile = Join-Path $PSScriptRoot 'libass-options.json'
$hash = (Get-FileHash -LiteralPath $optionFile).Hash + (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'dependencies.lock.json')).Hash + (Get-FileHash -LiteralPath $PSCommandPath).Hash
$same = (Test-Path -LiteralPath $stamp) -and ((Get-Content -LiteralPath $stamp -Raw).Trim() -eq $hash)
# UTF-8 is the only input encoding used by the bridge. Avoid an unpinned iconv
# from the developer's toolchain being linked by libass's optional autodetection.
$mesonFile=Join-Path $source 'meson.build'
$mesonText=[IO.File]::ReadAllText($mesonFile).Replace("iconv_dep = dependency('iconv', required: false)","iconv_dep = disabler()")
[IO.File]::WriteAllText($mesonFile,$mesonText,(New-Object Text.UTF8Encoding($false)))
if ($Rebuild -or -not $same -or -not (Test-Path -LiteralPath (Join-Path $output 'libass-9.dll'))) {
    $pythonTools = Join-Path $repo '.local/tools/python'
    if (-not (Test-Path (Join-Path $pythonTools 'mesonbuild/mesonmain.py'))) {
        & python -m pip install --target $pythonTools "meson==$($lock.meson)" "ninja==$($lock.ninja)" --disable-pip-version-check
        if ($LASTEXITCODE -ne 0) { throw 'Cannot prepare Meson and Ninja.' }
    }
    $oldPythonPath=$env:PYTHONPATH; $oldNinja=$env:NINJA
    try {
        $env:PYTHONPATH=$pythonTools; $env:NINJA=Join-Path $pythonTools 'bin/ninja.exe'
        $options = Get-Content -LiteralPath $optionFile -Raw | ConvertFrom-Json
        if (-not (Test-Path -LiteralPath (Join-Path $build 'build.ninja'))) {
            & python -m mesonbuild.mesonmain setup $build $source @options
        } else { & python -m mesonbuild.mesonmain setup --reconfigure $build $source @options }
        if ($LASTEXITCODE -ne 0) { throw 'libass configuration failed.' }
        & python -m mesonbuild.mesonmain compile -C $build -j 4
        if ($LASTEXITCODE -ne 0) { throw 'libass compilation failed.' }
        Copy-Item -LiteralPath (Join-Path $build 'libass/libass-9.dll') -Destination $output -Force
        Copy-Item -LiteralPath (Join-Path $build 'libass/libass.dll.a') -Destination $output -Force
        $hash | Set-Content -LiteralPath $stamp -Encoding ASCII
    } finally { $env:PYTHONPATH=$oldPythonPath; $env:NINJA=$oldNinja }
}
Copy-Item -LiteralPath (Join-Path $source 'COPYING') -Destination (Join-Path $repo 'LICENSES/libass-ISC.txt') -Force
Copy-Item -LiteralPath (Join-Path $source 'subprojects/freetype2/docs/FTL.TXT') -Destination (Join-Path $repo 'LICENSES/FreeType-FTL.txt') -Force
Copy-Item -LiteralPath (Join-Path $source 'subprojects/harfbuzz/COPYING') -Destination (Join-Path $repo 'LICENSES/HarfBuzz-MIT.txt') -Force
Copy-Item -LiteralPath (Join-Path $source 'subprojects/harfbuzz/src/ms-use/COPYING') -Destination (Join-Path $repo 'LICENSES/HarfBuzz-Microsoft-MIT.txt') -Force
Copy-Item -LiteralPath (Join-Path $source 'subprojects/fribidi/COPYING') -Destination (Join-Path $repo 'LICENSES/FriBidi-LGPL-2.1.txt') -Force
Write-Output 'Prepared libass shared renderer and license notices.'
