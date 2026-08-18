using UnityEngine;

/// <summary>
/// 抛物线投掷武器。沿当前瞄准方向发射一把或多把池化飞斧。
/// </summary>
public sealed class LobbedWeapon : WeaponBase
{
    private Rigidbody2D _ownerRigidbody;

    /// <summary>
    /// 缓存玩家刚体，使每轮投掷只需读取一次发射瞬间的移动速度。
    /// </summary>
    protected override void Awake()
    {
        base.Awake();
        _ownerRigidbody = GetComponentInParent<Rigidbody2D>();
    }

    /// <summary>
    /// 按当前等级的数量、散射角和弹道参数生成飞斧；lifeTime 对投掷武器表示最大安全生命周期。
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

        Vector3 baseDirection = GetUpperHemisphereAimDirection();
        int count = Mathf.Max(1, levelData.projectileCount);
        Vector3 inheritedVelocity = _ownerRigidbody != null
            ? new Vector3(_ownerRigidbody.velocity.x, _ownerRigidbody.velocity.y, 0f)
            : Vector3.zero;

        for (int index = 0; index < count; index++)
        {
            Vector3 direction = CalculateUpperHemisphereSpreadDirection(
                baseDirection,
                index,
                count,
                levelData.spreadAngle);
            GameObject instance = PoolManager.Instance.Spawn(
                weaponData.projectilePrefab,
                transform.position,
                Quaternion.identity);

            if (instance != null
                && instance.TryGetComponent<LobbedProjectile>(out var projectile))
            {
                projectile.Initialize(
                    transform.position,
                    inheritedVelocity,
                    direction,
                    levelData.damage,
                    levelData.projectileSpeed,
                    levelData.lifeTime,
                    levelData.pierceCount,
                    levelData.lobGravity,
                    levelData.spinSpeed);
            }
            else if (instance != null)
            {
                PoolManager.Instance.Release(weaponData.projectilePrefab, instance);
            }
        }
    }

    /// <summary>
    /// 将当前瞄准方向限制在画面上半平面；正下方输入无法提供水平分量时，回退到玩家最后水平朝向。
    /// </summary>
    private Vector3 GetUpperHemisphereAimDirection()
    {
        Vector3 aimDirection = GetAimDirection();
        aimDirection.y = Mathf.Max(0f, aimDirection.y);

        if (aimDirection.sqrMagnitude > 0.0001f)
        {
            return aimDirection.normalized;
        }

        float horizontalSign = GetHorizontalFacingSign() >= 0f ? 1f : -1f;
        return new Vector3(horizontalSign, 0f, 0f);
    }

    /// <summary>
    /// 在世界角度 0 至 180 度内生成完整散射扇面，靠近水平边界时整体平移扇面而不是截断单发方向。
    /// </summary>
    private Vector3 CalculateUpperHemisphereSpreadDirection(
        Vector3 baseDirection,
        int index,
        int count,
        float totalSpreadAngle)
    {
        if (count <= 1)
        {
            return baseDirection.normalized;
        }

        // 世界角度 0/90/180 分别表示向右、向上、向左。
        // 基础方向已经过上半平面约束，此处再次 Clamp 用于抵御未来其他瞄准模式传入的浮点误差。
        float baseAngle = Mathf.Clamp(
            Mathf.Atan2(baseDirection.y, baseDirection.x) * Mathf.Rad2Deg,
            0f,
            180f);
        float safeSpreadAngle = Mathf.Clamp(totalSpreadAngle, 0f, 180f);

        // 当扇面靠近 0 或 180 度边界时整体向内平移，确保每一发都保留间距且不会朝下。
        float startAngle = Mathf.Clamp(
            baseAngle - safeSpreadAngle * 0.5f,
            0f,
            180f - safeSpreadAngle);
        int safeIndex = Mathf.Clamp(index, 0, count - 1);
        float angle = startAngle + safeSpreadAngle * safeIndex / (count - 1);

        return Quaternion.Euler(0f, 0f, angle) * Vector3.right;
    }
}
