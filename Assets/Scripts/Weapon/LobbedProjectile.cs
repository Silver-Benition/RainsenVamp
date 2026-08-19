using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 池化抛物线投射物。使用确定性参数方程移动，不受 Top-Down 物理重力方向影响。
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D), typeof(SpriteRenderer))]
public sealed class LobbedProjectile : MonoBehaviour, IPoolable
{
    private readonly HashSet<Collider2D> _hitColliders = new HashSet<Collider2D>();

    private GameObject _prefabReference;
    private SpriteRenderer _spriteRenderer;
    private Camera _mainCamera;
    private Vector3 _startPosition;
    private Vector3 _initialVelocity;
    private float _damage;
    private float _gravity;
    private float _releaseDeadline;
    private float _elapsedTime;
    private float _spinSpeed;
    private int _remainingPierce;

    /// <summary>
    /// 缓存飞斧表现组件和游戏相机，避免在每帧视口检测时重复查找。
    /// </summary>
    private void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _mainCamera = Camera.main;
    }

    /// <summary>
    /// 保存原始 Prefab 引用，供生命周期结束时归还正确的池。
    /// </summary>
    public void SetPrefabReference(GameObject prefab)
    {
        _prefabReference = prefab;
    }

    /// <summary>
    /// 对象池取出时清理上一把飞斧留下的命中集合。
    /// </summary>
    private void OnEnable()
    {
        _hitColliders.Clear();
    }

    /// <summary>
    /// 注入一次投掷的完整数值快照并重置弹道计时器。
    /// </summary>
    /// <param name="startPosition">发射瞬间的世界坐标。</param>
    /// <param name="inheritedVelocity">发射瞬间继承的玩家世界速度。</param>
    /// <param name="direction">限制在上半平面的投掷方向。</param>
    /// <param name="damage">命中造成的伤害。</param>
    /// <param name="launchSpeed">沿投掷方向施加的恒定初速度大小。</param>
    /// <param name="maxLifetime">未从侧边或下边离开视口时的强制回收上限。</param>
    /// <param name="pierceCount">允许额外穿透的目标数量。</param>
    /// <param name="gravity">持续向下的加速度大小。</param>
    /// <param name="spinSpeed">贴图自转速度，单位为度/秒。</param>
    public void Initialize(
        Vector3 startPosition,
        Vector3 inheritedVelocity,
        Vector3 direction,
        float damage,
        float launchSpeed,
        float maxLifetime,
        int pierceCount,
        float gravity,
        float spinSpeed)
    {
        _startPosition = startPosition;
        Vector3 safeDirection = direction.sqrMagnitude > 0.0001f
            ? direction.normalized
            : Vector3.right;
        _initialVelocity = inheritedVelocity + safeDirection * Mathf.Max(0f, launchSpeed);
        _damage = Mathf.Max(0f, damage);
        _gravity = Mathf.Max(0.01f, gravity);
        _releaseDeadline = Mathf.Max(0.1f, maxLifetime);
        _remainingPierce = Mathf.Max(0, pierceCount);
        _spinSpeed = spinSpeed;
        _elapsedTime = 0f;
        _hitColliders.Clear();
        transform.position = _startPosition;

        if (_mainCamera == null)
        {
            _mainCamera = Camera.main;
        }
    }

    /// <summary>
    /// 按恒定初速度和向下重力计算确定性弹道，同步飞斧自转，并在满足安全条件时回收。
    /// </summary>
    private void Update()
    {
        _elapsedTime += Time.deltaTime;

        // 直接由发射快照和累计时间求位置，避免逐帧积分误差随帧率变化。
        // 高角度飞斧的竖直速度会在重力作用下自然归零并反向，低角度则更早飞出左右边界。
        Vector3 ballisticOffset = _initialVelocity * _elapsedTime
            + Vector3.down * (0.5f * _gravity * _elapsedTime * _elapsedTime);
        transform.position = _startPosition + ballisticOffset;
        transform.Rotate(0f, 0f, _spinSpeed * Time.deltaTime, Space.Self);

        if (IsCompletelyOutsideGameView() || _elapsedTime >= _releaseDeadline)
        {
            ReleaseToPool();
        }
    }

    /// <summary>
    /// 判断旋转后的完整贴图是否已从左右或下方离开视口；上方离屏的飞斧仍保留以等待回落。
    /// </summary>
    private bool IsCompletelyOutsideGameView()
    {
        if (_mainCamera == null || _spriteRenderer == null)
        {
            return false;
        }

        Bounds bounds = _spriteRenderer.bounds;
        Vector3 viewportMin = _mainCamera.WorldToViewportPoint(bounds.min);
        Vector3 viewportMax = _mainCamera.WorldToViewportPoint(bounds.max);

        float minX = Mathf.Min(viewportMin.x, viewportMax.x);
        float maxX = Mathf.Max(viewportMin.x, viewportMax.x);
        float maxY = Mathf.Max(viewportMin.y, viewportMax.y);

        // 故意不检查视口上边界：陡角投掷必须能飞出顶部，越过最高点后再从下方回收。
        return maxX < 0f || minX > 1f || maxY < 0f;
    }

    /// <summary>
    /// 首次命中可受击实体时结算伤害，并按额外穿透层数决定是否回收。
    /// </summary>
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_hitColliders.Contains(other)
            || !DamageTargetFilter.TryGetEnemyDamageable(other, out IDamageable damageable))
        {
            return;
        }

        _hitColliders.Add(other);
        damageable.TakeDamage(_damage);
        _remainingPierce--;
        if (_remainingPierce < 0)
        {
            ReleaseToPool();
        }
    }

    /// <summary>
    /// 清理运行时状态并把飞斧归还对象池。
    /// </summary>
    private void ReleaseToPool()
    {
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
