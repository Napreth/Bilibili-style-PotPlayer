param(
    [ValidateSet('push','pull_request','workflow_dispatch')][string]$Event = 'push',
    [string]$Tag,
    [switch]$Offline,
    [switch]$List
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$state = Join-Path $repo '.local/act'
$act = (Get-Command act -ErrorAction Stop).Source
# act inherits PowerShell 7's module paths without the filtering performed when
# PowerShell itself starts Windows PowerShell. Keep the runner's base paths native;
# pwsh adds its own built-in modules when a pwsh step starts.
$nativeModules = @(
    (Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/Modules'),
    (Join-Path $env:ProgramFiles 'WindowsPowerShell/Modules')
) -join [IO.Path]::PathSeparator
# act defaults to run ID 1; full local builds also need fresh immutable cache keys.
$runId = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
$arguments = @(
    $Event,'--strict','--directory',$repo,'--job','windows',
    '--platform','windows-2025=-self-hosted',
    '--workflows',(Join-Path $repo '.github/workflows/ci.yml'),
    '--action-cache-path',(Join-Path $state 'actions'),
    '--cache-server-path',(Join-Path $state 'cache'),
    '--artifact-server-path',(Join-Path $state 'artifacts'),
    '--cache-server-addr','127.0.0.1','--artifact-server-addr','127.0.0.1',
    '--env',"PSModulePath=$nativeModules",
    '--env',"GITHUB_RUN_ID=$runId",
    '--env',('ACT_ARTIFACT_DIR='+(Join-Path $state 'artifacts')),
    '--env-file','NUL','--secret-file','NUL','--var-file','NUL','--input-file','NUL'
)
if ($Offline) { $arguments += '--action-offline-mode' }
if ($List) { $arguments += '--list' }
$eventFile = $null
try {
    if ($Tag) {
        if ($Event -ne 'push') { throw 'Tag simulation requires the push event.' }
        & git -C $repo check-ref-format "refs/tags/$Tag"
        if ($LASTEXITCODE -ne 0) { throw 'Invalid tag name.' }
        New-Item -ItemType Directory -Path $state -Force | Out-Null
        $eventFile = Join-Path $state ('tag-event-'+[guid]::NewGuid().ToString('N')+'.json')
        $eventJson = @{ref="refs/tags/$Tag"} | ConvertTo-Json
        [IO.File]::WriteAllText($eventFile,$eventJson,(New-Object Text.UTF8Encoding($false)))
        $arguments += @('--eventpath',$eventFile)
    }
    & $act @arguments
    if ($LASTEXITCODE -ne 0) { throw "Local CI failed with exit code $LASTEXITCODE." }
} finally {
    if ($eventFile -and (Test-Path -LiteralPath $eventFile)) { Remove-Item -LiteralPath $eventFile -Force }
}
