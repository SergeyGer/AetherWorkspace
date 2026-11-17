#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Builds the verified hero MP4 and preview GIF from a local OBS capture (v1.1 AW-056).
.DESCRIPTION
  Reads the capture manifest and event manifest, generates captions from demo-scenes.json, and runs
  FFmpeg locally. Captions are keyed to measured event timestamps with the recording-start offset.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $CaptureManifest,
    [string] $OutputDirectory,
    [string] $ScenesFile = (Join-Path $PSScriptRoot 'demo-scenes.json'),
    [int] $MaxDurationSeconds = 95
)

$ErrorActionPreference = 'Stop'

function Get-BlackIntervals([string] $path) {
    $output = & ffmpeg -hide_banner -i $path -vf 'blackdetect=d=0.4:pix_th=0.10' -an -f null - 2>&1
    $intervals = @()
    foreach ($line in ($output | Select-String 'black_start:(?<s>[\d.]+) black_end:(?<e>[\d.]+)')) {
        $intervals += [pscustomobject]@{
            Start = [double]$line.Matches[0].Groups['s'].Value
            End = [double]$line.Matches[0].Groups['e'].Value
        }
    }
    return $intervals
}

$capture = Get-Content -LiteralPath $CaptureManifest -Raw | ConvertFrom-Json
$raw = (Resolve-Path -LiteralPath $capture.rawRecording).Path
$rawDuration = [double](& ffprobe -v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 $raw)
$blackIntervals = @(Get-BlackIntervals $raw)
$trimStart = 0.0
$trimEnd = $rawDuration
$leadingBlack = $blackIntervals | Where-Object { $_.Start -le 0.75 } | Sort-Object End -Descending | Select-Object -First 1
if ($leadingBlack) {
    # Leave a small margin after the leading black interval; never trim below the 80 s minimum.
    $trimStart = [Math]::Round([Math]::Min($leadingBlack.End + 0.2, [Math]::Max(0, $rawDuration - 60)), 1)
}
$trailingBlack = $blackIntervals | Where-Object { $_.End -ge ($rawDuration - 0.75) } | Sort-Object Start | Select-Object -First 1
if ($trailingBlack) { $trimEnd = [Math]::Round([Math]::Max($trimStart + 60, $trailingBlack.Start - 0.2), 1) }
$available = $trimEnd - $trimStart
if ($available -lt 60) {
    throw ("FAIL_MEDIA: only {0:0.0}s of usable recording after trimming leading/trailing black; capture at least 60s of visible content." -f $available)
}
$buildDuration = [Math]::Min([double]$MaxDurationSeconds, $available)
$events = Get-Content -LiteralPath $capture.eventsFile -Raw | ConvertFrom-Json
$scenes = Get-Content -LiteralPath $ScenesFile -Raw | ConvertFrom-Json
$output = if ($OutputDirectory) { $OutputDirectory } else { Split-Path -Parent $raw }
New-Item -ItemType Directory -Force -Path $output | Out-Null

$ffmpeg = Get-Command ffmpeg -ErrorAction SilentlyContinue
if (-not $ffmpeg) { throw 'FAIL_HARNESS: FFmpeg is not on PATH.' }

function ConvertTo-DateTimeOffsetValue($value) {
    # PowerShell 7.5+ ConvertFrom-Json auto-converts ISO strings to DateTime; parsing that again
    # with [DateTimeOffset]::Parse uses the current culture and fails on e.g. Russian Windows.
    if ($value -is [DateTimeOffset]) { return $value }
    if ($value -is [DateTime]) { return [DateTimeOffset]::new($value) }
    return [DateTimeOffset]::Parse(
        [string]$value,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Globalization.DateTimeStyles]::RoundtripKind)
}

$recordingStart = ConvertTo-DateTimeOffsetValue $capture.recordingStartedUtc
$captionById = @{}
foreach ($scene in $scenes.scenes) { $captionById[$scene.event] = $scene }


function ConvertTo-SrtTime([double] $seconds) {
    $ts = [TimeSpan]::FromSeconds([Math]::Max(0, $seconds))
    return ('{0:00}:{1:00}:{2:00},{3:000}' -f [int]$ts.TotalHours, $ts.Minutes, $ts.Seconds, $ts.Milliseconds)
}

$srt = New-Object System.Collections.Generic.List[string]
$index = 1
foreach ($event in ($events.events | Sort-Object timestampUtc)) {
    $scene = $captionById[$event.name]
    if (-not $scene) { continue }
    $start = ((ConvertTo-DateTimeOffsetValue $event.timestampUtc) - $recordingStart).TotalSeconds - $trimStart
    if ($start -lt 0) { $start = 0 }
    $end = $start + [double]$scene.durationSeconds
    $srt.Add([string]$index)
    $srt.Add("$(ConvertTo-SrtTime $start) --> $(ConvertTo-SrtTime $end)")
    $srt.Add([string]$scene.caption)
    $srt.Add('')
    $index++
}

$srtPath = Join-Path $output 'captions.srt'
$srt | Set-Content -LiteralPath $srtPath -Encoding utf8
$mp4Path = Join-Path $output 'demo-hero.mp4'
$gifPath = Join-Path $output 'demo-preview.gif'
$filter = "scale=1920:1080:force_original_aspect_ratio=decrease,pad=1920:1080:(ow-iw)/2:(oh-ih)/2,subtitles=captions.srt:force_style='Fontname=DejaVu Sans,Fontsize=24,PrimaryColour=&H00FFFFFF,OutlineColour=&H80000000,Outline=1,Shadow=1'"
Push-Location $output
try {
    & ffmpeg -y -ss $trimStart -i $raw -t $buildDuration -vf $filter -r 30 -c:v libx264 -pix_fmt yuv420p -an $mp4Path
    if ($LASTEXITCODE -ne 0) { throw 'FAIL_MEDIA: FFmpeg MP4 build failed.' }
    $gifStart = 0.0
    $firstTicker = $events.events | Where-Object { $_.name -eq 'ticker-vega' } | Select-Object -First 1
    if ($firstTicker) { $gifStart = [Math]::Max(0, ((ConvertTo-DateTimeOffsetValue $firstTicker.timestampUtc) - $recordingStart).TotalSeconds - $trimStart - 2) }
    & ffmpeg -y -ss $gifStart -t 10 -i $mp4Path -vf 'fps=12,scale=960:-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse' -loop 0 $gifPath
    if ($LASTEXITCODE -ne 0) { throw 'FAIL_MEDIA: FFmpeg GIF build failed.' }
}
finally {
    Pop-Location
}

$result = [ordered]@{
    schemaVersion = '1.0'
    kind = 'windows11-demo-video'
    builtAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    capture = $capture
    sourceDurationSeconds = [Math]::Round($rawDuration, 2)
    trimStartSeconds = [Math]::Round($trimStart, 2)
    trimEndSeconds = [Math]::Round($trimEnd, 2)
    builtDurationSeconds = [Math]::Round($buildDuration, 2)
    detectedBlackIntervals = $blackIntervals
    mp4 = $mp4Path
    mp4Sha256 = (Get-FileHash -LiteralPath $mp4Path -Algorithm SHA256).Hash.ToLowerInvariant()
    gif = $gifPath
    gifSha256 = (Get-FileHash -LiteralPath $gifPath -Algorithm SHA256).Hash.ToLowerInvariant()
    captions = $srtPath
}
$resultPath = Join-Path $output 'video-build.json'
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resultPath -Encoding utf8
Write-Host "VIDEO BUILD OK: $resultPath"
Write-Host "mp4: $mp4Path"
Write-Host "gif: $gifPath"
exit 0
