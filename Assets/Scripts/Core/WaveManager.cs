using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 数据驱动波次刷怪系统（Wave Manager）。
/// 运行时职责：
/// - 维护波次计时（受 Time.timeScale 影响：升级面板时停 -> 刷怪自然暂停）
/// - 对每条 SpawnRule 做“速率积分”，按需生成
/// - 可选并发上限：通过 WaveSpawnedNotifier 在 OnDisable 回调减少 alive 计数
/// </summary>
public class WaveManager : MonoBehaviour
{
    [Header("配置")]
    public WaveConfigSO waveConfig;

    [Header("运行时依赖")]
    [Tooltip("默认会通过 Tag=Player 自动寻找。建议后续替换为全局引用，避免 Find 开销。")]
    public Transform playerTransform;

    [Header("调试")]
    public bool drawGizmos = true;

    [Header("性能保护")]
    [Tooltip("单条规则单帧最多生成数量。<=0 代表不限制。用于防止卡顿补帧时瞬时刷怪尖峰。")]
    public int maxSpawnPerRulePerFrame = 3;

    private float elapsed;

    // 每条规则一个积分器（用于处理 spawnsPerSecond 的小数）
    private readonly List<float> spawnAccumulators = new List<float>();

    // 每条规则一个在场计数（可用于 maxAlive 闸门）
    private readonly List<int> aliveCounts = new List<int>();
    private readonly Dictionary<GameObject, EnemyDataSO> _enemyDataCache =
        new Dictionary<GameObject, EnemyDataSO>();
    private PlayerStats _playerStats;

    /// <summary>初始化每条刷怪规则的运行时计数槽位。</summary>
    private void Awake()
    {
        EnsureCapacity();
    }

    /// <summary>组件启用时重新绑定玩家并清空本轮波次状态。</summary>
    private void OnEnable()
    {
        elapsed = 0f;
        EnsurePlayer();
        ResetRuntimeState();
    }

    /// <summary>按规则时间窗、属性倍率和并发上限推进刷怪积分。</summary>
    private void Update()
    {
        if (waveConfig == null) return;
        EnsurePlayer();
        if (playerTransform == null) return;

        elapsed += Time.deltaTime;

        // 总时长到点则停止生成（不清场）
        if (waveConfig.duration > 0f && elapsed >= waveConfig.duration)
        {
            return;
        }

        EnsureCapacity();

        var rules = waveConfig.rules;
        for (int i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            if (rule == null) continue;
            if (rule.enemyPrefab == null) continue;
            float curse = _playerStats != null ? _playerStats.Curse : 1f;
            float charm = _playerStats != null ? _playerStats.Charm : 0f;
            float effectiveSpawnRate = EnemySpawnSnapshotFactory.GetEffectiveSpawnRate(
                rule.spawnsPerSecond,
                curse,
                charm);
            int effectiveMaxAlive = EnemySpawnSnapshotFactory.GetEffectiveMaxAlive(
                rule.maxAlive,
                curse,
                charm);
            if (effectiveSpawnRate <= 0f) continue;

            // 时间窗判断
            if (elapsed < rule.startTime) continue;
            if (rule.endTime > 0f && elapsed > rule.endTime) continue;

            // 并发上限闸门
            if (effectiveMaxAlive > 0 && aliveCounts[i] >= effectiveMaxAlive) continue;

            spawnAccumulators[i] += effectiveSpawnRate * Time.deltaTime;

            // 允许一帧刷多只（例如高压波次），但可通过上限保护瞬时性能
            int spawnedThisFrame = 0;
            while (spawnAccumulators[i] >= 1f)
            {
                if (maxSpawnPerRulePerFrame > 0 && spawnedThisFrame >= maxSpawnPerRulePerFrame)
                {
                    // 到达单帧上限后保留剩余积分，下帧继续兑现，避免单帧尖峰。
                    break;
                }

                if (effectiveMaxAlive > 0 && aliveCounts[i] >= effectiveMaxAlive)
                {
                    // 如果在 while 中触顶，清空积分，避免并发闸门打开后出现“欠账爆发”。
                    spawnAccumulators[i] = 0f;
                    break;
                }

                SpawnFromRule(i, rule);
                spawnAccumulators[i] -= 1f;
                spawnedThisFrame++;
            }
        }
    }

    /// <summary>从指定规则生成一名敌人，并应用本次出生快照。</summary>
    private void SpawnFromRule(int ruleIndex, WaveConfigSO.SpawnRule rule)
    {
        Vector3 spawnPos = GetSpawnPositionAroundPlayer(rule.spawnRadiusMin, rule.spawnRadiusMax);

        GameObject enemyObj = PoolManager.Instance.Spawn(rule.enemyPrefab, spawnPos, Quaternion.identity);

        EnemyBase enemyBase = enemyObj != null ? enemyObj.GetComponent<EnemyBase>() : null;
        EnemyDataSO enemyData = GetEnemyData(rule.enemyPrefab);
        if (enemyBase != null)
        {
            enemyBase.ApplySpawnSnapshot(EnemySpawnSnapshotFactory.Create(
                enemyData,
                _playerStats,
                Random.value));
        }

        // 计数 + 回调绑定（通过 OnDisable 减计数）
        aliveCounts[ruleIndex]++;

        var notifier = enemyObj.GetComponent<WaveSpawnedNotifier>();
        if (notifier == null)
        {
            // 运行时补组件：只发生在第一次遇到该预制体的实例上，后续复用不会再 Add。
            notifier = enemyObj.AddComponent<WaveSpawnedNotifier>();
        }

        // 先清理旧追踪，再显式绑定到当前 WaveManager。
        // 这样在“多个生成来源共用同一敌人预制体”时，可避免统计串线。
        notifier.DisableTracking();
        notifier.EnableTracking(this, ruleIndex);
    }

    /// <summary>取得玩家周围指定环形范围内的随机出生点。</summary>
    private Vector3 GetSpawnPositionAroundPlayer(float radiusMin, float radiusMax)
    {
        float min = Mathf.Max(0f, radiusMin);
        float max = Mathf.Max(min, radiusMax);

        Vector2 dir = Random.insideUnitCircle.normalized;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.right;

        float radius = Random.Range(min, max);
        Vector3 center = playerTransform.position;
        return center + new Vector3(dir.x, dir.y, 0f) * radius;
    }

    /// <summary>确保玩家 Transform 与属性组件引用可用。</summary>
    private void EnsurePlayer()
    {
        if (playerTransform == null)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null) playerTransform = player.transform;
        }

        if (_playerStats == null && playerTransform != null)
        {
            _playerStats = playerTransform.GetComponent<PlayerStats>();
        }
    }

    /// <summary>确保积分器和在场计数与刷怪规则数量一致。</summary>
    private void EnsureCapacity()
    {
        if (waveConfig == null) return;

        int count = waveConfig.rules != null ? waveConfig.rules.Count : 0;
        while (spawnAccumulators.Count < count) spawnAccumulators.Add(0f);
        while (aliveCounts.Count < count) aliveCounts.Add(0);
    }

    /// <summary>清空本轮所有规则的累计积分与在场计数。</summary>
    private void ResetRuntimeState()
    {
        for (int i = 0; i < spawnAccumulators.Count; i++) spawnAccumulators[i] = 0f;
        for (int i = 0; i < aliveCounts.Count; i++) aliveCounts[i] = 0;
    }

    /// <summary>
    /// 由 WaveSpawnedNotifier 在对象被回收（Disable）时回调，用于减少 alive 计数。
    /// </summary>
    public void NotifyDespawn(int ruleIndex)
    {
        if (ruleIndex < 0 || ruleIndex >= aliveCounts.Count) return;
        aliveCounts[ruleIndex] = Mathf.Max(0, aliveCounts[ruleIndex] - 1);
    }

    /// <summary>在场景视图中绘制首条规则的刷怪半径参考线。</summary>
    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;
        if (waveConfig == null) return;
        if (playerTransform == null) return;
        if (waveConfig.rules == null || waveConfig.rules.Count == 0) return;

        // 只画第一条规则的半径作为参考（避免 Scene 过度杂乱）
        var rule = waveConfig.rules[0];
        if (rule == null) return;

        Gizmos.color = new Color(1f, 1f, 0f, 0.25f);
        Gizmos.DrawWireSphere(playerTransform.position, rule.spawnRadiusMin);
        Gizmos.DrawWireSphere(playerTransform.position, rule.spawnRadiusMax);
    }

    /// <summary>缓存每种敌人 Prefab 的数据引用，避免连续生成时重复查询资产组件。</summary>
    private EnemyDataSO GetEnemyData(GameObject enemyPrefab)
    {
        if (enemyPrefab == null)
        {
            return null;
        }

        if (_enemyDataCache.TryGetValue(enemyPrefab, out EnemyDataSO cachedData))
        {
            return cachedData;
        }

        EnemyBase templateEnemy = enemyPrefab.GetComponent<EnemyBase>();
        EnemyDataSO resolvedData = templateEnemy != null ? templateEnemy.enemyData : null;
        _enemyDataCache[enemyPrefab] = resolvedData;
        return resolvedData;
    }
}
