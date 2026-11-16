#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Collects a privacy-safe diagnostics archive for a packaged desktop run.
.DESCRIPTION
  Gathers bundle/process information, child logs, workflow self-test results and recent relevant
  Event Log errors into a ZIP. The broker endpoint handshake file (contains an operator token) is
  deliberately excluded. Logs contain synthetic workflow data only; review before sharing.
#>
[CmdletBinding()]
param(
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$work = Join-Path $env:TEMP "aether-diagnostics-$stamp"
$output = if ($OutputPath) { $OutputPath } else { Join-Path $root "aether-diagnostics-$stamp.zip" }

New-Item -ItemType Directory -Force -Path $work | Out-Null

function Copy-IfExists([string] $Source, [string] $DestinationDirectory) {
    if (Test-Path -LiteralPath $Source) {
        Copy-Item -LiteralPath $Source -Destination $DestinationDirectory -Recurse -Force
    }
}

$summary = Join-Path $work 'summary.txt'
$lines = @(
    "AetherWorkspace desktop diagnostics",
    "collected: $(Get-Date -Format o)",
    "bundle root: $root",
    "os: $([System.Environment]::OSVersion.VersionString)",
    "powershell: $($PSVersionTable.PSVersion)",
    "dotnet: $(dotnet --version 2>$null)",
    "",
    "Top-level files:"
)
$lines += (Get-ChildItem -LiteralPath $root -Force | Select-Object -ExpandProperty Name)
$lines += @('', 'Processes:')
$lines += (Get-Process Aether*, dotnet -ErrorAction SilentlyContinue |
    Select-Object Id, ProcessName, StartTime |
    Format-Table -AutoSize | Out-String).Split([Environment]::NewLine)
$lines += @('', 'Endpoint handshake present (contents excluded deliberately):')
$lines += (Test-Path -LiteralPath (Join-Path $root 'endpoint.json'))
$lines += @('', 'Child log files:')
$logs = Join-Path $root 'logs'
if (Test-Path -LiteralPath $logs) {
    $lines += (Get-ChildItem -LiteralPath $logs -File | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize | Out-String).Split([Environment]::NewLine)
}
$lines += @('', 'Recent Application/.NET Runtime errors:')
$lines += (Get-WinEvent -LogName Application -MaxEvents 100 -ErrorAction SilentlyContinue |
    Where-Object { $_.ProviderName -match 'Application Error|\.NET Runtime' } |
    Select-Object -First 10 |
    Format-List TimeCreated, ProviderName, Message | Out-String).Split([Environment]::NewLine)
$lines | Set-Content -LiteralPath $summary -Encoding utf8

Copy-IfExists -Source $logs -DestinationDirectory $work
Copy-IfExists -Source (Join-Path $root 'diagnostics') -DestinationDirectory $work
Copy-IfExists -Source (Join-Path $root 'README.txt') -DestinationDirectory $work

Compress-Archive -Path (Join-Path $work '*') -DestinationPath $output -Force
Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "diagnostics archive: $output"
Write-Host 'Privacy: endpoint.json (operator token) is excluded; logs/results are synthetic-only.'
