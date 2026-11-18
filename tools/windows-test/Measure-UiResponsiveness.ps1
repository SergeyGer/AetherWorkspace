#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Local Windows 11 UI-responsiveness measurement wrapper (v1.1 AW-066).
.DESCRIPTION
  Runs the real WPF shell reference workflow repeatedly (default 100 cycles), collects the shell's
  sanitized frame/command telemetry, computes p50/p95/p99 summaries and writes a local measurement
  report. Physical-desktop only; no remote control and no raw desktop capture.
.PARAMETER Cycles
  Number of workflow cycles (1-100; 100 for the reference measurement).
.PARAMETER ResultsDirectory
  Repository-relative output directory for telemetry and the summary report.
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 100)]
    [int] $Cycles = 100,

    [string] $ResultsDirectory = 'results/ui-responsiveness'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$results = Join-Path $repoRoot $ResultsDirectory
New-Item -ItemType Directory -Force -Path $results | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$telemetryPath = Join-Path $results "ui-telemetry-$stamp.jsonl"
$testLog = Join-Path $results "dotnet-test-$stamp.log"
$reportPath = Join-Path $results "ui-responsiveness-$stamp.json"

function Skip([string] $reason) {
    Write-Host "SKIPPED_ENVIRONMENT: $reason"
    exit 2
}

if (-not $IsWindows) { Skip 'This measurement requires Windows.' }
if (-not [Environment]::UserInteractive) { Skip 'No interactive user session.' }
if (-not (Get-Process -Name explorer -ErrorAction SilentlyContinue)) { Skip 'No interactive Explorer session.' }

$systemDrive = Get-PSDrive -Name C
if ($systemDrive.Free -lt 5GB) { Skip 'Less than 5 GB free disk.' }
$battery = Get-CimInstance Win32_Battery -ErrorAction SilentlyContinue | Select-Object -First 1
if ($battery -and $battery.BatteryStatus -ne 2) {
    Write-Host 'WARNING: AC power not confirmed (battery status is not "AC"). Record this in the report.'
}

$osInfo = Get-CimInstance Win32_OperatingSystem
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$computer = Get-CimInstance Win32_ComputerSystem
$video = Get-CimInstance Win32_VideoController | Select-Object -First 1
$display = if ($video -and $video.CurrentHorizontalResolution -and $video.CurrentVerticalResolution) {
    "$($video.CurrentHorizontalResolution)x$($video.CurrentVerticalResolution)"
} else { 'unknown' }
$hardwareProfile = [ordered]@{
    os = $osInfo.Caption
    osVersion = $osInfo.Version
    osBuild = $osInfo.BuildNumber
    cpu = if ($cpu) { $cpu.Name } else { 'unknown' }
    cpuCores = if ($cpu) { $cpu.NumberOfCores } else { 0 }
    cpuLogical = if ($cpu) { $cpu.NumberOfLogicalProcessors } else { 0 }
    ramGb = if ($computer) { [Math]::Round($computer.TotalPhysicalMemory / 1GB, 1) } else { 0 }
    gpu = if ($video) { $video.Name } else { 'unknown' }
    gpuDriver = if ($video) { $video.DriverVersion } else { 'unknown' }
    display = $display
    sessionInteractive = [Environment]::UserInteractive
}
$thresholds = [ordered]@{
    commandP95MsMax = 250
    commandP99MsMax = 2000
    frameP95MsMax = 16
    longFramesMax = 0
    note = 'Conservative baseline thresholds; see docs/testing/ui-responsiveness.md and compare p95/p99 against the recorded baseline for regressions.'
}

$env:AETHER_UI_CYCLES = $Cycles
$env:AETHER_UI_TELEMETRY_OUT = $telemetryPath

Write-Host "Building solution prerequisites, Windows UI test host and running $Cycles workflow cycle(s)..."
Push-Location $repoRoot
try {
    & dotnet build AetherWorkspace.slnx -c Release | Tee-Object -FilePath $testLog
    if ($LASTEXITCODE -ne 0) { Write-Host 'FAIL_HARNESS: solution build failed.'; exit 3 }
    & dotnet build tests/Aether.Shell.UiTests/Aether.Shell.UiTests.csproj -c Release -p:EnableWindowsTargeting=true | Tee-Object -FilePath $testLog -Append
    if ($LASTEXITCODE -ne 0) { Write-Host 'FAIL_HARNESS: UI test build failed.'; exit 3 }
}
finally {
    Pop-Location
}

& dotnet test tests/Aether.Shell.UiTests/Aether.Shell.UiTests.csproj -c Release --no-build `
    --filter 'FullyQualifiedName~RepeatedWorkflowReportsUiTelemetry' | Tee-Object -FilePath $testLog -Append
$testExit = $LASTEXITCODE
if ($testExit -ne 0) { Write-Host "FAIL_PRODUCT: UI telemetry run failed (exit $testExit)."; exit 4 }
if (-not (Test-Path -LiteralPath $telemetryPath)) { Write-Host 'FAIL_HARNESS: no telemetry file was written.'; exit 3 }

$records = @(Get-Content -LiteralPath $telemetryPath | ForEach-Object { $_ | ConvertFrom-Json })
$measured = @($records | Where-Object { $_.frames -gt 0 })
if ($records.Count -eq 0 -or $measured.Count -eq 0) {
    Write-Host 'FAIL_HARNESS: no UI telemetry samples were captured; the measurement is inconclusive (check interactive session/rendering).'
    exit 3
}
function Percentile($values, [double] $p) {
    if ($values.Count -eq 0) { return 0 }
    $sorted = @($values | Sort-Object)
    $index = [Math]::Ceiling(($p / 100.0) * $sorted.Count) - 1
    return [Math]::Round([double]$sorted[[Math]::Max(0, [Math]::Min($index, $sorted.Count - 1))], 2)
}
$summary = [ordered]@{
    schemaVersion  = '1.0'
    kind           = 'windows11-wpf-ui-responsiveness'
    measuredAtUtc  = (Get-Date).ToUniversalTime().ToString('o')
    commitSha      = (git -C $repoRoot rev-parse HEAD 2>$null | Out-String).Trim()
    cycles         = $records.Count
    host           = [ordered]@{
        os          = [System.Environment]::OSVersion.VersionString
        interactive = [Environment]::UserInteractive
        powerNote   = if ($battery -and $battery.BatteryStatus -ne 2) { 'AC power not confirmed' } else { 'AC power confirmed' }
        hardware    = $hardwareProfile
    }
    thresholds     = $thresholds
    metrics        = [ordered]@{
        commandDurationMs = [ordered]@{
            p50 = Percentile ($records.commandDurationMs) 50
            p95 = Percentile ($records.commandDurationMs) 95
            p99 = Percentile ($records.commandDurationMs) 99
        }
        frameP95Ms     = [ordered]@{
            p50 = Percentile ($records.p95FrameMs) 50
            p95 = Percentile ($records.p95FrameMs) 95
            p99 = Percentile ($records.p95FrameMs) 99
        }
        frameP99Ms     = [ordered]@{
            p50 = Percentile ($records.p99FrameMs) 50
            p95 = Percentile ($records.p99FrameMs) 95
            p99 = Percentile ($records.p99FrameMs) 99
        }
        longFramesTotal = ($records.longFrames | Measure-Object -Sum).Sum
        framesTotal     = ($records.frames | Measure-Object -Sum).Sum
        memory          = [ordered]@{
            workingSetMb = [ordered]@{
                start = if ($records[0].workingSetMb) { [double]$records[0].workingSetMb } else { $null }
                end = if ($records[-1].workingSetMb) { [double]$records[-1].workingSetMb } else { $null }
                p50 = Percentile ($records.workingSetMb | Where-Object { $null -ne $_ }) 50
                max = if ($records.workingSetMb) { [double](($records.workingSetMb | Measure-Object -Maximum).Maximum) } else { $null }
            }
            managedMemoryMb = [ordered]@{
                start = if ($records[0].managedMemoryMb) { [double]$records[0].managedMemoryMb } else { $null }
                end = if ($records[-1].managedMemoryMb) { [double]$records[-1].managedMemoryMb } else { $null }
                p50 = Percentile ($records.managedMemoryMb | Where-Object { $null -ne $_ }) 50
                max = if ($records.managedMemoryMb) { [double](($records.managedMemoryMb | Measure-Object -Maximum).Maximum) } else { $null }
            }
        }
    }
    telemetryFile  = $telemetryPath
}
$violations = @()
if ($summary.metrics.commandDurationMs.p95 -gt $thresholds.commandP95MsMax) { $violations += "command p95 $($summary.metrics.commandDurationMs.p95) ms > $($thresholds.commandP95MsMax) ms" }
if ($summary.metrics.commandDurationMs.p99 -gt $thresholds.commandP99MsMax) { $violations += "command p99 $($summary.metrics.commandDurationMs.p99) ms > $($thresholds.commandP99MsMax) ms" }
if ($summary.metrics.frameP95Ms.p95 -gt $thresholds.frameP95MsMax) { $violations += "frame-p95 p95 $($summary.metrics.frameP95Ms.p95) ms > $($thresholds.frameP95MsMax) ms" }
if ($summary.metrics.longFramesTotal -gt $thresholds.longFramesMax) { $violations += "long frames $($summary.metrics.longFramesTotal) > $($thresholds.longFramesMax)" }
$summary.thresholdViolations = $violations
$summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $reportPath -Encoding utf8
Write-Host "UI responsiveness report: $reportPath"
if ($violations.Count -gt 0) {
    Write-Host "FAIL_THRESHOLD: $($violations -join '; ')"
    exit 7
}
Write-Host ("command p50/p95/p99={0}/{1}/{2} ms; frame-p95 p50/p95/p99={3}/{4}/{5} ms; cycles={6}" -f `
    $summary.metrics.commandDurationMs.p50, $summary.metrics.commandDurationMs.p95, $summary.metrics.commandDurationMs.p99, `
    $summary.metrics.frameP95Ms.p50, $summary.metrics.frameP95Ms.p95, $summary.metrics.frameP95Ms.p99, $records.Count)
exit 0
