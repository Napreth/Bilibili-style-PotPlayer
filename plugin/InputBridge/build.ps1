param([switch]$Test)
$ErrorActionPreference = 'Stop'
$repoPath = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$outputPath = Join-Path $PSScriptRoot 'bin'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { throw '64-bit .NET Framework C# compiler is required.' }
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$sourcePaths = @('InputLogic.cs','ControlLayout.cs','ChildSurfaceHost.cs','BilibiliInput.cs','VolumePopup.cs','SpeedUi.cs','StreamMetadata.cs','DolbyVisionProbe.cs','SphericalVideoProbe.cs','DolbyMark.cs','SvgGlyph.cs','NativeTrackAccess.cs','NativeMenuSnapshot.cs','NativeCaptionAccess.cs','TimedText.cs','CaptionCatalog.cs','TimedOverlay.cs','SubtitleUi.cs','StreamUi.cs','CameraControl.cs','ResponsiveControls.cs','PlaylistLayout.cs','PlayerControls.cs' | ForEach-Object {Join-Path $PSScriptRoot $_})
$references = @('/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll','/r:System.Xml.dll')
$svgResource = "/resource:$PSScriptRoot\assets\dolby-double-d.svg,BilibiliPotPlayer.DolbyDoubleD.svg"
$filterResources = @('scroll','fixed','color','advanced' | ForEach-Object {"/resource:$PSScriptRoot\assets\bili-filter-$_.svg,BilibiliPotPlayer.Filter.$_.svg"})
$manifest = "/win32manifest:$PSScriptRoot\app.manifest"
& $compilerPath /nologo /optimize+ /platform:x64 /target:winexe @references $svgResource @filterResources $manifest "/out:$outputPath\BilibiliInput.exe" @sourcePaths
if ($LASTEXITCODE -ne 0) { throw 'Input bridge compilation failed.' }
& $compilerPath /nologo /optimize+ /platform:x64 /target:exe /main:BridgeTests @references $svgResource @filterResources $manifest "/out:$outputPath\InputBridgeTests.exe" @sourcePaths (Join-Path $repoPath 'tests\InputBridgeTests.cs') (Join-Path $repoPath 'tests\DolbyVisionProbeChecks.cs') (Join-Path $repoPath 'tests\SphericalVideoProbeChecks.cs') (Join-Path $repoPath 'tests\TimedTextChecks.cs')
if ($LASTEXITCODE -ne 0) { throw 'Input tests compilation failed.' }
Copy-Item -LiteralPath (Join-Path $repoPath 'settings\input-profile.json') -Destination $outputPath -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'vendor\MediaInfo.dll') -Destination $outputPath -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'vendor\MediaInfo.License.html') -Destination $outputPath -Force
if ($Test) {
    & (Join-Path $outputPath 'InputBridgeTests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Input transition tests failed.' }
}
Write-Output "Built: $outputPath\BilibiliInput.exe"
