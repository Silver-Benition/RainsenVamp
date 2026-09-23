using UnityEngine;

/// <summary>单次攻击的值类型快照；品质、武器自带属性与角色负值已经合并，池复用必须清零。</summary>
public readonly struct WeaponHitSnapshot
{
    public readonly float CriticalChance;
    public readonly float CriticalMultiplier;
    public readonly float LifeStealChance;
    private readonly PlayerHealth _owner;

    /// <summary>在发射或持续武器刷新时构造，无每次命中的组件查找或集合分配。</summary>
    public WeaponHitSnapshot(PlayerStats stats, PlayerHealth owner, WeaponLevelData weapon)
    {
        bool active = stats != null && stats.UsesBrotatoStats && weapon != null;
        CriticalChance = active ? BrotatoStatRules.Probability(weapon.critChance, stats.GetFinalStat(PlayerStatType.CritChance)) : 0;
        CriticalMultiplier = active ? Mathf.Max(1, weapon.critMultiplier) : 1;
        LifeStealChance = active ? BrotatoStatRules.Probability(weapon.lifeSteal, stats.GetFinalStat(PlayerStatType.LifeSteal)) : 0;
        _owner = active ? owner : null;
    }

    /// <summary>每次命中独立掷暴击；伤害被目标接受后才尝试回血，所有武器共享玩家节流。</summary>
    public CombatDamageResult Apply(IDamageable target, float damage, WeaponDataSO weapon)
    {
        bool critical = CriticalChance > 0 && Random.value < CriticalChance;
        CombatDamageResult result = CombatDamageResolver.Apply(target,
            critical ? Mathf.Max(1, Mathf.Floor(damage * CriticalMultiplier)) : damage, weapon, critical);
        if (result.Accepted && result.AppliedDamage > 0 && _owner != null && LifeStealChance > 0)
            _owner.TryLifeSteal(LifeStealChance, Random.value);
        return result;
    }
}
