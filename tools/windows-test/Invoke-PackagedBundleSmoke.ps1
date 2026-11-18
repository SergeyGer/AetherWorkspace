#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Fresh-extraction smoke test for the published Windows desktop bundle (v1.1 AW-054).
.DESCRIPTION
  Extracts the release ZIP into a clean temporary directory, runs the bundled headless reference
  workflow (Test-AetherWorkflow.ps1), validates the structured result (status=succeeded, six steps),
  confirms no owned sample processes leaked after cleanup, and writes a sanitized smoke manifest for
  CI/release evidence. No source checkout or interactive UI automation is required.
.PARAMETER BundleZip
  Path to AetherWorkspace-<tag>-windows-x64-desktop.zip.
.PARAMETER ResultsDirectory
  Directory (relative to the repository root) receiving the manifest, workflow result and logs.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $BundleZip,

    [string] $ResultsDirectory = 'artifacts/packaged-smoke',

    [string] $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
)

$ErrorActionPreference = 'Stop'
$zip = (Resolve-Path -LiteralPath $BundleZip).Path
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$work = Join-Path $env:TEMP "aether-packaged-smoke-$stamp"
$results = Join-Path $RepoRoot $ResultsDirectory
New-Item -ItemType Directory -Force -Path $work, $results | Out-Null

$started = Get-Date
$selfTestExit = $null
$status = $null
$steps = 0
$failure = $null
$leaked = @()

try {
    Expand-Archive -LiteralPath $zip -DestinationPath $work -Force
    $selfTest = Join-Path $work 'Test-AetherWorkflow.ps1'
    if (-not (Test-Path -LiteralPath $selfTest)) {
        throw "The bundle does not contain Test-AetherWorkflow.ps1."
    }

    & $selfTest *>&1 | Tee-Object -FilePath (Join-Path $results 'self-test.log')
    $selfTestExit = $LASTEXITCODE

    $diagnostics = Join-Path $work 'diagnostics'
    if (Test-Path -LiteralPath $diagnostics) {
        Copy-Item (Join-Path $diagnostics '*') -Destination $results -Recurse -Force -ErrorAction SilentlyContinue
        $resultFile = Get-ChildItem -LiteralPath $diagnostics -Filter 'workflow-result-*.json' |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($resultFile) {
            $result = Get-Content -LiteralPath $resultFile.FullName -Raw | ConvertFrom-Json
            $status = $result.status
            $steps = @($result.steps).Count
        }
    }

    $leaked = @(Get-Process Aether.SampleApps, Aether.SampleApps.WebView2, Aether.Shell -ErrorAction SilentlyContinue |
        Where-Object { $_.StartTime -ge $started })
}
catch {
    $failure = $_.Exception.Message
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

$pass = $selfTestExit -eq 0 -and $status -eq 'succeeded' -and $steps -eq 6 -and $leaked.Count -eq 0 -and -not $failure
$manifest = [ordered]@{
    schemaVersion    = '1.0'
    kind             = 'packaged-desktop-smoke'
    runAtUtc         = (Get-Date).ToUniversalTime().ToString('o')
    bundle           = Split-Path -Leaf $zip
    bundleSha256     = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    commitSha        = (git -C $RepoRoot rev-parse HEAD 2>$null | Out-String).Trim()
    selfTestExit     = $selfTestExit
    workflowStatus   = $status
    workflowSteps    = $steps
    leakedOwnedProcesses = $leaked.Count
    failure          = $failure
    pass             = $pass
}
$manifestPath = Join-Path $results "smoke-$stamp.json"
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding utf8

Write-Host "packaged smoke manifest: $manifestPath"
if (-not $pass) {
    Write-Host "PACKAGED SMOKE FAIL: exit=$selfTestExit status=$status steps=$steps leaked=$($leaked.Count) failure=$failure"
    exit 1
}

Write-Host "PACKAGED SMOKE PASS: fresh extraction, reference workflow succeeded, six steps, clean cleanup."
exit 0
