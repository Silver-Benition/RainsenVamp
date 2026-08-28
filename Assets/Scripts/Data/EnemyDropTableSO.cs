using UnityEngine;

/// <summary>单类敌人的低频掉落配置；所有 Prefab 必须可由 PoolManager 管理。</summary>
[CreateAssetMenu(fileName = "NewEnemyDropTable", menuName = "GameData/Enemy Drop Table")]
public sealed class EnemyDropTableSO : ScriptableObject
{
    [Header("金币")]
    [Tooltip("金币拾取物 Prefab；为空时禁用金币掉落。")]
    public GameObject coinPrefab;

    [Range(0f, 1f), Tooltip("Luck=1 时单个敌人的金币掉落概率。")]
    public float baseCoinChance = 0.12f;

    [Min(1), Tooltip("单枚金币在 Greed=1 时提供的基础价值。")]
    public int coinBaseValue = 1;

    [Header("宝箱")]
    [Tooltip("宝箱拾取物 Prefab；为空时禁用宝箱掉落。")]
    public GameObject chestPrefab;

    [Range(0f, 1f), Tooltip("Luck=1 时单个敌人的宝箱掉落概率。")]
    public float baseChestChance = 0.01f;
}
