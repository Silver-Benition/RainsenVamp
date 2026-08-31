using UnityEngine;

/// <summary>能力机制运行时使用的场景依赖快照。</summary>
public sealed class AbilityRuntimeContext
{
    /// <summary>建立不会持有 ScriptableObject 运行状态的机制上下文。</summary>
    public AbilityRuntimeContext(
        Transform owner,
        PlayerStats playerStats,
        PlayerHealth playerHealth)
    {
        Owner = owner;
        PlayerStats = playerStats;
        PlayerHealth = playerHealth;
    }

    public Transform Owner { get; }
    public PlayerStats PlayerStats { get; }
    public PlayerHealth PlayerHealth { get; }
}

/// <summary>
/// 单个能力机制的纯运行时契约。
/// 机制通过事件工作，并在场景销毁时显式解除订阅，不把冷却或条件状态写回配置资产。
/// </summary>
public interface IAbilityMechanicRuntime
{
    /// <summary>能力升级后切换到新的配置等级。</summary>
    void SetLevel(int level);

    /// <summary>释放事件订阅、临时属性来源与其他本局状态。</summary>
    void Dispose();
}

/// <summary>玩家本局持有的一项能力状态，按首次获得顺序由 AbilityManager 管理。</summary>
public sealed class OwnedAbilityState
{
    /// <summary>以 Lv.1 建立一项运行时能力。</summary>
    public OwnedAbilityState(AbilityDataSO data)
    {
        Data = data;
        CurrentLevel = 1;
    }

    public AbilityDataSO Data { get; }
    public int CurrentLevel { get; private set; }
    public int MaxLevel => Data != null ? Data.MaxLevel : 1;
    public bool IsMaxLevel => CurrentLevel >= MaxLevel;
    internal IAbilityMechanicRuntime MechanicRuntime { get; set; }

    /// <summary>在未满级时提升一级，并报告本次是否发生变化。</summary>
    internal bool TryLevelUp()
    {
        if (IsMaxLevel)
        {
            return false;
        }

        CurrentLevel++;
        return true;
    }
}
