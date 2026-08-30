using UnityEngine;

/// <summary>
/// 远程敌人使用的池化飞行道具。
/// 发射时复制来源敌人的最终伤害，Defang 敌人发射的投射物因此始终保持 0 伤害。
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public sealed class EnemyProjectile : MonoBehaviour, IPoolable
{
    [SerializeField, Min(0.01f)] private float lifetime = 6f;

    private Rigidbody2D _rigidbody;
    private GameObject _prefabReference;
    private WorldEnemySimulation _worldSimulation;
    private int _defaultLayer = -1;
    private float _remainingLifetime;
    private float _resolvedDamage;

    /// <summary>当前投射物已经快照化的实际伤害。</summary>
    public float ResolvedDamage => _resolvedDamage;

    /// <summary>当前投射物剩余寿命，供运行时诊断和确定性测试观察。</summary>
    public float RemainingLifetime => _remainingLifetime;

    /// <summary>缓存刚体引用，避免飞行与碰撞热路径查找组件。</summary>
    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody2D>();
        _defaultLayer = LayerMask.NameToLayer("Default");
    }

    /// <summary>保存对象池使用的原始投射物 Prefab 键。</summary>
    public void SetPrefabReference(GameObject prefab)
    {
        _prefabReference = prefab;
    }

    /// <summary>绑定投射物所属世界，避免共享对象池跨世界伤害玩家。</summary>
    public void SetWorldSimulation(WorldEnemySimulation simulation)
    {
        _worldSimulation = simulation;
    }

    /// <summary>池取出时先进入零伤害安全状态，等待远程敌人在同一生成调用中执行 Launch。</summary>
    private void OnEnable()
    {
        _resolvedDamage = 0f;
        _remainingLifetime = Mathf.Max(0.01f, lifetime);
        if (_rigidbody == null)
        {
            _rigidbody = GetComponent<Rigidbody2D>();
        }

        if (_rigidbody != null)
        {
            _rigidbody.velocity = Vector2.zero;
            _rigidbody.angularVelocity = 0f;
        }
    }

    /// <summary>回池时清空动量和伤害，防止下一生命周期继承旧发射者状态。</summary>
    private void OnDisable()
    {
        _resolvedDamage = 0f;
        if (_rigidbody != null)
        {
            _rigidbody.velocity = Vector2.zero;
            _rigidbody.angularVelocity = 0f;
        }

        _worldSimulation = null;
    }

    /// <summary>
    /// 使用来源敌人的生成快照发射投射物。
    /// 调用方应先从 PoolManager 取出对象，再在同一方法栈内立即调用本方法。
    /// </summary>
    public void Launch(Vector2 velocity, float baseDamage, EnemyBase sourceEnemy)
    {
        Launch(velocity, baseDamage, sourceEnemy, lifetime);
    }

    /// <summary>
    /// 使用来源敌人的生成快照和指定寿命发射投射物。
    /// </summary>
    public void Launch(
        Vector2 velocity,
        float baseDamage,
        EnemyBase sourceEnemy,
        float lifetimeOverride)
    {
        _resolvedDamage = sourceEnemy != null
            ? sourceEnemy.ResolveOutgoingDamage(baseDamage)
            : Mathf.Max(0f, baseDamage);
        _remainingLifetime = Mathf.Max(0.01f, lifetimeOverride);
        if (_rigidbody != null)
        {
            _rigidbody.velocity = velocity;
        }
    }

    /// <summary>更新寿命并在超时后回收，避免远程弹体永久占用池实例。</summary>
    private void Update()
    {
        _remainingLifetime -= Time.deltaTime;
        if (_remainingLifetime <= 0f)
        {
            ReleaseToPool();
        }
    }

    /// <summary>
    /// 碰到玩家时请求快照伤害；正式受击体由 DamageTargetFilter 的 PlayerHurtbox 标记决定，
    /// 因此未来的正式 Trigger 受击框也能参与解析；即使伤害为零也回收弹体。
    /// </summary>
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (_worldSimulation != null && !_worldSimulation.IsWorldActive)
        {
            return;
        }

        if (collision != null && _defaultLayer >= 0 && collision.gameObject.layer == _defaultLayer)
        {
            ReleaseToPool();
            return;
        }

        if (!DamageTargetFilter.TryGetPlayerDamageable(collision, out IDamageable player))
        {
            return;
        }

        if (_resolvedDamage > 0f)
        {
            player.TakeDamage(_resolvedDamage);
        }

        ReleaseToPool();
    }

    /// <summary>归还投射物对象池；测试对象缺少池时使用禁用作为安全降级。</summary>
    private void ReleaseToPool()
    {
        if (!gameObject.activeSelf)
        {
            return;
        }

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
