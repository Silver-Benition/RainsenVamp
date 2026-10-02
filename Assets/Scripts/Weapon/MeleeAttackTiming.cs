using UnityEngine;

/// <summary>近战三段动作的统一时序；运行时与详情页共用，范围只增加回收旅程成本。</summary>
public readonly struct MeleeAttackTiming
{
    public readonly float Windup, Swing, Recovery;
    public float Total => Windup + Swing + Recovery;

    /// <summary>攻速倍率同时作用于三段动作；最短动作保障高攻速下姿势仍连续。</summary>
    public MeleeAttackTiming(float activeDuration, float range, float intervalMultiplier)
    {
        float speed = Mathf.Max(.01f, intervalMultiplier);
        Windup = Mathf.Max(.025f, .07f * speed);
        Swing = Mathf.Max(.045f, Mathf.Max(.1f, activeDuration) * .65f * speed);
        Recovery = Mathf.Max(.04f, (Mathf.Max(.1f, activeDuration) * .35f + Mathf.Max(.25f, range) * .045f) * speed);
    }

    /// <summary>冷却从出手时计时，至少覆盖完整动作；仅额外范围追加回收成本，避免按范围倍乘整个冷却。</summary>
    public static float Interval(WeaponLevelData level, float range, float multiplier)
    {
        var motion = new MeleeAttackTiming(level.activeDuration, range, multiplier);
        float extraTravel = (range - Mathf.Max(.25f, level.meleeRange)) * .045f * multiplier;
        return Mathf.Max(motion.Total, level.cooldown * multiplier + extraTravel);
    }
}
