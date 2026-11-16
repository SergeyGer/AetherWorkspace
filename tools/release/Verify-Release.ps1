#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Verifies a published AetherWorkspace release: SHA-256 checksums and SPDX SBOM.
.DESCRIPTION
  Offline verification of downloaded release assets. Exit code 0 = PASS, 1 = verification failure.
.EXAMPLE
  pwsh tools/release/Verify-Release.ps1 -AssetDirectory ./release/v1.0.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $AssetDirectory
)

$ErrorActionPreference = 'Stop'
$failures = 0
$AssetDirectory = (Resolve-Path -LiteralPath $AssetDirectory).Path

$sumsFile = Get-ChildItem -LiteralPath $AssetDirectory -Filter 'SHA256SUMS-*.txt' | Select-Object -First 1
if (-not $sumsFile) {
    Write-Error "No SHA256SUMS-*.txt file found in '$AssetDirectory'."
    exit 1
}

Write-Host "checksums: $($sumsFile.Name)"
foreach ($line in Get-Content -LiteralPath $sumsFile.FullName) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    if ($line -notmatch '^([0-9a-fA-F]{64})\s+\*?(.+?)\s*$') {
        Write-Host "FAIL malformed checksum line: $line"
        $failures++
        continue
    }

    $expected = $matches[1].ToLowerInvariant()
    $fileName = $matches[2]
    $assetPath = Join-Path $AssetDirectory $fileName
    if (-not (Test-Path -LiteralPath $assetPath)) {
        Write-Host "FAIL missing asset: $fileName"
        $failures++
        continue
    }

    $actual = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -eq $expected) {
        Write-Host "OK   $fileName"
    }
    else {
        Write-Host "FAIL checksum mismatch: $fileName"
        $failures++
    }
}

$sbomPath = Join-Path $AssetDirectory 'manifest.spdx.json'
if (-not (Test-Path -LiteralPath $sbomPath)) {
    Write-Host 'FAIL missing SPDX SBOM: manifest.spdx.json'
    $failures++
}
else {
    try {
        $sbom = Get-Content -LiteralPath $sbomPath -Raw | ConvertFrom-Json
        if (-not $sbom.spdxVersion) {
            Write-Host 'FAIL SBOM has no spdxVersion.'
            $failures++
        }
        else {
            $packageCount = @($sbom.packages).Count
            Write-Host "SBOM spdxVersion=$($sbom.spdxVersion), packages=$packageCount"
        }
    }
    catch {
        Write-Host "FAIL SBOM is not valid JSON: $($_.Exception.Message)"
        $failures++
    }
}

if ($failures -gt 0) {
    Write-Host "VERIFY FAIL ($failures problem(s))"
    exit 1
}

Write-Host 'VERIFY PASS'
exit 0
