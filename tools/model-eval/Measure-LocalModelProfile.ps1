#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Reproducible local-model profile measurement for v1.3 AW-067 (operator-run, physical machine).
.DESCRIPTION
  Requires Ollama and the supported model to be installed already. Runs the labelled v1.3 gold
  corpus through the local provider with two trials, measures the Ollama server working set and
  writes a sanitized measurement report. It does NOT install models, change network configuration
  or invent values: if the model/runtime is absent the script fails with guidance.
#>
[CmdletBinding()]
param(
    [string] $Model = 'qwen2.5:7b',
    [string] $Endpoint = 'http://127.0.0.1:11434/v1/chat/completions',
    [string] $ReportDirectory = '.aether/evaluation',
    [int] $Trials = 2,
    [string] $Corpus = 'docs/evaluation/command-corpus-v1.3.json'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repoRoot

if (-not (Get-Command ollama -ErrorAction SilentlyContinue)) {
    throw 'Ollama is not installed. Install it from https://ollama.com/download and run: ollama pull ' + $Model
}

$models = & ollama list 2>$null
if ($LASTEXITCODE -ne 0 -or ($models -notmatch [regex]::Escape($Model))) {
    throw "Local model '$Model' is not installed. Run: ollama pull $Model (see docs/evaluation/local-model-profile-qwen2.5-7b.md)."
}

$server = @(Get-Process -Name 'ollama*' -ErrorAction SilentlyContinue | Sort-Object WorkingSet64 -Descending | Select-Object -First 1)
if ($server.Count -eq 0) {
    throw 'The Ollama server process is not running. Start it with: ollama serve'
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
New-Item -ItemType Directory -Force -Path (Join-Path $repoRoot $ReportDirectory) | Out-Null
$evalReport = Join-Path $repoRoot (Join-Path $ReportDirectory "local-model-eval-$stamp.json")

$env:AETHER_PLANNER_PROVIDER = 'local'
$env:AETHER_SUMMARY_PROVIDER = 'local'
$env:AETHER_LOCAL_MODEL_ENDPOINT = $Endpoint
$env:AETHER_LOCAL_MODEL = $Model
$env:AETHER_EVAL_HARDWARE_PROFILE = 'physical-windows11-reference'
$env:AETHER_EVAL_GPU_PROFILE = 'operator-recorded' # set a truthful GPU description if measured

& dotnet build tools/Aether.ModelEval/Aether.ModelEval.csproj -c Release
& dotnet run -c Release --no-build --project tools/Aether.ModelEval -- `
    --provider local --corpus $Corpus --out $evalReport --trials $Trials
$evalExit = $LASTEXITCODE
if ($evalExit -ne 0) {
    throw "Local evaluation failed with exit code $evalExit; no measurement claim is made."
}

$after = Get-Process -Id $server[0].Id -ErrorAction Stop
$evaluation = Get-Content -LiteralPath $evalReport -Raw | ConvertFrom-Json
$hardware = [ordered]@{
    os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    cpu = (Get-CimInstance Win32_Processor | Select-Object -First 1 -ExpandProperty Name)
    logicalCpus = [Environment]::ProcessorCount
    totalRamGb = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 1)
    gpu = (Get-CimInstance Win32_VideoController | Select-Object -ExpandProperty Name) -join '; '
}
$measurement = [ordered]@{
    schemaVersion = '1.0'
    kind = 'local-model-physical-measurement'
    measuredAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    model = $Model
    endpointHost = ([uri]$Endpoint).Host
    trials = $Trials
    hardware = $hardware
    ollamaServerWorkingSetMb = [math]::Round($after.WorkingSet64 / 1MB, 1)
    eval = [ordered]@{
        report = (Resolve-Path -Relative $evalReport)
        semanticPlanAccuracy = $evaluation.metrics.semanticPlanAccuracy
        denialAccuracy = $evaluation.metrics.denialAccuracy
        latencyMs = $evaluation.metrics.latencyMs
        ttftMs = $evaluation.metrics.ttftMs
        tokens = $evaluation.metrics.tokens
        cost = $evaluation.metrics.cost
    }
    note = 'Measured values only. TTFT remains unavailable for the non-streaming adapter; reported as a deviation.'
}
$measurementPath = Join-Path $repoRoot (Join-Path $ReportDirectory "local-model-measurement-$stamp.json")
$measurement | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $measurementPath -Encoding utf8
Write-Host "local model measurement: $measurementPath"
Write-Host "attach this JSON plus the eval report; do not claim values that are not in them."
