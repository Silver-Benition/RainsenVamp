using UnityEngine;

/// <summary>
/// 武器运行时基类，同时提供直飞投射物的默认攻击实现。
/// </summary>
public class WeaponBase : MonoBehaviour
{
    [Header("武器配置")]
    public WeaponDataSO weaponData;

    protected float _currentCooldown;
    protected int _currentLevel = 1;
    protected AimController _aimController;
    protected PlayerStats _playerStats;
    protected PlayerHealth _ownerHealth;
    private Collider2D _attackTarget;
    private Component _attackTargetIdentity;
    private uint _attackTargetGeneration;
    private float _nextAttackTargetSearch;
    public float CurrentAttackRange => GetAttackRange();
    /// <summary>近战按玩家中心索敌，避免装备在背侧挂点时损失基础距离。</summary>
    public Vector2 AttackOrigin => weaponData != null && weaponData.runtimeType == WeaponRuntimeType.Melee
        ? (Vector2)OwnerTransform.position : (Vector2)transform.position;
    public MeleeAttackTiming CurrentMeleeTiming => new MeleeAttackTiming(GetCurrentLevelData()?.activeDuration ?? .2f,
        CurrentVisualRange, GetCurrentCooldownMultiplier());

    /// <summary>出手时锁定攻击落点；手动方向不被自动目标覆盖，大体型敌人只要求碰撞边缘入圈。</summary>
    protected Vector2 GetMeleeTargetPoint(Vector2 direction)
    {
        Collider2D target = WeaponTargeting.IsValidCached(_attackTarget, _attackTargetIdentity, _attackTargetGeneration)
            ? _attackTarget : WeaponTargeting.FindNearest(AttackOrigin, CurrentAttackRange);
        bool automatic = _aimController == null || _aimController.aimMode == AimController.AimMode.NearestEnemy;
        if (automatic && target != null)
            return AttackOrigin + Vector2.ClampMagnitude((Vector2)WeaponTargeting.Identity(target).transform.position - AttackOrigin, CurrentAttackRange);
        float distance = target != null ? Vector2.Distance(AttackOrigin, target.transform.position) : CurrentAttackRange;
        return AttackOrigin + direction.normalized * Mathf.Clamp(distance, .25f, CurrentAttackRange);
    }

    /// <summary>攻击挂点与玩家中心分离，持续范围技能显式使用玩家中心。</summary>
    public Transform OwnerTransform => _aimController != null ? _aimController.transform : transform.parent != null ? transform.parent : transform;
    public Vector2 CurrentAimDirection => GetAimDirection();
    /// <summary>持续环绕和光环仅保留玩家中心效果，不占用持武挂点。</summary>
    public bool UsesHeldMount => !(this is AuraWeapon) && !(this is OrbitWeapon);
    internal WeaponHeldView HeldView { get; set; }
    /// <summary>投掷使用发射瞬间实际显示的长度，范围正在平滑改变时也不会突然放大。</summary>
    protected float LaunchVisualLength => HeldView != null && HeldView.Renderer != null
        ? WeaponVisualGeometry.ProjectedLength(HeldView.Renderer.sprite, weaponData.visualAngleOffset) * Mathf.Abs(HeldView.Renderer.transform.lossyScale.x)
        : CurrentVisualLength;
    public float CurrentVisualRange => GetModifiedRange(GetCurrentLevelData()?.meleeRange ?? 1f);
    /// <summary>发射器随当前射程比例缩放；近战与攻击实体共用剑身长度。</summary>
    public float CurrentVisualLength => weaponData != null && weaponData.runtimeType == WeaponRuntimeType.Melee
        ? WeaponVisualGeometry.MeleeLength(CurrentVisualRange)
        : Mathf.Max(.05f, weaponData != null ? weaponData.heldSize : .65f) * CurrentVisualRangeRatio;
    public float CurrentVisualRangeRatio => GetModifiedRange(GetCurrentLevelData()?.attackRange ?? 5f)
        / Mathf.Max(.25f, GetCurrentLevelData()?.attackRange ?? 5f);
    public int CurrentLevel => _currentLevel;
    public int MaxLevel => RoundController.Enabled ? 4 : (weaponData != null ? weaponData.MaxLevel : 1);
    public bool IsMaxLevel => CurrentLevel >= MaxLevel;

    /// <summary>
    /// 缓存玩家父级上的瞄准控制器，避免每次攻击重复查找组件。
    /// </summary>
    protected virtual void Awake()
    {
        _aimController = GetComponentInParent<AimController>();
        _playerStats = GetComponentInParent<PlayerStats>();
        _ownerHealth = GetComponentInParent<PlayerHealth>();
    }

    /// <summary>
    /// 推进攻击冷却，并在冷却结束时调用当前武器的攻击实现。
    /// </summary>
    protected virtual void Update()
    {
        if (weaponData == null || !RoundController.AllowsCombat)
        {
            return;
        }

        _currentCooldown = Mathf.Max(0f, _currentCooldown - Time.deltaTime);
        if (_currentCooldown > 0f)
        {
            return;
        }

        // 空场只等待目标，不消耗已经就绪的冷却，也不推进近战交替序号。
        // 光环与环绕是用户保留的常驻例外，仍按原有生命周期刷新。
        if (UsesHeldMount && !TryAcquireAttackTarget()) return;
        if (!CanStartAttack) return;
        Attack();
        _currentCooldown = GetCurrentCooldown();
    }

    /// <summary>直飞与投掷使用当前品质射程；近战覆盖此入口，按下一动作真实可达距离判断。</summary>
    protected virtual bool CanStartAttack => true;

    /// <summary>返回出手前有效索敌范围。</summary>
    protected virtual float GetAttackRange()
    {
        WeaponLevelData level = GetCurrentLevelData();
        return level != null ? GetModifiedRange(level.attackRange) : 0f;
    }

    /// <summary>就绪武器至多每 0.08 秒查询一次局部物理范围；缓存目标每次出手前校验生命及距离。</summary>
    private bool TryAcquireAttackTarget()
    {
        float range = GetAttackRange();
        bool valid = WeaponTargeting.IsValidCached(_attackTarget, _attackTargetIdentity, _attackTargetGeneration);
        if (Time.time >= _nextAttackTargetSearch || (_attackTargetGeneration != 0 && !valid))
        {
            _attackTarget = WeaponTargeting.FindNearest(AttackOrigin, range);
            _attackTargetIdentity = WeaponTargeting.Identity(_attackTarget);
            _attackTargetGeneration = _attackTargetIdentity is EnemyBase enemy ? enemy.LifeGeneration : 0;
            _nextAttackTargetSearch = Time.time + .08f;
            valid = WeaponTargeting.IsValidCached(_attackTarget, _attackTargetIdentity, _attackTargetGeneration);
        }
        // 使用敌人碰撞边缘进入范围作为触发，避免大体型敌人必须中心进入才会出手。
        return valid && ((Vector2)_attackTarget.ClosestPoint(AttackOrigin) - AttackOrigin).sqrMagnitude
            <= range * range + .00001f;
    }

    /// <summary>
    /// 尝试把武器提升一级；达到上限或缺少数据时返回 false。
    /// </summary>
    public virtual bool TryLevelUp()
    {
        if (weaponData == null || IsMaxLevel)
        {
            return false;
        }

        _currentLevel++;
        OnLevelChanged();
        return true;
    }

    /// <summary>
    /// 等级变化钩子；持续型武器可在这里立即刷新场上实体。
    /// </summary>
    protected virtual void OnLevelChanged()
    {
    }

    /// <summary>
    /// 返回当前等级配置，不存在武器数据时返回 null。
    /// </summary>
    protected WeaponLevelData GetCurrentLevelData()
    {
        return weaponData != null ? (RoundController.Enabled ? weaponData.GetRoundTierConfig(_currentLevel) : weaponData.GetLevelConfig(_currentLevel)) : null;
    }

    /// <summary>
    /// 判断当前等级是否包含指定功能标签。
    /// </summary>
    protected bool HasFeature(WeaponFeatureType featureType)
    {
        WeaponLevelData levelData = GetCurrentLevelData();
        return levelData != null
            && levelData.features != null
            && levelData.features.Contains(featureType);
    }

    /// <summary>
    /// 返回当前等级伤害。
    /// </summary>
    protected float GetCurrentDamage()
    {
        WeaponLevelData levelData = GetCurrentLevelData();
        if (levelData == null) return 0f;

        if (_playerStats != null && _playerStats.UsesBrotatoStats) return BrotatoStatRules.Damage(levelData, _playerStats);

        float might = _playerStats != null
            && weaponData != null
            && !weaponData.IgnoresPlayerStat(IgnoredPlayerWeaponStats.Might)
            ? _playerStats.Might
            : 1f;
        return levelData.damage * might;
    }

    /// <summary>
    /// 返回经过安全下限约束的当前攻击冷却。
    /// </summary>
    protected float GetCurrentCooldown()
    {
        WeaponLevelData levelData = GetCurrentLevelData();
        if (levelData == null) return 0.1f;

        float interval = levelData.cooldown * GetCurrentCooldownMultiplier();
        if (weaponData.runtimeType == WeaponRuntimeType.Melee)
            interval = MeleeAttackTiming.Interval(levelData, CurrentVisualRange, GetCurrentCooldownMultiplier());
        return Mathf.Max(0.05f, interval);
    }

    /// <summary>返回玩家 Cooldown 处理后的攻击间隔倍率。</summary>
    protected float GetCurrentCooldownMultiplier()
    {
        if (_playerStats != null && _playerStats.UsesBrotatoStats)
            return BrotatoStatRules.AttackIntervalMultiplier(_playerStats.GetFinalStat(PlayerStatType.AttackSpeed) + (GetCurrentLevelData()?.attackSpeed ?? 0));
        return _playerStats != null
            && weaponData != null
            && !weaponData.IgnoresPlayerStat(IgnoredPlayerWeaponStats.Cooldown)
            ? _playerStats.Cooldown
            : 1f;
    }

    /// <summary>
    /// 返回当前投射物速度。
    /// </summary>
    protected float GetCurrentProjectileSpeed()
    {
        WeaponLevelData levelData = GetCurrentLevelData();
        if (levelData == null) return 0f;
        return levelData.projectileSpeed * GetCurrentProjectileSpeedMultiplier();
    }

    /// <summary>返回投射物速度倍率，供环绕等非直线运动武器复用。</summary>
    protected float GetCurrentProjectileSpeedMultiplier()
    {
        return _playerStats != null
            && weaponData != null
            && !weaponData.IgnoresPlayerStat(IgnoredPlayerWeaponStats.ProjectileSpeed)
            ? _playerStats.ProjectileSpeed
            : 1f;
    }

    /// <summary>返回当前等级基础数量叠加玩家 Amount 后的安全整数数量。</summary>
    protected int GetCurrentProjectileCount()
    {
        WeaponLevelData levelData = GetCurrentLevelData();
        if (levelData == null) return 1;

        float bonusAmount = _playerStats != null
            && weaponData != null
            && !weaponData.IgnoresPlayerStat(IgnoredPlayerWeaponStats.Amount)
            ? _playerStats.Amount
            : 0f;
        return Mathf.Max(1, levelData.projectileCount + Mathf.FloorToInt(bonusAmount + 0.0001f));
    }

    /// <summary>返回玩家 Duration 处理后的武器效果持续时间。</summary>
    protected float GetModifiedDuration(float baseDuration)
    {
        float multiplier = _playerStats != null
            && weaponData != null
            && !weaponData.IgnoresPlayerStat(IgnoredPlayerWeaponStats.Duration)
            ? _playerStats.Duration
            : 1f;
        return Mathf.Max(0.01f, baseDuration * multiplier);
    }

    /// <summary>读取武器范围，近战减半；持续光环/环绕使用完整增量作为项目适配。</summary>
    protected float GetModifiedRange(float baseline)
    {
        return _playerStats != null && _playerStats.UsesBrotatoStats
            ? BrotatoStatRules.WeaponRange(baseline, GetCurrentLevelData()?.rangeBonus ?? 0,
                _playerStats.GetFinalStat(PlayerStatType.Range), weaponData.runtimeType == WeaponRuntimeType.Melee)
            : GetModifiedArea(baseline);
    }

    /// <summary>仅在发射/刷新时构造该武器品质的不可变命中快照。</summary>
    protected WeaponHitSnapshot CreateHitSnapshot()
    { return new WeaponHitSnapshot(_playerStats, _ownerHealth, GetCurrentLevelData()); }

    /// <summary>返回直飞攻击的飞行寿命；新模式以射程除速度，旧模式保留 Duration。</summary>
    protected float GetProjectileLifetime(WeaponLevelData data)
    {
        return _playerStats != null && _playerStats.UsesBrotatoStats
            ? GetModifiedRange(data.attackRange) / Mathf.Max(.01f, GetCurrentProjectileSpeed())
            : GetModifiedDuration(data.lifeTime);
    }

    /// <summary>返回玩家 Area 处理后的范围、半径或尺寸值。</summary>
    protected float GetModifiedArea(float baseArea)
    {
        return Mathf.Max(0.01f, baseArea * GetCurrentAreaMultiplier());
    }

    /// <summary>返回供池化攻击实体保存的 Area 尺寸倍率。</summary>
    protected float GetCurrentAreaMultiplier()
    {
        return _playerStats != null
            && weaponData != null
            && !weaponData.IgnoresPlayerStat(IgnoredPlayerWeaponStats.Area)
            ? _playerStats.Area
            : 1f;
    }

    /// <summary>
    /// 默认直飞攻击：按数量和总散射角生成池化投射物，并注入当前等级快照。
    /// </summary>
    protected virtual void Attack()
    {
        if (weaponData == null || weaponData.projectilePrefab == null || PoolManager.Instance == null)
        {
            return;
        }

        WeaponLevelData levelData = GetCurrentLevelData();
        if (levelData == null)
        {
            return;
        }

        Vector3 baseDirection = GetAimDirection();
        int count = GetCurrentProjectileCount();

        for (int index = 0; index < count; index++)
        {
            Vector3 fireDirection = CalculateSpreadDirection(
                baseDirection,
                index,
                count,
                levelData.spreadAngle);
            GameObject projectileObject = PoolManager.Instance.Spawn(
                weaponData.projectilePrefab,
                transform.position,
                Quaternion.identity);

            if (projectileObject != null
                && projectileObject.TryGetComponent<ProjectileBase>(out var projectile))
            {
                projectile.Initialize(
                    weaponData,
                    fireDirection,
                    GetCurrentDamage(),
                    GetCurrentProjectileSpeed(),
                    levelData.pierceCount,
                    GetProjectileLifetime(levelData),
                    levelData.bounceCount,
                    levelData.bounceMode,
                    CurrentVisualRangeRatio, CreateHitSnapshot());
                projectile.MatchHeldSize(LaunchVisualLength);
            }
        }
    }

    /// <summary>
    /// 计算多发武器中指定序号的发射方向，均匀分布在总散射角内。
    /// </summary>
    protected Vector3 CalculateSpreadDirection(
        Vector3 baseDirection,
        int index,
        int count,
        float totalSpreadAngle)
    {
        if (count <= 1)
        {
            return baseDirection.normalized;
        }

        float angleOffset = -totalSpreadAngle * 0.5f
            + totalSpreadAngle * index / (count - 1);
        return (Quaternion.Euler(0f, 0f, angleOffset) * baseDirection).normalized;
    }

    /// <summary>
    /// 读取当前瞄准方向；缺少瞄准组件时回退到世界右方向。
    /// </summary>
    protected Vector3 GetAimDirection()
    {
        if ((_aimController == null || _aimController.aimMode == AimController.AimMode.NearestEnemy)
            && WeaponTargeting.IsValidCached(_attackTarget, _attackTargetIdentity, _attackTargetGeneration))
        {
            Vector3 delta = _attackTargetIdentity.transform.position - (Vector3)AttackOrigin;
            if (delta.sqrMagnitude > .0001f) return delta.normalized;
        }
        if (_aimController == null) return Vector3.right;

        Vector2 aim = _aimController.DirectionFrom(AttackOrigin);
        return aim.sqrMagnitude > 0.0001f
            ? new Vector3(aim.x, aim.y, 0f).normalized
            : Vector3.right;
    }

    /// <summary>
    /// 返回玩家最后一次有效水平输入的稳定朝向；缺少瞄准组件时默认向右。
    /// </summary>
    protected float GetHorizontalFacingSign()
    {
        return _aimController != null
            ? _aimController.HorizontalFacingSign
            : 1f;
    }

    /// <summary>本局独立装备身份；同种武器不再以内容 ID 代替实例 ID。</summary>
    public string InstanceId { get; } = System.Guid.NewGuid().ToString("N");

    /// <summary>回合开始重置攻击冷却，避免继承商店或上一回合的计时。</summary>
    public virtual void ResetRoundCooldown()
    {
        _currentCooldown = 0f;
        _attackTarget = null;
        _attackTargetIdentity = null;
        _attackTargetGeneration = 0;
        _nextAttackTargetSearch = 0f;
        WeaponHeldView view = GetComponent<WeaponHeldView>();
        if (view != null) view.ResetView();
    }

}
