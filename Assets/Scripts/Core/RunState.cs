using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 单局运行状态的权威来源。
/// 保存击杀、金币、可消耗属性次数与本局放逐集合，UI 只能订阅和请求操作，不能自行维护副本。
/// </summary>
[DisallowMultipleComponent]
public sealed class RunState : MonoBehaviour
{
    private readonly HashSet<string> _banishedUpgradeIds =
        new HashSet<string>(StringComparer.Ordinal);

    private PlayerStats _playerStats;
    private int _revivalCapacity;
    private int _rerollCapacity;
    private int _skipCapacity;
    private int _banishCapacity;
    private int _remainingRevivals;
    private int _remainingRerolls;
    private int _remainingSkips;
    private int _remainingBanishes;

    /// <summary>当前场景中的单局状态实例。</summary>
    public static RunState Instance { get; private set; }

    /// <summary>击杀、金币或次数变化时触发，表现层应在此时刷新。</summary>
    public event Action StateChanged;

    /// <summary>本局玩家属性来源。</summary>
    public PlayerStats PlayerStats => _playerStats;

    /// <summary>本局已确认击杀数。</summary>
    public int KillCount { get; private set; }

    /// <summary>本局已拾取金币数。</summary>
    public int GoldCount { get; private set; }

    /// <summary>剩余复活次数。</summary>
    public int RemainingRevivals => _remainingRevivals;

    /// <summary>剩余重掷次数。</summary>
    public int RemainingRerolls => _remainingRerolls;

    /// <summary>剩余跳过次数。</summary>
    public int RemainingSkips => _remainingSkips;

    /// <summary>剩余放逐次数。</summary>
    public int RemainingBanishes => _remainingBanishes;

    /// <summary>本局已经放逐的升级稳定 ID 集合。</summary>
    public IReadOnlyCollection<string> BanishedUpgradeIds => _banishedUpgradeIds;

    /// <summary>登记场景唯一实例并解析同对象上的玩家属性。</summary>
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // RunState 挂在玩家本体上，重复状态只能移除自身，绝不能连带销毁玩家对象。
            Destroy(this);
            return;
        }

        Instance = this;
        _playerStats = GetComponent<PlayerStats>();
        InitializeResourceCapacities();
    }

    /// <summary>启用时订阅属性重算，以增量同步本局可消耗次数。</summary>
    private void OnEnable()
    {
        if (_playerStats == null)
        {
            _playerStats = GetComponent<PlayerStats>();
        }

        if (_playerStats != null)
        {
            _playerStats.StatsChanged -= HandleStatsChanged;
            _playerStats.StatsChanged += HandleStatsChanged;
        }
    }

    /// <summary>停用时取消属性事件，避免重复订阅。</summary>
    private void OnDisable()
    {
        if (_playerStats != null)
        {
            _playerStats.StatsChanged -= HandleStatsChanged;
        }
    }

    /// <summary>销毁时释放静态引用。</summary>
    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// 获取指定玩家的单局状态；不存在时把组件添加到玩家对象。
    /// 动态补组件用于兼容当前已序列化场景，后续新场景仍可直接预挂载。
    /// </summary>
    public static RunState GetOrCreate(PlayerStats playerStats)
    {
        if (playerStats == null)
        {
            return Instance;
        }

        RunState state = playerStats.GetComponent<RunState>();
        if (state == null)
        {
            state = playerStats.gameObject.AddComponent<RunState>();
        }

        return state;
    }

    /// <summary>登记一次有效击杀并发布状态变化。</summary>
    public void RegisterKill()
    {
        if (KillCount < int.MaxValue)
        {
            KillCount++;
            StateChanged?.Invoke();
        }
    }

    /// <summary>增加正整数金币并发布状态变化。</summary>
    public void AddGold(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        GoldCount = AddClamped(GoldCount, amount);
        StateChanged?.Invoke();
    }

    /// <summary>尝试消耗一次复活；次数不足时不改变状态。</summary>
    public bool TryConsumeRevival()
    {
        return TryConsume(ref _remainingRevivals);
    }

    /// <summary>尝试消耗一次重掷；次数不足时不改变状态。</summary>
    public bool TryConsumeReroll()
    {
        return TryConsume(ref _remainingRerolls);
    }

    /// <summary>尝试消耗一次跳过；次数不足时不改变状态。</summary>
    public bool TryConsumeSkip()
    {
        return TryConsume(ref _remainingSkips);
    }

    /// <summary>尝试消耗一次放逐；次数不足时不改变状态。</summary>
    public bool TryConsumeBanish()
    {
        return TryConsume(ref _remainingBanishes);
    }

    /// <summary>查询指定升级是否已在本局被放逐。</summary>
    public bool IsBanished(string upgradeId)
    {
        return !string.IsNullOrWhiteSpace(upgradeId) && _banishedUpgradeIds.Contains(upgradeId);
    }

    /// <summary>把稳定升级 ID 加入本局放逐集合；重复 ID 不发布事件。</summary>
    public bool BanishUpgrade(string upgradeId)
    {
        if (string.IsNullOrWhiteSpace(upgradeId) || !_banishedUpgradeIds.Add(upgradeId))
        {
            return false;
        }

        StateChanged?.Invoke();
        return true;
    }

    /// <summary>把本局统计与放逐状态清零，并按当前属性重新获得全部次数。</summary>
    public void ResetRun()
    {
        KillCount = 0;
        GoldCount = 0;
        _banishedUpgradeIds.Clear();
        InitializeResourceCapacities();
        StateChanged?.Invoke();
    }

    /// <summary>首次建立四类次数容量与剩余值。</summary>
    private void InitializeResourceCapacities()
    {
        _revivalCapacity = GetStatCount(PlayerStatType.Revival);
        _rerollCapacity = GetStatCount(PlayerStatType.Reroll);
        _skipCapacity = GetStatCount(PlayerStatType.Skip);
        _banishCapacity = GetStatCount(PlayerStatType.Banish);
        _remainingRevivals = _revivalCapacity;
        _remainingRerolls = _rerollCapacity;
        _remainingSkips = _skipCapacity;
        _remainingBanishes = _banishCapacity;
    }

    /// <summary>
    /// 属性变化后按容量差额调整剩余次数。
    /// 已消费次数不会因为一次普通重算而返还；容量降低时则同步扣减超出的可用次数。
    /// </summary>
    private void HandleStatsChanged()
    {
        ReconcileResource(ref _revivalCapacity, ref _remainingRevivals, GetStatCount(PlayerStatType.Revival));
        ReconcileResource(ref _rerollCapacity, ref _remainingRerolls, GetStatCount(PlayerStatType.Reroll));
        ReconcileResource(ref _skipCapacity, ref _remainingSkips, GetStatCount(PlayerStatType.Skip));
        ReconcileResource(ref _banishCapacity, ref _remainingBanishes, GetStatCount(PlayerStatType.Banish));
        StateChanged?.Invoke();
    }

    /// <summary>读取非负整数型属性容量。</summary>
    private int GetStatCount(PlayerStatType statType)
    {
        return _playerStats != null
            ? Mathf.Max(0, Mathf.FloorToInt(_playerStats.GetFinalStat(statType)))
            : 0;
    }

    /// <summary>根据新旧容量差额更新剩余次数，并保证结果位于有效范围。</summary>
    private static void ReconcileResource(ref int oldCapacity, ref int remaining, int newCapacity)
    {
        int delta = newCapacity - oldCapacity;
        oldCapacity = newCapacity;
        remaining = Mathf.Clamp(remaining + delta, 0, newCapacity);
    }

    /// <summary>消耗一个引用计数并发布状态事件。</summary>
    private bool TryConsume(ref int remaining)
    {
        if (remaining <= 0)
        {
            return false;
        }

        remaining--;
        StateChanged?.Invoke();
        return true;
    }

    /// <summary>用长整型执行饱和加法，避免大额金币溢出为负数。</summary>
    private static int AddClamped(int current, int amount)
    {
        long total = (long)current + amount;
        return total >= int.MaxValue ? int.MaxValue : (int)total;
    }
}
