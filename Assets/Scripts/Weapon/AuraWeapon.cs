using UnityEngine;

/// <summary>
/// 持续光环武器。同一把武器只维持一个池化伤害区，升级时原地刷新参数。
/// </summary>
public sealed class AuraWeapon : WeaponBase
{
    [Header("光环生命周期")]
    [Tooltip("未配置 AuraPersistent 标签时是否仍强制常驻。")]
    [SerializeField] private bool forcePersistentAura = true;

    private GameObject _auraInstance;

    /// <summary>
    /// 武器禁用时主动回收光环，避免长生命周期实体脱离玩家后继续伤害。
    /// </summary>
    private void OnDisable()
    {
        if (_auraInstance != null
            && _auraInstance.TryGetComponent<AuraDamageZone>(out var aura))
        {
            aura.ReleaseToPool();
        }

        _auraInstance = null;
    }

    /// <summary>
    /// 创建或刷新唯一光环实例，并注入当前等级的伤害、频率和半径。
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

        bool persistent = forcePersistentAura || HasFeature(WeaponFeatureType.AuraPersistent);
        float lifeTime = persistent ? Mathf.Max(levelData.lifeTime, 99999f) : levelData.lifeTime;

        if (_auraInstance != null && _auraInstance.activeInHierarchy)
        {
            if (_auraInstance.TryGetComponent<AuraDamageZone>(out var existingAura))
            {
                existingAura.Initialize(
                    weaponData,
                    transform,
                    levelData.tickInterval,
                    levelData.damage,
                    lifeTime,
                    levelData.auraRadius);
            }
            return;
        }

        _auraInstance = PoolManager.Instance.Spawn(
            weaponData.projectilePrefab,
            transform.position,
            Quaternion.identity);
        if (_auraInstance != null
            && _auraInstance.TryGetComponent<AuraDamageZone>(out var aura))
        {
            aura.Initialize(
                weaponData,
                transform,
                levelData.tickInterval,
                levelData.damage,
                lifeTime,
                levelData.auraRadius);
        }
        else if (_auraInstance != null)
        {
            PoolManager.Instance.Release(weaponData.projectilePrefab, _auraInstance);
            _auraInstance = null;
        }
    }

    /// <summary>
    /// 升级后立即刷新现有光环参数。
    /// </summary>
    protected override void OnLevelChanged()
    {
        Attack();
    }
}
