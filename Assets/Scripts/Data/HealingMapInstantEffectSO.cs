using UnityEngine;

/// <summary>为玩家立即恢复固定生命值的地图拾取效果。</summary>
[CreateAssetMenu(fileName = "NewHealingMapInstantEffect", menuName = "GameData/Map Instant Effects/Healing")]
public sealed class HealingMapInstantEffectSO : MapInstantEffectSO
{
    [SerializeField, Min(0f)] private float healAmount = 45f;

    /// <summary>经过非负钳制的恢复量。</summary>
    public float HealAmount => Mathf.Max(0f, healAmount);

    /// <summary>恢复玩家生命；满血、死亡或依赖缺失时不视为效果成功。</summary>
    public override bool TryApply(MapInstantEffectContext context)
    {
        return context.PlayerHealth != null &&
            context.PlayerHealth.RestoreHealth(HealAmount) > 0f;
    }

    /// <summary>在 Inspector 修改时阻止负数恢复配置。</summary>
    private void OnValidate()
    {
        healAmount = Mathf.Max(0f, healAmount);
    }
}
