using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WeaponBase : MonoBehaviour
{
    [Header("武器配置")]
    public WeaponDataSO weaponData; // 策划配置的数据

    protected float currentCooldown;
    protected int currentLevel = 1;

    // 瞄准控制器缓存（从 Player 父级获取）
    protected AimController aimController;

    public int CurrentLevel => currentLevel;
    public int MaxLevel => weaponData != null ? weaponData.MaxLevel : 1;
    public bool IsMaxLevel => CurrentLevel >= MaxLevel;

    protected virtual void Awake()
    {
        // 武器挂在 Player 子物体上，向上查找 AimController
        aimController = GetComponentInParent<AimController>();
    }

    protected virtual void Update()
    {
        if (weaponData == null) return;

        // 冷却计时器
        currentCooldown -= Time.deltaTime;
        if (currentCooldown <= 0f)
        {
            Attack();
            currentCooldown = GetCurrentCooldown(); // 重置冷却（受等级成长影响）
        }
    }

    /// <summary>
    /// 升级武器等级。达到上限返回 false。
    /// </summary>
    public virtual bool TryLevelUp()
    {
        if (weaponData == null) return false;
        if (IsMaxLevel) return false;

        currentLevel++;
        OnLevelChanged();
        return true;
    }

    protected virtual void OnLevelChanged()
    {
        // 默认实现留空，派生武器可覆盖（例如升级时立即刷新光环半径/特效）
    }

    protected WeaponLevelData GetCurrentLevelData()
    {
        if (weaponData == null) return null;
        return weaponData.GetLevelConfig(currentLevel);
    }

    protected bool HasFeature(WeaponFeatureType featureType)
    {
        WeaponLevelData levelData = GetCurrentLevelData();
        if (levelData == null || levelData.features == null) return false;
        return levelData.features.Contains(featureType);
    }

    protected float GetCurrentDamage()
    {
        WeaponLevelData levelData = GetCurrentLevelData();
        return levelData != null ? levelData.damage : 0f;
    }

    protected float GetCurrentCooldown()
    {
        WeaponLevelData levelData = GetCurrentLevelData();
        if (levelData == null) return 0.1f;
        return Mathf.Max(0.05f, levelData.cooldown);
    }

    protected float GetCurrentProjectileSpeed()
    {
        WeaponLevelData levelData = GetCurrentLevelData();
        return levelData != null ? levelData.projectileSpeed : 0f;
    }

    /// <summary>
    /// 核心攻击逻辑，声明为 virtual 以便特殊武器（如散射、环绕物）重写。
    /// 支持多发（projectileCount）+ 扩散角（spreadAngle）+ 弹射（bounceCount）。
    /// </summary>
    protected virtual void Attack()
    {
        if (weaponData == null || weaponData.projectilePrefab == null) return;
        WeaponLevelData levelData = GetCurrentLevelData();
        if (levelData == null) return;

        // TODO: 结合 PlayerController 获取最近敌人方向；目前以 Vector3.right 作为占位方向
        Vector3 baseDirection = GetAimDirection();

        // --- 多发逻辑 ---
        int count = Mathf.Max(1, levelData.projectileCount);
        float totalSpread = levelData.spreadAngle;

        for (int i = 0; i < count; i++)
        {
            // 计算每发子弹的偏转角度：均匀分布在 [-totalSpread/2, +totalSpread/2] 范围内
            float angleOffset = 0f;
            if (count > 1)
            {
                // 例：3 发 / 30° → 偏转分别为 -15°、0°、+15°
                angleOffset = -totalSpread * 0.5f + totalSpread * i / (count - 1);
            }

            // 旋转基础方向
            Vector3 fireDirection = Quaternion.Euler(0f, 0f, angleOffset) * baseDirection;

            // 从对象池获取子弹
            GameObject projectileObj = PoolManager.Instance.Spawn(
                weaponData.projectilePrefab,
                transform.position,
                Quaternion.identity
            );

            if (projectileObj == null) continue;

            // 初始化子弹数据（含穿透次数与弹射参数）
            if (projectileObj.TryGetComponent<ProjectileBase>(out var projectile))
            {
                projectile.Initialize(
                    weaponData,
                    fireDirection,
                    GetCurrentDamage(),
                    GetCurrentProjectileSpeed(),
                    levelData.pierceCount,
                    levelData.lifeTime,
                    levelData.bounceCount,
                    levelData.bounceMode
                );
            }
        }
    }

    /// <summary>
    /// 获取当前发射方向。优先从 AimController 读取；
    /// 若未找到（如武器不在玩家身上），回退到默认右方向。
    /// </summary>
    protected Vector3 GetAimDirection()
    {
        if (aimController != null)
        {
            Vector2 aim = aimController.AimDirection;
            return new Vector3(aim.x, aim.y, 0f).normalized;
        }
        return Vector3.right;
    }
}
