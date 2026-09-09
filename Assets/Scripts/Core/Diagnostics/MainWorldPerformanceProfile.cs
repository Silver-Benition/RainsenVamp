using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>主世界性能基线的运行模式。</summary>
public enum MainWorldPerformanceRunMode
{
    NormalManual = 0,
    CapacitySweep = 1,
    PickupBurst = 2,
    FreezeTransition = 3
}

/// <summary>性能采样阶段；阶段名称会直接写入 JSON 报告。</summary>
public enum MainWorldPerformanceStageKind
{
    Startup = 0,
    Warmup = 1,
    Ramp = 2,
    Steady = 3,
    Transition = 4,
    FreezeSustain = 5,
    Thaw = 6,
    PickupBurst = 7,
    Manual = 8,
    Completed = 9,
    Interrupted = 10
}

/// <summary>单个容量阶梯的目标和显示名称。</summary>
[Serializable]
public sealed class MainWorldPerformanceTier
{
    [Min(1)] public int targetActiveEnemies = 100;
    [Tooltip("只用于报告和人工复核，不参与逻辑判断。")]
    public string label = "tier-100";
}

/// <summary>采样器配置。数组容量按最高帧率和阶段时长预分配，避免采样热路径扩容。</summary>
[Serializable]
public sealed class MainWorldPerformanceSamplingSettings
{
    [Min(1f)] public float warmupSeconds = 15f;
    [Min(0.1f)] public float rampSeconds = 15f;
    [Min(1f)] public float steadySeconds = 60f;
    [Min(0.1f)] public float transitionSeconds = 2f;
    [Min(0.1f)] public float freezeSeconds = 15f;
    [Min(0.1f)] public float thawSeconds = 5f;
    [Min(0.1f)] public float pickupSeconds = 15f;
    [Min(0.1f)] public float lowFrequencySampleInterval = 0.25f;
    [Min(30)] public int expectedMaximumFramesPerSecond = 240;
    [Min(0.1f)] public float minimumTargetCoverage = 0.9f;
    [Range(0.5f, 1f)] public float minimumTargetSampleCoverage = 0.95f;
    [Min(0.1f)] public float severeFrameTimeMilliseconds = 66.67f;
    [Min(0.1f)] public float severeFrameTimeAbortSeconds = 5f;
    [Min(0f)] public float instrumentationControlSeconds = 2f;
    [Min(1)] public int maximumPickupBurstCount = 1000;

    /// <summary>根据阶段秒数与预期最高帧率计算固定的帧样本容量。</summary>
    public int CalculateFrameCapacity()
    {
        float totalSeconds = warmupSeconds + rampSeconds + steadySeconds +
            transitionSeconds + freezeSeconds + thawSeconds + pickupSeconds + 10f;
        float safeRate = Mathf.Max(30f, expectedMaximumFramesPerSecond);
        return Mathf.Max(1024, Mathf.CeilToInt(totalSeconds * safeRate));
    }
}

/// <summary>主世界性能基线的场景和数据入口。</summary>
[CreateAssetMenu(fileName = "MainWorldPerformanceProfile", menuName = "RainsenVamp/Diagnostics/Main World Performance Profile")]
public sealed class MainWorldPerformanceProfile : ScriptableObject
{
    [Header("运行入口")]
    public string profileId = "main-world-capacity-baseline";
    public MainWorldPerformanceRunMode defaultMode = MainWorldPerformanceRunMode.CapacitySweep;
    [Tooltip("只会作用于包含本 Profile 的生成测试场景；普通 MainLevel 没有 Runner，因此不会自动启动。")]
    public bool enableHarness = true;
    public bool autoStart = true;
    public int fixedRandomSeed = 21021;

    [Header("生成测试资产")]
    public WorldLineDataSO generatedMainWorld;
    public WaveConfigSO generatedWaveConfig;
    public CharacterDataSO normalCharacter;
    public CharacterDataSO capacityCharacter;
    public BossEncounterDataSO delayedBossEncounter;
    public GameObject expPickupPrefab;
    public GameObject coinPickupPrefab;
    public WeaponDataSO[] fullLoadout = new WeaponDataSO[0];

    [Header("容量目标")]
    public MainWorldPerformanceTier[] capacityTiers =
    {
        new MainWorldPerformanceTier { targetActiveEnemies = 100, label = "tier-100" },
        new MainWorldPerformanceTier { targetActiveEnemies = 300, label = "tier-300" },
        new MainWorldPerformanceTier { targetActiveEnemies = 500, label = "tier-500" }
    };
    [Min(1)] public int nearLimitTierIndex = 2;
    [Min(1)] public int fullLoadoutLevel = 8;
    [Min(1f)] public float capacityMaximumHealthOverride = 100000f;
    [Min(1f)] public float capacityExperienceRequirementOverride = 100000000f;
    [Min(1f)] public float capacityWaveSupplementalRateMultiplier = 2f;

    [Header("采样设置")]
    public MainWorldPerformanceSamplingSettings sampling = new MainWorldPerformanceSamplingSettings();
    [Tooltip("容量模式在运行时关闭 VSync 并使用无上限吞吐采样；手动模式保留 60 FPS 目标。")]
    public bool throughputSweep = true;
    [Min(1)] public int normalModeTargetEnemies = 10;
    [Min(1)] public int normalModeTargetFrameRate = 60;

    [Header("构建证据")]
    [Tooltip("由 Editor 构建入口写入，不由运行时猜测。")]
    public string sourceRevision;
    public string sourceHash;
    public string generatedByUnityVersion;

    /// <summary>判断性能测试资产是否能启动真实主世界链路。</summary>
    public bool IsValid
    {
        get
        {
            return enableHarness && generatedMainWorld != null && generatedWaveConfig != null &&
                normalCharacter != null && capacityCharacter != null && sampling != null &&
                capacityTiers != null && capacityTiers.Length > 0;
        }
    }

    /// <summary>返回经过边界保护的最高容量阶梯索引。</summary>
    public int GetSafeNearLimitTierIndex()
    {
        int count = capacityTiers == null ? 0 : capacityTiers.Length;
        return count == 0 ? 0 : Mathf.Clamp(nearLimitTierIndex, 0, count - 1);
    }
}

/// <summary>单个 ProfilerRecorder 计数器的可用性与单位说明。</summary>
[Serializable]
public struct MainWorldPerformanceCounterSupport
{
    public bool mainThread;
    public bool renderThread;
    public bool gpu;
    public bool gcAllocatedInFrame;
    public bool systemUsedMemory;
    public bool totalUsedMemory;
    public bool graphicsUsedMemory;
    public bool textureMemory;
    public bool batches;
    public bool setPassCalls;
    public bool drawCalls;

    /// <summary>返回指定计数器缺失时报告使用的无效值。</summary>
    public static float UnavailableValue => -1f;
}

/// <summary>单个阶段的帧时间、对象数量和容量判断结果。</summary>
[Serializable]
public sealed class MainWorldPerformanceStageReport
{
    public MainWorldPerformanceStageKind stage;
    public string label;
    public bool instrumentationEnabled;
    public bool interrupted;
    public bool insufficientSamples;
    public bool capacityOverflowed;
    public bool insufficientLoad;
    public bool contaminated;
    public string interruptionReason;
    public string invalidReason;
    public float requestedDurationSeconds;
    public float observedDurationSeconds;
    public string stageStartedAtUtc;
    public int requestedTargetActiveEnemies;
    public int minimumObservedActiveEnemies;
    public int maximumObservedActiveEnemies;
    public float targetCoverageRatio;
    public int frameSampleCount;
    public int droppedFrameSampleCount;
    public int lowFrequencySampleCount;
    public string loadoutDescription;
    public int gpuCounterAnomalyCount;
    public string gpuCounterInvalidReason;
    public MainWorldPerformanceFrameStatistics frame;
}

/// <summary>阶段帧样本的统计快照。GPU 和其他不可用计数器以 -1 表示，并由 support 字段解释。</summary>
[Serializable]
public struct MainWorldPerformanceFrameStatistics
{
    public float averageMilliseconds;
    public float p95Milliseconds;
    public float p99Milliseconds;
    public float maximumMilliseconds;
    public float ratioOver16Point67Milliseconds;
    public float ratioOver33Point33Milliseconds;
    public float ratioOver50Milliseconds;
    public float averageMainThreadMilliseconds;
    public float averageRenderThreadMilliseconds;
    public float averageGpuMilliseconds;
    public float averageGcAllocatedBytesPerFrame;
    public float averageSystemUsedMemoryBytes;
    public float averageTotalUsedMemoryBytes;
    public float averageGraphicsUsedMemoryBytes;
    public float averageTextureMemoryBytes;
    public float averageBatches;
    public float averageSetPassCalls;
    public float averageDrawCalls;
    public float firstSystemUsedMemoryBytes;
    public float lastSystemUsedMemoryBytes;
    public float systemUsedMemoryTrendBytes;
    public int maximumActiveProjectiles;
    public int averageActiveProjectiles;
    public int maximumActivePickups;
    public int averageActivePickups;
    public int maximumVisibleEnemyEstimate;
    public int averageVisibleEnemyEstimate;
    public MainWorldPerformanceCounterSupport support;
}

/// <summary>运行期间采集的设备、构建、设置和阶段列表。</summary>
[Serializable]
public sealed class MainWorldPerformanceReport
{
    public string schemaVersion = "session-21-main-world-performance-v1";
    public string reportId;
    public string profileId;
    public MainWorldPerformanceRunMode mode;
    public string outcome;
    public string generatedAtUtc;
    public string unityVersion;
    public string applicationVersion;
    public string platform;
    public string deviceModel;
    public string processorType;
    public int processorCount;
    public int systemMemorySizeMb;
    public int graphicsMemorySizeMb;
    public string graphicsDeviceName;
    public string graphicsDeviceType;
    public string graphicsDeviceVendor;
    public int screenWidth;
    public int screenHeight;
    public int qualityLevel;
    public string qualityName;
    public int vSyncCount;
    public int targetFrameRate;
    public float fixedDeltaTime;
    public bool developmentBuild;
    public bool dedicatedPerformanceBuild;
    public string sourceRevision;
    public string sourceHash;
    public string workloadDescription;
    public string settingsDescription;
    public int fixedRandomSeed;
    public string fixedLoadoutDescription;
    public float capacityMaximumHealthOverride;
    public float capacityExperienceRequirementOverride;
    public float capacityWaveSupplementalRateMultiplier;
    public int pickupBurstRequested;
    public int pickupBurstSpawnedCount;
    public int maximumTrackedBurstPickups;
    public int maximumObservedNaturalPickups;
    public bool pickupSourceBreakdownIsEstimate;
    public string interruptionReason;
    public bool stableEvidence;
    public bool executionComplete;
    public bool capacityEvidenceValid;
    public bool hasInsufficientLoad;
    public bool hasUnavailableCounters;
    public bool accountIsolationApplied;
    public bool automaticBossDisabledForHorizon;
    public bool freezeTransitionCaptured;
    public bool freezeTransitionMissed;
    public float freezeRequestedDurationSeconds;
    public float freezeObservedDurationSeconds;
    public bool ordinaryBuildAutoActivationBlocked;
    public bool activeCountsAreAuthoritative;
    public bool visibleCountsAreEstimate;
    public bool instrumentationControlCaptured;
    public float instrumentationControlAverageMilliseconds = -1f;
    public float instrumentationEnabledAverageMilliseconds = -1f;
    public float instrumentationOverheadMilliseconds = -1f;
    public int gpuCounterAnomalyCount;
    public string gpuCounterInvalidReason;
    public List<MainWorldPerformanceStageReport> stages = new List<MainWorldPerformanceStageReport>();

    /// <summary>将阶段结果加入报告，并同步整体的不足和计数器可用性标记。</summary>
    public void AddStage(MainWorldPerformanceStageReport stageReport)
    {
        if (stageReport == null)
        {
            return;
        }

        stages.Add(stageReport);
        hasInsufficientLoad |= stageReport.insufficientLoad;
        gpuCounterAnomalyCount += stageReport.gpuCounterAnomalyCount;
        if (string.IsNullOrWhiteSpace(gpuCounterInvalidReason) && !string.IsNullOrWhiteSpace(stageReport.gpuCounterInvalidReason))
        {
            gpuCounterInvalidReason = stageReport.gpuCounterInvalidReason;
        }
        hasUnavailableCounters |= !stageReport.frame.support.mainThread ||
            !stageReport.frame.support.renderThread || !stageReport.frame.support.gpu;
        if (stageReport.interrupted)
        {
            stableEvidence = false;
        }
    }
}

/// <summary>报告判断逻辑的无 Unity 状态辅助入口，供 Editor 测试复核容量证据语义。</summary>
public static class MainWorldPerformanceReportEvaluator
{
    /// <summary>判断实际最低在场数量是否达到目标覆盖率。</summary>
    public static bool MeetsTargetCoverage(int minimumObserved, int requestedTarget, float minimumCoverage)
    {
        if (requestedTarget <= 0)
        {
            return true;
        }

        float safeCoverage = Mathf.Clamp01(minimumCoverage);
        return minimumObserved >= Mathf.CeilToInt(requestedTarget * safeCoverage);
    }

    /// <summary>判断低频负载采样点中有多少比例达到目标阈值，默认采用 95% 证据门槛。</summary>
    public static bool MeetsTargetSampleCoverage(float observedCoverageRatio, float minimumSampleCoverage)
    {
        return observedCoverageRatio >= Mathf.Clamp01(minimumSampleCoverage);
    }

    /// <summary>判断阶段是否可作为稳定容量证据，排除中断、样本不足、负载不足和污染。</summary>
    public static bool IsStableCapacityEvidence(MainWorldPerformanceStageReport stageReport)
    {
        return stageReport != null && !stageReport.interrupted &&
            !stageReport.insufficientSamples && !stageReport.capacityOverflowed && !stageReport.insufficientLoad &&
            !stageReport.contaminated && stageReport.frameSampleCount > 0;
    }
}
