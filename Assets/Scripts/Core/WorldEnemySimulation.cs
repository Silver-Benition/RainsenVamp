using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 世界线敌人运行器。
/// 负责从对象池取出本世界敌人、缓存表现/碰撞组件，并切换当前世界的交互状态。
/// </summary>
public class WorldEnemySimulation : MonoBehaviour
{
    private sealed class TrackedEnemy
    {
        public GameObject instance;
        public EnemyBase enemyBase;
        public Collider2D[] colliders;
        public Renderer[] renderers;
    }

    private sealed class TrackedProjectile
    {
        public GameObject instance;
        public EnemyProjectile projectile;
        public Collider2D[] colliders;
        public Renderer[] renderers;
    }

    [Header("世界配置")]
    [SerializeField] private WorldLineDataSO worldLine;
    [SerializeField] private Transform target;
    [SerializeField] private Transform entityRoot;

    private readonly List<TrackedEnemy> trackedEnemies = new List<TrackedEnemy>();
    private readonly List<TrackedProjectile> trackedProjectiles = new List<TrackedProjectile>();
    private static readonly Dictionary<GameObject, WorldEnemySimulation> EntityOwners =
        new Dictionary<GameObject, WorldEnemySimulation>();
    private bool worldActive;

    /// <summary>当前世界使用的静态配置。</summary>
    public WorldLineDataSO WorldLine => worldLine;

    /// <summary>当前世界池中仍处于激活状态的敌人数。</summary>
    public int ActiveEnemyCount => CountActiveEnemies();

    /// <summary>当前世界池中仍处于激活状态的投射物数量。</summary>
    public int ActiveProjectileCount => CountActiveProjectiles();

    /// <summary>当前世界是否允许已归属实体与玩家交互。</summary>
    public bool IsWorldActive => worldActive;

    /// <summary>缓存玩家目标和实体父节点。</summary>
    private void Awake()
    {
        if (target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) target = player.transform;
        }

        if (entityRoot == null) entityRoot = transform;
    }

    /// <summary>校验依赖；敌人由 WorldWaveManager 在运行时按规则生成。</summary>
    private void Start()
    {
        ValidateSetup();
    }

    /// <summary>移除本世界对共享池实例的所有权记录。</summary>
    private void OnDestroy()
    {
        for (int i = 0; i < trackedEnemies.Count; i++)
        {
            RemoveOwnership(trackedEnemies[i] != null ? trackedEnemies[i].instance : null);
        }

        for (int i = 0; i < trackedProjectiles.Count; i++)
        {
            RemoveOwnership(trackedProjectiles[i] != null ? trackedProjectiles[i].instance : null);
        }
    }

    /// <summary>设置当前世界是否允许敌人表现和物理交互。</summary>
    public void SetWorldActive(bool active)
    {
        worldActive = active;
        ApplyWorldState();
    }

    /// <summary>
    /// 从对象池生成并注册一个敌人。
    /// 只在生成时执行组件缓存和回收通知绑定，不进入每帧热路径。
    /// </summary>
    public GameObject SpawnEnemy(
        GameObject prefab,
        Vector3 position,
        WorldWaveManager owner,
        int ruleIndex,
        EnemySpawnSnapshot snapshot)
    {
        if (prefab == null || owner == null || PoolManager.Instance == null) return null;

        GameObject enemy = PoolManager.Instance.Spawn(prefab, position, Quaternion.identity);
        if (enemy == null) return null;

        enemy.transform.SetParent(entityRoot, true);
        TrackedEnemy tracked = FindTrackedEnemy(enemy);
        if (tracked == null)
        {
            tracked = new TrackedEnemy
            {
                instance = enemy,
                enemyBase = enemy.GetComponent<EnemyBase>(),
                colliders = enemy.GetComponentsInChildren<Collider2D>(true),
                renderers = enemy.GetComponentsInChildren<Renderer>(true)
            };
            trackedEnemies.Add(tracked);
        }

        if (tracked.enemyBase != null)
        {
            tracked.enemyBase.SetWorldSimulation(this);
            tracked.enemyBase.ApplySpawnSnapshot(snapshot);
        }

        RangedEnemyController rangedEnemy = enemy.GetComponent<RangedEnemyController>();
        if (rangedEnemy != null)
        {
            rangedEnemy.BindWorldSimulation(this);
        }

        WaveSpawnedNotifier notifier = enemy.GetComponent<WaveSpawnedNotifier>();
        if (notifier == null) notifier = enemy.AddComponent<WaveSpawnedNotifier>();
        notifier.EnableTracking(owner, ruleIndex);
        ClaimOwnership(enemy);
        ApplyWorldState(tracked);
        return enemy;
    }

    /// <summary>
    /// 从共享对象池生成并注册一个世界专属投射物。
    /// 投射物的运动和寿命不随世界显示状态暂停，但非当前世界会关闭渲染器与碰撞器。
    /// </summary>
    public GameObject SpawnProjectile(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        Vector2 velocity,
        float baseDamage,
        EnemyBase sourceEnemy,
        float projectileLifetime)
    {
        if (prefab == null || PoolManager.Instance == null)
        {
            return null;
        }

        GameObject projectileInstance = PoolManager.Instance.Spawn(prefab, position, rotation);
        if (projectileInstance == null)
        {
            return null;
        }

        projectileInstance.transform.SetParent(entityRoot, true);
        EnemyProjectile projectile = projectileInstance.GetComponent<EnemyProjectile>();
        if (projectile == null)
        {
            PoolManager.Instance.Release(prefab, projectileInstance);
            return null;
        }

        TrackedProjectile tracked = FindTrackedProjectile(projectileInstance);
        if (tracked == null)
        {
            tracked = new TrackedProjectile
            {
                instance = projectileInstance,
                projectile = projectile,
                colliders = projectileInstance.GetComponentsInChildren<Collider2D>(true),
                renderers = projectileInstance.GetComponentsInChildren<Renderer>(true)
            };
            trackedProjectiles.Add(tracked);
        }

        ClaimOwnership(projectileInstance);
        projectile.SetWorldSimulation(this);
        projectile.Launch(velocity, baseDamage, sourceEnemy, projectileLifetime);
        ApplyWorldState(tracked);
        return projectileInstance;
    }

    /// <summary>查找已缓存的池实例，避免同一实例重复加入跟踪列表。</summary>
    private TrackedEnemy FindTrackedEnemy(GameObject instance)
    {
        for (int i = 0; i < trackedEnemies.Count; i++)
        {
            if (trackedEnemies[i].instance == instance) return trackedEnemies[i];
        }
        return null;
    }

    /// <summary>查找已缓存的池化投射物，避免同一实例重复加入世界跟踪列表。</summary>
    private TrackedProjectile FindTrackedProjectile(GameObject instance)
    {
        for (int i = 0; i < trackedProjectiles.Count; i++)
        {
            if (trackedProjectiles[i].instance == instance) return trackedProjectiles[i];
        }

        return null;
    }

    /// <summary>校验世界线、玩家和对象池是否可用。</summary>
    private bool ValidateSetup()
    {
        bool valid = true;
        if (worldLine == null || !worldLine.IsValid)
        {
            Debug.LogError("WorldEnemySimulation 缺少有效世界线配置。", this);
            valid = false;
        }
        if (target == null)
        {
            Debug.LogError("WorldEnemySimulation 缺少玩家跟踪目标。", this);
            valid = false;
        }
        if (PoolManager.Instance == null)
        {
            Debug.LogError("WorldEnemySimulation 找不到 PoolManager。", this);
            valid = false;
        }
        return valid;
    }

    /// <summary>刷新全部已注册敌人的渲染器和碰撞器状态。</summary>
    private void ApplyWorldState()
    {
        for (int i = 0; i < trackedEnemies.Count; i++) ApplyWorldState(trackedEnemies[i]);
        for (int i = 0; i < trackedProjectiles.Count; i++) ApplyWorldState(trackedProjectiles[i]);
    }

    /// <summary>刷新单个敌人的世界交互状态。</summary>
    private void ApplyWorldState(TrackedEnemy enemy)
    {
        if (enemy == null || !IsOwnedByThisWorld(enemy.instance)) return;
        bool active = worldActive && enemy.instance.activeInHierarchy;
        for (int i = 0; i < enemy.colliders.Length; i++) enemy.colliders[i].enabled = active;
        for (int i = 0; i < enemy.renderers.Length; i++) enemy.renderers[i].enabled = active;
    }

    /// <summary>刷新单个投射物的世界交互状态。</summary>
    private void ApplyWorldState(TrackedProjectile projectile)
    {
        if (projectile == null || !IsOwnedByThisWorld(projectile.instance)) return;
        bool active = worldActive && projectile.instance.activeInHierarchy;
        for (int i = 0; i < projectile.colliders.Length; i++) projectile.colliders[i].enabled = active;
        for (int i = 0; i < projectile.renderers.Length; i++) projectile.renderers[i].enabled = active;
    }

    /// <summary>统计当前世界池中激活的敌人，不跨世界扫描。</summary>
    private int CountActiveEnemies()
    {
        int count = 0;
        for (int i = 0; i < trackedEnemies.Count; i++)
        {
            if (trackedEnemies[i].instance != null &&
                IsOwnedByThisWorld(trackedEnemies[i].instance) &&
                trackedEnemies[i].instance.activeInHierarchy)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>统计当前世界仍处于激活状态的投射物，不跨世界扫描。</summary>
    private int CountActiveProjectiles()
    {
        int count = 0;
        for (int i = 0; i < trackedProjectiles.Count; i++)
        {
            if (trackedProjectiles[i].instance != null &&
                IsOwnedByThisWorld(trackedProjectiles[i].instance) &&
                trackedProjectiles[i].instance.activeInHierarchy)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>将共享池实例声明为当前世界的唯一运行时所有者。</summary>
    private void ClaimOwnership(GameObject instance)
    {
        if (instance != null)
        {
            EntityOwners[instance] = this;
        }
    }

    /// <summary>检查共享池实例是否仍由当前世界拥有。</summary>
    private bool IsOwnedByThisWorld(GameObject instance)
    {
        return instance != null &&
            EntityOwners.TryGetValue(instance, out WorldEnemySimulation owner) &&
            owner == this;
    }

    /// <summary>仅移除仍指向当前世界的所有权，避免覆盖新世界的归属。</summary>
    private void RemoveOwnership(GameObject instance)
    {
        if (instance != null &&
            EntityOwners.TryGetValue(instance, out WorldEnemySimulation owner) &&
            owner == this)
        {
            EntityOwners.Remove(instance);
        }
    }
}
