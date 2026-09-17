using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>局内商品静态定义，沿用升级包装稳定 ID 映射账号封印。</summary>
[Serializable]
public sealed class RunShopProduct
{
    public UpgradeDataSO content;
    [Min(1)] public int basePrice = 12;
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
}

/// <summary>局内商店与属性候选目录；价格、概率和候选均可由策划配置。</summary>
[CreateAssetMenu(menuName = "GameData/Rounds/Shop Catalog")]
public sealed class RunShopCatalogSO : ScriptableObject
{
    public List<RunShopProduct> products = new List<RunShopProduct>();
    public List<RoundStatUpgrade> stats = new List<RoundStatUpgrade>();
    [Min(0)] public int initialRerollPrice = 2;
    [Min(1)] public int rerollPriceStep = 2;
    [Range(0, 1)] public float recycleRatio = 0.25f;

    /// <summary>拒绝重复商品、非法价格和不足四项的属性池。</summary>
    public bool Validate(out string error)
    {
        error = "";
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (RunShopProduct product in products)
            if (product == null || product.content == null || !product.content.HasExactlyOneReward()
                || product.basePrice < 1 || !keys.Add(product.Id))
                { error = "商品内容、价格或稳定 ID 无效。"; return false; }
        keys.Clear();
        foreach (RoundStatUpgrade stat in stats)
            if (stat == null || string.IsNullOrWhiteSpace(stat.id) || !keys.Add(stat.id))
                { error = "属性候选 ID 无效。"; return false; }
        if (products.Count == 0 || stats.Count < 4)
            { error = "商店需要商品和至少四项属性。"; return false; }
        return true;
    }
}
