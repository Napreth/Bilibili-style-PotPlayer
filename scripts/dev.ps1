param([ValidateSet('test','status','ui-status','caption-preview')][string]$Action = 'test', [string]$OutputPath)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if ($Action -eq 'test') { & (Join-Path $repo 'build/managed.ps1') -Test; return }
$tests = Join-Path $repo 'build/out/runtime/BilibiliBehaviors.Tests.exe'
if ($Action -eq 'caption-preview') {
    if (-not $OutputPath) { $OutputPath = Join-Path $repo '.local/caption-preview' }
    & $tests '--caption-preview' $OutputPath
} else { & $tests "--$Action" }
if ($LASTEXITCODE -ne 0) { throw 'Developer command failed.' }
