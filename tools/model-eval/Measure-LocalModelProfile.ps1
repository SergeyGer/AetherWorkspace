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
    [int] $MaxItems = 0,
    [string] $Corpus = 'docs/evaluation/command-corpus-v1.3.json'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repoRoot

if (-not (Get-Command ollama -ErrorAction SilentlyContinue)) {
    throw 'Ollama is not installed. Install it from https://ollama.com/download and run: ollama pull ' + $Model
}

$models = & ollama list 2>$null
if ($LASTEXITCODE -ne 0 -or -not (($models -join "`n") -match [regex]::Escape($Model))) {
    throw "Local model '$Model' is not installed. Run: ollama pull $Model (see docs/evaluation/local-model-profile-qwen2.5-7b.md)."
}

$server = @(Get-Process -Name 'ollama*' -ErrorAction SilentlyContinue | Sort-Object WorkingSet64 -Descending | Select-Object -First 1)
if ($server.Count -eq 0) {
    throw 'The Ollama server process is not running. Start it with: ollama serve'
}

$runner = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match 'llama-server' })
$loadedModels = try { (Invoke-RestMethod -Uri 'http://127.0.0.1:11434/api/ps' -TimeoutSec 5).models } catch { @() }
$loadedModelSizeBytes = @($loadedModels | ForEach-Object { [double]$_.size } | Measure-Object -Sum).Sum

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
$limitArgs = @()
if ($MaxItems -gt 0) { $limitArgs = @('--limit', [string]$MaxItems) }
& dotnet run -c Release --no-build --project tools/Aether.ModelEval -- `
    --provider local --corpus $Corpus --out $evalReport --trials $Trials @limitArgs
$evalExit = $LASTEXITCODE
if ($evalExit -ne 0) {
    throw "Local evaluation failed with exit code $evalExit; no measurement claim is made."
}

$afterServer = Get-Process -Name 'ollama*' -ErrorAction SilentlyContinue | Sort-Object WorkingSet64 -Descending | Select-Object -First 1
$afterRunner = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match 'llama-server' })
$runnerWorkingSet = @($afterRunner | Measure-Object -Property WorkingSet64 -Sum).Sum
$serverWorkingSet = if ($afterServer) { $afterServer.WorkingSet64 } else { 0 }
$evaluation = Get-Content -LiteralPath $evalReport -Raw | ConvertFrom-Json
if ($IsWindows) {
    $hardware = [ordered]@{
        os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
        cpu = (Get-CimInstance Win32_Processor | Select-Object -First 1 -ExpandProperty Name)
        logicalCpus = [Environment]::ProcessorCount
        totalRamGb = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 1)
        gpu = (Get-CimInstance Win32_VideoController | Select-Object -ExpandProperty Name) -join '; '
    }
}
else {
    $cpuLine = (Get-Content -LiteralPath '/proc/cpuinfo' | Where-Object { $_ -match '^model name' } | Select-Object -First 1)
    $memKb = (Get-Content -LiteralPath '/proc/meminfo' | Where-Object { $_ -match '^MemTotal:' } | Select-Object -First 1) -replace '[^0-9]', ''
    $hardware = [ordered]@{
        os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
        cpu = ($cpuLine -replace '.*:\s*', '')
        logicalCpus = [Environment]::ProcessorCount
        totalRamGb = [math]::Round([double]$memKb / 1MB, 1)
        gpu = (Get-ChildItem -LiteralPath '/dev/dri' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name) -join '; '
    }
}
$measurement = [ordered]@{
    schemaVersion = '1.0'
    kind = 'local-model-physical-measurement'
    measuredAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    model = $Model
    endpointHost = ([uri]$Endpoint).Host
    trials = $Trials
    maxItems = $MaxItems
    hardware = $hardware
    ollamaServerWorkingSetMb = [math]::Round($serverWorkingSet / 1MB, 1)
    modelRunnerWorkingSetMb = [math]::Round($runnerWorkingSet / 1MB, 1)
    localInferenceWorkingSetMb = [math]::Round(($serverWorkingSet + $runnerWorkingSet) / 1MB, 1)
    loadedModelSizeMb = if ($loadedModelSizeBytes -gt 0) { [math]::Round($loadedModelSizeBytes / 1MB, 1) } else { $null }
    loadedModelNames = @($loadedModels | ForEach-Object { $_.name })
    eval = [ordered]@{
        report = (Resolve-Path -Relative $evalReport)
        evaluatedItems = $evaluation.corpus.evaluatedItems
        limit = $evaluation.corpus.limit
        semanticPlanAccuracy = $evaluation.metrics.semanticPlanAccuracy
        denialAccuracy = $evaluation.metrics.denialAccuracy
        latencyMs = $evaluation.metrics.latencyMs
        ttftMs = $evaluation.metrics.ttftMs
        tokens = $evaluation.metrics.tokens
        cost = $evaluation.metrics.cost
    }
    note = 'Measured values only (server + model-runner working sets and Ollama /api/ps loaded model size). TTFT remains unavailable for the non-streaming adapter; reported as a deviation.'
}
$measurementPath = Join-Path $repoRoot (Join-Path $ReportDirectory "local-model-measurement-$stamp.json")
$measurement | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $measurementPath -Encoding utf8
Write-Host "local model measurement: $measurementPath"
Write-Host "attach this JSON plus the eval report; do not claim values that are not in them."
