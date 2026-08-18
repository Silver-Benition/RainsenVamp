using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 单个世界线的独立波次管理器。
/// 只维护本世界的计时器和在场计数，副世界隐藏时仍继续推进。
/// </summary>
public class WorldWaveManager : MonoBehaviour
{
    [SerializeField] private WorldLineDataSO worldLine;
    [SerializeField] private WorldEnemySimulation enemySimulation;
    [SerializeField] private Transform playerTransform;
    [SerializeField] private int maxSpawnPerRulePerFrame = 3;

    private readonly List<float> spawnAccumulators = new List<float>();
    private readonly List<int> aliveCounts = new List<int>();
    private float elapsed;

    /// <summary>当前世界累计运行时间。</summary>
    public float Elapsed => elapsed;

    /// <summary>当前世界所有规则的在场敌人总数。</summary>
    public int ActiveEnemyCount
    {
        get
        {
            int total = 0;
            for (int i = 0; i < aliveCounts.Count; i++) total += aliveCounts[i];
            return total;
        }
    }

    /// <summary>缓存依赖并自动寻找玩家目标。</summary>
    private void Awake()
    {
        if (playerTransform == null)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null) playerTransform = player.transform;
        }

        if (enemySimulation == null) enemySimulation = GetComponent<WorldEnemySimulation>();
        EnsureCapacity();
    }

    /// <summary>重置本世界波次运行状态。</summary>
    private void OnEnable()
    {
        elapsed = 0f;
        ResetRuntimeState();
    }

    /// <summary>按规则积分生成敌人，避免小数生成速率的时间误差。</summary>
    private void Update()
    {
        WaveConfigSO config = worldLine != null ? worldLine.WaveConfig : null;
        if (config == null || playerTransform == null || enemySimulation == null) return;

        elapsed += Time.deltaTime;
        if (config.duration > 0f && elapsed >= config.duration) return;

        EnsureCapacity();
        if (config.rules == null) return;

        for (int i = 0; i < config.rules.Count; i++)
        {
            WaveConfigSO.SpawnRule rule = config.rules[i];
            if (rule == null || rule.enemyPrefab == null || rule.spawnsPerSecond <= 0f) continue;
            if (elapsed < rule.startTime || (rule.endTime > 0f && elapsed > rule.endTime)) continue;
            if (rule.maxAlive > 0 && aliveCounts[i] >= rule.maxAlive) continue;

            spawnAccumulators[i] += rule.spawnsPerSecond * Time.deltaTime;
            int spawned = 0;
            while (spawnAccumulators[i] >= 1f)
            {
                if (maxSpawnPerRulePerFrame > 0 && spawned >= maxSpawnPerRulePerFrame) break;
                if (rule.maxAlive > 0 && aliveCounts[i] >= rule.maxAlive)
                {
                    spawnAccumulators[i] = 0f;
                    break;
                }

                SpawnFromRule(i, rule);
                spawnAccumulators[i] -= 1f;
                spawned++;
            }
        }
    }

    /// <summary>按规则从玩家周围的圆环区域生成一个世界敌人。</summary>
    private void SpawnFromRule(int ruleIndex, WaveConfigSO.SpawnRule rule)
    {
        float min = Mathf.Max(0f, rule.spawnRadiusMin);
        float max = Mathf.Max(min, rule.spawnRadiusMax);
        Vector2 direction = Random.insideUnitCircle.normalized;
        if (direction.sqrMagnitude < 0.0001f) direction = Vector2.right;
        Vector3 position = playerTransform.position + (Vector3)(direction * Random.Range(min, max));
        GameObject enemy = enemySimulation.SpawnEnemy(rule.enemyPrefab, position, this, ruleIndex);
        if (enemy != null) aliveCounts[ruleIndex]++;
    }

    /// <summary>供回收通知减少指定规则的在场数量。</summary>
    public void NotifyDespawn(int ruleIndex)
    {
        if (ruleIndex < 0 || ruleIndex >= aliveCounts.Count) return;
        aliveCounts[ruleIndex] = Mathf.Max(0, aliveCounts[ruleIndex] - 1);
    }

    /// <summary>确保规则数量变化后运行时计数数组仍与配置对齐。</summary>
    private void EnsureCapacity()
    {
        int count = worldLine != null && worldLine.WaveConfig != null && worldLine.WaveConfig.rules != null ? worldLine.WaveConfig.rules.Count : 0;
        while (spawnAccumulators.Count < count) spawnAccumulators.Add(0f);
        while (aliveCounts.Count < count) aliveCounts.Add(0);
    }

    /// <summary>清空本世界的计时积分和在场计数。</summary>
    private void ResetRuntimeState()
    {
        for (int i = 0; i < spawnAccumulators.Count; i++) spawnAccumulators[i] = 0f;
        for (int i = 0; i < aliveCounts.Count; i++) aliveCounts[i] = 0;
    }
}