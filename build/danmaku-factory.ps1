param([switch]$Rebuild, [switch]$DependenciesOnly, [switch]$BridgeOnly, [string]$Compiler = 'gcc')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$lock = (Get-Content (Join-Path $PSScriptRoot 'dependencies.lock.json') -Raw | ConvertFrom-Json).danmakuFactory
$source = Join-Path $repo 'third_party/DanmakuFactory'
if (-not (Test-Path (Join-Path $source 'src/CDanmakuFactory.h'))) {
    & git -C $repo submodule update --init --recursive -- third_party/DanmakuFactory
    if ($LASTEXITCODE -ne 0) { throw 'Cannot initialize DanmakuFactory submodule.' }
}
$revision = (& git -C $source rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $revision -ne $lock.commit) { throw 'DanmakuFactory checkout differs from dependencies.lock.json.' }
$cache = Join-Path $repo '.local/dependencies'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$prepared = Join-Path $cache 'danmaku-factory/win-x64'
$coreLibrary = Join-Path $prepared 'libDanmakuFactory.a'
$pcreLibrary = Join-Path $prepared 'libpcre2-8.a'
$bridge = Join-Path $repo 'src/Native/DanmakuConverter'
$core = Join-Path $source 'src'
if ($BridgeOnly -and ($Rebuild -or $DependenciesOnly)) { throw 'BridgeOnly cannot rebuild or prepare dependencies.' }
if (-not $BridgeOnly) {
    $archive = Join-Path $cache $lock.pcre2.archive
    if (-not (Test-Path -LiteralPath $archive)) { Invoke-WebRequest -Uri $lock.pcre2.url -OutFile $archive }
    if ((Get-FileHash -LiteralPath $archive).Hash -ne $lock.pcre2.sha256) { throw 'PCRE2 source checksum mismatch.' }
    $pcreRoot = Join-Path $cache 'pcre2'
    $pcreSource = Join-Path $pcreRoot "pcre2-$($lock.pcre2.version)"
    $pcreBuild = Join-Path $pcreRoot ("out-"+$lock.pcre2.version)
    New-Item -ItemType Directory -Path $pcreRoot,$pcreBuild -Force | Out-Null
    if (-not (Test-Path (Join-Path $pcreSource 'CMakeLists.txt'))) {
        & tar.exe -xf $archive -C $pcreRoot
        if ($LASTEXITCODE -ne 0) { throw 'PCRE2 source extraction failed.' }
    }
    if ($Rebuild -or -not (Test-Path (Join-Path $pcreBuild 'build.ninja'))) {
        $ninja = Join-Path $repo '.local/tools/python/bin/ninja.exe'
        if (-not (Test-Path -LiteralPath $ninja)) { $ninja = (Get-Command ninja -ErrorAction Stop).Source }
        $cc = (Get-Command $Compiler -ErrorAction Stop).Source
        & cmake -S $pcreSource -B $pcreBuild -G Ninja "-DCMAKE_MAKE_PROGRAM=$ninja" "-DCMAKE_C_COMPILER=$cc" -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=OFF -DPCRE2_BUILD_PCRE2_8=ON -DPCRE2_BUILD_PCRE2_16=OFF -DPCRE2_BUILD_PCRE2_32=OFF -DPCRE2_BUILD_PCRE2GREP=OFF -DPCRE2_BUILD_TESTS=OFF -DPCRE2_SUPPORT_JIT=OFF
        if ($LASTEXITCODE -ne 0) { throw 'PCRE2 configuration failed.' }
    }
    & cmake --build $pcreBuild --target pcre2-8-static --parallel 4
    if ($LASTEXITCODE -ne 0) { throw 'PCRE2 compilation failed.' }
    $library = @(Get-ChildItem -LiteralPath $pcreBuild -Filter '*pcre2-8*.a' -Recurse -File)
    if ($library.Count -ne 1) { throw 'Cannot find a unique PCRE2 static library.' }
    New-Item -ItemType Directory -Path $prepared -Force | Out-Null
    Copy-Item -LiteralPath $library[0].FullName -Destination $pcreLibrary -Force
    $files = @('AssFile/AssFile.c','AssFile/AssStringProcessing.c','List/DanmakuFactoryList.c','String/DanmakuFactoryString.c','FileUtil/FileUtil.c') | ForEach-Object { Join-Path $core $_ }
    $objects = @()
    foreach ($file in $files) {
        $object = Join-Path $pcreBuild (([IO.Path]::GetFileNameWithoutExtension($file))+'.o')
        & $Compiler -std=gnu11 -c -O2 -ffunction-sections -fdata-sections -DPCRE2_STATIC "-I$core" "-I$pcreBuild/interface" "-I$pcreSource/src" -o $object $file
        if ($LASTEXITCODE -ne 0) { throw 'DanmakuFactory core compilation failed.' }
        $objects += $object
    }
    $archiver = Join-Path (Split-Path (Get-Command $Compiler -ErrorAction Stop).Source -Parent) 'ar.exe'
    if (Test-Path -LiteralPath $coreLibrary) { Remove-Item -LiteralPath $coreLibrary -Force }
    & $archiver rcs $coreLibrary @objects
    if ($LASTEXITCODE -ne 0) { throw 'DanmakuFactory core archive failed.' }
    Copy-Item -LiteralPath (Join-Path $source 'LICENSE') -Destination (Join-Path $repo 'LICENSES/DanmakuFactory-MIT.txt') -Force
    Copy-Item -LiteralPath (Join-Path $pcreSource 'LICENCE.md') -Destination (Join-Path $repo 'LICENSES/PCRE2-BSD.txt') -Force
    # Snapshot the actual core sources used, including local edits for future development.
    $snapshot = Join-Path $cache 'DanmakuFactory-source.zip'
    Compress-Archive -LiteralPath (Join-Path $source 'src'),(Join-Path $source 'LICENSE'),(Join-Path $source 'xmake.lua') -DestinationPath $snapshot -Force
    [ordered]@{Repository=$lock.repository;Commit=$revision;LocalChanges=([bool](& git -C $source status --porcelain -- src LICENSE));SourceSHA256=(Get-FileHash -LiteralPath $snapshot).Hash} | ConvertTo-Json | Set-Content (Join-Path $cache 'DanmakuFactory-revision.json') -Encoding UTF8
}
if ($DependenciesOnly) { Write-Output 'Prepared DanmakuFactory and PCRE2 static libraries.'; return }
foreach ($library in @($coreLibrary,$pcreLibrary)) {
    if (-not (Test-Path -LiteralPath $library)) { throw 'DanmakuFactory dependencies are missing. Run build/danmaku-factory.ps1 first or restore the CI dependency cache.' }
}
& $Compiler -std=gnu11 -shared -O2 -s -static-libgcc -ffunction-sections -fdata-sections '-Wl,--gc-sections' '-Wl,--exclude-all-symbols' '-Wl,--no-insert-timestamp' "-I$core" -o "$bridge/BilibiliDanmakuFactory.dll" "$bridge/DanmakuConverter.c" $coreLibrary $pcreLibrary -lshell32 -luser32 -lm
if ($LASTEXITCODE -ne 0) { throw 'DanmakuFactory native bridge compilation failed.' }
Write-Output 'Built DanmakuFactory DLL from the pinned submodule.'
