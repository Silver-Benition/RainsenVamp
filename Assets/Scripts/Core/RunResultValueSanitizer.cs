using UnityEngine;

/// <summary>
/// 结果统计使用的有限非负浮点边界工具。
/// 所有结果快照和热路径累加都通过这里收敛 NaN、无穷大、负数与溢出，
/// 保证结果页不会因为一个异常运行时值出现不可序列化或不可显示的数字。
/// </summary>
public static class RunResultValueSanitizer
{
    /// <summary>将任意输入收敛为有限非负值；正无穷按最大有限浮点数封顶。</summary>
    public static float SanitizeNonNegative(float value)
    {
        if (float.IsNaN(value) || value <= 0f)
        {
            return 0f;
        }

        return float.IsPositiveInfinity(value) ? float.MaxValue : value;
    }

    /// <summary>
    /// 对两个非负数执行饱和加法。
    /// 先分别清洗输入，再在加法前检查上界，避免浮点溢出为正无穷。
    /// </summary>
    public static float SaturatingAdd(float current, float amount)
    {
        float safeCurrent = SanitizeNonNegative(current);
        float safeAmount = SanitizeNonNegative(amount);
        if (safeAmount <= 0f)
        {
            return safeCurrent;
        }

        return safeCurrent >= float.MaxValue - safeAmount
            ? float.MaxValue
            : safeCurrent + safeAmount;
    }

    /// <summary>
    /// 计算武器从首次生效到结算时刻的有效作用时长。
    /// 结算时间早于首次生效时间时返回零，避免倒计时或异常时序制造负时长。
    /// </summary>
    public static float CalculateActiveDuration(
        float survivalTimeSeconds,
        float firstEffectTimeSeconds)
    {
        float safeSurvivalTime = SanitizeNonNegative(survivalTimeSeconds);
        float safeFirstEffectTime = SanitizeNonNegative(firstEffectTimeSeconds);
        if (safeSurvivalTime <= safeFirstEffectTime)
        {
            return 0f;
        }

        // 两个输入都已收敛为有限非负值，因此差值不会向正无穷溢出；
        // 再次清洗是为了让这个公共边界方法在未来改动后仍保持结果契约。
        return SanitizeNonNegative(safeSurvivalTime - safeFirstEffectTime);
    }

    /// <summary>
    /// 使用已经冻结的有效作用时长计算结果页 DPS；零时长和异常输入统一视为零 DPS。
    /// </summary>
    public static float CalculateDamagePerSecond(
        float actualDamage,
        float activeDurationSeconds)
    {
        float safeDamage = SanitizeNonNegative(actualDamage);
        float safeActiveDuration = SanitizeNonNegative(activeDurationSeconds);
        if (safeDamage <= 0f || safeActiveDuration <= 0f)
        {
            return 0f;
        }

        return SanitizeNonNegative(safeDamage / safeActiveDuration);
    }

    /// <summary>
    /// 兼容旧调用方：先按首次生效时间和结算时间计算有效时长，再计算 DPS。
    /// 新的结果冻结流程应优先传入已计算的有效时长，确保时间显示与 DPS 使用同一权威值。
    /// </summary>
    public static float CalculateDamagePerSecond(
        float actualDamage,
        float firstEffectTimeSeconds,
        float survivalTimeSeconds)
    {
        return CalculateDamagePerSecond(
            actualDamage,
            CalculateActiveDuration(survivalTimeSeconds, firstEffectTimeSeconds));
    }
}
