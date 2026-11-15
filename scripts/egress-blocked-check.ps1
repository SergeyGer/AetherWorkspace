#!/usr/bin/env pwsh
# OS-level restricted-egress evidence on Windows: block outbound traffic for the .NET
# host via Windows Firewall, prove external connectivity is blocked, then run the
# named-pipe reference demo (which asserts zero external egress attempts).
$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$ruleName = 'AetherWorkspace-EgressBlock'
$dotnet = (Get-Command dotnet).Source
$pwsh = (Get-Process -Id $PID).Path
$ruleCreated = $false
try {
    try {
        New-NetFirewallRule -DisplayName "$ruleName-dotnet" -Direction Outbound -Action Block -Program $dotnet -Profile Any | Out-Null
        New-NetFirewallRule -DisplayName "$ruleName-pwsh" -Direction Outbound -Action Block -Program $pwsh -Profile Any | Out-Null
        $ruleCreated = $true
    } catch {
        Write-Warning "egress-blocked-check: SKIP (cannot create firewall rule: $($_.Exception.Message))"
        exit 0
    }

    Write-Host '== OS-level blocked-egress check: outbound firewall block for dotnet.exe =='
    $blocked = $true
    try {
        $client = [System.Net.Http.HttpClient]::new()
        $client.Timeout = [TimeSpan]::FromSeconds(8)
        $null = $client.GetAsync('https://api.nuget.org/v3/index.json').GetAwaiter().GetResult()
        $blocked = $false
    } catch {
        $blocked = $true
    }
    if (-not $blocked) { throw 'external connectivity unexpectedly available despite firewall block' }
    Write-Host 'external HTTPS unreachable as expected'

    & pwsh -NoProfile -File (Join-Path $Root 'scripts/demo.ps1')
    if ($LASTEXITCODE -ne 0) { throw "reference demo failed under blocked egress (exit $LASTEXITCODE)" }
    Write-Host '== blocked-egress named-pipe workflow succeeded with zero external attempts =='
}
finally {
    if ($ruleCreated) {
        Remove-NetFirewallRule -DisplayName "$ruleName-dotnet" -ErrorAction SilentlyContinue
        Remove-NetFirewallRule -DisplayName "$ruleName-pwsh" -ErrorAction SilentlyContinue
    }
}
