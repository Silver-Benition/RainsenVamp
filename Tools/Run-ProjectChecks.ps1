[CmdletBinding()]
param(
    [ValidateSet('All', 'EditMode', 'PlayMode')]
    [string]$TestPlatform = 'All',

    [string]$UnityPath,

    [string]$ProjectPath,

    [switch]$NoGraphics
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

trap {
    Write-Host "自动化脚本异常：$($_.Exception.Message)" -ForegroundColor Red
    exit 2
}

function Get-ProjectUnityVersion {
    param([Parameter(Mandatory = $true)][string]$RootPath)

    $versionFile = Join-Path $RootPath 'ProjectSettings\ProjectVersion.txt'
    if (-not (Test-Path -LiteralPath $versionFile -PathType Leaf)) { return $null }

    $versionLine = Select-String -LiteralPath $versionFile -Pattern 'm_EditorVersion:\s*(.+)$' | Select-Object -First 1
    if ($null -eq $versionLine) { return $null }
    return $versionLine.Matches[0].Groups[1].Value.Trim()
}

function Resolve-UnityCandidate {
    param([Parameter(Mandatory = $true)][string]$CandidatePath)

    if ([string]::IsNullOrWhiteSpace($CandidatePath)) { return $null }

    if (Test-Path -LiteralPath $CandidatePath -PathType Leaf) {
        $resolvedFile = Resolve-Path -LiteralPath $CandidatePath
        if ($resolvedFile.Path -match '\\Unity(?:\.exe)?$') { return $resolvedFile.Path }
    }

    if (Test-Path -LiteralPath $CandidatePath -PathType Container) {
        $editorFile = Join-Path $CandidatePath 'Unity.exe'
        if (Test-Path -LiteralPath $editorFile -PathType Leaf) {
            return (Resolve-Path -LiteralPath $editorFile).Path
        }
    }

    return $null
}

function Find-UnityEditor {
    param(
        [string]$ExplicitPath,
        [string]$ProjectVersion
    )

    foreach ($directCandidate in @($ExplicitPath, $env:UNITY_EDITOR_PATH)) {
        if ([string]::IsNullOrWhiteSpace([string]$directCandidate)) { continue }
        $resolvedCandidate = Resolve-UnityCandidate -CandidatePath $directCandidate
        if ($null -ne $resolvedCandidate) { return $resolvedCandidate }
    }

    $command = Get-Command Unity.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) { return $command.Source }

    $editorRoots = @(
        (Join-Path $env:ProgramFiles 'Unity\Hub\Editor'),
        (Join-Path ${env:ProgramFiles(x86)} 'Unity\Hub\Editor'),
        (Join-Path $env:LOCALAPPDATA 'Unity\Hub\Editor')
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    $discoveredCandidates = @()
    foreach ($editorRoot in $editorRoots) {
        if (-not (Test-Path -LiteralPath $editorRoot -PathType Container)) { continue }

        if (-not [string]::IsNullOrWhiteSpace($ProjectVersion)) {
            $versionCandidate = Join-Path (Join-Path $editorRoot $ProjectVersion) 'Editor\Unity.exe'
            if (Test-Path -LiteralPath $versionCandidate -PathType Leaf) {
                $discoveredCandidates += (Resolve-Path -LiteralPath $versionCandidate).Path
            }
        }

        $discoveredCandidates += Get-ChildItem -LiteralPath $editorRoot -Directory -ErrorAction SilentlyContinue |
            ForEach-Object {
                $candidate = Join-Path $_.FullName 'Editor\Unity.exe'
                if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                    (Resolve-Path -LiteralPath $candidate).Path
                }
            }
    }

    $discoveredCandidates = @($discoveredCandidates | Sort-Object -Unique)
    if ($discoveredCandidates.Count -eq 0) { return $null }

    if (-not [string]::IsNullOrWhiteSpace($ProjectVersion)) {
        $matchingVersion = $discoveredCandidates |
            Where-Object { $_ -match ('\\' + [regex]::Escape($ProjectVersion) + '\\Editor\\Unity\.exe$') } |
            Select-Object -First 1
        if ($null -ne $matchingVersion) { return $matchingVersion }
    }

    return ($discoveredCandidates | Select-Object -Last 1)
}

function Get-XmlIntAttribute {
    param(
        [Parameter(Mandatory = $true)][System.Xml.XmlElement]$Node,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $number = 0
    if ([int]::TryParse($Node.GetAttribute($Name), [ref]$number)) { return $number }
    return 0
}

function Invoke-UnityTestRun {
    param(
        [Parameter(Mandatory = $true)][string]$Platform,
        [Parameter(Mandatory = $true)][string]$UnityEditorPath,
        [Parameter(Mandatory = $true)][string]$RootPath,
        [Parameter(Mandatory = $true)][string]$OutputPath,
        [switch]$UseNoGraphics
    )

    $platformName = $Platform.ToLowerInvariant()
    $resultPath = Join-Path $OutputPath ($Platform + '.xml')
    $logPath = Join-Path $OutputPath ($Platform + '.log')
    $arguments = @(
        '-batchmode', '-projectPath', $RootPath,
        '-runTests', '-testPlatform', $platformName,
        '-testResults', $resultPath, '-logFile', $logPath
    )
    if ($UseNoGraphics) { $arguments += '-nographics' }

    Write-Host "`n[$Platform] 开始运行 Unity 测试..." -ForegroundColor Cyan
    & $UnityEditorPath @arguments
    $processExitCode = $LASTEXITCODE

    $result = [ordered]@{
        platform = $Platform
        processExitCode = $processExitCode
        resultPath = $resultPath
        logPath = $logPath
        result = $null
        total = 0
        passed = 0
        failed = 0
        errors = 0
        inconclusive = 0
        skipped = 0
        compileErrorDetected = $false
        passedQualityGate = $false
        failureReason = $null
    }

    if (Test-Path -LiteralPath $logPath -PathType Leaf) {
        $logContent = Get-Content -LiteralPath $logPath -Raw
        $result.compileErrorDetected = ($logContent -match 'error CS\d{4}') -or
            ($logContent -match 'Compilation failed') -or
            ($logContent -match 'Scripts have compiler errors')
    }

    # Unity 2022 的命令行测试可能在启动器进程返回后才完成结果文件的磁盘刷新。
    # 这里同时等待文件出现和 XML 完整可解析，避免读取到正在写入的半成品报告。
    # 全新隔离工程首次导入中文字体等大型资产可能超过一分钟，最多等待五分钟。
    $resultDeadline = (Get-Date).AddMinutes(5)
    $testRun = $null
    $parseError = $null
    while ($null -eq $testRun -and (Get-Date) -lt $resultDeadline) {
        if (Test-Path -LiteralPath $resultPath -PathType Leaf) {
            try {
                [xml]$xml = Get-Content -LiteralPath $resultPath -Raw
                $testRun = $xml.SelectSingleNode('//test-run')
                if ($null -eq $testRun) {
                    $parseError = '测试结果 XML 中没有找到 test-run 节点。'
                }
            }
            catch {
                $parseError = $_.Exception.Message
                $testRun = $null
            }
        }

        if ($null -eq $testRun) {
            Start-Sleep -Milliseconds 500
        }
    }

    if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
        $result.failureReason = 'Unity 没有生成测试结果 XML。请检查 Unity 日志或项目锁状态。'
        return [pscustomobject]$result
    }

    if ($null -eq $testRun) {
        $result.failureReason = "测试结果 XML 在等待期限内未写入完成：$parseError"
        return [pscustomobject]$result
    }

    $result.result = $testRun.GetAttribute('result')
    $result.total = Get-XmlIntAttribute -Node $testRun -Name 'total'
    $result.passed = Get-XmlIntAttribute -Node $testRun -Name 'passed'
    $result.failed = Get-XmlIntAttribute -Node $testRun -Name 'failed'
    $result.errors = Get-XmlIntAttribute -Node $testRun -Name 'errors'
    $result.inconclusive = Get-XmlIntAttribute -Node $testRun -Name 'inconclusive'
    $result.skipped = Get-XmlIntAttribute -Node $testRun -Name 'skipped'

    $qualityGatePassed =
        ($processExitCode -eq 0) -and
        ($result.result -eq 'Passed') -and
        ($result.total -gt 0) -and
        ($result.failed -eq 0) -and
        ($result.errors -eq 0) -and
        ($result.inconclusive -eq 0) -and
        (-not $result.compileErrorDetected)

    $result.passedQualityGate = $qualityGatePassed
    if (-not $qualityGatePassed -and [string]::IsNullOrWhiteSpace($result.failureReason)) {
        $result.failureReason = '测试失败、编译错误、测试报告状态异常，或没有执行到任何测试。'
    }
    return [pscustomobject]$result
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($ProjectPath)) { $ProjectPath = Join-Path $scriptRoot '..' }
$resolvedProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
$projectUnityVersion = Get-ProjectUnityVersion -RootPath $resolvedProjectPath
$unityEditor = Find-UnityEditor -ExplicitPath $UnityPath -ProjectVersion $projectUnityVersion

if ($null -eq $unityEditor) {
    Write-Error @"
无法找到 Unity Editor。
项目版本：$projectUnityVersion
请使用 -UnityPath 指定 Unity.exe，或设置 UNITY_EDITOR_PATH 环境变量。
示例：
  .\Tools\Run-ProjectChecks.ps1 -UnityPath 'C:\Program Files\Unity\Hub\Editor\2022.3.62f3c1\Editor\Unity.exe'
"@
    exit 2
}

$requiredFiles = @(
    'ProjectSettings\ProjectVersion.txt', 'ProjectSettings\EditorBuildSettings.asset',
    'Packages\manifest.json', 'Assets\Scenes\MainMenu.unity',
    'Assets\Scenes\MainMenu.unity.meta', 'Assets\Scenes\MainLevel.unity',
    'Assets\Scenes\MainLevel.unity.meta'
)
$preflightErrors = @()
foreach ($relativePath in $requiredFiles) {
    $absolutePath = Join-Path $resolvedProjectPath $relativePath
    if (-not (Test-Path -LiteralPath $absolutePath -PathType Leaf)) {
        $preflightErrors += "缺少关键文件：$relativePath"
    }
}

$gitCommand = Get-Command git.exe -ErrorAction SilentlyContinue
if ($null -ne $gitCommand) {
    & $gitCommand.Source -C $resolvedProjectPath diff --check 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { $preflightErrors += 'git diff --check 检测到空白或补丁格式问题。' }
}

if ($preflightErrors.Count -gt 0) {
    Write-Host '预检失败：' -ForegroundColor Red
    $preflightErrors | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

$runId = Get-Date -Format 'yyyyMMdd-HHmmss'
$outputPath = Join-Path $resolvedProjectPath (Join-Path 'Logs\Automation' $runId)
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null

$platforms = switch ($TestPlatform) {
    'All' { @('EditMode', 'PlayMode'); break }
    'EditMode' { @('EditMode'); break }
    'PlayMode' { @('PlayMode'); break }
}

Write-Host "项目：$resolvedProjectPath"
Write-Host "Unity：$unityEditor"
Write-Host "版本：$projectUnityVersion"
Write-Host "报告目录：$outputPath"

$testResults = @()
foreach ($platform in $platforms) {
    $testResults += Invoke-UnityTestRun -Platform $platform -UnityEditorPath $unityEditor -RootPath $resolvedProjectPath -OutputPath $outputPath -UseNoGraphics:$NoGraphics
}

$failedTestCount = @($testResults | Where-Object { -not $_.passedQualityGate }).Count
$overallPassed = (@($testResults).Count -gt 0) -and ($failedTestCount -eq 0)
$summary = [ordered]@{
    runId = $runId
    timestamp = (Get-Date).ToString('o')
    projectPath = $resolvedProjectPath
    unityEditor = $unityEditor
    unityVersion = $projectUnityVersion
    testPlatform = $TestPlatform
    noGraphics = [bool]$NoGraphics
    passedQualityGate = $overallPassed
    tests = $testResults
}
$summaryPath = Join-Path $outputPath 'summary.json'
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

Write-Host "`n测试摘要：" -ForegroundColor Cyan
$testResults |
    Select-Object platform, processExitCode, result, total, passed, failed, errors, inconclusive, skipped, passedQualityGate |
    Format-Table -AutoSize
Write-Host "报告：$summaryPath"

if ($overallPassed) {
    Write-Host '质量门禁通过。' -ForegroundColor Green
    exit 0
}

Write-Host '质量门禁失败：交付前必须处理失败项，或明确记录为未验证。' -ForegroundColor Red
$testResults |
    Where-Object { -not $_.passedQualityGate } |
    ForEach-Object { Write-Host "  - $($_.platform)：$($_.failureReason)" -ForegroundColor Red }
exit 1
