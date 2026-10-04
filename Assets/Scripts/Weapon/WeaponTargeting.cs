using System.Collections.Generic;
using UnityEngine;

/// <summary>复用物理候选缓存的选敌服务；仅主线程同步调用，不保存共享列表到调用方。</summary>
public static class WeaponTargeting
{
    private static readonly List<Collider2D> Candidates = new List<Collider2D>(512);

    /// <summary>解析敌人的真实受击组件，多个碰撞体映射到同一个伤害身份。</summary>
    public static Component Identity(Collider2D collider)
    {
        return DamageTargetFilter.TryGetEnemyDamageable(collider, out IDamageable target)
            ? target as Component : null;
    }

    /// <summary>排除死亡、回池、禁用碰撞和非当前世界对象；生命代次可阻止追踪引用跨池复用。</summary>
    public static bool IsValid(Collider2D collider, uint generation = 0)
    {
        return IsValidCached(collider, Identity(collider), generation);
    }

    /// <summary>校验已缓存的受击身份；持续瞄准和追踪无需每帧 GetComponent。</summary>
    public static bool IsValidCached(Collider2D collider, Component identity, uint generation)
    {
        if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) return false;
        if (identity == null || !identity.gameObject.activeInHierarchy) return false;
        if (identity is EnemyBase enemy)
            return enemy.isActiveAndEnabled && enemy.CurrentHealth > 0f && enemy.IsWorldInteractionEnabled
                && (generation == 0 || generation == enemy.LifeGeneration);
        return true;
    }

    /// <summary>记录目标当前生命代次；非池化测试受击体使用零作为无需版本校验的标记。</summary>
    public static uint Generation(Collider2D collider)
    {
        return Identity(collider) is EnemyBase enemy ? enemy.LifeGeneration : 0;
    }

    /// <summary>查找半径内最近有效敌人。列表自动扩容，不截断密集敌群；稳态不分配数组。</summary>
    public static Collider2D FindNearest(Vector2 origin, float radius, HashSet<Component> excluded = null, bool centersWithinRadius = false)
    {
        var filter = new ContactFilter2D { useTriggers = true };
        filter.SetLayerMask(DamageTargetFilter.EnemyLayerMask);
        Candidates.Clear();
        Physics2D.OverlapCircle(origin, radius, filter, Candidates);
        Collider2D best = null;
        float bestDistance = float.PositiveInfinity;
        int bestId = int.MaxValue;
        for (int i = 0; i < Candidates.Count; i++)
        {
            Collider2D candidate = Candidates[i];
            Component identity = Identity(candidate);
            if (!IsValidCached(candidate, identity, 0)) continue;
            if (excluded != null && excluded.Contains(identity)) continue;
            float distance = ((Vector2)identity.transform.position - origin).sqrMagnitude;
            if (centersWithinRadius && distance > radius * radius) continue;
            int id = identity.GetInstanceID();
            // 平距使用稳定实例 ID，避免物理查询顺序引起每次选敌抖动。
            if (distance < bestDistance || (Mathf.Approximately(distance, bestDistance) && id < bestId))
            { best = candidate; bestDistance = distance; bestId = id; }
        }
        Candidates.Clear();
        return best;
    }
}
