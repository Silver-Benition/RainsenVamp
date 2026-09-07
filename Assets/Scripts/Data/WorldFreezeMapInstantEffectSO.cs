using UnityEngine;

/// <summary>冻结本局全部敌对模拟、但保留玩家行动的地图拾取效果。</summary>
[CreateAssetMenu(fileName = "NewWorldFreezeMapInstantEffect", menuName = "GameData/Map Instant Effects/World Freeze")]
public sealed class WorldFreezeMapInstantEffectSO : MapInstantEffectSO
{
    [SerializeField, Min(0f)] private float duration = 15f;

    /// <summary>经过非负钳制的冻结秒数。</summary>
    public float Duration => Mathf.Max(0f, duration);

    /// <summary>向本局冻结控制器请求刷新冻结时间；缺少控制器时不伪报成功。</summary>
    public override bool TryApply(MapInstantEffectContext context)
    {
        return WorldFreezeController.Instance != null &&
            WorldFreezeController.Instance.TryFreeze(Duration);
    }

    /// <summary>在 Inspector 修改时阻止负数持续时间。</summary>
    private void OnValidate()
    {
        duration = Mathf.Max(0f, duration);
    }
}
