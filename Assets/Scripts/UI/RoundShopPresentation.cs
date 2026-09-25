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

    /// <summary>商店与暂停页共享详情；每个有效属性独占一行，图标只在已绑定图集的 TMP 文本中启用。</summary>
    public static string WeaponDetails(WeaponDataSO data, int tier, PlayerStats player = null, bool richText = false)
    {
        if (data == null) return "";
        WeaponLevelData level = data.GetRoundTierConfig(tier);
        if (level == null) return data.GetDisplayDescription();
        bool modern = player != null && player.UsesBrotatoStats;
        bool aura = data.runtimeType == WeaponRuntimeType.Aura;
        bool orbit = data.runtimeType == WeaponRuntimeType.Orbiting;
        bool melee = data.runtimeType == WeaponRuntimeType.Melee;
        bool projectile = data.runtimeType == WeaponRuntimeType.Projectile || data.runtimeType == WeaponRuntimeType.Lobbed;
        var text = new StringBuilder();
        float damage = modern ? BrotatoStatRules.Damage(level, player) : level.damage;
        text.Append(Text("round.damage", "伤害")).Append("：").Append(damage.ToString("0.##"));
        if (modern)
        {
            text.Append("（").Append(level.damage.ToString("0.##"));
            AppendScaling(text, level.meleeScaling, PlayerStatType.MeleeDamage, richText);
            AppendScaling(text, level.rangedScaling, PlayerStatType.RangedDamage, richText);
            AppendScaling(text, level.elementalScaling, PlayerStatType.ElementalDamage, richText);
            text.Append("）");
            // 基础暴击为零仍可从角色获得概率；乘数是武器的能力，零概率也值得展示。
            if (level.critMultiplier > 1)
                Row(text, "critical", "暴击", "x" + level.critMultiplier.ToString("0.##") + "（" +
                    (BrotatoStatRules.Probability(level.critChance, player.GetFinalStat(PlayerStatType.CritChance)) * 100).ToString("0.#") + "%" + Text("round.chance", "概率") + "）");
            float steal = BrotatoStatRules.Probability(level.lifeSteal, player.GetFinalStat(PlayerStatType.LifeSteal)) * 100;
            // 武器自带吸血被负属性完全抵消时保留零值，明确反馈这把武器的效果已失效。
            if (steal > 0 || level.lifeSteal != 0) Row(text, "lifeSteal", "生命窃取", steal.ToString("0.#") + "%");
        }
        float baseRange = melee ? level.meleeRange : aura ? level.auraRadius : orbit ? level.orbitRadius : level.attackRange;
        float range = modern ? BrotatoStatRules.WeaponRange(baseRange, level.rangeBonus, player.GetFinalStat(PlayerStatType.Range), melee) : baseRange;
        float speed = modern ? BrotatoStatRules.AttackIntervalMultiplier(level.attackSpeed + player.GetFinalStat(PlayerStatType.AttackSpeed)) : 1;
        float interval = (aura ? level.tickInterval : level.cooldown) * speed;
        if (modern && melee) interval *= range / Mathf.Max(.25f, baseRange);
        if (orbit) Row(text, "rotation", "转速", (level.orbitAngularSpeed / speed).ToString("0.#") + "度/秒");
        else Row(text, aura ? "tick" : "cooldown", aura ? "伤害间隔" : "冷却", Mathf.Max(aura ? .01f : .05f, interval).ToString("0.##") + Text("round.seconds", "秒"));
        if (range > 0)
        {
            string type = Text(melee ? "round.melee" : aura ? "round.aura" : orbit ? "round.orbit" : "round.ranged",
                melee ? "近战" : aura ? "光环" : orbit ? "环绕" : "远程");
            // 新体系用范围点展示，100 点对应 1 世界单位；不改武器实际射程。
            Row(text, "range", "范围", (modern ? range / BrotatoStatRules.RangeUnits : range).ToString("0.#") + "（" + type + "）");
        }
        if (!aura && level.projectileCount > 1) Row(text, "amount", "数量", level.projectileCount.ToString());
        if (projectile && level.pierceCount > 0) Row(text, "pierce", "穿透", level.pierceCount.ToString());
        if (data.runtimeType == WeaponRuntimeType.Projectile && level.bounceCount > 0) Row(text, "bounce", "弹射", level.bounceCount.ToString());
        return text.ToString();
    }

    /// <summary>只列出非零缩放系数；角色属性为零不会抹去武器成长信息。</summary>
    private static void AppendScaling(StringBuilder text, float coefficient, PlayerStatType stat, bool richText)
    {
        if (coefficient == 0) return;
        text.Append(coefficient > 0 ? " + " : " - ").Append((Mathf.Abs(coefficient) * 100).ToString("0.#"))
            .Append("%").Append(StatIconPresentation.Token(stat, richText));
    }

    /// <summary>追加独立属性行，名称通过统一翻译入口解析。</summary>
    private static void Row(StringBuilder text, string key, string fallback, string value)
    { text.Append("\n").Append(Text("round." + key, fallback)).Append("：").Append(value); }

    /// <summary>购买或宝箱展示单件收益与持有上限，避免把累计收益误读为本次增量。</summary>
    public static string ItemOfferDetails(AbilityDataSO data, int owned)
    {
        if (data == null) return "";
        string detail = ItemDetails(data, data.stackPerCopy ? 1 : (owned < data.MaxLevel ? owned + 1 : owned));
        return data.stackPerCopy ? Text("item.perCopy", "每件：") + "\n" + detail + "\n"
            + string.Format(Text("item.ownedLimit", "持有 {0}/{1}"), owned, data.CopyLimitText) : detail;
    }

    /// <summary>道具按当前等级配置列出属性，保留负面收益；机制型道具另附其说明。</summary>
    public static string ItemDetails(AbilityDataSO data, int level)
    {
        if (data == null) return "";
        var text = new StringBuilder();
        var config = data.GetLevelConfig(level);
        if (config != null && config.statModifiers != null)
            foreach (PlayerStatModifier modifier in config.statModifiers)
            {
                float value = modifier.Value;
                if (modifier.Mode == PlayerStatModifierMode.Multiplicative) value -= 1;
                if (value == 0) continue;
                bool percent = modifier.Mode != PlayerStatModifierMode.Flat;
                if (text.Length > 0) text.Append("\n");
                text.Append(PlayerStatPresentation.GetDisplayName(modifier.StatType)).Append("：")
                    .Append((percent ? value * 100 : value).ToString("+0.##;-0.##;0"))
                    .Append(percent || PlayerStatPresentation.IsPointPercent(modifier.StatType) ? "%" : "");
                if (modifier.Mode == PlayerStatModifierMode.Multiplicative) text.Append(Text("round.multiplier", "（乘算）"));
            }
        if (data.mechanic != null || text.Length == 0)
        {
            string description = data.GetLevelDescription(level);
            if (!string.IsNullOrWhiteSpace(description))
            { if (text.Length > 0) text.Append("\n"); text.Append(description); }
        }
        return text.ToString();
    }
}
