using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家本局属性与等级经验的唯一运行时来源。
/// 角色资产提供基础值，能力与临时效果通过稳定 sourceId 提供修改器；最终值仅在来源变化时重算。
/// </summary>
[DisallowMultipleComponent]
public class PlayerStats : MonoBehaviour
{
    private const int StatCount = (int)PlayerStatType.Defang + 1;

    [Header("角色配置")]
    [SerializeField] private CharacterDataSO characterData;
    [SerializeField, Tooltip("未绑定角色资产时使用的安全后备值，主要供独立测试对象使用。")]
    private CharacterBaseStats fallbackBaseStats = new CharacterBaseStats();

    [Header("等级与经验")]
    public int currentLevel = 1;
    public float currentExp;
    public float expToNextLevel = 10f;

    [Tooltip("逐级经验需求：第 1 项表示 Lv.1 到 Lv.2，第 2 项表示 Lv.2 到 Lv.3，以此类推。")]
    [SerializeField] private List<float> experienceRequirements = new List<float>
    {
        10f, 12f, 15f, 18f, 21f, 25f, 30f, 36f, 43f, 52f
    };

    [Tooltip("超过列表配置的等级后，以上一项经验需求乘此倍率继续增长。")]
    [Min(1f)]
    [SerializeField] private float experienceFallbackGrowth = 1.2f;

    private readonly Dictionary<string, List<PlayerStatModifier>> _modifierSources =
        new Dictionary<string, List<PlayerStatModifier>>(StringComparer.Ordinal);
    private readonly float[] _finalValues = new float[StatCount];
    private readonly float[] _flatTotals = new float[StatCount];
    private readonly float[] _additivePercentTotals = new float[StatCount];
    private readonly float[] _multiplicativeTotals = new float[StatCount];
    private bool _statsInitialized;
    private bool _sessionCharacterResolved;
    private int _levelUpQueue;

    /// <summary>任一最终属性重算完成后触发；监听者应只刷新自己消费的低频状态。</summary>
    public event Action StatsChanged;

    /// <summary>当前角色静态配置资产。</summary>
    public CharacterDataSO CharacterData => characterData;

    /// <summary>最终最大生命。</summary>
    public float MaxHealth => GetFinalStat(PlayerStatType.MaxHealth);

    /// <summary>最终每秒恢复生命。</summary>
    public float Recovery => GetFinalStat(PlayerStatType.Recovery);

    /// <summary>最终平减护甲。</summary>
    public float Armor => GetFinalStat(PlayerStatType.Armor);

    /// <summary>最终世界单位移动速度。</summary>
    public float FinalMoveSpeed => GetFinalStat(PlayerStatType.MoveSpeed);

    /// <summary>最终武器伤害倍率。</summary>
    public float Might => GetFinalStat(PlayerStatType.Might);

    /// <summary>最终攻击范围倍率。</summary>
    public float Area => GetFinalStat(PlayerStatType.Area);

    /// <summary>最终投射物速度倍率。</summary>
    public float ProjectileSpeed => GetFinalStat(PlayerStatType.ProjectileSpeed);

    /// <summary>最终武器持续时间倍率。</summary>
    public float Duration => GetFinalStat(PlayerStatType.Duration);

    /// <summary>最终额外投射物数量；消费端按向下取整转换为整数。</summary>
    public float Amount => GetFinalStat(PlayerStatType.Amount);

    /// <summary>最终攻击冷却倍率；1 表示原始间隔，0.8 表示缩短 20%。</summary>
    public float Cooldown => GetFinalStat(PlayerStatType.Cooldown);

    /// <summary>最终经验获得倍率。</summary>
    public float Growth => GetFinalStat(PlayerStatType.Growth);

    /// <summary>最终拾取触发半径，单位为 Unity 世界单位。</summary>
    public float Magnet => GetFinalStat(PlayerStatType.Magnet);

    /// <summary>最终幸运倍率；1 表示基础候选权重与基础掉率。</summary>
    public float Luck => GetFinalStat(PlayerStatType.Luck);

    /// <summary>最终金币收益倍率；1 表示拾取物基础价值。</summary>
    public float Greed => GetFinalStat(PlayerStatType.Greed);

    /// <summary>最终诅咒倍率；1 表示敌人基础属性与基础生成压力。</summary>
    public float Curse => GetFinalStat(PlayerStatType.Curse);

    /// <summary>本局可获得的复活次数容量；消费端按向下取整转换为整数。</summary>
    public float Revival => GetFinalStat(PlayerStatType.Revival);

    /// <summary>本局可获得的重掷次数容量；消费端按向下取整转换为整数。</summary>
    public float Reroll => GetFinalStat(PlayerStatType.Reroll);

    /// <summary>本局可获得的跳过次数容量；消费端按向下取整转换为整数。</summary>
    public float Skip => GetFinalStat(PlayerStatType.Skip);

    /// <summary>本局可获得的放逐次数容量；消费端按向下取整转换为整数。</summary>
    public float Banish => GetFinalStat(PlayerStatType.Banish);

    /// <summary>最终魅惑等级，用于提高生成速率与敌人容量。</summary>
    public float Charm => GetFinalStat(PlayerStatType.Charm);

    /// <summary>敌人生成时成为无害单位的最终概率，范围为 0 到 1。</summary>
    public float Defang => GetFinalStat(PlayerStatType.Defang);

    /// <summary>初始化角色属性缓存与当前等级经验需求。</summary>
    private void Awake()
    {
        EnsureStatsInitialized();
        currentLevel = Mathf.Max(1, currentLevel);
        expToNextLevel = GetExperienceRequiredForLevel(currentLevel);
    }

    /// <summary>
    /// 切换本局角色配置并重建最终属性。
    /// 应仅在开局初始化或明确的角色切换流程中调用。
    /// </summary>
    public void SetCharacterData(CharacterDataSO newCharacterData)
    {
        // 显式切换角色优先于菜单会话，防止测试、重开或未来局内切换流程
        // 在重建缓存时又被仍然存活的静态选择覆盖。
        _sessionCharacterResolved = true;
        characterData = newCharacterData;
        _statsInitialized = false;
        EnsureStatsInitialized();
        StatsChanged?.Invoke();
    }

    /// <summary>
    /// 添加或替换一个稳定来源提供的全部属性修改器。
    /// 传入空列表等价于移除该来源，避免能力升级时旧等级继续叠加。
    /// </summary>
    public bool SetModifiers(string sourceId, IReadOnlyList<PlayerStatModifier> modifiers)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            Debug.LogError("[PlayerStats] 属性修改来源 ID 不能为空。", this);
            return false;
        }

        if (modifiers == null || modifiers.Count == 0)
        {
            return RemoveModifiers(sourceId);
        }

        var sourceCopy = new List<PlayerStatModifier>(modifiers.Count);
        for (int index = 0; index < modifiers.Count; index++)
        {
            sourceCopy.Add(modifiers[index]);
        }

        _modifierSources[sourceId] = sourceCopy;
        RecalculateFinalStats();
        StatsChanged?.Invoke();
        return true;
    }

    /// <summary>移除指定来源的全部修改器；来源不存在时不触发无意义重算。</summary>
    public bool RemoveModifiers(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId) || !_modifierSources.Remove(sourceId))
        {
            return false;
        }

        RecalculateFinalStats();
        StatsChanged?.Invoke();
        return true;
    }

    /// <summary>读取任意最终属性；首次访问时会安全完成惰性初始化，消除组件 Awake 顺序依赖。</summary>
    public float GetFinalStat(PlayerStatType statType)
    {
        EnsureStatsInitialized();
        int index = (int)statType;
        return index >= 0 && index < _finalValues.Length ? _finalValues[index] : 0f;
    }

    /// <summary>读取指定等级到下一级所需的正数经验。</summary>
    public float GetExperienceRequiredForLevel(int level)
    {
        int normalizedLevel = Mathf.Max(1, level);
        int listIndex = normalizedLevel - 1;

        if (experienceRequirements != null
            && listIndex < experienceRequirements.Count
            && experienceRequirements[listIndex] > 0f)
        {
            return experienceRequirements[listIndex];
        }

        float fallbackRequirement = Mathf.Max(expToNextLevel, 1f);
        int fallbackStartIndex = 0;
        if (experienceRequirements != null)
        {
            for (int index = experienceRequirements.Count - 1; index >= 0; index--)
            {
                if (experienceRequirements[index] <= 0f) continue;
                fallbackRequirement = experienceRequirements[index];
                fallbackStartIndex = index;
                break;
            }
        }

        int missingLevels = Mathf.Max(0, listIndex - fallbackStartIndex);
        return Mathf.Max(
            1f,
            fallbackRequirement * Mathf.Pow(Mathf.Max(experienceFallbackGrowth, 1f), missingLevels));
    }

    /// <summary>按最终 Growth 倍率吸收经验，并处理可能连续跨越多级的升级队列。</summary>
    public void AddExp(float amount)
    {
        if (amount <= 0f) return;

        currentExp += amount * Growth;
        if (expToNextLevel <= 0f)
        {
            expToNextLevel = GetExperienceRequiredForLevel(currentLevel);
        }

        while (currentExp >= expToNextLevel)
        {
            currentExp -= expToNextLevel;
            currentLevel++;
            expToNextLevel = GetExperienceRequiredForLevel(currentLevel);
            _levelUpQueue++;
        }

        CheckLevelUpQueue();
    }

    /// <summary>若升级队列非空且游戏可继续，则交给 LevelUpManager 展示下一次选择。</summary>
    public void CheckLevelUpQueue()
    {
        if (_levelUpQueue > 0 && Time.timeScale > 0f && LevelUpManager.Instance != null)
        {
            _levelUpQueue--;
            LevelUpManager.Instance.ShowLevelUpUI();
        }
    }

    /// <summary>
    /// 确保最终属性数组至少构建一次。
    /// 角色选择必须在任何组件首次读取属性前解析，避免子物体或同物体组件的 Awake
    /// 先触发惰性初始化后，PlayerStats.Awake 只替换角色引用却遗留旧缓存。
    /// </summary>
    private void EnsureStatsInitialized()
    {
        ResolveSessionCharacterOnce();
        if (_statsInitialized) return;
        _statsInitialized = true;
        RecalculateFinalStats();
    }

    /// <summary>
    /// 消费主菜单确认的本局角色，并在角色发生变化时使旧属性快照失效。
    /// 没有菜单选择时保持场景序列化角色作为安全默认值；成功消费后不再读取静态会话。
    /// </summary>
    private void ResolveSessionCharacterOnce()
    {
        if (_sessionCharacterResolved)
        {
            return;
        }

        CharacterDataSO selectedCharacter = CharacterSelectionSession.SelectedCharacter;
        if (selectedCharacter == null)
        {
            return;
        }

        _sessionCharacterResolved = true;
        if (ReferenceEquals(characterData, selectedCharacter))
        {
            return;
        }

        characterData = selectedCharacter;
        _statsInitialized = false;
    }

    /// <summary>
    /// 重新聚合全部来源并生成最终快照。
    /// 复杂度与当前修改器数量成正比，只在开局或来源变化时执行，不进入武器与投射物热路径。
    /// </summary>
    private void RecalculateFinalStats()
    {
        for (int index = 0; index < StatCount; index++)
        {
            _flatTotals[index] = 0f;
            _additivePercentTotals[index] = 0f;
            _multiplicativeTotals[index] = 1f;
        }

        foreach (KeyValuePair<string, List<PlayerStatModifier>> source in _modifierSources)
        {
            List<PlayerStatModifier> modifiers = source.Value;
            for (int modifierIndex = 0; modifierIndex < modifiers.Count; modifierIndex++)
            {
                PlayerStatModifier modifier = modifiers[modifierIndex];
                int statIndex = (int)modifier.StatType;
                if (statIndex < 0 || statIndex >= StatCount) continue;

                switch (modifier.Mode)
                {
                    case PlayerStatModifierMode.Flat:
                        _flatTotals[statIndex] += modifier.Value;
                        break;
                    case PlayerStatModifierMode.AdditivePercent:
                        _additivePercentTotals[statIndex] += modifier.Value;
                        break;
                    case PlayerStatModifierMode.Multiplicative:
                        _multiplicativeTotals[statIndex] *= modifier.Value;
                        break;
                }
            }
        }

        CharacterBaseStats fallback = fallbackBaseStats ?? new CharacterBaseStats();
        for (int index = 0; index < StatCount; index++)
        {
            PlayerStatType statType = (PlayerStatType)index;
            float baseValue = characterData != null
                ? characterData.GetBaseValue(statType)
                : fallback.GetValue(statType);
            float rawValue = (baseValue + _flatTotals[index])
                * (1f + _additivePercentTotals[index])
                * _multiplicativeTotals[index];
            _finalValues[index] = NormalizeFinalValue(statType, rawValue);
        }
    }

    /// <summary>按属性语义执行安全下限、冷却上限与概率钳制。</summary>
    private static float NormalizeFinalValue(PlayerStatType statType, float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;

        switch (statType)
        {
            case PlayerStatType.MaxHealth: return Mathf.Max(1f, value);
            case PlayerStatType.Armor: return value;
            case PlayerStatType.Greed: return value;
            case PlayerStatType.Cooldown: return Mathf.Max(0.1f, value);
            case PlayerStatType.Area:
            case PlayerStatType.Duration: return Mathf.Max(0.01f, value);
            case PlayerStatType.Defang: return Mathf.Clamp01(value);
            default: return Mathf.Max(0f, value);
        }
    }
}
