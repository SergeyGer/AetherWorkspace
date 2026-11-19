#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Validates the generated hero MP4/GIF and emits a QA report (v1.1 AW-056).
.DESCRIPTION
  Checks codec, dimensions, pixel format, duration, GIF size, black frames and SHA-256. Publication
  still requires human review; this script never auto-publishes.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $VideoBuildManifest,
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
$build = Get-Content -LiteralPath $VideoBuildManifest -Raw | ConvertFrom-Json
$mp4 = (Resolve-Path -LiteralPath $build.mp4).Path
$gif = (Resolve-Path -LiteralPath $build.gif).Path
$ffprobe = Get-Command ffprobe -ErrorAction SilentlyContinue
if (-not $ffprobe) { throw 'FAIL_HARNESS: ffprobe is not on PATH.' }

$problems = New-Object System.Collections.Generic.List[string]
$probe = & ffprobe -v error -select_streams v:0 -show_entries stream=codec_name,width,height,pix_fmt,duration -of json $mp4 | ConvertFrom-Json
$video = $probe.streams | Select-Object -First 1
if (-not $video) { throw 'FAIL_MEDIA: no video stream found in the MP4.' }
if ($video.codec_name -ne 'h264') { $problems.Add("codec is $($video.codec_name), expected h264") }
if ($video.width -ne 1920 -or $video.height -ne 1080) { $problems.Add("dimensions are $($video.width)x$($video.height), expected 1920x1080") }
if ($video.pix_fmt -ne 'yuv420p') { $problems.Add("pixel format is $($video.pix_fmt), expected yuv420p") }
$duration = [double]$video.duration
if ($duration -lt 60 -or $duration -gt 95) { $problems.Add("MP4 duration is $duration s, expected 60-95 s") }

$audio = $null
if ($build.narration) {
    $audioProbe = & ffprobe -v error -select_streams a:0 -show_entries stream=codec_name,channels,sample_rate,duration -of json $mp4 | ConvertFrom-Json
    $audio = $audioProbe.streams | Select-Object -First 1
    if (-not $audio) { $problems.Add('MP4 has no narration audio stream even though the build manifest declares one') }
    else {
        if ($audio.codec_name -ne 'aac') { $problems.Add("audio codec is $($audio.codec_name), expected aac") }
        if ([int]$audio.channels -ne 2) { $problems.Add("audio channels are $($audio.channels), expected 2") }
        if ([int]$audio.sample_rate -ne 48000) { $problems.Add("audio sample rate is $($audio.sample_rate), expected 48000") }
    }
}

$gifInfo = Get-Item -LiteralPath $gif
if ($gifInfo.Length -gt 8MB) { $problems.Add("GIF is $([Math]::Round($gifInfo.Length / 1MB, 2)) MB, expected <= 8 MB") }
$black = (& ffmpeg -i $mp4 -vf 'blackdetect=d=0.4:pix_th=0.10' -an -f null - 2>&1) -join "`n"
$blackMatches = [regex]::Matches($black, 'black_start:(?<s>[\d.]+) black_end:(?<e>[\d.]+)')
$blackIntervals = @($blackMatches | ForEach-Object {
        [ordered]@{ startSeconds = [double]$_.Groups['s'].Value; endSeconds = [double]$_.Groups['e'].Value }
    })
$blackCount = $blackIntervals.Count
if ($blackCount -gt 0) { $problems.Add("MP4 contains $blackCount black-frame interval(s) >= 0.4 s: " + (($blackIntervals | ForEach-Object { "$($_.startSeconds)-$($_.endSeconds)" }) -join ', ')) }

$report = [ordered]@{
    schemaVersion = '1.0'
    kind = 'windows11-demo-video-validation'
    validatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    pass = ($problems.Count -eq 0)
    mp4 = [ordered]@{
        path = $mp4; sha256 = $build.mp4Sha256; codec = $video.codec_name; width = [int]$video.width; height = [int]$video.height
        pixFmt = $video.pix_fmt; durationSeconds = $duration; blackIntervals = $blackIntervals
    }
    gif = [ordered]@{ path = $gif; sha256 = $build.gifSha256; bytes = $gifInfo.Length }
    audio = if ($audio) { [ordered]@{ codec = $audio.codec_name; channels = [int]$audio.channels; sampleRate = [int]$audio.sample_rate; durationSeconds = [double]$audio.duration } } else { $null }
    captionsIncluded = [bool]$build.captionsIncluded
    narration = $build.narration
    problems = @($problems)
    humanReviewRequired = $true
}
$target = if ($OutputPath) { $OutputPath } else { Join-Path (Split-Path -Parent $mp4) 'video-validation.json' }
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $target -Encoding utf8
Write-Host "video validation: $target"
if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "FAIL_MEDIA: $_" }
    exit 6
}
Write-Host 'VIDEO VALIDATION PASS — human review is still required before publication.'
exit 0
