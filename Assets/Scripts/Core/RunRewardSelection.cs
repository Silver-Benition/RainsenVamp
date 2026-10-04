using System.Collections.Generic;
using UnityEngine;

/// <summary>商店与宝箱共用的品质池选择；调用方先应用封印、放逐、持有上限等硬性过滤。</summary>
public static class RunRewardSelection
{
    /// <summary>
    /// 从给定类型和品质抽取商品。道具缺档只向低档回退，不提前生成高档道具。
    /// 上一页排除和同武器偏好为软条件，优先在原品质放宽它们；本页去重由调用方移除候选保证。
    /// 不改动候选和共享资产，无临时数组；随机样本显式传入，便于确定性验证。
    /// </summary>
    public static RunShopProduct Pick(IReadOnlyList<RunShopProduct> candidates, bool weapon, int tier,
        HashSet<string> previousIds, HashSet<string> preferredIds, float sample)
    {
        for (int quality = Mathf.Clamp(tier, 1, 4); quality >= 1; quality--)
        {
            bool fresh = Count(candidates, weapon, quality, previousIds, null, true, false) > 0;
            bool preferred = preferredIds != null && preferredIds.Count > 0
                && Count(candidates, weapon, quality, previousIds, preferredIds, fresh, true) > 0;
            int count = Count(candidates, weapon, quality, previousIds, preferredIds, fresh, preferred);
            if (count > 0)
            {
                int index = Mathf.Min(count - 1, Mathf.FloorToInt(Mathf.Clamp01(sample) * count));
                foreach (RunShopProduct product in candidates)
                    if (Matches(product, weapon, quality, previousIds, preferredIds, fresh, preferred) && index-- == 0)
                        return product;
            }
            // 武器自身具有四档数据，同一次抽取不因商品种类回退改变品质。
            if (weapon) break;
        }
        return null;
    }

    /// <summary>计数与实际选取使用相同条件，避免小池回退突破调用方已经应用的硬限制。</summary>
    private static int Count(IReadOnlyList<RunShopProduct> candidates, bool weapon, int quality,
        HashSet<string> previous, HashSet<string> preferred, bool freshOnly, bool preferredOnly)
    {
        int count = 0;
        foreach (RunShopProduct product in candidates)
            if (Matches(product, weapon, quality, previous, preferred, freshOnly, preferredOnly)) count++;
        return count;
    }

    /// <summary>道具使用其固有品质，武器使用此次抽取的档位；稳定 ID 与显示语言无关。</summary>
    private static bool Matches(RunShopProduct product, bool weapon, int quality,
        HashSet<string> previous, HashSet<string> preferred, bool freshOnly, bool preferredOnly)
    {
        return product != null && product.IsWeapon == weapon
            && (weapon || (product.content != null && product.content.abilityToGrant != null
                && product.content.abilityToGrant.quality == quality))
            && (!freshOnly || previous == null || !previous.Contains(product.Id))
            && (!preferredOnly || preferred.Contains(product.Id));
    }
}
