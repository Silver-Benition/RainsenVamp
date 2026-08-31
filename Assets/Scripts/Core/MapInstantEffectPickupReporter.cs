using UnityEngine;

/// <summary>
/// 未来即时效果拾取物在“效果已成功应用”之后调用的单次报告组件。
/// 每次 OnEnable 到 OnDisable 视为一个池化生命周期，同一生命周期最多成功上报一次。
/// </summary>
[DisallowMultipleComponent]
public sealed class MapInstantEffectPickupReporter : MonoBehaviour
{
    [SerializeField] private MapInstantEffectPickupDataSO pickupData;

    private bool _reportedThisLifecycle;

    /// <summary>当前报告器绑定的静态拾取物配置。</summary>
    public MapInstantEffectPickupDataSO PickupData => pickupData;

    /// <summary>池对象取出时开启新的可报告生命周期。</summary>
    private void OnEnable()
    {
        _reportedThisLifecycle = false;
    }

    /// <summary>
    /// 在调用方确认即时效果成功后报告一次；效果失败或结果系统缺失时保留未报告状态。
    /// </summary>
    /// <returns>本次报告确实写入当前局统计时返回 true。</returns>
    public bool ReportEffectApplied()
    {
        if (_reportedThisLifecycle || pickupData == null || RunDirector.Instance == null)
        {
            return false;
        }

        if (!RunDirector.Instance.ReportInstantEffectPickup(pickupData))
        {
            return false;
        }

        _reportedThisLifecycle = true;
        return true;
    }
}
