using System.Collections.Generic;
using UnityEngine;

/// <summary>第二批武器的出手调度；冷却、最近目标与挂点仍由原有武器基类负责。</summary>
public sealed class ExpansionWeapon : WeaponBase
{
    private int _sequence;
    private MeleeSwingHitbox _swing;
    private ExpansionEffect _blocking;
    private readonly List<ExpansionEffect> _effects = new List<ExpansionEffect>(4);
    protected override bool CanStartAttack => (_swing == null || !_swing.gameObject.activeInHierarchy || _swing.ExpansionOwner != this)
        && (_blocking == null || !_blocking.BelongsTo(transform));

    /// <summary>近战按挂点到目标中心加开火余量索敌，动作始终跟随本挂点。</summary>
    protected override float GetAttackRange()
    {
        if (weaponData == null || weaponData.runtimeType != WeaponRuntimeType.Melee) return base.GetAttackRange();
        return CurrentVisualRange + .5f;
    }
    /// <summary>基类确认范围内有目标后才推进实例攻击序号；所有弹体从实际挂点出发。</summary>
    protected override void Attack()
    {
        if (weaponData == null || PoolManager.Instance == null) return;
        if (!CanStartAttack) return;
        WeaponLevelData level = GetCurrentLevelData();
        Collider2D meleeTarget = weaponData.runtimeType == WeaponRuntimeType.Melee ? CaptureMeleeTarget() : null;
        Vector2 direction = GetAimDirection();
        if (weaponData.runtimeType == WeaponRuntimeType.Melee)
        {
            GameObject go = PoolManager.Instance.Spawn(weaponData.projectilePrefab, transform.position, Quaternion.identity);
            if (go == null) return;
            _swing = go.GetComponent<MeleeSwingHitbox>();
            if (_swing == null) { PoolManager.Instance.Release(weaponData.projectilePrefab, go); return; }
            _sequence++;
            _swing.InitializeDirected(weaponData, transform, direction, weaponData.expansionKind == ExpansionWeaponKind.Piston,
                GetCurrentDamage(), CurrentVisualRange, level.meleeArc, level.activeDuration, CreateHitSnapshot());
            _swing.ConfigureMotion(this, meleeTarget, UsesAutomaticMeleeAim);
            _swing.ConfigureExpansion(this, _sequence % 3 == 0, SecondaryDamage(level));
            if (HeldView != null) _swing.BeginFromHeld(HeldView);
            return;
        }
        EffectSpec spec = CreateSpec(direction);
        switch (weaponData.expansionKind)
        {
            case ExpansionWeaponKind.Railgun: spec.Kind = ExpansionEffectKind.Beam; spec.Delay = .3f; spec.Width = .16f; spec.MaxHits = CurrentLevel + 2; break;
            case ExpansionWeaponKind.Welder: spec.Kind = ExpansionEffectKind.Cone; spec.Duration = .6f; spec.Width = 60; break;
            case ExpansionWeaponKind.Mine: spec.Kind = ExpansionEffectKind.Mine; spec.Width = 1f + .1f * (CurrentLevel - 1); spec.Duration = 6; TrimEffects(2); break;
            case ExpansionWeaponKind.Returning: spec.Kind = ExpansionEffectKind.Returning; spec.Speed = 8; spec.Sprite = weaponData.icon; spec.VisualLength = LaunchVisualLength; break;
            case ExpansionWeaponKind.Frost: spec.Kind = ExpansionEffectKind.Frost; spec.Width = 1f + .1f * (CurrentLevel - 1); spec.Slow = .25f; spec.SlowSeconds = 1f + .2f * (CurrentLevel - 1); break;
            case ExpansionWeaponKind.Seed: spec.Kind = ExpansionEffectKind.Seed; spec.Speed = 9; break;
            case ExpansionWeaponKind.Ink: spec.Kind = ExpansionEffectKind.Ink; spec.Width = .65f; spec.Duration = 1.5f; TrimEffects(1); break;
        }
        ExpansionEffect effect = SpawnEffect(spec, transform.position);
        if (spec.Kind == ExpansionEffectKind.Beam || spec.Kind == ExpansionEffectKind.Cone || spec.Kind == ExpansionEffectKind.Returning) _blocking = effect;
    }
    /// <summary>灼烧独立使用元素系数和伤害百分比，不复用直接近战系数。</summary>
    private float SecondaryDamage(WeaponLevelData level)
    {
        float flat = level.secondaryDamage + level.secondaryElementalScaling * (_playerStats != null ? _playerStats.GetFinalStat(PlayerStatType.ElementalDamage) : 0);
        float percent = level.damagePercent + (_playerStats != null ? _playerStats.GetFinalStat(PlayerStatType.DamagePercent) : 0);
        return BrotatoStatRules.ScaleDamage(flat, percent);
    }
    /// <summary>建立发射快照；手动模式沿玩家指定方向定点，自动模式采用当前最近目标的位置。</summary>
    private EffectSpec CreateSpec(Vector2 direction)
    {
        float range = GetModifiedRange(GetCurrentLevelData().attackRange);
        Collider2D target = WeaponTargeting.FindNearest(transform.position, range);
        Vector2 point = target != null && (_aimController == null || _aimController.aimMode == AimController.AimMode.NearestEnemy)
            ? (Vector2)target.transform.position : (Vector2)transform.position + direction * range;
        return new EffectSpec { Source = transform, Owner = OwnerTransform, Weapon = weaponData, Hit = CreateHitSnapshot(),
            Damage = GetCurrentDamage(), Direction = direction, Target = point, Range = range, Tier = CurrentLevel,
            Sprite = weaponData.effectSprite, VisualLength = .3f, Speed = 9, Width = .15f };
    }
    /// <summary>正常近战动作完成后生成一次派生攻击；回收/切波取消动作不会调用此入口。</summary>
    public void CompleteMelee(Vector2 origin, Vector2 direction, Dictionary<Component, uint> hitTargets, float damage, WeaponHitSnapshot hit, bool empowered)
    {
        if (!isActiveAndEnabled || !RoundController.AllowsCombat) return;
        EffectSpec spec = CreateSpec(direction); spec.Hit = hit;
        if (weaponData.expansionKind == ExpansionWeaponKind.Piston && empowered)
        {
            spec.Kind = ExpansionEffectKind.Wave; spec.Damage = Mathf.Max(1, Mathf.Floor(damage * .5f));
            spec.Range = GetModifiedRange(2.2f); spec.MaxHits = CurrentLevel + 1; spec.Width = .55f;
            ExpansionEffect effect = SpawnEffect(spec, origin); if (effect != null) effect.Exclude(hitTargets);
        }
        else if (weaponData.expansionKind == ExpansionWeaponKind.Echo)
        {
            spec.Kind = ExpansionEffectKind.Echo; spec.Damage = Mathf.Max(1, Mathf.Floor(damage * .6f));
            spec.Delay = .3f; spec.Range = CurrentVisualRange; spec.Width = GetCurrentLevelData().meleeArc;
            SpawnEffect(spec, origin);
        }
    }
    /// <summary>所有派生效果使用项目资产作为池键，记录来源以便立即回收。</summary>
    private ExpansionEffect SpawnEffect(EffectSpec spec, Vector2 position)
    {
        Prune();
        GameObject go = PoolManager.Instance.Spawn(weaponData.expansionEffectPrefab, position, Quaternion.identity);
        if (go == null) return null;
        ExpansionEffect effect = go.GetComponent<ExpansionEffect>();
        if (effect == null) { PoolManager.Instance.Release(weaponData.expansionEffectPrefab, go); return null; }
        effect.Configure(spec); _effects.Add(effect); return effect;
    }
    /// <summary>过期引用可能被其他武器复用，必须核对来源后才进行计数或回收。</summary>
    private void Prune()
    { for (int i = _effects.Count - 1; i >= 0; i--) if (_effects[i] == null || !_effects[i].BelongsTo(transform)) _effects.RemoveAt(i); }
    /// <summary>地雷和墨带按每件装备限制数量，超额时先回收最旧实体。</summary>
    private void TrimEffects(int limit)
    { Prune(); while (_effects.Count >= limit) { _effects[0].Release(); _effects.RemoveAt(0); } }
    /// <summary>装备消失时清理其效果，不能遗留在途或延迟攻击。</summary>
    private void OnDisable() { ClearEffects(); }
    /// <summary>新波次重置序号，并立即停止上一波的派生攻击。</summary>
    public override void ResetRoundCooldown() { ClearEffects(); _sequence = 0; base.ResetRoundCooldown(); }
    /// <summary>只回收本来源持有的池实例，避免释放已被复用的其他武器效果。</summary>
    private void ClearEffects()
    {
        ExpansionEffect.ReleaseSource(transform);
        Prune(); for (int i = _effects.Count - 1; i >= 0; i--) _effects[i].Release(); _effects.Clear();
        if (_swing != null && _swing.gameObject.activeInHierarchy && _swing.ExpansionOwner == this)
            PoolManager.Instance?.Release(weaponData.projectilePrefab, _swing.gameObject);
        _swing = null; _blocking = null;
    }
}
