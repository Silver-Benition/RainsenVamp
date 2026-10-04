using System.Collections.Generic;
using System.Text;

/// <summary>只读武器标签说明；短名称用于卡片，当前羁绊及下一档用于悬停详情。</summary>
public static class WeaponSetPresentation
{
    /// <summary>列出策划显式配置的标签，不从攻击方式或伤害系数推断。</summary>
    public static string Names(WeaponDataSO data)
    {
        var text = new StringBuilder();
        if (data == null || data.weaponSets == null) return "";
        foreach (WeaponSetSO set in data.weaponSets)
        {
            if (set == null) continue;
            if (text.Length > 0) text.Append(" / ");
            text.Append(set.GetDisplayName());
        }
        return text.ToString();
    }

    /// <summary>展示已装备数量与当前总收益；下一档为该档总值，不会被误读为叠加增量。</summary>
    public static string Details(WeaponDataSO data, LevelUpManager loadout)
    {
        if (data == null || data.weaponSets == null) return "";
        var text = new StringBuilder();
        var seen = new HashSet<string>();
        foreach (WeaponSetSO set in data.weaponSets)
        {
            if (set == null || !seen.Add(set.GetStableId())) continue;
            int count = loadout != null ? loadout.GetWeaponSetCount(set) : 0;
            if (text.Length > 0) text.Append('\n');
            text.Append(set.GetDisplayName()).Append(" (").Append(count).Append("/6)：");
            text.Append(count < 2 ? RoundShopPresentation.Text("weapon.set.inactive", "未激活") : Bonuses(set, count));
            if (count < 6)
            {
                int next = UnityEngine.Mathf.Max(2, count + 1);
                text.Append('\n').Append(string.Format(RoundShopPresentation.Text("weapon.set.next", "{0} 把时：{1}"), next, Bonuses(set, next)));
            }
        }
        return text.ToString();
    }

    /// <summary>列出每个标签的 2–6 把总加成；只点亮实际生效的一档，避免误读为逐档累加。</summary>
    public static string AllTiers(WeaponDataSO data, LevelUpManager loadout)
    {
        if (data == null || data.weaponSets == null) return "";
        var text = new StringBuilder(); var seen = new HashSet<string>();
        foreach (WeaponSetSO set in data.weaponSets)
        {
            if (set == null || !seen.Add(set.GetStableId())) continue;
            int count = loadout != null ? loadout.GetWeaponSetCount(set) : 0;
            if (text.Length > 0) text.Append("\n\n");
            text.Append("<color=#E8DEB0>").Append(set.GetDisplayName()).Append(" (").Append(count).Append("/6)</color>");
            for (int tier = 2; tier <= 6; tier++)
                text.Append("\n<color=").Append(tier == UnityEngine.Mathf.Min(count, 6) ? "#B5E780" : "#94978E")
                    .Append(">(").Append(tier).Append(") ").Append(Bonuses(set, tier)).Append("</color>");
        }
        return text.ToString();
    }

    /// <summary>格式化一档真实修改器，保持正负号与百分比单位。</summary>
    private static string Bonuses(WeaponSetSO set, int count)
    {
        var text = new StringBuilder();
        foreach (PlayerStatModifier modifier in set.GetModifiers(count))
        {
            float value = modifier.Value;
            bool percent = modifier.Mode != PlayerStatModifierMode.Flat;
            if (modifier.Mode == PlayerStatModifierMode.Multiplicative) value -= 1;
            if (text.Length > 0) text.Append("，");
            text.Append(PlayerStatPresentation.GetDisplayName(modifier.StatType))
                .Append((percent ? value * 100 : value).ToString("+0.#;-0.#;0"))
                .Append(percent || PlayerStatPresentation.IsPointPercent(modifier.StatType) ? "%" : "");
        }
        return text.ToString();
    }
}
