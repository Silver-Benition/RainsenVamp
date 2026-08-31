using UnityEngine;

/// <summary>
/// 武装巨像运行时控制器。
/// 继承 EnemyBase 的 Rigidbody2D 追踪与玩家接触规则，独立维护阶段、弹幕冷却和无掉落死亡出口。
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public sealed class BossEnemyController : EnemyBase
{
    [Header("首领数据")]
    [SerializeField] private BossDataSO bossData;
    [SerializeField] private Sprite[] animationFrames;
    [SerializeField, Min(0.05f)] private float animationInterval = 0.2f;

    private BossDataSO _runtimeBossData;
    private RunDirector _runDirector;
    private SpriteRenderer _spriteRenderer;
    private float _barrageTimer;
    private float _animationTimer;
    private int _animationFrame;
    private bool _phaseTwoActive;
    private bool _defeated;

    /// <summary>当前首领是否已经进入第二阶段。</summary>
    public bool IsPhaseTwoActive => _phaseTwoActive;

    /// <summary>当前控制器使用的首领数据。</summary>
    public BossDataSO BossData => _runtimeBossData != null ? _runtimeBossData : bossData;

    /// <summary>缓存首领动画 SpriteRenderer；基础敌人缓存仍由 EnemyBase 负责。</summary>
    protected override void Awake()
    {
        base.Awake();
        _spriteRenderer = GetComponent<SpriteRenderer>();
        if (_spriteRenderer == null)
        {
            _spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
        }
    }

    /// <summary>回池时清除首领控制器引用、阶段和弹幕计时。</summary>
    protected override void OnDisable()
    {
        base.OnDisable();
        _runtimeBossData = null;
        _runDirector = null;
        _barrageTimer = 0f;
        _animationTimer = 0f;
        _animationFrame = 0;
        _phaseTwoActive = false;
        _defeated = false;
    }

    /// <summary>
    /// 由当前世界模拟器在生成后注入首领数据与 RunDirector。
    /// 该调用发生在同一生成栈内，保证首领不会以旧生命周期参数运行一帧。
    /// </summary>
    public void InitializeBoss(BossDataSO data, RunDirector director)
    {
        _runtimeBossData = data != null ? data : bossData;
        _runDirector = director;
        _barrageTimer = GetCurrentPhase().GetSafeInterval();
        _animationTimer = 0f;
        _animationFrame = 0;
        _phaseTwoActive = false;
        _defeated = false;

        if (_runtimeBossData != null)
        {
            ApplySpawnSnapshot(_runtimeBossData.CreateSpawnSnapshot());
        }

        ApplyAnimationFrame();
    }

    /// <summary>首领弹体不受普通敌人的 Curse/Defang 输出倍率影响，直接使用数据中的基础伤害。</summary>
    public override float ResolveOutgoingDamage(float baseDamage)
    {
        return Mathf.Max(0f, baseDamage);
    }

    /// <summary>推进两帧循环、检测阶段阈值并在当前世界发射池化径向弹幕。</summary>
    private void Update()
    {
        if (!isActiveAndEnabled || _runtimeBossData == null ||
            WorldSimulation == null || !WorldSimulation.IsWorldActive ||
            (_runDirector != null && _runDirector.IsResultFrozen))
        {
            return;
        }

        UpdateAnimation();
        UpdatePhase();

        _barrageTimer -= Time.deltaTime;
        if (_barrageTimer > 0f)
        {
            return;
        }

        FireRadialBarrage();
        _barrageTimer = GetCurrentPhase().GetSafeInterval();
    }

    /// <summary>按固定帧间隔切换两张首领 Sprite，不创建动画状态机运行时对象。</summary>
    private void UpdateAnimation()
    {
        if (animationFrames == null || animationFrames.Length == 0 || _spriteRenderer == null)
        {
            return;
        }

        _animationTimer -= Time.deltaTime;
        if (_animationTimer > 0f)
        {
            return;
        }

        _animationTimer = Mathf.Max(0.05f, animationInterval);
        _animationFrame = (_animationFrame + 1) % animationFrames.Length;
        ApplyAnimationFrame();
    }

    /// <summary>将当前动画帧绑定到首领 SpriteRenderer。</summary>
    private void ApplyAnimationFrame()
    {
        if (_spriteRenderer != null && animationFrames != null && animationFrames.Length > 0)
        {
            int safeIndex = Mathf.Clamp(_animationFrame, 0, animationFrames.Length - 1);
            _spriteRenderer.sprite = animationFrames[safeIndex];
        }
    }

    /// <summary>生命值小于等于 50% 时只切换一次第二阶段并重置弹幕间隔。</summary>
    private void UpdatePhase()
    {
        if (_phaseTwoActive || _runtimeBossData == null)
        {
            return;
        }

        float threshold = _runtimeBossData.maxHealth * Mathf.Clamp01(_runtimeBossData.phaseTwoHealthRatio);
        if (CurrentHealth <= threshold)
        {
            _phaseTwoActive = true;
            _barrageTimer = GetCurrentPhase().GetSafeInterval();
        }
    }

    /// <summary>从当前世界生成预警 VFX 和均匀分布的 8/12 向首领弹体。</summary>
    private void FireRadialBarrage()
    {
        BossBarragePhaseData phase = GetCurrentPhase();
        if (phase == null)
        {
            return;
        }

        if (BossData.warningVfxPrefab != null)
        {
            WorldSimulation.SpawnVfx(BossData.warningVfxPrefab, transform.position, Quaternion.identity);
        }

        int count = phase.GetSafeProjectileCount();
        float angleStep = 360f / count;
        for (int index = 0; index < count; index++)
        {
            float angle = angleStep * index;
            Vector2 direction = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            WorldSimulation.SpawnProjectile(
                BossData.projectilePrefab,
                transform.position,
                Quaternion.identity,
                direction * Mathf.Max(0f, phase.projectileSpeed),
                Mathf.Max(0f, phase.projectileDamage),
                this,
                Mathf.Max(0.1f, phase.projectileLifetime));
        }
    }

    /// <summary>读取当前阶段数据，避免配置资产被运行时状态写回。</summary>
    private BossBarragePhaseData GetCurrentPhase()
    {
        BossDataSO data = BossData;
        return data != null ? data.GetPhase(_phaseTwoActive) : new BossBarragePhaseData();
    }

    /// <summary>
    /// 首领死亡只登记一次击杀并通知 RunDirector 胜利，不生成任何普通敌人掉落。
    /// </summary>
    protected override void Die()
    {
        if (_defeated)
        {
            return;
        }

        _defeated = true;
        RegisterKillForDefeat();
        if (_runDirector != null)
        {
            _runDirector.NotifyBossDefeated(this);
        }

        ReleaseToPool();
    }
}
