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

    private void Awake()
    {
        EnsureCapacity();
    }

    private void OnEnable()
    {
        elapsed = 0f;
        EnsurePlayer();
        ResetRuntimeState();
    }

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
            if (rule.spawnsPerSecond <= 0f) continue;

            // 时间窗判断
            if (elapsed < rule.startTime) continue;
            if (rule.endTime > 0f && elapsed > rule.endTime) continue;

            // 并发上限闸门
            if (rule.maxAlive > 0 && aliveCounts[i] >= rule.maxAlive) continue;

            spawnAccumulators[i] += rule.spawnsPerSecond * Time.deltaTime;

            // 允许一帧刷多只（例如高压波次），但可通过上限保护瞬时性能
            int spawnedThisFrame = 0;
            while (spawnAccumulators[i] >= 1f)
            {
                if (maxSpawnPerRulePerFrame > 0 && spawnedThisFrame >= maxSpawnPerRulePerFrame)
                {
                    // 到达单帧上限后保留剩余积分，下帧继续兑现，避免单帧尖峰。
                    break;
                }

                if (rule.maxAlive > 0 && aliveCounts[i] >= rule.maxAlive)
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

    private void SpawnFromRule(int ruleIndex, WaveConfigSO.SpawnRule rule)
    {
        Vector3 spawnPos = GetSpawnPositionAroundPlayer(rule.spawnRadiusMin, rule.spawnRadiusMax);

        GameObject enemyObj = PoolManager.Instance.Spawn(rule.enemyPrefab, spawnPos, Quaternion.identity);

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

    private void EnsurePlayer()
    {
        if (playerTransform != null) return;
        GameObject player = GameObject.FindWithTag("Player");
        if (player != null) playerTransform = player.transform;
    }

    private void EnsureCapacity()
    {
        if (waveConfig == null) return;

        int count = waveConfig.rules != null ? waveConfig.rules.Count : 0;
        while (spawnAccumulators.Count < count) spawnAccumulators.Add(0f);
        while (aliveCounts.Count < count) aliveCounts.Add(0);
    }

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
}

