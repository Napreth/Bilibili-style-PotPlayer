$ErrorActionPreference = 'Stop'
if (@(Get-Process -Name PotPlayerMini64,PotPlayer64 -ErrorAction SilentlyContinue).Count) {
    throw 'Close PotPlayer before restoring settings. Playback is never forcibly closed.'
}
$backupPath = Join-Path $PSScriptRoot '.local\original-input.json'
if (-not (Test-Path -LiteralPath $backupPath)) { throw 'Original input settings backup not found.' }
$backup = Get-Content -LiteralPath $backupPath -Raw | ConvertFrom-Json
if ($backup.SchemaVersion -ne 1 -or $backup.RegistryPath -ne 'Software\DAUM\PotPlayerMini64\Settings') { throw 'Invalid backup.' }
$runtimeExe = if ($backup.RuntimePath) { Join-Path $backup.RuntimePath 'BilibiliInput.exe' } else { Join-Path $PSScriptRoot '.local\runtime\BilibiliInput.exe' }
if (Test-Path -LiteralPath $runtimeExe) { Start-Process -FilePath $runtimeExe -ArgumentList '--stop' -WindowStyle Hidden -Wait }
$nativeKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($backup.RegistryPath)
$runKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
try {
    foreach ($value in $backup.Values) {
        if ($value.Exists) { $nativeKey.SetValue($value.Name, $value.Value, [Microsoft.Win32.RegistryValueKind]::$($value.Kind)) }
        else { $nativeKey.DeleteValue($value.Name, $false) }
    }
    $shortKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\DAUM\PotPlayerMini64\Main ShortCutList', $true)
    if ($shortKey) {
        try {
            foreach ($name in $shortKey.GetValueNames()) { $shortKey.DeleteValue($name) }
            foreach ($value in $backup.Shortcuts) { $shortKey.SetValue($value.Name, $value.Value, [Microsoft.Win32.RegistryValueKind]::$($value.Kind)) }
        } finally { $shortKey.Dispose() }
    }
    elseif ($backup.Shortcuts.Count) {
        $shortKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\DAUM\PotPlayerMini64\Main ShortCutList')
        try { foreach ($value in $backup.Shortcuts) { $shortKey.SetValue($value.Name, $value.Value, [Microsoft.Win32.RegistryValueKind]::$($value.Kind)) } }
        finally { $shortKey.Dispose() }
    }
    if ($backup.RunExists) { $runKey.SetValue('BilibiliPotPlayerInput', $backup.RunValue, [Microsoft.Win32.RegistryValueKind]::String) }
    else { $runKey.DeleteValue('BilibiliPotPlayerInput', $false) }
    if ($backup.BootstrapPath -and (Split-Path $backup.BootstrapPath -Leaf) -eq 'MediaPlayParse - BilibiliInput.as') {
        if (Test-Path -LiteralPath $backup.BootstrapPath) { Remove-Item -LiteralPath $backup.BootstrapPath }
    }
    Write-Output 'Original input settings restored. The skin and other player settings were preserved.'
}
finally { $nativeKey.Dispose(); $runKey.Dispose() }
