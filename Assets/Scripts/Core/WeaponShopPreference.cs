using UnityEngine;

/// <summary>一次武器偏好抽签的互斥结果。</summary>
public enum WeaponPreference { Any, SameWeapon, SharedSet }

/// <summary>原版同一次随机数上的累计阈值；不把 35% 误当成额外的同标签概率。</summary>
public static class WeaponShopPreference
{
    // 明确存储六档单精度阈值，避免运行时中间精度让恰好落在边界的样本进入错误分支。
    private static readonly float[] SameWeaponThresholds = { .25f, .24f, .23f, .22f, .21f, .20f };
    private static readonly float[] SharedSetThresholds = { .55f, .51f, .47f, .43f, .39f, .35f };

    /// <summary>前五波逐步减弱偏好；第六波起同武器 20%、同标签 15%、自由池 65%。</summary>
    public static WeaponPreference Roll(int completedWave, float sample)
    {
        int index = Mathf.Clamp(completedWave, 1, 6) - 1;
        if (sample < SameWeaponThresholds[index]) return WeaponPreference.SameWeapon;
        return sample < SharedSetThresholds[index] ? WeaponPreference.SharedSet : WeaponPreference.Any;
    }

    /// <summary>至少一个已持有标签即可进入偏好池；副本数量和匹配标签数均不加权。</summary>
    public static bool MatchesOwnedSet(WeaponDataSO data, LevelUpManager loadout)
    {
        if (data == null || data.weaponSets == null || loadout == null) return false;
        foreach (WeaponSetSO set in data.weaponSets)
            if (loadout.GetWeaponSetCount(set) > 0) return true;
        return false;
    }
}
