param(
    [string]$Output = (Join-Path (Split-Path $PSScriptRoot -Parent) '.local/danmaku-sample'),
    [ValidateRange(10,600)][int]$Seconds = 240
)
$ErrorActionPreference = 'Stop'
$encoder = (Get-Command ffmpeg -ErrorAction Stop).Source
New-Item -ItemType Directory -Path $Output -Force | Out-Null
$video = Join-Path $Output 'smooth.mkv'
& $encoder -hide_banner -loglevel error -f lavfi -i "color=c=0x202028:s=1280x720:r=60:d=$Seconds" -an -c:v libx264 -preset ultrafast -crf 35 -pix_fmt yuv420p -y $video
if ($LASTEXITCODE -ne 0) { throw 'Could not create the development video.' }
$header = @'
[Script Info]
Title: danmaku settings sample
ScriptType: v4.00+
PlayResX: 1280
PlayResY: 720
[V4+ Styles]
Format: Name,Fontname,Fontsize,PrimaryColour,SecondaryColour,OutlineColour,BackColour,Bold,Italic,Underline,StrikeOut,ScaleX,ScaleY,Spacing,Angle,BorderStyle,Outline,Shadow,Alignment,MarginL,MarginR,MarginV,Encoding
Style: Default,Microsoft YaHei UI,36,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,-1,0,0,0,100,100,0,0,1,1,0,7,0,0,0,1
[Events]
Format: Layer,Start,End,Style,Name,MarginL,MarginR,MarginV,Effect,Text
'@
function Time([double]$Value) { $span = [TimeSpan]::FromSeconds($Value); return ('{0}:{1:00}:{2:00}.{3:00}' -f [int][Math]::Floor($span.TotalHours),$span.Minutes,$span.Seconds,[int]($span.Milliseconds/10)) }
$text = [Text.StringBuilder]::new($header.TrimEnd()+"`n")
for ($i=0; $i -lt $Seconds*4; $i++) {
    $start = $i/4.0
    $row = 30+($i%16)*38
    $tags = if ($i%12 -eq 0) { '\an8\pos(640,650)' } elseif ($i%12 -eq 1) { "\an7\move(-320,$row,1280,$row)" } else { "\an7\move(1280,$row,-320,$row)" }
    $color = if ($i%3 -eq 0) { '\1c&H00CCFF&' } else { '' }
    [void]$text.AppendLine(('Dialogue: 0,{0},{1},Default,,0,0,0,,{{{2}{3}}}SMOOTH COMMENT {4:0000}' -f (Time $start),(Time ($start+8)),$tags,$color,$i))
}
[IO.File]::WriteAllText((Join-Path $Output 'smooth.ass'),$text.ToString(),[Text.UTF8Encoding]::new($false))
Write-Output "Created 60 fps video and ordinary ASS comments: $Output"
