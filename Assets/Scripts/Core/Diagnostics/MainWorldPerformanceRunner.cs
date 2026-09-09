using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// 生成测试场景中的主世界性能运行器。
/// 
/// Runner 只存在于 Assets/Tests/Performance/Generated 生成场景，并以 Profile 作为显式
/// 启动开关。容量阶梯只在运行时修改生成的 WaveConfig 副本；敌人、敌方弹体、玩家武器、
/// 经验和金币仍通过正式 WorldWaveManager、WorldEnemySimulation 与 PoolManager 链路运行。
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-10000)]
public sealed class MainWorldPerformanceRunner : MonoBehaviour
{
    private const string BurstModifierId = "session21-performance-pickup-burst";
    private const string HealthModifierId = "session21-performance-capacity-health";
    private const string OutputArgument = "--perf-output";
    private const string ModeArgument = "--perf-mode";
    private const string EventModeArgument = "--perf-events";
    private const string SeedArgument = "--perf-seed";
    private const string DurationArgument = "--perf-duration";
    private const string TierArgument = "--perf-tier";
    private const string PickupArgument = "--perf-pickups";
    private const string RepeatsArgument = "--perf-repeats";

    [SerializeField] private MainWorldPerformanceProfile profile;
    [SerializeField] private bool runOnStart = true;

    private MainWorldPerformanceRunMode _mode;
    private MainWorldPerformanceEventMode _eventMode;
    private MainWorldPerformanceStageKind _currentStage;
    private MainWorldPerformanceReport _report;
    private PerformanceSampler _sampler;
    private Coroutine _runCoroutine;
    private string _outputPath;
    private string _currentStageLabel;
    private string _interruptReason;
    private string _modeOverride;
    private string _eventModeOverride;
    private float _durationScale = 1f;
    private int _seedOverride;
    private int _tierOverride = -1;
    private int _pickupCountOverride = -1;
    private int _repeatCount = 1;
    private bool _hasSeedOverride;
    private bool _running;
    private bool _interrupted;
    private bool _completed;
    private bool _loadoutContaminated;
    private bool _stageUnexpectedFreeze;
    private bool _pickupModifierApplied;
    private bool _burstTrackingActive;
    private bool _originalSettingsCaptured;
    private bool _accountIsolationApplied;
    private int _originalVSyncCount;
    private int _originalTargetFrameRate;
    private bool _originalRunInBackground;
    private readonly List<GameObject> _burstPickups = new List<GameObject>(1024);
    private readonly List<float> _originalSpawnRates = new List<float>(4);
    private readonly List<int> _originalSpawnCaps = new List<int>(4);
    private readonly List<float> _originalSpawnStarts = new List<float>(4);
    private readonly List<float> _originalSpawnEnds = new List<float>(4);
    private readonly Collider2D[] _visibleColliderBuffer = new Collider2D[4096];
    private readonly List<GameObject> _sceneRootBuffer = new List<GameObject>(64);
    private readonly List<MagneticPickupMotion> _pickupBuffer = new List<MagneticPickupMotion>(2048);

    private PlayerStats _playerStats;
    private PlayerController _playerController;
    private Rigidbody2D _playerRigidbody;
    private PlayerHealth _playerHealth;
    private LevelUpManager _levelUpManager;
    private WorldLineCoordinator _coordinator;
    private WorldEnemySimulation _mainSimulation;
    private WorldWaveManager _mainWaveManager;
    private WorldFreezeController _freezeController;
    private float _lowFrequencyTimer;
    private int _activeEnemyCount;
    private int _activeProjectileCount;
    private int _activePickupCount;
    private int _visibleEnemyEstimate;
    private float _severeFrameTimeElapsed;
    private int _expectedWeaponCount = -1;
    private bool _expectedFreezeStage;
    private bool _runtimeOverridesApplied;
    private bool _playerControllerOverrideApplied;
    private bool _originalPlayerControllerEnabled;
    private bool _originalWorldSwitchLocked;
    private bool _excludeNextFrameSample;
    private bool _freezeTransitionWindowActive;
    private bool _freezeTransitionObserved;
    private bool _freezeTransitionMissed;
    private float _freezeEventStartUnscaledTime;
    private float _freezeObservedDuration;
    private bool _reportWriteFailed;
    private bool _dropTableOverrideApplied;
    private float _originalChestDropChance;
    private float _originalMapInstantEffectDropChance;
    private EnemyDropTableSO _generatedDropTable;
    private readonly List<float> _originalMapInstantEffectDropWeights = new List<float>(8);
    private float _lastStageObservedDuration;
    private string _lastStageStartedAtUtc;
    private int[] _expectedWeaponLevels;
    private int _maximumTrackedBurstPickups;
    private int _maximumObservedNaturalPickups;
    private int _pickupBurstSpawnedCount;
    private Camera _mainCamera;
    private int _enemyLayerMask = -1;

    /// <summary>当前使用的性能 Profile。</summary>
    public MainWorldPerformanceProfile Profile => profile;

    /// <summary>当前运行阶段。</summary>
    public MainWorldPerformanceStageKind CurrentStage => _currentStage;

    /// <summary>运行器是否正在采样。</summary>
    public bool IsRunning => _running;

    /// <summary>运行结束或阶段刷新后的最近报告快照。</summary>
    public MainWorldPerformanceReport LastReport => _report;

    /// <summary>实际使用的模式，便于自动化测试确认命令行覆盖是否生效。</summary>
    public MainWorldPerformanceRunMode ActiveMode => _mode;

    /// <summary>实际使用的随机事件模式；manual 始终报告 Natural。</summary>
    public MainWorldPerformanceEventMode ActiveEventMode => _eventMode;

    /// <summary>受控掉落是否已应用到明确的生成副本。</summary>
    public bool ControlledDropOverrideApplied => _dropTableOverrideApplied;

    /// <summary>供 PlayMode 清理测试读取明确的生成掉落副本。</summary>
    public EnemyDropTableSO GeneratedDropTable => profile != null ? profile.generatedDropTable : null;

    /// <summary>供生成场景测试断言主世界真实模拟器存在。</summary>
    public WorldEnemySimulation MainSimulation => _mainSimulation;

    /// <summary>供生成场景测试使用的真实经验 Prefab。</summary>
    public GameObject ExpPickupPrefab => profile != null ? profile.expPickupPrefab : null;

    /// <summary>供生成场景测试使用的真实金币 Prefab。</summary>
    public GameObject CoinPickupPrefab => profile != null ? profile.coinPickupPrefab : null;

#if MAINWORLD_PERFORMANCE_BUILD
    /// <summary>专用 Development Build 在任何场景 Awake 前注入内存账号后端。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InstallDedicatedAccountStorage()
    {
        AccountProgressService.SetStorageForTests(new InMemoryAccountProgressStorage());
    }
#endif

    /// <summary>读取 Profile、固定随机种子、创建预分配采样器并应用运行时吞吐设置。</summary>
    private void Awake()
    {
        ParseCommandLineArguments();
        if (!ShouldRunHarness())
        {
            enabled = false;
            return;
        }

        if (profile == null || !profile.IsValid)
        {
            FailStartup("invalid-profile");
            enabled = false;
            return;
        }

#if MAINWORLD_PERFORMANCE_BUILD
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
        {
            FailStartup("null-graphics-device");
            enabled = false;
            return;
        }
#endif

        CaptureOriginalSettings();
        InstallEditorAccountIsolation();
        _mode = ResolveMode();
        _eventMode = ResolveEventMode();
        if (_mode == MainWorldPerformanceRunMode.NormalManual)
        {
            // 手动模式必须保留正常奖励、升级和冻结行为，即使 Profile 默认使用 controlled。
            _eventMode = MainWorldPerformanceEventMode.Natural;
        }
        int seed = _hasSeedOverride ? _seedOverride : profile.fixedRandomSeed;
        UnityEngine.Random.InitState(seed);
        ApplyRuntimeFrameSettings();
        CacheWaveDefaults();

        _outputPath = ResolveOutputPath();
        _sampler = new PerformanceSampler(profile.sampling.CalculateFrameCapacity());
        _sampler.BeginInstrumentationControl(profile.sampling.instrumentationControlSeconds);
        _sampler.BeginStage(MainWorldPerformanceStageKind.Startup, 0, profile.sampling.minimumTargetCoverage, false);
        _report = CreateReport(seed);
        _currentStage = MainWorldPerformanceStageKind.Startup;
        _currentStageLabel = "startup-control";
    }

    /// <summary>等待场景组件完成 Start 后启动阶段状态机。</summary>
    private IEnumerator Start()
    {
        if (!enabled || !runOnStart)
        {
            yield break;
        }

        yield return null;
        if (!ResolveRuntimeDependencies())
        {
            InterruptRun("runtime-dependency-missing");
            FinalizeReport("incomplete");
            yield break;
        }

        if (!ApplyEventMode())
        {
            InterruptRun("controlled-drop-table-missing");
            FinalizeReport("incomplete");
            yield break;
        }

        ConfigureCharacterAndExperience();
        CaptureCurrentLoadoutSnapshot();
        if (_report != null)
        {
            _report.fixedLoadoutDescription = BuildLoadoutDescription();
        }
        RestoreWaveDefaults();
        ApplyAutomatedRuntimeOverrides();
        _running = true;
        _runCoroutine = StartCoroutine(RunStateMachine());
    }

    /// <summary>每帧写入基础帧时间；对象数量和可见估计只按低频间隔更新。</summary>
    private void Update()
    {
        if (!_running || _sampler == null || _completed)
        {
            return;
        }

        if (_excludeNextFrameSample)
        {
            _excludeNextFrameSample = false;
            return;
        }

        if (_runtimeOverridesApplied && _mode != MainWorldPerformanceRunMode.NormalManual && _playerRigidbody != null)
        {
            _playerRigidbody.velocity = Vector2.zero;
        }

        if (_expectedFreezeStage && !_freezeTransitionWindowActive &&
            _currentStage == MainWorldPerformanceStageKind.FreezeSustain &&
            !WorldFreezeController.IsHostileSimulationFrozen)
        {
            // 冻结在持续窗口结束前失效时，不能把后续低负载当成成功的 thaw 事件。
            _freezeTransitionMissed = true;
        }

        if (_freezeTransitionWindowActive && !WorldFreezeController.IsHostileSimulationFrozen)
        {
            _freezeTransitionObserved = true;
            if (_freezeEventStartUnscaledTime > 0f && _freezeObservedDuration <= 0f)
            {
                _freezeObservedDuration = Mathf.Max(0f, Time.unscaledTime - _freezeEventStartUnscaledTime);
            }
        }

        if (Time.timeScale <= 0f)
        {
            InterruptRun("game-paused");
        }

        bool lowFrequencySample = false;
        if (_lowFrequencyTimer <= 0f)
        {
            _activeEnemyCount = _mainSimulation != null ? Mathf.Max(0, _mainSimulation.ActiveEnemyCount) : 0;
            if (_sampler.InstrumentationEnabled)
            {
                _activeProjectileCount = _mainSimulation != null ? Mathf.Max(0, _mainSimulation.ActiveProjectileCount) : 0;
                _activePickupCount = CountActivePickups();
                _visibleEnemyEstimate = CountVisibleEnemyEstimate();
            }
            else
            {
                // A 对照只保留最小帧时和 WorldEnemySimulation 权威敌人数；
                // 弹体、拾取物和相机可见估算会触发额外遍历，必须在 control 窗口关闭。
                _activeProjectileCount = 0;
                _activePickupCount = 0;
                _visibleEnemyEstimate = 0;
            }
            _lowFrequencyTimer = Mathf.Max(0.05f, profile.sampling.lowFrequencySampleInterval);
            lowFrequencySample = true;
            RecordContaminationState();
        }
        else
        {
            _lowFrequencyTimer -= Mathf.Max(0f, Time.unscaledDeltaTime);
        }

        float frameMilliseconds = Mathf.Max(0f, Time.unscaledDeltaTime) * 1000f;
        if (frameMilliseconds > profile.sampling.severeFrameTimeMilliseconds)
        {
            _severeFrameTimeElapsed += Mathf.Max(0f, Time.unscaledDeltaTime);
            if (_severeFrameTimeElapsed >= profile.sampling.severeFrameTimeAbortSeconds)
            {
                InterruptRun("severe-frame-time-abort");
            }
        }
        else
        {
            _severeFrameTimeElapsed = 0f;
        }

        _sampler.RecordFrame(
            Time.unscaledDeltaTime,
            _activeEnemyCount,
            _activeProjectileCount,
            _activePickupCount,
            _visibleEnemyEstimate,
            lowFrequencySample);
    }

    /// <summary>玩家死亡或正式结果冻结时中断当前性能证据。</summary>
    private void OnEnable()
    {
        if (_playerHealth != null)
        {
            _playerHealth.Died -= HandlePlayerDied;
            _playerHealth.Died += HandlePlayerDied;
        }
    }

    /// <summary>取消玩家死亡监听。</summary>
    private void OnDisable()
    {
        if (_playerHealth != null)
        {
            _playerHealth.Died -= HandlePlayerDied;
        }

        if (_runCoroutine != null)
        {
            StopCoroutine(_runCoroutine);
            _runCoroutine = null;
        }

        _running = false;
        if (!_completed && _report != null)
        {
            _interruptReason = "runner-disabled";
            FinalizeReport("interrupted");
        }

        RestoreWaveDefaults();
        RestoreDropTableDefaults();
        RestoreAutomatedRuntimeOverrides();
        RestoreOriginalSettings();
    }

    /// <summary>场景销毁时停止协程、回收拾取物、释放计数器和恢复帧设置。</summary>
    private void OnDestroy()
    {
        if (_runCoroutine != null)
        {
            StopCoroutine(_runCoroutine);
            _runCoroutine = null;
        }

        if (_pickupModifierApplied && _playerStats != null)
        {
            _playerStats.RemoveModifiers(BurstModifierId);
            _pickupModifierApplied = false;
        }
        CleanupBurstPickups();
        RestoreWaveDefaults();
        RestoreDropTableDefaults();
        RestoreAutomatedRuntimeOverrides();
        if (_sampler != null)
        {
            _sampler.Dispose();
            _sampler = null;
        }

        RestoreOriginalSettings();
    }

    /// <summary>容量、手动和专项阶段的统一状态机。</summary>
    private IEnumerator RunStateMachine()
    {
        float startupDuration = Mathf.Max(1f, profile.sampling.instrumentationControlSeconds) * _durationScale;
        yield return RunTimedStage(MainWorldPerformanceStageKind.Startup, _currentStageLabel, startupDuration, 0);

        _sampler.SetInstrumentationEnabled(true);
        if (_interrupted) { FinalizeReport("interrupted"); yield break; }

        switch (_mode)
        {
            case MainWorldPerformanceRunMode.NormalManual:
                yield return RunNormalManualMode();
                break;
            case MainWorldPerformanceRunMode.PickupBurst:
                yield return RunPickupBurstMode("pickup-burst");
                break;
            case MainWorldPerformanceRunMode.FreezeTransition:
                yield return RunFreezeMode();
                break;
            default:
                yield return RunCapacitySweepMode();
                break;
        }

        if (!_completed)
        {
            FinalizeReport(_interrupted ? "interrupted" : "complete");
        }
    }

    /// <summary>执行普通配置手动模式；玩家输入仍由正式 PlayerController 读取。</summary>
    private IEnumerator RunNormalManualMode()
    {
        if (_tierOverride >= 0 && profile.capacityTiers != null && profile.capacityTiers.Length > 0)
        {
            int tierIndex = Mathf.Clamp(_tierOverride, 0, profile.capacityTiers.Length - 1);
            MainWorldPerformanceTier tier = profile.capacityTiers[tierIndex];
            ApplyWaveTarget(tier == null ? profile.normalModeTargetEnemies : tier.targetActiveEnemies);
        }
        else
        {
            RestoreWaveDefaults();
        }
        yield return RunTimedStage(
            MainWorldPerformanceStageKind.Manual,
            "normal-manual",
            GetScaledDuration(profile.sampling.steadySeconds),
            profile.normalModeTargetEnemies);
    }

    /// <summary>执行容量阶梯，并只在选定近限阶梯注入完整武器组合。</summary>
    private IEnumerator RunCapacitySweepMode()
    {
        RestoreWaveDefaults();
        yield return RunTimedStage(
            MainWorldPerformanceStageKind.Warmup,
            "starter-warmup",
            GetScaledDuration(profile.sampling.warmupSeconds),
            profile.normalModeTargetEnemies);

        if (_interrupted) yield break;
        yield return RunTimedStage(
            MainWorldPerformanceStageKind.Steady,
            "starter-baseline-steady",
            GetScaledDuration(profile.sampling.steadySeconds),
            0);

        if (_interrupted) yield break;
        MainWorldPerformanceTier[] tiers = profile.capacityTiers;
        int startTier = _tierOverride >= 0 ? Mathf.Clamp(_tierOverride, 0, tiers.Length - 1) : 0;
        int endTier = _tierOverride >= 0 ? startTier : tiers.Length - 1;
        bool fullLoadoutMeasured = false;
        for (int tierIndex = startTier; tierIndex <= endTier; tierIndex++)
        {
            MainWorldPerformanceTier tier = tiers[tierIndex];
            int target = tier != null ? Mathf.Max(1, tier.targetActiveEnemies) : profile.normalModeTargetEnemies;
            string label = tier != null && !string.IsNullOrWhiteSpace(tier.label)
                ? tier.label
                : "tier-" + target;
            ApplyWaveTarget(target);

            yield return RunTimedStage(
                MainWorldPerformanceStageKind.Ramp,
                label + "-ramp",
                GetScaledDuration(profile.sampling.rampSeconds),
                target);
            if (_interrupted) yield break;

            yield return RunTimedStage(
                MainWorldPerformanceStageKind.Steady,
                label + "-steady",
                GetScaledDuration(profile.sampling.steadySeconds),
                target);
            if (_interrupted) yield break;

            // 先测同一容量目标的 starter 配置，再在独立阶段注入固定完整武器栏，
            // 避免把武器组合的额外弹体和伤害吞吐混入 tier-500 首次容量结论。
            if (tierIndex == profile.GetSafeNearLimitTierIndex())
            {
                EnsureFullLoadout();
                fullLoadoutMeasured = true;
                yield return RunTimedStage(
                    MainWorldPerformanceStageKind.Transition,
                    label + "-full-loadout-setup",
                    GetScaledDuration(profile.sampling.transitionSeconds),
                    target);
                if (_interrupted) yield break;

                yield return RunTimedStage(
                    MainWorldPerformanceStageKind.Ramp,
                    label + "-full-loadout-ramp",
                    GetScaledDuration(profile.sampling.rampSeconds),
                    target);
                if (_interrupted) yield break;

                yield return RunTimedStage(
                    MainWorldPerformanceStageKind.Steady,
                    label + "-full-loadout-steady",
                    GetScaledDuration(profile.sampling.steadySeconds),
                    target);
                if (_interrupted) yield break;
            }
        }

        if (!_interrupted && fullLoadoutMeasured)
        {
            yield return RunInstrumentationComparison(GetNearLimitTarget());
        }

        for (int repeatIndex = 0; repeatIndex < _repeatCount; repeatIndex++)
        {
            if (_interrupted) yield break;
            string repeatLabel = _repeatCount > 1
                ? "reuse-" + (repeatIndex + 1).ToString(CultureInfo.InvariantCulture)
                : "pickup-burst";
            yield return RunPickupBurstMode(repeatLabel);
            if (_interrupted) yield break;
            if (_repeatCount > 1) yield return RunFreezeMode();
        }
    }

    /// <summary>
    /// 在固定近限目标和同一满武器组合下执行 ABBA 采样器开关对照。
    /// A 窗口关闭全部 ProfilerRecorder、弹体/拾取物/可见估算扫描，只保留帧时和权威敌人数；
    /// B 窗口恢复计数器与辅助低频采集。四个窗口使用相同持续时间和目标，阶段报告保留实际负载。
    /// </summary>
    private IEnumerator RunInstrumentationComparison(int target)
    {
        float duration = GetScaledDuration(profile.sampling.instrumentationComparisonSeconds);

        _sampler.SetInstrumentationEnabled(false);
        yield return RunTimedStage(
            MainWorldPerformanceStageKind.InstrumentationControl,
            "ab-control-a",
            duration,
            target);
        if (_interrupted) yield break;

        _sampler.SetInstrumentationEnabled(true);
        yield return RunTimedStage(
            MainWorldPerformanceStageKind.InstrumentationEnabled,
            "ab-enabled-b",
            duration,
            target);
        if (_interrupted) yield break;

        _sampler.SetInstrumentationEnabled(true);
        yield return RunTimedStage(
            MainWorldPerformanceStageKind.InstrumentationEnabled,
            "ab-enabled-b2",
            duration,
            target);
        if (_interrupted) yield break;

        _sampler.SetInstrumentationEnabled(false);
        yield return RunTimedStage(
            MainWorldPerformanceStageKind.InstrumentationControl,
            "ab-control-a2",
            duration,
            target);
        _sampler.SetInstrumentationEnabled(true);
    }

    /// <summary>生成半经验半金币的真实池化拾取物，并通过 PlayerMagnet 完成合法磁吸。</summary>
    private IEnumerator RunPickupBurstMode(string stagePrefix)
    {
        int requested = _pickupCountOverride >= 0 ? _pickupCountOverride : profile.sampling.maximumPickupBurstCount;
        int total = Mathf.Clamp(requested, 2, 1000);
        yield return RunTimedStage(
            MainWorldPerformanceStageKind.Transition,
            stagePrefix + "-setup",
            GetScaledDuration(profile.sampling.transitionSeconds),
            0);
        if (_interrupted) yield break;

        SpawnPickupBurst(total);
        if (_report != null) _report.pickupBurstRequested = total;
        yield return RunTimedStage(
            MainWorldPerformanceStageKind.PickupBurst,
            stagePrefix + "-" + total,
            GetScaledDuration(profile.sampling.pickupSeconds),
            0);
        CleanupBurstPickups();
        if (_pickupModifierApplied && _playerStats != null)
        {
            _playerStats.RemoveModifiers(BurstModifierId);
            _pickupModifierApplied = false;
        }
    }

    /// <summary>触发真实世界冻结，分别记录过渡、冻结持续和解冻阶段。</summary>
    private IEnumerator RunFreezeMode()
    {
        // 每次重复复用循环都必须重新证明本次冻结的 thaw；沿用上一次 true 会把
        // 未捕获的后续事件错误地视为已覆盖。
        _expectedFreezeStage = false;
        _freezeTransitionWindowActive = false;
        _freezeTransitionObserved = false;
        _freezeTransitionMissed = false;
        int target = GetNearLimitTarget();
        ApplyWaveTarget(target);
        EnsureFullLoadout();
        yield return RunTimedStage(
            MainWorldPerformanceStageKind.Ramp,
            "freeze-target-ramp",
            GetScaledDuration(profile.sampling.rampSeconds),
            target);
        if (_interrupted) yield break;

        yield return RunTimedStage(
            MainWorldPerformanceStageKind.Warmup,
            "freeze-pre-event-warmup",
            GetScaledDuration(profile.sampling.warmupSeconds),
            target);
        if (_interrupted) yield break;

        yield return RunTimedStage(
            MainWorldPerformanceStageKind.Steady,
            "freeze-prefreeze-steady",
            GetScaledDuration(profile.sampling.steadySeconds),
            target);
        if (_interrupted) yield break;

        float freezeDuration = GetScaledDuration(profile.sampling.freezeSeconds);
        // TryFreeze 的参数保持正式 15 秒规则。Sustain 提前结束约 2 秒，边界 IO 和
        // 下一帧排除都发生在仍冻结期间，剩余 Transition 窗口可以捕捉真实恢复帧。
        float transitionReserve = Mathf.Min(2f, Mathf.Max(0.1f, freezeDuration * 0.2f));
        float freezeSustainDuration = Mathf.Max(0.1f, freezeDuration - transitionReserve);
        float transitionDuration = transitionReserve + 0.25f;
        if (_freezeController == null || !_freezeController.TryFreeze(freezeDuration))
        {
            InterruptRun("freeze-controller-unavailable");
            yield break;
        }

        _freezeEventStartUnscaledTime = Time.unscaledTime;
        _freezeObservedDuration = 0f;
        _expectedFreezeStage = true;
        yield return RunTimedStage(MainWorldPerformanceStageKind.FreezeSustain, "freeze-sustain", freezeSustainDuration, target);
        if (_interrupted) yield break;
        if (_freezeTransitionMissed || !WorldFreezeController.IsHostileSimulationFrozen)
        {
            _freezeTransitionMissed = true;
            MarkLatestStageInvalid("freeze-ended-before-transition", true);
            InterruptRun("freeze-ended-before-transition");
            yield break;
        }

        _freezeTransitionWindowActive = true;
        yield return RunTimedStage(
            MainWorldPerformanceStageKind.Transition,
            "freeze-expiry-transition",
            transitionDuration,
            target);
        _freezeTransitionWindowActive = false;
        if (!_freezeTransitionObserved)
        {
            _freezeTransitionMissed = true;
            MarkLatestStageInvalid("freeze-transition-not-captured", true);
            InterruptRun("freeze-transition-not-captured");
            yield break;
        }

        while (!_interrupted && WorldFreezeController.IsHostileSimulationFrozen)
        {
            yield return null;
        }
        if (_interrupted) yield break;
        if (WorldFreezeController.IsHostileSimulationFrozen)
        {
            _freezeTransitionMissed = true;
            MarkLatestStageInvalid("freeze-expiry-not-observed", true);
            InterruptRun("freeze-expiry-not-observed");
            yield break;
        }
        _expectedFreezeStage = false;
        if (_report != null)
        {
            _report.freezeTransitionCaptured = true;
            _report.freezeRequestedDurationSeconds = freezeDuration;
            _report.freezeObservedDurationSeconds = _freezeObservedDuration > 0f
                ? _freezeObservedDuration
                : Mathf.Max(0f, Time.unscaledTime - _freezeEventStartUnscaledTime);
        }
        if (_interrupted) yield break;
        yield return RunTimedStage(MainWorldPerformanceStageKind.Thaw, "post-thaw-sustain", GetScaledDuration(profile.sampling.thawSeconds), target);
    }

    /// <summary>运行一个阶段并在结束时冻结统计快照、校验负载和刷新 JSON 文件。</summary>
    private IEnumerator RunTimedStage(MainWorldPerformanceStageKind stage, string label, float duration, int requestedTarget)
    {
        _currentStage = stage;
        _currentStageLabel = label;
        _stageUnexpectedFreeze = !_expectedFreezeStage && WorldFreezeController.IsHostileSimulationFrozen;
        _lowFrequencyTimer = 0f;
        _sampler.BeginStage(stage, requestedTarget, profile.sampling.minimumTargetCoverage, _sampler.InstrumentationEnabled);
        _lastStageStartedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        float start = Time.unscaledTime;
        float safeDuration = Mathf.Max(0.1f, duration);
        while (!_interrupted && Time.unscaledTime - start < safeDuration) yield return null;
        _lastStageObservedDuration = Mathf.Max(0f, Time.unscaledTime - start);
        FlushCurrentStageReport(stage, label, requestedTarget, safeDuration, _lastStageObservedDuration);

        // 文件写入、排序和 JSON 序列化属于 harness 边界开销。跳过下一次 Update 的
        // 帧样本，避免把这段边界工作误报成游戏阶段尖峰；事件生成发生在该 yield 之后，
        // 因此真实拾取生成和冻结调用仍会落入后续事件窗口。
        _excludeNextFrameSample = true;
        yield return null;
    }

    /// <summary>结束当前阶段，区分样本不足、容量溢出、负载不足和污染。</summary>
    private void FlushCurrentStageReport(
        MainWorldPerformanceStageKind stage,
        string label,
        int requestedTarget,
        float expectedDuration,
        float observedDuration)
    {
        int minimumObserved;
        int maximumObserved;
        MainWorldPerformanceFrameStatistics frame = _sampler.EndStage(out minimumObserved, out maximumObserved);
        MainWorldPerformanceStageReport stageReport = new MainWorldPerformanceStageReport
        {
            stage = stage,
            label = label,
            requestedDurationSeconds = expectedDuration,
            observedDurationSeconds = observedDuration,
            stageStartedAtUtc = _lastStageStartedAtUtc,
            instrumentationEnabled = _sampler.InstrumentationEnabled,
            interrupted = _interrupted,
            insufficientSamples = _sampler.FrameCount < Mathf.Max(2, Mathf.CeilToInt(expectedDuration * 5f)) ||
                observedDuration < expectedDuration * 0.95f,
            capacityOverflowed = _sampler.CapacityOverflowed,
            requestedTargetActiveEnemies = Mathf.Max(0, requestedTarget),
            minimumObservedActiveEnemies = minimumObserved,
            maximumObservedActiveEnemies = maximumObserved,
            targetCoverageRatio = _sampler.GetTargetCoverageRatio(),
            frameSampleCount = _sampler.FrameCount,
            droppedFrameSampleCount = _sampler.DroppedFrameCount,
            lowFrequencySampleCount = _sampler.LowFrequencyCount,
            auxiliaryCountsCollected = _sampler.InstrumentationEnabled,
            frame = frame,
            loadoutDescription = BuildLoadoutDescription(),
            gpuCounterAnomalyCount = _sampler.GpuCounterAnomalyCount,
            gpuCounterInvalidReason = _sampler.GpuCounterInvalidReason,
            contaminated = _loadoutContaminated || (!_expectedFreezeStage && _stageUnexpectedFreeze),
            interruptionReason = _interruptReason
        };
        bool requiresStableCapacityCoverage = stage == MainWorldPerformanceStageKind.Steady;
        stageReport.insufficientLoad = requiresStableCapacityCoverage && requestedTarget > 0 &&
            !MainWorldPerformanceReportEvaluator.MeetsTargetSampleCoverage(
                stageReport.targetCoverageRatio,
                profile.sampling.minimumTargetSampleCoverage);
        if (stageReport.insufficientSamples) stageReport.invalidReason = "insufficient-frame-samples";
        else if (stageReport.capacityOverflowed) stageReport.invalidReason = "frame-sample-capacity-overflow";
        else if (stageReport.insufficientLoad) stageReport.invalidReason = "insufficient-active-enemy-load";
        else if (stageReport.contaminated)
        {
            stageReport.invalidReason = _loadoutContaminated
                ? "weapon-loadout-contaminated"
                : "unexpected-freeze-contaminated";
        }

        _report.AddStage(stageReport);
        _report.maximumTrackedBurstPickups = _maximumTrackedBurstPickups;
        _report.maximumObservedNaturalPickups = _maximumObservedNaturalPickups;
        _report.pickupBurstSpawnedCount = _pickupBurstSpawnedCount;
        _report.pickupSourceBreakdownIsEstimate = true;
        WriteReportFile("stage-" + _report.stages.Count.ToString("00", CultureInfo.InvariantCulture));
        WriteRawSamplesFile("stage-" + _report.stages.Count.ToString("00", CultureInfo.InvariantCulture));
    }

    /// <summary>解析玩家、世界模拟、波次、冻结和对象池依赖；不使用反射或每帧查找。</summary>
    private bool ResolveRuntimeDependencies()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            _playerStats = player.GetComponent<PlayerStats>();
            _playerController = player.GetComponent<PlayerController>();
            _playerRigidbody = player.GetComponent<Rigidbody2D>();
            _playerHealth = player.GetComponent<PlayerHealth>();
        }

        _levelUpManager = LevelUpManager.Instance != null ? LevelUpManager.Instance : FindObjectOfType<LevelUpManager>();
        _coordinator = FindObjectOfType<WorldLineCoordinator>();
        _freezeController = WorldFreezeController.Instance;
        if (_coordinator != null)
        {
            _mainSimulation = _coordinator.MainWorldWaveManager != null
                ? _coordinator.MainWorldWaveManager.GetComponent<WorldEnemySimulation>()
                : null;
            _mainWaveManager = _coordinator.MainWorldWaveManager;
        }

        if (_playerHealth != null)
        {
            _playerHealth.Died -= HandlePlayerDied;
            _playerHealth.Died += HandlePlayerDied;
        }

        return _playerStats != null && _playerHealth != null && _levelUpManager != null &&
            _coordinator != null && _mainSimulation != null && _mainWaveManager != null && PoolManager.Instance != null;
    }

    /// <summary>
    /// 应用生成副本的随机事件控制。
    /// Controlled 仅关闭宝箱并把 WorldFreezeMapInstantEffectSO 条目权重置零；Captain 等
    /// 真实地图即时效果仍可掉落，经验、金币、敌人死亡和对象池生命周期不被绕过。
    /// </summary>
    private bool ApplyEventMode()
    {
        if (_mode == MainWorldPerformanceRunMode.NormalManual ||
            _eventMode == MainWorldPerformanceEventMode.Natural)
        {
            _eventMode = MainWorldPerformanceEventMode.Natural;
            return true;
        }

        _generatedDropTable = profile.generatedDropTable;
        if (_generatedDropTable == null)
        {
            return false;
        }

        if (_dropTableOverrideApplied)
        {
            return true;
        }

        _originalChestDropChance = _generatedDropTable.baseChestChance;
        _originalMapInstantEffectDropChance = _generatedDropTable.baseMapInstantEffectChance;
        _originalMapInstantEffectDropWeights.Clear();
        if (_generatedDropTable.mapInstantEffectDrops != null)
        {
            for (int index = 0; index < _generatedDropTable.mapInstantEffectDrops.Count; index++)
            {
                MapInstantEffectDropEntry entry = _generatedDropTable.mapInstantEffectDrops[index];
                _originalMapInstantEffectDropWeights.Add(entry != null ? entry.weight : 0f);
                if (entry == null || entry.prefab == null)
                {
                    continue;
                }

                // Reporter 的 PickupData 是序列化引用，读取 Prefab 本身不会触发池实例 Awake。
                MapInstantEffectPickupReporter reporter =
                    entry.prefab.GetComponent<MapInstantEffectPickupReporter>();
                MapInstantEffectPickupDataSO pickupData = reporter != null ? reporter.PickupData : null;
                if (pickupData != null && pickupData.Effect is WorldFreezeMapInstantEffectSO)
                {
                    entry.weight = 0f;
                }
            }
        }

        _generatedDropTable.baseChestChance = 0f;
        _dropTableOverrideApplied = true;
        return true;
    }

    /// <summary>恢复生成副本掉落表的原始概率和地图即时效果权重，避免污染后续场景测试。</summary>
    private void RestoreDropTableDefaults()
    {
        if (!_dropTableOverrideApplied || _generatedDropTable == null)
        {
            return;
        }

        _generatedDropTable.baseChestChance = _originalChestDropChance;
        _generatedDropTable.baseMapInstantEffectChance = _originalMapInstantEffectDropChance;
        if (_generatedDropTable.mapInstantEffectDrops != null)
        {
            int count = Mathf.Min(
                _generatedDropTable.mapInstantEffectDrops.Count,
                _originalMapInstantEffectDropWeights.Count);
            for (int index = 0; index < count; index++)
            {
                MapInstantEffectDropEntry entry = _generatedDropTable.mapInstantEffectDrops[index];
                if (entry != null)
                {
                    entry.weight = _originalMapInstantEffectDropWeights[index];
                }
            }
        }

        _originalMapInstantEffectDropWeights.Clear();
        _generatedDropTable = null;
        _dropTableOverrideApplied = false;
    }

    /// <summary>自动容量/事件模式锁定主世界并关闭输入移动，保证站桩负载可复现。</summary>
    private void ApplyAutomatedRuntimeOverrides()
    {
        if (_runtimeOverridesApplied)
        {
            return;
        }

        _runtimeOverridesApplied = true;
        if (_mode != MainWorldPerformanceRunMode.NormalManual && _playerController != null)
        {
            _originalPlayerControllerEnabled = _playerController.enabled;
            _playerController.enabled = false;
            _playerControllerOverrideApplied = true;
        }

        if (_mode != MainWorldPerformanceRunMode.NormalManual && _playerRigidbody != null)
        {
            _playerRigidbody.velocity = Vector2.zero;
            _playerRigidbody.angularVelocity = 0f;
        }

        if (_coordinator != null)
        {
            _originalWorldSwitchLocked = _coordinator.IsWorldSwitchLocked;
            _coordinator.SetWorldSwitchLocked(true);
        }
    }

    /// <summary>退出或测试清理时恢复玩家输入、刚体和世界切换锁的原始状态。</summary>
    private void RestoreAutomatedRuntimeOverrides()
    {
        if (!_runtimeOverridesApplied)
        {
            return;
        }

        if (_playerControllerOverrideApplied && _playerController != null)
        {
            _playerController.enabled = _originalPlayerControllerEnabled;
        }

        if (_mode != MainWorldPerformanceRunMode.NormalManual && _playerRigidbody != null)
        {
            _playerRigidbody.velocity = Vector2.zero;
            _playerRigidbody.angularVelocity = 0f;
        }

        if (_coordinator != null)
        {
            _coordinator.SetWorldSwitchLocked(_originalWorldSwitchLocked);
        }

        _runtimeOverridesApplied = false;
        _playerControllerOverrideApplied = false;
    }

    /// <summary>容量模式使用生成的高生命角色；普通模式恢复正式角色并保护升级队列。</summary>
    private void ConfigureCharacterAndExperience()
    {
        if (_playerStats == null || _playerHealth == null) return;
        bool automatedHighHealthMode = _mode != MainWorldPerformanceRunMode.NormalManual;
        CharacterDataSO desiredCharacter = automatedHighHealthMode
            ? profile.capacityCharacter
            : profile.normalCharacter;
        if (desiredCharacter != null) _playerStats.SetCharacterData(desiredCharacter);

        _playerStats.currentLevel = 1;
        _playerStats.currentExp = 0f;
        _playerStats.expToNextLevel = !automatedHighHealthMode
            ? _playerStats.GetExperienceRequiredForLevel(1)
            : profile.capacityExperienceRequirementOverride;
        if (automatedHighHealthMode)
        {
            // 自动容量、拾取和冻结事件都需要在真实敌人链路中存活；普通手动模式
            // 仍使用正式角色和经验曲线。生成角色副本已设置目标生命，这里仅补齐
            // 差值，避免把同一个值重复叠加成 200000。
            float baseMaximumHealth = desiredCharacter != null
                ? desiredCharacter.GetBaseValue(PlayerStatType.MaxHealth)
                : 0f;
            float healthDelta = Mathf.Max(0f, profile.capacityMaximumHealthOverride - baseMaximumHealth);
            _playerStats.SetModifiers(
                HealthModifierId,
                new[]
                {
                    new PlayerStatModifier(
                        PlayerStatType.MaxHealth,
                        PlayerStatModifierMode.Flat,
                        healthDelta)
                });
        }

        _playerHealth.RestoreHealth(_playerHealth.MaxHealth);
    }

    /// <summary>在近限阶梯使用正式 DebugEnsureWeaponLevel 建立固定完整武器组合。</summary>
    private void EnsureFullLoadout()
    {
        if (_levelUpManager == null || profile.fullLoadout == null) return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // 这是一个明确的预定武器栏边界：成功重建后允许后续 full-loadout 阶段
        // 重新成为干净样本；任何本次重建失败仍会在下面保留污染标记。
        _loadoutContaminated = false;
        for (int index = 0; index < profile.fullLoadout.Length; index++)
        {
            WeaponDataSO weaponData = profile.fullLoadout[index];
            if (weaponData == null) continue;
            WeaponBase weapon = _levelUpManager.DebugEnsureWeaponLevel(weaponData, profile.fullLoadoutLevel);
            if (weapon == null)
            {
                _loadoutContaminated = true;
                continue;
            }

        }

        _expectedWeaponCount = _levelUpManager.OwnedWeaponCount;
        if (_expectedWeaponLevels == null || _expectedWeaponLevels.Length != profile.fullLoadout.Length)
        {
            _expectedWeaponLevels = new int[profile.fullLoadout.Length];
        }

        for (int index = 0; index < profile.fullLoadout.Length; index++)
        {
            WeaponDataSO weaponData = profile.fullLoadout[index];
            WeaponBase weapon = weaponData == null ? null : _levelUpManager.GetOwnedWeapon(weaponData);
            _expectedWeaponLevels[index] = weapon == null ? -1 : weapon.CurrentLevel;
        }
#else
        _loadoutContaminated = true;
#endif
    }

    /// <summary>记录阶段开始前正式武器栏的实际数量和已知等级，捕捉自然宝箱改动。</summary>
    private void CaptureCurrentLoadoutSnapshot()
    {
        _expectedWeaponCount = _levelUpManager != null ? _levelUpManager.OwnedWeaponCount : -1;
        if (profile == null || profile.fullLoadout == null)
        {
            _expectedWeaponLevels = null;
            return;
        }

        if (_expectedWeaponLevels == null || _expectedWeaponLevels.Length != profile.fullLoadout.Length)
        {
            _expectedWeaponLevels = new int[profile.fullLoadout.Length];
        }

        for (int index = 0; index < profile.fullLoadout.Length; index++)
        {
            WeaponDataSO weaponData = profile.fullLoadout[index];
            WeaponBase weapon = _levelUpManager == null || weaponData == null
                ? null
                : _levelUpManager.GetOwnedWeapon(weaponData);
            _expectedWeaponLevels[index] = weapon == null ? -1 : weapon.CurrentLevel;
        }
    }

    /// <summary>按目标拆分弱敌与远程敌的并发上限，并提高生成速率以快速到达阶梯。</summary>
    private void ApplyWaveTarget(int target)
    {
        if (profile.generatedWaveConfig == null || profile.generatedWaveConfig.rules == null) return;
        int safeTarget = Mathf.Max(1, target);
        int weakTarget = Mathf.CeilToInt(safeTarget * 0.8f);
        int rangedTarget = Mathf.Max(0, safeTarget - weakTarget);
        float rate = safeTarget / Mathf.Max(1f, profile.sampling.rampSeconds) *
            Mathf.Max(1f, profile.capacityWaveSupplementalRateMultiplier);
        for (int index = 0; index < profile.generatedWaveConfig.rules.Count; index++)
        {
            WaveConfigSO.SpawnRule rule = profile.generatedWaveConfig.rules[index];
            if (rule == null) continue;
            bool firstRule = index == 0;
            rule.startTime = 0f;
            rule.endTime = -1f;
            rule.maxAlive = firstRule ? weakTarget : rangedTarget;
            rule.spawnsPerSecond = Mathf.Max(0.5f, rate * (firstRule ? 0.8f : 0.2f));
        }
    }

    /// <summary>保存生成 WaveConfig 的原始首发规则，场景销毁前恢复内存资产状态。</summary>
    private void CacheWaveDefaults()
    {
        _originalSpawnRates.Clear();
        _originalSpawnCaps.Clear();
        _originalSpawnStarts.Clear();
        _originalSpawnEnds.Clear();
        if (profile == null || profile.generatedWaveConfig == null || profile.generatedWaveConfig.rules == null) return;
        for (int index = 0; index < profile.generatedWaveConfig.rules.Count; index++)
        {
            WaveConfigSO.SpawnRule rule = profile.generatedWaveConfig.rules[index];
            _originalSpawnRates.Add(rule != null ? rule.spawnsPerSecond : 0f);
            _originalSpawnCaps.Add(rule != null ? rule.maxAlive : 0);
            _originalSpawnStarts.Add(rule != null ? rule.startTime : 0f);
            _originalSpawnEnds.Add(rule != null ? rule.endTime : 0f);
        }
    }

    /// <summary>恢复测试副本的原始波次参数，防止 Editor 场景重载污染下一次运行。</summary>
    private void RestoreWaveDefaults()
    {
        if (profile == null || profile.generatedWaveConfig == null || profile.generatedWaveConfig.rules == null) return;
        for (int index = 0; index < profile.generatedWaveConfig.rules.Count && index < _originalSpawnRates.Count; index++)
        {
            WaveConfigSO.SpawnRule rule = profile.generatedWaveConfig.rules[index];
            if (rule == null) continue;
            rule.spawnsPerSecond = _originalSpawnRates[index];
            rule.maxAlive = _originalSpawnCaps[index];
            rule.startTime = _originalSpawnStarts[index];
            rule.endTime = _originalSpawnEnds[index];
        }
    }

    /// <summary>生成固定圆环中的测试拾取物，并通过扩大 Magnet 属性触发真实 PlayerMagnet 入口。</summary>
    private void SpawnPickupBurst(int total)
    {
        if (_playerStats == null || PoolManager.Instance == null) return;
        GameObject expPrefab = profile.expPickupPrefab;
        GameObject coinPrefab = profile.coinPickupPrefab;
        if (expPrefab == null || coinPrefab == null) return;

        _burstPickups.Clear();
        _burstTrackingActive = true;
        float baseRadius = Mathf.Max(3.5f, _playerStats.Magnet + 0.75f);
        int safeTotal = Mathf.Clamp(total, 2, 1000);
        for (int index = 0; index < safeTotal; index++)
        {
            float angle = index * 2.39996323f;
            Vector3 position = _playerStats.transform.position + new Vector3(
                Mathf.Cos(angle) * baseRadius,
                Mathf.Sin(angle) * baseRadius,
                0f);
            GameObject prefab = index < safeTotal / 2 ? expPrefab : coinPrefab;
            GameObject instance = PoolManager.Instance.Spawn(prefab, position, Quaternion.identity);
            if (instance != null) _burstPickups.Add(instance);
        }
        _pickupBurstSpawnedCount = _burstPickups.Count;

        // 先在基础 Magnet 半径外生成，再通过正式属性通知扩大触发器，保持 PlayerMagnet -> IMagneticPickup 链路。
        _playerStats.SetModifiers(
            BurstModifierId,
            new[] { new PlayerStatModifier(PlayerStatType.Magnet, PlayerStatModifierMode.Flat, baseRadius + 1f) });
        _pickupModifierApplied = true;
        Physics2D.SyncTransforms();
    }

    /// <summary>结束 Runner 跟踪；不强制回池，避免磁吸后被自然掉落复用的实例误回收。</summary>
    private void CleanupBurstPickups()
    {
        _burstTrackingActive = false;
        _burstPickups.Clear();
    }

    /// <summary>计算仍被 Runner 跟踪且处于激活状态的池化拾取物。</summary>
    private int CountTrackedActivePickups()
    {
        int count = 0;
        for (int index = 0; index < _burstPickups.Count; index++)
        {
            if (_burstPickups[index] != null && _burstPickups[index].activeInHierarchy) count++;
        }

        return count;
    }

    /// <summary>低频统计场景中所有激活磁吸拾取物，并把 Runner burst 与自然掉落分开记录。</summary>
    private int CountActivePickups()
    {
        int burstCount = _burstTrackingActive ? CountTrackedActivePickups() : 0;
        _sceneRootBuffer.Clear();
        SceneManager.GetActiveScene().GetRootGameObjects(_sceneRootBuffer);
        int totalCount = 0;
        for (int rootIndex = 0; rootIndex < _sceneRootBuffer.Count; rootIndex++)
        {
            GameObject root = _sceneRootBuffer[rootIndex];
            _pickupBuffer.Clear();
            if (root == null) continue;
            // Unity 的 List 重载以当前调用结果覆盖列表；逐根清空并累计，避免只统计
            // 最后一个场景根节点，同时显式过滤被停用的池实例。
            root.GetComponentsInChildren(true, _pickupBuffer);
            for (int pickupIndex = 0; pickupIndex < _pickupBuffer.Count; pickupIndex++)
            {
                MagneticPickupMotion pickup = _pickupBuffer[pickupIndex];
                if (pickup != null && pickup.isActiveAndEnabled && pickup.gameObject.activeInHierarchy) totalCount++;
            }
        }
        int naturalCount = Mathf.Max(0, totalCount - burstCount);
        _maximumTrackedBurstPickups = Mathf.Max(_maximumTrackedBurstPickups, burstCount);
        _maximumObservedNaturalPickups = Mathf.Max(_maximumObservedNaturalPickups, naturalCount);
        return totalCount;
    }

    /// <summary>在相机矩形内用 OverlapAreaNonAlloc 估算可见敌人数；该值永远标注为 estimate。</summary>
    private int CountVisibleEnemyEstimate()
    {
        if (_mainCamera == null || !_mainCamera.isActiveAndEnabled)
        {
            _mainCamera = Camera.main;
        }

        Camera camera = _mainCamera;
        if (camera == null || !camera.orthographic) return 0;
        float halfHeight = Mathf.Max(0.01f, camera.orthographicSize);
        float halfWidth = halfHeight * Mathf.Max(0.01f, camera.aspect);
        Vector2 center = camera.transform.position;
        Vector2 min = center - new Vector2(halfWidth, halfHeight);
        Vector2 max = center + new Vector2(halfWidth, halfHeight);
        if (_enemyLayerMask < 0)
        {
            _enemyLayerMask = LayerMask.GetMask("Enemy");
            if (_enemyLayerMask == 0) _enemyLayerMask = Physics2D.AllLayers;
        }

        int hitCount = Physics2D.OverlapAreaNonAlloc(min, max, _visibleColliderBuffer, _enemyLayerMask);
        int estimate = 0;
        int safeHitCount = Mathf.Min(hitCount, _visibleColliderBuffer.Length);
        for (int index = 0; index < safeHitCount; index++)
        {
            Collider2D collider = _visibleColliderBuffer[index];
            if (collider != null && collider.GetComponentInParent<EnemyBase>() != null) estimate++;
        }

        return estimate;
    }

    /// <summary>检测武器栏和冻结状态是否偏离当前阶段的预期，并把样本标为污染。</summary>
    private void RecordContaminationState()
    {
        if (_expectedWeaponCount >= 0 && _levelUpManager != null &&
            _levelUpManager.OwnedWeaponCount != _expectedWeaponCount)
        {
            _loadoutContaminated = true;
        }

        if (_expectedWeaponLevels != null && _levelUpManager != null && profile.fullLoadout != null)
        {
            int safeCount = Mathf.Min(_expectedWeaponLevels.Length, profile.fullLoadout.Length);
            for (int index = 0; index < safeCount; index++)
            {
                WeaponDataSO weaponData = profile.fullLoadout[index];
                if (weaponData == null) continue;
                WeaponBase weapon = _levelUpManager.GetOwnedWeapon(weaponData);
                int currentLevel = weapon == null ? -1 : weapon.CurrentLevel;
                if (currentLevel != _expectedWeaponLevels[index])
                {
                    _loadoutContaminated = true;
                    break;
                }
            }
        }

        if (!_expectedFreezeStage && WorldFreezeController.IsHostileSimulationFrozen)
        {
            _stageUnexpectedFreeze = true;
        }
    }

    /// <summary>玩家死亡事件只设置中断标志，实际报告写入仍由状态机完成。</summary>
    private void HandlePlayerDied()
    {
        InterruptRun("player-died");
    }

    /// <summary>设置一次性中断原因，避免后续低负载阶段覆盖最初故障。</summary>
    private void InterruptRun(string reason)
    {
        if (_interrupted || _completed) return;
        _interrupted = true;
        _interruptReason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason;
        _currentStage = MainWorldPerformanceStageKind.Interrupted;
    }

    /// <summary>把已写入的最近阶段标为不可用，保留缺失 thaw 等边界故障的明确原因。</summary>
    private void MarkLatestStageInvalid(string reason, bool interrupted)
    {
        if (_report == null || _report.stages == null || _report.stages.Count == 0)
        {
            return;
        }

        MainWorldPerformanceStageReport stage = _report.stages[_report.stages.Count - 1];
        stage.invalidReason = reason;
        stage.contaminated = true;
        if (interrupted)
        {
            stage.interrupted = true;
            stage.interruptionReason = reason;
        }
    }

    /// <summary>收集设备、图形后端、帧设置和构建来源，供人工判断硬件与证据边界。</summary>
    private MainWorldPerformanceReport CreateReport(int seed)
    {
        return new MainWorldPerformanceReport
        {
            reportId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture),
            profileId = profile.profileId,
            mode = _mode,
            eventMode = _eventMode,
            generatedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            unityVersion = Application.unityVersion,
            applicationVersion = Application.version,
            platform = Application.platform.ToString(),
            deviceModel = SystemInfo.deviceModel,
            processorType = SystemInfo.processorType,
            processorCount = SystemInfo.processorCount,
            systemMemorySizeMb = SystemInfo.systemMemorySize,
            graphicsMemorySizeMb = SystemInfo.graphicsMemorySize,
            graphicsDeviceName = SystemInfo.graphicsDeviceName,
            graphicsDeviceType = SystemInfo.graphicsDeviceType.ToString(),
            graphicsDeviceVendor = SystemInfo.graphicsDeviceVendor,
            screenWidth = Screen.width,
            screenHeight = Screen.height,
            qualityLevel = QualitySettings.GetQualityLevel(),
            qualityName = QualitySettings.names != null && QualitySettings.GetQualityLevel() < QualitySettings.names.Length
                ? QualitySettings.names[QualitySettings.GetQualityLevel()]
                : string.Empty,
            vSyncCount = QualitySettings.vSyncCount,
            targetFrameRate = Application.targetFrameRate,
            fixedDeltaTime = Time.fixedDeltaTime,
            developmentBuild = Debug.isDebugBuild,
#if MAINWORLD_PERFORMANCE_BUILD
            dedicatedPerformanceBuild = true,
#else
            dedicatedPerformanceBuild = false,
#endif
            sourceRevision = profile.sourceRevision,
            sourceHash = profile.sourceHash,
            fixedRandomSeed = seed,
            fixedLoadoutDescription = BuildLoadoutDescription(),
            capacityMaximumHealthOverride = profile.capacityMaximumHealthOverride,
            capacityExperienceRequirementOverride = profile.capacityExperienceRequirementOverride,
            capacityWaveSupplementalRateMultiplier = profile.capacityWaveSupplementalRateMultiplier,
            controlledEventsApplied = _eventMode == MainWorldPerformanceEventMode.Controlled,
            naturalEventsRetained = _eventMode == MainWorldPerformanceEventMode.Natural,
            pickupBurstRequested = 0,
            pickupBurstSpawnedCount = 0,
            maximumTrackedBurstPickups = 0,
            maximumObservedNaturalPickups = 0,
            pickupSourceBreakdownIsEstimate = true,
            workloadDescription = "GrassWorldLine MainWorld only; real WorldWaveManager -> WorldEnemySimulation -> PoolManager chain; SubWorldRuntime inactive.",
            settingsDescription = "fixedSeed=" + seed.ToString(CultureInfo.InvariantCulture) + "; eventMode=" + _eventMode + "; activeCounts=authoritative; visibleCounts=camera estimate; pickupCounts=all MagneticPickupMotion plus burst peak; pickupSourceBreakdown=estimate-after-pool-reuse; profilerControl=unavailable-unmatched-windows; GPU support flag required; waveSupplementalRateMultiplier=" + profile.capacityWaveSupplementalRateMultiplier.ToString(CultureInfo.InvariantCulture),
            instrumentationAbCaptured = false,
            instrumentationAbLoadMatched = false,
            instrumentationAbDescription = "unavailable-unmatched-windows",
            instrumentationAbControlAverageMilliseconds = MainWorldPerformanceCounterSupport.UnavailableValue,
            instrumentationAbEnabledAverageMilliseconds = MainWorldPerformanceCounterSupport.UnavailableValue,
            instrumentationAbOverheadMilliseconds = MainWorldPerformanceCounterSupport.UnavailableValue,
            instrumentationAbControlP95Milliseconds = MainWorldPerformanceCounterSupport.UnavailableValue,
            instrumentationAbEnabledP95Milliseconds = MainWorldPerformanceCounterSupport.UnavailableValue,
            accountIsolationApplied = _accountIsolationApplied,
            automaticBossDisabledForHorizon = profile.delayedBossEncounter != null &&
                profile.delayedBossEncounter.GetSafeTriggerTime() > profile.sampling.warmupSeconds + profile.sampling.steadySeconds,
            ordinaryBuildAutoActivationBlocked = true,
            freezeTransitionCaptured = false,
            freezeTransitionMissed = false,
            activeCountsAreAuthoritative = true,
            visibleCountsAreEstimate = true,
            stableEvidence = true
        };
    }

    /// <summary>把实际固定武器数据和目标等级写入报告，便于复现实验而不依赖对象名称猜测。</summary>
    private string BuildLoadoutDescription()
    {
        if (profile == null || profile.fullLoadout == null || profile.fullLoadout.Length == 0)
        {
            return "starter-loadout-only";
        }

        List<string> entries = new List<string>(profile.fullLoadout.Length);
        for (int index = 0; index < profile.fullLoadout.Length; index++)
        {
            WeaponDataSO weapon = profile.fullLoadout[index];
            if (weapon == null) continue;
            int level = _expectedWeaponLevels != null && index < _expectedWeaponLevels.Length
                ? _expectedWeaponLevels[index]
                : -1;
            if (level >= 0)
            {
                entries.Add(weapon.name + "@" + level.ToString(CultureInfo.InvariantCulture));
            }
        }

        string knownLoadout = entries.Count == 0 ? "none" : string.Join(",", entries.ToArray());
        return "ownedCount=" + Mathf.Max(0, _expectedWeaponCount).ToString(CultureInfo.InvariantCulture) + ";known=" + knownLoadout;
    }

    /// <summary>在阶段边界和最终结束时写入 JSON；连续帧不做文件操作。</summary>
    private void WriteReportFile(string suffix)
    {
        if (_report == null || string.IsNullOrWhiteSpace(_outputPath)) return;
        try
        {
            Directory.CreateDirectory(_outputPath);
            string fileName = _report.reportId + "-" + suffix + ".json";
            File.WriteAllText(Path.Combine(_outputPath, fileName), JsonUtility.ToJson(_report, true));
        }
        catch (Exception exception)
        {
            _reportWriteFailed = true;
            Debug.LogError("[MainWorldPerformance] 报告写入失败：" + exception.Message, this);
        }
    }

    /// <summary>阶段边界写入可复现的原始逐帧对象数量与时间样本。</summary>
    private void WriteRawSamplesFile(string suffix)
    {
        if (_report == null || _sampler == null || string.IsNullOrWhiteSpace(_outputPath)) return;
        try
        {
            File.WriteAllText(
                Path.Combine(_outputPath, _report.reportId + "-" + suffix + "-raw.json"),
                _sampler.CreateRawSamplesJson());
        }
        catch (Exception exception)
        {
            _reportWriteFailed = true;
            Debug.LogError("[MainWorldPerformance] 原始样本写入失败：" + exception.Message, this);
        }
    }

    /// <summary>按阶段标签读取 ABBA 对照窗口，避免把启动控制窗口与战斗窗口相减。</summary>
    private MainWorldPerformanceStageReport FindStageReport(string label)
    {
        if (_report == null || _report.stages == null || string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        for (int index = 0; index < _report.stages.Count; index++)
        {
            MainWorldPerformanceStageReport stage = _report.stages[index];
            if (stage != null && string.Equals(stage.label, label, StringComparison.Ordinal))
            {
                return stage;
            }
        }

        return null;
    }

    /// <summary>
    /// 汇总同一 500 满武器负载下的 ABBA 采样器增量，并保留负差值作为测量结果。
    /// A 只采集帧时与权威敌人数；B 额外开启 ProfilerRecorder、弹体/拾取物/可见估算扫描。
    /// </summary>
    private void CalculateInstrumentationComparison()
    {
        MainWorldPerformanceStageReport controlA = FindStageReport("ab-control-a");
        MainWorldPerformanceStageReport enabledB = FindStageReport("ab-enabled-b");
        MainWorldPerformanceStageReport enabledB2 = FindStageReport("ab-enabled-b2");
        MainWorldPerformanceStageReport controlA2 = FindStageReport("ab-control-a2");
        bool captured = controlA != null && enabledB != null && enabledB2 != null && controlA2 != null &&
            IsCompleteInstrumentationWindow(controlA) && IsCompleteInstrumentationWindow(enabledB) &&
            IsCompleteInstrumentationWindow(enabledB2) && IsCompleteInstrumentationWindow(controlA2);
        if (!captured)
        {
            _report.instrumentationAbCaptured = false;
            _report.instrumentationAbLoadMatched = false;
            _report.instrumentationAbDescription = "incomplete-abba-windows";
            return;
        }

        _report.instrumentationAbCaptured = true;
        _report.instrumentationAbControlAverageMilliseconds =
            (controlA.frame.averageMilliseconds + controlA2.frame.averageMilliseconds) * 0.5f;
        _report.instrumentationAbEnabledAverageMilliseconds =
            (enabledB.frame.averageMilliseconds + enabledB2.frame.averageMilliseconds) * 0.5f;
        _report.instrumentationAbOverheadMilliseconds =
            _report.instrumentationAbEnabledAverageMilliseconds - _report.instrumentationAbControlAverageMilliseconds;
        _report.instrumentationAbControlP95Milliseconds =
            (controlA.frame.p95Milliseconds + controlA2.frame.p95Milliseconds) * 0.5f;
        _report.instrumentationAbEnabledP95Milliseconds =
            (enabledB.frame.p95Milliseconds + enabledB2.frame.p95Milliseconds) * 0.5f;
        _report.instrumentationAbControlMinimumActiveEnemies = Mathf.Min(
            controlA.minimumObservedActiveEnemies,
            controlA2.minimumObservedActiveEnemies);
        _report.instrumentationAbControlMaximumActiveEnemies = Mathf.Max(
            controlA.maximumObservedActiveEnemies,
            controlA2.maximumObservedActiveEnemies);
        _report.instrumentationAbEnabledMinimumActiveEnemies = Mathf.Min(
            enabledB.minimumObservedActiveEnemies,
            enabledB2.minimumObservedActiveEnemies);
        _report.instrumentationAbEnabledMaximumActiveEnemies = Mathf.Max(
            enabledB.maximumObservedActiveEnemies,
            enabledB2.maximumObservedActiveEnemies);
        _report.instrumentationAbLoadMatched =
            MatchesInstrumentationLoad(controlA, enabledB) &&
            MatchesInstrumentationLoad(enabledB, enabledB2) &&
            MatchesInstrumentationLoad(enabledB2, controlA2);
        _report.instrumentationAbDescription = _report.instrumentationAbLoadMatched
            ? "ABBA matched target=" + controlA.requestedTargetActiveEnemies.ToString(CultureInfo.InvariantCulture) +
              "; A=frame+authoritative-enemy-only; B=ProfilerRecorder+projectile/pickup/visible scans; " +
              "negative overhead retained as measurement noise"
            : "ABBA captured but unmatched due to contamination, loadout, coverage, target, or duration mismatch; " +
              "raw window values retained for diagnosis";
    }

    /// <summary>判断一个 AB 窗口是否具备足够帧数、时长且没有数组溢出或中断。</summary>
    private static bool IsCompleteInstrumentationWindow(MainWorldPerformanceStageReport stage)
    {
        return stage != null && !stage.interrupted && !stage.insufficientSamples &&
            !stage.capacityOverflowed && stage.frameSampleCount > 0;
    }

    /// <summary>以目标和低频覆盖率判断两个 AB 窗口是否处于可比负载。</summary>
    private bool MatchesInstrumentationLoad(
        MainWorldPerformanceStageReport first,
        MainWorldPerformanceStageReport second)
    {
        return MainWorldPerformanceReportEvaluator.IsComparableInstrumentationWindow(
            first,
            second,
            profile.sampling.minimumTargetSampleCoverage,
            profile.sampling.instrumentationComparisonSeconds * 0.1f);
    }

    /// <summary>写入最终结果并停止运行器。</summary>
    private void FinalizeReport(string outcome)
    {
        if (_completed) return;
        _completed = true;
        _running = false;
        string resolvedOutcome = string.IsNullOrWhiteSpace(outcome) ? "incomplete" : outcome;
        if (_reportWriteFailed && string.Equals(resolvedOutcome, "complete", StringComparison.OrdinalIgnoreCase))
        {
            resolvedOutcome = "incomplete";
            _interruptReason = "report-write-failed";
        }

        _report.outcome = resolvedOutcome;
        _report.executionComplete = string.Equals(_report.outcome, "complete", StringComparison.OrdinalIgnoreCase);
        _report.interruptionReason = _interruptReason;
        _report.freezeTransitionCaptured = _freezeTransitionObserved;
        _report.freezeTransitionMissed = _freezeTransitionMissed;
        // 当前控制窗只覆盖启动空负载，而启用窗覆盖战斗阶段，二者没有匹配工作负载；
        // 因此不把差值冒充采样器 A/B 开销，统一以 unavailable 表达未测。
        _report.instrumentationControlCaptured = false;
        _report.instrumentationControlAverageMilliseconds = MainWorldPerformanceCounterSupport.UnavailableValue;
        _report.instrumentationEnabledAverageMilliseconds = MainWorldPerformanceCounterSupport.UnavailableValue;
        _report.instrumentationOverheadMilliseconds = MainWorldPerformanceCounterSupport.UnavailableValue;
        CalculateInstrumentationComparison();

        bool hasCapacitySteadyStage = false;
        bool capacityStagesValid = true;
        bool allStagesValid = _report.stages.Count > 0;
        for (int index = 0; index < _report.stages.Count; index++)
        {
            MainWorldPerformanceStageReport stage = _report.stages[index];
            bool valid = MainWorldPerformanceReportEvaluator.IsStableCapacityEvidence(stage);
            allStagesValid &= valid;
            if (stage.stage == MainWorldPerformanceStageKind.Steady && stage.requestedTargetActiveEnemies > 0)
            {
                hasCapacitySteadyStage = true;
                capacityStagesValid &= valid;
            }
        }

        _report.capacityEvidenceValid = hasCapacitySteadyStage && capacityStagesValid;
        _report.stableEvidence = _report.executionComplete && allStagesValid;
        WriteReportFile("final");
#if MAINWORLD_PERFORMANCE_BUILD
        // 普通 manual 模式必须保持交互，玩家可以继续移动观察正常角色与升级流程；
        // 自动容量/事件模式才在写完可复核报告后退出，供 launcher 得到确定退出码。
        if (_mode != MainWorldPerformanceRunMode.NormalManual)
        {
            Application.Quit(_report.executionComplete ? 0 : 3);
        }
#endif
    }

    /// <summary>在编辑器或专用开发构建中设置内存账号后端，避免性能运行写入真实存档。</summary>
    private void InstallEditorAccountIsolation()
    {
#if UNITY_EDITOR
        AccountProgressService.SetStorageForTests(new InMemoryAccountProgressStorage());
        _accountIsolationApplied = true;
#elif DEVELOPMENT_BUILD
        _accountIsolationApplied = true;
#endif
    }

    /// <summary>记录无法启动的专用构建结果并返回非成功玩家退出码，避免静默空跑。</summary>
    private void FailStartup(string reason)
    {
        _outputPath = ResolveOutputPath();
        _report = new MainWorldPerformanceReport
        {
            reportId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture),
            profileId = profile != null ? profile.profileId : "missing-profile",
            outcome = "incomplete",
            generatedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            unityVersion = Application.unityVersion,
            platform = Application.platform.ToString(),
            graphicsDeviceName = SystemInfo.graphicsDeviceName,
            graphicsDeviceType = SystemInfo.graphicsDeviceType.ToString(),
            graphicsDeviceVendor = SystemInfo.graphicsDeviceVendor,
            sourceRevision = profile != null ? profile.sourceRevision : string.Empty,
            sourceHash = profile != null ? profile.sourceHash : string.Empty,
            interruptionReason = reason,
            ordinaryBuildAutoActivationBlocked = true,
            executionComplete = false,
            stableEvidence = false,
            capacityEvidenceValid = false
        };
        WriteReportFile("final");
#if MAINWORLD_PERFORMANCE_BUILD
        Application.Quit(2);
#endif
    }

    /// <summary>只允许生成测试场景或编辑器预览启动性能运行器。</summary>
    private bool ShouldRunHarness()
    {
        if (profile == null || !profile.enableHarness)
        {
            return false;
        }

#if MAINWORLD_PERFORMANCE_BUILD
        return true;
#else
        // Editor PlayMode tests also run with -batchmode; the component's explicit generated
        // scene/profile gate is sufficient, while ordinary MainLevel has no Runner component.
        return Application.isEditor;
#endif
    }

    /// <summary>保存当前帧设置，保证运行器退出后不会改变用户的游戏设置。</summary>
    private void CaptureOriginalSettings()
    {
        if (_originalSettingsCaptured) return;
        _originalSettingsCaptured = true;
        _originalVSyncCount = QualitySettings.vSyncCount;
        _originalTargetFrameRate = Application.targetFrameRate;
        _originalRunInBackground = Application.runInBackground;
    }

    /// <summary>容量吞吐模式关闭 VSync 和帧率上限，手动模式保持 Profile 的 60 FPS 目标。</summary>
    private void ApplyRuntimeFrameSettings()
    {
        bool throughput = profile.throughputSweep && _mode != MainWorldPerformanceRunMode.NormalManual;
        QualitySettings.vSyncCount = throughput ? 0 : 0;
        Application.targetFrameRate = throughput ? -1 : Mathf.Max(1, profile.normalModeTargetFrameRate);
        Application.runInBackground = true;
    }

    /// <summary>恢复运行器修改过的帧设置。</summary>
    private void RestoreOriginalSettings()
    {
        if (!_originalSettingsCaptured) return;
        QualitySettings.vSyncCount = _originalVSyncCount;
        Application.targetFrameRate = _originalTargetFrameRate;
        Application.runInBackground = _originalRunInBackground;
        _originalSettingsCaptured = false;
    }

    /// <summary>读取命令行覆盖项；参数可使用 --key=value 或 --key value 两种形式。</summary>
    private void ParseCommandLineArguments()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        for (int index = 0; index < arguments.Length; index++)
        {
            string argument = arguments[index];
            if (string.IsNullOrWhiteSpace(argument)) continue;
            if (TryReadArgument(arguments, ref index, ModeArgument, out string mode))
            {
                _modeOverride = mode;
            }
            else if (TryReadArgument(arguments, ref index, EventModeArgument, out string eventMode))
            {
                _eventModeOverride = eventMode;
            }
            else if (TryReadArgument(arguments, ref index, SeedArgument, out string seedText) &&
                int.TryParse(seedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed))
            {
                _seedOverride = seed;
                _hasSeedOverride = true;
            }
            else if (TryReadArgument(arguments, ref index, DurationArgument, out string durationText) &&
                float.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture, out float duration))
            {
                _durationScale = Mathf.Clamp(duration, 0.01f, 100f);
            }
            else if (TryReadArgument(arguments, ref index, TierArgument, out string tierText) &&
                int.TryParse(tierText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int tier))
            {
                _tierOverride = Mathf.Max(0, tier);
            }
            else if (TryReadArgument(arguments, ref index, PickupArgument, out string pickupText) &&
                int.TryParse(pickupText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pickupCount))
            {
                _pickupCountOverride = Mathf.Clamp(pickupCount, 2, 1000);
            }
            else if (TryReadArgument(arguments, ref index, RepeatsArgument, out string repeatsText) &&
                int.TryParse(repeatsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int repeats))
            {
                _repeatCount = Mathf.Clamp(repeats, 1, 12);
            }
            else if (TryReadArgument(arguments, ref index, OutputArgument, out string output))
            {
                _outputPath = output;
            }
        }
    }

    /// <summary>读取一个命令行键值，并安全跳过它的独立值参数。</summary>
    private static bool TryReadArgument(string[] arguments, ref int index, string name, out string value)
    {
        value = null;
        string argument = arguments[index];
        string prefix = name + "=";
        if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = argument.Substring(prefix.Length);
            return true;
        }

        if (!string.Equals(argument, name, StringComparison.OrdinalIgnoreCase) || index + 1 >= arguments.Length)
        {
            return false;
        }

        index++;
        value = arguments[index];
        return true;
    }

    /// <summary>把命令行或 Profile 的模式文本解析成固定枚举。</summary>
    private MainWorldPerformanceRunMode ResolveMode()
    {
        if (string.IsNullOrWhiteSpace(_modeOverride)) return profile.defaultMode;
        switch (_modeOverride.Trim().ToLowerInvariant())
        {
            case "normal":
            case "manual":
                return MainWorldPerformanceRunMode.NormalManual;
            case "pickup":
            case "pickups":
                return MainWorldPerformanceRunMode.PickupBurst;
            case "freeze":
            case "transition":
                return MainWorldPerformanceRunMode.FreezeTransition;
            case "capacity":
            case "sweep":
                return MainWorldPerformanceRunMode.CapacitySweep;
            default:
                Debug.LogWarning("[MainWorldPerformance] 未知 --perf-mode，使用 Profile 默认模式：" + _modeOverride, this);
                return profile.defaultMode;
        }
    }

    /// <summary>把命令行或 Profile 的事件文本解析成 Natural/Controlled 枚举。</summary>
    private MainWorldPerformanceEventMode ResolveEventMode()
    {
        if (string.IsNullOrWhiteSpace(_eventModeOverride))
        {
            return profile.defaultEventMode;
        }

        switch (_eventModeOverride.Trim().ToLowerInvariant())
        {
            case "natural":
            case "real":
                return MainWorldPerformanceEventMode.Natural;
            case "controlled":
            case "control":
                return MainWorldPerformanceEventMode.Controlled;
            default:
                Debug.LogWarning(
                    "[MainWorldPerformance] 未知 --perf-events，使用 Profile 默认事件模式：" +
                    _eventModeOverride,
                    this);
                return profile.defaultEventMode;
        }
    }

    /// <summary>解析输出目录；空参数时写入项目可持久化目录的 Performance 子目录。</summary>
    private string ResolveOutputPath()
    {
        if (!string.IsNullOrWhiteSpace(_outputPath))
        {
            return Path.GetFullPath(_outputPath);
        }

        return Path.Combine(Application.persistentDataPath, "Logs", "Performance");
    }

    /// <summary>应用 CLI 时间倍率，同时保持所有阶段至少运行一个可观察帧。</summary>
    private float GetScaledDuration(float seconds)
    {
        return Mathf.Max(0.1f, seconds * Mathf.Clamp(_durationScale, 0.01f, 100f));
    }

    /// <summary>返回配置的近限容量目标，并在数组为空时安全回退到普通目标。</summary>
    private int GetNearLimitTarget()
    {
        if (profile.capacityTiers == null || profile.capacityTiers.Length == 0)
        {
            return Mathf.Max(1, profile.normalModeTargetEnemies);
        }

        MainWorldPerformanceTier tier = profile.capacityTiers[profile.GetSafeNearLimitTierIndex()];
        return tier == null ? Mathf.Max(1, profile.normalModeTargetEnemies) : Mathf.Max(1, tier.targetActiveEnemies);
    }
}
