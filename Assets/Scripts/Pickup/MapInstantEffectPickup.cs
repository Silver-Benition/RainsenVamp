using UnityEngine;

/// <summary>
/// 通用池化地图即时效果拾取物。
/// 地图道具不参与磁吸，仅在玩家本体进入近身触发范围后应用效果；
/// 效果配置和结果统计分别由 MapInstantEffectSO 与报告器负责。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D), typeof(MapInstantEffectPickupReporter))]
public sealed class MapInstantEffectPickup : MonoBehaviour, IPoolable
{
    private GameObject _prefabReference;
    private MapInstantEffectPickupReporter _reporter;
    private bool _consumed;

    /// <summary>当前 Prefab 绑定的拾取物数据。</summary>
    public MapInstantEffectPickupDataSO PickupData =>
        _reporter != null ? _reporter.PickupData : null;

    /// <summary>当前池生命周期是否已被玩家消费。</summary>
    public bool IsConsumed => _consumed;

    /// <summary>缓存单次报告器，避免物理回调中重复查找组件。</summary>
    private void Awake()
    {
        _reporter = GetComponent<MapInstantEffectPickupReporter>();
    }

    /// <summary>保存对象池使用的原始 Prefab 键。</summary>
    public void SetPrefabReference(GameObject prefab)
    {
        _prefabReference = prefab;
    }

    /// <summary>池取出时开启新的可消费生命周期。</summary>
    private void OnEnable()
    {
        _consumed = false;
        if (_reporter == null)
        {
            _reporter = GetComponent<MapInstantEffectPickupReporter>();
        }
    }

    /// <summary>
    /// 只有碰到同对象持有 PlayerStats 的正式玩家本体才消费，避免 MagnetRadius 子物体提前触发效果。
    /// 满血治疗等未实际生效的情况仍消费世界物体，但不会写入成功统计。
    /// </summary>
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (_consumed || collision == null || !collision.TryGetComponent(out PlayerStats playerStats))
        {
            return;
        }

        _consumed = true;
        MapInstantEffectPickupDataSO data = PickupData;
        MapInstantEffectSO effect = data != null ? data.Effect : null;
        PlayerHealth playerHealth = playerStats.GetComponent<PlayerHealth>();
        bool applied = effect != null && effect.TryApply(
            new MapInstantEffectContext(playerStats, playerHealth));
        if (applied && _reporter != null)
        {
            _reporter.ReportEffectApplied();
        }

        ReleaseToPool();
    }

    /// <summary>通过原始 Prefab 键归还对象池；缺少池依赖时禁用对象作为安全降级。</summary>
    private void ReleaseToPool()
    {
        if (PoolManager.Instance != null && _prefabReference != null)
        {
            PoolManager.Instance.Release(_prefabReference, gameObject);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    /// <summary>回池时清除消费守卫，下一次 OnEnable 将建立全新生命周期。</summary>
    private void OnDisable()
    {
        _consumed = false;
    }
}
