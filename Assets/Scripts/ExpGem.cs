using UnityEngine;

[RequireComponent(typeof(Collider2D), typeof(MagneticPickupMotion))]
public class ExpGem : MonoBehaviour, IPoolable
{
    [Header("配置")]
    public float expValue = 1f;       // 提供的经验值

    private GameObject prefabReference;
    private bool _consumed;
    public int RoundMaterialValue => Mathf.Max(1, Mathf.RoundToInt(expValue));

    /// <summary>池取出时恢复一次性拾取标志。</summary>
    private void OnEnable() { _consumed = false; }

    /// <summary>保存对象池使用的原始经验球 Prefab 键。</summary>
    public void SetPrefabReference(GameObject prefab)
    {
        prefabReference = prefab;
    }

    /// <summary>碰到玩家本体时授予经验并归还对象池。</summary>
    private void OnTriggerEnter2D(Collider2D collision)
    {
        // 只有碰到挂有 PlayerStats 的物体（即玩家本体），才会被吸收
        if (!_consumed && RoundController.AllowsCombat && collision.TryGetComponent<PlayerStats>(out var stats))
        {
            _consumed = true;
            if (RoundController.Enabled) RoundController.Instance.CollectMaterial(RoundMaterialValue);
            else stats.AddExp(expValue);
            PoolManager.Instance.Release(prefabReference, gameObject);
        }
    }
}
