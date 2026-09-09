[CmdletBinding()]
param(
    [ValidateSet('Build', 'Run', 'All')]
    [string]$Action = 'All',

    [ValidateSet('capacity', 'normal', 'pickup', 'freeze')]
    [string]$Mode = 'capacity',

    [ValidateSet('controlled', 'natural')]
    [string]$EventMode = 'controlled',

    [int]$Tier = -1,
    [ValidateSet(300, 1000)]
    [int]$PickupCount = 1000,
    [ValidateRange(1, 12)]
    [int]$Repeats = 1,
    [ValidateRange(0.01, 100)]
    [double]$DurationScale = 1,
    [int]$Seed = 21021,

    [string]$UnityPath,
    [string]$ProjectPath,
    [string]$BuildPath,
    [string]$OutputPath,
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

trap {
    Write-Error ("MainWorldPerformance failed: " + $_.Exception.Message)
    exit 2
}

function Get-ProjectUnityVersion {
    param([Parameter(Mandatory = $true)][string]$RootPath)
    $versionFile = Join-Path $RootPath 'ProjectSettings/ProjectVersion.txt'
    if (-not (Test-Path -LiteralPath $versionFile -PathType Leaf)) { return $null }
    $line = Select-String -LiteralPath $versionFile -Pattern 'm_EditorVersion:[ 	]*(.+)$' | Select-Object -First 1
    if ($null -eq $line) { return $null }
    return $line.Matches[0].Groups[1].Value.Trim()
}

function Resolve-UnityEditor {
    param([string]$ExplicitPath, [string]$ProjectVersion)
    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath) -and
        (Test-Path -LiteralPath $ExplicitPath -PathType Leaf)) {
        return (Resolve-Path -LiteralPath $ExplicitPath).Path
    }

    if (-not [string]::IsNullOrWhiteSpace($env:UNITY_EDITOR_PATH) -and
        (Test-Path -LiteralPath $env:UNITY_EDITOR_PATH -PathType Leaf)) {
        return (Resolve-Path -LiteralPath $env:UNITY_EDITOR_PATH).Path
    }

    $roots = @(
        (Join-Path $env:ProgramFiles 'Unity/Hub/Editor'),
        (Join-Path ${env:ProgramFiles(x86)} 'Unity/Hub/Editor'),
        (Join-Path $env:LOCALAPPDATA 'Unity/Hub/Editor')
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    $candidates = @()
    foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root -PathType Container)) { continue }
        if (-not [string]::IsNullOrWhiteSpace($ProjectVersion)) {
            $preferred = Join-Path (Join-Path $root $ProjectVersion) 'Editor/Unity.exe'
            if (Test-Path -LiteralPath $preferred -PathType Leaf) { $candidates += $preferred }
        }
        $candidates += Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue |
            ForEach-Object {
                $candidate = Join-Path $_.FullName 'Editor/Unity.exe'
                if (Test-Path -LiteralPath $candidate -PathType Leaf) { $candidate }
            }
    }
    $candidates = @($candidates | Sort-Object -Unique)
    if ($candidates.Count -eq 0) { return $null }
    if (-not [string]::IsNullOrWhiteSpace($ProjectVersion)) {
        $matching = $candidates | Where-Object { $_ -match [regex]::Escape($ProjectVersion) } | Select-Object -First 1
        if ($null -ne $matching) { return (Resolve-Path -LiteralPath $matching).Path }
    }
    return (Resolve-Path -LiteralPath ($candidates | Select-Object -Last 1)).Path
}

function Invoke-WaitingProcess {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$Label,
        [switch]$NoNewWindow
    )
    Write-Host ("[$Label] " + $FilePath) -ForegroundColor Cyan
    $startParameters = @{
        FilePath = $FilePath
        ArgumentList = $Arguments
        Wait = $true
        PassThru = $true
    }
    if ($NoNewWindow) { $startParameters.NoNewWindow = $true }
    $process = Start-Process @startParameters
    return $process.ExitCode
}

function Get-LatestFinalReport {
    param([Parameter(Mandatory = $true)][string]$Directory)
    $reports = @(Get-ChildItem -LiteralPath $Directory -Filter '*-final.json' -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending)
    if ($reports.Count -eq 0) { return $null }
    return $reports[0]
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($ProjectPath)) { $ProjectPath = Join-Path $scriptRoot '..' }
$ProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
$version = Get-ProjectUnityVersion -RootPath $ProjectPath
$unity = Resolve-UnityEditor -ExplicitPath $UnityPath -ProjectVersion $version
if ($null -eq $unity) { throw 'Unity Editor was not found. Use -UnityPath or UNITY_EDITOR_PATH.' }

if ([string]::IsNullOrWhiteSpace($BuildPath)) {
    $BuildPath = Join-Path $ProjectPath 'Builds/Performance/MainWorldPerformance.exe'
}
else {
    $BuildPath = [System.IO.Path]::GetFullPath($BuildPath)
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $ProjectPath 'Logs/Performance'
}
else {
    $OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
}
$outputRoot = $OutputPath
$runId = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss-fff') + '-' +
    ([guid]::NewGuid().ToString('N').Substring(0, 8))
$OutputPath = Join-Path $outputRoot ('run-' + $runId)
New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $ProjectPath 'Logs/Automation') | Out-Null

$unityProcesses = @(Get-Process -Name Unity -ErrorAction SilentlyContinue)
if ($unityProcesses.Count -gt 0) {
    throw 'Unity is already running. Close it normally before build or performance execution; this script never kills a user process.'
}

if (($Action -eq 'Build' -or $Action -eq 'All') -and -not $SkipBuild) {
    $buildLog = Join-Path $ProjectPath 'Logs/Automation/session21-performance-build.log'
    $buildArguments = @(
        '-batchmode', '-quit', '-projectPath', $ProjectPath,
        '-executeMethod', 'MainWorldPerformanceBuild.BuildWindowsDevelopment',
        '-logFile', $buildLog
    )
    $buildExit = Invoke-WaitingProcess -FilePath $unity -Arguments $buildArguments -Label 'Build' -NoNewWindow
    if ($buildExit -ne 0) { throw "Unity build exited with code $buildExit. See $buildLog" }
    if (-not (Test-Path -LiteralPath $BuildPath -PathType Leaf)) { throw "Build output missing: $BuildPath" }
}

if ($Action -eq 'Build') { exit 0 }
if (-not (Test-Path -LiteralPath $BuildPath -PathType Leaf)) {
    throw "Performance Player missing: $BuildPath. Run with -Action Build or -Action All."
}

$durationText = $DurationScale.ToString('0.###', [System.Globalization.CultureInfo]::InvariantCulture)
$playerArguments = @(
    '-screen-width', '1920',
    '-screen-height', '1080',
    '-screen-fullscreen', '0',
    '--perf-mode', $Mode,
    '--perf-events', $EventMode,
    '--perf-output', $OutputPath,
    '--perf-duration', $durationText,
    '--perf-seed', $Seed.ToString([System.Globalization.CultureInfo]::InvariantCulture),
    '--perf-pickups', $PickupCount.ToString([System.Globalization.CultureInfo]::InvariantCulture),
    '--perf-repeats', $Repeats.ToString([System.Globalization.CultureInfo]::InvariantCulture)
)
$playerArguments += '-logFile'
$playerArguments += (Join-Path $OutputPath 'UnityPlayer.log')
if ($Tier -ge 0) {
    $playerArguments += '--perf-tier'
    $playerArguments += $Tier.ToString([System.Globalization.CultureInfo]::InvariantCulture)
}

# 不传 -batchmode/-nographics；这是图形 Player，必须真实创建窗口和 GPU backend。
$playerExit = Invoke-WaitingProcess -FilePath $BuildPath -Arguments $playerArguments -Label 'Graphical Player'
$reportFile = Get-LatestFinalReport -Directory $OutputPath
if ($null -eq $reportFile) { throw "Player exited $playerExit but produced no *-final.json under $OutputPath." }

$report = Get-Content -LiteralPath $reportFile.FullName -Raw | ConvertFrom-Json
$summary = [ordered]@{
    generatedAtUtc = [DateTime]::UtcNow.ToString('o')
    playerExitCode = $playerExit
    reportPath = $reportFile.FullName
    outcome = $report.outcome
    executionComplete = [bool]$report.executionComplete
    stableEvidence = [bool]$report.stableEvidence
    capacityEvidenceValid = [bool]$report.capacityEvidenceValid
    graphicsDeviceType = $report.graphicsDeviceType
    graphicsDeviceName = $report.graphicsDeviceName
    screenWidth = [int]$report.screenWidth
    screenHeight = [int]$report.screenHeight
    stageCount = @($report.stages).Count
}
$summaryPath = Join-Path $OutputPath ($reportFile.BaseName + '-script-summary.json')
$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

if ($playerExit -ne 0) { throw "Graphical Player exited with code $playerExit. Report: $($reportFile.FullName)" }
if ($report.outcome -ne 'complete' -or -not $report.executionComplete) {
    throw "Performance run is incomplete/interrupted (outcome=$($report.outcome)). Report: $($reportFile.FullName)"
}
if ($report.graphicsDeviceType -eq 'Null' -or [string]::IsNullOrWhiteSpace($report.graphicsDeviceName)) {
    throw "Performance run has no valid graphics backend. Report: $($reportFile.FullName)"
}
if ([int]$report.screenWidth -ne 1920 -or [int]$report.screenHeight -ne 1080) {
    throw "Performance run did not use the required 1920x1080 window (actual=$($report.screenWidth)x$($report.screenHeight)). Report: $($reportFile.FullName)"
}
if ($Mode -eq 'capacity' -and -not $report.capacityEvidenceValid) {
    throw "Capacity run completed but lacks valid steady-load evidence (insufficient load, contamination, overflow, or interruption). Report: $($reportFile.FullName)"
}

Write-Host ("Performance report: " + $reportFile.FullName) -ForegroundColor Green
Write-Host ("Script summary: " + $summaryPath) -ForegroundColor Green
exit 0
