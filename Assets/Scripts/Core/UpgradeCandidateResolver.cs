using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>候选抽样使用的最小随机源接口，允许运行时与确定性测试共享同一算法。</summary>
public interface IRandomSource
{
    /// <summary>返回大于等于 0 且小于 1 的随机浮点数。</summary>
    float NextUnitFloat();
}

/// <summary>把 Unity 全局随机数适配为候选与掉落解析器使用的随机源。</summary>
public sealed class UnityRandomSource : IRandomSource
{
    /// <summary>取得 Unity 运行时随机值，并钳制极端边界以满足半开区间约定。</summary>
    public float NextUnitFloat()
    {
        return Mathf.Clamp(UnityEngine.Random.value, 0f, 0.99999994f);
    }
}

/// <summary>
/// 升级候选的纯逻辑解析器。
/// 使用加权无放回抽样，避免同次面板重复，并让 Luck 只提升明确标注的稀有内容。
/// </summary>
public static class UpgradeCandidateResolver
{
    /// <summary>
    /// 从合法池中按权重无放回抽取指定数量。
    /// 输入池不会被修改；无效、重复 ID 或非正权重内容会被忽略。
    /// </summary>
    public static List<UpgradeDataSO> SelectWeightedWithoutReplacement(
        IReadOnlyList<UpgradeDataSO> eligiblePool,
        int choiceCount,
        float luck,
        IRandomSource randomSource)
    {
        var result = new List<UpgradeDataSO>(Mathf.Max(0, choiceCount));
        if (eligiblePool == null || choiceCount <= 0)
        {
            return result;
        }

        randomSource = randomSource ?? new UnityRandomSource();
        var candidates = new List<UpgradeDataSO>(eligiblePool.Count);
        var stableIds = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < eligiblePool.Count; index++)
        {
            UpgradeDataSO candidate = eligiblePool[index];
            if (candidate == null || candidate.baseWeight <= 0f)
            {
                continue;
            }

            string stableId = candidate.GetStableId();
            if (string.IsNullOrWhiteSpace(stableId) || !stableIds.Add(stableId))
            {
                continue;
            }

            candidates.Add(candidate);
        }

        int targetCount = Mathf.Min(choiceCount, candidates.Count);
        float safeLuck = Mathf.Max(0.01f, luck);
        while (result.Count < targetCount && candidates.Count > 0)
        {
            int selectedIndex = SelectIndex(candidates, safeLuck, randomSource.NextUnitFloat());
            result.Add(candidates[selectedIndex]);
            candidates.RemoveAt(selectedIndex);
        }

        return result;
    }

    /// <summary>根据累计权重定位一个候选索引；浮点误差时回退到最后一项。</summary>
    private static int SelectIndex(
        IReadOnlyList<UpgradeDataSO> candidates,
        float luck,
        float unitRoll)
    {
        double totalWeight = 0d;
        for (int index = 0; index < candidates.Count; index++)
        {
            totalWeight += GetEffectiveWeight(candidates[index], luck);
        }

        if (totalWeight <= 0d)
        {
            return Mathf.Clamp(
                Mathf.FloorToInt(Mathf.Clamp01(unitRoll) * candidates.Count),
                0,
                candidates.Count - 1);
        }

        double target = Mathf.Clamp(unitRoll, 0f, 0.99999994f) * totalWeight;
        double accumulated = 0d;
        for (int index = 0; index < candidates.Count; index++)
        {
            accumulated += GetEffectiveWeight(candidates[index], luck);
            if (target < accumulated)
            {
                return index;
            }
        }

        return candidates.Count - 1;
    }

    /// <summary>计算一项候选经过 Luck 指数修正后的非负权重。</summary>
    private static double GetEffectiveWeight(UpgradeDataSO candidate, float luck)
    {
        double baseWeight = Math.Max(0d, candidate.baseWeight);
        double influence = Math.Max(0d, candidate.luckInfluence);
        return baseWeight * Math.Pow(luck, influence);
    }
}
