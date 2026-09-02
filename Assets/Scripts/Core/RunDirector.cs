using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 单局流程权威：计时、首领遭遇、世界切换锁和最终结果冻结均由此组件决定。
/// 结果页只消费本组件生成的 RunResultSnapshot，不自行累加或读取战斗运行时状态。
/// </summary>
[DisallowMultipleComponent]
public sealed class RunDirector : MonoBehaviour
{
    public static RunDirector Instance { get; private set; }

    [Header("遭遇与世界")]
    [SerializeField] private BossEncounterDataSO bossEncounter;
    [SerializeField] private WorldLineCoordinator worldLineCoordinator;

    [Header("玩家与局内系统")]
    [SerializeField] private PlayerStats playerStats;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private RunState runState;
    [SerializeField] private LevelUpManager levelUpManager;

    [Header("结果页地图回退")]
    [SerializeField] private string mapNameKey = "map.double_world_trial";
    [SerializeField] private string mapDisplayName = "双世界试炼";

    private Transform _playerTransform;
    private RunTelemetry _telemetry;
    private BossEnemyController _boss;
    private RunResultSnapshot _finalSnapshot;
    private RunResultSnapshot _deathPreviewSnapshot;
    private bool _bossSpawned;
    private bool _resultFrozen;
    private bool _initialWeaponScanDone;
    private PlayerHealth _boundPlayerHealth;
    private LevelUpManager _boundLevelUpManager;
    private BossEnemyController _pendingBossDefeat;
    private float _elapsedSeconds;

    /// <summary>权威单局经过的缩放时间秒数；手动暂停和升级暂停期间不增长。</summary>
    public float ElapsedSeconds => _elapsedSeconds;

    /// <summary>本局权威统计容器。</summary>
    public RunTelemetry Telemetry => _telemetry;

    /// <summary>首领是否已经在当前活动世界生成。</summary>
    public bool IsBossSpawned => _bossSpawned;

    /// <summary>最终结果是否已冻结；冻结后任何迟到回调不能改变本局结果。</summary>
    public bool IsResultFrozen => _resultFrozen;

    /// <summary>冻结后的最终结果；复活预览不会填入此属性。</summary>
    public RunResultSnapshot FinalSnapshot => _finalSnapshot;

    /// <summary>有可用复活时的临时死亡预览；复活后立即丢弃。</summary>
    public RunResultSnapshot DeathPreviewSnapshot => _deathPreviewSnapshot;

    /// <summary>最终快照建立后触发一次，供 UI 和测试观察结果冻结。</summary>
    public event Action<RunResultSnapshot> ResultFrozen;

    /// <summary>死亡预览建立时触发；预览不是最终提交。</summary>
    public event Action<RunResultSnapshot> DeathPreviewChanged;

    /// <summary>复活成功后通知表现层丢弃死亡预览。</summary>
    public event Action DeathPreviewDiscarded;

    /// <summary>建立单例、统计容器并解析不依赖 Start 顺序的场景引用。</summary>
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        _telemetry = new RunTelemetry();
        RunTelemetry.Activate(_telemetry);
        ResolveDependencies();
    }

    /// <summary>订阅玩家死亡/复活与武器生命周期事件。</summary>
    private void OnEnable()
    {
        ResolveDependencies();
        BindRuntimeEvents();
    }

    /// <summary>场景对象完成 Start 后补齐预置武器的 0 秒获得时间。</summary>
    private void Start()
    {
        ResolveDependencies();
        BindRuntimeEvents();
        SyncInitialWeapons();
    }

    /// <summary>解除事件、清理当前局全局遥测引用和单例。</summary>
    private void OnDestroy()
    {
        UnbindRuntimeEvents();
        if (ReferenceEquals(RunTelemetry.Active, _telemetry))
        {
            RunTelemetry.Activate(null);
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>推进权威计时并在达到遭遇时间后生成当前活动世界的首领。</summary>
    private void Update()
    {
        ResolveDependencies();
        BindRuntimeEvents();

        if (_resultFrozen || Time.timeScale <= 0f)
        {
            return;
        }

        _elapsedSeconds = RunResultValueSanitizer.SaturatingAdd(
            _elapsedSeconds,
            RunResultValueSanitizer.SanitizeNonNegative(Time.deltaTime));
        if (!_bossSpawned && bossEncounter != null &&
            _elapsedSeconds >= bossEncounter.GetSafeTriggerTime())
        {
            TryStartBossEncounter();
        }
    }

    /// <summary>
    /// 只允许在当前活动世界生成一次首领，并在成功后锁定世界切换。
    /// </summary>
    /// <returns>成功从对象池生成首领时返回 true。</returns>
    public bool TryStartBossEncounter()
    {
        if (_resultFrozen || _bossSpawned || bossEncounter == null || !bossEncounter.IsValid)
        {
            return false;
        }

        ResolveDependencies();
        WorldEnemySimulation simulation = worldLineCoordinator != null
            ? worldLineCoordinator.ActiveWorldSimulation
            : null;
        if (simulation == null || _playerTransform == null)
        {
            return false;
        }

        Vector3 spawnPosition = _playerTransform.position + Vector3.right * Mathf.Max(0.5f, bossEncounter.spawnDistance);
        GameObject bossObject = simulation.SpawnBoss(
            bossEncounter.bossPrefab,
            spawnPosition,
            bossEncounter.bossData,
            this);
        if (bossObject == null || !bossObject.TryGetComponent(out BossEnemyController boss))
        {
            return false;
        }

        _boss = boss;
        _bossSpawned = true;
        if (worldLineCoordinator != null)
        {
            worldLineCoordinator.SetWorldSwitchLocked(true);
        }

        return true;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>编辑器/开发包测试入口；不改变生产计时触发规则。</summary>
    public bool DebugTriggerBossEncounter()
    {
        return TryStartBossEncounter();
    }
#endif

    /// <summary>BossEnemyController 在唯一死亡出口调用此方法请求胜利结算。</summary>
    public void NotifyBossDefeated(BossEnemyController defeatedBoss)
    {
        if (_resultFrozen || defeatedBoss == null || defeatedBoss != _boss)
        {
            return;
        }

        if (CombatDamageResolver.IsSettlingDamage)
        {
            _pendingBossDefeat = defeatedBoss;
            return;
        }

        FreezeRun(RunOutcome.Victory);
    }

    /// <summary>
    /// 在 CombatDamageResolver 完成有效命中伤害记账后提交延迟的 Boss 胜利。
    /// 该入口只消费当前首领的待结算请求，避免重复冻结或重复提交账号进度。
    /// </summary>
    public void FlushPendingBossDefeat()
    {
        if (_resultFrozen || CombatDamageResolver.IsSettlingDamage || _pendingBossDefeat == null)
        {
            return;
        }

        BossEnemyController pendingBoss = _pendingBossDefeat;
        _pendingBossDefeat = null;
        NotifyBossDefeated(pendingBoss);
    }

    /// <summary>
    /// 在即时效果已经成功应用后登记未来地图拾取物；结果冻结后拒绝迟到报告。
    /// </summary>
    public bool ReportInstantEffectPickup(MapInstantEffectPickupDataSO pickupData)
    {
        return !_resultFrozen && _telemetry != null && _telemetry.ReportInstantEffectPickup(pickupData);
    }

    /// <summary>
    /// 玩家死亡时建立可复活预览；没有剩余复活次数则直接冻结 Defeat。
    /// </summary>
    private void HandlePlayerDied()
    {
        if (_resultFrozen)
        {
            return;
        }

        ResolveDependencies();
        if (runState != null && runState.RemainingRevivals > 0)
        {
            _deathPreviewSnapshot = BuildSnapshot(RunOutcome.Defeat, true);
            DeathPreviewChanged?.Invoke(_deathPreviewSnapshot);
            return;
        }

        FreezeRun(RunOutcome.Defeat);
    }

    /// <summary>复活事件发生后丢弃临时预览，并继续使用同一局统计容器。</summary>
    private void HandlePlayerRevived(float currentHealth, float maxHealth)
    {
        if (_deathPreviewSnapshot == null)
        {
            return;
        }

        _deathPreviewSnapshot = null;
        DeathPreviewDiscarded?.Invoke();
    }

    /// <summary>
    /// 重开、回主菜单或退出等非胜利出口调用的最终 Defeat 冻结守卫。
    /// 重复入口不会重复构造快照或重新提交。
    /// </summary>
    public void EndRunAsDefeat()
    {
        if (!_resultFrozen)
        {
            FreezeRun(RunOutcome.Defeat);
        }
    }

    /// <summary>按结果方向建立一次不可变最终快照，并通知流程管理器暂停和提交。</summary>
    private void FreezeRun(RunOutcome outcome)
    {
        if (_resultFrozen)
        {
            return;
        }

        _pendingBossDefeat = null;
        _resultFrozen = true;
        if (_telemetry != null)
        {
            _telemetry.Freeze();
        }

        if (worldLineCoordinator != null)
        {
            worldLineCoordinator.SetWorldSwitchLocked(true);
        }

        _finalSnapshot = BuildSnapshot(outcome, false);
        _deathPreviewSnapshot = null;
        ResultFrozen?.Invoke(_finalSnapshot);

        GameFlowManager flowManager = GameFlowManager.Instance;
        if (flowManager != null)
        {
            flowManager.EnterRunResult(_finalSnapshot);
        }
        else
        {
            Time.timeScale = 0f;
        }
    }

    /// <summary>生成结果前解析玩家、局内管理器和当前持有武器。</summary>
    private RunResultSnapshot BuildSnapshot(RunOutcome outcome, bool isPreview)
    {
        ResolveDependencies();
        List<RunResultWeaponSnapshot> weaponSnapshots = _telemetry != null
            ? _telemetry.CreateWeaponSnapshots(
                _elapsedSeconds,
                levelUpManager != null ? levelUpManager.OwnedWeapons : null)
            : new List<RunResultWeaponSnapshot>();
        List<RunResultAbilitySnapshot> itemSnapshots = new List<RunResultAbilitySnapshot>(PlayerLoadoutRules.MaxAbilityCount);
        List<RunResultAbilitySnapshot> abilitySnapshots = new List<RunResultAbilitySnapshot>(PlayerLoadoutRules.MaxAbilityCount);

        AbilityManager abilityManager = playerStats != null
            ? playerStats.GetComponent<AbilityManager>()
            : null;
        if (abilityManager != null)
        {
            IReadOnlyList<OwnedAbilityState> ownedAbilities = abilityManager.OwnedAbilities;
            for (int index = 0; index < ownedAbilities.Count; index++)
            {
                OwnedAbilityState state = ownedAbilities[index];
                if (state == null || state.Data == null)
                {
                    continue;
                }

                AbilityDataSO data = state.Data;
                var snapshot = new RunResultAbilitySnapshot(
                    data.GetStableId(),
                    data.abilityNameKey,
                    data.GetDisplayName(),
                    data.icon,
                    state.CurrentLevel,
                    state.MaxLevel,
                    data.presentationCategory);
                if (data.presentationCategory == AbilityPresentationCategory.Item)
                {
                    itemSnapshots.Add(snapshot);
                }
                else
                {
                    abilitySnapshots.Add(snapshot);
                }
            }
        }

        CharacterDataSO characterData = playerStats != null ? playerStats.CharacterData : null;
        var characterSnapshot = new RunResultCharacterSnapshot(
            characterData != null ? characterData.characterID : string.Empty,
            characterData != null ? characterData.characterNameKey : string.Empty,
            characterData != null ? characterData.GetDisplayName() : "未知角色",
            characterData != null ? characterData.GetSelectionIcon() : null);

        int gold = runState != null ? runState.GoldCount : 0;
        int kills = runState != null ? runState.KillCount : 0;
        int level = playerStats != null ? Mathf.Max(1, playerStats.currentLevel) : 1;
        return new RunResultSnapshot(
            outcome,
            isPreview,
            string.IsNullOrWhiteSpace(mapNameKey) ? "map.double_world_trial" : mapNameKey,
            string.IsNullOrWhiteSpace(mapDisplayName) ? "双世界试炼" : mapDisplayName,
            _elapsedSeconds,
            gold,
            kills,
            level,
            characterSnapshot,
            weaponSnapshots,
            itemSnapshots,
            abilitySnapshots,
            _telemetry != null
                ? _telemetry.CreatePickupSnapshots()
                : new List<RunResultPickupSnapshot>());
    }

    /// <summary>解析 Player、RunState、WorldLineCoordinator 和结果统计相关管理器。</summary>
    private void ResolveDependencies()
    {
        if (worldLineCoordinator == null)
        {
            worldLineCoordinator = FindObjectOfType<WorldLineCoordinator>();
        }

        if (levelUpManager == null)
        {
            levelUpManager = LevelUpManager.Instance != null
                ? LevelUpManager.Instance
                : FindObjectOfType<LevelUpManager>();
        }

        if (playerStats == null)
        {
            playerStats = FindObjectOfType<PlayerStats>();
        }

        if (playerHealth == null)
        {
            playerHealth = FindObjectOfType<PlayerHealth>();
        }

        if (_playerTransform == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                _playerTransform = player.transform;
            }
        }

        if (runState == null && playerStats != null)
        {
            runState = RunState.GetOrCreate(playerStats);
        }
    }

    /// <summary>建立一次事件绑定，并扫描正式持有武器，避免重复统计和重复订阅。</summary>
    private void BindRuntimeEvents()
    {
        if (_boundPlayerHealth != playerHealth)
        {
            if (_boundPlayerHealth != null)
            {
                _boundPlayerHealth.Died -= HandlePlayerDied;
                _boundPlayerHealth.Revived -= HandlePlayerRevived;
            }

            _boundPlayerHealth = playerHealth;
            if (_boundPlayerHealth != null)
            {
                _boundPlayerHealth.Died += HandlePlayerDied;
                _boundPlayerHealth.Revived += HandlePlayerRevived;
            }
        }

        if (_boundLevelUpManager != levelUpManager)
        {
            if (_boundLevelUpManager != null)
            {
                _boundLevelUpManager.InitialWeaponsReady -= HandleInitialWeaponsReady;
                _boundLevelUpManager.WeaponAdded -= HandleRuntimeWeaponAdded;
            }

            _boundLevelUpManager = levelUpManager;
            if (_boundLevelUpManager != null)
            {
                _boundLevelUpManager.InitialWeaponsReady += HandleInitialWeaponsReady;
                _boundLevelUpManager.WeaponAdded += HandleRuntimeWeaponAdded;
            }
        }
    }

    /// <summary>解除死亡、复活和武器清单事件绑定。</summary>
    private void UnbindRuntimeEvents()
    {
        if (_boundPlayerHealth != null)
        {
            _boundPlayerHealth.Died -= HandlePlayerDied;
            _boundPlayerHealth.Revived -= HandlePlayerRevived;
            _boundPlayerHealth = null;
        }

        if (_boundLevelUpManager != null)
        {
            _boundLevelUpManager.InitialWeaponsReady -= HandleInitialWeaponsReady;
            _boundLevelUpManager.WeaponAdded -= HandleRuntimeWeaponAdded;
            _boundLevelUpManager = null;
        }
    }

    /// <summary>在 LevelUpManager 明确发布初始化完成后登记起始武器的 0 秒获得时间。</summary>
    private void SyncInitialWeapons()
    {
        if (_initialWeaponScanDone || _telemetry == null || levelUpManager == null ||
            !levelUpManager.IsInitialWeaponsReady)
        {
            return;
        }

        _telemetry.SyncOwnedWeapons(levelUpManager.OwnedWeapons, 0f, true);
        _initialWeaponScanDone = true;
    }

    /// <summary>收到明确的初始武器完成事件后执行一次起始扫描。</summary>
    private void HandleInitialWeaponsReady()
    {
        SyncInitialWeapons();
    }

    /// <summary>只为正式新增武器登记获得时间；升级事件不会重置既有获得时间。</summary>
    private void HandleRuntimeWeaponAdded(WeaponBase weapon)
    {
        if (_telemetry == null || levelUpManager == null || _resultFrozen || weapon == null ||
            weapon.weaponData == null || !levelUpManager.IsInitialWeaponsReady)
        {
            return;
        }

        SyncInitialWeapons();
        _telemetry.RegisterRuntimeWeapon(weapon.weaponData, _elapsedSeconds);
    }
}
