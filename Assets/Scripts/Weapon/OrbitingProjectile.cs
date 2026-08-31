using UnityEngine;

/// <summary>
/// 单个环绕投射物。位置由 OrbitWeapon 驱动，触发器进入时造成一次伤害。
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public sealed class OrbitingProjectile : MonoBehaviour, IPoolable
{
    private GameObject _prefabReference;
    private Transform _owner;
    private WeaponDataSO _weaponData;
    private float _damage;
    private Vector3 _baseLocalScale;

    /// <summary>缓存 Prefab 初始尺寸，避免池化复用时 Area 倍率重复累积。</summary>
    private void Awake()
    {
        _baseLocalScale = transform.localScale;
    }

    /// <summary>对象禁用时恢复初始尺寸并清理旧归属。</summary>
    private void OnDisable()
    {
        transform.localScale = _baseLocalScale;
        _owner = null;
        _weaponData = null;
        _damage = 0f;
    }

    /// <summary>
    /// 保存原始 Prefab 引用，供对象池回收时作为稳定键。
    /// </summary>
    public void SetPrefabReference(GameObject prefab)
    {
        _prefabReference = prefab;
    }

    /// <summary>
    /// 注入本次生命周期的玩家归属和伤害快照。
    /// </summary>
    public void Initialize(Transform owner, float damage, float areaMultiplier = 1f)
    {
        Initialize(null, owner, damage, areaMultiplier);
    }

    /// <summary>注入带稳定武器来源的环绕投射物生命周期。</summary>
    public void Initialize(WeaponDataSO weaponData, Transform owner, float damage, float areaMultiplier = 1f)
    {
        _weaponData = weaponData;
        _owner = owner;
        _damage = Mathf.Max(0f, damage);
        float safeAreaMultiplier = Mathf.Max(0.01f, areaMultiplier);
        transform.localScale = new Vector3(
            _baseLocalScale.x * safeAreaMultiplier,
            _baseLocalScale.y * safeAreaMultiplier,
            _baseLocalScale.z);
    }

    /// <summary>
    /// 按共享相位计算世界坐标，并让刀刃切线方向朝向运动轨迹。
    /// </summary>
    public void SetOrbitPosition(float angleDegrees, float radius)
    {
        if (_owner == null)
        {
            ReleaseToPool();
            return;
        }

        float angleRadians = angleDegrees * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(
            Mathf.Cos(angleRadians),
            Mathf.Sin(angleRadians),
            0f) * Mathf.Max(0.05f, radius);
        transform.position = _owner.position + offset;
        transform.rotation = Quaternion.Euler(0f, 0f, angleDegrees + 90f);
    }

    /// <summary>
    /// 敌人进入刀刃触发器时结算一次伤害；持续重叠不会逐物理帧重复结算。
    /// </summary>
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_damage > 0f
            && DamageTargetFilter.TryGetEnemyDamageable(other, out IDamageable damageable))
        {
            CombatDamageResolver.Apply(damageable, _damage, _weaponData);
        }
    }

    /// <summary>
    /// 将实例归还所属对象池；缺失池引用时仅禁用，避免残留伤害。
    /// </summary>
    public void ReleaseToPool()
    {
        _owner = null;
        _weaponData = null;
        _damage = 0f;

        if (_prefabReference != null && PoolManager.Instance != null)
        {
            PoolManager.Instance.Release(_prefabReference, gameObject);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }
}
