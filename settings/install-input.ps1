param(
    [switch]$UseConfiguredPlayer,
    [string]$PlayerPath
)
$ErrorActionPreference = 'Stop'
$repoPath = Split-Path $PSScriptRoot -Parent
$profile = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'input-profile.json') -Raw | ConvertFrom-Json
$nativePath = 'Software\DAUM\PotPlayerMini64\Settings'
$runPath = 'Software\Microsoft\Windows\CurrentVersion\Run'
$runName = 'BilibiliPotPlayerInput'
$shortcutPath = 'Software\DAUM\PotPlayerMini64\Main ShortCutList'
$localPath = Join-Path $PSScriptRoot '.local'
$legacyRuntimePath = Join-Path $localPath 'runtime'
$backupPath = Join-Path $localPath 'original-input.json'
$players = @(Get-Process -Name PotPlayerMini64,PotPlayer64 -ErrorAction SilentlyContinue)
$playerRunning = $players.Count -gt 0
if (-not $PlayerPath -and $playerRunning) { $PlayerPath = $players[0].Path }
if (-not $PlayerPath) {
    $candidates = @('C:\Software\Media\PotPlayer\PotPlayerMini64.exe', (Join-Path $env:ProgramFiles 'DAUM\PotPlayer\PotPlayerMini64.exe'))
    $PlayerPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $PlayerPath -or -not (Test-Path -LiteralPath $PlayerPath) -or (Split-Path $PlayerPath -Leaf) -notin 'PotPlayerMini64.exe','PotPlayer64.exe') {
    throw 'Specify the installed 64-bit player with -PlayerPath.'
}
$PlayerPath = (Resolve-Path -LiteralPath $PlayerPath).Path
$playerFolder = Split-Path $PlayerPath -Parent
$runtimePath = Join-Path $playerFolder 'Extension\BilibiliInput'
$layoutDll = Join-Path $runtimePath 'PlayerLayout.dll'
$layoutSource = Join-Path $repoPath 'plugin\InputBridge\native\PlayerLayout.dll'
$layoutChanged = -not (Test-Path -LiteralPath $layoutDll) -or (Get-FileHash -LiteralPath $layoutDll).Hash -ne (Get-FileHash -LiteralPath $layoutSource).Hash
if ($playerRunning -and (Test-Path -LiteralPath $layoutDll) -and $layoutChanged) {
    throw 'Close PotPlayer before updating its window layout module.'
}
$bootstrapPath = Join-Path $playerFolder 'Extension\Media\PlayParse\MediaPlayParse - BilibiliInput.as'
if ((Test-Path -LiteralPath $bootstrapPath) -and -not (Test-Path -LiteralPath $backupPath)) {
    throw 'The input extension already exists without a restore backup.'
}
if ($playerRunning -and -not $UseConfiguredPlayer) {
    throw 'Close PotPlayer before applying native settings. Playback is never forcibly closed.'
}
$nativeKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($nativePath)
$runKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($runPath)
try {
    if ($playerRunning) {
        # This mode only installs the bridge after the native options were applied in the UI.
        foreach ($required in @{DisableShortKey=0;UseMediaKey=0;PlayUseKeyFrame=0}.GetEnumerator()) {
            # PotPlayer may omit its enabled-default-shortcuts value when saving.
            $nativeDefault = if ($required.Key -eq 'DisableShortKey') { 0 } else { -1 }
            if ($nativeKey.GetValue($required.Key, $nativeDefault) -ne $required.Value) {
                throw "Native option '$($required.Key)' has not been applied in the running player. Close PotPlayer and rerun without -UseConfiguredPlayer."
            }
        }
        if ($nativeKey.GetValue('MouseRightClick', -1) -notin 22,23) {
            throw 'Clear the native right-click action first, or close PotPlayer and rerun.'
        }
        $shortKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($shortcutPath)
        if ($shortKey) {
            try { if ($shortKey.GetValueNames().Count) { throw 'Close PotPlayer and rerun to clear native custom shortcuts.' } }
            finally { $shortKey.Dispose() }
        }
    }
    & (Join-Path $repoPath 'plugin\InputBridge\build.ps1') -Test
    New-Item -ItemType Directory -Path $localPath -Force | Out-Null
    New-Item -ItemType Directory -Path $runtimePath -Force | Out-Null
    if (-not (Test-Path -LiteralPath $backupPath)) {
        $values = @($profile.Native.PSObject.Properties | ForEach-Object {
            $exists = $nativeKey.GetValueNames() -contains $_.Name
            [ordered]@{Name=$_.Name;Exists=$exists;Kind=if($exists){$nativeKey.GetValueKind($_.Name).ToString()}else{'DWord'};Value=if($exists){$nativeKey.GetValue($_.Name)}else{$null}}
        })
        $shortcuts = @()
        $shortKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($shortcutPath)
        if ($shortKey) {
            try { $shortcuts = @($shortKey.GetValueNames() | ForEach-Object { [ordered]@{Name=$_;Kind=$shortKey.GetValueKind($_).ToString();Value=$shortKey.GetValue($_)} }) }
            finally { $shortKey.Dispose() }
        }
        [ordered]@{SchemaVersion=1;RegistryPath=$nativePath;Values=$values;Shortcuts=$shortcuts;RunExists=($runKey.GetValueNames() -contains $runName);RunValue=$runKey.GetValue($runName)} |
            ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $backupPath -Encoding UTF8
    }
    $backup = Get-Content -LiteralPath $backupPath -Raw | ConvertFrom-Json
    # New settings added by later releases retain their own pre-change value too.
    foreach ($value in $profile.Native.PSObject.Properties) {
        if ($backup.Values.Name -contains $value.Name) { continue }
        $exists = $nativeKey.GetValueNames() -contains $value.Name
        $backup.Values += [pscustomobject][ordered]@{Name=$value.Name;Exists=$exists;Kind=if($exists){$nativeKey.GetValueKind($value.Name).ToString()}else{'DWord'};Value=if($exists){$nativeKey.GetValue($value.Name)}else{$null}}
    }
    if ($backup.RuntimePath -and $backup.RuntimePath -ne $runtimePath) { throw 'Restore the previous player installation before changing -PlayerPath.' }
    $backup | Add-Member -MemberType NoteProperty -Name RuntimePath -Value $runtimePath -Force
    $backup | Add-Member -MemberType NoteProperty -Name BootstrapPath -Value $bootstrapPath -Force
    $backup | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $backupPath -Encoding UTF8
    $runtimeExe = Join-Path $runtimePath 'BilibiliInput.exe'
    $oldExe = @($runtimeExe, (Join-Path $legacyRuntimePath 'BilibiliInput.exe')) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if ($oldExe) {
        Start-Process -FilePath $oldExe -ArgumentList '--stop' -WindowStyle Hidden -Wait
        $deadline = [DateTime]::UtcNow.AddSeconds(4)
        do {
            $oldInstances = @(Get-Process -Name BilibiliInput -ErrorAction SilentlyContinue | Where-Object {$_.Path -in $runtimeExe,(Join-Path $legacyRuntimePath 'BilibiliInput.exe')})
            if ($oldInstances.Count -eq 0) { break }
            Start-Sleep -Milliseconds 100
        } while ([DateTime]::UtcNow -lt $deadline)
        if ($oldInstances.Count -gt 0) { throw 'The old input bridge did not shut down cleanly.' }
    }
    Copy-Item -LiteralPath (Join-Path $repoPath 'plugin\InputBridge\bin\BilibiliInput.exe') -Destination $runtimeExe -Force
    if ($layoutChanged) { Copy-Item -LiteralPath $layoutSource -Destination $layoutDll -Force }
    Copy-Item -LiteralPath (Join-Path $repoPath 'plugin\InputBridge\bin\MediaInfo.dll') -Destination $runtimePath -Force
    Copy-Item -LiteralPath (Join-Path $repoPath 'plugin\InputBridge\bin\MediaInfo.License.html') -Destination $runtimePath -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'input-profile.json') -Destination $runtimePath -Force
    New-Item -ItemType Directory -Path (Split-Path $bootstrapPath -Parent) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoPath 'plugin\InputBridge\MediaPlayParse - BilibiliInput.as') -Destination $bootstrapPath -Force
    if (-not $playerRunning) {
        foreach ($value in $profile.Native.PSObject.Properties) {
            $nativeKey.SetValue($value.Name, [int]$value.Value, [Microsoft.Win32.RegistryValueKind]::DWord)
        }
        $shortKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($shortcutPath, $true)
        if ($shortKey) { try { foreach ($name in $shortKey.GetValueNames()) { $shortKey.DeleteValue($name) } } finally { $shortKey.Dispose() } }
    }
    $runKey.DeleteValue($runName, $false)
    if ($playerRunning) {
        Start-Process -FilePath $runtimeExe -WorkingDirectory $runtimePath -WindowStyle Hidden
        Start-Sleep -Milliseconds 500
        if (-not @(Get-Process -Name BilibiliInput -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $runtimeExe}).Count) {
            throw "Input bridge failed to start. Check $runtimePath\startup-error.txt"
        }
    }
    Write-Output "Player-started input bridge installed: $runtimeExe"
    Write-Output "Restore backup: $backupPath"
}
finally { $nativeKey.Dispose(); $runKey.Dispose() }
