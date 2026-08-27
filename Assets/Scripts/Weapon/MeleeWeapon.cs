using UnityEngine;

/// <summary>
/// 近战挥击武器。每次冷却生成一个短生命周期的池化扇扫判定。
/// </summary>
public sealed class MeleeWeapon : WeaponBase
{
    /// <summary>
    /// 从玩家头顶生成雨伞挥击，并按稳定水平面向决定顺时针或逆时针。
    /// </summary>
    protected override void Attack()
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

        int count = GetCurrentProjectileCount();
        for (int index = 0; index < count; index++)
        {
            GameObject instance = PoolManager.Instance.Spawn(
                weaponData.projectilePrefab,
                transform.position,
                Quaternion.identity);
            if (instance != null
                && instance.TryGetComponent<MeleeSwingHitbox>(out var swingHitbox))
            {
                // Amount 大于零时把额外挥击均匀排布在玩家周围，避免完全重叠后退化为伤害倍增。
                float startAngleOffset = count > 1 ? 360f * index / count : 0f;
                swingHitbox.Initialize(
                    transform,
                    GetHorizontalFacingSign() >= 0f,
                    GetCurrentDamage(),
                    GetModifiedArea(levelData.meleeRange),
                    levelData.meleeArc,
                    GetModifiedDuration(levelData.activeDuration),
                    startAngleOffset);
            }
            else if (instance != null)
            {
                PoolManager.Instance.Release(weaponData.projectilePrefab, instance);
            }
        }
    }
}
