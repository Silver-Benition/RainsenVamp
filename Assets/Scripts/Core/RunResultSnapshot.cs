using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

/// <summary>本局最终结算方向。</summary>
public enum RunOutcome
{
    Victory = 0,
    Defeat = 1
}

/// <summary>结果页展示的角色身份快照。</summary>
public sealed class RunResultCharacterSnapshot
{
    /// <summary>复制角色展示所需的稳定字段与头像引用。</summary>
    public RunResultCharacterSnapshot(string characterId, string nameKey, string displayName, Sprite avatar)
    {
        CharacterId = characterId ?? string.Empty;
        NameKey = nameKey ?? string.Empty;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? "未知角色" : displayName;
        Avatar = avatar;
    }

    public string CharacterId { get; }
    public string NameKey { get; }
    public string DisplayName { get; }
    public Sprite Avatar { get; }
}

/// <summary>结果页单行武器统计快照。</summary>
public sealed class RunResultWeaponSnapshot
{
    /// <summary>复制武器稳定身份、等级、有效命中伤害和冻结后的有效作用时长。</summary>
    public RunResultWeaponSnapshot(
        string weaponId,
        string nameKey,
        string displayName,
        Sprite icon,
        int currentLevel,
        int maxLevel,
        float actualTotalDamage,
        float firstEffectTime,
        float activeDurationSeconds,
        float damagePerSecond)
    {
        WeaponId = weaponId ?? string.Empty;
        NameKey = nameKey ?? string.Empty;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? WeaponId : displayName;
        Icon = icon;
        CurrentLevel = Mathf.Max(1, currentLevel);
        MaxLevel = Mathf.Max(CurrentLevel, maxLevel);
        ActualTotalDamage = RunResultValueSanitizer.SanitizeNonNegative(actualTotalDamage);
        FirstEffectTime = RunResultValueSanitizer.SanitizeNonNegative(firstEffectTime);
        ActiveDurationSeconds = RunResultValueSanitizer.SanitizeNonNegative(activeDurationSeconds);
        DamagePerSecond = RunResultValueSanitizer.SanitizeNonNegative(damagePerSecond);
    }

    /// <summary>
    /// 兼容只提供首次生效时间的旧构造调用；没有结算时间时无法推导有效作用时长，按零保存。
    /// 正式结果冻结流程必须使用包含 activeDurationSeconds 的构造函数。
    /// </summary>
    public RunResultWeaponSnapshot(
        string weaponId,
        string nameKey,
        string displayName,
        Sprite icon,
        int currentLevel,
        int maxLevel,
        float actualTotalDamage,
        float firstEffectTime,
        float damagePerSecond)
        : this(
            weaponId,
            nameKey,
            displayName,
            icon,
            currentLevel,
            maxLevel,
            actualTotalDamage,
            firstEffectTime,
            0f,
            damagePerSecond)
    {
    }

    public string WeaponId { get; }
    public string NameKey { get; }
    public string DisplayName { get; }
    public Sprite Icon { get; }
    public int CurrentLevel { get; }
    public int MaxLevel { get; }
    public float ActualTotalDamage { get; }
    public float FirstEffectTime { get; }
    public float ActiveDurationSeconds { get; }
    public float DamagePerSecond { get; }
}

/// <summary>结果页右侧能力/物品网格中的单元格快照。</summary>
public sealed class RunResultAbilitySnapshot
{
    /// <summary>复制能力展示字段与显式分类；分类不从 mechanic 是否为空推断。</summary>
    public RunResultAbilitySnapshot(
        string abilityId,
        string nameKey,
        string displayName,
        Sprite icon,
        int currentLevel,
        int maxLevel,
        AbilityPresentationCategory category)
    {
        AbilityId = abilityId ?? string.Empty;
        NameKey = nameKey ?? string.Empty;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? AbilityId : displayName;
        Icon = icon;
        CurrentLevel = Mathf.Max(1, currentLevel);
        MaxLevel = Mathf.Max(CurrentLevel, maxLevel);
        Category = category;
    }

    public string AbilityId { get; }
    public string NameKey { get; }
    public string DisplayName { get; }
    public Sprite Icon { get; }
    public int CurrentLevel { get; }
    public int MaxLevel { get; }
    public AbilityPresentationCategory Category { get; }
}

/// <summary>结果页地图即时效果拾取统计快照。</summary>
public sealed class RunResultPickupSnapshot
{
    /// <summary>复制稳定 ID、显示字段、排序权重和成功上报次数。</summary>
    public RunResultPickupSnapshot(
        string pickupId,
        string nameKey,
        string displayName,
        Sprite icon,
        int sortOrder,
        int count)
    {
        PickupId = pickupId ?? string.Empty;
        NameKey = nameKey ?? string.Empty;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? PickupId : displayName;
        Icon = icon;
        SortOrder = sortOrder;
        Count = Mathf.Max(0, count);
    }

    public string PickupId { get; }
    public string NameKey { get; }
    public string DisplayName { get; }
    public Sprite Icon { get; }
    public int SortOrder { get; }
    public int Count { get; }
}

/// <summary>
/// 单局结果的不可变冻结快照。
/// 构造完成后只暴露只读集合；结果页不再读取 RunState、武器或能力运行时对象。
/// </summary>
public sealed class RunResultSnapshot
{
    private readonly ReadOnlyCollection<RunResultWeaponSnapshot> _weapons;
    private readonly ReadOnlyCollection<RunResultAbilitySnapshot> _items;
    private readonly ReadOnlyCollection<RunResultAbilitySnapshot> _abilities;
    private readonly ReadOnlyCollection<RunResultPickupSnapshot> _instantEffectPickups;

    /// <summary>复制完整的单局结算数据，防止冻结后仍被运行时容器改变。</summary>
    public RunResultSnapshot(
        RunOutcome outcome,
        bool isPreview,
        string mapNameKey,
        string mapDisplayName,
        float survivalTimeSeconds,
        int gold,
        int kills,
        int level,
        RunResultCharacterSnapshot character,
        IList<RunResultWeaponSnapshot> weapons,
        IList<RunResultAbilitySnapshot> items,
        IList<RunResultAbilitySnapshot> abilities,
        IList<RunResultPickupSnapshot> instantEffectPickups)
    {
        Outcome = outcome;
        IsPreview = isPreview;
        MapNameKey = mapNameKey ?? string.Empty;
        MapDisplayName = string.IsNullOrWhiteSpace(mapDisplayName) ? "双世界试炼" : mapDisplayName;
        SurvivalTimeSeconds = RunResultValueSanitizer.SanitizeNonNegative(survivalTimeSeconds);
        Gold = Mathf.Max(0, gold);
        Kills = Mathf.Max(0, kills);
        Level = Mathf.Max(1, level);
        Character = character ?? new RunResultCharacterSnapshot(string.Empty, string.Empty, "未知角色", null);
        _weapons = CopyReadOnly(weapons);
        _items = CopyReadOnly(items);
        _abilities = CopyReadOnly(abilities);
        _instantEffectPickups = CopyReadOnly(instantEffectPickups);
    }

    public RunOutcome Outcome { get; }
    public bool IsPreview { get; }
    public string MapNameKey { get; }
    public string MapDisplayName { get; }
    public float SurvivalTimeSeconds { get; }
    public int Gold { get; }
    public int Kills { get; }
    public int Level { get; }
    public RunResultCharacterSnapshot Character { get; }
    public IReadOnlyList<RunResultWeaponSnapshot> Weapons => _weapons;
    public IReadOnlyList<RunResultAbilitySnapshot> Items => _items;
    public IReadOnlyList<RunResultAbilitySnapshot> Abilities => _abilities;
    public IReadOnlyList<RunResultPickupSnapshot> InstantEffectPickups => _instantEffectPickups;

    /// <summary>复制列表并包裹为只读集合；该操作只在结果冻结时执行一次。</summary>
    private static ReadOnlyCollection<T> CopyReadOnly<T>(IList<T> source)
    {
        if (source == null || source.Count == 0)
        {
            return new List<T>().AsReadOnly();
        }

        var copy = new List<T>(source.Count);
        for (int index = 0; index < source.Count; index++)
        {
            if (!ReferenceEquals(source[index], null))
            {
                copy.Add(source[index]);
            }
        }

        return copy.AsReadOnly();
    }
}
