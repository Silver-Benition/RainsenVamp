using UnityEngine;

/// <summary>近战动作与冷却的数值契约；距离采用世界单位，内部按每范围点 0.01 单位换算。</summary>
public readonly struct MeleeAttackTiming
{
    public readonly float Windup, Swing, Recovery;
    public float Total => Windup + Swing + Recovery;

    /// <summary>按实际攻击行程计算三段时长；范围只延长主动段，负攻速不延长前摇和回收。</summary>
    public MeleeAttackTiming(float recoilDuration, float distance, float intervalMultiplier)
    {
        float attackSpeed = AttackSpeedRatio(intervalMultiplier);
        float positiveSpeed = Mathf.Max(0f, attackSpeed);
        Windup = Mathf.Max(.001f, recoilDuration) / (1f + positiveSpeed);
        Recovery = .2f / (1f + 3f * positiveSpeed);
        // 分母以原版范围点计；统一通过项目范围单位换算，避免把 70 当成 70 世界单位。
        float rangePoints = Mathf.Max(0f, distance) / BrotatoStatRules.RangeUnits;
        float factor = rangePoints / Mathf.Clamp(70f * (1f + attackSpeed / 3f), 70f, 120f);
        Swing = (Mathf.Max(.01f, .2f - attackSpeed / 10f) + .15f * factor) * .5f;
    }

    /// <summary>由项目正负攻速间隔倍率还原有符号比例；仅保护零分母，保留普通负值分支。</summary>
    private static float AttackSpeedRatio(float multiplier)
    {
        float safe = Mathf.Max(.000001f, multiplier);
        return safe <= 1f ? 1f / safe - 1f : 1f - safe;
    }

    /// <summary>正攻速缩短后坐距离；负攻速保留基础后坐，与前摇规则对应。</summary>
    public static float Recoil(float distance, float multiplier)
    {
        return Mathf.Max(0f, distance) / (1f + Mathf.Max(0f, AttackSpeedRatio(multiplier)));
    }

    /// <summary>动作结束后独立推进的冷却；以 60 ticks/秒截断，最低为两个 tick。</summary>
    public static float Cooldown(float baseSeconds, float multiplier)
    {
        float baseTicks = Mathf.Max(2f, baseSeconds * 60f);
        return Mathf.Floor(Mathf.Max(2f, baseTicks * Mathf.Max(.000001f, multiplier))) / 60f;
    }

    /// <summary>普通冷却按持武数量错峰；样本可确定性注入，面板展示未扰动参考值。</summary>
    public static float RandomizedCooldown(float cooldown, int weaponCount, float sample)
    {
        float ticks = cooldown * 60f;
        int count = Mathf.Clamp(weaponCount, 0, 6);
        float spread = Mathf.Min(count * ticks / 5f, count * 5f);
        return Mathf.Lerp(Mathf.Max(1f, ticks - spread), ticks + spread, Mathf.Clamp01(sample)) / 60f;
    }

    /// <summary>详情页展示最大范围下的完整参考间隔：动作三段相加，再加未扰动冷却。</summary>
    public static float Interval(WeaponLevelData level, float range, float multiplier, float recoilDuration = .1f)
    {
        return new MeleeAttackTiming(recoilDuration, range, multiplier).Total + Cooldown(level.cooldown, multiplier);
    }
}
