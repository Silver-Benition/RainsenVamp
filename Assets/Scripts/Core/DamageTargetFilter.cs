using UnityEngine;

/// <summary>
/// 集中执行玩家与敌人的 Layer 过滤，避免玩家实现 IDamageable 后被己方武器误伤。
/// 玩家正式受击体必须显式挂载 PlayerHurtbox，并兼容碰撞体位于刚体子节点、
/// IDamageable 位于刚体根节点的 Prefab。
/// </summary>
public static class DamageTargetFilter
{
    private static readonly int PlayerLayer = LayerMask.NameToLayer("Player");
    private static readonly int EnemyLayer = LayerMask.NameToLayer("Enemy");

    /// <summary>供物理查询限定敌人候选；缺少 Enemy Layer 时返回空掩码。</summary>
    public static int EnemyLayerMask => EnemyLayer >= 0 ? 1 << EnemyLayer : 0;

    /// <summary>尝试从 Enemy Layer 的碰撞体或刚体根节点取得可受击接口。</summary>
    public static bool TryGetEnemyDamageable(Collider2D collider, out IDamageable damageable)
    {
        return TryGetDamageableOnLayer(collider, EnemyLayer, out damageable);
    }

    /// <summary>
    /// 尝试从带 PlayerHurtbox 标记的 Player Layer 碰撞体或刚体根节点取得可受击接口。
    /// 未标记的玩家身体和 MagnetRadius 等辅助 Collider 一律拒绝。
    /// </summary>
    public static bool TryGetPlayerDamageable(Collider2D collider, out IDamageable damageable)
    {
        // 标记必须与命中的 Collider 位于同一 GameObject，避免把 Player 根节点上的
        // PlayerHealth 误当成所有子级 Trigger 的受击接口。
        if (collider == null || !collider.TryGetComponent<PlayerHurtbox>(out _))
        {
            damageable = null;
            return false;
        }

        return TryGetDamageableOnLayer(collider, PlayerLayer, out damageable);
    }

    /// <summary>
    /// 先做低成本 Layer 过滤，再在碰撞体和刚体根节点查找接口。
    /// 该方法位于物理热路径，不创建集合、不执行父级递归搜索。
    /// </summary>
    private static bool TryGetDamageableOnLayer(
        Collider2D collider,
        int expectedLayer,
        out IDamageable damageable)
    {
        damageable = null;
        if (collider == null || expectedLayer < 0)
        {
            return false;
        }

        Rigidbody2D attachedBody = collider.attachedRigidbody;
        GameObject bodyRoot = attachedBody != null ? attachedBody.gameObject : collider.gameObject;
        if (collider.gameObject.layer != expectedLayer && bodyRoot.layer != expectedLayer)
        {
            return false;
        }

        if (collider.TryGetComponent(out damageable))
        {
            return true;
        }

        return bodyRoot != collider.gameObject && bodyRoot.TryGetComponent(out damageable);
    }
}
