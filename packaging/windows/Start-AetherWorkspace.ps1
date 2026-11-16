#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Starts the AetherWorkspace Windows desktop reference bundle (broker + WPF shell).
.DESCRIPTION
  Locates the bundled broker, shell and WPF sample applications, starts the broker with the
  Windows app registry and local launch tokens, waits for the operator handshake file, then
  starts the WPF shell with AETHER_ENDPOINT_FILE set. No network listener is created.
#>
[CmdletBinding()]
param(
    [int] $StartupTimeoutSeconds = 45
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$broker = Join-Path $root 'broker\Aether.BrokerApp.exe'
$shell = Join-Path $root 'shell\Aether.Shell.exe'
$samples = Join-Path $root 'samples\app\Aether.SampleApps.exe'
$registry = Join-Path $root 'samples\registry\apps.windows.json'
$contexts = Join-Path $root 'samples\contexts\contexts.json'
$fixtures = Join-Path $root 'samples\fixtures'
$endpointFile = Join-Path $root 'endpoint.json'
$layoutStore = Join-Path $root 'layouts'
$childLogs = Join-Path $root 'logs'

foreach ($required in @($broker, $shell, $samples, $registry, $contexts)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Bundle is incomplete: missing '$required'. Re-extract the release archive."
    }
}

Remove-Item -LiteralPath $endpointFile -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $layoutStore, $childLogs | Out-Null

Write-Host 'Starting local Aether.BrokerApp (local IPC only)...'
$brokerProcess = Start-Process -FilePath $broker -WorkingDirectory $root -PassThru -ArgumentList @(
    '--transport', 'named-pipe',
    '--registry', $registry,
    '--contexts', $contexts,
    '--token', "WPF_SAMPLES_EXE=$samples",
    '--token', "FIXTURES_DIR=$fixtures",
    '--token', "AETHER_HOME=$root",
    '--allowed-root', $root,
    '--operator-app', 'aether.shell',
    '--endpoint-file', $endpointFile,
    '--layout-store', $layoutStore,
    '--child-log-dir', $childLogs,
    '--max-actions', '6'
)

$deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
while ([DateTime]::UtcNow -lt $deadline) {
    if (Test-Path -LiteralPath $endpointFile) { break }
    if ($brokerProcess.HasExited) {
        throw "The broker exited with code $($brokerProcess.ExitCode). Check that the .NET 10 Desktop Runtime is installed."
    }
    Start-Sleep -Milliseconds 250
}

if (-not (Test-Path -LiteralPath $endpointFile)) {
    throw "The broker did not write its handshake file within $StartupTimeoutSeconds seconds."
}

Write-Host "Broker ready: $endpointFile"
Write-Host 'Starting WPF shell...'
$env:AETHER_ENDPOINT_FILE = $endpointFile
Start-Process -FilePath $shell -WorkingDirectory $root | Out-Null

Write-Host ''
Write-Host 'AetherWorkspace desktop reference is starting.'
Write-Host 'Run the pre-filled reference command in the shell. Close the shell and stop the broker process when finished.'
Write-Host "Broker PID: $($brokerProcess.Id)"
