#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Records a Track A (deterministic) or Track B (LLM-assisted PUBLIC) demo with local OBS (v1.1 AW-055).
.DESCRIPTION
  Starts OBS recording through the local WebSocket, runs the Category=DemoCapture scenario from the
  prepared worktree, stops OBS, copies the raw MKV and writes a capture manifest. Requires an
  already prepared run directory (Prepare-Demo.ps1) and a running OBS with WebSocket enabled.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $RunDirectory,
    [ValidateSet('TrackA', 'TrackB')]
    [string] $Scenario = 'TrackB',
    [string] $EnvironmentFile = 'C:\AetherWorkspace\trusted\.env',
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'The demo recording requires Windows.' }
$run = (Resolve-Path -LiteralPath $RunDirectory).Path
$manifestPath = Join-Path $run 'demo-preparation.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'FAIL_HARNESS: demo-preparation.json is missing; run Prepare-Demo.ps1 first.' }
$preparation = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

# Load only provider configuration into the process environment; never print secret values.
if (Test-Path -LiteralPath $EnvironmentFile) {
    foreach ($line in Get-Content -LiteralPath $EnvironmentFile) {
        $line = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith('#') -or $line.IndexOf('=') -le 0) { continue }
        $name, $value = $line.Split('=', 2)
        $name = $name.Trim()
        if ($name -match '^(AETHER_|OPENAI_|GEMINI_)') {
            Set-Item -Path ("env:" + $name) -Value $value.Split('#', 2)[0].Trim()
        }
    }
}

if ($Scenario -eq 'TrackB') {
    $provider = if ([string]::IsNullOrWhiteSpace($env:AETHER_MODEL_PROVIDER)) { 'openai' } else { $env:AETHER_MODEL_PROVIDER.Trim().ToLowerInvariant() }
    if ($provider -eq 'gemini') {
        if ([string]::IsNullOrWhiteSpace($env:GEMINI_API_KEY) -or
            [string]::IsNullOrWhiteSpace($env:GEMINI_BASE_URL) -or
            [string]::IsNullOrWhiteSpace($env:GEMINI_MODEL)) {
            throw 'FAIL_HARNESS: Track B with provider gemini requires GEMINI_API_KEY, GEMINI_BASE_URL and GEMINI_MODEL loaded from the local .env; no secrets are printed.'
        }
    }
    else {
        $openAiKey = $env:OPENAI_API_KEY
        $openAiEndpoint = if ($env:OPENAI_BASE_URL) { $env:OPENAI_BASE_URL } else { $env:AETHER_CLOUD_ENDPOINT }
        $openAiModel = if ($env:OPENAI_MODEL) { $env:OPENAI_MODEL } else { $env:AETHER_CLOUD_MODEL }
        if ([string]::IsNullOrWhiteSpace($openAiKey) -or [string]::IsNullOrWhiteSpace($openAiEndpoint) -or [string]::IsNullOrWhiteSpace($openAiModel)) {
            throw 'FAIL_HARNESS: Track B with provider openai requires OPENAI_API_KEY plus OPENAI_BASE_URL/OPENAI_MODEL (AETHER_CLOUD_* may be used as endpoint/model fallbacks) loaded from the local .env; no secrets are printed.'
        }
    }
}

. (Join-Path $PSScriptRoot 'Obs-Control.ps1')
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$eventsPath = Join-Path $run "demo-events-$Scenario-$stamp.json"
$testLog = Join-Path $run "demo-test-$Scenario-$stamp.log"
$env:AETHER_DEMO_SCENARIO = $Scenario
$env:AETHER_DEMO_EVENTS_OUT = $eventsPath
$env:AETHER_DEMO_SHA = $preparation.approvedSha
$env:AETHER_DEMO_RUN_ID = $preparation.runId

$testExit = 3
$obs = Connect-Obs
$recordingStarted = $false
try {
    Start-ObsRecording $obs
    $recordingStarted = $true
    $recordingStartedUtc = (Get-Date).ToUniversalTime().ToString('o')
    Write-Host 'OBS recording started.'

    Push-Location $run
    try {
        & dotnet test tests/Aether.Desktop.E2E.Tests/Aether.Desktop.E2E.Tests.csproj -c $Configuration --no-build `
            --filter 'Category=DemoCapture' | Tee-Object -FilePath $testLog
        $testExit = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
}
finally {
    $obsPath = $null
    if ($recordingStarted) {
        try { $obsPath = Stop-ObsRecording $obs } finally { Disconnect-Obs $obs }
    }
    else {
        try { Disconnect-Obs $obs } catch { }
    }
}

if ($testExit -ne 0) {
    Write-Host "FAIL_PRODUCT: demo capture scenario failed (exit $testExit). Raw recording: $obsPath"
    exit 4
}
if (-not $obsPath -or -not (Test-Path -LiteralPath $obsPath)) {
    Write-Host 'FAIL_MEDIA: OBS did not report a usable recording path.'
    exit 5
}
$rawExtension = [System.IO.Path]::GetExtension($obsPath)
if ([string]::IsNullOrWhiteSpace($rawExtension)) { $rawExtension = '.mkv' }
$rawTarget = Join-Path $run "raw-$Scenario-$stamp$rawExtension"
Copy-Item -LiteralPath $obsPath -Destination $rawTarget -Force
$events = Get-Content -LiteralPath $eventsPath -Raw | ConvertFrom-Json
$captureManifest = [ordered]@{
    schemaVersion = '1.0'
    kind = 'windows11-demo-capture'
    scenario = $Scenario
    capturedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    recordingStartedUtc = $recordingStartedUtc
    approvedSha = $preparation.approvedSha
    runId = $preparation.runId
    rawRecording = $rawTarget
    rawSha256 = (Get-FileHash -LiteralPath $rawTarget -Algorithm SHA256).Hash.ToLowerInvariant()
    eventsFile = $eventsPath
    traceId = $events.traceId
    planner = $events.planner
    usedModel = $events.usedModel
    pids = @($events.pids)
    tickers = @($events.tickers)
    testExit = $testExit
}
$capturePath = Join-Path $run "capture-$Scenario-$stamp.json"
$captureManifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $capturePath -Encoding utf8
Write-Host "DEMO CAPTURE OK: $capturePath"
Write-Host "raw recording: $rawTarget"
exit 0
