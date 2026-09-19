using UnityEngine;

/// <summary>属性升级的品质规则；随机样本由调用方提供，便于验证队列边界和幸运加成。</summary>
public static class RoundUpgradeRollRules
{
    /// <summary>队列按获得顺序领取；无待选等级时返回当前等级，最低为初始等级一。</summary>
    public static int PendingLevel(int currentLevel, int pendingCount)
    { return Mathf.Max(1, currentLevel - Mathf.Max(0, pendingCount - 1)); }

    /// <summary>普通升级沿用原品质阈值但取消波数限制；最低品质参数用于十级保底，幸运仍提高稀有率。</summary>
    public static int RollTier(float sample, float luck, int minimumTier = 1)
    {
        float roll = Mathf.Clamp01(sample) / Mathf.Max(.1f, luck);
        int tier = roll < .05f ? 4 : roll < .15f ? 3 : roll < .4f ? 2 : 1;
        return Mathf.Max(Mathf.Clamp(minimumTier, 1, 4), tier);
    }
}
