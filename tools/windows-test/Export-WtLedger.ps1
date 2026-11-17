#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Builds the per-WT-04/WT-05 evidence ledger from reference-desktop TRX results (v1.1 AW-065).
.DESCRIPTION
  Maps automated test names to Windows 11 reference-desktop test IDs, merges optional manual
  DPI/focus/modal entries, and writes a JSON + Markdown ledger. Aggregate suite counters are not a
  substitute for this per-ID register.
.PARAMETER ResultsDirectory
  Directory containing TRX files (for example a reference-desktop run results folder).
.PARAMETER ManualLedgerPath
  Optional JSON array of manual entries: [{ "id": "WT-05-05", "status": "PASS|FAIL|SKIPPED|EXCLUDED", "evidence": "...", "notes": "..." }].
.PARAMETER OutputDirectory
  Repository-relative output directory (default results/wt-ledger).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ResultsDirectory,

    [string] $ManualLedgerPath,

    [string] $OutputDirectory = 'results/wt-ledger',

    [string] $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
)

$ErrorActionPreference = 'Stop'
$results = (Resolve-Path -LiteralPath $ResultsDirectory).Path
$output = Join-Path $RepoRoot $OutputDirectory
New-Item -ItemType Directory -Force -Path $output | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'

$mapping = [ordered]@{
    'WT-04-01' = @('GuiWorkflowRunsOverWindowsNamedPipes')
    'WT-04-02' = @('PrivacyRestrictedWorkflowProducesPrivateSummaryAndTrace', 'GuiWorkflowProtectsRestrictedData')
    'WT-04-03' = @('ShellStartsConnectsToBrokerAndExposesCoreControls')
    'WT-04-04' = @('GuiWorkflowWithRealWpfSampleAppsKeepsWindowsOpen')
    'WT-04-05' = @('WpfSampleAppsStayOpenAndReceiveContext')
    'WT-04-06' = @('WatchlistSelectionSynchronizesRiskAndChartWithoutEchoPublish')
    'WT-04-07' = @('RepeatedWorkflowReportsUiTelemetry')
    'WT-04-08' = @('RepeatedWorkflowReportsUiTelemetry')
    'WT-05-01' = @('SaveRestoreKeepsAllWindowsOnVisibleScreens')
    'WT-05-02' = @('SaveRestoreKeepsAllWindowsOnVisibleScreens')
    'WT-05-03' = @()
    'WT-05-04' = @('DualMonitorLayoutRestoresSecondaryMonitorPlacements')
    'WT-05-05' = @()
    'WT-05-06' = @()
    'WT-05-07' = @('MissingMonitorFallsBackToAPresentScreen')
    'WT-05-08' = @('RepeatedWorkflowReportsUiTelemetry')
    'WT-05-09' = @()
    'WT-05-10' = @()
}

$trx = Get-ChildItem -LiteralPath $results -Recurse -Filter '*.trx' -File -ErrorAction SilentlyContinue
$tests = foreach ($file in $trx) {
    try {
        [xml]$xml = Get-Content -LiteralPath $file.FullName -Raw
        foreach ($result in $xml.TestRun.Results.UnitTestResult) {
            [pscustomobject]@{ Name = [string]$result.testName; Outcome = [string]$result.outcome }
        }
    }
    catch {
        Write-Warning "Unreadable TRX '$($file.FullName)': $($_.Exception.Message)"
    }
}
$tests = @($tests)

$manual = @()
if ($ManualLedgerPath) {
    $manual = Get-Content -LiteralPath $ManualLedgerPath -Raw | ConvertFrom-Json
}

$entries = foreach ($id in $mapping.Keys) {
    $patterns = @($mapping[$id])
    $matched = @($tests | Where-Object { $name = $_.Name; $patterns | Where-Object { $name -like "*$_*" } })
    $manualEntry = $manual | Where-Object { $_.id -eq $id } | Select-Object -First 1
    $status = if ($manualEntry) { [string]$manualEntry.status }
        elseif ($matched.Count -eq 0) { if ($patterns.Count -eq 0) { 'MANUAL_PENDING' } else { 'NOT_RUN' } }
        elseif ($matched | Where-Object { $_.Outcome -eq 'Failed' }) { 'FAIL' }
        else { 'PASS' }
    [pscustomobject]@{
        id       = $id
        status   = $status
        automated = if ($patterns.Count -gt 0) { 'yes' } else { 'manual' }
        tests    = ($matched | Select-Object -ExpandProperty Name -Unique) -join '; '
        evidence = if ($manualEntry) { [string]$manualEntry.evidence } else { '' }
        notes    = if ($manualEntry) { [string]$manualEntry.notes } else { '' }
    }
}
$entries = @($entries)

$jsonPath = Join-Path $output "wt-ledger-$stamp.json"
$mdPath = Join-Path $output "wt-ledger-$stamp.md"
[ordered]@{
    schemaVersion = '1.0'
    kind          = 'windows11-wt-ledger'
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    sourceResults = $results
    trxFiles      = $trx.Count
    entries       = $entries
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding utf8

$lines = @(
    '# Windows 11 WT-04/WT-05 evidence ledger',
    '',
    "Generated: $(Get-Date -Format o)",
    "Source: ``$results`` (TRX files: $($trx.Count))",
    '',
    '| Test ID | Status | Automated | Tests / manual evidence |',
    '|---|---|---|---|'
)
$lines += $entries | ForEach-Object {
    $evidence = if ($_.tests) { $_.tests } else { "$($_.evidence) $($_.notes)".Trim() }
    "| $($_.id) | $($_.status) | $($_.automated) | $evidence |"
}
$lines | Set-Content -LiteralPath $mdPath -Encoding utf8

Write-Host "WT ledger: $mdPath"
Write-Host "WT ledger JSON: $jsonPath"
$failures = @($entries | Where-Object { $_.status -eq 'FAIL' })
if ($failures.Count -gt 0) {
    Write-Host "WT LEDGER FAIL: $($failures.Count) failed ID(s): $($failures.id -join ', ')"
    exit 1
}
Write-Host "WT LEDGER: $($entries.Count) IDs recorded; manual/pending IDs require the physical Windows 11 run."
exit 0
