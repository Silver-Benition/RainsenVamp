using System.Globalization;
using UnityEngine;

/// <summary>
/// 统一角色属性在 HUD 与调试工具中的名称、顺序和最终值格式。
/// 展示顺序与 PlayerStatType 数值顺序一致，新增属性时必须同步扩展名称映射。
/// </summary>
public static class PlayerStatPresentation
{
    /// <summary>当前局内属性总数。</summary>
    public static int StatCount => (int)PlayerStatType.Defang + 1;

    /// <summary>按固定展示顺序取得属性类型。</summary>
    public static PlayerStatType GetStatAt(int index)
    {
        return (PlayerStatType)Mathf.Clamp(index, 0, StatCount - 1);
    }

    /// <summary>取得面向玩家的中文属性名称。</summary>
    public static string GetDisplayName(PlayerStatType statType)
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
            default: return statType.ToString();
        }
    }

    /// <summary>
    /// 把内部最终值转换为类似《吸血鬼幸存者》属性页的可读数值。
    /// 倍率类以相对中性值 1 的增减百分比显示，冷却 0.75 因而显示为 -25%。
    /// </summary>
    public static string FormatFinalValue(PlayerStatType statType, float value)
    {
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
