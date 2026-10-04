using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>局内商品静态定义，沿用升级包装稳定 ID 映射账号封印。</summary>
[Serializable]
public sealed class RunShopProduct
{
    public UpgradeDataSO content;
    [Min(1)] public int basePrice = 12;
    [Tooltip("武器四档独立基础价；旧资产留空时按原有基础价乘品质兼容。道具忽略此表。")]
    public int[] weaponTierPrices;
    /// <summary>读取当前品质的基础价；兼容旧目录，溢出时饱和，不修改静态资产。</summary>
    public int BasePriceAtTier(int tier)
    {
        if (!IsWeapon) return Mathf.Max(1, basePrice);
        int index = Mathf.Clamp(tier, 1, 4) - 1;
        return weaponTierPrices != null && weaponTierPrices.Length == 4
            ? Mathf.Max(1, weaponTierPrices[index]) : (int)Math.Min(int.MaxValue, (long)Mathf.Max(1, basePrice) * (index + 1));
    }
    public string Id => content != null ? content.GetStableId() : "";
    public bool IsWeapon => content != null && content.weaponToGrant != null;
    public string Name => content != null ? content.GetDisplayName() : "";
    public Sprite Icon => content != null ? content.icon : null;
}

/// <summary>属性四选一的基础档；品质倍率在抽取时应用，状态不写入此定义。</summary>
[Serializable]
public sealed class RoundStatUpgrade
{
    public string id;
    public string nameKey;
    public string displayName;
    public Sprite icon;
    public PlayerStatModifier modifier;
    public float[] tierValues;
    /// <summary>新体系逐档配置，旧数据无表时保留历史倍率规则。</summary>
    public PlayerStatModifier AtTier(int tier)
    { return new PlayerStatModifier(modifier.StatType, modifier.Mode, tierValues != null && tierValues.Length == 4
        ? tierValues[Mathf.Clamp(tier - 1, 0, 3)] : modifier.Value * Mathf.Clamp(tier, 1, 4)); }
}

/// <summary>局内商店与属性候选目录；价格、概率和候选均可由策划配置。</summary>
[CreateAssetMenu(menuName = "GameData/Rounds/Shop Catalog")]
public sealed class RunShopCatalogSO : ScriptableObject
{
    public List<RunShopProduct> products = new List<RunShopProduct>();
    public List<RoundStatUpgrade> stats = new List<RoundStatUpgrade>();
    // 只为旧资产序列化兼容保留；当前商店与升级刷新统一由 RunEconomyRules 按波次报价。
    [HideInInspector, Min(0)] public int initialRerollPrice = 2;
    [HideInInspector, Min(1)] public int rerollPriceStep = 2;
    [Range(0, 1)] public float recycleRatio = 0.25f;

    /// <summary>拒绝重复商品、非法价格和不足四项的属性池。</summary>
    public bool Validate(out string error)
    {
        error = "";
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (RunShopProduct product in products)
            if (product == null || product.content == null || !product.content.HasExactlyOneReward()
                || product.basePrice < 1 || !keys.Add(product.Id)
                || (product.weaponTierPrices != null && product.weaponTierPrices.Length > 0 &&
                    (product.weaponTierPrices.Length != 4 || Array.Exists(product.weaponTierPrices, value => value < 1))))
                { error = "商品内容、价格或稳定 ID 无效。"; return false; }
        keys.Clear();
        foreach (RoundStatUpgrade stat in stats)
            if (stat == null || string.IsNullOrWhiteSpace(stat.id) || !keys.Add(stat.id) ||
                (stat.tierValues != null && stat.tierValues.Length != 0 &&
                (stat.tierValues.Length != 4 || Array.Exists(stat.tierValues, value => float.IsNaN(value) || float.IsInfinity(value)))))
                { error = "属性候选 ID 无效。"; return false; }
        if (products.Count == 0 || stats.Count < 4)
            { error = "商店需要商品和至少四项属性。"; return false; }
        return true;
    }
}
