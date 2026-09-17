using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>整局阶段；死亡/手动暂停由 GameFlowManager 独立管理，不破坏当前回合。</summary>
public enum RoundPhase { Preparing, Combat, Settling, Upgrades, Shop, Finished }

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
    public int ChoiceTier { get; private set; } = 1;
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
    private int _crates;
    private int _choiceSequence;
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
        if (config == null || Shop != null || Phase != RoundPhase.Preparing) return;
        _loadout = LevelUpManager.Instance;
        if (_loadout == null || !_loadout.IsInitialWeaponsReady || RunDirector.Instance == null) return;
        if (!config.Validate(out string error)) { Debug.LogError(error, this); Phase = RoundPhase.Finished; return; }
        _director = RunDirector.Instance;
        _player = FindObjectOfType<PlayerStats>();
        _health = _player.GetComponent<PlayerHealth>();
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
    { if (Instance == this) Instance = null; }

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
        if (!AllowsCombat) return false;
        _crates = Math.Min(int.MaxValue, _crates + 1);
        return true;
    }

    /// <summary>拾取材料同时增加经验；旧金币仍由原账号统计链处理。</summary>
    public void CollectMaterial(int value)
    {
        if (!AllowsCombat || _player == null) return;
        _player.AddExp(Wallet.Collect(value));
        Changed?.Invoke();
    }

    /// <summary>关闭攻击后处理剩余材料与宝箱，然后按非死亡回收路径清场。</summary>
    private void SettleRound()
    {
        Phase = RoundPhase.Settling;
        _waves.enabled = false;
        GameFlowManager.Instance.SetIntermission(true);
        SetWeaponsActive(false);
        // 清场前读取地面价值；这些低频遍历只发生在回合边界。
        foreach (ExpGem gem in FindObjectsOfType<ExpGem>()) Wallet.Bag(gem.RoundMaterialValue);
        foreach (TreasureChestPickup chest in FindObjectsOfType<TreasureChestPickup>()) _crates++;
        PoolManager.Instance.ReleaseRoundObjects();
        WorldFreezeController.Instance?.CancelFreeze();
        CompletedRounds = RoundNumber;
        LastReward = "";
        while (_crates > 0) { LastReward = Shop.GrantCrate(); _crates--; }
        if (RoundNumber >= config.rounds.Count) { _director.CompleteRoundRun(); return; }
        ContinueGrowth();
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
        Phase = RoundPhase.Shop;
        Shop.Enter(RoundNumber);
        Changed?.Invoke();
    }

    /// <summary>从未放逐的属性池无放回抽四项，品质随进度与 Luck 提升。</summary>
    private void BuildChoices()
    {
        _choices.Clear();
        _candidates.Clear();
        foreach (RoundStatUpgrade stat in config.shopCatalog.stats)
            if (!RunState.Instance.IsBanished(stat.id)) _candidates.Add(stat);
        ChoiceTier = 1;
        float roll = UnityEngine.Random.value / Mathf.Max(0.1f, _player.Luck);
        if (RoundNumber >= 8 && roll < .05f) ChoiceTier = 4;
        else if (RoundNumber >= 4 && roll < .15f) ChoiceTier = 3;
        else if (RoundNumber >= 2 && roll < .4f) ChoiceTier = 2;
        while (_choices.Count < 4 && _candidates.Count > 0)
        {
            int index = UnityEngine.Random.Range(0, _candidates.Count);
            _choices.Add(_candidates[index]); _candidates.RemoveAt(index);
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
                new[] { new PlayerStatModifier(mod.StatType, mod.Mode, mod.Value * ChoiceTier) });
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

    /// <summary>消耗放逐并移除所选属性候选，本次升级仍保留。</summary>
    public bool Banish(int index)
    {
        if (_busy || Phase != RoundPhase.Upgrades || index < 0 || index >= _choices.Count) return false;
        _busy = true;
        try
        {
            if (!RunState.Instance.TryConsumeBanish()) return false;
            RunState.Instance.BanishUpgrade(_choices[index].id);
            BuildChoices();
            if (_choices.Count == 0) ContinueGrowth(); else Changed?.Invoke();
            return true;
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
        Current = new RoundRuntime(config.rounds[RoundNumber - 1], RoundNumber);
        _health.PrepareRound();
        Rigidbody2D body = _player.GetComponent<Rigidbody2D>();
        body.position = Vector2.zero; body.velocity = Vector2.zero;
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

    /// <summary>控制所有独立武器实例；禁用时持续光环与环绕物各自完成回池。</summary>
    private void SetWeaponsActive(bool active)
    {
        foreach (WeaponBase weapon in _loadout.OwnedWeapons)
        {
            if (weapon == null) continue;
            weapon.enabled = active;
            if (active) weapon.ResetRoundCooldown();
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
