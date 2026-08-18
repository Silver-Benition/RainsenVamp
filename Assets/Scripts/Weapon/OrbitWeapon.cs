using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 环绕类武器。武器本体维护共享相位与槽位，池化投射物只负责表现和命中。
/// </summary>
public sealed class OrbitWeapon : WeaponBase
{
    private readonly List<OrbitingProjectile> _orbiters = new List<OrbitingProjectile>(8);
    private float _orbitPhase;

    /// <summary>
    /// 复用基础冷却进行实体数量校准，并逐帧更新少量环绕物的位置。
    /// </summary>
    protected override void Update()
    {
        base.Update();

        WeaponLevelData levelData = GetCurrentLevelData();
        if (levelData == null || _orbiters.Count == 0)
        {
            return;
        }

        _orbitPhase = Mathf.Repeat(
            _orbitPhase + levelData.orbitAngularSpeed * Time.deltaTime,
            360f);

        int count = _orbiters.Count;
        float slotAngle = 360f / count;
        for (int index = 0; index < count; index++)
        {
            OrbitingProjectile orbiter = _orbiters[index];
            if (orbiter != null)
            {
                orbiter.SetOrbitPosition(
                    _orbitPhase + slotAngle * index,
                    levelData.orbitRadius);
            }
        }
    }

    /// <summary>
    /// 按当前等级补齐或回收环绕物，并刷新全部实例的伤害与归属。
    /// </summary>
    protected override void Attack()
    {
        SynchronizeOrbiters();
    }

    /// <summary>
    /// 升级后立即刷新数量与伤害，无需等待下一次冷却。
    /// </summary>
    protected override void OnLevelChanged()
    {
        SynchronizeOrbiters();
    }

    /// <summary>
    /// 武器被禁用时归还所有环绕物，防止脱离玩家后继续造成伤害。
    /// </summary>
    private void OnDisable()
    {
        ReleaseAllOrbiters();
    }

    /// <summary>
    /// 让环绕实体数量与等级配置一致。循环上限是武器数量级，不扫描场上敌人。
    /// </summary>
    private void SynchronizeOrbiters()
    {
        if (weaponData == null || weaponData.projectilePrefab == null || PoolManager.Instance == null)
        {
            ReleaseAllOrbiters();
            return;
        }

        WeaponLevelData levelData = GetCurrentLevelData();
        if (levelData == null)
        {
            ReleaseAllOrbiters();
            return;
        }

        int desiredCount = Mathf.Max(1, levelData.projectileCount);
        while (_orbiters.Count > desiredCount)
        {
            int lastIndex = _orbiters.Count - 1;
            OrbitingProjectile extra = _orbiters[lastIndex];
            _orbiters.RemoveAt(lastIndex);
            if (extra != null)
            {
                extra.ReleaseToPool();
            }
        }

        while (_orbiters.Count < desiredCount)
        {
            GameObject instance = PoolManager.Instance.Spawn(
                weaponData.projectilePrefab,
                transform.position,
                Quaternion.identity);
            if (instance == null
                || !instance.TryGetComponent<OrbitingProjectile>(out var orbiter))
            {
                if (instance != null)
                {
                    PoolManager.Instance.Release(weaponData.projectilePrefab, instance);
                }
                break;
            }

            _orbiters.Add(orbiter);
        }

        for (int index = 0; index < _orbiters.Count; index++)
        {
            OrbitingProjectile orbiter = _orbiters[index];
            if (orbiter != null)
            {
                orbiter.Initialize(transform, levelData.damage);
            }
        }
    }

    /// <summary>
    /// 将仍存活的环绕物全部归还对象池，并清空本武器持有的引用。
    /// </summary>
    private void ReleaseAllOrbiters()
    {
        for (int index = 0; index < _orbiters.Count; index++)
        {
            if (_orbiters[index] != null)
            {
                _orbiters[index].ReleaseToPool();
            }
        }

        _orbiters.Clear();
    }
}
