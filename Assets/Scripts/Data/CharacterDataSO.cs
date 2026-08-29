using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 角色进入一局游戏时使用的基础属性快照。
/// 数值均为最终基础值而非显示名称，例如 Might=1 表示 100%。
/// </summary>
[Serializable]
public sealed class CharacterBaseStats
{
    [Header("生存")]
    [Min(1f)] public float maxHealth = 100f;
    [Min(0f)] public float recovery;
    public float armor;

    [Header("行动与战斗")]
    [Min(0f)] public float moveSpeed = 3f;
    [Min(0f)] public float might = 1f;
    [Min(0.01f)] public float area = 1f;
    [Min(0f)] public float projectileSpeed = 1f;
    [Min(0.01f)] public float duration = 1f;
    [Min(0f)] public float amount;
    [Min(0.1f)] public float cooldown = 1f;

    [Header("成长与经济")]
    [Min(0f)] public float luck = 1f;
    [Min(0f)] public float growth = 1f;
    public float greed = 1f;
    [Min(0f)] public float curse = 1f;
    [Min(0f)] public float magnet = 3f;

    [Header("局内资源")]
    [Min(0f)] public float revival;
    [Min(0f)] public float reroll;
    [Min(0f)] public float skip;
    [Min(0f)] public float banish;

    [Header("敌群规则")]
    [Min(0f)] public float charm;
    [Range(0f, 1f)] public float defang;

    /// <summary>按类型读取基础值，供 PlayerStats 无分配地构建运行时数组。</summary>
    public float GetValue(PlayerStatType statType)
    {
        switch (statType)
        {
            case PlayerStatType.MaxHealth: return maxHealth;
            case PlayerStatType.Recovery: return recovery;
            case PlayerStatType.Armor: return armor;
            case PlayerStatType.MoveSpeed: return moveSpeed;
            case PlayerStatType.Might: return might;
            case PlayerStatType.Area: return area;
            case PlayerStatType.ProjectileSpeed: return projectileSpeed;
            case PlayerStatType.Duration: return duration;
            case PlayerStatType.Amount: return amount;
            case PlayerStatType.Cooldown: return cooldown;
            case PlayerStatType.Luck: return luck;
            case PlayerStatType.Growth: return growth;
            case PlayerStatType.Greed: return greed;
            case PlayerStatType.Curse: return curse;
            case PlayerStatType.Magnet: return magnet;
            case PlayerStatType.Revival: return revival;
            case PlayerStatType.Reroll: return reroll;
            case PlayerStatType.Skip: return skip;
            case PlayerStatType.Banish: return banish;
            case PlayerStatType.Charm: return charm;
            case PlayerStatType.Defang: return defang;
            default: return 0f;
        }
    }
}

/// <summary>Session 14 首版支持的角色解锁条件。</summary>
public enum CharacterUnlockConditionType
{
    None = 0,
    LifetimeKills = 1,
    GoldPurchase = 2
}

/// <summary>角色解锁规则与本地化展示数据。</summary>
[Serializable]
public sealed class CharacterUnlockDefinition
{
    [Tooltip("None 表示新账号默认解锁；首版另支持累计击杀和金币购买。")]
    public CharacterUnlockConditionType conditionType;
    [Min(0), Tooltip("累计击杀目标或金币价格。")]
    public int requiredAmount;
    [Tooltip("解锁条件文本的本地化键。")]
    public string descriptionKey;
    [TextArea, Tooltip("本地化系统接入前使用的直接显示文本。")]
    public string description;

    /// <summary>返回配置文本；为空时根据首版条件生成安全后备描述。</summary>
    public string GetDisplayDescription()
    {
        if (!string.IsNullOrWhiteSpace(description))
        {
            return description;
        }

        switch (conditionType)
        {
            case CharacterUnlockConditionType.LifetimeKills:
                return $"账号累计击杀 {Mathf.Max(0, requiredAmount)} 个敌人";
            case CharacterUnlockConditionType.GoldPurchase:
                return $"消耗 {Mathf.Max(0, requiredAmount)} 账号金币";
            default:
                return "默认解锁";
        }
    }
}

/// <summary>角色不占用能力槽的固有属性型被动。</summary>
[Serializable]
public sealed class CharacterPassiveDefinition
{
    [Tooltip("稳定且唯一的被动 ID，用于 PlayerStats 修改器来源。")]
    public string passiveID;
    [Tooltip("被动名称的本地化键。")]
    public string passiveNameKey;
    [Tooltip("本地化系统接入前使用的直接显示名称。")]
    public string passiveDisplayName;
    [Tooltip("被动描述的本地化键。")]
    public string descriptionKey;
    [TextArea, Tooltip("本地化系统接入前使用的直接显示描述。")]
    public string description;
    [Tooltip("首版只支持通过稳定 sourceId 注入的属性修改器。")]
    public List<PlayerStatModifier> modifiers = new List<PlayerStatModifier>();

    /// <summary>返回被动显示名称；未配置时使用通用后备文本。</summary>
    public string GetDisplayName()
    {
        return !string.IsNullOrWhiteSpace(passiveDisplayName)
            ? passiveDisplayName
            : "无固有被动";
    }

    /// <summary>返回被动直接描述；未配置时给出明确的无效果说明。</summary>
    public string GetDisplayDescription()
    {
        return !string.IsNullOrWhiteSpace(description)
            ? description
            : "该角色当前没有额外的固有属性效果。";
    }
}

/// <summary>
/// 角色静态配置资产。
/// 只保存跨局不变的身份与基础属性，运行时绝不修改该资产。
/// </summary>
[CreateAssetMenu(fileName = "NewCharacterData", menuName = "GameData/Character Data")]
public sealed class CharacterDataSO : ScriptableObject
{
    [Header("基础信息")]
    [Tooltip("稳定且唯一的逻辑 ID，禁止使用本地化显示名作为系统键。")]
    public string characterID = "character_default";
    [Tooltip("角色显示名称的本地化键。")]
    public string characterNameKey = "character.default.name";

    [Header("角色选择表现")]
    [Tooltip("本地化系统接入前使用的直接显示名称；为空时回退到资产名。")]
    public string characterDisplayName = "默认角色";
    [Tooltip("4×3 角色槽位使用的头像；为空时回退到预览第一帧。")]
    public Sprite selectionIcon;
    [Tooltip("选择页左右两侧使用的半透明立绘；为空时回退到头像。")]
    public Sprite portraitSprite;
    [Tooltip("底部角色预览按顺序循环的 Sprite 帧。")]
    public List<Sprite> previewFrames = new List<Sprite>();

    [Header("基础属性")]
    public CharacterBaseStats baseStats = new CharacterBaseStats();

    [Header("角色内容")]
    [Tooltip("开局以 Lv.1 授予且计入六格武器容量的起始武器。")]
    public WeaponDataSO startingWeapon;
    [Tooltip("不占用能力槽的角色固有属性型被动。")]
    public CharacterPassiveDefinition passive = new CharacterPassiveDefinition();

    [Header("账号解锁")]
    public CharacterUnlockDefinition unlock = new CharacterUnlockDefinition();

    /// <summary>安全读取指定基础属性；缺失数据块时使用系统默认角色数值。</summary>
    public float GetBaseValue(PlayerStatType statType)
    {
        return (baseStats ?? new CharacterBaseStats()).GetValue(statType);
    }

    /// <summary>返回当前可直接展示的角色名。</summary>
    public string GetDisplayName()
    {
        return !string.IsNullOrWhiteSpace(characterDisplayName)
            ? characterDisplayName
            : name;
    }

    /// <summary>返回槽位头像，并按预览帧与立绘顺序安全回退。</summary>
    public Sprite GetSelectionIcon()
    {
        if (selectionIcon != null)
        {
            return selectionIcon;
        }

        if (previewFrames != null && previewFrames.Count > 0 && previewFrames[0] != null)
        {
            return previewFrames[0];
        }

        return portraitSprite;
    }

    /// <summary>返回左右立绘，并在未配置独立立绘时复用头像。</summary>
    public Sprite GetPortraitSprite()
    {
        return portraitSprite != null ? portraitSprite : GetSelectionIcon();
    }

    /// <summary>按循环索引取得角色预览帧；没有动画帧时使用头像。</summary>
    public Sprite GetPreviewFrame(int frameIndex)
    {
        if (previewFrames == null || previewFrames.Count == 0)
        {
            return GetSelectionIcon();
        }

        int normalizedIndex = Mathf.Abs(frameIndex) % previewFrames.Count;
        return previewFrames[normalizedIndex] != null
            ? previewFrames[normalizedIndex]
            : GetSelectionIcon();
    }

    /// <summary>返回固有被动稳定来源 ID；被动未配置 ID 时使用角色 ID 安全回退。</summary>
    public string GetPassiveSourceId()
    {
        string passiveId = passive != null ? passive.passiveID : string.Empty;
        string stableId = !string.IsNullOrWhiteSpace(passiveId) ? passiveId : characterID;
        return !string.IsNullOrWhiteSpace(stableId)
            ? $"character.passive:{stableId}"
            : string.Empty;
    }
}
