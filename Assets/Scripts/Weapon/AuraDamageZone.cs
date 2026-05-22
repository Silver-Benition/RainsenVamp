using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 近战光环伤害区（池化友好）。
/// 设计约定：
/// - 使用 WeaponDataSO 的 baseDamage 作为每跳伤害
/// - 使用 tickInterval 控制伤害跳动频率（与 WeaponBase.cooldown 独立，避免概念混淆）
/// - 使用 WeaponDataSO.lifeTime 作为存在时长，到点自动回收
/// </summary>
[RequireComponent(typeof(CircleCollider2D))]
public class AuraDamageZone : MonoBehaviour, IPoolable
{
    [Header("光环参数（可被 Initialize 覆盖）")]
    public float tickInterval = 0.25f;

    [Header("范围可视化（可选）")]
    [Tooltip("拖入一个 SpriteRenderer（建议圆形半透明贴图），用于显示光环范围。")]
    public SpriteRenderer rangeVisualRenderer;
    [Tooltip("是否自动根据 CircleCollider2D 半径同步可视化大小。")]
    public bool autoSyncVisualSize = true;
    [Range(0f, 1f)]
    [Tooltip("范围贴图透明度。")]
    public float visualAlpha = 0.25f;
    [Tooltip("选中对象时绘制真实碰撞范围 Gizmo，方便校对可视化圈与实际判定。")]
    public bool drawDebugGizmo = true;

    private Transform followTarget;
    private CircleCollider2D circleCollider;

    private float lifeTimer;
    private float tickTimer;
    private float currentDamage;

    private GameObject prefabReference;

    // 当前在范围内的可受击目标（避免每帧物理查询）
    private readonly List<IDamageable> targets = new List<IDamageable>(64);

    private void Awake()
    {
        circleCollider = GetComponent<CircleCollider2D>();
        RefreshRangeVisual();
    }

    /// <summary>
    /// 对象池取出时（首次激活）清空追踪列表，确保不残留上一次生命周期的数据。
    /// </summary>
    private void OnEnable()
    {
        targets.Clear();
    }

    public void SetPrefabReference(GameObject prefab)
    {
        prefabReference = prefab;
    }

    public void Initialize(WeaponDataSO data, Transform target, float overrideTickInterval)
    {
        WeaponLevelData levelData = data != null ? data.GetLevelConfig(1) : null;
        Initialize(
            data,
            target,
            overrideTickInterval,
            levelData != null ? levelData.damage : 0f,
            levelData != null ? levelData.lifeTime : 1f,
            levelData != null ? levelData.auraRadius : 3f
        );
    }

    // 供 AuraWeapon 注入”当前等级快照”属性
    public void Initialize(WeaponDataSO data, Transform target, float overrideTickInterval, float damage, float lifeTimeValue, float radius)
    {
        followTarget = target;
        tickInterval = Mathf.Max(0.01f, overrideTickInterval);
        if (circleCollider != null)
        {
            circleCollider.radius = Mathf.Max(0.05f, radius);
        }

        // 重置运行态（注意：不再清空 targets！
        // targets 在 OnEnable 时已清空；运行中 Re-Initialize 属于”刷新参数”场景，
        // 不应清空已追踪的范围内敌人，否则已在内部的怪物无法重新触发 OnTriggerEnter2D）
        currentDamage = damage;
        lifeTimer = lifeTimeValue;
        tickTimer = 0f;
        RefreshRangeVisual();
    }

    private void Update()
    {
        if (followTarget != null)
        {
            transform.position = followTarget.position;
        }

        if (currentDamage <= 0f)
        {
            ReturnToPool();
            return;
        }

        // 生命周期
        lifeTimer -= Time.deltaTime;
        if (lifeTimer <= 0f)
        {
            ReturnToPool();
            return;
        }

        // 伤害跳动
        tickTimer -= Time.deltaTime;
        if (tickTimer <= 0f)
        {
            TickDamage();
            tickTimer = tickInterval;
        }
    }

    private void TickDamage()
    {
        // 倒序遍历，顺便清理失效引用
        for (int i = targets.Count - 1; i >= 0; i--)
        {
            var t = targets[i];
            if (t == null)
            {
                targets.RemoveAt(i);
                continue;
            }
            t.TakeDamage(currentDamage);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.TryGetComponent<IDamageable>(out var damageable))
        {
            if (!targets.Contains(damageable))
            {
                targets.Add(damageable);
            }
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.TryGetComponent<IDamageable>(out var damageable))
        {
            targets.Remove(damageable);
        }
    }

    private void ReturnToPool()
    {
        if (prefabReference != null && PoolManager.Instance != null)
        {
            PoolManager.Instance.Release(prefabReference, gameObject);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    private void OnValidate()
    {
        circleCollider = GetComponent<CircleCollider2D>();
        RefreshRangeVisual();
    }

    private void RefreshRangeVisual()
    {
        if (rangeVisualRenderer == null) return;

        Color color = rangeVisualRenderer.color;
        color.a = visualAlpha;
        rangeVisualRenderer.color = color;

        if (!autoSyncVisualSize || circleCollider == null) return;

        // 1) 对齐到碰撞器偏移，避免“中心点看着对不上”的错位。
        Vector3 visualLocalPos = rangeVisualRenderer.transform.localPosition;
        visualLocalPos.x = circleCollider.offset.x;
        visualLocalPos.y = circleCollider.offset.y;
        rangeVisualRenderer.transform.localPosition = visualLocalPos;

        // 2) 使用 Collider 的世界尺寸做目标，保证和“实际触发范围”一致。
        //    bounds.size 已包含了对象缩放影响，比仅用 radius 更可靠。
        float targetWorldWidth = circleCollider.bounds.size.x;
        float targetWorldHeight = circleCollider.bounds.size.y;

        Sprite sprite = rangeVisualRenderer.sprite;
        if (sprite == null) return;

        // sprite.bounds.size 是贴图在 scale=1 时的本地单位尺寸
        Vector2 spriteLocalSize = sprite.bounds.size;
        if (spriteLocalSize.x <= 0f || spriteLocalSize.y <= 0f) return;

        // 3) 从目标“世界尺寸”反推可视化节点需要的 localScale（扣除父级缩放影响）
        Transform visualTransform = rangeVisualRenderer.transform;
        Vector3 parentLossyScale = visualTransform.parent != null ? visualTransform.parent.lossyScale : Vector3.one;

        float parentScaleX = Mathf.Max(Mathf.Abs(parentLossyScale.x), 0.0001f);
        float parentScaleY = Mathf.Max(Mathf.Abs(parentLossyScale.y), 0.0001f);

        float localScaleX = targetWorldWidth / (spriteLocalSize.x * parentScaleX);
        float localScaleY = targetWorldHeight / (spriteLocalSize.y * parentScaleY);

        visualTransform.localScale = new Vector3(localScaleX, localScaleY, 1f);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebugGizmo) return;

        CircleCollider2D debugCollider = circleCollider != null ? circleCollider : GetComponent<CircleCollider2D>();
        if (debugCollider == null) return;

        Gizmos.color = new Color(0f, 1f, 1f, 0.9f);
        Vector3 center = transform.TransformPoint(debugCollider.offset);

        // 使用 lossyScale 的最大轴近似半径，适合当前常见的等比缩放场景。
        // 如果未来出现非等比缩放，建议改用 DrawWireMesh 画椭圆更精确。
        float scale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));
        float worldRadius = debugCollider.radius * scale;
        Gizmos.DrawWireSphere(center, worldRadius);
    }
}

