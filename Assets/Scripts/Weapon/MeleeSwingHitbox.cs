using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 池化近战挥击实体。根节点固定在玩家中心充当旋转枢轴，视觉与碰撞体沿攻击方向向外延伸。
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(CapsuleCollider2D))]
public sealed class MeleeSwingHitbox : MonoBehaviour, IPoolable
{
    [Header("挥击几何")]
    [Tooltip("武器判定距离玩家中心的最近距离，用于避免贴图和角色身体重叠。")]
    [SerializeField, Min(0f)] private float minimumInnerRadius = 0.35f;
    [Tooltip("只影响武器贴图，不改变外圈攻击范围。")]
    [SerializeField, Min(0.1f)] private float visualScaleMultiplier = 1.25f;
    [Tooltip("素材长轴相对局部 +X 径向线的校正角度；水平向右的正式雨伞素材应设为 0。")]
    [SerializeField] private float visualAngleOffset = -45f;
    [SerializeField] private Transform visualRoot;
    [SerializeField] private SpriteRenderer spriteRenderer;

    private readonly HashSet<Collider2D> _hitColliders = new HashSet<Collider2D>();

    private GameObject _prefabReference;
    private Transform _owner;
    private CapsuleCollider2D _hitCollider;
    private Vector3 _baseRootScale;
    private Vector3 _baseVisualScale;
    private Vector2 _baseColliderSize;
    private WeaponDataSO _weaponData;
    private float _damage;
    private float _duration;
    private float _elapsedTime;
    private float _startAngle;
    private float _endAngle;

    /// <summary>
    /// 缓存枢轴、表现与判定组件的 Prefab 初始几何信息。
    /// </summary>
    private void Awake()
    {
        _hitCollider = GetComponent<CapsuleCollider2D>();
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
        }
        if (visualRoot == null && spriteRenderer != null)
        {
            visualRoot = spriteRenderer.transform;
        }

        _baseRootScale = transform.localScale;
        _baseVisualScale = visualRoot != null ? visualRoot.localScale : Vector3.one;
        _baseColliderSize = _hitCollider != null
            ? _hitCollider.size
            : new Vector2(1f, 0.25f);
    }

    /// <summary>
    /// 对象池取出时清空上一次挥击命中过的碰撞体。
    /// </summary>
    private void OnEnable()
    {
        _hitColliders.Clear();
    }

    /// <summary>对象池回收时清除旧武器来源、玩家跟随和本次命中集合。</summary>
    private void OnDisable()
    {
        _owner = null;
        _weaponData = null;
        _damage = 0f;
        _hitColliders.Clear();
    }

    /// <summary>
    /// 保存原始 Prefab 引用，供挥击结束时归还正确的池。
    /// </summary>
    public void SetPrefabReference(GameObject prefab)
    {
        _prefabReference = prefab;
    }

    /// <summary>
    /// 初始化一次近战挥击；同一目标在该生命周期内最多受击一次。
    /// </summary>
    public void Initialize(
        Transform owner,
        bool facesRight,
        float damage,
        float range,
        float arc,
        float duration,
        float startAngleOffset = 0f)
    {
        Initialize(
            null,
            owner,
            facesRight,
            damage,
            range,
            arc,
            duration,
            startAngleOffset);
    }

    /// <summary>注入带稳定武器来源的近战挥击生命周期。</summary>
    public void Initialize(
        WeaponDataSO weaponData,
        Transform owner,
        bool facesRight,
        float damage,
        float range,
        float arc,
        float duration,
        float startAngleOffset = 0f)
    {
        _weaponData = weaponData;
        _owner = owner;
        _damage = Mathf.Max(0f, damage);
        _duration = Mathf.Max(0.02f, duration);
        _elapsedTime = 0f;
        _hitColliders.Clear();

        float swingArc = Mathf.Clamp(arc, 1f, 360f);
        _startAngle = 90f + startAngleOffset;
        _endAngle = facesRight
            ? _startAngle - swingArc
            : _startAngle + swingArc;

        RefreshGeometry(range);
        transform.position = owner != null ? owner.position : transform.position;
        transform.rotation = Quaternion.Euler(0f, 0f, _startAngle);
    }

    /// <summary>
    /// 让根节点保持在玩家中心，并把视觉和胶囊判定放到内圈之外、攻击范围之内。
    /// </summary>
    private void RefreshGeometry(float range)
    {
        float safeRange = Mathf.Max(0.05f, range);
        float innerRadius = Mathf.Clamp(
            minimumInnerRadius,
            0f,
            Mathf.Max(0f, safeRange - 0.05f));
        float hitboxLength = Mathf.Max(0.05f, safeRange - innerRadius);
        float centerDistance = innerRadius + hitboxLength * 0.5f;

        transform.localScale = _baseRootScale;

        if (visualRoot != null)
        {
            float spriteLength = spriteRenderer != null && spriteRenderer.sprite != null
                ? Mathf.Max(0.01f, spriteRenderer.sprite.bounds.size.x)
                : 1f;
            float visualScale = hitboxLength / spriteLength * visualScaleMultiplier;
            visualRoot.localPosition = Vector3.right * centerDistance;
            visualRoot.localRotation = Quaternion.Euler(0f, 0f, visualAngleOffset);
            visualRoot.localScale = new Vector3(
                _baseVisualScale.x * visualScale,
                _baseVisualScale.y * visualScale,
                _baseVisualScale.z);
        }

        if (_hitCollider != null)
        {
            float colliderAspect = _baseColliderSize.x > 0.001f
                ? _baseColliderSize.y / _baseColliderSize.x
                : 0.25f;
            _hitCollider.offset = new Vector2(centerDistance, 0f);
            _hitCollider.size = new Vector2(
                hitboxLength,
                Mathf.Max(0.05f, hitboxLength * colliderAspect));
        }
    }

    /// <summary>
    /// 跟随玩家位置并在配置的扇形角度内完成一次平滑挥击。
    /// </summary>
    private void Update()
    {
        if (_owner == null)
        {
            ReleaseToPool();
            return;
        }

        _elapsedTime += Time.deltaTime;
        float normalizedTime = Mathf.Clamp01(_elapsedTime / _duration);
        float easedTime = Mathf.SmoothStep(0f, 1f, normalizedTime);
        float angle = Mathf.Lerp(_startAngle, _endAngle, easedTime);

        transform.position = _owner.position;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);

        if (_elapsedTime >= _duration)
        {
            ReleaseToPool();
        }
    }

    /// <summary>
    /// 对首次进入本次挥击的可受击实体结算伤害。
    /// </summary>
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_hitColliders.Contains(other)
            || !DamageTargetFilter.TryGetEnemyDamageable(other, out IDamageable damageable))
        {
            return;
        }

        _hitColliders.Add(other);
        CombatDamageResolver.Apply(damageable, _damage, _weaponData);
    }

    /// <summary>
    /// 清理持有引用并归还对象池，防止池化实例继续跟随旧玩家。
    /// </summary>
    private void ReleaseToPool()
    {
        _owner = null;
        _weaponData = null;
        _damage = 0f;
        _hitColliders.Clear();

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
