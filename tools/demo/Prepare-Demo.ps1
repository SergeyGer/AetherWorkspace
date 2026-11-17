#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Trusted preparation for the local Windows 11 demo capture (v1.1 AW-055).
.DESCRIPTION
  Validates the operator-approved full SHA, creates a detached worktree, builds the demo-relevant
  projects, runs local preflight (interactive session, power, disk, OBS WebSocket, FFmpeg, .NET) and
  writes a run manifest. Does not start OBS or manipulate the desktop.
#>
[CmdletBinding()]
param(
    [string] $RepoRoot = 'C:\AetherWorkspace\trusted',
    [Parameter(Mandatory = $true)]
    [string] $ApprovedRevisionFile,
    [string] $RunRoot,
    [string] $Configuration = 'Release',
    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'The demo preparation requires Windows.' }
$repo = (Resolve-Path -LiteralPath $RepoRoot).Path
if (-not $RunRoot) { $RunRoot = Join-Path (Split-Path -Parent $repo) 'runs' }
New-Item -ItemType Directory -Force -Path $RunRoot | Out-Null

$approved = (Get-Content -LiteralPath $ApprovedRevisionFile -Raw).Trim().ToLowerInvariant()
if ($approved -notmatch '^[0-9a-f]{40}$') { throw 'SKIPPED_UNAPPROVED_SHA: the approval file must contain one full 40-hex commit SHA.' }
$objectType = (& git -C $repo cat-file -t $approved 2>$null | Out-String).Trim()
if ($objectType -ne 'commit') { throw 'SKIPPED_UNAPPROVED_SHA: the approved SHA is not a commit in the trusted repository.' }
& git -C $repo merge-base --is-ancestor $approved 'origin/main' 2>$null
if ($LASTEXITCODE -ne 0) { throw 'SKIPPED_UNAPPROVED_SHA: the approved commit is not on origin/main.' }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$runDir = Join-Path $RunRoot "demo-$stamp-$($approved.Substring(0,8))"
& git -C $repo worktree add --detach $runDir $approved | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'FAIL_HARNESS: could not create the detached worktree from the approved SHA.' }

# Preflight environment.
if (-not [Environment]::UserInteractive) { throw 'SKIPPED_ENVIRONMENT: no interactive user session.' }
if (-not (Get-Process -Name explorer -ErrorAction SilentlyContinue)) { throw 'SKIPPED_ENVIRONMENT: no interactive Explorer session.' }
$drive = Get-PSDrive -Name ($runDir.Substring(0,1))
if ($drive.Free -lt 10GB) { throw 'SKIPPED_ENVIRONMENT: less than 10 GB free disk.' }
$battery = Get-CimInstance Win32_Battery -ErrorAction SilentlyContinue | Select-Object -First 1
if ($battery -and $battery.BatteryStatus -ne 2) { throw 'SKIPPED_ENVIRONMENT: AC power is required for capture.' }

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { throw 'SKIPPED_ENVIRONMENT: dotnet is not on PATH.' }
$runtimes = & dotnet --list-runtimes
if ($runtimes -notmatch 'Microsoft\.WindowsDesktop\.App 10\.') { throw 'SKIPPED_ENVIRONMENT: .NET 10 Desktop Runtime is missing.' }
$ffmpeg = Get-Command ffmpeg -ErrorAction SilentlyContinue
if (-not $ffmpeg) { throw 'SKIPPED_ENVIRONMENT: FFmpeg is not on PATH.' }

. (Join-Path $PSScriptRoot 'Obs-Control.ps1')
$obs = $null
try {
    $obs = Connect-Obs
    Disconnect-Obs $obs
}
catch {
    throw "SKIPPED_ENVIRONMENT: OBS WebSocket is unavailable (start OBS, enable Tools > WebSocket Server Settings on 127.0.0.1 and set AETHER_OBS_WEBSOCKET_PASSWORD): $($_.Exception.Message)"
}

if (-not $SkipBuild) {
    Push-Location $runDir
    try {
        & dotnet build AetherWorkspace.slnx -c $Configuration
        if ($LASTEXITCODE -ne 0) { throw 'FAIL_HARNESS: solution build failed.' }
        & dotnet build tests/Aether.Desktop.E2E.Tests/Aether.Desktop.E2E.Tests.csproj -c $Configuration
        if ($LASTEXITCODE -ne 0) { throw 'FAIL_HARNESS: demo capture test project build failed.' }
    }
    finally {
        Pop-Location
    }
}

$manifest = [ordered]@{
    schemaVersion = '1.0'
    kind = 'windows11-demo-preparation'
    runId = Split-Path -Leaf $runDir
    approvedSha = $approved
    preparedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    runDirectory = $runDir
    configuration = $Configuration
    os = [Environment]::OSVersion.VersionString
    dotnet = (& dotnet --version | Out-String).Trim()
    ffmpeg = (& ffmpeg -version | Select-Object -First 1)
    obsWebSocket = $env:AETHER_OBS_WEBSOCKET_URL ?? 'ws://127.0.0.1:4455'
}
$manifestPath = Join-Path $runDir 'demo-preparation.json'
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding utf8
Write-Host "DEMO PREPARED: $runDir"
Write-Host "manifest: $manifestPath"
exit 0
