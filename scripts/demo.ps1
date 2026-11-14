#!/usr/bin/env pwsh
# End-to-end reference workflow demo for Windows (named pipes by default).
# All data is synthetic; IPC is local only.
$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Config = if ($env:CONFIG) { $env:CONFIG } else { 'Release' }
$RunDir = Join-Path $Root '.aether/win-demo'
$EndpointFile = Join-Path $RunDir 'broker-endpoint.json'
$BrokerAudit = Join-Path $RunDir 'broker-audit.jsonl'
$ShellAudit = Join-Path $RunDir 'shell-audit.jsonl'
$ResultFile = Join-Path $RunDir 'result.json'
$ChildLogs = Join-Path $RunDir 'child-logs'
$BrokerOut = Join-Path $RunDir 'broker.out'
$BrokerErr = Join-Path $RunDir 'broker.err'

if ($env:SKIP_BUILD -ne '1') {
    Write-Host '== building (Release) =='
    dotnet build (Join-Path $Root 'src/Aether.BrokerApp/Aether.BrokerApp.csproj') -c $Config -v q --nologo
    dotnet build (Join-Path $Root 'src/Aether.SampleApps.Headless/Aether.SampleApps.Headless.csproj') -c $Config -v q --nologo
    dotnet build (Join-Path $Root 'src/Aether.Shell.Headless/Aether.Shell.Headless.csproj') -c $Config -v q --nologo
}

Remove-Item -Recurse -Force $RunDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $RunDir -Force | Out-Null

$dotnet = (Get-Command dotnet).Source
$samplesDll = Join-Path $Root "src/Aether.SampleApps.Headless/bin/$Config/net10.0/Aether.SampleApps.Headless.dll"
$brokerDll = Join-Path $Root "src/Aether.BrokerApp/bin/$Config/net10.0/Aether.BrokerApp.dll"
$shellDll = Join-Path $Root "src/Aether.Shell.Headless/bin/$Config/net10.0/Aether.Shell.Headless.dll"
foreach ($required in @($samplesDll, $brokerDll, $shellDll)) {
    if (-not (Test-Path $required)) { throw "missing $required" }
}

function Quote-Arg([string]$value) {
    if ($value -match '\s') { return '"' + $value.Replace('"', '\"') + '"' }
    return $value
}

$brokerArgs = @(
    (Quote-Arg $brokerDll),
    '--registry', (Quote-Arg (Join-Path $Root 'samples/registry/apps.json')),
    '--contexts', (Quote-Arg (Join-Path $Root 'samples/contexts/contexts.json')),
    '--token', (Quote-Arg "DOTNET=$dotnet"),
    '--token', (Quote-Arg "SAMPLES_DLL=$samplesDll"),
    '--token', (Quote-Arg "AETHER_HOME=$Root"),
    '--token', (Quote-Arg "FIXTURES_DIR=$(Join-Path $Root 'samples/fixtures')"),
    '--allowed-root', (Quote-Arg (Split-Path -Parent $dotnet)),
    '--allowed-root', (Quote-Arg $Root),
    '--endpoint-file', (Quote-Arg $EndpointFile),
    '--audit-file', (Quote-Arg $BrokerAudit),
    '--child-log-dir', (Quote-Arg $ChildLogs),
    '--max-actions', '6'
)

Write-Host '== starting local broker (Windows named pipe by default) =='
$broker = Start-Process -FilePath $dotnet -ArgumentList $brokerArgs -WorkingDirectory $Root -PassThru -RedirectStandardOutput $BrokerOut -RedirectStandardError $BrokerErr -WindowStyle Hidden
try {
    $deadline = (Get-Date).AddSeconds(30)
    while (-not (Test-Path $EndpointFile)) {
        if ($broker.HasExited) { throw "broker exited unexpectedly: $(Get-Content $BrokerErr -Raw -ErrorAction SilentlyContinue)" }
        if ((Get-Date) -gt $deadline) { throw 'broker did not become ready' }
        Start-Sleep -Milliseconds 100
    }
    $endpointInfo = Get-Content $EndpointFile -Raw | ConvertFrom-Json
    Write-Host "broker endpoint: $($endpointInfo.transport) $($endpointInfo.endpoint)"

    $command = 'Open Northstar bond analytics, show the associated risk view, synchronize this instrument across the workspace and summarize the exposure privately.'
    Write-Host '== running reference workflow (classification RESTRICTED, cloud disabled) =='
    & $dotnet $shellDll --endpoint-file $EndpointFile --classification RESTRICTED --command $command --audit-file $ShellAudit --output-file $ResultFile --max-actions 6 --text
    $shellExit = $LASTEXITCODE

    Write-Host ''
    Write-Host '== evidence =='
    $result = Get-Content $ResultFile -Raw | ConvertFrom-Json
    Write-Host "trace:          $($result.traceId)"
    Write-Host "success:        $($result.success) ($($result.status))"
    Write-Host "plan:           $($result.planId) planner=$($result.planner) usedModel=$($result.usedModel)"
    Write-Host "steps:          $((($result.steps | ForEach-Object { "$($_.action):$($_.state)" }) -join ', '))"
    $publish = $result.steps | Where-Object { $_.action -eq 'PublishContext' -and $_.artifacts } | Select-Object -First 1
    if ($publish) { Write-Host "delivered:      $($publish.artifacts.deliveredCount) subscriber(s), dropped=$($publish.artifacts.droppedCount)" }
    if ($result.summary) {
        Write-Host "summary:        $($result.summary.text)"
        Write-Host "summary route:  $($result.summary.provider) / $($result.summary.classification)"
    }
    Write-Host "egress:         attempts=$($result.egress.attempts) external=$($result.egress.externalAttempts) denied=$($result.egress.denied)"
    Write-Host "audit:          entries=$($result.audit.entryCount) chainValid=$($result.audit.chainValid)"

    if ($shellExit -ne 0) { throw "reference workflow failed with exit code $shellExit" }
    if (-not $result.success) { throw 'reference workflow reported denial' }
    if ($result.egress.externalAttempts -ne 0) { throw 'unexpected external egress attempts for RESTRICTED data' }
    if (-not $result.audit.chainValid) { throw 'audit chain failed verification' }

    $contextEvents = 0
    if (Test-Path $ChildLogs) {
        $contextEvents = (Select-String -Path (Join-Path $ChildLogs '*.log') -Pattern '"event":"context"' -ErrorAction SilentlyContinue | Measure-Object).Count
    }
    Write-Host "child context events: $contextEvents"
    if ($contextEvents -lt 2) { throw 'expected at least two sample apps to receive the context' }
    Write-Host ''
    Write-Host 'Demo finished successfully. Artifacts: ' $RunDir
}
finally {
    if ($broker -and -not $broker.HasExited) { Stop-Process -Id $broker.Id -Force -ErrorAction SilentlyContinue }
    Get-Process -Name 'Aether.SampleApps.Headless' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}
