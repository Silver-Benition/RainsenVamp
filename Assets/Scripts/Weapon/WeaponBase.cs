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

    public int CurrentLevel => _currentLevel;
    public int MaxLevel => weaponData != null ? weaponData.MaxLevel : 1;
    public bool IsMaxLevel => CurrentLevel >= MaxLevel;

    /// <summary>
    /// 缓存玩家父级上的瞄准控制器，避免每次攻击重复查找组件。
    /// </summary>
    protected virtual void Awake()
    {
        _aimController = GetComponentInParent<AimController>();
        _playerStats = GetComponentInParent<PlayerStats>();
    }

    /// <summary>
    /// 推进攻击冷却，并在冷却结束时调用当前武器的攻击实现。
    /// </summary>
    protected virtual void Update()
    {
        if (weaponData == null)
        {
            return;
        }

        _currentCooldown -= Time.deltaTime;
        if (_currentCooldown > 0f)
        {
            return;
        }

        Attack();
        _currentCooldown = GetCurrentCooldown();
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
        return weaponData != null ? weaponData.GetLevelConfig(_currentLevel) : null;
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

        return Mathf.Max(0.05f, levelData.cooldown * GetCurrentCooldownMultiplier());
    }

    /// <summary>返回玩家 Cooldown 处理后的攻击间隔倍率。</summary>
    protected float GetCurrentCooldownMultiplier()
    {
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
                    GetModifiedDuration(levelData.lifeTime),
                    levelData.bounceCount,
                    levelData.bounceMode,
                    GetCurrentAreaMultiplier());
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
        if (_aimController == null)
        {
            return Vector3.right;
        }

        Vector2 aim = _aimController.AimDirection;
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
}
