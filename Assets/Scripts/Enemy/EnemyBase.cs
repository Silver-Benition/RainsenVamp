using UnityEngine;

/// <summary>
/// 池化敌人的生命、追踪移动、伤害输出与死亡掉落入口。
/// 每次生成消费 EnemySpawnSnapshot，避免热路径读取共享数据资产或遗留上一生命周期状态。
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class EnemyBase : MonoBehaviour, IDamageable, ICombatDamageTarget, IPoolable
{
    public EnemyDataSO enemyData;

    private float _currentHealth;
    private Transform _playerTransform;
    private PlayerStats _playerStats;
    private Rigidbody2D _rigidbody;
    private GameObject _prefabReference;
    private HitFlash _hitFlash;
    private EnemySpawnSnapshot _spawnSnapshot;
    private WorldEnemySimulation _worldSimulation;
    private SpriteRenderer[] _spriteRenderers;
    private Color[] _baseRendererColors;

    /// <summary>当前生命周期剩余生命，供调试和测试只读观察。</summary>
    public float CurrentHealth => _currentHealth;

    /// <summary>当前生命周期移动速度。</summary>
    public float CurrentMoveSpeed => _spawnSnapshot.MoveSpeed;

    /// <summary>当前生命周期玩家接触伤害。</summary>
    public float CurrentCollisionDamage => _spawnSnapshot.CollisionDamage;

    /// <summary>当前敌人是否被 Defang。</summary>
    public bool IsDefanged => _spawnSnapshot.IsDefanged;

    /// <summary>当前敌人绑定的世界是否允许它与玩家交互。</summary>
    public bool IsWorldInteractionEnabled =>
        _worldSimulation == null || _worldSimulation.IsWorldActive;

    /// <summary>当前敌人所属世界模拟器，供首领等特殊敌人生成世界归属实体。</summary>
    public WorldEnemySimulation WorldSimulation => _worldSimulation;

    /// <summary>缓存刚体、受击表现和 SpriteRenderer 原色，避免战斗热路径重复查找。</summary>
    protected virtual void Awake()
    {
        _rigidbody = GetComponent<Rigidbody2D>();
        _hitFlash = GetComponent<HitFlash>();
        if (_hitFlash == null)
        {
            _hitFlash = GetComponentInChildren<HitFlash>();
        }

        _spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        _baseRendererColors = new Color[_spriteRenderers.Length];
        for (int index = 0; index < _spriteRenderers.Length; index++)
        {
            _baseRendererColors[index] = _spriteRenderers[index].color;
        }
    }

    /// <summary>保存对象池使用的原始敌人 Prefab 键。</summary>
    public void SetPrefabReference(GameObject prefab)
    {
        _prefabReference = prefab;
    }

    /// <summary>绑定敌人的世界模拟器，防止跨世界池复用后产生伤害串线。</summary>
    public void SetWorldSimulation(WorldEnemySimulation simulation)
    {
        _worldSimulation = simulation;
    }

    /// <summary>对象池取出时恢复基础快照、动量、朝向和玩家目标。</summary>
    protected virtual void OnEnable()
    {
        ResetRuntimeSnapshot();
        if (_rigidbody != null)
        {
            _rigidbody.velocity = Vector2.zero;
            _rigidbody.angularVelocity = 0f;
        }

        transform.localScale = Vector3.one;
        ResolvePlayerTarget();
    }

    /// <summary>对象池回收时清空动量，并进入不可造成伤害的停用状态。</summary>
    protected virtual void OnDisable()
    {
        if (_rigidbody != null)
        {
            _rigidbody.velocity = Vector2.zero;
            _rigidbody.angularVelocity = 0f;
        }

        EnterInactivePoolState();
        _worldSimulation = null;
    }

    /// <summary>
    /// 注入本次生成的不可变属性快照。
    /// WorldWaveManager 或其他生成器应在 PoolManager.Spawn 返回后立即调用。
    /// </summary>
    public virtual void ApplySpawnSnapshot(EnemySpawnSnapshot snapshot)
    {
        _spawnSnapshot = snapshot;
        _currentHealth = snapshot.MaxHealth;
        ApplyDefangVisual(snapshot.IsDefanged);
    }

    /// <summary>
    /// 解析一次由本敌人发起的基础伤害。
    /// 远程攻击组件必须在创建投射物时调用，确保 Defang 同时约束近战和飞行道具。
    /// </summary>
    public virtual float ResolveOutgoingDamage(float baseDamage)
    {
        return _spawnSnapshot.ResolveOutgoingDamage(baseDamage);
    }

    /// <summary>
    /// 计算朝向玩家的期望速度并交给 Dynamic Rigidbody2D。
    /// Physics2D 随后求解 Enemy 间实体接触，使敌群保持追踪的同时互相滑开。
    /// </summary>
    protected virtual void FixedUpdate()
    {
        if (_rigidbody == null)
        {
            return;
        }

        Vector2 direction = GetMovementDirection();
        if (direction.sqrMagnitude <= Mathf.Epsilon)
        {
            _rigidbody.velocity = Vector2.zero;
            return;
        }

        _rigidbody.velocity = direction * _spawnSnapshot.MoveSpeed;

        if (direction.x != 0f)
        {
            transform.localScale = new Vector3(direction.x > 0f ? -1f : 1f, 1f, 1f);
        }
    }

    /// <summary>获取普通敌人朝向玩家的移动意图。</summary>
    protected virtual Vector2 GetMovementDirection()
    {
        if (_playerTransform == null)
        {
            return Vector2.zero;
        }

        return (_playerTransform.position - transform.position).normalized;
    }

    /// <summary>确保敌人已经缓存当前玩家目标。</summary>
    protected void ResolvePlayerTarget()
    {
        if (_playerTransform != null)
        {
            return;
        }

        GameObject player = GameObject.FindWithTag("Player");
        if (player == null)
        {
            return;
        }

        _playerTransform = player.transform;
        _playerStats = player.GetComponent<PlayerStats>();
    }

    /// <summary>获取当前缓存的玩家目标。</summary>
    protected Transform PlayerTransform => _playerTransform;

    /// <summary>持续接触玩家时请求当前快照伤害；Defang 敌人的值固定为零。</summary>
    private void OnCollisionStay2D(Collision2D collision)
    {
        // Unity 可能在同一物理步内把碰撞消息送达刚回池的组件。
        // 同时检查组件状态与快照，防止停用对象读取下一生命周期的基础伤害。
        if (!isActiveAndEnabled || !IsWorldInteractionEnabled || _spawnSnapshot.IsDefanged ||
            _spawnSnapshot.CollisionDamage <= 0f)
        {
            return;
        }

        if (DamageTargetFilter.TryGetPlayerDamageable(collision.collider, out IDamageable player))
        {
            player.TakeDamage(_spawnSnapshot.CollisionDamage);
        }
    }

    /// <summary>承受普通伤害，转发到包含暴击标记的统一入口。</summary>
    public void TakeDamage(float damage)
    {
        TakeDamage(damage, false);
    }

    /// <summary>兼容旧 IDamageable 调用，执行实际扣血但不额外重复记账。</summary>
    public void TakeDamage(float damage, bool isCritical)
    {
        ApplyCombatDamage(damage, isCritical);
    }

    /// <summary>
    /// 扣除生命并返回实际损失值；过量伤害只按剩余生命计入武器统计。
    /// </summary>
    public CombatDamageResult ApplyCombatDamage(float damage, bool isCritical)
    {
        float safeDamage = RunResultValueSanitizer.SanitizeNonNegative(damage);
        // 回池后的对象已恢复下一生命周期基础生命，但在再次 Spawn 前必须拒绝所有外部伤害。
        if (!isActiveAndEnabled || safeDamage <= 0f || _currentHealth <= 0f ||
            (RunDirector.Instance != null && RunDirector.Instance.IsResultFrozen))
        {
            return new CombatDamageResult(safeDamage, 0f, false, _currentHealth <= 0f);
        }

        float previousHealth = _currentHealth;
        float appliedDamage = Mathf.Min(previousHealth, safeDamage);
        _currentHealth = Mathf.Max(0f, previousHealth - appliedDamage);
        if (_hitFlash != null)
        {
            _hitFlash.TriggerFlash();
        }

        if (DamagePopupManager.Instance != null)
        {
            DamagePopupManager.Instance.Show(appliedDamage, transform.position, isCritical);
        }

        bool targetDefeated = _currentHealth <= 0f;
        if (targetDefeated)
        {
            Die();
        }

        return new CombatDamageResult(safeDamage, appliedDamage, true, targetDefeated);
    }

    /// <summary>登记击杀、生成经验与概率掉落，并把敌人归还对应对象池。</summary>
    protected virtual void Die()
    {
        RegisterKillForDefeat();

        if (PoolManager.Instance != null && enemyData != null)
        {
            SpawnPooledDrop(enemyData.dropExpPrefab);
            SpawnAdditionalDrops(enemyData.dropTable);
        }

        ReleaseToPool();
    }

    /// <summary>将一次正式敌人或首领死亡计为一击杀，兼容旧测试中的 RunStatsUI 后备路径。</summary>
    protected void RegisterKillForDefeat()
    {
        if (RunState.Instance != null)
        {
            RunState.Instance.RegisterKill();
        }
        else if (RunStatsUI.Instance != null)
        {
            RunStatsUI.Instance.RegisterKill();
        }
    }

    /// <summary>归还当前敌人的对象池；缺失池引用时仅禁用以隔离迟到物理回调。</summary>
    protected void ReleaseToPool()
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

    /// <summary>按 Luck 独立判定金币和宝箱，并在成功时从对象池生成。</summary>
    private void SpawnAdditionalDrops(EnemyDropTableSO dropTable)
    {
        if (dropTable == null)
        {
            return;
        }

        float luck = _playerStats != null ? _playerStats.Luck : 1f;
        if (dropTable.coinPrefab != null && DropChanceResolver.ShouldDrop(
                dropTable.baseCoinChance,
                luck,
                Random.value))
        {
            GameObject coinObject = SpawnPooledDrop(dropTable.coinPrefab);
            if (coinObject != null && coinObject.TryGetComponent(out CoinPickup coin))
            {
                coin.ConfigureValue(dropTable.coinBaseValue);
            }
        }

        if (dropTable.chestPrefab != null && DropChanceResolver.ShouldDrop(
                dropTable.baseChestChance,
                luck,
                Random.value))
        {
            SpawnPooledDrop(dropTable.chestPrefab);
        }
    }

    /// <summary>在敌人当前位置生成一个可选池化掉落，并返回实例供额外初始化。</summary>
    private GameObject SpawnPooledDrop(GameObject prefab)
    {
        return prefab != null && PoolManager.Instance != null
            ? PoolManager.Instance.Spawn(prefab, transform.position, Quaternion.identity)
            : null;
    }

    /// <summary>恢复 EnemyDataSO 基础值和原始颜色，作为生成器注入前的安全状态。</summary>
    private void ResetRuntimeSnapshot()
    {
        _spawnSnapshot = EnemySpawnSnapshotFactory.Create(enemyData, 1f, 0f, 1f);
        _currentHealth = _spawnSnapshot.MaxHealth;
        ApplyDefangVisual(false);
    }

    /// <summary>回池后保留基础生命但清零移动和全部输出，隔离延迟物理回调。</summary>
    private void EnterInactivePoolState()
    {
        float baseHealth = enemyData != null ? enemyData.maxHealth : 1f;
        _spawnSnapshot = new EnemySpawnSnapshot(baseHealth, 0f, 0f, 0f, false);
        _currentHealth = _spawnSnapshot.MaxHealth;
        ApplyDefangVisual(false);
    }

    /// <summary>应用或清除 Defang 绿色提示，逐个使用实例初始颜色避免污染共享材质。</summary>
    private void ApplyDefangVisual(bool defanged)
    {
        if (_spriteRenderers == null || _baseRendererColors == null)
        {
            return;
        }

        for (int index = 0; index < _spriteRenderers.Length; index++)
        {
            SpriteRenderer renderer = _spriteRenderers[index];
            if (renderer == null)
            {
                continue;
            }

            Color baseColor = _baseRendererColors[index];
            Color defangColor = new Color(0.35f, 1f, 0.42f, baseColor.a);
            renderer.color = defanged ? Color.Lerp(baseColor, defangColor, 0.65f) : baseColor;
        }
    }
}
