using UnityEngine;

/// <summary>Luck 概率修正的纯逻辑工具，供敌人掉落和确定性测试共同使用。</summary>
public static class DropChanceResolver
{
    /// <summary>
    /// 使用互补概率公式修正一次基础掉率。
    /// Luck=1 保持原概率，Luck 增长时平滑趋近 100%，并且不会越界。
    /// </summary>
    public static float GetLuckAdjustedChance(float baseChance, float luck)
    {
        float safeChance = Mathf.Clamp01(baseChance);
        float safeLuck = Mathf.Max(0f, luck);
        if (safeChance <= 0f || safeLuck <= 0f)
        {
            return 0f;
        }

        if (safeChance >= 1f)
        {
            return 1f;
        }

        return Mathf.Clamp01(1f - Mathf.Pow(1f - safeChance, safeLuck));
    }

    /// <summary>根据随机值判断一次掉落是否成功；相等边界按失败处理。</summary>
    public static bool ShouldDrop(float baseChance, float luck, float unitRoll)
    {
        return Mathf.Clamp01(unitRoll) < GetLuckAdjustedChance(baseChance, luck);
    }
}
