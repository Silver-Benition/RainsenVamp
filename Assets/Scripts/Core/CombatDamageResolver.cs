using UnityEngine;

/// <summary>
/// 一次战斗伤害结算的不可变结果。
/// AppliedDamage 表示目标接受的有效命中伤害；HealthLost 才表示本次实际减少的生命值。
/// 因此致命过量命中可以保留完整伤害统计，同时把生命值安全夹到零。
/// </summary>
public readonly struct CombatDamageResult
{
    /// <summary>建立一次包含有效命中伤害、生命损失和死亡状态的伤害结果。</summary>
    public CombatDamageResult(
        float requestedDamage,
        float appliedDamage,
        float healthLost,
        bool accepted,
        bool targetDefeated)
    {
        RequestedDamage = RunResultValueSanitizer.SanitizeNonNegative(requestedDamage);
        AppliedDamage = RunResultValueSanitizer.SanitizeNonNegative(appliedDamage);
        HealthLost = RunResultValueSanitizer.SanitizeNonNegative(healthLost);
        Accepted = accepted;
        TargetDefeated = targetDefeated;
    }

    /// <summary>攻击方请求的原始伤害。</summary>
    public float RequestedDamage { get; }

    /// <summary>目标接受的有效命中伤害；不会因目标剩余生命不足而被封顶。</summary>
    public float AppliedDamage { get; }

    /// <summary>本次命中实际减少的生命值，最大不超过命中前目标剩余生命。</summary>
    public float HealthLost { get; }

    /// <summary>本次请求是否通过目标的有效性与运行流程检查。</summary>
    public bool Accepted { get; }

    /// <summary>结算后目标是否已经归零。</summary>
    public bool TargetDefeated { get; }
}

/// <summary>
/// 支持有效命中与生命损失收据的目标契约。
/// IDamageable 仍保留给旧系统和玩家受击；敌人实现此接口后，武器系统可以得到完整命中与生命变化结果。
/// </summary>
public interface ICombatDamageTarget
{
    /// <summary>结算一次伤害并返回有效命中、生命损失与死亡状态。</summary>
    CombatDamageResult ApplyCombatDamage(float damage, bool isCritical);
}

/// <summary>
/// 统一的武器命中入口。
/// 目标层负责生命变化，解析器负责把“有效命中伤害”提交给当前局统计，避免 UI 或投射物自行累加。
/// </summary>
public static class CombatDamageResolver
{
    private static int _damageSettlementDepth;

    /// <summary>返回当前是否正处于伤害结果提交窗口，供 Boss 胜利出口延迟冻结。</summary>
    internal static bool IsSettlingDamage => _damageSettlementDepth > 0;

    /// <summary>
    /// 对一个敌方伤害目标结算武器伤害，并将有效命中伤害记录到指定或当前局遥测。
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
        float safeDamage = RunResultValueSanitizer.SanitizeNonNegative(damage);
        if (target == null || safeDamage <= 0f)
        {
            return new CombatDamageResult(safeDamage, 0f, 0f, false, false);
        }

        RunDirector director = RunDirector.Instance;
        if (director != null && director.IsResultFrozen)
        {
            return new CombatDamageResult(safeDamage, 0f, 0f, false, false);
        }

        ICombatDamageTarget combatTarget = target as ICombatDamageTarget;
        CombatDamageResult result = default(CombatDamageResult);
        BeginDamageSettlement();
        try
        {
            if (combatTarget != null)
            {
                result = combatTarget.ApplyCombatDamage(safeDamage, isCritical);
            }
            else
            {
                // 兼容尚未迁移的 IDamageable：旧目标无法返回生命损失，只能把已接受的请求视作有效命中值。
                // 正式敌人均实现 ICombatDamageTarget，因此生产统计不会走这里。
                target.TakeDamage(safeDamage, isCritical);
                result = new CombatDamageResult(safeDamage, safeDamage, safeDamage, true, false);
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
        }
        finally
        {
            EndDamageSettlement();
        }

        // Boss 的 Die() 可能在 ApplyCombatDamage() 内同步触发；必须等有效命中伤害完成记账后再冻结快照。
        if (director != null)
        {
            director.FlushPendingBossDefeat();
        }

        return result;
    }

    /// <summary>进入可嵌套的伤害结算深度，保护同步死亡回调不提前冻结结果。</summary>
    private static void BeginDamageSettlement()
    {
        if (_damageSettlementDepth < int.MaxValue)
        {
            _damageSettlementDepth++;
        }
    }

    /// <summary>离开伤害结算深度；异常也必须恢复深度，避免后续胜利出口永久挂起。</summary>
    private static void EndDamageSettlement()
    {
        _damageSettlementDepth = Mathf.Max(0, _damageSettlementDepth - 1);
    }
}
