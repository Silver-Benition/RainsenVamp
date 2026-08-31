using UnityEngine;

/// <summary>
/// 单局首领遭遇配置：只描述何时、在哪个当前世界和用哪个池化 Prefab 生成首领。
/// </summary>
[CreateAssetMenu(fileName = "NewBossEncounter", menuName = "GameData/Boss Encounter")]
public sealed class BossEncounterDataSO : ScriptableObject
{
    [Header("触发")]
    [Min(0.1f), Tooltip("RunDirector 权威计时达到该秒数后触发。")]
    public float triggerTimeSeconds = 120f;

    [Tooltip("Boss 生成在当前玩家位置外侧的距离。")]
    [Min(0.5f)] public float spawnDistance = 7f;

    [Header("首领与对象池")]
    public BossDataSO bossData;
    public GameObject bossPrefab;

    /// <summary>读取安全的触发时间。</summary>
    public float GetSafeTriggerTime()
    {
        return Mathf.Max(0.1f, triggerTimeSeconds);
    }

    /// <summary>判断遭遇配置是否具备计时、首领数据与首领 Prefab。</summary>
    public bool IsValid => bossData != null && bossPrefab != null && GetSafeTriggerTime() > 0f;
}
