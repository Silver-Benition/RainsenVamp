using UnityEngine;

/// <summary>单次攻击的值类型快照；品质、武器自带属性与角色负值已经合并，池复用必须清零。</summary>
public readonly struct WeaponHitSnapshot
{
    public readonly float CriticalChance;
    public readonly float CriticalMultiplier;
    public readonly float LifeStealChance;
    private readonly PlayerHealth _owner;
    private readonly WeaponWaveDamage _waveDamage;
    private readonly int _waveToken;

    /// <summary>在发射或持续武器刷新时构造，无每次命中的组件查找或集合分配。</summary>
    public WeaponHitSnapshot(PlayerStats stats, PlayerHealth owner, WeaponLevelData weapon)
        : this(stats, owner, weapon, null) { }

    /// <summary>捕获实例账本及代次；子弹、近战、持续伤害和派生攻击复制同一归属。</summary>
    public WeaponHitSnapshot(PlayerStats stats, PlayerHealth owner, WeaponLevelData weapon, WeaponWaveDamage waveDamage)
    {
        _waveDamage = waveDamage;
        _waveToken = waveDamage != null ? waveDamage.Token : 0;
        bool active = stats != null && stats.UsesBrotatoStats && weapon != null;
        CriticalChance = active ? BrotatoStatRules.Probability(weapon.critChance, stats.GetFinalStat(PlayerStatType.CritChance)) : 0;
        CriticalMultiplier = active ? Mathf.Max(1, weapon.critMultiplier) : 1;
        LifeStealChance = active ? BrotatoStatRules.Probability(weapon.lifeSteal, stats.GetFinalStat(PlayerStatType.LifeSteal)) : 0;
        _owner = active ? owner : null;
    }

    /// <summary>延迟灼烧沿用发射归属，但保留不暴击、不吸血的既有规则。</summary>
    public CombatDamageResult ApplyDamageOverTime(IDamageable target, float damage, WeaponDataSO weapon)
    { return CombatDamageResolver.ApplyAttributed(target, damage, weapon, false, null, _waveDamage, _waveToken); }

    /// <summary>每次命中独立掷暴击；伤害被目标接受后才尝试回血，所有武器共享玩家节流。</summary>
    public CombatDamageResult Apply(IDamageable target, float damage, WeaponDataSO weapon)
    {
        bool critical = CriticalChance > 0 && Random.value < CriticalChance;
        CombatDamageResult result = CombatDamageResolver.ApplyAttributed(target,
            critical ? BrotatoStatRules.RoundDamage(damage * CriticalMultiplier) : damage, weapon, critical, null, _waveDamage, _waveToken);
        if (result.Accepted && result.AppliedDamage > 0 && _owner != null && LifeStealChance > 0)
            _owner.TryLifeSteal(LifeStealChance, Random.value);
        return result;
    }
}
