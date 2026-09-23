using System;
using System.Collections.Generic;
using Cinemachine;
using UnityEngine;

/// <summary>整局阶段；死亡/手动暂停由 GameFlowManager 独立管理，不破坏当前回合。</summary>
public enum RoundPhase { Preparing, Combat, Settling, Upgrades, Shop, Finished, Crates }

/// <summary>回合流程执行器；RunDirector 驱动时间，本组件负责阶段和局间服务。</summary>
[DefaultExecutionOrder(-200)]
[DisallowMultipleComponent]
public sealed class RoundController : MonoBehaviour
{
    public RoundRunConfigSO config;
    public static RoundController Instance { get; private set; }
    public static bool Enabled => Instance != null && Instance.isActiveAndEnabled && Instance.config != null;
    public static bool AllowsCombat => !Enabled || (Instance.Phase == RoundPhase.Combat
        && (GameFlowManager.Instance == null || !GameFlowManager.Instance.IsPaused));
    public RoundPhase Phase { get; private set; } = RoundPhase.Preparing;
    public RoundRuntime Current { get; private set; }
    public int RoundNumber { get; private set; }
    public int CompletedRounds { get; private set; }
    public RunMaterialWallet Wallet { get; private set; } = new RunMaterialWallet();
    public RunShopService Shop { get; private set; }
    public IReadOnlyList<RoundStatUpgrade> Choices => _choices;
    public IReadOnlyList<int> ChoiceTiers => _choiceTiers;
    /// <summary>按待领取队列还原本页对应的实际升级等级，避免一次获得多级时跳过十级保底。</summary>
    public int UpgradeLevel => RoundUpgradeRollRules.PendingLevel(_player.currentLevel, _player.PendingLevelUps);
    private readonly List<int> _choiceTiers = new List<int>(4);
    public string LastReward { get; private set; } = "";
    public PlayerStats Player => _player;
    public LevelUpManager Loadout => _loadout;
    public AbilityManager Items => _items;
    public event Action Changed;
    private PlayerStats _player;
    private PlayerHealth _health;
    private LevelUpManager _loadout;
    private AbilityManager _items;
    private WorldWaveManager _waves;
    private RunDirector _director;
    public int PendingCrates => _crates;
    public int PendingUpgrades => _player != null ? _player.PendingLevelUps : 0;
    public RunCrateReward CurrentCrate { get; private set; }
    private int _crates;
    private float _settlingElapsed;
    private int _lastCrateFrame = -1;
    private readonly List<MapInstantEffectPickup> _healingPickups = new List<MapInstantEffectPickup>();
    private readonly List<Vector3> _healingOrigins = new List<Vector3>();
    private readonly List<RunShopProduct> _crateCandidates = new List<RunShopProduct>();
    private int _choiceSequence;
    /// <summary>本波真正生成的普通宝箱数，用于幸运掉率递减；拾取不再重复计数。</summary>
    public int DroppedCrates { get; private set; }
    /// <summary>敌人实际生成宝箱后登记一次；只在战斗阶段接受。</summary>
    public void RegisterCrateDrop() { if (Phase == RoundPhase.Combat) DroppedCrates++; }
    private int _harvestedRound;
    private float _harvestGrowth;
    private int _upgradeRerolls;
    private bool _busy;
    private int _lastChoiceFrame = -1;
    private readonly List<RoundStatUpgrade> _choices = new List<RoundStatUpgrade>(4);
    private readonly List<RoundStatUpgrade> _candidates = new List<RoundStatUpgrade>(24);

    /// <summary>在其他系统初始化前建立模式识别，阻止未开始回合的攻击。</summary>
    private void Awake()
    {
        if (Instance != null && Instance != this) { enabled = false; return; }
        Instance = this;
    }

    /// <summary>依赖 Start 尚未完成时等待；准备完成后只启动一次。</summary>
    private void Update()
    {
        if (Phase == RoundPhase.Settling) { AdvanceSettlement(Time.unscaledDeltaTime); return; }
        if (config == null || Shop != null || Phase != RoundPhase.Preparing) return;
        _loadout = LevelUpManager.Instance;
        if (_loadout == null || !_loadout.IsInitialWeaponsReady || RunDirector.Instance == null) return;
        if (!config.Validate(out string error)) { Debug.LogError(error, this); Phase = RoundPhase.Finished; return; }
        _director = RunDirector.Instance;
        _player = FindObjectOfType<PlayerStats>();
        _health = _player.GetComponent<PlayerHealth>();
        _player.LevelGained += HandleLevelGained;
        _items = _player.GetComponent<AbilityManager>();
        WorldLineCoordinator coordinator = FindObjectOfType<WorldLineCoordinator>();
        coordinator.SetWorldSwitchLocked(true);
        _waves = coordinator.MainWorldWaveManager;
        _waves.enabled = false;
        Shop = new RunShopService(config.shopCatalog, Wallet, _loadout, _items, _player, () => Phase == RoundPhase.Shop && !_busy);
        BeginNextRound();
    }

    /// <summary>销毁时释放单例；场景重开自然创建全新钱包与目标状态。</summary>
    private void OnDestroy()
    {
        if (_player != null) _player.LevelGained -= HandleLevelGained;
        if (Instance == this) Instance = null;
    }

    /// <summary>每次真实升级通知提示栏，一次经验获得多级也逐级广播。</summary>
    private void HandleLevelGained(int level) { Changed?.Invoke(); }

    /// <summary>只累计战斗时间；生存时间不受敌对冻结影响。</summary>
    public void Tick(float delta)
    { if (Phase == RoundPhase.Combat && Current != null) Current.Tick(delta); }

    /// <summary>在本帧伤害事件完成后裁决通关；死亡复活流程拥有优先权。</summary>
    private void LateUpdate()
    {
        if (Phase != RoundPhase.Combat || Current == null || _health == null || _health.IsDead
            || (GameFlowManager.Instance != null && GameFlowManager.Instance.IsPaused)) return;
        RoundEvaluation evaluation = Current.Evaluate(true);
        if (evaluation == RoundEvaluation.Running || !Current.TryClose(evaluation)) return;
        if (evaluation == RoundEvaluation.Failed) { _director.EndRunAsDefeat(); return; }
        SettleRound();
    }

    /// <summary>目标事件带当前回合代次；测试和未来任务系统可显式提交代次。</summary>
    public bool ReportObjective(string key, int amount, int generation)
    {
        return Phase == RoundPhase.Combat && Current != null && Current.Report(key, amount, generation);
    }

    /// <summary>拾取宝箱仅入队，奖励在回合通过后发放。</summary>
    public bool QueueCrate()
    {
        if (!AllowsCombat && Phase != RoundPhase.Settling) return false;
        if (_crates == int.MaxValue) return false;
        _crates++;
        Changed?.Invoke();
        return true;
    }

    /// <summary>拾取材料同时增加经验；旧金币仍由原账号统计链处理。</summary>
    public void CollectMaterial(int value)
    {
        if (!AllowsCombat || _player == null) return;
        _player.AddExp(Wallet.Collect(value));
        Changed?.Invoke();
    }

    /// <summary>先关闭战斗并显示通过蒙版；清场不调用死亡入口，只保留回血道具用于短暂吸收动画。</summary>
    private void SettleRound()
    {
        Phase = RoundPhase.Settling;
        _settlingElapsed = 0;
        CompletedRounds = RoundNumber;
        _waves.enabled = false;
        GameFlowManager.Instance.SetIntermission(true);
        SetWeaponsActive(false);
        Changed?.Invoke();
        // 回合边界一次性快照，不在战斗热路径搜索对象。材料只装袋，不经过经验入口。
        foreach (ExpGem gem in FindObjectsOfType<ExpGem>()) Wallet.Bag(gem.RoundMaterialValue);
        foreach (TreasureChestPickup chest in FindObjectsOfType<TreasureChestPickup>()) chest.CollectForSettlement();
        foreach (CoinPickup coin in FindObjectsOfType<CoinPickup>()) coin.CollectForSettlement(_player);
        _healingPickups.Clear(); _healingOrigins.Clear();
        foreach (MapInstantEffectPickup pickup in FindObjectsOfType<MapInstantEffectPickup>())
        {
            if (pickup.IsConsumed || !(pickup.PickupData?.Effect is HealingMapInstantEffectSO)) continue;
            _healingPickups.Add(pickup); _healingOrigins.Add(pickup.transform.position);
            pickup.GetComponent<Collider2D>().enabled = false;
        }
        PoolManager.Instance.ReleaseRoundObjects(true);
        WorldFreezeController.Instance?.CancelFreeze();
        LastReward = "";
    }

    /// <summary>使用真实时间推进结算；0.45 秒吸收回血物，完整停留后进入奖励，退出或失败自然取消。</summary>
    private void AdvanceSettlement(float delta)
    {
        if (Phase != RoundPhase.Settling) return;
        _settlingElapsed += Mathf.Max(0, delta);
        float travel = Mathf.Clamp01(_settlingElapsed / .45f);
        for (int i = 0; i < _healingPickups.Count; i++)
        {
            MapInstantEffectPickup pickup = _healingPickups[i];
            if (pickup == null || !pickup.gameObject.activeInHierarchy || pickup.IsConsumed) continue;
            pickup.transform.position = Vector3.Lerp(_healingOrigins[i], _player.transform.position, travel * travel);
            if (travel >= 1) pickup.CollectForSettlement(_player);
        }
        if (_settlingElapsed < Mathf.Max(.45f, config.settlementSeconds)) return;
        _healingPickups.Clear(); _healingOrigins.Clear();
        SettleHarvesting();
        if (Phase == RoundPhase.Settling) ContinueGrowth();
    }

    /// <summary>每个成功回合只结算一次收获；先标记防止事件重入，经验可增加待选队列。</summary>
    private void SettleHarvesting()
    {
        if (_player == null || !_player.UsesBrotatoStats || _harvestedRound >= RoundNumber || _health.IsDead || Phase != RoundPhase.Settling) return;
        int harvest = (int)Math.Max(int.MinValue + 1d, Math.Min(int.MaxValue,
            Math.Round((double)_player.GetFinalStat(PlayerStatType.Harvesting), MidpointRounding.AwayFromZero)));
        int cost = harvest < 0 ? Math.Min(Wallet.Balance, -harvest) : 0;
        // 复用钱包通知延迟：观察者只看到材料、经验与增长全部完成后的状态。
        // 不创建账号记录；提前标记当波，防止通知回调重复领奖。
        Wallet.Transact(cost, Math.Max(0, harvest), () =>
        {
            _harvestedRound = RoundNumber;
            if (harvest > 0)
            {
                _player.AddExp(harvest);
                _harvestGrowth += Mathf.Ceil(harvest * .05f);
                _player.SetModifiers("round.harvest.growth", new[] {
                    new PlayerStatModifier(PlayerStatType.Harvesting, PlayerStatModifierMode.Flat, _harvestGrowth) });
            }
            else if (harvest < 0) _player.LoseExperience(-(float)harvest);
            return true;
        });
    }

    /// <summary>升级队列消耗完毕才开放商店；空属性池自动消费剩余次数，避免死锁。</summary>
    private void ContinueGrowth()
    {
        Phase = RoundPhase.Upgrades;
        _upgradeRerolls = 0;
        while (_player.PendingLevelUps > 0)
        {
            BuildChoices();
            if (_choices.Count > 0) { Changed?.Invoke(); return; }
            _player.ConsumePendingLevelUp();
        }
        ContinueCrates();
    }

    /// <summary>逐箱生成当前合法道具；空池兑换补偿，最终波也先处理奖励再提交胜利快照。</summary>
    private void ContinueCrates()
    {
        Phase = RoundPhase.Crates;
        while (_crates > 0)
        {
            _crateCandidates.Clear();
            foreach (RunShopProduct product in config.shopCatalog.products)
            {
                if (product.IsWeapon || RunState.Instance.IsBanished(product.Id)) continue;
                if (_player.UsesBrotatoStats && !product.content.abilityToGrant.IsAvailableInBrotato()) continue;
                OwnedAbilityState owned = _items.GetOwnedAbility(product.content.abilityToGrant);
                if (owned == null || owned.CurrentLevel < owned.Data.MaxLevel) _crateCandidates.Add(product);
            }
            if (_crateCandidates.Count > 0)
            {
                RunShopProduct product = _crateCandidates[UnityEngine.Random.Range(0, _crateCandidates.Count)];
                CurrentCrate = new RunCrateReward(product, RoundNumber, config.shopCatalog.recycleRatio);
                Changed?.Invoke(); return;
            }
            _crates--; Wallet.Credit(10);
            LastReward = RoundShopPresentation.Text("round.crate.empty", "无可用道具，材料 +10");
        }
        CurrentCrate = null;
        if (RoundNumber >= config.rounds.Count) { _director.CompleteRoundRun(); return; }
        // 仍保持战斗关闭；同一帧内完成复位并通知视图显示不透明商店，渲染不暴露瞬移过程。
        ResetPlayerPosition();
        Phase = RoundPhase.Shop;
        Shop.Enter(RoundNumber);
        Changed?.Invoke();
    }

    /// <summary>按当前奖励快照完成拿取、回收或禁用；同帧及事件重入不能连续处理下一箱。</summary>
    public bool ResolveCrate(CrateRewardAction action)
    {
        if (_busy || Phase != RoundPhase.Crates || CurrentCrate == null || _lastCrateFrame == Time.frameCount
            || !Enum.IsDefined(typeof(CrateRewardAction), action)) return false;
        RunCrateReward reward = CurrentCrate;
        RunState state = RunState.Instance;
        if (action == CrateRewardAction.Banish && (state.RemainingBanishes <= 0 || state.IsBanished(reward.Product.Id))) return false;
        if (action == CrateRewardAction.Take)
        {
            OwnedAbilityState owned = _items.GetOwnedAbility(reward.Product.content.abilityToGrant);
            if (owned != null && owned.CurrentLevel >= owned.Data.MaxLevel) return false;
        }
        _busy = true;
        try
        {
            bool committed = Wallet.Transact(0, action == CrateRewardAction.Take ? 0 : reward.RecycleValue, () =>
            {
                if (action == CrateRewardAction.Banish && !state.TryBanishUpgrade(reward.Product.Id)) return false;
                if (action == CrateRewardAction.Take && _items.GrantOrUpgrade(reward.Product.content.abilityToGrant) == null) return false;
                _crates--; CurrentCrate = null; _lastCrateFrame = Time.frameCount;
                return true;
            });
            if (!committed) return false;
            ContinueCrates(); return true;
        }
        finally { _busy = false; }
    }

    /// <summary>属性池无放回抽四项；普通页逐卡抽品质，十级页共用不低于三级的品质。</summary>
    private void BuildChoices()
    {
        _choices.Clear(); _choiceTiers.Clear();
        _candidates.Clear();
        foreach (RoundStatUpgrade entry in config.shopCatalog.stats)
            if (!_player.UsesBrotatoStats || BrotatoStatRules.IsAvailable(entry.modifier.StatType)) _candidates.Add(entry);
        bool milestone = UpgradeLevel % 10 == 0;
        int guaranteed = _player.UsesBrotatoStats ? BrotatoStatRules.GuaranteedUpgradeTier(UpgradeLevel) : 0;
        int sharedTier = milestone ? RoundUpgradeRollRules.RollTier(UnityEngine.Random.value, _player.Luck, 3) : 1;
        while (_choices.Count < 4 && _candidates.Count > 0)
        {
            int index = UnityEngine.Random.Range(0, _candidates.Count);
            _choices.Add(_candidates[index]); _candidates.RemoveAt(index);
            // 品质随选项一同保存，显示、重投与领取都读取同一份结果。
            _choiceTiers.Add(_player.UsesBrotatoStats ? (guaranteed > 0 ? guaranteed :
                BrotatoStatRules.RollTier(UpgradeLevel, _player.GetFinalStat(PlayerStatType.LuckPoints), UnityEngine.Random.value))
                : milestone ? sharedTier : RoundUpgradeRollRules.RollTier(UnityEngine.Random.value, _player.Luck));
        }
    }

    /// <summary>确认一次属性成长，使用独立来源叠加并且只消费一个待选次数。</summary>
    public bool Choose(int index)
    {
        if (_busy || _lastChoiceFrame == Time.frameCount || Phase != RoundPhase.Upgrades || index < 0 || index >= _choices.Count) return false;
        _busy = true;
        try
        {
            _lastChoiceFrame = Time.frameCount;
            RoundStatUpgrade choice = _choices[index];
            PlayerStatModifier mod = choice.modifier;
            _player.SetModifiers("round.level." + (++_choiceSequence),
                new[] { _choices[index].AtTier(_choiceTiers[index]) });
            _player.ConsumePendingLevelUp();
            ContinueGrowth();
            return true;
        }
        finally { _busy = false; }
    }

    /// <summary>免费重投耗尽后使用材料；不消耗升级次数。</summary>
    public bool RerollUpgrade()
    {
        if (_busy || Phase != RoundPhase.Upgrades) return false;
        _busy = true;
        try
        {
            if (!RunState.Instance.TryConsumeReroll() && !Wallet.TrySpend(UpgradeRerollPrice)) return false;
            _upgradeRerolls++;
            BuildChoices(); Changed?.Invoke(); return true;
        }
        finally { _busy = false; }
    }

    public int UpgradeRerollPrice => config.shopCatalog.initialRerollPrice + _upgradeRerolls * config.shopCatalog.rerollPriceStep;

    /// <summary>消耗本局跳过次数，放弃一次属性奖励。</summary>
    public bool SkipUpgrade()
    {
        if (_busy || Phase != RoundPhase.Upgrades) return false;
        _busy = true;
        try
        {
            if (!RunState.Instance.TryConsumeSkip()) return false;
            _player.ConsumePendingLevelUp(); ContinueGrowth(); return true;
        }
        finally { _busy = false; }
    }

    /// <summary>开始下一回合；调用只允许准备阶段或商店，重复按钮不能跳过回合。</summary>
    public bool BeginNextRound()
    {
        if (_busy || (Phase != RoundPhase.Preparing && Phase != RoundPhase.Shop) || Shop == null
            || Shop.IsBusy || RoundNumber >= config.rounds.Count) return false;
        Phase = RoundPhase.Preparing;
        RoundNumber++;
        DroppedCrates = 0;
        Current = new RoundRuntime(config.rounds[RoundNumber - 1], RoundNumber);
        _health.PrepareRound();
        // 后续波已经在进入商店时准备位置；首次进入整局尚未经过商店，单独初始化一次。
        if (RoundNumber == 1) ResetPlayerPosition();
        WorldFreezeController.Instance?.CancelFreeze();
        _director.PrepareRoundEncounter();
        _waves.BeginRound(Current.Definition.spawnConfig);
        Phase = RoundPhase.Combat;
        GameFlowManager.Instance.SetIntermission(false);
        SetWeaponsActive(true);
        if (Current.Definition.spawnBoss && !_director.TryStartBossEncounter())
        { Debug.LogError("首领回合生成失败。", this); _director.EndRunAsDefeat(); }
        Changed?.Invoke(); return true;
    }

    /// <summary>仅在战斗关闭时瞬移到中心，并同步子武器发射点；临时关闭插值防止旧姿态在恢复时回放。</summary>
    private void ResetPlayerPosition()
    {
        Vector3 previousPosition = _player.transform.position;
        Rigidbody2D body = _player.GetComponent<Rigidbody2D>();
        RigidbodyInterpolation2D interpolation = body.interpolation;
        body.interpolation = RigidbodyInterpolation2D.None;
        body.velocity = Vector2.zero; body.angularVelocity = 0;
        body.position = Vector2.zero;
        // Rigidbody2D.position 在物理步之前不保证 Transform 已更新，而武器从 Transform 读取发射原点。
        _player.transform.position = new Vector3(0, 0, _player.transform.position.z);
        Physics2D.SyncTransforms();
        body.interpolation = interpolation;
        ResetFollowingCameras(_player.transform.position - previousPosition);
    }

    /// <summary>角色瞬移后重置跟随镜头的历史状态；只在回合边界执行，保留正常战斗的阻尼和死区配置。</summary>
    private void ResetFollowingCameras(Vector3 positionDelta)
    {
        CinemachineCore core = CinemachineCore.Instance;
        for (int i = 0; i < core.VirtualCameraCount; i++)
        {
            CinemachineVirtualCameraBase camera = core.GetVirtualCamera(i);
            Transform follow = camera.Follow;
            if (follow == null || (follow != _player.transform && !follow.IsChildOf(_player.transform))) continue;
            // 通知组件与扩展目标已瞬移，随后放弃旧构图和阻尼历史。
            // 正式 Brain 在 LateUpdate（暂停时仍执行）重建镜头，首个商店渲染帧内即完成归中。
            camera.OnTargetObjectWarped(follow, positionDelta);
            camera.PreviousStateIsValid = false;
        }
    }

    /// <summary>控制所有独立武器实例；禁用时持续光环与环绕物各自完成回池。</summary>
    private void SetWeaponsActive(bool active)
    {
        foreach (WeaponBase weapon in _loadout.OwnedWeapons)
        {
            if (weapon == null) continue;
            if (active) weapon.ResetRoundCooldown();
            weapon.enabled = active;
        }
    }

    /// <summary>整局胜负出口关闭局间 UI 和战斗，回收时不产生额外奖励。</summary>
    public void Finish()
    {
        Phase = RoundPhase.Finished;
        Current?.TryClose(RoundEvaluation.Failed);
        if (_waves != null) _waves.enabled = false;
        if (_loadout != null) SetWeaponsActive(false);
        PoolManager.Instance?.ReleaseRoundObjects();
        Changed?.Invoke();
    }
}
