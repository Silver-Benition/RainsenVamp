using UnityEngine;

/// <summary>磁吸拾取物的运行时运动阶段。</summary>
public enum MagneticPickupMotionState
{
    Idle,
    Scatter,
    Homing
}

/// <summary>
/// 可复用的磁吸拾取表现组件。
/// 首次进入玩家磁吸范围时先向外短暂飘散，再以递增加速度追向玩家；逻辑拾取仍由同对象的拾取组件负责。
/// </summary>
[DisallowMultipleComponent]
public sealed class MagneticPickupMotion : MonoBehaviour, IMagneticPickup
{
    [Header("外飘阶段")]
    [SerializeField, Min(0f)] private float scatterDuration = 0.18f;
    [SerializeField, Min(0f)] private float scatterDistance = 0.45f;
    [SerializeField, Range(0f, 0.75f)] private float lateralDriftRatio = 0.3f;

    [Header("追踪阶段")]
    [SerializeField, Min(0f)] private float baseFlySpeed = 5f;
    [SerializeField, Min(0f)] private float acceleration = 15f;

    private Transform _targetPlayer;
    private MagneticPickupMotionState _state;
    private Vector3 _scatterStart;
    private Vector3 _scatterEnd;
    private float _scatterElapsed;
    private float _currentSpeed;

    /// <summary>当前运动阶段，供诊断与自动化测试只读观察。</summary>
    public MagneticPickupMotionState State => _state;

    /// <summary>当前磁吸目标；空值表示本生命周期尚未被捕获。</summary>
    public Transform TargetPlayer => _targetPlayer;

    /// <summary>当前追踪速度，外飘阶段保持为基础速度。</summary>
    public float CurrentSpeed => _currentSpeed;

    /// <summary>池对象启用时清除上一生命周期的目标、路径和速度。</summary>
    private void OnEnable()
    {
        ResetMotion();
    }

    /// <summary>按当前阶段推进外飘或加速追踪；静止拾取物不会产生位移计算。</summary>
    private void Update()
    {
        if (_state == MagneticPickupMotionState.Idle)
        {
            return;
        }

        if (_targetPlayer == null)
        {
            ResetMotion();
            return;
        }

        float deltaTime = Mathf.Max(0f, Time.deltaTime);
        if (_state == MagneticPickupMotionState.Scatter)
        {
            AdvanceScatter(deltaTime);
            return;
        }

        AdvanceHoming(deltaTime);
    }

    /// <summary>锁定玩家并开始一次外飘动画；重复磁吸调用保持幂等，防止多个触发器重置路径。</summary>
    public void StartFlyingTowards(Transform player)
    {
        if (_state != MagneticPickupMotionState.Idle || player == null)
        {
            return;
        }

        _targetPlayer = player;
        _currentSpeed = Mathf.Max(0f, baseFlySpeed);
        _scatterElapsed = 0f;
        _scatterStart = transform.position;

        Vector2 offsetFromPlayer = transform.position - player.position;
        Vector2 outward = offsetFromPlayer.sqrMagnitude > 0.0001f
            ? offsetFromPlayer.normalized
            : CreateFallbackDirection(GetInstanceID());
        Vector2 tangent = new Vector2(-outward.y, outward.x);
        float signedDrift = CreateSignedUnitValue(GetInstanceID() ^ 0x4f1bbcdc);
        Vector2 scatterDirection =
            (outward + tangent * signedDrift * Mathf.Clamp(lateralDriftRatio, 0f, 0.75f)).normalized;

        _scatterEnd = _scatterStart + (Vector3)(scatterDirection * Mathf.Max(0f, scatterDistance));
        if (scatterDuration <= 0f || scatterDistance <= 0f)
        {
            BeginHoming();
            return;
        }

        _state = MagneticPickupMotionState.Scatter;
    }

    /// <summary>推进外飘缓出插值，并在规定时间结束后切换到追踪阶段。</summary>
    private void AdvanceScatter(float deltaTime)
    {
        _scatterElapsed += deltaTime;
        float duration = Mathf.Max(0.0001f, scatterDuration);
        float normalizedTime = Mathf.Clamp01(_scatterElapsed / duration);

        // 三次缓出让拾取物先明显离开原位再减速，表达“已被磁吸锁定”，且无需协程或曲线采样。
        float remaining = 1f - normalizedTime;
        float easedTime = 1f - remaining * remaining * remaining;
        transform.position = Vector3.LerpUnclamped(_scatterStart, _scatterEnd, easedTime);

        if (normalizedTime >= 1f)
        {
            transform.position = _scatterEnd;
            BeginHoming();
        }
    }

    /// <summary>按递增加速度追向玩家；暂停时 deltaTime 为零，因此自然停止。</summary>
    private void AdvanceHoming(float deltaTime)
    {
        _currentSpeed += Mathf.Max(0f, acceleration) * deltaTime;
        transform.position = Vector3.MoveTowards(
            transform.position,
            _targetPlayer.position,
            _currentSpeed * deltaTime);
    }

    /// <summary>进入追踪阶段，并保留基础速度作为初始飞行速度。</summary>
    private void BeginHoming()
    {
        _state = MagneticPickupMotionState.Homing;
        _currentSpeed = Mathf.Max(0f, baseFlySpeed);
    }

    /// <summary>清除所有运行时运动状态，供对象池启用、回收和目标丢失时复用。</summary>
    private void ResetMotion()
    {
        _targetPlayer = null;
        _state = MagneticPickupMotionState.Idle;
        _scatterStart = Vector3.zero;
        _scatterEnd = Vector3.zero;
        _scatterElapsed = 0f;
        _currentSpeed = Mathf.Max(0f, baseFlySpeed);
    }

    /// <summary>根据实例标识生成确定性的单位方向，处理拾取物与玩家完全重叠的边界。</summary>
    private static Vector2 CreateFallbackDirection(int seed)
    {
        float radians = (CreateSignedUnitValue(seed) + 1f) * Mathf.PI;
        return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
    }

    /// <summary>将整数种子混合为 -1 到 1 的稳定值，不污染 Unity 全局随机序列。</summary>
    private static float CreateSignedUnitValue(int seed)
    {
        unchecked
        {
            // 分阶段混合相邻实例 ID，避免大量同帧拾取物得到几乎相同的飘散方向。
            uint hash = (uint)seed;
            hash ^= hash >> 16;
            hash *= 0x7feb352dU;
            hash ^= hash >> 15;
            hash *= 0x846ca68bU;
            hash ^= hash >> 16;
            return (hash & 0x00ffffffU) / 8388607.5f - 1f;
        }
    }

    /// <summary>回池时立即释放目标引用，防止旧玩家对象跨生命周期残留。</summary>
    private void OnDisable()
    {
        ResetMotion();
    }

    /// <summary>在 Inspector 修改时钳制动画参数，防止负值破坏状态切换。</summary>
    private void OnValidate()
    {
        scatterDuration = Mathf.Max(0f, scatterDuration);
        scatterDistance = Mathf.Max(0f, scatterDistance);
        lateralDriftRatio = Mathf.Clamp(lateralDriftRatio, 0f, 0.75f);
        baseFlySpeed = Mathf.Max(0f, baseFlySpeed);
        acceleration = Mathf.Max(0f, acceleration);
    }
}
