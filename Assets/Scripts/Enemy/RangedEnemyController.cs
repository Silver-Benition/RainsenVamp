using UnityEngine;

/// <summary>
/// Grass 主世界远程敌人的移动与定点发射控制器。
/// 移动、生命和接触伤害复用 EnemyBase；发射通过所属 WorldEnemySimulation 进入共享对象池。
/// </summary>
public sealed class RangedEnemyController : EnemyBase
{
    private const float RetreatDistance = 4f;
    private const float HoldDistance = 6f;

    [SerializeField] private RangedEnemyAttackDataSO attackData;

    private WorldEnemySimulation _worldSimulation;
    private float _attackTimer;

    /// <summary>当前远程攻击配置。</summary>
    public RangedEnemyAttackDataSO AttackData => attackData;

    /// <summary>当前攻击周期剩余时间。</summary>
    public float AttackTimer => _attackTimer;

    /// <summary>当前攻击是否已经进入可发射状态。</summary>
    public bool IsAttackReady => _attackTimer <= 0f;

    /// <summary>
    /// 缓存父级世界模拟器，并执行 EnemyBase 的组件缓存。
    /// </summary>
    protected override void Awake()
    {
        base.Awake();
        _worldSimulation = GetComponentInParent<WorldEnemySimulation>();
    }

    /// <summary>
    /// 在对象池取出时重置远程攻击的首发延迟。
    /// </summary>
    protected override void OnEnable()
    {
        base.OnEnable();
        ResetAttackCycle();
    }

    /// <summary>
    /// 在对象池回收时清理远程攻击计时和世界绑定。
    /// </summary>
    protected override void OnDisable()
    {
        _attackTimer = 0f;
        _worldSimulation = null;
        base.OnDisable();
    }

    /// <summary>
    /// 应用敌人生成快照，并重新开始本次池化生命周期的首发延迟。
    /// </summary>
    public override void ApplySpawnSnapshot(EnemySpawnSnapshot snapshot)
    {
        base.ApplySpawnSnapshot(snapshot);
        ResetAttackCycle();
    }

    /// <summary>
    /// 绑定远程敌人的世界模拟器和交互所有权。
    /// </summary>
    public void BindWorldSimulation(WorldEnemySimulation simulation)
    {
        _worldSimulation = simulation;
        SetWorldSimulation(simulation);
    }

    /// <summary>
    /// 按玩家距离决定接近、保持或后退的移动方向。
    /// </summary>
    protected override Vector2 GetMovementDirection()
    {
        ResolvePlayerTarget();
        if (PlayerTransform == null)
        {
            return Vector2.zero;
        }

        Vector2 offset = PlayerTransform.position - transform.position;
        float distance = offset.magnitude;
        if (distance < RetreatDistance)
        {
            return offset.sqrMagnitude > Mathf.Epsilon ? -offset.normalized : Vector2.zero;
        }

        if (distance > HoldDistance)
        {
            return offset.normalized;
        }

        return Vector2.zero;
    }

    /// <summary>
    /// 推进首发或成功发射后的攻击计时，并在最大射程内发射一次快照瞄准的弹体。
    /// </summary>
    private void Update()
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        ResolvePlayerTarget();
        if (attackData == null || attackData.ProjectilePrefab == null || PlayerTransform == null)
        {
            return;
        }

        if (_attackTimer > 0f)
        {
            _attackTimer = Mathf.Max(0f, _attackTimer - Time.deltaTime);
            return;
        }

        Vector2 aimOffset = PlayerTransform.position - transform.position;
        if (aimOffset.sqrMagnitude > attackData.MaxRange * attackData.MaxRange ||
            aimOffset.sqrMagnitude <= Mathf.Epsilon)
        {
            return;
        }

        if (TryFireAt(aimOffset.normalized))
        {
            _attackTimer = attackData.Cooldown;
        }
    }

    /// <summary>
    /// 将一次发射委托给世界模拟器，并使用发射瞬间的玩家位置作为固定瞄准方向。
    /// </summary>
    private bool TryFireAt(Vector2 direction)
    {
        if (_worldSimulation == null)
        {
            return false;
        }

        GameObject projectile = _worldSimulation.SpawnProjectile(
            attackData.ProjectilePrefab,
            transform.position,
            Quaternion.identity,
            direction * attackData.ProjectileSpeed,
            attackData.BaseDamage,
            this,
            attackData.ProjectileLifetime);
        return projectile != null;
    }

    /// <summary>
    /// 重置本次池化生命周期的攻击计时。
    /// </summary>
    private void ResetAttackCycle()
    {
        _attackTimer = attackData != null ? attackData.FirstShotDelay : 0f;
    }
}
