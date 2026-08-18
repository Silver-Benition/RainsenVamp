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

        GameObject instance = PoolManager.Instance.Spawn(
            weaponData.projectilePrefab,
            transform.position,
            Quaternion.identity);
        if (instance != null
            && instance.TryGetComponent<MeleeSwingHitbox>(out var swingHitbox))
        {
            swingHitbox.Initialize(
                transform,
                GetHorizontalFacingSign() >= 0f,
                levelData.damage,
                levelData.meleeRange,
                levelData.meleeArc,
                levelData.activeDuration);
        }
        else if (instance != null)
        {
            PoolManager.Instance.Release(weaponData.projectilePrefab, instance);
        }
    }
}
