using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class ProjectileBase : MonoBehaviour, IPoolable
{
    // =====================================================================
    // 内部运行时状态
    // =====================================================================
    private GameObject prefabReference;     // 归还对象池的凭证
    private WeaponDataSO weaponData;        // 武器数据（保留引用，供未来扩展使用）

    private int currentPierce;             // 剩余额外穿透层数（0=下次命中即消失）
    private int currentBounce;             // 剩余弹射次数
    private BounceMode currentBounceMode;  // 弹射类型（指向性 / 追踪性）
    private float lifeTimer;               // 剩余存活时间
    private Vector3 moveDirection;         // 当前飞行方向
    private float currentDamage;           // 当前伤害值
    private float currentSpeed;            // 当前飞行速度
    private Vector3 baseLocalScale;

    // 弹射时排除已命中目标，防止来回弹同一个怪
    // 用 HashSet 而非 List：Contains() 查找复杂度 O(1) vs List 的 O(N)，
    // OnTriggerEnter2D 高频调用时性能更稳定。对象池复用时必须 Clear()。
    private readonly HashSet<Collider2D> hitColliders = new HashSet<Collider2D>();

    /// <summary>缓存 Prefab 初始缩放，确保对象池复用时 Area 不会重复累乘。</summary>
    private void Awake()
    {
        baseLocalScale = transform.localScale;
    }

    /// <summary>对象池回收时恢复初始尺寸并清理命中集合。</summary>
    private void OnDisable()
    {
        transform.localScale = baseLocalScale;
        hitColliders.Clear();
    }

    // =====================================================================
    // IPoolable 接口
    // =====================================================================
    public void SetPrefabReference(GameObject prefab)
    {
        prefabReference = prefab;
    }

    // =====================================================================
    // Initialize 重载：默认从 Lv1 配置读取（供快速测试用）
    // =====================================================================
    public virtual void Initialize(WeaponDataSO data, Vector3 direction)
    {
        WeaponLevelData levelData = data != null ? data.GetLevelConfig(1) : null;
        Initialize(
            data,
            direction,
            levelData != null ? levelData.damage : 0f,
            levelData != null ? levelData.projectileSpeed : 0f,
            levelData != null ? levelData.pierceCount : 0,
            levelData != null ? levelData.lifeTime : 1f,
            levelData != null ? levelData.bounceCount : 0,
            levelData != null ? levelData.bounceMode : BounceMode.Directional,
            1f
        );
    }

    // =====================================================================
    // Initialize 重载：注入当前等级快照（由 WeaponBase.Attack() 调用）
    // =====================================================================
    public virtual void Initialize(WeaponDataSO data, Vector3 direction,
        float damage, float speed, int pierce, float lifeTimeValue,
        int bounce = 0, BounceMode bounceMode = BounceMode.Directional,
        float areaMultiplier = 1f)
    {
        weaponData = data;
        moveDirection = direction.normalized;
        currentDamage = damage;
        currentSpeed = speed;
        currentPierce = pierce;       // 0 = 不穿透，命中1次即消失
        lifeTimer = lifeTimeValue;
        currentBounce = bounce;
        currentBounceMode = bounceMode;
        float safeAreaMultiplier = Mathf.Max(0.01f, areaMultiplier);
        transform.localScale = new Vector3(
            baseLocalScale.x * safeAreaMultiplier,
            baseLocalScale.y * safeAreaMultiplier,
            baseLocalScale.z);

        // 对象池复用时必须清空，否则上一发子弹的命中记录会影响新子弹
        hitColliders.Clear();
    }

    // =====================================================================
    // 每帧更新：移动 + 生命周期
    // =====================================================================
    private void Update()
    {
        // 直线飞行（Translate 比 Rigidbody2D.MovePosition 轻量，适合不参与复杂物理的子弹）
        transform.Translate(moveDirection * currentSpeed * Time.deltaTime, Space.World);

        // 生命周期超时自动回收
        lifeTimer -= Time.deltaTime;
        if (lifeTimer <= 0f)
        {
            ReturnToPool();
        }
    }

    // =====================================================================
    // 碰撞处理：伤害 → 弹射判断 → 穿透判断
    // =====================================================================
    private void OnTriggerEnter2D(Collider2D collision)
    {
        // 弹射模式：跳过已命中过的目标（防止来回弹同一个怪）
        if (currentBounce > 0 && hitColliders.Contains(collision)) return;

        // 先按 Enemy Layer 过滤，防止玩家实现 IDamageable 后被己方投射物误伤。
        if (!DamageTargetFilter.TryGetEnemyDamageable(collision, out IDamageable damageableEntity)) return;

        damageableEntity.TakeDamage(currentDamage);

        // 记录本次命中（用于弹射排除）
        hitColliders.Add(collision);

        // --- 弹射判断 ---
        if (currentBounce > 0)
        {
            switch (currentBounceMode)
            {
                case BounceMode.Directional:
                    // 指向性弹射：立即找最近未命中目标，瞬间转向继续飞
                    Collider2D nextTarget = FindNextBounceTarget();
                    if (nextTarget != null)
                    {
                        moveDirection = (nextTarget.transform.position - transform.position).normalized;
                        currentBounce--;
                        return; // 弹射不消耗穿透次数，直接继续飞
                    }
                    // 找不到下一个目标，弹射耗尽，走穿透/回收流程
                    break;

                case BounceMode.Tracking:
                    // 追踪性弹射：预留接口，实际逻辑由 TrackingProjectile 子类实现
                    // 此处仅消耗次数，不改变行为（基类不处理全程追踪运动）
                    currentBounce--;
                    return;
            }
        }

        // --- 穿透判断 ---
        // pierceCount = 0：不穿透，命中1次即消失
        // pierceCount = N：还能额外命中 N 次
        currentPierce--;
        if (currentPierce < 0)  // 注意：< 0 而非 <= 0，因为 0 表示"还剩最后一次穿透"
        {
            ReturnToPool();
        }
    }

    // =====================================================================
    // 辅助方法：寻找下一个弹射目标（指向性弹射专用）
    // =====================================================================
    /// <summary>
    /// 以当前位置为圆心，在 searchRadius 范围内查找最近的未命中 IDamageable。
    /// OverlapCircleAll 是 Physics2D 轻量查询，无 Rigidbody 分配，性能可接受。
    /// </summary>
    private Collider2D FindNextBounceTarget(float searchRadius = 10f)
    {
        Collider2D[] candidates = Physics2D.OverlapCircleAll(
            transform.position,
            searchRadius,
            DamageTargetFilter.EnemyLayerMask);

        Collider2D bestTarget = null;
        float bestDist = float.MaxValue;

        foreach (var candidate in candidates)
        {
            if (hitColliders.Contains(candidate)) continue;                    // 排除已命中目标
            if (!DamageTargetFilter.TryGetEnemyDamageable(candidate, out _)) continue;

            float dist = Vector3.Distance(transform.position, candidate.transform.position);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestTarget = candidate;
            }
        }

        return bestTarget;
    }

    // =====================================================================
    // 回收
    // =====================================================================
    private void ReturnToPool()
    {
        PoolManager.Instance.Release(prefabReference, gameObject);
    }
}
