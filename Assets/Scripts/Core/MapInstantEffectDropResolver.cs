using System.Collections.Generic;
using UnityEngine;

/// <summary>按非负权重从地图即时效果掉落表中确定性选择一个有效 Prefab。</summary>
public static class MapInstantEffectDropResolver
{
    /// <summary>
    /// 使用 0 到 1 的单位随机数执行一次权重选择；无有效条目或总权重为零时返回 null。
    /// </summary>
    public static GameObject Select(
        IReadOnlyList<MapInstantEffectDropEntry> entries,
        float unitRoll)
    {
        if (entries == null || entries.Count == 0)
        {
            return null;
        }

        float totalWeight = 0f;
        GameObject lastValidPrefab = null;
        for (int index = 0; index < entries.Count; index++)
        {
            MapInstantEffectDropEntry entry = entries[index];
            if (entry == null || entry.prefab == null || entry.weight <= 0f)
            {
                continue;
            }

            totalWeight += entry.weight;
            lastValidPrefab = entry.prefab;
        }

        if (totalWeight <= 0f || lastValidPrefab == null)
        {
            return null;
        }

        float normalizedRoll = Mathf.Clamp01(unitRoll);
        float threshold = normalizedRoll * totalWeight;
        float accumulatedWeight = 0f;
        for (int index = 0; index < entries.Count; index++)
        {
            MapInstantEffectDropEntry entry = entries[index];
            if (entry == null || entry.prefab == null || entry.weight <= 0f)
            {
                continue;
            }

            accumulatedWeight += entry.weight;
            if (threshold < accumulatedWeight)
            {
                return entry.prefab;
            }
        }

        // unitRoll=1 时阈值恰好位于总权重右边界，明确回退到最后一个有效条目。
        return lastValidPrefab;
    }
}
