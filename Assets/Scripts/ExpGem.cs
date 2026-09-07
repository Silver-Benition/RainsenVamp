using UnityEngine;

[RequireComponent(typeof(Collider2D), typeof(MagneticPickupMotion))]
public class ExpGem : MonoBehaviour, IPoolable
{
    [Header("配置")]
    public float expValue = 1f;       // 提供的经验值

    private GameObject prefabReference;

    /// <summary>保存对象池使用的原始经验球 Prefab 键。</summary>
    public void SetPrefabReference(GameObject prefab)
    {
        prefabReference = prefab;
    }

    /// <summary>碰到玩家本体时授予经验并归还对象池。</summary>
    private void OnTriggerEnter2D(Collider2D collision)
    {
        // 只有碰到挂有 PlayerStats 的物体（即玩家本体），才会被吸收
        if (collision.TryGetComponent<PlayerStats>(out var stats))
        {
            stats.AddExp(expValue);
            PoolManager.Instance.Release(prefabReference, gameObject);
        }
    }
}
