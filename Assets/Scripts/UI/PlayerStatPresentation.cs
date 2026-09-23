using System.Globalization;
using UnityEngine;

/// <summary>
/// 统一角色属性在 HUD 与调试工具中的名称、顺序和最终值格式。
/// 展示顺序与 PlayerStatType 数值顺序一致，新增属性时必须同步扩展名称映射。
/// </summary>
public static class PlayerStatPresentation
{
    /// <summary>可替换的本地化解析入口；空译文沿用中文回退。</summary>
    public static System.Func<string, string, string> ResolveText;

    /// <summary>旧属性展示顺序总数；新角色由 BrotatoStatRules 分组提供。</summary>
    public static int StatCount => (int)PlayerStatType.Defang + 1;

    /// <summary>按固定展示顺序取得属性类型。</summary>
    public static PlayerStatType GetStatAt(int index)
    {
        return (PlayerStatType)Mathf.Clamp(index, 0, StatCount - 1);
    }

    /// <summary>取得面向玩家的中文属性名称。</summary>
    public static string GetDisplayName(PlayerStatType statType)
    {
        string fallback = GetFallbackName(statType);
        string translated = ResolveText?.Invoke("stat." + statType, fallback);
        return string.IsNullOrEmpty(translated) ? fallback : translated;
    }

    /// <summary>统一保留新旧属性的中文名称，不参与逻辑身份判断。</summary>
    private static string GetFallbackName(PlayerStatType statType)
    {
        switch (statType)
        {
            case PlayerStatType.MaxHealth: return "最大生命";
            case PlayerStatType.Recovery: return "生命恢复";
            case PlayerStatType.Armor: return "护甲";
            case PlayerStatType.MoveSpeed: return "移动速度";
            case PlayerStatType.Might: return "力量";
            case PlayerStatType.Area: return "攻击范围";
            case PlayerStatType.ProjectileSpeed: return "投射物速度";
            case PlayerStatType.Duration: return "持续时间";
            case PlayerStatType.Amount: return "数量";
            case PlayerStatType.Cooldown: return "冷却";
            case PlayerStatType.Luck: return "幸运";
            case PlayerStatType.Growth: return "成长";
            case PlayerStatType.Greed: return "贪婪";
            case PlayerStatType.Curse: return "诅咒";
            case PlayerStatType.Magnet: return "磁吸范围";
            case PlayerStatType.Revival: return "复活";
            case PlayerStatType.Reroll: return "重投";
            case PlayerStatType.Skip: return "跳过";
            case PlayerStatType.Banish: return "放逐";
            case PlayerStatType.Charm: return "魅惑";
            case PlayerStatType.Defang: return "削弱";
            case PlayerStatType.HpRegeneration: return "生命再生";
            case PlayerStatType.LifeSteal: return "生命窃取";
            case PlayerStatType.DamagePercent: return "伤害";
            case PlayerStatType.MeleeDamage: return "近战伤害";
            case PlayerStatType.RangedDamage: return "远程伤害";
            case PlayerStatType.ElementalDamage: return "元素伤害";
            case PlayerStatType.AttackSpeed: return "攻击速度";
            case PlayerStatType.CritChance: return "暴击率";
            case PlayerStatType.Engineering: return "工程学";
            case PlayerStatType.Range: return "范围";
            case PlayerStatType.Dodge: return "闪避";
            case PlayerStatType.SpeedPercent: return "速度";
            case PlayerStatType.LuckPoints: return "幸运";
            case PlayerStatType.Harvesting: return "收获";
            case PlayerStatType.ExperienceGain: return "经验获取";
            case PlayerStatType.PickupRange: return "拾取范围";
            default: return statType.ToString();
        }
    }

    /// <summary>
    /// 把内部最终值转换为类似《吸血鬼幸存者》属性页的可读数值。
    /// 倍率类以相对中性值 1 的增减百分比显示，冷却 0.75 因而显示为 -25%。
    /// </summary>
    public static string FormatFinalValue(PlayerStatType statType, float value)
    {
        if ((int)statType >= 21) return FormatNumber(value) + (IsPointPercent(statType) ? "%" : "");
        switch (statType)
        {
            case PlayerStatType.Might:
            case PlayerStatType.Area:
            case PlayerStatType.ProjectileSpeed:
            case PlayerStatType.Duration:
            case PlayerStatType.Cooldown:
            case PlayerStatType.Luck:
            case PlayerStatType.Growth:
            case PlayerStatType.Greed:
            case PlayerStatType.Curse:
                return FormatSignedPercent((value - 1f) * 100f);
            case PlayerStatType.Defang:
                return FormatNumber(value * 100f) + "%";
            case PlayerStatType.Amount:
            case PlayerStatType.Revival:
            case PlayerStatType.Reroll:
            case PlayerStatType.Skip:
            case PlayerStatType.Banish:
            case PlayerStatType.Charm:
                return Mathf.FloorToInt(value).ToString(CultureInfo.InvariantCulture);
            case PlayerStatType.Recovery:
                return FormatNumber(value) + "/秒";
            default:
                return FormatNumber(value);
        }
    }

    /// <summary>新属性百分比采用点数，不重复乘一百。</summary>
    public static bool IsPointPercent(PlayerStatType stat)
    { return stat == PlayerStatType.LifeSteal || stat == PlayerStatType.DamagePercent || stat == PlayerStatType.AttackSpeed ||
        stat == PlayerStatType.CritChance || stat == PlayerStatType.Dodge || stat == PlayerStatType.SpeedPercent ||
        stat == PlayerStatType.ExperienceGain || stat == PlayerStatType.PickupRange; }

    /// <summary>把调试输入值格式化为可再次解析的稳定小数字符串。</summary>
    public static string FormatRawValue(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string FormatSignedPercent(float percent)
    {
        if (Mathf.Abs(percent) < 0.0001f)
        {
            return "0%";
        }

        string prefix = percent > 0f ? "+" : string.Empty;
        return prefix + FormatNumber(percent) + "%";
    }

    private static string FormatNumber(float value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
