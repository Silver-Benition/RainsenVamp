using System;
using System.Text;
using UnityEngine;

/// <summary>局间界面的纯展示格式；不写武器、角色属性或商品配置。</summary>
public static class RoundShopPresentation
{
    /// <summary>可选本地化解析入口；未接入翻译表时使用明确的中文回退。</summary>
    public static Func<string, string, string> ResolveText;
    /// <summary>按稳定键取文本，空翻译保留回退文案。</summary>
    public static string Text(string key, string fallback)
    {
        string value = ResolveText?.Invoke(key, fallback);
        return string.IsNullOrEmpty(value) ? fallback : value;
    }

    /// <summary>以文字和边框双重表达品质，不依赖单一颜色。</summary>
    public static string Tier(int tier) => Text("round.quality", "品质") + " " + Mathf.Clamp(tier, 1, 4);
    /// <summary>读取四档可辨识的品质边框色。</summary>
    public static Color TierColor(int tier)
    {
        switch (tier)
        {
            case 2: return new Color32(105, 174, 208, 255);
            case 3: return new Color32(182, 118, 208, 255);
            case 4: return new Color32(224, 102, 89, 255);
            default: return new Color32(141, 144, 131, 255);
        }
    }

    /// <summary>商品和已持有武器共用基础品质快照，明确标注基础值，避免冒充玩家加成后的伤害。</summary>
    public static string WeaponDetails(WeaponDataSO data, int tier)
    {
        if (data == null) return "";
        WeaponLevelData level = data.GetRoundTierConfig(tier);
        if (level == null) return data.GetDisplayDescription();
        var text = new StringBuilder();
        text.Append(Text("round.baseStats", "基础属性")).Append("\n");
        text.Append(Text("round.damage", "伤害")).Append("  ").Append(level.damage.ToString("0.##")).Append("\n");
        bool aura = data.runtimeType == WeaponRuntimeType.Aura;
        text.Append(Text(aura ? "round.tick" : "round.cooldown", aura ? "伤害间隔" : "冷却"))
            .Append("  ").Append((aura ? level.tickInterval : level.cooldown).ToString("0.##")).Append("s\n");
        if (aura || data.runtimeType == WeaponRuntimeType.Orbiting || data.runtimeType == WeaponRuntimeType.Melee)
        {
            float range = aura ? level.auraRadius : data.runtimeType == WeaponRuntimeType.Orbiting ? level.orbitRadius : level.meleeRange;
            text.Append(Text("round.range", "范围")).Append("  ").Append(range.ToString("0.##")).Append("\n");
        }
        if (!aura) text.Append(Text("round.amount", "数量")).Append("  ").Append(level.projectileCount).Append("\n");
        if (data.runtimeType == WeaponRuntimeType.Projectile || data.runtimeType == WeaponRuntimeType.Lobbed)
            text.Append(Text("round.pierce", "穿透")).Append("  ").Append(level.pierceCount).Append("\n");
        string description = data.GetDisplayDescription();
        if (!string.IsNullOrWhiteSpace(description)) text.Append("\n").Append(description);
        return text.ToString().TrimEnd();
    }

    /// <summary>道具详情使用当前等级说明；商店使用购买后目标等级的明确说明。</summary>
    public static string ItemDetails(AbilityDataSO data, int level)
    {
        return data == null ? "" : data.GetLevelDescription(level);
    }
}
