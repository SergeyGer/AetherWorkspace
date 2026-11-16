#Requires -Version 7.0
<#
.SYNOPSIS
    Local-only orchestrator for the AetherWorkspace Windows 11 reference-desktop test run
    (v3.0 Addendum A01, lifecycle stages 01 TRIGGER .. 08 RESULT).

.DESCRIPTION
    Runs the automated acceptance evidence for a reviewed revision on a dedicated Windows 11
    reference desktop. The orchestrator never runs on a GitHub-hosted runner and never
    bypasses Windows security:

      01 TRIGGER      Manual invocation or a per-user Task Scheduler trigger ("run only when
                      the user is logged on"; no SYSTEM, no wake/unlock).
      02 TRUST        Reads a reviewed full 40-hex SHA from an approved-revision file and
                      verifies that the commit exists in the trusted checkout.
      03 PREFLIGHT    Hard gates: interactive unlocked desktop (LogonUI/PSSession heuristic),
                      memory/CPU/display minimums, AC power, free disk space, single-instance
                      mutex ("no overlap"). Any failed gate becomes SKIPPED_* with exit code 0.
      04 PREPARE      Creates a detached worktree under <runsRoot>\<run-id> from the approved
                      SHA and builds Release (solution + Windows UI projects).
      05 EXECUTE      Runs the profile's dotnet test suites from that worktree with bounded,
                      redacted logging, no inbound listeners and no display/firewall changes.
      06 ASSERT/CAPTURE  Writes environment-manifest.json and run-result.json, collects TRX,
                      logs, build info and privacy/cleanup flags.
      07 CLEANUP      Kills only processes owned by the run worktree (paths under
                      <runsRoot>\<run-id>), removes the detached worktree, releases the mutex.
                      Nothing global is restored or modified (display settings, firewall and
                      machine environment are never touched).
      08 RESULT       PASS / FAIL_PRODUCT / FAIL_HARNESS / SKIPPED_* / INCONCLUSIVE with a
                      deterministic exit code (see README.md).

    This script is the local-only replacement for running desktop GUI acceptance tests on
    GitHub runners. The desktop E2E project (tests/Aether.Desktop.E2E.Tests) is not part of
    AetherWorkspace.slnx and must not be added there.

.PARAMETER Profile
    daily-smoke | release-full | developer-smoke. Suite selection comes from
    test-config.json (profiles.<name>.suites) with built-in defaults.

.PARAMETER ApprovedRevisionFile
    Path to the reviewed revision file. Exactly one full 40-hex SHA must be present (blank
    lines and '#' comments are ignored). Defaults to
    <trustedRoot>\approved-revision.txt.

.PARAMETER RepoRoot
    Path to the trusted git checkout. Defaults to two levels above this script (the
    repository checkout); a deployed copy under C:\AetherWorkspace\automation must pass
    -RepoRoot explicitly.

.PARAMETER ResultsRoot
    Directory for run evidence. Defaults to <paths.resultsRoot> from test-config.json, or
    C:\AetherWorkspace\results.

.PARAMETER ConfigPath
    Optional test-config.json. Defaults to test-config.json next to this script; when no
    file exists the built-in defaults are used.

.EXAMPLE
    pwsh -File .\Run-AetherTests.ps1 -Profile release-full

.EXAMPLE
    pwsh -File .\Run-AetherTests.ps1 -Profile developer-smoke `
        -RepoRoot C:\AetherWorkspace\trusted\AetherWorkspace `
        -ApprovedRevisionFile C:\AetherWorkspace\trusted\approved-revision.txt

.NOTES
    Requires PowerShell 7, an interactive unlocked desktop session and the .NET 10 SDK.
    HTTPS is only needed for NuGet package restore over TLS if packages are not cached; the
    run opens no inbound listener. Scripts are proposed-but-not-executed in the Linux
    development workspace (see README.md).
#>
[CmdletBinding()]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute(
    'PSAvoidUsingWriteHost',
    '',
    Justification = 'The orchestrator prints bounded progress to the interactive console; persisted evidence is redacted and file-based.')]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('daily-smoke', 'release-full', 'developer-smoke')]
    [string] $Profile,

    [string] $ApprovedRevisionFile = '',

    [string] $RepoRoot = '',

    [string] $ResultsRoot = '',

    [string] $ConfigPath = ''
)

Set-StrictMode -Version 1.0
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$script:DefaultInstallRoot = 'C:\AetherWorkspace'
$script:DefaultReleaseTarget = 'v0.4.0'
$script:DefaultResultsRoot = 'C:\AetherWorkspace\results'
$script:DefaultRunsRoot = 'C:\AetherWorkspace\runs'
$script:DefaultTrustedRoot = 'C:\AetherWorkspace\trusted'
$script:DefaultMutexName = 'Local\AetherWorkspace.ReferenceDesktop.Run'
$script:FullShaPattern = '^[0-9a-f]{40}$'

$script:SuiteDefinitions = @{
    'portable' = @{
        project = 'AetherWorkspace.slnx'
        filter = ''
        description = 'Portable .NET solution suites (contracts, security, execution, AI, broker, workflow, FDC3, E2E, testing helpers).'
    }
    'windows-ui' = @{
        project = 'tests/Aether.Shell.UiTests/Aether.Shell.UiTests.csproj'
        filter = 'Category=UI'
        description = 'Existing Windows WPF shell UI automation (smoke + restricted workflow, Unix-domain-socket harness).'
    }
    'desktop-e2e' = @{
        project = 'tests/Aether.Desktop.E2E.Tests/Aether.Desktop.E2E.Tests.csproj'
        filter = 'Category=ReferenceDesktop'
        description = 'Reference-desktop GUI end-to-end tests over the Windows named-pipe transport (WT-04-01/02).'
    }
}

$script:SuiteDefaults = @{
    'daily-smoke' = @('windows-ui', 'desktop-e2e')
    'release-full' = @('portable', 'windows-ui', 'desktop-e2e')
    'developer-smoke' = @('desktop-e2e')
}

$script:state = @{
    Status = $null
    Stage = 'TRIGGER'
    Message = ''
    RunId = ''
    CommitSha = ''
    RepoRoot = ''
    WorktreePath = ''
    RunsRoot = ''
    ResultsRoot = ''
    ResultsDirectory = ''
    Config = $null
    ConfigPath = ''
    Profile = ''
    ReleaseTarget = $script:DefaultReleaseTarget
    StartedAtUtc = (Get-Date).ToUniversalTime()
    FinishedAtUtc = $null
    Suites = [System.Collections.Generic.List[object]]::new()
    Builds = [System.Collections.Generic.List[object]]::new()
    Failures = [System.Collections.Generic.List[object]]::new()
    Artifacts = [System.Collections.Generic.List[object]]::new()
    LogLines = [System.Collections.Generic.List[string]]::new()
    Environment = $null
    Preflight = $null
    InboundListeners = @()
    OwnedProcessIds = @()
    Mutex = $null
    MutexHeld = $false
    WorktreeCreated = $false
    WorktreeRemoved = $false
    CleanupWarnings = [System.Collections.Generic.List[string]]::new()
    ApprovedRevisionFile = ''
}

$script:options = @{
    Profile = $Profile
    ApprovedRevisionFile = $ApprovedRevisionFile
    RepoRoot = $RepoRoot
    ResultsRoot = $ResultsRoot
    ConfigPath = $ConfigPath
}

function Write-RunLog {
    <#
    .SYNOPSIS
        Bounded, redaction-aware run log (also capped in memory; written by Write-BoundedLog).
    #>
    param(
        [Parameter(Mandatory = $true)][string] $Tag,
        [Parameter(Mandatory = $true)][string] $Message,
        [ValidateSet('INFO', 'WARN', 'ERROR', 'SKIP')][string] $Level = 'INFO'
    )

    $line = '[{0}] [{1}] [{2}] {3}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Tag, $Level, $Message
    $script:state.LogLines.Add($line)
    if ($script:state.LogLines.Count -gt 5000) {
        $script:state.LogLines.RemoveAt(0)
    }

    Write-Host $line
}

function Get-RedactedText {
    <#
    .SYNOPSIS
        Removes credentials/tokens from captured command output before it is persisted.
    #>
    param([string] $Text)

    if ([string]::IsNullOrEmpty($Text)) {
        return ''
    }

    $result = $Text
    $patterns = @(
        @{ pattern = '(?i)("operatorToken"\s*:\s*")[^"]+'; replacement = '$1<redacted>' }
        @{ pattern = '(?i)("sessionSecret"\s*:\s*")[^"]+'; replacement = '$1<redacted>' }
        @{ pattern = '(?i)("registrationToken"\s*:\s*")[^"]+'; replacement = '$1<redacted>' }
        @{ pattern = '(?i)("sessionId"\s*:\s*")[^"]+'; replacement = '$1<redacted>' }
        @{ pattern = '(?i)(\bBearer\s+)[A-Za-z0-9\-\._~\+/]+=*'; replacement = '$1<redacted>' }
        @{ pattern = '(?i)(--operator-token\s+)\S+'; replacement = '$1<redacted>' }
        @{ pattern = '(?i)(--token\s+)([A-Za-z_][A-Za-z0-9_]*=)\S+'; replacement = '$1$2<redacted>' }
        @{ pattern = '(?i)([A-Za-z0-9_.\-]*(?:token|secret|password)\s*[=:]\s*)("?)[^\s",'']+'; replacement = '$1$2<redacted>' }
        @{ pattern = '(?i)(\bapi[_-]?key\s*[=:]\s*)("?)[^\s",'']+'; replacement = '$1$2<redacted>' }
    )
    foreach ($entry in $patterns) {
        $result = [regex]::Replace($result, $entry['pattern'], $entry['replacement'])
    }

    return $result
}

function Write-BoundedLog {
    <#
    .SYNOPSIS
        Writes redacted text bounded to artifacts.maximumLogLines lines (head + tail kept).
    #>
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [string] $Text,
        [string] $Header = ''
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $redacted = Get-RedactedText -Text $Text
    $maximumLines = [int](Get-ConfigValue -Path 'artifacts.maximumLogLines' -Default 4000)
    if ($maximumLines -lt 200) {
        $maximumLines = 200
    }

    $lines = @()
    if (-not [string]::IsNullOrEmpty($redacted)) {
        $lines = @($redacted -split "`r?`n")
    }

    $bounded = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($Header)) {
        $bounded.Add($Header)
    }

    if ($lines.Count -le $maximumLines) {
        foreach ($line in $lines) {
            $bounded.Add($line)
        }
    }
    else {
        $headCount = [Math]::Min(50, $maximumLines)
        $tailCount = $maximumLines - $headCount
        for ($index = 0; $index -lt $headCount; $index++) {
            $bounded.Add($lines[$index])
        }

        $bounded.Add("... [$($lines.Count - $maximumLines) lines omitted by the bounded redacted logger] ...")
        for ($index = $lines.Count - $tailCount; $index -lt $lines.Count; $index++) {
            $bounded.Add($lines[$index])
        }
    }

    Set-Content -LiteralPath $Path -Value $bounded -Encoding utf8
}

function Get-ConfigValue {
    <#
    .SYNOPSIS
        Looks up a dotted path in the optional test-config.json object, returning Default.
    #>
    param(
        [string] $Path,
        $Default,
        $Config = $script:state.Config
    )

    $current = $Config
    foreach ($segment in $Path.Split('.')) {
        if ($null -eq $current) {
            return $Default
        }

        $property = $current.PSObject.Properties[$segment]
        if ($null -eq $property) {
            return $Default
        }

        $current = $property.Value
    }

    if ($null -eq $current) {
        return $Default
    }

    return $current
}

function Get-PathSetting {
    <#
    .SYNOPSIS
        Resolves a configured directory path, expanding environment variables.
    #>
    param([string] $SettingPath, [string] $Default)

    $value = [string](Get-ConfigValue -Path $SettingPath -Default $Default)
    if ([string]::IsNullOrWhiteSpace($value)) {
        $value = $Default
    }

    return [Environment]::ExpandEnvironmentVariables($value)
}

function Resolve-RepositoryRoot {
    <#
    .SYNOPSIS
        Resolves and validates the trusted git checkout root.
    #>
    param([string] $ConfiguredRoot)

    if (-not [string]::IsNullOrWhiteSpace($ConfiguredRoot)) {
        if (-not (Test-Path -LiteralPath $ConfiguredRoot -PathType Container)) {
            throw "Repository root '$ConfiguredRoot' does not exist."
        }

        $candidate = (Resolve-Path -LiteralPath $ConfiguredRoot).Path
    }
    else {
        $candidate = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
    }

    if (-not (Test-Path -LiteralPath (Join-Path $candidate '.git'))) {
        throw "Repository root '$candidate' has no .git directory. Pass -RepoRoot pointing at the trusted checkout."
    }

    return $candidate
}

function Read-TestConfiguration {
    <#
    .SYNOPSIS
        Loads the optional test-config.json (addendum section 8.3 shape).
    #>
    param([string] $Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Test configuration '$Path' was not found."
    }

    try {
        $json = Get-Content -LiteralPath $Path -Raw -ErrorAction Stop
        return ($json | ConvertFrom-Json -ErrorAction Stop)
    }
    catch {
        throw "Test configuration '$Path' is not valid JSON: $($_.Exception.Message)"
    }
}

function Get-ConfigurationPath {
    <#
    .SYNOPSIS
        Resolves the optional configuration file: explicit path, then next to this script.
    #>
    param([string] $ConfiguredPath)

    if (-not [string]::IsNullOrWhiteSpace($ConfiguredPath)) {
        if (-not (Test-Path -LiteralPath $ConfiguredPath -PathType Leaf)) {
            throw "Test configuration '$ConfiguredPath' was not found."
        }

        return (Resolve-Path -LiteralPath $ConfiguredPath).Path
    }

    $candidate = Join-Path $PSScriptRoot 'test-config.json'
    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
        return $candidate
    }

    return ''
}

function Initialize-RunContext {
    <#
    .SYNOPSIS
        Resolves paths, creates the evidence directory, and computes the run id.
    #>
    param(
        [Parameter(Mandatory = $true)][string] $ProfileName,
        [string] $RequestedRepoRoot,
        [string] $RequestedResultsRoot,
        [string] $RequestedApprovedRevisionFile,
        [string] $RequestedConfigPath
    )

    $script:state.Profile = $ProfileName
    $configurationPath = Get-ConfigurationPath -ConfiguredPath $RequestedConfigPath
    $script:state.Config = Read-TestConfiguration -Path $configurationPath
    $script:state.ConfigPath = $configurationPath
    if ([string]::IsNullOrWhiteSpace($configurationPath)) {
        Write-RunLog 'TRIGGER' 'No test-config.json found next to the script; using built-in defaults.'
    }
    else {
        Write-RunLog 'TRIGGER' "Using test configuration '$configurationPath'."
    }

    $script:state.RepoRoot = Resolve-RepositoryRoot -ConfiguredRoot $RequestedRepoRoot
    $script:state.ReleaseTarget = [string](Get-ConfigValue -Path 'release.target' -Default $script:DefaultReleaseTarget)

    $trustedRoot = Get-PathSetting -SettingPath 'paths.trustedRoot' -Default $script:DefaultTrustedRoot
    $script:state.RunsRoot = Get-PathSetting -SettingPath 'paths.runsRoot' -Default $script:DefaultRunsRoot

    if (-not [string]::IsNullOrWhiteSpace($RequestedResultsRoot)) {
        $script:state.ResultsRoot = [Environment]::ExpandEnvironmentVariables($RequestedResultsRoot)
    }
    else {
        $script:state.ResultsRoot = Get-PathSetting -SettingPath 'paths.resultsRoot' -Default $script:DefaultResultsRoot
    }

    if (-not [string]::IsNullOrWhiteSpace($RequestedApprovedRevisionFile)) {
        $script:state.ApprovedRevisionFile = [Environment]::ExpandEnvironmentVariables($RequestedApprovedRevisionFile)
    }
    else {
        $configuredRevisionFile = [string](Get-ConfigValue -Path 'release.approvedRevisionFile' -Default '')
        if ([string]::IsNullOrWhiteSpace($configuredRevisionFile)) {
            $configuredRevisionFile = Join-Path $trustedRoot 'approved-revision.txt'
        }

        $configuredRevisionFile = [Environment]::ExpandEnvironmentVariables($configuredRevisionFile)
        if (-not [System.IO.Path]::IsPathRooted($configuredRevisionFile)) {
            $configuredRevisionFile = Join-Path $script:state.RepoRoot $configuredRevisionFile
        }

        $script:state.ApprovedRevisionFile = $configuredRevisionFile
    }

    $runId = '{0}-{1}-{2}' -f (Get-Date -Format 'yyyyMMdd-HHmmss'), $ProfileName, ([guid]::NewGuid().ToString('n').Substring(0, 6))
    $script:state.RunId = $runId
    $script:state.ResultsDirectory = Join-Path $script:state.ResultsRoot $runId
    foreach ($subdirectory in @('logs', 'trx', 'manifests')) {
        New-Item -ItemType Directory -Path (Join-Path $script:state.ResultsDirectory $subdirectory) -Force | Out-Null
    }

    Write-RunLog 'TRIGGER' "Run id '$runId': repo '$($script:state.RepoRoot)', results '$($script:state.ResultsDirectory)', runs root '$($script:state.RunsRoot)'."
}

function Read-ApprovedRevision {
    <#
    .SYNOPSIS
        Reads the reviewed full 40-hex SHA; blank lines and '#' comments are ignored.
    #>
    param([string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return [pscustomobject]@{
            Ok = $false
            Sha = ''
            Message = "Approved revision file '$Path' was not found. A reference-desktop run requires a reviewed 40-hex SHA."
        }
    }

    $lines = @(
        Get-Content -LiteralPath $Path -ErrorAction Stop |
            ForEach-Object { ([string]$_).Trim() } |
            Where-Object { $_ -ne '' -and -not $_.StartsWith('#') }
    )

    if ($lines.Count -ne 1) {
        return [pscustomobject]@{
            Ok = $false
            Sha = ''
            Message = "Approved revision file '$Path' must contain exactly one SHA (ignoring blank lines and # comments); found $($lines.Count) content lines."
        }
    }

    $sha = ([string]$lines[0]).ToLowerInvariant()
    if ($sha -notmatch $script:FullShaPattern) {
        return [pscustomobject]@{
            Ok = $false
            Sha = ''
            Message = "Approved revision '$sha' is not a full 40-hex git SHA; short SHAs and branch names are not accepted."
        }
    }

    return [pscustomobject]@{ Ok = $true; Sha = $sha; Message = '' }
}

function Test-CommitExists {
    <#
    .SYNOPSIS
        Returns true when the approved SHA resolves to a commit in the trusted checkout.
    #>
    param([string] $Repository, [string] $Sha)

    $output = & git -C $Repository cat-file -e "$Sha^{commit}" 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-RunLog 'TRUST' "git cat-file -e failed for '$Sha': $($output | Out-String)" 'WARN'
        return $false
    }

    return $true
}

function Get-DotnetExecutable {
    <#
    .SYNOPSIS
        Resolves the dotnet muxer; failure is a harness problem, not a product failure.
    #>
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        throw "The .NET SDK ('dotnet') was not found on PATH. Install the .NET 10 SDK for the reference desktop."
    }

    return $command.Source
}

function Get-GitExecutable {
    <#
    .SYNOPSIS
        Resolves git; the orchestrator creates detached worktrees with it.
    #>
    $command = Get-Command git -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        throw "git was not found on PATH; the orchestrator needs a trusted git checkout."
    }

    return $command.Source
}

function Get-CimInstanceSafely {
    <#
    .SYNOPSIS
        CIM query that never fails the run (missing classes/providers are recorded as empty).
    #>
    param(
        [Parameter(Mandatory = $true)][string] $ClassName,
        [string] $Namespace = 'root/cimv2'
    )

    try {
        return @(Get-CimInstance -ClassName $ClassName -Namespace $Namespace -ErrorAction Stop)
    }
    catch {
        return @()
    }
}

function Get-FreeDiskGB {
    <#
    .SYNOPSIS
        Free space of the drive that hosts Path (works for paths that do not exist yet).
    #>
    param([string] $Path)

    try {
        $fullPath = [System.IO.Path]::GetFullPath($Path)
        $root = [System.IO.Path]::GetPathRoot($fullPath)
        $drive = [System.IO.DriveInfo]::new($root)
        return [Math]::Round($drive.AvailableFreeSpace / 1GB, 1)
    }
    catch {
        return -1
    }
}

function Get-PowerState {
    <#
    .SYNOPSIS
        Best-effort AC/battery detection via root\wmi BatteryStatus, falling back to CIM.
    #>
    $batteries = @(Get-CimInstanceSafely -ClassName 'Win32_Battery')
    if ($batteries.Count -eq 0) {
        return [pscustomobject]@{ HasBattery = $false; OnBattery = $false; Status = 'ac-or-desktop' }
    }

    $batteryStatus = @(Get-CimInstanceSafely -ClassName 'BatteryStatus' -Namespace 'root/wmi')
    if ($batteryStatus.Count -gt 0) {
        $online = @($batteryStatus | ForEach-Object { [bool]($_.PowerOnline) })
        if ($online -contains $false -and -not ($online -contains $true)) {
            return [pscustomobject]@{ HasBattery = $true; OnBattery = $true; Status = 'battery-discharging' }
        }

        return [pscustomobject]@{ HasBattery = $true; OnBattery = $false; Status = 'ac-online' }
    }

    $discharging = @($batteries | Where-Object { $null -ne $_.BatteryStatus -and [int]$_.BatteryStatus -eq 1 }).Count -gt 0
    return [pscustomobject]@{ HasBattery = $true; OnBattery = $discharging; Status = $(if ($discharging) { 'battery-discharging' } else { 'ac-unknown' }) }
}

function Get-HardwareSnapshot {
    <#
    .SYNOPSIS
        Collects the hardware facts used by the preflight gates and the environment manifest.
    #>
    $os = @(Get-CimInstanceSafely -ClassName 'Win32_OperatingSystem') | Select-Object -First 1
    $computer = @(Get-CimInstanceSafely -ClassName 'Win32_ComputerSystem') | Select-Object -First 1
    $processors = @(Get-CimInstanceSafely -ClassName 'Win32_Processor')
    $videoControllers = @(Get-CimInstanceSafely -ClassName 'Win32_VideoController')
    $desktopMonitors = @(Get-CimInstanceSafely -ClassName 'Win32_DesktopMonitor')

    $memoryBytes = 0
    if ($null -ne $computer -and $null -ne $computer.TotalPhysicalMemory) {
        $memoryBytes = [double]$computer.TotalPhysicalMemory
    }

    $cpuName = ''
    $physicalCores = 0
    $logicalProcessors = 0
    $maxClockMhz = 0
    foreach ($processor in $processors) {
        if ([string]::IsNullOrWhiteSpace($cpuName)) {
            $cpuName = ([string]$processor.Name).Trim()
        }

        $physicalCores += [int]$processor.NumberOfCores
        $logicalProcessors += [int]$processor.NumberOfLogicalProcessors
        $maxClockMhz = [Math]::Max($maxClockMhz, [int]$processor.MaxClockSpeed)
    }

    $displayAdapters = @()
    $maximumWidth = 0
    $maximumHeight = 0
    foreach ($controller in $videoControllers) {
        $width = [int]$controller.CurrentHorizontalResolution
        $height = [int]$controller.CurrentVerticalResolution
        $maximumWidth = [Math]::Max($maximumWidth, $width)
        $maximumHeight = [Math]::Max($maximumHeight, $height)
        $displayAdapters += [pscustomobject]@{
            name = ([string]$controller.Name).Trim()
            driverVersion = ([string]$controller.DriverVersion).Trim()
            width = $width
            height = $height
            refreshHz = [int]$controller.CurrentRefreshRate
        }
    }

    $appliedDpi = 96
    try {
        $windowMetrics = Get-ItemProperty -LiteralPath 'HKCU:\Control Panel\Desktop\WindowMetrics' -Name 'AppliedDPI' -ErrorAction Stop
        if ($null -ne $windowMetrics.AppliedDPI -and [int]$windowMetrics.AppliedDPI -gt 0) {
            $appliedDpi = [int]$windowMetrics.AppliedDPI
        }
    }
    catch {
        $appliedDpi = 96
    }

    $monitorCount = [Math]::Max($desktopMonitors.Count, 1)
    if ($videoControllers.Count -eq 0) {
        $monitorCount = 0
    }

    return [pscustomobject]@{
        osCaption = ([string]$os.Caption).Trim()
        osVersion = ([string]$os.Version).Trim()
        osBuildNumber = ([string]$os.BuildNumber).Trim()
        osArchitecture = ([string]$os.OSArchitecture).Trim()
        machineName = [Environment]::MachineName
        userName = [Environment]::UserName
        memoryGB = [Math]::Round($memoryBytes / 1GB, 1)
        cpuName = $cpuName
        physicalCores = $physicalCores
        logicalProcessors = $logicalProcessors
        maxClockMhz = $maxClockMhz
        displayAdapters = $displayAdapters
        monitorCount = $monitorCount
        desktopMonitorCount = $desktopMonitors.Count
        width = $maximumWidth
        height = $maximumHeight
        dpi = $appliedDpi
        dpiPercent = [Math]::Round(($appliedDpi / 96.0) * 100, 0)
    }
}

function Invoke-Preflight {
    <#
    .SYNOPSIS
        Hard preflight gates. Returns Status 'OK' or one of the SKIPPED_* statuses.
    #>
    $script:state.Stage = 'PREFLIGHT'
    $sessionId = (Get-Process -Id $PID).SessionId
    $hardware = Get-HardwareSnapshot
    $power = Get-PowerState
    $details = [pscustomobject]@{
        sessionId = $sessionId
        userInteractive = [Environment]::UserInteractive
        userName = [Environment]::UserName
        hardware = $hardware
        power = $power
        checkedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    }

    # --- Interactive, unlocked session gate ------------------------------------------------
    if (-not [Environment]::UserInteractive) {
        return [pscustomobject]@{
            Status = 'SKIPPED_NO_INTERACTIVE_SESSION'
            Message = 'The orchestrator is not running in an interactive session ([Environment]::UserInteractive is false).'
            Details = $details
        }
    }

    if ($sessionId -eq 0) {
        return [pscustomobject]@{
            Status = 'SKIPPED_NO_INTERACTIVE_SESSION'
            Message = 'The orchestrator is running in Session 0 (service/SYSTEM context). Log on to the reference desktop and run from the user session.'
            Details = $details
        }
    }

    $logonUi = @(Get-Process -Name 'logonui' -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -eq $sessionId })
    if ($logonUi.Count -gt 0) {
        return [pscustomobject]@{
            Status = 'SKIPPED_LOCKED'
            Message = "The interactive desktop session $sessionId appears locked (LogonUI.exe is present). Unlock the reference desktop and rerun; UI Automation must never drive a locked session."
            Details = $details
        }
    }

    # --- Hardware minimums -----------------------------------------------------------------
    $minimumMemoryGB = [double](Get-ConfigValue -Path 'preflight.minimumMemoryGB' -Default 16)
    if ($hardware.memoryGB -lt $minimumMemoryGB) {
        return [pscustomobject]@{
            Status = 'SKIPPED_INSUFFICIENT_RESOURCES'
            Message = "Physical memory ($($hardware.memoryGB) GB) is below the configured reference-desktop minimum ($minimumMemoryGB GB)."
            Details = $details
        }
    }

    $minimumProcessors = [int](Get-ConfigValue -Path 'preflight.minimumLogicalProcessors' -Default 4)
    if ($hardware.logicalProcessors -lt $minimumProcessors) {
        return [pscustomobject]@{
            Status = 'SKIPPED_INSUFFICIENT_RESOURCES'
            Message = "Logical processors ($($hardware.logicalProcessors)) are below the configured minimum ($minimumProcessors)."
            Details = $details
        }
    }

    $minimumMonitors = [int](Get-ConfigValue -Path 'preflight.display.minimumMonitors' -Default 1)
    $minimumWidth = [int](Get-ConfigValue -Path 'preflight.display.minimumWidth' -Default 1280)
    $minimumHeight = [int](Get-ConfigValue -Path 'preflight.display.minimumHeight' -Default 720)
    if ($hardware.monitorCount -lt $minimumMonitors -or $hardware.width -lt $minimumWidth -or $hardware.height -lt $minimumHeight) {
        return [pscustomobject]@{
            Status = 'SKIPPED_INSUFFICIENT_RESOURCES'
            Message = "The desktop does not meet the configured display minimums (monitors=$($hardware.monitorCount), resolution=$($hardware.width)x$($hardware.height))."
            Details = $details
        }
    }

    # --- Power gate ------------------------------------------------------------------------
    $allowBatteryPower = [bool](Get-ConfigValue -Path 'preflight.allowBatteryPower' -Default $false)
    if ($power.OnBattery -and -not $allowBatteryPower) {
        return [pscustomobject]@{
            Status = 'SKIPPED_ON_BATTERY'
            Message = 'The reference desktop is running on battery power; connect AC power (or set preflight.allowBatteryPower=true deliberately).'
            Details = $details
        }
    }

    # --- Disk gate -------------------------------------------------------------------------
    $minimumFreeDiskGB = [double](Get-ConfigValue -Path 'preflight.minimumFreeDiskGB' -Default 25)
    $freeDiskGB = Get-FreeDiskGB -Path $script:state.ResultsRoot
    if ($freeDiskGB -lt $minimumFreeDiskGB) {
        return [pscustomobject]@{
            Status = 'SKIPPED_INSUFFICIENT_RESOURCES'
            Message = "Free disk space ($freeDiskGB GB on the results drive) is below the configured minimum ($minimumFreeDiskGB GB)."
            Details = $details
        }
    }

    # --- Single-instance ("no overlap") mutex ----------------------------------------------
    $mutexName = [string](Get-ConfigValue -Path 'preflight.singleInstanceMutexName' -Default $script:DefaultMutexName)
    $createdNew = $false
    $mutex = [System.Threading.Mutex]::new($false, $mutexName, [ref]$createdNew)
    $acquired = $false
    try {
        $acquired = $mutex.WaitOne(0)
    }
    catch [System.Threading.AbandonedMutexException] {
        # A previous run crashed while holding the mutex; the mutex is now owned by this run.
        $acquired = $true
    }

    if (-not $acquired) {
        $mutex.Dispose()
        return [pscustomobject]@{
            Status = 'SKIPPED_OVERLAP'
            Message = "Another reference-desktop run already owns the single-instance mutex '$mutexName'; overlapping runs would corrupt the evidence."
            Details = $details
        }
    }

    $script:state.Mutex = $mutex
    $script:state.MutexHeld = $true
    $details | Add-Member -NotePropertyName freeDiskGB -NotePropertyValue $freeDiskGB -Force
    $details | Add-Member -NotePropertyName mutexName -NotePropertyValue $mutexName -Force

    return [pscustomobject]@{ Status = 'OK'; Message = ''; Details = $details }
}

function New-RunWorktree {
    <#
    .SYNOPSIS
        Creates the detached worktree under <runsRoot>\<run-id> from the approved SHA.
    #>
    param([string] $Repository, [string] $Sha, [string] $RunsRoot, [string] $RunId)

    $worktreePath = Join-Path $RunsRoot $RunId
    $runsParent = Split-Path -Parent $worktreePath
    if (-not [string]::IsNullOrWhiteSpace($runsParent)) {
        New-Item -ItemType Directory -Path $runsParent -Force | Out-Null
    }

    if (Test-Path -LiteralPath $worktreePath) {
        throw "Run worktree '$worktreePath' already exists; refusing to reuse a non-owned directory."
    }

    Write-RunLog 'PREPARE' "Creating detached worktree '$worktreePath' from approved revision $Sha."
    $output = & git -C $Repository worktree add --detach $worktreePath $Sha 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git worktree add failed (exit $LASTEXITCODE): $($output | Out-String)"
    }

    $script:state.WorktreePath = $worktreePath
    $script:state.WorktreeCreated = $true
    Write-RunLog 'PREPARE' "Worktree ready at '$worktreePath'."
}

function Invoke-LoggedCommand {
    <#
    .SYNOPSIS
        Runs a child process with bounded timeout and persists redacted, bounded output.
    #>
    param(
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)][string] $FilePath,
        [string[]] $Arguments = @(),
        [Parameter(Mandatory = $true)][string] $WorkingDirectory,
        [Parameter(Mandatory = $true)][string] $LogPath,
        [int] $TimeoutMinutes = 60
    )

    $startedAt = Get-Date
    Write-RunLog 'EXECUTE' "Starting '$Name': $FilePath $($Arguments -join ' ')"
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add([string]$argument)
    }

    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    $childEnvironment = @{
        'DOTNET_CLI_TELEMETRY_OPTOUT' = '1'
        'DOTNET_NOLOGO' = '1'
        'DOTNET_SKIP_FIRST_TIME_EXPERIENCE' = '1'
        'DOTNET_CLI_UI_LANGUAGE' = 'en'
        'DOTNET_GENERATE_ASPNET_CERTIFICATE' = 'false'
    }
    foreach ($key in $childEnvironment.Keys) {
        $startInfo.Environment[$key] = $childEnvironment[$key]
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $timedOut = $false
    $exitCode = -1
    $stdout = ''
    $stderr = ''
    try {
        if (-not $process.Start()) {
            throw "Process '$FilePath' could not be started."
        }

        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $timeoutMilliseconds = [int]([Math]::Min(2147483647.0, [double]$TimeoutMinutes * 60000.0))
        if (-not $process.WaitForExit($timeoutMilliseconds)) {
            $timedOut = $true
            Write-RunLog 'EXECUTE' "Command '$Name' exceeded its $TimeoutMinutes minute budget; terminating it." 'ERROR'
            try {
                $process.Kill($true)
            }
            catch {
                Write-RunLog 'EXECUTE' "Could not kill timed-out command '$Name': $($_.Exception.Message)" 'WARN'
            }

            $null = $process.WaitForExit(30000)
        }

        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        if (-not $timedOut) {
            $exitCode = $process.ExitCode
        }
    }
    finally {
        $process.Dispose()
    }

    $durationSeconds = [Math]::Round(((Get-Date) - $startedAt).TotalSeconds, 1)
    $text = $stdout + [Environment]::NewLine + $stderr
    $header = "# command: $FilePath $($Arguments -join ' ')"
    Write-BoundedLog -Path $LogPath -Text $text -Header $header

    if ($timedOut) {
        Write-RunLog 'EXECUTE' "Command '$Name' timed out after $TimeoutMinutes minutes." 'ERROR'
    }
    elseif ($exitCode -ne 0) {
        Write-RunLog 'EXECUTE' "Command '$Name' failed with exit code $exitCode after $durationSeconds s." 'ERROR'
    }
    else {
        Write-RunLog 'EXECUTE' "Command '$Name' completed with exit code 0 in $durationSeconds s."
    }

    return [pscustomobject]@{
        Name = $Name
        ExitCode = $exitCode
        TimedOut = $timedOut
        DurationSeconds = $durationSeconds
        Text = $text
        LogPath = $LogPath
    }
}

function Invoke-BuildStage {
    <#
    .SYNOPSIS
        Builds Release inside the detached worktree (solution + Windows-only test projects).
    #>
    param([string] $WorktreePath, [string] $ResultsDirectory)

    $configuration = [string](Get-ConfigValue -Path 'build.configuration' -Default 'Release')
    $buildTimeoutMinutes = [int](Get-ConfigValue -Path 'build.buildTimeoutMinutes' -Default 60)
    $dotnet = Get-DotnetExecutable
    $logsDirectory = Join-Path $ResultsDirectory 'logs'

    $builds = @(
        @{ name = 'solution'; arguments = @('build', 'AetherWorkspace.slnx', '-c', $configuration) }
        @{ name = 'windows-ui-tests'; arguments = @('build', 'tests/Aether.Shell.UiTests/Aether.Shell.UiTests.csproj', '-c', $configuration, '-p:EnableWindowsTargeting=true') }
        @{ name = 'desktop-e2e-tests'; arguments = @('build', 'tests/Aether.Desktop.E2E.Tests/Aether.Desktop.E2E.Tests.csproj', '-c', $configuration, '-p:EnableWindowsTargeting=true') }
    )

    foreach ($build in $builds) {
        $buildName = [string]$build['name']
        $logPath = Join-Path $logsDirectory "build-$buildName.log"
        $result = Invoke-LoggedCommand -Name "build-$buildName" -FilePath $dotnet -Arguments $build['arguments'] -WorkingDirectory $WorktreePath -LogPath $logPath -TimeoutMinutes $buildTimeoutMinutes
        $buildStatus = if ($result.ExitCode -eq 0 -and -not $result.TimedOut) { 'PASS' } else { 'FAIL' }
        $script:state.Builds.Add([pscustomobject]@{
            name = $buildName
            status = $buildStatus
            durationSeconds = $result.DurationSeconds
            log = "logs/build-$buildName.log"
        })

        if ($buildStatus -ne 'PASS') {
            $classification = 'FAIL_HARNESS'
            if (-not $result.TimedOut -and $result.Text -match '(?m)\berror (CS|MSB|NU|NETSDK)\d+') {
                $classification = 'FAIL_PRODUCT'
            }

            $message = "Release build '$buildName' failed (exit $($result.ExitCode), timedOut=$($result.TimedOut)); see logs/build-$buildName.log."
            Add-Failure -Suite "build-$buildName" -Status $classification -Message $message
            return $false
        }
    }

    return $true
}

function Get-ProfileSuites {
    <#
    .SYNOPSIS
        Returns the suite names for the selected profile from config, or the built-in default.
    #>
    $configured = Get-ConfigValue -Path "profiles.$($script:state.Profile).suites" -Default $null
    if ($null -ne $configured) {
        return @($configured | ForEach-Object { [string]$_ })
    }

    if ($script:SuiteDefaults.ContainsKey($script:state.Profile)) {
        return @($script:SuiteDefaults[$script:state.Profile])
    }

    return @('desktop-e2e')
}

function Get-ProfileSuiteTimeoutMinutes {
    <#
    .SYNOPSIS
        Returns the per-suite timeout for the selected profile.
    #>
    $configured = Get-ConfigValue -Path "profiles.$($script:state.Profile).suiteTimeoutMinutes" -Default $null
    if ($null -ne $configured) {
        return [int]$configured
    }

    return 90
}

function Get-XmlAttributeInt {
    <#
    .SYNOPSIS
        Reads an integer XML attribute with a safe default (TRX counters are optional).
    #>
    param($Node, [string] $Name, [int] $Default = 0)

    if ($null -eq $Node) {
        return $Default
    }

    $value = $Node.GetAttribute($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $Default
    }

    $parsed = 0
    if ([int]::TryParse($value, [ref]$parsed)) {
        return $parsed
    }

    return $Default
}

function Read-TrxCounters {
    <#
    .SYNOPSIS
        Parses the TRX Counters element; missing or malformed evidence yields zero counts.
    #>
    param([string] $Path)

    $empty = [pscustomobject]@{
        available = $false
        total = 0
        executed = 0
        passed = 0
        failed = 0
        skipped = 0
        aborted = 0
    }

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $empty
    }

    try {
        [xml]$document = Get-Content -LiteralPath $Path -Raw -ErrorAction Stop
        $node = $document.SelectSingleNode("//*[local-name()='Counters']")
        if ($null -eq $node) {
            return $empty
        }

        $total = Get-XmlAttributeInt -Node $node -Name 'total'
        $executed = Get-XmlAttributeInt -Node $node -Name 'executed'
        return [pscustomobject]@{
            available = $true
            total = $total
            executed = $executed
            passed = Get-XmlAttributeInt -Node $node -Name 'passed'
            failed = Get-XmlAttributeInt -Node $node -Name 'failed'
            skipped = [Math]::Max(0, $total - $executed)
            aborted = Get-XmlAttributeInt -Node $node -Name 'aborted'
        }
    }
    catch {
        return $empty
    }
}

function Get-SolutionTestProjects {
    <#
    .SYNOPSIS
        Returns the test project paths declared in the solution (.slnx), excluding helper
        libraries such as tests/Aether.Testing.
    #>
    param([string] $SolutionPath)

    if (-not (Test-Path -LiteralPath $SolutionPath -PathType Leaf)) {
        throw "Solution '$SolutionPath' was not found in the run worktree."
    }

    try {
        [xml]$document = Get-Content -LiteralPath $SolutionPath -Raw -ErrorAction Stop
    }
    catch {
        throw "Solution '$SolutionPath' is not valid XML: $($_.Exception.Message)"
    }

    $projects = [System.Collections.Generic.List[string]]::new()
    foreach ($node in @($document.SelectNodes("//*[local-name()='Project']"))) {
        $path = ([string]$node.GetAttribute('Path')).Replace('\\', '/')
        if ($path -match '^tests/.+\.Tests\.csproj$') {
            $projects.Add($path)
        }
    }

    return @($projects | Sort-Object)
}

function Invoke-SolutionTestSuite {
    <#
    .SYNOPSIS
        Runs each test project of the solution separately with a unique TRX file.

    .DESCRIPTION
        Running one `dotnet test` per test project makes the evidence deterministic: every
        project writes exactly one TRX into results/trx/<suite>/ and the harness aggregates
        those Counters. This avoids duplicate/stale TRX discovery and filename overwrites.
    #>
    param(
        [string] $SuiteName,
        [hashtable] $Definition,
        [string] $SolutionProjectPath,
        [string] $Configuration,
        [string] $WorktreePath,
        [string] $TrxDirectory,
        [string] $LogsDirectory,
        [string] $Dotnet,
        [int] $TimeoutMinutes
    )

    $solutionPath = Join-Path $WorktreePath $SolutionProjectPath
    try {
        $projects = @(Get-SolutionTestProjects -SolutionPath $solutionPath)
    }
    catch {
        Add-Failure -Suite $SuiteName -Status 'FAIL_HARNESS' -Message "Cannot enumerate solution test projects: $($_.Exception.Message)"
        return
    }

    if ($projects.Count -eq 0) {
        Add-Failure -Suite $SuiteName -Status 'FAIL_HARNESS' -Message "Solution '$SolutionProjectPath' declares no *\\.Tests.csproj projects."
        return
    }

    $suiteTrxDirectory = Join-Path $TrxDirectory $SuiteName
    New-Item -ItemType Directory -Path $suiteTrxDirectory -Force | Out-Null
    $copied = [System.Collections.Generic.List[string]]::new()
    $failedProjects = [System.Collections.Generic.List[string]]::new()
    $timedOutProjects = [System.Collections.Generic.List[string]]::new()
    $totalDuration = 0.0

    foreach ($project in $projects) {
        $projectName = [System.IO.Path]::GetFileNameWithoutExtension($project)
        $trxName = "$projectName.trx"
        $arguments = @(
            'test',
            $project,
            '-c',
            $Configuration,
            '--no-build',
            '--logger',
            "trx;LogFileName=$trxName",
            '--results-directory',
            $suiteTrxDirectory
        )
        $logPath = Join-Path $LogsDirectory "test-$SuiteName-$projectName.log"
        $result = Invoke-LoggedCommand -Name "test-$SuiteName-$projectName" -FilePath $Dotnet -Arguments $arguments -WorkingDirectory $WorktreePath -LogPath $logPath -TimeoutMinutes $TimeoutMinutes
        $totalDuration += [double]$result.DurationSeconds
        if ($result.TimedOut) {
            $timedOutProjects.Add($projectName)
        }
        elseif ($result.ExitCode -ne 0) {
            $failedProjects.Add($projectName)
        }

        $trxPath = Join-Path $suiteTrxDirectory $trxName
        if (Test-Path -LiteralPath $trxPath -PathType Leaf) {
            $copied.Add($trxPath)
        }
    }

    $counters = Read-TrxCountersAggregate -Paths @($copied)
    $status = 'PASS'
    if ($timedOutProjects.Count -gt 0) {
        $status = 'FAIL_HARNESS'
        Add-Failure -Suite $SuiteName -Status $status -Message ("Test project(s) timed out: " + ($timedOutProjects -join ', '))
    }
    elseif ($failedProjects.Count -gt 0) {
        $status = 'FAIL_PRODUCT'
        Add-Failure -Suite $SuiteName -Status $status -Message ("Test project(s) failed: " + ($failedProjects -join ', '))
    }
    elseif ($copied.Count -ne $projects.Count) {
        $status = 'INCONCLUSIVE'
        Add-Failure -Suite $SuiteName -Status $status -Message "Expected $($projects.Count) TRX files but collected $($copied.Count); evidence is incomplete."
    }
    elseif (-not $counters.available -or $counters.total -eq 0) {
        $status = 'INCONCLUSIVE'
        Add-Failure -Suite $SuiteName -Status $status -Message 'No executed tests were reported across the per-project TRX files.'
    }

    $script:state.Suites.Add([pscustomobject]@{
        name = $SuiteName
        description = [string]$Definition['description']
        project = $SolutionProjectPath
        filter = ''
        status = $status
        durationSeconds = [Math]::Round($totalDuration, 1)
        trx = "trx/$SuiteName"
        log = "logs/test-$SuiteName-*.log"
        counters = $counters
    })
}

function Invoke-TestSuites {
    <#
    .SYNOPSIS
        Runs the profile's dotnet test suites from the detached worktree.
    #>
    param([string] $WorktreePath, [string] $ResultsDirectory)

    $configuration = [string](Get-ConfigValue -Path 'build.configuration' -Default 'Release')
    $dotnet = Get-DotnetExecutable
    $timeoutMinutes = Get-ProfileSuiteTimeoutMinutes
    $logsDirectory = Join-Path $ResultsDirectory 'logs'
    $trxDirectory = Join-Path $ResultsDirectory 'trx'
    $suites = Get-ProfileSuites

    foreach ($suiteName in $suites) {
        if (-not $script:SuiteDefinitions.ContainsKey($suiteName)) {
            Add-Failure -Suite $suiteName -Status 'FAIL_HARNESS' -Message "Profile '$($script:state.Profile)' selects unknown suite '$suiteName'."
            continue
        }

        $definition = $script:SuiteDefinitions[$suiteName]
        $projectPath = [string]$definition['project']

        if ($projectPath -match '\.slnx?$') {
            Invoke-SolutionTestSuite -SuiteName $suiteName -Definition $definition -SolutionProjectPath $projectPath `
                -Configuration $configuration -WorktreePath $WorktreePath -TrxDirectory $trxDirectory `
                -LogsDirectory $logsDirectory -Dotnet $dotnet -TimeoutMinutes $timeoutMinutes
            continue
        }

        $trxName = "$suiteName.trx"
        $arguments = @(
            'test',
            $projectPath,
            '-c',
            $configuration,
            '--no-build',
            '--logger',
            "trx;LogFileName=$trxName",
            '--results-directory',
            $trxDirectory
        )
        $filter = [string]$definition['filter']
        if (-not [string]::IsNullOrWhiteSpace($filter)) {
            $arguments += @('--filter', $filter)
        }

        $logPath = Join-Path $logsDirectory "test-$suiteName.log"
        $suiteStartedUtc = (Get-Date).ToUniversalTime()
        $result = Invoke-LoggedCommand -Name "test-$suiteName" -FilePath $dotnet -Arguments $arguments -WorkingDirectory $WorktreePath -LogPath $logPath -TimeoutMinutes $timeoutMinutes
        $suiteTrx = "trx/$trxName"
        $counters = Read-TrxCounters -Path (Join-Path $trxDirectory $trxName)

        $suiteStatus = 'PASS'
        if ($result.TimedOut) {
            $suiteStatus = 'FAIL_HARNESS'
            Add-Failure -Suite $suiteName -Status $suiteStatus -Message "Suite '$suiteName' exceeded its $timeoutMinutes minute budget; see logs/test-$suiteName.log."
        }
        elseif ($result.ExitCode -ne 0) {
            if ((Test-Path -LiteralPath (Join-Path $trxDirectory $trxName)) -or $result.Text -match '(?m)Failed!') {
                $suiteStatus = 'FAIL_PRODUCT'
            }
            else {
                $suiteStatus = 'FAIL_HARNESS'
            }

            Add-Failure -Suite $suiteName -Status $suiteStatus -Message "Suite '$suiteName' failed (exit $($result.ExitCode)); see logs/test-$suiteName.log."
        }
        elseif (-not $counters.available -or $counters.total -eq 0) {
            $suiteStatus = 'INCONCLUSIVE'
            Add-Failure -Suite $suiteName -Status $suiteStatus -Message "Suite '$suiteName' did not report any executed tests (no TRX counters); the run cannot be interpreted."
        }

        $script:state.Suites.Add([pscustomobject]@{
            name = $suiteName
            description = [string]$definition['description']
            project = $projectPath
            filter = $filter
            status = $suiteStatus
            durationSeconds = $result.DurationSeconds
            trx = $suiteTrx
            log = "logs/test-$suiteName.log"
            counters = $counters
        })
    }
}

function Get-OwnedProcesses {
    <#
    .SYNOPSIS
        Processes whose executable or command line references the run worktree (owned PIDs).
    #>
    param([string] $WorktreePath)

    if ([string]::IsNullOrWhiteSpace($WorktreePath)) {
        return @()
    }

    $candidates = @(Get-CimInstanceSafely -ClassName 'Win32_Process')
    $owned = @()
    foreach ($candidate in $candidates) {
        $executablePath = [string]$candidate.ExecutablePath
        $commandLine = [string]$candidate.CommandLine
        $isOwned = $false
        if (-not [string]::IsNullOrWhiteSpace($executablePath) -and $executablePath.StartsWith($WorktreePath, [System.StringComparison]::OrdinalIgnoreCase)) {
            $isOwned = $true
        }
        elseif (-not [string]::IsNullOrWhiteSpace($commandLine) -and $commandLine.IndexOf($WorktreePath, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            $isOwned = $true
        }

        if ($isOwned) {
            $owned += [pscustomobject]@{ processId = [int]$candidate.ProcessId; name = [string]$candidate.Name; commandLine = $commandLine }
        }
    }

    return $owned
}

function Get-OwnedInboundListeners {
    <#
    .SYNOPSIS
        TCP listeners owned by the run's processes; expected to be empty (local IPC only).
    #>
    param([int[]] $ProcessIds)

    if ($null -eq $ProcessIds -or $ProcessIds.Count -eq 0) {
        return @()
    }

    try {
        $listeners = @(Get-NetTCPConnection -State Listen -ErrorAction Stop | Where-Object { $ProcessIds -contains [int]$_.OwningProcess })
        return @($listeners | ForEach-Object { '{0}:{1} (pid {2})' -f $_.LocalAddress, $_.LocalPort, $_.OwningProcess })
    }
    catch {
        return @()
    }
}

function Invoke-CaptureStage {
    <#
    .SYNOPSIS
        Captures ownership evidence (PIDs, inbound listeners) before cleanup.
    #>
    $owned = @(Get-OwnedProcesses -WorktreePath $script:state.WorktreePath)
    $script:state.OwnedProcessIds = @($owned | ForEach-Object { [int]$_.processId })
    $script:state.InboundListeners = @(Get-OwnedInboundListeners -ProcessIds $script:state.OwnedProcessIds)
    if ($script:state.InboundListeners.Count -gt 0) {
        Add-Failure -Suite 'network' -Status 'FAIL_HARNESS' -Message ("Run-owned processes opened inbound TCP listeners: " + ($script:state.InboundListeners -join ', '))
    }
    else {
        Write-RunLog 'ASSERT_CAPTURE' 'No inbound TCP listeners were observed for run-owned processes (local IPC only).'
    }
}

function Add-Failure {
    <#
    .SYNOPSIS
        Records a classified failure for the final run result.
    #>
    param(
        [Parameter(Mandatory = $true)][string] $Suite,
        [Parameter(Mandatory = $true)][string] $Status,
        [Parameter(Mandatory = $true)][string] $Message
    )

    $script:state.Failures.Add([pscustomobject]@{ suite = $Suite; status = $Status; message = $Message })
    Write-RunLog 'EXECUTE' "$Status ($Suite): $Message" 'ERROR'
}

function Set-SkipStatus {
    <#
    .SYNOPSIS
        Ends the run early with an explicit SKIPPED_* status and exit code 0.
    #>
    param([string] $Status, [string] $Message)

    $script:state.Status = $Status
    $script:state.Message = $Message
    Write-RunLog 'RESULT' "$Status - $Message" 'SKIP'
}

function Get-OverallStatus {
    <#
    .SYNOPSIS
        Derives the run status from classified failures (harness beats product beats inconclusive).
    #>
    $statuses = @($script:state.Failures | ForEach-Object { $_.status })
    if ($statuses -contains 'FAIL_HARNESS') {
        return 'FAIL_HARNESS'
    }

    if ($statuses -contains 'FAIL_PRODUCT') {
        return 'FAIL_PRODUCT'
    }

    if ($statuses -contains 'INCONCLUSIVE') {
        return 'INCONCLUSIVE'
    }

    return 'PASS'
}

function Invoke-AetherTestRun {
    <#
    .SYNOPSIS
        Stage driver for lifecycle 01 TRIGGER .. 08 RESULT.
    #>
    $requested = $script:options
    Write-RunLog 'TRIGGER' ("Reference-desktop run requested: profile '{0}', repo '{1}', results '{2}', config '{3}'." -f $requested['Profile'], $requested['RepoRoot'], $requested['ResultsRoot'], $requested['ConfigPath'])
    Initialize-RunContext -ProfileName $requested['Profile'] -RequestedRepoRoot $requested['RepoRoot'] -RequestedResultsRoot $requested['ResultsRoot'] -RequestedApprovedRevisionFile $requested['ApprovedRevisionFile'] -RequestedConfigPath $requested['ConfigPath']

    # 02 TRUST ------------------------------------------------------------------------------
    $script:state.Stage = 'TRUST'
    $null = Get-GitExecutable
    $revision = Read-ApprovedRevision -Path $script:state.ApprovedRevisionFile
    if (-not $revision.Ok) {
        Set-SkipStatus -Status 'SKIPPED_UNAPPROVED' -Message $revision.Message
        return
    }

    $script:state.CommitSha = $revision.Sha
    if (-not (Test-CommitExists -Repository $script:state.RepoRoot -Sha $revision.Sha)) {
        Set-SkipStatus -Status 'SKIPPED_UNAPPROVED' -Message "Approved revision $($revision.Sha) is not present in '$($script:state.RepoRoot)'. Fetch the approved commit into the trusted checkout first."
        return
    }

    Write-RunLog 'TRUST' "Approved full 40-hex revision $($revision.Sha) accepted."

    # 03 PREFLIGHT --------------------------------------------------------------------------
    $preflight = Invoke-Preflight
    $script:state.Preflight = $preflight.Details
    if ($preflight.Status -ne 'OK') {
        Set-SkipStatus -Status $preflight.Status -Message $preflight.Message
        return
    }

    Write-RunLog 'PREFLIGHT' "Preflight passed: session $($preflight.Details.sessionId) unlocked, hardware and power gates satisfied, single-instance mutex held."

    # 04 PREPARE ----------------------------------------------------------------------------
    $script:state.Stage = 'PREPARE'
    New-RunWorktree -Repository $script:state.RepoRoot -Sha $script:state.CommitSha -RunsRoot $script:state.RunsRoot -RunId $script:state.RunId
    if (-not (Invoke-BuildStage -WorktreePath $script:state.WorktreePath -ResultsDirectory $script:state.ResultsDirectory)) {
        $script:state.Status = Get-OverallStatus
        return
    }

    # 05 EXECUTE ----------------------------------------------------------------------------
    $script:state.Stage = 'EXECUTE'
    Invoke-TestSuites -WorktreePath $script:state.WorktreePath -ResultsDirectory $script:state.ResultsDirectory

    # 06 ASSERT / CAPTURE -------------------------------------------------------------------
    $script:state.Stage = 'ASSERT_CAPTURE'
    Invoke-CaptureStage
    $script:state.Status = Get-OverallStatus

    # 08 RESULT (evidence writing happens in Complete-Run after cleanup) ---------------------
    $script:state.Stage = 'RESULT'
    Write-RunLog 'RESULT' "Run status before cleanup: $($script:state.Status)."
}

function Complete-Cleanup {
    <#
    .SYNOPSIS
        Stage 07 CLEANUP: kills only run-owned PIDs, removes the detached worktree, releases the mutex.
    #>
    $script:state.Stage = 'CLEANUP'
    try {
        $owned = @(Get-OwnedProcesses -WorktreePath $script:state.WorktreePath)
        if ($owned.Count -gt 0) {
            Write-RunLog 'CLEANUP' "Stopping $($owned.Count) process(es) owned by the run worktree."
            foreach ($ownedProcess in $owned) {
                try {
                    Stop-Process -Id $ownedProcess.processId -Force -ErrorAction Stop
                }
                catch {
                    $message = "Could not stop run-owned PID $($ownedProcess.processId) ($($ownedProcess.name)): $($_.Exception.Message)"
                    $script:state.CleanupWarnings.Add($message)
                    Write-RunLog 'CLEANUP' $message 'WARN'
                }
            }
        }
    }
    catch {
        $message = "Owned-process cleanup failed: $($_.Exception.Message)"
        $script:state.CleanupWarnings.Add($message)
        Write-RunLog 'CLEANUP' $message 'WARN'
    }

    if (-not [string]::IsNullOrWhiteSpace($script:state.WorktreePath) -and $script:state.WorktreeCreated) {
        $safeToRemove = $false
        try {
            $resolvedWorktree = [System.IO.Path]::GetFullPath($script:state.WorktreePath)
            $resolvedRunsRoot = [System.IO.Path]::GetFullPath($script:state.RunsRoot)
            $safeToRemove = $resolvedWorktree.StartsWith($resolvedRunsRoot, [System.StringComparison]::OrdinalIgnoreCase) -and
                (Split-Path -Leaf $resolvedWorktree) -eq $script:state.RunId
        }
        catch {
            $safeToRemove = $false
        }

        if (-not $safeToRemove) {
            $message = "Refusing to remove worktree '$($script:state.WorktreePath)': it is not <runsRoot>\<runId> owned by this run."
            $script:state.CleanupWarnings.Add($message)
            Write-RunLog 'CLEANUP' $message 'WARN'
        }
        else {
            try {
                Write-RunLog 'CLEANUP' "Removing detached worktree '$($script:state.WorktreePath)'."
                $output = & git -C $script:state.RepoRoot worktree remove --force $script:state.WorktreePath 2>&1
                if ($LASTEXITCODE -ne 0) {
                    $message = "git worktree remove failed (exit $LASTEXITCODE): $($output | Out-String)"
                    $script:state.CleanupWarnings.Add($message)
                    Write-RunLog 'CLEANUP' $message 'WARN'
                    $null = & git -C $script:state.RepoRoot worktree prune 2>&1
                }
                else {
                    $script:state.WorktreeRemoved = $true
                }
            }
            catch {
                $message = "Worktree removal threw: $($_.Exception.Message)"
                $script:state.CleanupWarnings.Add($message)
                Write-RunLog 'CLEANUP' $message 'WARN'
            }
        }
    }

    if ($script:state.MutexHeld -and $null -ne $script:state.Mutex) {
        try {
            $script:state.Mutex.ReleaseMutex()
        }
        catch {
            Write-RunLog 'CLEANUP' "Mutex release failed: $($_.Exception.Message)" 'WARN'
        }

        $script:state.MutexHeld = $false
    }

    if ($null -ne $script:state.Mutex) {
        try {
            $script:state.Mutex.Dispose()
        }
        catch {
            Write-RunLog 'CLEANUP' "Mutex dispose failed: $($_.Exception.Message)" 'WARN'
        }

        $script:state.Mutex = $null
    }

    Write-RunLog 'CLEANUP' 'Cleanup finished; no global display, firewall or machine environment state was modified.'
}

function Build-EnvironmentManifest {
    <#
    .SYNOPSIS
        Builds environment-manifest.json content (OS/CPU/RAM/GPU/display/DPI/.NET/SHA).
    #>
    $hardware = Get-HardwareSnapshot
    $power = Get-PowerState
    $osBuild = ''
    if (-not [string]::IsNullOrWhiteSpace($hardware.osBuildNumber)) {
        $osBuild = $hardware.osBuildNumber
    }

    $dotnetVersion = ''
    $dotnetSdks = @()
    try {
        $dotnet = Get-DotnetExecutable
        $versionOutput = @(& $dotnet --version 2>$null)
        if ($versionOutput.Count -gt 0) {
            $dotnetVersion = ([string]$versionOutput[0]).Trim()
        }

        $sdkOutput = @(& $dotnet --list-sdks 2>$null)
        $dotnetSdks = @($sdkOutput | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ -ne '' })
    }
    catch {
        $dotnetVersion = "unavailable: $($_.Exception.Message)"
    }

    $gitVersion = ''
    try {
        $gitOutput = @(& git --version 2>$null)
        if ($gitOutput.Count -gt 0) {
            $gitVersion = ([string]$gitOutput[0]).Trim()
        }
    }
    catch {
        $gitVersion = 'unavailable'
    }

    $freeDiskGB = Get-FreeDiskGB -Path $script:state.ResultsRoot
    $sessionId = $null
    if ($null -ne $script:state.Preflight) {
        $sessionId = $script:state.Preflight.sessionId
    }

    return [pscustomobject]@{
        schemaVersion = '1.0'
        addendum = 'v3.0 Addendum A01 (Windows 11 reference desktop)'
        runId = $script:state.RunId
        capturedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        commitSha = $script:state.CommitSha
        releaseTarget = $script:state.ReleaseTarget
        profile = $script:state.Profile
        git = [pscustomobject]@{
            version = $gitVersion
            repositoryRoot = $script:state.RepoRoot
            worktreePath = $script:state.WorktreePath
            detached = $true
        }
        os = [pscustomobject]@{
            caption = $hardware.osCaption
            version = $hardware.osVersion
            buildNumber = $osBuild
            architecture = $hardware.osArchitecture
            machineName = $hardware.machineName
        }
        cpu = [pscustomobject]@{
            name = $hardware.cpuName
            physicalCores = $hardware.physicalCores
            logicalProcessors = $hardware.logicalProcessors
            maxClockMhz = $hardware.maxClockMhz
        }
        memory = [pscustomobject]@{
            totalGB = $hardware.memoryGB
            freeDiskGB = $freeDiskGB
        }
        gpu = @($hardware.displayAdapters | ForEach-Object { [pscustomobject]@{ name = $_.name; driverVersion = $_.driverVersion } })
        displays = [pscustomobject]@{
            monitorCount = $hardware.monitorCount
            desktopMonitorCount = $hardware.desktopMonitorCount
            maximumWidth = $hardware.width
            maximumHeight = $hardware.height
            dpi = $hardware.dpi
            dpiPercent = $hardware.dpiPercent
            adapters = @($hardware.displayAdapters)
        }
        power = [pscustomobject]@{ hasBattery = $power.HasBattery; onBattery = $power.OnBattery; status = $power.Status }
        session = [pscustomobject]@{
            sessionId = $sessionId
            interactive = [Environment]::UserInteractive
            user = [Environment]::UserName
        }
        dotnet = [pscustomobject]@{
            version = $dotnetVersion
            sdk = '10.0.x (pinned by global.json)'
            availableSdks = $dotnetSdks
        }
        powershell = [pscustomobject]@{
            version = $PSVersionTable.PSVersion.ToString()
            edition = [string]$PSVersionTable.PSEdition
        }
        build = [pscustomobject]@{
            configuration = [string](Get-ConfigValue -Path 'build.configuration' -Default 'Release')
            projects = @($script:state.Builds)
        }
        network = [pscustomobject]@{
            policy = 'local IPC only; no inbound listener; package restore over TLS when not cached'
            ownedInboundListeners = @($script:state.InboundListeners)
        }
    }
}

function Write-RunResult {
    <#
    .SYNOPSIS
        Stage 08 RESULT: writes environment-manifest.json, run.log, run-result.json.
    #>
    $status = $script:state.Status
    if ([string]::IsNullOrWhiteSpace($status)) {
        $status = Get-OverallStatus
        $script:state.Status = $status
    }

    $resultsDirectory = $script:state.ResultsDirectory
    if ([string]::IsNullOrWhiteSpace($resultsDirectory)) {
        $resultsDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('aether-windows-test-' + [guid]::NewGuid().ToString('n').Substring(0, 8))
        $script:state.ResultsDirectory = $resultsDirectory
    }

    New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $resultsDirectory 'logs') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $resultsDirectory 'trx') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $resultsDirectory 'manifests') -Force | Out-Null

    $environmentPath = Join-Path $resultsDirectory 'environment-manifest.json'
    $environmentManifestError = ''
    try {
        $manifest = Build-EnvironmentManifest
        $manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $environmentPath -Encoding utf8
    }
    catch {
        $environmentManifestError = $_.Exception.Message
        Write-RunLog 'RESULT' "Environment manifest capture failed: $environmentManifestError" 'WARN'
    }

    $runLogPath = Join-Path $resultsDirectory 'run.log'
    $runLogHeader = "# AetherWorkspace Windows reference-desktop run $($script:state.RunId) (profile $($script:state.Profile), status $status)"
    Write-BoundedLog -Path $runLogPath -Text ($script:state.LogLines -join [Environment]::NewLine) -Header $runLogHeader

    $suiteResults = @($script:state.Suites | ForEach-Object {
        [pscustomobject]@{
            name = $_.name
            description = $_.description
            project = $_.project
            filter = $_.filter
            status = $_.status
            durationSeconds = $_.durationSeconds
            trx = $_.trx
            log = $_.log
            counters = $_.counters
        }
    })

    $totalPassed = 0
    $totalFailed = 0
    $totalSkipped = 0
    $totalAborted = 0
    foreach ($suite in $suiteResults) {
        $totalPassed += [int]$suite.counters.passed
        $totalFailed += [int]$suite.counters.failed
        $totalSkipped += [int]$suite.counters.skipped
        $totalAborted += [int]$suite.counters.aborted
    }

    $artifactEntries = @()
    foreach ($file in @(Get-ChildItem -LiteralPath $resultsDirectory -Recurse -File -ErrorAction SilentlyContinue)) {
        $relativePath = [System.IO.Path]::GetRelativePath($resultsDirectory, $file.FullName)
        $artifactEntries += [pscustomobject]@{
            name = $file.Name
            path = ($relativePath -replace '\\', '/')
            bytes = $file.Length
            redacted = $true
        }
    }

    $exitCode = 0
    switch -Exact ($status) {
        'FAIL_PRODUCT' { $exitCode = 1 }
        'FAIL_HARNESS' { $exitCode = 2 }
        default { $exitCode = 0 }
    }

    $runResult = [ordered]@{
        schemaVersion = '1.0'
        addendum = 'v3.0 Addendum A01 (Windows 11 reference desktop)'
        runId = $script:state.RunId
        commitSha = $script:state.CommitSha
        releaseTarget = $script:state.ReleaseTarget
        profile = $script:state.Profile
        status = $status
        exitCode = $exitCode
        message = $script:state.Message
        startedAtUtc = $script:state.StartedAtUtc.ToString('o')
        finishedAtUtc = $script:state.FinishedAtUtc.ToString('o')
        stage = $script:state.Stage
        approvedRevisionFile = $script:state.ApprovedRevisionFile
        configPath = $script:state.ConfigPath
        environment = [pscustomobject]@{
            manifest = 'environment-manifest.json'
            manifestCaptured = [string]::IsNullOrWhiteSpace($environmentManifestError)
            error = $environmentManifestError
        }
        suites = $suiteResults
        builds = @($script:state.Builds)
        failures = @($script:state.Failures)
        summary = [pscustomobject]@{
            suitesTotal = $suiteResults.Count
            suitesPassed = @($suiteResults | Where-Object { $_.status -eq 'PASS' }).Count
            suitesFailed = @($suiteResults | Where-Object { $_.status -eq 'FAIL_PRODUCT' -or $_.status -eq 'FAIL_HARNESS' }).Count
            suitesInconclusive = @($suiteResults | Where-Object { $_.status -eq 'INCONCLUSIVE' }).Count
            testsPassed = $totalPassed
            testsFailed = $totalFailed
            testsSkipped = $totalSkipped
            testsAborted = $totalAborted
        }
        artifacts = $artifactEntries
        cleanup = [pscustomobject]@{
            ownedProcessIds = @($script:state.OwnedProcessIds)
            worktreeRemoved = [bool]$script:state.WorktreeRemoved
            warnings = @($script:state.CleanupWarnings)
            globalStateModified = $false
        }
        privacy = [pscustomobject]@{
            syntheticDataOnly = $true
            rawPayloadsPersisted = $false
            credentialsRedacted = $true
            windowImagesCaptured = $false
            externalProviderCalls = 0
            ownedInboundListeners = @($script:state.InboundListeners)
            networkPolicy = 'local IPC only; no inbound listener; package restore over TLS when not cached'
        }
        failureTaxonomy = @(
            'PASS: every selected suite passed on the reference desktop'
            'FAIL_PRODUCT: a product assertion/behaviour failed (test or build compiler error)'
            'FAIL_HARNESS: the run/harness/build environment failed (timeout, tool error, no tests)'
            'SKIPPED_LOCKED: interactive desktop session was locked'
            'SKIPPED_UNAPPROVED: no reviewed full 40-hex SHA / commit not present'
            'SKIPPED_OVERLAP: another run held the single-instance mutex'
            'SKIPPED_INSUFFICIENT_RESOURCES: memory/CPU/display/disk below the configured minimum'
            'SKIPPED_ON_BATTERY: reference desktop on battery power'
            'SKIPPED_NO_INTERACTIVE_SESSION: not an interactive user session (Session 0)'
            'INCONCLUSIVE: suites reported no executed tests and the run cannot be interpreted'
        )
    }

    $runResultPath = Join-Path $resultsDirectory 'run-result.json'
    $runResult | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $runResultPath -Encoding utf8

    Write-Host ''
    Write-Host "AetherWorkspace reference-desktop run: $status"
    Write-Host "  run id:  $($script:state.RunId)"
    Write-Host "  commit:  $($script:state.CommitSha)"
    Write-Host "  profile: $($script:state.Profile)"
    Write-Host "  results: $resultsDirectory"
    Write-Host "  exit:    $exitCode"
    return $exitCode
}

# ---------------------------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------------------------
if (-not $IsWindows) {
    Write-Error 'Run-AetherTests.ps1 orchestrates Windows 11 reference-desktop runs only; it cannot run on this platform.'
    exit 2
}

$exitCode = 0
try {
    Invoke-AetherTestRun
}
catch {
    $script:state.Status = 'FAIL_HARNESS'
    $script:state.Message = "Unhandled orchestrator error: $($_.Exception.Message)"
    Write-RunLog 'RESULT' $script:state.Message 'ERROR'
}
finally {
    try {
        Complete-Cleanup
    }
    catch {
        $message = "Cleanup failed: $($_.Exception.Message)"
        $script:state.CleanupWarnings.Add($message)
        Write-RunLog 'CLEANUP' $message 'ERROR'
    }
}

$script:state.FinishedAtUtc = (Get-Date).ToUniversalTime()
$exitCode = Write-RunResult
exit $exitCode
