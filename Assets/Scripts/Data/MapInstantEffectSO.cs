using UnityEngine;

/// <summary>地图即时效果执行时可读取的玩家上下文。</summary>
public readonly struct MapInstantEffectContext
{
    /// <summary>建立一次不可变效果上下文。</summary>
    public MapInstantEffectContext(PlayerStats playerStats, PlayerHealth playerHealth)
    {
        PlayerStats = playerStats;
        PlayerHealth = playerHealth;
    }

    /// <summary>触发拾取的玩家属性组件。</summary>
    public PlayerStats PlayerStats { get; }

    /// <summary>触发拾取的玩家生命组件。</summary>
    public PlayerHealth PlayerHealth { get; }
}

/// <summary>
/// 无运行时状态的地图即时效果策略。
/// 具体效果只读取配置并修改本局服务，不能把冷却、剩余时长等状态写回共享资产。
/// </summary>
public abstract class MapInstantEffectSO : ScriptableObject
{
    /// <summary>尝试应用一次即时效果；只有实际生效时返回 true，供结果统计判断。</summary>
    public abstract bool TryApply(MapInstantEffectContext context);
}
