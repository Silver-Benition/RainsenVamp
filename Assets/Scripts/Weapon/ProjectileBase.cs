using System.Collections.Generic;
using UnityEngine;

/// <summary>池化直飞弹体，支持命中后的瞬时转向或持续追踪；每次生命独立记录已命中的实体。</summary>
[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class ProjectileBase : MonoBehaviour, IPoolable
{
    private GameObject prefabReference;
    private WeaponDataSO weaponData;
    private int currentPierce;
    private int currentBounce;
    private BounceMode currentBounceMode;
    private float lifeTimer;
    private float totalLifetime;
    private Vector3 moveDirection;
    private float currentDamage;
    private float currentSpeed;
    private Vector3 baseLocalScale;
    private Transform visual;
    private SpriteRenderer visualRenderer;
    private Quaternion baseVisualRotation;
    private Collider2D trackingTarget;
    private uint trackingGeneration;
    private bool trackingActive;
    private Component trackingIdentity;
    private readonly HashSet<Component> hitTargets = new HashSet<Component>();
    private WeaponHitSnapshot _hitSnapshot;
    public Vector3 FlightDirection => moveDirection;

    /// <summary>缓存初始尺寸和视觉节点；运行时只旋转图像，不改变弹体的世界移动方式。</summary>
    private void Awake()
    {
        baseLocalScale = transform.localScale;
        SpriteRenderer renderer = GetComponentInChildren<SpriteRenderer>();
        // 扩展弹体的根渲染器被禁用，使用实际显示素材的子节点。
        if (renderer == null || !renderer.enabled)
        {
            foreach (SpriteRenderer candidate in GetComponentsInChildren<SpriteRenderer>())
                if (candidate.enabled) { renderer = candidate; break; }
        }
        visualRenderer = renderer;
        visual = renderer != null ? renderer.transform : transform;
        baseVisualRotation = visual.localRotation;
    }

    /// <summary>回池时清理目标、伤害来源与姿态，防止下一发继承旧目标。</summary>
    private void OnDisable()
    {
        _hitSnapshot = default;
        transform.localScale = baseLocalScale;
        if (visual != null) visual.localRotation = baseVisualRotation;
        hitTargets.Clear(); trackingTarget = null; trackingGeneration = 0; trackingActive = false; trackingIdentity = null; moveDirection = Vector3.zero;
        weaponData = null; currentDamage = 0f; currentSpeed = 0f;
        currentPierce = 0; currentBounce = 0; lifeTimer = 0f; totalLifetime = 0f;
    }

    /// <summary>记录对象池归还凭证。</summary>
    public void SetPrefabReference(GameObject prefab) { prefabReference = prefab; }

    /// <summary>以一级数据初始化独立弹体，供旧入口和测试使用。</summary>
    public virtual void Initialize(WeaponDataSO data, Vector3 direction)
    {
        WeaponLevelData level = data != null ? data.GetLevelConfig(1) : null;
        Initialize(data, direction, level != null ? level.damage : 0f,
            level != null ? level.projectileSpeed : 0f, level != null ? level.pierceCount : 0,
            level != null ? level.lifeTime : 1f, level != null ? level.bounceCount : 0,
            level != null ? level.bounceMode : BounceMode.None);
    }

    /// <summary>注入完整攻击快照；首次飞行沿发射方向，追踪只在成功弹射选敌后启用。</summary>
    public virtual void Initialize(WeaponDataSO data, Vector3 direction,
        float damage, float speed, int pierce, float lifeTimeValue,
        int bounce = 0, BounceMode bounceMode = BounceMode.Directional,
        float areaMultiplier = 1f, WeaponHitSnapshot hitSnapshot = default)
    {
        _hitSnapshot = hitSnapshot; weaponData = data;
        currentDamage = damage; currentSpeed = Mathf.Max(0f, speed);
        currentPierce = Mathf.Max(0, pierce); currentBounce = Mathf.Max(0, bounce);
        currentBounceMode = bounceMode; lifeTimer = Mathf.Max(.01f, lifeTimeValue); totalLifetime = 0f;
        trackingTarget = null; trackingGeneration = 0; trackingActive = false; trackingIdentity = null; hitTargets.Clear();
        transform.localScale = new Vector3(baseLocalScale.x * Mathf.Max(.01f, areaMultiplier),
            baseLocalScale.y * Mathf.Max(.01f, areaMultiplier), baseLocalScale.z);
        SetDirection(direction);
    }

    /// <summary>发射后按当前持武长度归一化同款投掷素材，保持碰撞体同步缩放。</summary>
    public void MatchHeldSize(float length) { WeaponVisualGeometry.MatchThrownSize(transform, visualRenderer, weaponData, length); }

    /// <summary>同步世界飞行方向与素材长轴；转向时调用同一入口，避免图像与运动分离。</summary>
    private void SetDirection(Vector3 direction)
    {
        if (direction.sqrMagnitude > .0001f) moveDirection = direction.normalized;
        else if (moveDirection.sqrMagnitude < .0001f) moveDirection = Vector3.right;
        if (visual != null)
            visual.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(moveDirection.y, moveDirection.x) * Mathf.Rad2Deg
                + (weaponData != null ? weaponData.visualAngleOffset : 0f));
    }

    /// <summary>追踪已选目标时只读取缓存位置；目标失效才补查，绝不每帧扫描全部敌人。</summary>
    private void Update()
    {
        if (trackingActive)
        {
            if (!WeaponTargeting.IsValidCached(trackingTarget, trackingIdentity, trackingGeneration))
            {
                trackingTarget = WeaponTargeting.FindNearest(transform.position, 10f, hitTargets);
                trackingIdentity = WeaponTargeting.Identity(trackingTarget);
                trackingGeneration = trackingIdentity is EnemyBase enemy ? enemy.LifeGeneration : 0;
                trackingActive = trackingTarget != null;
                if (trackingActive)
                    lifeTimer = Mathf.Max(lifeTimer, Vector3.Distance(transform.position,
                        trackingIdentity.transform.position) / Mathf.Max(.01f, currentSpeed) + .5f);
            }
            if (trackingTarget != null)
                SetDirection(trackingIdentity.transform.position - transform.position);
        }
        transform.Translate(moveDirection * currentSpeed * Time.deltaTime, Space.World);
        lifeTimer -= Time.deltaTime; totalLifetime += Time.deltaTime;
        if (lifeTimer <= 0f || totalLifetime >= 30f) ReturnToPool();
    }

    /// <summary>同一受击身份只命中一次；成功选择下一个敌人才消耗弹射次数，否则按穿透处理。</summary>
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!WeaponTargeting.IsValid(collision)) return;
        Component identity = WeaponTargeting.Identity(collision);
        if (!hitTargets.Add(identity)) return;
        // 先记录身份再结算：敌人可能在伤害回调中立刻死亡和回池。
        _hitSnapshot.Apply((IDamageable)identity, currentDamage, weaponData);
        if (currentBounce > 0 && currentBounceMode != BounceMode.None)
        {
            Collider2D next = WeaponTargeting.FindNearest(transform.position, 10f, hitTargets);
            if (next != null)
            {
                currentBounce--;
                Vector3 delta = WeaponTargeting.Identity(next).transform.position - transform.position;
                SetDirection(delta);
                trackingTarget = currentBounceMode == BounceMode.Tracking ? next : null;
                trackingActive = trackingTarget != null;
                trackingIdentity = WeaponTargeting.Identity(trackingTarget);
                trackingGeneration = trackingIdentity is EnemyBase enemy ? enemy.LifeGeneration : 0;
                // 每次有效弹射获得一段可到达目标的寿命；总寿命仍受上面的安全上限约束。
                lifeTimer = Mathf.Max(lifeTimer, delta.magnitude / Mathf.Max(.01f, currentSpeed) + .5f);
                return;
            }
        }
        trackingTarget = null; trackingActive = false; trackingIdentity = null;
        currentPierce--;
        if (currentPierce < 0) ReturnToPool();
    }

    /// <summary>通过原始 Prefab 键回池；独立测试实体没有池时仅禁用。</summary>
    private void ReturnToPool()
    {
        if (prefabReference != null && PoolManager.Instance != null) PoolManager.Instance.Release(prefabReference, gameObject);
        else gameObject.SetActive(false);
    }
}
