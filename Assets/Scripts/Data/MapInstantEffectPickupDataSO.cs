using UnityEngine;

/// <summary>
/// 地图即时效果拾取物的展示、统计与效果配置。
/// 经验、金币和宝箱仍保持各自的业务类型，不通过此资产执行。
/// </summary>
[CreateAssetMenu(fileName = "NewMapInstantEffectPickup", menuName = "GameData/Map Instant Effect Pickup")]
public sealed class MapInstantEffectPickupDataSO : ScriptableObject
{
    [Header("稳定身份")]
    [Tooltip("跨本地化与对象池生命周期稳定的拾取物 ID。")]
    public string pickupID;

    [Header("本地化与表现")]
    public string nameKey;
    [Tooltip("本地化系统接入前使用的直接显示名称；为空时回退到资产名。")]
    public string displayName;
    public Sprite icon;

    [Header("即时效果")]
    [SerializeField, Tooltip("玩家拾取后执行的无状态效果策略。")]
    private MapInstantEffectSO effect;

    [Header("结果页排序")]
    [Tooltip("数值越小越靠前；相同时按稳定 ID 排序。")]
    public int sortOrder;

    /// <summary>玩家碰到该拾取物时执行的效果策略。</summary>
    public MapInstantEffectSO Effect => effect;

    /// <summary>读取稳定拾取物 ID；旧资产缺失时以资产名安全回退。</summary>
    public string GetStableId()
    {
        return !string.IsNullOrWhiteSpace(pickupID) ? pickupID : name;
    }

    /// <summary>返回当前可直接展示的拾取物名称。</summary>
    public string GetDisplayName()
    {
        return !string.IsNullOrWhiteSpace(displayName) ? displayName : name;
    }
}
