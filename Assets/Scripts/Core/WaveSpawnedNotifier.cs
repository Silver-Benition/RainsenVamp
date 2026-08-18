using UnityEngine;

/// <summary>
/// 给池化怪物用的“刷怪计数回调”组件。
/// - WaveManager 在 Spawn 时注入 ruleIndex 与 manager 引用。
/// - 对象被 PoolManager.Release() 回收时会 SetActive(false)，从而触发 OnDisable，用于减少 alive 计数。
/// - 支持显式开启/关闭追踪，避免多生成来源共用同一敌人预制体时出现计数串线。
/// </summary>
public class WaveSpawnedNotifier : MonoBehaviour
{
    private WaveManager owner;
    private WorldWaveManager worldOwner;
    private int ruleIndex = -1;

    // 只有被 WaveManager 显式开启追踪的实例，才会在 OnDisable 时回调计数
    private bool trackingEnabled;

    /// <summary>
    /// 开启波次计数追踪（仅供 WaveManager 调用）。
    /// </summary>
    public void EnableTracking(WaveManager waveManager, int boundRuleIndex)
    {
        owner = waveManager;
        worldOwner = null;
        ruleIndex = boundRuleIndex;
        trackingEnabled = true;
    }

    /// <summary>
    /// 关闭追踪。用于非 WaveManager 来源生成同一敌人时，明确不参与波次统计。
    /// </summary>
    /// <summary>
    /// 绑定到世界专属波次管理器。
    /// </summary>
    public void EnableTracking(WorldWaveManager waveManager, int boundRuleIndex)
    {
        owner = null;
        worldOwner = waveManager;
        ruleIndex = boundRuleIndex;
        trackingEnabled = true;
    }

    public void DisableTracking()
    {
        owner = null;
        worldOwner = null;
        ruleIndex = -1;
        trackingEnabled = false;
    }

    private void OnDisable()
    {
        if (!trackingEnabled) return;

        if (owner != null && ruleIndex >= 0)
        {
            owner.NotifyDespawn(ruleIndex);
        }
        else if (worldOwner != null && ruleIndex >= 0)
        {
            worldOwner.NotifyDespawn(ruleIndex);
        }

        // 回调后立即关闭，防止重复 Disable 导致计数穿透。
        DisableTracking();
    }
}

