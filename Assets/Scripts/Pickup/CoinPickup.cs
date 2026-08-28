using UnityEngine;

/// <summary>池化金币拾取物；磁吸移动属于表现，最终价值在碰到玩家时按 Greed 结算。</summary>
[RequireComponent(typeof(Collider2D))]
public sealed class CoinPickup : MonoBehaviour, IPoolable, IMagneticPickup
{
    [Header("磁吸移动")]
    [SerializeField, Min(0f)] private float baseFlySpeed = 5f;
    [SerializeField, Min(0f)] private float acceleration = 15f;

    private GameObject _prefabReference;
    private Transform _targetPlayer;
    private bool _isFlying;
    private float _currentSpeed;
    private int _baseValue = 1;

    /// <summary>保存对象池使用的原始金币 Prefab 键。</summary>
    public void SetPrefabReference(GameObject prefab)
    {
        _prefabReference = prefab;
    }

    /// <summary>敌人生成金币后注入基础价值；取整与 Greed 计算延迟到实际拾取。</summary>
    public void ConfigureValue(int baseValue)
    {
        _baseValue = Mathf.Max(1, baseValue);
    }

    /// <summary>池取出时清除上次磁吸目标和运行速度。</summary>
    private void OnEnable()
    {
        _targetPlayer = null;
        _isFlying = false;
        _currentSpeed = baseFlySpeed;
        _baseValue = 1;
    }

    /// <summary>开始向玩家飞行；已经飞行时忽略重复捕获。</summary>
    public void StartFlyingTowards(Transform player)
    {
        if (_isFlying || player == null)
        {
            return;
        }

        _targetPlayer = player;
        _isFlying = true;
    }

    /// <summary>仅在被磁吸后执行加速靠近，不对场上静止金币产生逐帧移动成本。</summary>
    private void Update()
    {
        if (!_isFlying || _targetPlayer == null)
        {
            return;
        }

        _currentSpeed += acceleration * Time.deltaTime;
        transform.position = Vector3.MoveTowards(
            transform.position,
            _targetPlayer.position,
            _currentSpeed * Time.deltaTime);
    }

    /// <summary>碰到玩家时按最终 Greed 结算金币，并归还对象池。</summary>
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!collision.TryGetComponent(out PlayerStats playerStats))
        {
            return;
        }

        int awardedGold = Mathf.Max(0, Mathf.RoundToInt(_baseValue * playerStats.Greed));
        RunState.GetOrCreate(playerStats)?.AddGold(awardedGold);
        ReleaseToPool();
    }

    /// <summary>通过原始 Prefab 键归还金币；缺少池依赖时禁用对象作为安全降级。</summary>
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
}
