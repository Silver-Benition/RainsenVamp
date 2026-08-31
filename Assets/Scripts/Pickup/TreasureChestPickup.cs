using UnityEngine;

/// <summary>池化宝箱拾取物；拾取后请求升级系统即时授予一个合法武器奖励。</summary>
[RequireComponent(typeof(Collider2D))]
public sealed class TreasureChestPickup : MonoBehaviour, IPoolable
{
    [Header("拾取保护")]
    [SerializeField, Min(0f)] private float pickupProtectionDuration = 0.5f;

    [Header("表现")]
    [SerializeField] private GameObject openVfxPrefab;

    private GameObject _prefabReference;
    private Collider2D _pickupCollider;
    private float _protectionRemaining;
    private bool _consumed;

    /// <summary>当前触发器是否已经结束保护并允许玩家拾取。</summary>
    public bool IsPickupArmed => _pickupCollider != null && _pickupCollider.enabled;

    /// <summary>缓存触发器，避免池化生命周期与逐帧保护阶段重复查询组件。</summary>
    private void Awake()
    {
        _pickupCollider = GetComponent<Collider2D>();
    }

    /// <summary>保存对象池使用的原始宝箱 Prefab 键。</summary>
    public void SetPrefabReference(GameObject prefab)
    {
        _prefabReference = prefab;
    }

    /// <summary>
    /// 池取出时清除上次状态并暂时关闭触发器。
    /// 即使宝箱生成在玩家碰撞体内部，也会先完整展示保护时间再允许拾取。
    /// </summary>
    private void OnEnable()
    {
        if (_pickupCollider == null)
        {
            _pickupCollider = GetComponent<Collider2D>();
        }

        _consumed = false;
        _protectionRemaining = Mathf.Max(0f, pickupProtectionDuration);
        _pickupCollider.enabled = _protectionRemaining <= 0f;
    }

    /// <summary>
    /// 以游戏时间推进低频拾取保护；暂停期间不消耗保护时间。
    /// 仅在保护尚未结束时写一次 Collider 状态，不产生逐帧托管分配。
    /// </summary>
    private void Update()
    {
        if (_consumed || _pickupCollider == null || _pickupCollider.enabled)
        {
            return;
        }

        _protectionRemaining -= Time.deltaTime;
        if (_protectionRemaining <= 0f)
        {
            _protectionRemaining = 0f;
            _pickupCollider.enabled = true;
        }
    }

    /// <summary>玩家接触后申请一次即时奖励；没有合法奖励时仍回收宝箱，避免重复碰撞。</summary>
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (_consumed || !collision.TryGetComponent(out PlayerStats _))
        {
            return;
        }

        _consumed = true;
        if (LevelUpManager.Instance != null)
        {
            UpgradeDataSO reward = LevelUpManager.Instance.GrantRandomChestReward();
            if (reward != null)
            {
                Debug.Log($"[TreasureChest] 获得奖励：{reward.GetStableId()}", this);
                PlayOpenVfx();
            }
        }

        ReleaseToPool();
    }

    /// <summary>从对象池生成一次宝箱开启爆闪；配置缺失时静默跳过，不影响奖励结算。</summary>
    private void PlayOpenVfx()
    {
        if (openVfxPrefab == null || PoolManager.Instance == null)
        {
            return;
        }

        GameObject vfxObject = PoolManager.Instance.Spawn(
            openVfxPrefab,
            transform.position,
            Quaternion.identity);
        if (vfxObject != null && vfxObject.TryGetComponent(out PooledSpriteBurstVfx burstVfx))
        {
            burstVfx.Play();
            return;
        }

        if (vfxObject != null)
        {
            PoolManager.Instance.Release(openVfxPrefab, vfxObject);
        }
    }

    /// <summary>通过原始 Prefab 键归还宝箱；缺少池依赖时禁用对象作为安全降级。</summary>
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

    /// <summary>回池时关闭触发器并清空保护状态，防止旧生命周期穿透到下一次生成。</summary>
    private void OnDisable()
    {
        _consumed = false;
        _protectionRemaining = 0f;
        if (_pickupCollider != null)
        {
            _pickupCollider.enabled = false;
        }
    }

    /// <summary>在 Inspector 修改时钳制非法保护时间。</summary>
    private void OnValidate()
    {
        pickupProtectionDuration = Mathf.Max(0f, pickupProtectionDuration);
    }
}
