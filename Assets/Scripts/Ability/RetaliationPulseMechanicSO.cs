using System.Collections.Generic;
using UnityEngine;

/// <summary>反击脉冲单级的冷却、范围与基础伤害配置。</summary>
[System.Serializable]
public sealed class RetaliationPulseLevelConfig
{
    [Min(0.01f)] public float cooldown = 8f;
    [Min(0.01f)] public float radius = 2.5f;
    [Min(0f)] public float baseDamage = 20f;
}

/// <summary>
/// 玩家受到一次实际非致命伤害后触发范围反击的机制配置。
/// 使用预分配 Collider2D 数组和复用 HashSet 去重多碰撞体敌人，不调用 OverlapCircleAll。
/// </summary>
[CreateAssetMenu(fileName = "RetaliationPulseMechanic", menuName = "GameData/Ability Mechanics/Retaliation Pulse")]
public sealed class RetaliationPulseMechanicSO : AbilityMechanicSO
{
    public List<RetaliationPulseLevelConfig> levelConfigs =
        new List<RetaliationPulseLevelConfig>
        {
            new RetaliationPulseLevelConfig()
        };

    [Header("查询与表现")]
    [Min(8), Tooltip("单次脉冲可读取的碰撞体上限；数组仅在能力获得时分配一次。")]
    public int overlapCapacity = 128;
    [Tooltip("对象池生成的纯表现 Prefab；为空不影响伤害结算。")]
    public GameObject pulseVfxPrefab;

    /// <summary>创建事件驱动的反击运行时。</summary>
    public override IAbilityMechanicRuntime CreateRuntime(
        AbilityRuntimeContext context,
        AbilityDataSO abilityData,
        int initialLevel)
    {
        if (context == null || context.Owner == null || context.PlayerHealth == null ||
            context.PlayerStats == null || abilityData == null)
        {
            return null;
        }

        return new Runtime(this, context, initialLevel);
    }

    /// <summary>安全读取从 1 开始的机制等级配置。</summary>
    private RetaliationPulseLevelConfig GetConfig(int level)
    {
        if (levelConfigs == null || levelConfigs.Count == 0)
        {
            return new RetaliationPulseLevelConfig();
        }

        return levelConfigs[Mathf.Clamp(level - 1, 0, levelConfigs.Count - 1)];
    }

    /// <summary>保存单局冷却、预分配查询缓冲和事件订阅。</summary>
    private sealed class Runtime : IAbilityMechanicRuntime
    {
        private readonly RetaliationPulseMechanicSO _configSource;
        private readonly AbilityRuntimeContext _context;
        private readonly Collider2D[] _overlapResults;
        private readonly HashSet<IDamageable> _damagedTargets = new HashSet<IDamageable>();
        private ContactFilter2D _contactFilter;
        private int _level;
        private float _nextAllowedTime;
        private bool _disposed;

        /// <summary>建立非分配物理查询配置并订阅实际受伤事件。</summary>
        public Runtime(
            RetaliationPulseMechanicSO configSource,
            AbilityRuntimeContext context,
            int initialLevel)
        {
            _configSource = configSource;
            _context = context;
            _level = Mathf.Max(1, initialLevel);
            _overlapResults = new Collider2D[Mathf.Max(8, configSource.overlapCapacity)];
            _contactFilter = new ContactFilter2D();
            _contactFilter.useLayerMask = true;
            _contactFilter.SetLayerMask(DamageTargetFilter.EnemyLayerMask);
            _contactFilter.useTriggers = true;
            _context.PlayerHealth.Damaged += HandlePlayerDamaged;
        }

        /// <summary>切换后续脉冲使用的等级，不重置已经开始的冷却。</summary>
        public void SetLevel(int level)
        {
            _level = Mathf.Max(1, level);
        }

        /// <summary>解除受伤事件并清理复用集合。</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _context.PlayerHealth.Damaged -= HandlePlayerDamaged;
            _damagedTargets.Clear();
        }

        /// <summary>
        /// 处理一次实际受伤：致命伤害和冷却期请求不触发；合法请求只执行一次范围查询。
        /// 冷却使用 Time.time，因此暂停时与游戏战斗时间一起停止。
        /// </summary>
        private void HandlePlayerDamaged(float appliedDamage)
        {
            if (_disposed || appliedDamage <= 0f || _context.PlayerHealth.CurrentHealth <= 0f ||
                Time.time < _nextAllowedTime)
            {
                return;
            }

            RetaliationPulseLevelConfig config = _configSource.GetConfig(_level);
            _nextAllowedTime = Time.time + Mathf.Max(0.01f, config.cooldown);
            float finalDamage = Mathf.Max(0f, config.baseDamage) * _context.PlayerStats.Might;
            ApplyPulseDamage(config.radius, finalDamage);
            SpawnPulseVfx(config.radius);
        }

        /// <summary>
        /// 使用预分配数组查询 Enemy Layer，并按 IDamageable 引用去重多 Collider 敌人。
        /// 查询数组溢出时只处理当前容量，避免战斗中扩容和 GC。
        /// </summary>
        private void ApplyPulseDamage(float radius, float damage)
        {
            if (damage <= 0f || DamageTargetFilter.EnemyLayerMask == 0)
            {
                return;
            }

            int hitCount = Physics2D.OverlapCircle(
                _context.Owner.position,
                Mathf.Max(0.01f, radius),
                _contactFilter,
                _overlapResults);

            _damagedTargets.Clear();
            for (int index = 0; index < hitCount; index++)
            {
                Collider2D candidate = _overlapResults[index];
                if (!DamageTargetFilter.TryGetEnemyDamageable(candidate, out IDamageable damageable) ||
                    !_damagedTargets.Add(damageable))
                {
                    continue;
                }

                damageable.TakeDamage(damage);
            }

            // 立即清除接口引用，避免池中敌人回收后仍被本能力意外持有。
            _damagedTargets.Clear();
        }

        /// <summary>通过现有 PoolManager 请求纯表现脉冲；缺失池或 Prefab 时静默跳过。</summary>
        private void SpawnPulseVfx(float radius)
        {
            if (_configSource.pulseVfxPrefab == null || PoolManager.Instance == null)
            {
                return;
            }

            GameObject instance = PoolManager.Instance.Spawn(
                _configSource.pulseVfxPrefab,
                _context.Owner.position,
                Quaternion.identity);
            if (instance != null && instance.TryGetComponent(out AbilityPulseVfx pulseVfx))
            {
                pulseVfx.Play(radius);
            }
        }
    }
}
