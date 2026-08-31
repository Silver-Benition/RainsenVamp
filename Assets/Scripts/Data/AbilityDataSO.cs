using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 单个正式能力等级的完整配置快照。
/// 基础属性修改器使用累计值；运行时升级时由稳定来源整体替换，避免旧等级重复叠加。
/// </summary>
[System.Serializable]
public sealed class AbilityLevelData
{
    [TextArea, Tooltip("升到本等级时显示的说明。")]
    public string upgradeDescription;

    [Tooltip("本等级生效的累计属性修改器，而不是相对上一级的增量。")]
    public List<PlayerStatModifier> statModifiers = new List<PlayerStatModifier>();
}

/// <summary>
/// 正式能力的静态数据资产。
/// 资产只保存策划配置；当前等级、条件状态和冷却均由 AbilityManager 及机制运行时持有。
/// </summary>
[CreateAssetMenu(fileName = "NewAbilityData", menuName = "GameData/Ability Data")]
public sealed class AbilityDataSO : ScriptableObject
{
    [Header("系统识别")]
    [Tooltip("稳定且唯一的能力 ID；运行时字典与属性来源只依赖该字段。")]
    public string abilityID;

    [Header("本地化与显示")]
    public string abilityNameKey;
    [TextArea] public string descriptionKey;
    [Tooltip("本地化系统接入前使用的直接显示名称；为空时回退到资产名。")]
    public string abilityDisplayName;
    [TextArea, Tooltip("本地化系统接入前使用的直接显示描述。")]
    public string displayDescription;
    public Sprite icon;

    [Header("持有栏表现")]
    [Min(0.01f)] public float loadoutIconScale = 1f;
    public Vector2 loadoutIconOffset;

    [Header("等级与机制")]
    [Tooltip("索引 0 对应 Lv.1；每项保存该等级的累计属性快照。")]
    public List<AbilityLevelData> levelConfigs = new List<AbilityLevelData>
    {
        new AbilityLevelData()
    };

    [Tooltip("可选的机制配置；为空时该能力只提供基础属性修改。")]
    public AbilityMechanicSO mechanic;

    /// <summary>返回能力最大等级；空配置仍按一级处理，避免候选系统出现零级上限。</summary>
    public int MaxLevel => Mathf.Max(1, levelConfigs != null ? levelConfigs.Count : 0);

    /// <summary>读取能力稳定 ID；缺失时以资产名兼容测试和旧内容。</summary>
    public string GetStableId()
    {
        return !string.IsNullOrWhiteSpace(abilityID) ? abilityID : name;
    }

    /// <summary>返回当前可直接展示的能力名称。</summary>
    public string GetDisplayName()
    {
        return !string.IsNullOrWhiteSpace(abilityDisplayName)
            ? abilityDisplayName
            : name;
    }

    /// <summary>返回当前可直接展示的能力基础描述。</summary>
    public string GetDisplayDescription()
    {
        return !string.IsNullOrWhiteSpace(displayDescription)
            ? displayDescription
            : descriptionKey;
    }

    /// <summary>
    /// 安全读取从 1 开始的等级快照；空配置返回不含修改器的临时快照。
    /// </summary>
    /// <param name="level">从 1 开始的目标等级。</param>
    /// <returns>对应等级的只读配置来源。</returns>
    public AbilityLevelData GetLevelConfig(int level)
    {
        if (levelConfigs == null || levelConfigs.Count == 0)
        {
            return new AbilityLevelData();
        }

        int index = Mathf.Clamp(level - 1, 0, levelConfigs.Count - 1);
        return levelConfigs[index];
    }

    /// <summary>读取目标等级的升级说明；未配置时回退到能力基础描述。</summary>
    public string GetLevelDescription(int level)
    {
        AbilityLevelData config = GetLevelConfig(level);
        return config != null && !string.IsNullOrWhiteSpace(config.upgradeDescription)
            ? config.upgradeDescription
            : GetDisplayDescription();
    }
}
