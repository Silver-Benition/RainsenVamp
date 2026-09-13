using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>一个局外升级的完整等级表；每一级保存累计效果，不是相对前级的增量。</summary>
[CreateAssetMenu(menuName = "RainsenVampSur/Account Upgrade")]
public sealed class AccountUpgradeDataSO : ScriptableObject
{
    public string stableId;
    public string nameKey;
    public string descriptionKey;
    public string fallbackName;
    [TextArea] public string fallbackDescription;
    public Sprite icon;
    public PlayerStatType statType;
    public int maxLevel;
    public List<AccountUpgradeLevel> levels = new List<AccountUpgradeLevel>();

    /// <summary>主动消耗的三类属性资源不提供停用；稳定ID硬约束同时防止错误配置绕过。</summary>
    public bool CanToggleEnabled => !AccountProgressRules.IsUpgradeAlwaysEnabled(stableId) &&
        statType != PlayerStatType.Reroll && statType != PlayerStatType.Skip && statType != PlayerStatType.Banish;

    /// <summary>沿用项目本地化占位约定，优先可配置中文回退文本，无回退时使用键。</summary>
    public string GetDisplayName() { return !string.IsNullOrWhiteSpace(fallbackName) ? fallbackName : nameKey; }

    /// <summary>取得可配置具体效果说明，后续本地化可在此统一解析 descriptionKey。</summary>
    public string GetDescription() { return !string.IsNullOrWhiteSpace(fallbackDescription) ? fallbackDescription : descriptionKey; }

    /// <summary>检查完整等级配置；非法定义整体不可购买或生效，但历史退款不依赖此表。</summary>
    public bool Validate(out string error)
    {
        error = "升级配置不可用";
        if (string.IsNullOrWhiteSpace(stableId) || stableId != stableId.Trim() ||
            !Enum.IsDefined(typeof(PlayerStatType), statType) || maxLevel < 1 ||
            levels == null || levels.Count < maxLevel) return false;
        for (int i = 0; i < maxLevel; i++)
        {
            AccountUpgradeLevel level = levels[i];
            if (level == null || level.cost < 0 || level.modifiers == null || level.modifiers.Count == 0) return false;
            foreach (PlayerStatModifier modifier in level.modifiers)
            {
                if (modifier.StatType != statType || !Enum.IsDefined(typeof(PlayerStatModifierMode), modifier.Mode) ||
                    float.IsNaN(modifier.Value) || float.IsInfinity(modifier.Value) ||
                    (modifier.Mode == PlayerStatModifierMode.Multiplicative && modifier.Value <= 0f) ||
                    (modifier.Mode == PlayerStatModifierMode.AdditivePercent && modifier.Value <= -1f)) return false;
            }
        }
        error = string.Empty;
        return true;
    }
}

/// <summary>单级价格及达到该级后替换整个来源的累计修改器。</summary>
[Serializable]
public sealed class AccountUpgradeLevel
{
    public int cost;
    public List<PlayerStatModifier> modifiers = new List<PlayerStatModifier>();
}
