using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>只负责局外累计加成与本次增量的展示，不更改共享属性公式或存档。</summary>
public static class AccountShopEffectPresentation
{
    /// <summary>读取有效累计等级；本次收益是两个累计档位之差，首次以中性修改器为起点。</summary>
    public static string Format(AccountUpgradeDataSO definition, int purchasedLevel, bool increment)
    {
        if (definition == null || !definition.Validate(out _)) return "配置不可用";
        int current = Mathf.Clamp(purchasedLevel, 0, definition.maxLevel);
        if (increment && purchasedLevel >= definition.maxLevel) return "已满级";
        float[] before = Aggregate(definition, current);
        float[] after = increment ? Aggregate(definition, current + 1) : before;
        var text = new StringBuilder();
        // 三种模式分别计算，不能把混合模式强行加成一个数；乘算用相对中性倍率的加成展示。
        for (int mode = 0; mode < 3; mode++)
        {
            if (!UsesMode(definition, current, increment, mode)) continue;
            float value = increment ? after[mode] - before[mode] : after[mode] - (mode == 2 ? 1f : 0f);
            bool percent = mode != 0 || IsRatio(definition.statType);
            if (text.Length > 0) text.Append("，");
            text.Append((percent ? value * 100f : value).ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture));
            text.Append(percent || PlayerStatPresentation.IsPointPercent(definition.statType) ? "%" : Unit(definition.statType));
        }
        return text.Length == 0 ? "0" : text.ToString();
    }

    /// <summary>按现有公式聚合同一等级的修改器，避免重复同模式配置产生错误展示。</summary>
    private static float[] Aggregate(AccountUpgradeDataSO definition, int level)
    {
        var values = new[] { 0f, 0f, 1f };
        if (level == 0) return values;
        foreach (PlayerStatModifier modifier in definition.levels[level - 1].modifiers)
        {
            int mode = (int)modifier.Mode;
            if (mode == 2) values[mode] *= modifier.Value;
            else values[mode] += modifier.Value;
        }
        return values;
    }

    /// <summary>首级显示正确的中性单位，模式变化时同时显示旧模式的移除与新模式的加入。</summary>
    private static bool UsesMode(AccountUpgradeDataSO definition, int current, bool increment, int mode)
    {
        int from = Mathf.Max(1, current);
        int to = increment ? current + 1 : from;
        for (int level = from; level <= to; level++)
            foreach (PlayerStatModifier modifier in definition.levels[level - 1].modifiers)
                if ((int)modifier.Mode == mode) return true;
        return false;
    }

    /// <summary>倍率与概率属性的 Flat 值本身用百分比表达；削弱仍由 Flat 从零增加概率。</summary>
    private static bool IsRatio(PlayerStatType stat)
    {
        switch (stat)
        {
            case PlayerStatType.Might: case PlayerStatType.Area: case PlayerStatType.ProjectileSpeed:
            case PlayerStatType.Duration: case PlayerStatType.Cooldown: case PlayerStatType.Luck:
            case PlayerStatType.Growth: case PlayerStatType.Greed: case PlayerStatType.Curse:
            case PlayerStatType.Defang: return true;
            default: return false;
        }
    }

    /// <summary>为绝对值保留必要单位；百分比模式不追加绝对单位。</summary>
    private static string Unit(PlayerStatType stat)
    {
        switch (stat)
        {
            case PlayerStatType.Recovery: return "/秒";
            case PlayerStatType.MoveSpeed: return " 单位/秒";
            case PlayerStatType.Magnet: return " 单位";
            case PlayerStatType.Revival: case PlayerStatType.Reroll: case PlayerStatType.Skip:
            case PlayerStatType.Banish: return " 次";
            default: return string.Empty;
        }
    }
}
