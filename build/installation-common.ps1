$ErrorActionPreference = 'Stop'
trap {
    $diagnosticRoot = Join-Path $env:LOCALAPPDATA 'BilibiliPotPlayer'
    [IO.Directory]::CreateDirectory($diagnosticRoot) | Out-Null
    $_ | Out-String | Set-Content -LiteralPath (Join-Path $diagnosticRoot 'installer-error.txt') -Encoding UTF8
    Write-Error $_ -ErrorAction Continue
    exit 1
}
function Set-RawRegistryValue($Key, $Value) {
    $data = switch ($Value.Kind.ToString()) {
        'DWord' { [int]$Value.Value; break }
        'QWord' { [long]$Value.Value; break }
        'Binary' { ,([byte[]]$Value.Value); break }
        'MultiString' { ,([string[]]$Value.Value); break }
        default { [string]$Value.Value }
    }
    $Key.SetValue($Value.Name,$data,[Microsoft.Win32.RegistryValueKind]::$($Value.Kind))
}
function Assert-PlayerClosed {
    if (@(Get-Process -Name PotPlayerMini64,PotPlayer64 -ErrorAction SilentlyContinue).Count) {
        throw 'Close all PotPlayer windows before installing or uninstalling. Playback is never forcibly stopped.'
    }
}
function Resolve-PlayerFile([string]$PlayerPath) {
    if (-not $PlayerPath) {
        $PlayerPath = @('C:\Software\Media\PotPlayer\PotPlayerMini64.exe', (Join-Path $env:ProgramFiles 'DAUM\PotPlayer\PotPlayerMini64.exe')) |
            Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
    if (-not $PlayerPath -or -not (Test-Path -LiteralPath $PlayerPath -PathType Leaf) -or (Split-Path $PlayerPath -Leaf) -notin @('PotPlayerMini64.exe','PotPlayer64.exe')) { throw 'Select an installed 64-bit PotPlayer executable.' }
    $PlayerPath = (Resolve-Path -LiteralPath $PlayerPath).Path
    $stream = [IO.File]::OpenRead($PlayerPath)
    $reader = New-Object IO.BinaryReader($stream)
    try {
        if ($reader.ReadUInt16() -ne 0x5A4D) { throw 'Invalid player executable.' }
        $stream.Position = 0x3C; $offset = $reader.ReadInt32()
        if ($offset -lt 64 -or $offset -gt $stream.Length - 6) { throw 'Invalid PE header.' }
        $stream.Position = $offset
        if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664) { throw '64-bit PotPlayer is required.' }
    } finally { $reader.Dispose() }
    return $PlayerPath
}
function Get-InstallationStateRoot {
    return Join-Path $env:LOCALAPPDATA 'BilibiliPotPlayer\installation'
}
function Resolve-InstallChild([string]$Root, [string]$Relative) {
    $absoluteRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\','/')
    $absolute = [IO.Path]::GetFullPath((Join-Path $absoluteRoot $Relative))
    if (-not $absolute.StartsWith($absoluteRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Installation path escaped its root.' }
    if ((Test-Path -LiteralPath $absolute) -and ((Get-Item -LiteralPath $absolute -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Installation files must not be links.' }
    # Do not traverse directory junctions when restoring or deleting files.
    $directory = Split-Path $absolute -Parent
    while ($directory -and $directory.Length -ge $absoluteRoot.Length) {
        if ((Test-Path -LiteralPath $directory) -and ((Get-Item -LiteralPath $directory -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Installation paths must not traverse directory links.' }
        $directory = Split-Path $directory -Parent
    }
    return $absolute
}
function Stop-BehaviorHost([string]$Runtime) {
    foreach ($name in @('BilibiliBehaviors.exe','BilibiliInput.exe')) {
        $executable = Join-Path $Runtime $name
        if (Test-Path -LiteralPath $executable) {
            $process = Start-Process -FilePath $executable -ArgumentList '--stop' -WindowStyle Hidden -PassThru -Wait
            if ($process.ExitCode -ne 0) { throw "Cannot stop $name cleanly." }
        }
    }
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        $running = @(Get-Process -Name BilibiliBehaviors,BilibiliInput -ErrorAction SilentlyContinue | Where-Object { $_.Path -and (Split-Path $_.Path -Parent) -eq $Runtime })
        if (-not $running.Count) { return }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'Behavior host did not exit cleanly.'
}
function Write-InstallJson([string]$Path, $Value) {
    $temporary = $Path + '.tmp'
    $Value | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $temporary -Encoding UTF8
    Move-Item -LiteralPath $temporary -Destination $Path -Force
}
function Read-InstallBackup([string]$Path) {
    $backup = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ($backup.SchemaVersion -ne 2 -or -not $backup.PlayerRoot -or
        ($backup.RegistryPath -ne 'Software\DAUM\PotPlayerMini64\Settings' -and $backup.RegistryPath -notlike 'Software\BilibiliBehaviors.Tests\*')) { throw 'Invalid installation backup.' }
    return $backup
}
function Restore-PlayerPreferences($Backup) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($Backup.RegistryPath)
    try {
        foreach ($value in $Backup.Values) {
            if ($value.Exists) { Set-RawRegistryValue $key $value }
            else { $key.DeleteValue($value.Name,$false) }
        }
    } finally { $key.Dispose() }
    $shortcutPath = $Backup.RegistryPath -replace '\\Settings$','\Main ShortCutList'
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($shortcutPath)
    try {
        foreach ($name in $key.GetValueNames()) { $key.DeleteValue($name) }
        foreach ($value in $Backup.Shortcuts) { Set-RawRegistryValue $key $value }
    } finally { $key.Dispose() }
}
