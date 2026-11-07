# README GIF'i ve LinkedIn videosunu üretir.
# Önce kareleri oluştur:  $env:LINGORA_MEDIA = (Resolve-Path docs\media).Path; dotnet test --filter MediaCapture
# Sonra:                   powershell -File docs\media\make-media.ps1
$ErrorActionPreference = 'Stop'
$media = $PSScriptRoot
$frames = Join-Path $media 'frames'
$bg = '0x0B1120'

# 1) README için demo GIF (820 px genişlik, 10 kare/sn; boyut küçük kalsın diye renk az ve titreşimsiz)
ffmpeg -y -loglevel error -framerate 15 -i "$frames\f%04d.png" `
    -vf "fps=10,scale=820:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=96:stats_mode=diff[p];[b][p]paletteuse=dither=none:diff_mode=rectangle" `
    -loop 0 "$media\demo.gif"

# 2) LinkedIn için MP4 (1920x1080): demo + sözlük, hedef ve yardım ekranları
$fit = "scale=1920:1080:force_original_aspect_ratio=decrease:flags=lanczos,pad=1920:1080:(ow-iw)/2:(oh-ih)/2:color=$bg,setsar=1,fps=30,format=yuv420p"
$tmp = Join-Path $env:TEMP 'lingora-media'
New-Item -ItemType Directory -Force $tmp | Out-Null
ffmpeg -y -loglevel error -framerate 15 -i "$frames\f%04d.png" -vf $fit -c:v libx264 -crf 18 "$tmp\0.mp4"
$i = 1
foreach ($still in '07-ders-notu.png', '03-sozluk.png', '04-hedef.png', '05-yardim.png') {
    ffmpeg -y -loglevel error -loop 1 -t 3 -i "$media\$still" -vf "$fit,fade=in:0:8" -c:v libx264 -crf 18 "$tmp\$i.mp4"
    $i++
}
$list = (0..4 | ForEach-Object { "file '$tmp\$_.mp4'" }) -join "`n"
[IO.File]::WriteAllText("$tmp\list.txt", $list)
ffmpeg -y -loglevel error -f concat -safe 0 -i "$tmp\list.txt" -c copy -movflags +faststart "$media\lingora-demo.mp4"
Remove-Item $tmp -Recurse -Force
Get-ChildItem $media -Include *.gif, *.mp4 -File -Recurse | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 2) } }
