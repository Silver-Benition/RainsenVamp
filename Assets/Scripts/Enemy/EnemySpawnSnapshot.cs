using UnityEngine;

/// <summary>
/// 单个敌人在生成瞬间取得的不可变运行时数值。
/// 已生成单位不再读取共享 EnemyDataSO，因此之后的玩家属性变化只影响新敌人。
/// </summary>
public readonly struct EnemySpawnSnapshot
{
    /// <summary>建立一份完整敌人快照。</summary>
    public EnemySpawnSnapshot(
        float maxHealth,
        float moveSpeed,
        float collisionDamage,
        float outgoingDamageMultiplier,
        bool isDefanged)
    {
        MaxHealth = Mathf.Max(0.01f, maxHealth);
        MoveSpeed = Mathf.Max(0f, moveSpeed);
        CollisionDamage = Mathf.Max(0f, collisionDamage);
        OutgoingDamageMultiplier = Mathf.Max(0f, outgoingDamageMultiplier);
        IsDefanged = isDefanged;
    }

    /// <summary>本生命周期最大生命。</summary>
    public float MaxHealth { get; }

    /// <summary>本生命周期追踪移动速度。</summary>
    public float MoveSpeed { get; }

    /// <summary>本生命周期玩家接触伤害。</summary>
    public float CollisionDamage { get; }

    /// <summary>远程攻击等其他伤害来源使用的统一倍率。</summary>
    public float OutgoingDamageMultiplier { get; }

    /// <summary>本敌人是否被 Defang，所有主动伤害均应为零。</summary>
    public bool IsDefanged { get; }

    /// <summary>
    /// 把攻击配置的基础伤害解析为本敌人实际输出伤害。
    /// 远程投射物必须在发射时调用并保存结果，避免对象池复用后引用旧敌人状态。
    /// </summary>
    public float ResolveOutgoingDamage(float baseDamage)
    {
        return IsDefanged ? 0f : Mathf.Max(0f, baseDamage) * OutgoingDamageMultiplier;
    }
}

/// <summary>统一计算敌人生成快照、Curse 压力与 Charm 数量影响。</summary>
public static class EnemySpawnSnapshotFactory
{
    private const float CharmSpawnRatePercentPerPoint = 0.2f;
    private const int CharmAliveCapacityPerPoint = 20;

    /// <summary>按玩家最终属性与一次随机判定建立新敌人快照。</summary>
    public static EnemySpawnSnapshot Create(
        EnemyDataSO enemyData,
        PlayerStats playerStats,
        float defangRoll)
    {
        float curse = playerStats != null ? playerStats.Curse : 1f;
        float defangChance = playerStats != null ? playerStats.Defang : 0f;
        return Create(enemyData, curse, defangChance, defangRoll);
    }

    /// <summary>按显式数值建立快照，供确定性测试和非玩家生成来源复用。</summary>
    public static EnemySpawnSnapshot Create(
        EnemyDataSO enemyData,
        float curse,
        float defangChance,
        float defangRoll)
    {
        float safeCurse = Mathf.Max(0f, curse);
        bool defanged = enemyData != null && enemyData.canBeDefanged &&
            Mathf.Clamp01(defangRoll) < Mathf.Clamp01(defangChance);

        float baseHealth = enemyData != null ? enemyData.maxHealth : 1f;
        float baseSpeed = enemyData != null ? enemyData.moveSpeed : 0f;
        float baseCollisionDamage = enemyData != null ? enemyData.collisionDamage : 0f;
        float damageMultiplier = defanged ? 0f : safeCurse;
        return new EnemySpawnSnapshot(
            baseHealth * safeCurse,
            baseSpeed * safeCurse,
            defanged ? 0f : baseCollisionDamage * safeCurse,
            damageMultiplier,
            defanged);
    }

    /// <summary>计算一条规则经过 Curse 与 Charm 修正后的每秒生成速率。</summary>
    public static float GetEffectiveSpawnRate(float baseRate, float curse, float charm)
    {
        int charmPoints = Mathf.Max(0, Mathf.FloorToInt(charm));
        float charmMultiplier = 1f + charmPoints * CharmSpawnRatePercentPerPoint;
        return Mathf.Max(0f, baseRate) * Mathf.Max(0f, curse) * charmMultiplier;
    }

    /// <summary>计算有限并发上限；原配置小于等于零时继续表示不限制。</summary>
    public static int GetEffectiveMaxAlive(int baseMaxAlive, float curse, float charm)
    {
        if (baseMaxAlive <= 0)
        {
            return 0;
        }

        int charmPoints = Mathf.Max(0, Mathf.FloorToInt(charm));
        double result = baseMaxAlive * (double)Mathf.Max(0f, curse) +
            charmPoints * (double)CharmAliveCapacityPerPoint;
        return result >= int.MaxValue ? int.MaxValue : Mathf.Max(1, Mathf.FloorToInt((float)result));
    }
}
