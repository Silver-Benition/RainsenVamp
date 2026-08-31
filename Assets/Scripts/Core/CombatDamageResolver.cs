using UnityEngine;

/// <summary>
/// 一次战斗伤害结算的不可变结果。
/// AppliedDamage 只表示目标实际损失的生命值，因此致命一击不会把过量伤害计入武器统计。
/// </summary>
public readonly struct CombatDamageResult
{
    /// <summary>建立一次伤害结算结果。</summary>
    public CombatDamageResult(float requestedDamage, float appliedDamage, bool accepted, bool targetDefeated)
    {
        RequestedDamage = Mathf.Max(0f, requestedDamage);
        AppliedDamage = Mathf.Max(0f, appliedDamage);
        Accepted = accepted;
        TargetDefeated = targetDefeated;
    }

    /// <summary>攻击方请求的原始伤害。</summary>
    public float RequestedDamage { get; }

    /// <summary>目标实际扣除的生命值。</summary>
    public float AppliedDamage { get; }

    /// <summary>本次请求是否通过目标的有效性与运行流程检查。</summary>
    public bool Accepted { get; }

    /// <summary>结算后目标是否已经归零。</summary>
    public bool TargetDefeated { get; }
}

/// <summary>
/// 支持真实伤害收据的目标契约。
/// IDamageable 仍保留给旧系统和玩家受击；敌人实现此接口后，武器系统可以得到实际扣血结果。
/// </summary>
public interface ICombatDamageTarget
{
    /// <summary>结算一次伤害并返回真实扣血量。</summary>
    CombatDamageResult ApplyCombatDamage(float damage, bool isCritical);
}

/// <summary>
/// 统一的武器命中入口。
/// 目标层负责生命变化，解析器负责把“实际扣血”提交给当前局统计，避免 UI 或投射物自行累加。
/// </summary>
public static class CombatDamageResolver
{
    /// <summary>
    /// 对一个敌方伤害目标结算武器伤害，并将实际扣血记录到指定或当前局遥测。
    /// </summary>
    /// <param name="target">经过 DamageTargetFilter 筛选的敌人目标。</param>
    /// <param name="damage">本次攻击请求的伤害。</param>
    /// <param name="weaponData">稳定归属武器；能力或非武器伤害应传 null。</param>
    /// <param name="isCritical">是否为暴击，仅影响受击表现，不改变统计策略。</param>
    /// <param name="telemetry">可选的测试或独立运行遥测；为空时使用当前局遥测。</param>
    /// <returns>目标返回的真实结算结果。</returns>
    public static CombatDamageResult Apply(
        IDamageable target,
        float damage,
        WeaponDataSO weaponData,
        bool isCritical = false,
        RunTelemetry telemetry = null)
    {
        float safeDamage = Mathf.Max(0f, damage);
        if (target == null || safeDamage <= 0f)
        {
            return new CombatDamageResult(safeDamage, 0f, false, false);
        }

        RunDirector director = RunDirector.Instance;
        if (director != null && director.IsResultFrozen)
        {
            return new CombatDamageResult(safeDamage, 0f, false, false);
        }

        ICombatDamageTarget combatTarget = target as ICombatDamageTarget;
        CombatDamageResult result;
        if (combatTarget != null)
        {
            result = combatTarget.ApplyCombatDamage(safeDamage, isCritical);
        }
        else
        {
            // 兼容尚未迁移的 IDamageable：旧目标无法返回实际扣血，只能把已接受的请求视作实际值。
            // 正式敌人均实现 ICombatDamageTarget，因此生产统计不会走这里。
            target.TakeDamage(safeDamage, isCritical);
            result = new CombatDamageResult(safeDamage, safeDamage, true, false);
        }

        if (result.AppliedDamage > 0f && weaponData != null)
        {
            RunTelemetry targetTelemetry = telemetry ?? RunTelemetry.Active;
            if (targetTelemetry != null)
            {
                float effectTime = director != null ? director.ElapsedSeconds : 0f;
                targetTelemetry.RecordWeaponDamage(weaponData, result.AppliedDamage, effectTime);
            }
        }

        return result;
    }
}
