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

    [Header("世界配置")]
    [SerializeField] private WorldLineDataSO worldLine;
    [SerializeField] private Transform target;
    [SerializeField] private Transform entityRoot;

    private readonly List<TrackedEnemy> trackedEnemies = new List<TrackedEnemy>();
    private bool worldActive;

    /// <summary>当前世界使用的静态配置。</summary>
    public WorldLineDataSO WorldLine => worldLine;

    /// <summary>当前世界池中仍处于激活状态的敌人数。</summary>
    public int ActiveEnemyCount => CountActiveEnemies();

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
            tracked.enemyBase.ApplySpawnSnapshot(snapshot);
        }

        WaveSpawnedNotifier notifier = enemy.GetComponent<WaveSpawnedNotifier>();
        if (notifier == null) notifier = enemy.AddComponent<WaveSpawnedNotifier>();
        notifier.EnableTracking(owner, ruleIndex);
        ApplyWorldState(tracked);
        return enemy;
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
    }

    /// <summary>刷新单个敌人的世界交互状态。</summary>
    private void ApplyWorldState(TrackedEnemy enemy)
    {
        if (enemy == null || enemy.instance == null) return;
        bool active = worldActive && enemy.instance.activeInHierarchy;
        for (int i = 0; i < enemy.colliders.Length; i++) enemy.colliders[i].enabled = active;
        for (int i = 0; i < enemy.renderers.Length; i++) enemy.renderers[i].enabled = active;
    }

    /// <summary>统计当前世界池中激活的敌人，不跨世界扫描。</summary>
    private int CountActiveEnemies()
    {
        int count = 0;
        for (int i = 0; i < trackedEnemies.Count; i++)
        {
            if (trackedEnemies[i].instance != null && trackedEnemies[i].instance.activeInHierarchy) count++;
        }
        return count;
    }
}
