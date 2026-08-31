using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家本局正式能力的唯一运行时容器。
/// 负责六格容量、按获得顺序登记、累计属性来源替换，以及机制运行时的创建与释放。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerStats), typeof(PlayerHealth))]
public sealed class AbilityManager : MonoBehaviour
{
    private readonly Dictionary<string, OwnedAbilityState> _ownedById =
        new Dictionary<string, OwnedAbilityState>(StringComparer.Ordinal);
    private readonly List<OwnedAbilityState> _ownedOrder =
        new List<OwnedAbilityState>(PlayerLoadoutRules.MaxAbilityCount);

    private PlayerStats _playerStats;
    private PlayerHealth _playerHealth;
    private AbilityRuntimeContext _runtimeContext;

    /// <summary>能力种类或等级变化时触发，供 HUD 等只读表现层刷新。</summary>
    public event Action OwnedAbilitiesChanged;

    /// <summary>按首次获得顺序排列的只读能力列表。</summary>
    public IReadOnlyList<OwnedAbilityState> OwnedAbilities => _ownedOrder;

    /// <summary>玩家当前持有的不同能力种类数。</summary>
    public int OwnedAbilityCount => _ownedOrder.Count;

    /// <summary>缓存同对象上的玩家权威依赖，并建立机制上下文。</summary>
    private void Awake()
    {
        ResolveDependencies();
    }

    /// <summary>
    /// 场景销毁时释放全部机制订阅和能力属性来源。
    /// 测试中单独销毁组件时也不会把属性残留在仍存活的 PlayerStats 上。
    /// </summary>
    private void OnDestroy()
    {
        for (int index = 0; index < _ownedOrder.Count; index++)
        {
            OwnedAbilityState state = _ownedOrder[index];
            state.MechanicRuntime?.Dispose();
            if (_playerStats != null && state.Data != null)
            {
                _playerStats.RemoveModifiers(GetBaseSourceId(state.Data));
            }
        }

        _ownedById.Clear();
        _ownedOrder.Clear();
    }

    /// <summary>
    /// 获得或升级一项正式能力。
    /// 新能力受六格容量限制；已持有能力可继续升级到数据资产声明的最大等级。
    /// </summary>
    /// <param name="abilityData">需要授予的能力静态配置。</param>
    /// <returns>成功获得或已持有时返回运行时状态；无效、满级或容量不足时返回 null。</returns>
    public OwnedAbilityState GrantOrUpgrade(AbilityDataSO abilityData)
    {
        if (abilityData == null || !ResolveDependencies())
        {
            return null;
        }

        string abilityId = abilityData.GetStableId();
        if (string.IsNullOrWhiteSpace(abilityId))
        {
            Debug.LogWarning("[AbilityManager] 能力授予失败：稳定 ID 为空。", abilityData);
            return null;
        }

        if (_ownedById.TryGetValue(abilityId, out OwnedAbilityState existing))
        {
            if (!existing.TryLevelUp())
            {
                Debug.Log($"[AbilityManager] 能力已满级，跳过：{abilityData.GetDisplayName()}");
                return null;
            }

            ApplyCurrentLevel(existing);
            existing.MechanicRuntime?.SetLevel(existing.CurrentLevel);
            OwnedAbilitiesChanged?.Invoke();
            Debug.Log($"[AbilityManager] 能力升级：{abilityData.GetDisplayName()} Lv.{existing.CurrentLevel}/{existing.MaxLevel}");
            return existing;
        }

        if (_ownedOrder.Count >= PlayerLoadoutRules.MaxAbilityCount)
        {
            Debug.LogWarning(
                $"[AbilityManager] 已达到 {PlayerLoadoutRules.MaxAbilityCount} 种能力上限，" +
                $"无法获得：{abilityData.GetDisplayName()}");
            return null;
        }

        var state = new OwnedAbilityState(abilityData);
        _ownedById.Add(abilityId, state);
        _ownedOrder.Add(state);
        ApplyCurrentLevel(state);

        if (abilityData.mechanic != null)
        {
            state.MechanicRuntime = abilityData.mechanic.CreateRuntime(
                _runtimeContext,
                abilityData,
                state.CurrentLevel);
        }

        OwnedAbilitiesChanged?.Invoke();
        Debug.Log($"[AbilityManager] 获得能力：{abilityData.GetDisplayName()} Lv.1/{state.MaxLevel}");
        return state;
    }

    /// <summary>查询玩家是否已持有指定能力；未持有或配置无效时返回 null。</summary>
    public OwnedAbilityState GetOwnedAbility(AbilityDataSO abilityData)
    {
        if (abilityData == null)
        {
            return null;
        }

        _ownedById.TryGetValue(abilityData.GetStableId(), out OwnedAbilityState state);
        return state;
    }

    /// <summary>判断能力当前是否可以首次获得或继续升级。</summary>
    public bool CanAcquireAbility(AbilityDataSO abilityData)
    {
        if (abilityData == null || string.IsNullOrWhiteSpace(abilityData.GetStableId()))
        {
            return false;
        }

        if (_ownedById.TryGetValue(abilityData.GetStableId(), out OwnedAbilityState state))
        {
            return state != null && !state.IsMaxLevel;
        }

        return _ownedOrder.Count < PlayerLoadoutRules.MaxAbilityCount;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>
    /// 调试环境下确保玩家持有指定能力并至少达到目标等级；只升级，不执行降级或删除。
    /// </summary>
    public OwnedAbilityState DebugEnsureAbilityLevel(AbilityDataSO abilityData, int targetLevel)
    {
        if (abilityData == null)
        {
            return null;
        }

        int safeTarget = Mathf.Clamp(targetLevel, 1, abilityData.MaxLevel);
        OwnedAbilityState state = GetOwnedAbility(abilityData);
        while (state == null || state.CurrentLevel < safeTarget)
        {
            OwnedAbilityState changedState = GrantOrUpgrade(abilityData);
            if (changedState == null)
            {
                break;
            }

            state = changedState;
        }

        return state;
    }
#endif

    /// <summary>
    /// 用当前等级累计快照替换稳定基础来源。
    /// 仅在获得或升级时分配 PlayerStats 的来源副本，不进入战斗热路径。
    /// </summary>
    private void ApplyCurrentLevel(OwnedAbilityState state)
    {
        AbilityLevelData config = state.Data.GetLevelConfig(state.CurrentLevel);
        _playerStats.SetModifiers(
            GetBaseSourceId(state.Data),
            config != null ? config.statModifiers : null);
    }

    /// <summary>解析同对象上的权威依赖；缺失时记录明确错误并拒绝授予。</summary>
    private bool ResolveDependencies()
    {
        if (_playerStats == null)
        {
            _playerStats = GetComponent<PlayerStats>();
        }

        if (_playerHealth == null)
        {
            _playerHealth = GetComponent<PlayerHealth>();
        }

        if (_playerStats == null || _playerHealth == null)
        {
            Debug.LogError("[AbilityManager] PlayerStats 或 PlayerHealth 缺失，能力系统无法初始化。", this);
            return false;
        }

        if (_runtimeContext == null)
        {
            _runtimeContext = new AbilityRuntimeContext(transform, _playerStats, _playerHealth);
        }

        return true;
    }

    /// <summary>生成能力累计基础属性的稳定来源 ID。</summary>
    private static string GetBaseSourceId(AbilityDataSO abilityData)
    {
        return $"ability:{abilityData.GetStableId()}:base";
    }
}
