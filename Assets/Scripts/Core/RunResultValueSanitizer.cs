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
    /// 计算结果页 DPS，并把零时长、倒计时和异常浮点输入统一视为零 DPS。
    /// </summary>
    public static float CalculateDamagePerSecond(
        float actualDamage,
        float firstEffectTime,
        float finalTime)
    {
        float safeDamage = SanitizeNonNegative(actualDamage);
        float safeFirstEffectTime = SanitizeNonNegative(firstEffectTime);
        float safeFinalTime = SanitizeNonNegative(finalTime);
        float activeDuration = safeFinalTime - safeFirstEffectTime;
        if (safeDamage <= 0f || activeDuration <= 0f || float.IsNaN(activeDuration) ||
            float.IsInfinity(activeDuration))
        {
            return 0f;
        }

        return SanitizeNonNegative(safeDamage / activeDuration);
    }
}
