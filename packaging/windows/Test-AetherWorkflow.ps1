#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Runs the packaged reference workflow headlessly and writes machine-readable diagnostics.
.DESCRIPTION
  Starts the bundled broker with the Windows WPF app registry, runs the bundled headless shell
  with the reference command, captures its structured JSON result and a sanitized diagnostics
  bundle, then stops the broker. This reproduces the desktop workflow without UI automation and
  makes packaged-only failures directly inspectable.
#>
[CmdletBinding()]
param(
    [switch] $KeepBroker,
    [int] $StartupTimeoutSeconds = 45
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$broker = Join-Path $root 'broker\Aether.BrokerApp.exe'
$headlessShell = Join-Path $root 'shell-headless\Aether.Shell.Headless.exe'
$registry = Join-Path $root 'samples\registry\apps.windows.json'
$contexts = Join-Path $root 'samples\contexts\contexts.json'
$samples = Join-Path $root 'samples\app\Aether.SampleApps.exe'
$webview2Chart = Join-Path $root 'samples\webview2\Aether.SampleApps.WebView2.exe'
$fixtures = Join-Path $root 'samples\fixtures'
$endpointFile = Join-Path $root 'endpoint.json'
$layoutStore = Join-Path $root 'layouts'
$logs = Join-Path $root 'logs'
$diagnostics = Join-Path $root 'diagnostics'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'

foreach ($required in @($broker, $headlessShell, $samples, $webview2Chart, $registry, $contexts)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Bundle is incomplete: missing '$required'. Re-extract the release archive."
    }
}

New-Item -ItemType Directory -Force -Path $layoutStore, $logs, $diagnostics | Out-Null
Remove-Item -LiteralPath $endpointFile -ErrorAction SilentlyContinue

$workflowResult = Join-Path $diagnostics "workflow-result-$stamp.json"
$diagnosticsBundle = Join-Path $diagnostics "diagnostics-bundle-$stamp.json"
$shellStdout = Join-Path $diagnostics "shell-stdout-$stamp.log"
$shellStderr = Join-Path $diagnostics "shell-stderr-$stamp.log"

Write-Host 'Starting packaged broker and reference workflow (headless)...'
$brokerProcess = Start-Process -FilePath $broker -WorkingDirectory $root -PassThru -ArgumentList @(
    '--transport', 'named-pipe',
    '--registry', $registry,
    '--contexts', $contexts,
    '--token', "WPF_SAMPLES_EXE=$samples",
    '--token', "WEBVIEW2_CHART_EXE=$webview2Chart",
    '--token', "FIXTURES_DIR=$fixtures",
    '--token', "AETHER_HOME=$root",
    '--allowed-root', $root,
    '--operator-app', 'aether.shell',
    '--endpoint-file', $endpointFile,
    '--layout-store', $layoutStore,
    '--child-log-dir', $logs,
    '--audit-file', (Join-Path $logs 'broker-audit.jsonl'),
    '--max-actions', '6'
)

try {
    $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline -and -not (Test-Path -LiteralPath $endpointFile)) {
        if ($brokerProcess.HasExited) {
            throw "The packaged broker exited with code $($brokerProcess.ExitCode) before writing its handshake file."
        }
        Start-Sleep -Milliseconds 250
    }
    if (-not (Test-Path -LiteralPath $endpointFile)) {
        throw "The packaged broker did not write its handshake file within $StartupTimeoutSeconds seconds."
    }

    $command = 'Open Northstar bond analytics, show the associated risk view, synchronize this instrument across the workspace and summarize the exposure privately.'
    & $headlessShell --endpoint-file $endpointFile --classification RESTRICTED --command $command --export-diagnostics $diagnosticsBundle 1> $shellStdout 2> $shellStderr
    $shellExit = $LASTEXITCODE
    Copy-Item -LiteralPath $shellStdout -Destination $workflowResult -Force

    Write-Host "workflow shell exit: $shellExit"
    Write-Host "result:  $workflowResult"
    Write-Host "stderr:  $shellStderr"
    if (Test-Path -LiteralPath $diagnosticsBundle) { Write-Host "diagnostics bundle: $diagnosticsBundle" }
    Write-Host "broker audit: $(Join-Path $logs 'broker-audit.jsonl')"

    if (Test-Path -LiteralPath $shellStdout) {
        $result = Get-Content -LiteralPath $shellStdout -Raw | ConvertFrom-Json -ErrorAction SilentlyContinue
        if ($result) {
            Write-Host ("status={0} trace={1} denial={2} steps={3}" -f $result.status, $result.traceId, $result.denialCode, @($result.steps).Count)
        }
    }

    exit $shellExit
}
finally {
    if (-not $KeepBroker) {
        # Stop the broker and any WPF sample children launched for this test run.
        $started = $brokerProcess.StartTime
        if (-not $brokerProcess.HasExited) { Stop-Process -Id $brokerProcess.Id -Force -ErrorAction SilentlyContinue }
        Get-Process Aether.SampleApps, Aether.SampleApps.WebView2 -ErrorAction SilentlyContinue |
            Where-Object { $_.StartTime -ge $started } |
            ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
    }
    else {
        Write-Host "Broker left running (PID $($brokerProcess.Id)) because -KeepBroker was specified."
    }
}
