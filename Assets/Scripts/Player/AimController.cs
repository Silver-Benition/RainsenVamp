using UnityEngine;

/// <summary>统一维护自动目标和手动方向；武器从各自挂点计算射向目标的方向。</summary>
public class AimController : MonoBehaviour
{
    public enum AimMode { FollowMovement = 0, NearestEnemy = 1, Manual = 2 }
    public AimMode aimMode = AimMode.NearestEnemy;
    [SerializeField] private Vector2 defaultDirection = Vector2.right;
    [SerializeField, Min(.02f)] private float retargetInterval = .08f;
    [SerializeField, Min(1f)] private float searchRadius = 1000f;
    private Collider2D _target;
    private uint _targetGeneration;
    private Component _targetIdentity;
    private float _nextSearch;
    private Vector2 _manualDirection = Vector2.right;
    public Vector2 AimDirection { get; private set; } = Vector2.right;
    public float HorizontalFacingSign { get; private set; } = 1f;

    /// <summary>准备默认方向，零向量回退向右；不在初始化阶段枚举场景。</summary>
    private void Awake()
    {
        AimDirection = defaultDirection.sqrMagnitude > .0001f ? defaultDirection.normalized : Vector2.right;
        _manualDirection = AimDirection;
        HorizontalFacingSign = AimDirection.x < 0 ? -1f : 1f;
    }

    /// <summary>以低频共享查询更新最近目标，保留旧移动模式供历史夹具使用。</summary>
    private void Update()
    {
        Vector2 input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        if (Mathf.Abs(input.x) > .01f) HorizontalFacingSign = Mathf.Sign(input.x);
        if (!RoundController.AllowsCombat) return;
        if (aimMode == AimMode.FollowMovement && input.sqrMagnitude > .0001f) AimDirection = input.normalized;
        else if (aimMode == AimMode.Manual) AimDirection = _manualDirection;
        else if (aimMode == AimMode.NearestEnemy) RefreshTarget();
    }

    /// <summary>切换模式并使目标缓存过期，不修改任何武器的独立冷却。</summary>
    public void SetMode(AimMode mode)
    { aimMode = mode; _target = null; _targetGeneration = 0; _nextSearch = 0f; }

    /// <summary>鼠标或摇杆适配层注入世界方向；零输入保留上次方向，是否切换模式由调用方控制。</summary>
    public void SetManualDirection(Vector2 direction)
    {
        if (!float.IsNaN(direction.x) && !float.IsNaN(direction.y)
            && !float.IsInfinity(direction.x) && !float.IsInfinity(direction.y) && direction.sqrMagnitude > .0001f)
            _manualDirection = direction.normalized;
        if (aimMode == AimMode.Manual) AimDirection = _manualDirection;
    }

    /// <summary>返回指定挂点指向目标的方向；无敌人时保持上次有效朝向。</summary>
    public Vector2 DirectionFrom(Vector2 origin)
    {
        if (aimMode == AimMode.Manual) return _manualDirection;
        if (aimMode == AimMode.NearestEnemy)
        {
            RefreshTarget();
            if (WeaponTargeting.IsValidCached(_target, _targetIdentity, _targetGeneration))
            {
                Vector2 delta = (Vector2)_targetIdentity.transform.position - origin;
                if (delta.sqrMagnitude > .0001f) return delta.normalized;
            }
        }
        return AimDirection;
    }

    /// <summary>每个角色至多按固定间隔扫描一次；目标失效时本次发射立即补查。</summary>
    private void RefreshTarget()
    {
        bool valid = WeaponTargeting.IsValidCached(_target, _targetIdentity, _targetGeneration);
        if (Time.time >= _nextSearch || ((_target != null || _targetGeneration != 0) && !valid))
        {
            _target = WeaponTargeting.FindNearest(transform.position, searchRadius);
            _targetIdentity = WeaponTargeting.Identity(_target);
            _targetGeneration = _targetIdentity is EnemyBase enemy ? enemy.LifeGeneration : 0;
            _nextSearch = Time.time + retargetInterval;
        }
        if (WeaponTargeting.IsValidCached(_target, _targetIdentity, _targetGeneration))
        {
            Vector2 delta = _targetIdentity.transform.position - transform.position;
            if (delta.sqrMagnitude > .0001f) AimDirection = delta.normalized;
        }
    }
}
