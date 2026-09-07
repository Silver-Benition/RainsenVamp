using System;
using UnityEngine;

/// <summary>
/// 本局敌对模拟冻结的唯一权威入口。
/// 它不修改 Time.timeScale，因此玩家、玩家武器、磁吸拾取物和 RunDirector 权威计时仍会继续。
/// </summary>
[DisallowMultipleComponent]
public sealed class WorldFreezeController : MonoBehaviour
{
    private float _remainingDuration;

    /// <summary>
    /// 冻结状态切换事件。仅在“未冻结 → 冻结”或“冻结 → 未冻结”时广播，
    /// 避免表现层在每帧轮询或重复刷新冻结时反复工作。
    /// </summary>
    public event Action<bool> FreezeStateChanged;

    /// <summary>当前场景中的冻结控制器实例。</summary>
    public static WorldFreezeController Instance { get; private set; }

    /// <summary>敌人、敌方弹体、Boss 与波次系统当前是否应暂停。</summary>
    public static bool IsHostileSimulationFrozen =>
        Instance != null && Instance.isActiveAndEnabled && Instance._remainingDuration > 0f;

    /// <summary>当前剩余冻结秒数，使用受 Time.timeScale 影响的游戏时间。</summary>
    public float RemainingDuration => Mathf.Max(0f, _remainingDuration);

    /// <summary>登记场景唯一实例；重复组件会被禁用并给出明确错误。</summary>
    private void Awake()
    {
        RegisterInstance();
    }

    /// <summary>组件重新启用时恢复唯一实例登记。</summary>
    private void OnEnable()
    {
        RegisterInstance();
    }

    /// <summary>以游戏时间推进倒计时；升级、暂停和结果页期间自然停止。</summary>
    private void Update()
    {
        if (_remainingDuration <= 0f)
        {
            return;
        }

        _remainingDuration = Mathf.Max(0f, _remainingDuration - Mathf.Max(0f, Time.deltaTime));
        if (_remainingDuration <= 0f)
        {
            FreezeStateChanged?.Invoke(false);
        }
    }

    /// <summary>
    /// 激活或刷新敌对冻结。重复拾取取现有剩余值与新时长的较大值，不进行累加。
    /// </summary>
    /// <returns>当前局允许且持续时间有效时返回 true。</returns>
    public bool TryFreeze(float duration)
    {
        float safeDuration = Mathf.Max(0f, duration);
        if (safeDuration <= 0f ||
            (RunDirector.Instance != null && RunDirector.Instance.IsResultFrozen))
        {
            return false;
        }

        bool wasFrozen = _remainingDuration > 0f;
        _remainingDuration = Mathf.Max(_remainingDuration, safeDuration);
        if (!wasFrozen)
        {
            FreezeStateChanged?.Invoke(true);
        }

        return true;
    }

    /// <summary>立即结束冻结，供场景退出和确定性测试清理使用。</summary>
    public void CancelFreeze()
    {
        if (_remainingDuration <= 0f)
        {
            return;
        }

        _remainingDuration = 0f;
        FreezeStateChanged?.Invoke(false);
    }

    /// <summary>确保只有一个启用组件能成为本局权威实例。</summary>
    private void RegisterInstance()
    {
        if (Instance == null || Instance == this)
        {
            Instance = this;
            return;
        }

        Debug.LogError("场景中存在重复的 WorldFreezeController，后创建的组件已禁用。", this);
        enabled = false;
    }

    /// <summary>组件停用时清除状态和静态引用，避免重载场景后残留冻结。</summary>
    private void OnDisable()
    {
        bool wasFrozen = _remainingDuration > 0f;
        _remainingDuration = 0f;
        if (wasFrozen)
        {
            FreezeStateChanged?.Invoke(false);
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }
}
