#!/usr/bin/env pwsh
<#
.SYNOPSIS
  One-command local Windows 11 physical release gate (v1.1 AW-054/AW-055/AW-056/AW-065/AW-066).
.DESCRIPTION
  Runs the reference-desktop release-full suite on an approved SHA, exports the per-WT ledger,
  runs the 100-cycle UI responsiveness measurement, and (unless skipped) captures the Track B
  demo, builds the MP4/GIF and validates it. Copies sanitized artifacts into one gate directory.
  All execution is local; raw media stays local and no data is uploaded.
#>
[CmdletBinding()]
param(
    [string] $RepoRoot = 'C:\AetherWorkspace\trusted',
    [Parameter(Mandatory = $true)]
    [string] $ApprovedRevisionFile,
    [string] $ConfigPath = 'C:\AetherWorkspace\automation\test-config.json',
    [string] $EnvironmentFile,
    [int] $UiCycles = 100,
    [string[]] $CaptureScenarios = @('TrackB'),
    [string] $ManualLedgerPath,
    [switch] $SkipCapture,
    [switch] $SkipMeasurements,
    [switch] $PromptObsPassword
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath $RepoRoot).Path
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$gateRoot = Join-Path (Split-Path -Parent $repo) "results\physical-gate-$stamp"
New-Item -ItemType Directory -Force -Path $gateRoot | Out-Null
$resultsDir = Join-Path (Split-Path -Parent $repo) 'results'
$summary = [ordered]@{
    schemaVersion = '1.0'
    kind = 'windows11-physical-release-gate'
    runId = "physical-gate-$stamp"
    startedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    repoRoot = $repo
    gateRoot = $gateRoot
    steps = [ordered]@{}
}
function Step([string] $name, [string] $state, $detail = $null) {
    $summary.steps[$name] = [ordered]@{ state = $state; detail = $detail }
    Write-Host ("[$state] $name" + ($(if ($detail) { ": $detail" } else { '' })))
}
function Finish([int] $code, [string] $state) {
    $summary.finishedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    $summary.overall = $state
    $summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $gateRoot 'gate-summary.json') -Encoding utf8
    Write-Host "PHYSICAL GATE $state — $gateRoot"
    if ($code -eq 0) { Write-Host 'Share gate-summary.json + sanitized manifests from the gate directory; raw media stays local.' }
    exit $code
}

$approved = (Get-Content -LiteralPath $ApprovedRevisionFile -Raw).Trim().ToLowerInvariant()
if ($approved -notmatch '^[0-9a-f]{40}$') { Step 'trust' 'SKIPPED_UNAPPROVED_SHA'; Finish 2 'FAILED' }
& git -C $repo cat-file -t $approved *> $null
if ($LASTEXITCODE -ne 0) { Step 'trust' 'SKIPPED_UNAPPROVED_SHA' 'commit not found'; Finish 2 'FAILED' }
& git -C $repo merge-base --is-ancestor $approved 'origin/main' *> $null
if ($LASTEXITCODE -ne 0) { Step 'trust' 'SKIPPED_UNAPPROVED_SHA' 'not on origin/main'; Finish 2 'FAILED' }
Step 'trust' 'PASS' $approved

$corePass = $true
if (-not $SkipMeasurements) {
    $before = @(Get-ChildItem -LiteralPath $resultsDir -Directory -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
    & pwsh -NoProfile -File (Join-Path $repo 'tools\windows-test\Run-AetherTests.ps1') `
        -Profile release-full -ApprovedRevisionFile $ApprovedRevisionFile -ConfigPath $ConfigPath
    $referenceExit = $LASTEXITCODE
    $after = @(Get-ChildItem -LiteralPath $resultsDir -Directory -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
    $newRuns = @($after | Where-Object { $_ -notin $before } | Sort-Object)
    $referenceRun = if ($newRuns.Count -gt 0) { $newRuns[-1] } else { $after | Sort-Object | Select-Object -Last 1 }
    if ($referenceExit -eq 0 -and $referenceRun -and (Test-Path (Join-Path $referenceRun 'run-result.json'))) {
        Step 'reference-full' 'PASS' $referenceRun
        Copy-Item (Join-Path $referenceRun 'run-result.json') -Destination $gateRoot -Force
        Copy-Item (Join-Path $referenceRun 'environment-manifest.json') -Destination $gateRoot -Force -ErrorAction SilentlyContinue
    }
    else {
        Step 'reference-full' 'FAIL' "exit=$referenceExit"; $corePass = $false
    }

    if ($referenceRun -and (Test-Path (Join-Path $referenceRun 'run-result.json'))) {
        $ledgerArgs = @('-NoProfile', '-File', (Join-Path $repo 'tools\windows-test\Export-WtLedger.ps1'), '-ResultsDirectory', $referenceRun, '-OutputDirectory', $gateRoot)
        if ($ManualLedgerPath -and (Test-Path -LiteralPath $ManualLedgerPath)) { $ledgerArgs += @('-ManualLedgerPath', (Resolve-Path -LiteralPath $ManualLedgerPath).Path) }
        & pwsh @ledgerArgs
        $ledgerExit = $LASTEXITCODE
        if ($ledgerExit -ne 0) { Step 'wt-ledger' 'FAIL' 'see ledger'; $corePass = $false }
        else { Step 'wt-ledger' 'PASS' $gateRoot }
    }

    & pwsh -NoProfile -File (Join-Path $repo 'tools\windows-test\Measure-UiResponsiveness.ps1') -Cycles $UiCycles -ResultsDirectory 'results/ui-responsiveness'
    $uiExit = $LASTEXITCODE
    if ($uiExit -eq 0) { Step 'ui-responsiveness' 'PASS'; Get-ChildItem (Join-Path $repo 'results\ui-responsiveness') -File | Where-Object { $_.Name -like 'ui-responsiveness-*.json' } | Sort-Object LastWriteTime | Select-Object -Last 1 | ForEach-Object { Copy-Item $_.FullName -Destination $gateRoot -Force } }
    elseif ($uiExit -eq 2) { Step 'ui-responsiveness' 'SKIPPED_ENVIRONMENT'; $corePass = $false }
    else { Step 'ui-responsiveness' 'FAIL'; $corePass = $false }
}

$capturePass = $true
if (-not $SkipCapture) {
    if (-not $env:AETHER_OBS_WEBSOCKET_PASSWORD -and $PromptObsPassword) {
        $securePassword = Read-Host -Prompt 'OBS WebSocket password' -AsSecureString
        $env:AETHER_OBS_WEBSOCKET_PASSWORD = [System.Net.NetworkCredential]::new('', $securePassword).Password
    }
    if (-not $env:AETHER_OBS_WEBSOCKET_PASSWORD) { Step 'capture' 'SKIPPED_ENVIRONMENT' 'AETHER_OBS_WEBSOCKET_PASSWORD is not set (use -PromptObsPassword or run in the same PowerShell process where it is set)'; $capturePass = $false }
    elseif (-not $EnvironmentFile) { Step 'capture' 'SKIPPED_ENVIRONMENT' 'EnvironmentFile is required for Track B'; $capturePass = $false }
    else {
        $runsRoot = Join-Path (Split-Path -Parent $repo) 'runs'
        New-Item -ItemType Directory -Force -Path $runsRoot | Out-Null
        $beforeRuns = @(Get-ChildItem -LiteralPath $runsRoot -Directory -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
        & pwsh -NoProfile -File (Join-Path $repo 'tools\demo\Prepare-Demo.ps1') -RepoRoot $repo -ApprovedRevisionFile $ApprovedRevisionFile -RunRoot $runsRoot
        $prepareExit = $LASTEXITCODE
        $afterRuns = @(Get-ChildItem -LiteralPath $runsRoot -Directory -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
        $demoRun = @($afterRuns | Where-Object { $_ -notin $beforeRuns } | Sort-Object) | Select-Object -Last 1
        if ($prepareExit -ne 0 -or -not $demoRun) { Step 'prepare-demo' 'FAIL' 'see output'; $capturePass = $false }
        else {
            Step 'prepare-demo' 'PASS' $demoRun
            Copy-Item (Join-Path $demoRun 'demo-preparation.json') -Destination $gateRoot -Force -ErrorAction SilentlyContinue
            foreach ($scenario in $CaptureScenarios) {
                $captureManifest = $null
                $recordOutput = & pwsh -NoProfile -File (Join-Path $repo 'tools\demo\Record-Demo.ps1') -RunDirectory $demoRun -Scenario $scenario -EnvironmentFile $EnvironmentFile 2>&1
                $recordExit = $LASTEXITCODE
                $recordOutput | Tee-Object -FilePath (Join-Path $gateRoot "record-$scenario.log") | Out-Null
                $match = [regex]::Match(($recordOutput -join "`n"), 'DEMO CAPTURE OK: (.+)')
                if ($recordExit -eq 0 -and $match.Success) {
                    $captureManifest = $match.Groups[1].Value.Trim()
                    Step "capture-$scenario" 'PASS' $captureManifest
                    Copy-Item $captureManifest -Destination $gateRoot -Force
                }
                else { Step "capture-$scenario" 'FAIL' "exit=$recordExit"; $capturePass = $false; continue }

                $buildOutput = & pwsh -NoProfile -File (Join-Path $repo 'tools\demo\Build-Video.ps1') -CaptureManifest $captureManifest 2>&1
                $buildExit = $LASTEXITCODE
                $buildOutput | Tee-Object -FilePath (Join-Path $gateRoot "build-$scenario.log") | Out-Null
                $buildMatch = [regex]::Match(($buildOutput -join "`n"), 'VIDEO BUILD OK: (.+)')
                if ($buildExit -ne 0 -or -not $buildMatch.Success) { Step "video-build-$scenario" 'FAIL' "exit=$buildExit"; $capturePass = $false; continue }
                Step "video-build-$scenario" 'PASS' $buildMatch.Groups[1].Value
                $videoBuild = $buildMatch.Groups[1].Value.Trim()
                Copy-Item $videoBuild -Destination $gateRoot -Force

                & pwsh -NoProfile -File (Join-Path $repo 'tools\demo\Validate-Video.ps1') -VideoBuildManifest $videoBuild 2>&1 | Tee-Object -FilePath (Join-Path $gateRoot "validate-$scenario.log") | Out-Null
                $validationExit = $LASTEXITCODE
                $validation = Get-ChildItem (Split-Path $videoBuild -Parent) -Filter 'video-validation.json' | Select-Object -First 1
                if ($validation) { Copy-Item $validation.FullName -Destination $gateRoot -Force }
                if ($validationExit -eq 0) { Step "video-validate-$scenario" 'PASS' }
                else { Step "video-validate-$scenario" 'FAIL' "exit=$validationExit"; $capturePass = $false }
            }
        }
    }
}

$overall = if ($corePass -and $capturePass) { 'PASS' } else { 'FAILED' }
Finish ($(if ($overall -eq 'PASS') { 0 } else { 1 })) $overall
