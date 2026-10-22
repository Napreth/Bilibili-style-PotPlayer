$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$destination = Join-Path $repo '.local/tools/inno'
$compiler = Join-Path $destination 'ISCC.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $lock = (Get-Content (Join-Path $PSScriptRoot 'dependencies.lock.json') -Raw | ConvertFrom-Json).innoSetup
    $cache = Join-Path $repo '.local/dependencies'
    New-Item -ItemType Directory -Path $cache -Force | Out-Null
    $installer = Join-Path $cache "innosetup-$($lock.version).exe"
    if (-not (Test-Path -LiteralPath $installer)) { Invoke-WebRequest -Uri $lock.url -OutFile $installer }
    if ((Get-FileHash -LiteralPath $installer).Hash -ne $lock.sha256) { throw 'Inno Setup checksum mismatch.' }
    $signature = Get-AuthenticodeSignature -LiteralPath $installer
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Pyrsys') { throw 'Inno Setup signature validation failed.' }
    $process = Start-Process -FilePath $installer -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER','/PORTABLE=1','/NOICONS','/TASKS=""',('/DIR="'+$destination+'"')) -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw 'Portable compiler preparation failed.' }
}
return $compiler
