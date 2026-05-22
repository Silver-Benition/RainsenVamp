using UnityEngine;

/// <summary>
/// 近战光环类武器（示例：大蒜/圣经的“贴身持续伤害”雏形）。
/// 关键点：
/// - 默认复用 WeaponBase 的“冷却计时”框架（作为“刷新光环参数”的间隔）
/// - 光环实体用对象池生成，挂 AuraDamageZone 负责跟随与持续伤害
/// </summary>
public class AuraWeapon : WeaponBase
{
    [Header("光环伤害跳动")]
    [Tooltip("光环伤害结算频率（秒）。数值越小越频繁。")]
    public float tickInterval = 0.25f;
    [Tooltip("当等级未配置 AuraPersistent 功能标签时，是否也强制常驻。")]
    public bool forcePersistentAura = true;

    // 单实例光环：同一把武器只维持一个 Aura 实体，避免场上堆叠多个实例。
    private GameObject auraInstance;

    private void OnDisable()
    {
        // 武器被禁用时不强制回收 Aura（避免与对象池生命周期打架）。
        // AuraDamageZone 会按自身 lifeTime 回收；如果你未来希望“武器卸下立刻消失”，再在这里主动 Release。
        auraInstance = null;
    }

    protected override void Attack()
    {
        if (weaponData == null) return;
        if (weaponData.projectilePrefab == null) return;
        WeaponLevelData levelData = GetCurrentLevelData();
        if (levelData == null) return;

        bool isPersistentAura = forcePersistentAura || HasFeature(WeaponFeatureType.AuraPersistent);
        float auraLifeTime = isPersistentAura ? Mathf.Max(levelData.lifeTime, 99999f) : levelData.lifeTime;
        float auraRadius = Mathf.Max(0.1f, levelData.auraRadius);

        // 如果已有光环实例且仍在场，则刷新它（重置计时/跟随/目标列表），不要再次 Spawn 造成多个实例叠加。
        if (auraInstance != null && auraInstance.activeInHierarchy)
        {
            if (auraInstance.TryGetComponent<AuraDamageZone>(out var existingAura))
            {
                existingAura.Initialize(weaponData, transform, tickInterval, GetCurrentDamage(), auraLifeTime, auraRadius);
            }
            return;
        }

        // 首次创建/已回收：从对象池取一个光环实例
        auraInstance = PoolManager.Instance.Spawn(weaponData.projectilePrefab, transform.position, Quaternion.identity);

        if (auraInstance != null && auraInstance.TryGetComponent<AuraDamageZone>(out var aura))
        {
            aura.Initialize(weaponData, transform, tickInterval, GetCurrentDamage(), auraLifeTime, auraRadius);
        }
    }
}

