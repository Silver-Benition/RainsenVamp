using UnityEngine;
using System.Collections.Generic;

/// <summary>从独立挂点生成池化近战动作；交替序号属于装备实例，不写入共享配置。</summary>
public sealed class MeleeWeapon : WeaponBase
{
    private bool _nextThrust = true;
    public bool NextAttackIsThrust => weaponData != null && (weaponData.meleePattern == MeleeAttackPattern.Thrust
        || (weaponData.meleePattern == MeleeAttackPattern.Alternating && _nextThrust));

    private readonly List<MeleeSwingHitbox> _swings = new List<MeleeSwingHitbox>(4);
    protected override bool CanStartAttack
    {
        get
        {
            for (int i = _swings.Count - 1; i >= 0; i--)
                if (_swings[i] == null || !_swings[i].BelongsTo(transform)) _swings.RemoveAt(i);
            return _swings.Count == 0;
        }
    }

    /// <summary>自动近战以挂点到目标中心判断，保留原作 50 范围点的开火余量。</summary>
    protected override float GetAttackRange() => CurrentVisualRange + .5f;

    /// <summary>新回合清理未完成动作，再从突刺开始。</summary>
    public override void ResetRoundCooldown() { ClearSwings(); base.ResetRoundCooldown(); _nextThrust = true; }

    /// <summary>卸下或暂停装备时取消动作，避免遗留脱离来源的伤害。</summary>
    private void OnDisable() { ClearSwings(); }

    /// <summary>仅取消仍属于本挂点的实例，池复用后的外来动作不受影响。</summary>
    private void ClearSwings()
    {
        foreach (MeleeSwingHitbox swing in _swings)
            if (swing != null && swing.BelongsTo(transform)) swing.Cancel();
        _swings.Clear();
    }

    /// <summary>捕获本次目标后生成局部近战动作；仅成功生成才推进交替序号，多发共享动作类型。</summary>
    protected override void Attack()
    {
        if (weaponData == null || weaponData.projectilePrefab == null || PoolManager.Instance == null) return;
        WeaponLevelData level = GetCurrentLevelData();
        if (level == null) return;
        if (!CanStartAttack) return;
        Collider2D target = CaptureMeleeTarget();
        bool thrust = NextAttackIsThrust;
        Vector3 direction = GetAimDirection();
        int count = GetCurrentProjectileCount();
        float duration = GetModifiedDuration(level.activeDuration);
        bool generated = false;
        WeaponHeldView view = HeldView;
        for (int i = 0; i < count; i++)
        {
            GameObject instance = PoolManager.Instance.Spawn(weaponData.projectilePrefab, transform.position, Quaternion.identity);
            if (instance != null && instance.TryGetComponent<MeleeSwingHitbox>(out var hitbox))
            {
                hitbox.InitializeDirected(weaponData, transform, CalculateSpreadDirection(direction, i, count, level.spreadAngle),
                    thrust, GetCurrentDamage(), GetModifiedRange(level.meleeRange), level.meleeArc, duration, CreateHitSnapshot());
                hitbox.ConfigureMotion(this, target, UsesAutomaticMeleeAim);
                _swings.Add(hitbox);
                if (!generated && view != null) hitbox.BeginFromHeld(view);
                generated = true;
            }
            else if (instance != null) PoolManager.Instance.Release(weaponData.projectilePrefab, instance);
        }
        if (!generated) return;
        if (weaponData.meleePattern == MeleeAttackPattern.Alternating) _nextThrust = !_nextThrust;

    }
}
