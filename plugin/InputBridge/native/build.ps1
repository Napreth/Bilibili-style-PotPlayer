param([string]$Compiler = 'gcc')
$ErrorActionPreference = 'Stop'
& $Compiler -shared -O2 -s -static-libgcc '-Wl,--no-insert-timestamp' -o "$PSScriptRoot\PlayerLayout.dll" "$PSScriptRoot\PlayerLayout.c" -luser32
if ($LASTEXITCODE -ne 0) { throw 'Player layout hook compilation failed (64-bit MinGW-w64 is required).' }
