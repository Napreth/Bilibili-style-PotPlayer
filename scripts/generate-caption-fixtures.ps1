param([string]$FFmpeg = 'ffmpeg.exe')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$scratch = Join-Path $repo '.local/caption-fixtures'
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
$srt = "1`n00:00:01,250 --> 00:00:03,750`n你好，字幕`n`n2`n00:00:06,000 --> 00:00:08,000`nSecond cue`n"
$ass = @'
[Script Info]
ScriptType: v4.00+
[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: Default,Arial,20,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,1,0,2,10,10,10,1
[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
Dialogue: 0,0:00:02.00,0:00:05.00,Default,,0,0,0,,日本語テキスト
'@
[IO.File]::WriteAllText((Join-Path $scratch 'first.srt'),$srt,(New-Object Text.UTF8Encoding($false)))
[IO.File]::WriteAllText((Join-Path $scratch 'second.ass'),$ass,(New-Object Text.UTF8Encoding($false)))
& $FFmpeg -nostdin -hide_banner -loglevel error -y -i "$scratch/first.srt" -i "$scratch/second.ass" -map 0:s -map 1:s -c:s copy "$repo/tests/fixtures/text-tracks.mkv"
if ($LASTEXITCODE -ne 0) { throw 'Matroska fixture generation failed.' }
& $FFmpeg -nostdin -hide_banner -loglevel error -y -i "$scratch/first.srt" -map 0:s -c:s mov_text "$repo/tests/fixtures/text-track.mp4"
if ($LASTEXITCODE -ne 0) { throw 'MP4 fixture generation failed.' }
& $FFmpeg -nostdin -hide_banner -loglevel error -y -i "$repo/tests/fixtures/classification.ass" -map 0:s -c:s copy "$repo/tests/fixtures/classification.mkv"
if ($LASTEXITCODE -ne 0) { throw 'Mixed ASS classification fixture generation failed.' }
