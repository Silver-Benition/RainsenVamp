using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 池化近战挥击实体。正式动作先移至锁定目标旁，沿固定枢轴横挥，然后回到持武挂点。
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(CapsuleCollider2D))]
public sealed class MeleeSwingHitbox : MonoBehaviour, IPoolable
{
    [Header("挥击几何")]
    [Tooltip("武器判定距离武器挂点的最近距离，用于避免贴图和角色身体重叠。")]
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
    private bool _thrust;
    private bool _directed;
    private float _centerAngle;
    private WeaponHeldView _heldView;
    private Vector3 _initialVisualScale, _targetVisualScale, _initialVisualPosition;
    private float _initialAngle;
    private bool _blendFromHeld;
    private Vector2 _initialColliderSize, _targetColliderSize;
    public SpriteRenderer VisualRenderer => spriteRenderer;
    private bool _targeted;
    private WeaponBase _sourceWeapon;
    private MeleeAttackTiming _timing;
    private Vector3 _attackPivot, _launchPosition;
    private readonly List<Collider2D> _overlaps = new List<Collider2D>(64);
    public bool IsStriking => _targeted && _elapsedTime >= _timing.Windup && _elapsedTime <= _timing.Windup + _timing.Swing;
    public Vector3 AttackPivot => _attackPivot;
    private float _thrustTravel;
    private Vector2 _restColliderOffset;
    private Vector3 _restVisualPosition;
    private readonly Dictionary<Component, uint> _hitTargets = new Dictionary<Component, uint>();

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
        _hitTargets.Clear();
    }

    private WeaponHitSnapshot _hitSnapshot;
    public ExpansionWeapon ExpansionOwner { get; private set; }
    private bool _empowered;
    private float _burnDamage;
    private Vector2 _expansionOrigin;

    /// <summary>绑定本次扩展近战来源；保存原始位置供残影使用，不读取后续攻击的可变序号。</summary>
    public void ConfigureExpansion(ExpansionWeapon source, bool empowered, float burnDamage)
    { ExpansionOwner = source; _empowered = empowered; _burnDamage = burnDamage; _expansionOrigin = _targeted ? (Vector2)_attackPivot : (Vector2)transform.position; }


    /// <summary>对象池回收时清除旧武器来源、玩家跟随和本次命中集合。</summary>
    private void OnDisable()
    {
        if (_heldView != null) _heldView.CompleteAttack(spriteRenderer);
        _heldView = null;
        _blendFromHeld = false;
        _targeted = false;
        _sourceWeapon = null;
        _overlaps.Clear();
        _hitSnapshot = default;
        ExpansionOwner = null; _empowered = false; _burnDamage = 0;
        _owner = null;
        _weaponData = null;
        _damage = 0f;
        _hitColliders.Clear();
        _hitTargets.Clear();
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
        float startAngleOffset = 0f, WeaponHitSnapshot hitSnapshot = default)
    {
        Initialize(
            null,
            owner,
            facesRight,
            damage,
            range,
            arc,
            duration,
            startAngleOffset, hitSnapshot);
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
        float startAngleOffset = 0f, WeaponHitSnapshot hitSnapshot = default)
    {
        _hitSnapshot = hitSnapshot;
        _weaponData = weaponData;
        _owner = owner;
        _damage = Mathf.Max(0f, damage);
        _duration = Mathf.Max(0.02f, duration);
        _elapsedTime = 0f;
        _thrust = false;
        _directed = false;
        _targeted = false;
        _blendFromHeld = false;
        _hitCollider.enabled = true;
        _hitColliders.Clear();
        _hitTargets.Clear();

        float swingArc = Mathf.Clamp(arc, 1f, 360f);
        _startAngle = 90f + startAngleOffset;
        _endAngle = facesRight
            ? _startAngle - swingArc
            : _startAngle + swingArc;

        RefreshGeometry(range);
        transform.position = owner != null ? owner.position : transform.position;
        transform.rotation = Quaternion.Euler(0f, 0f, _startAngle);
    }

    /// <summary>从武器挂点向指定世界方向执行挥击或突刺，方向在动作开始时固定。</summary>
    public void InitializeDirected(WeaponDataSO data, Transform mount, Vector2 direction,
        bool thrust, float damage, float range, float arc, float duration, WeaponHitSnapshot snapshot)
    {
        Initialize(data, mount, direction.x >= 0, damage, range, arc, duration, 0f, snapshot);
        _thrust = thrust;
        _directed = true;
        _centerAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        float halfArc = Mathf.Clamp(arc, 1f, 360f) * .5f;
        _startAngle = thrust ? _centerAngle : _centerAngle + halfArc;
        _endAngle = thrust ? _centerAngle : _centerAngle - halfArc;
        // 两种动作使用相同剑身；只有突刺额外前伸，横挥固定握持半径。
        _thrustTravel = Mathf.Max(.05f, range) * .4f;
        RefreshGeometry(Mathf.Max(.05f, range) * .6f);
        if (spriteRenderer != null && data != null && data.icon != null)
        {
            spriteRenderer.sprite = data.icon;
            float size = WeaponVisualGeometry.MeleeLength(range) / WeaponVisualGeometry.ProjectedLength(data.icon, data.visualAngleOffset);
            visualRoot.localScale = new Vector3(size, size, 1f);
            visualRoot.localPosition = Vector3.right * WeaponVisualGeometry.MeleeCenter(range);
            visualRoot.localRotation = Quaternion.Euler(0f, 0f, data.visualAngleOffset);
        }
        _restColliderOffset = _hitCollider.offset;
        _restVisualPosition = visualRoot != null ? visualRoot.localPosition : Vector3.zero;
        transform.rotation = Quaternion.Euler(0f, 0f, _centerAngle);
    }

    /// <summary>快照落点和时序。枢轴沿攻击方向退回到目标靠近玩家的一侧，横挥与突刺都保留瞄准轴。</summary>
    public void ConfigureTargeted(Vector2 target, MeleeAttackTiming timing)
    {
        _targeted = true;
        _sourceWeapon = _owner.GetComponent<WeaponBase>();
        _timing = timing;
        _duration = timing.Total;
        _launchPosition = _owner.position;
        float radius = _restVisualPosition.x;
        Vector2 forward = Quaternion.Euler(0, 0, _centerAngle) * Vector2.right;
        // 基准点必须在目标朝向玩家的一侧；世界右侧和固定 180 度会让不同方位的挥击都横着朝左。
        // 保留 InitializeDirected 快照的攻击方向及扇形角度，刀身中点在中段经过锁定目标，握柄朝内。
        // 突刺和横挥共享这条局部 +X 攻击轴，素材校正角只在视觉子节点上使用一次。
        _attackPivot = (Vector3)(target - forward * radius);
        // 以素材实际长度作为碰撞长度；前摇和回收禁用接触伤害，由主动段的胶囊采样结算。
        float length = visualRoot != null && spriteRenderer.sprite != null
            ? WeaponVisualGeometry.ProjectedLength(spriteRenderer.sprite, _weaponData.visualAngleOffset) * visualRoot.localScale.x
            : radius * 2f;
        _hitCollider.size = new Vector2(length, Mathf.Max(.08f, length * .2f));
        _hitCollider.offset = _restColliderOffset = (Vector2)_restVisualPosition;
        _hitCollider.enabled = false;
    }

    /// <summary>来源校验必须包含活跃状态，避免武器保留的池引用取消其他装备的动作。</summary>
    public bool BelongsTo(Transform mount) => gameObject.activeInHierarchy && _owner == mount;

    /// <summary>来源移除或换波时只回收，不触发余波、残影等正常完成效果。</summary>
    public void Cancel() { ReleaseToPool(); }

    /// <summary>目标锁定后的三段动作：前摇移动、圆弧/直线伤害段、无伤害回收。目标移动不牵引挥击。</summary>
    private void UpdateTargeted(float previousTime)
    {
        float activeStart = _timing.Windup;
        float activeEnd = activeStart + _timing.Swing;
        if (_elapsedTime >= activeStart && previousTime <= activeEnd)
        {
            float from = Mathf.Clamp01((previousTime - activeStart) / _timing.Swing);
            float to = Mathf.Clamp01((_elapsedTime - activeStart) / _timing.Swing);
            // 补采两帧之间的动作路径，高攻速或卡帧跨过整个主动段时也不会漏伤害。
            // 按刀身宽度限制采样间隔，缓存列表可扩容，密集敌群不会被固定数组截断。
            float distance = _thrust ? _thrustTravel : Mathf.Abs(_endAngle - _startAngle) * Mathf.Deg2Rad * _restVisualPosition.x;
            int steps = Mathf.Max(1, Mathf.CeilToInt(distance * (to - from) / Mathf.Max(.025f, (_blendFromHeld ? _targetColliderSize.y : _hitCollider.size.y) * .4f)));
            for (int i = 0; i <= steps; i++) SampleStrike(Mathf.Lerp(from, to, i / (float)steps));
        }
        if (_elapsedTime < activeStart)
        {
            float blend = Mathf.SmoothStep(0, 1, _elapsedTime / activeStart);
            transform.position = Vector3.Lerp(_launchPosition, _attackPivot, blend);
            transform.rotation = Quaternion.Euler(0, 0, Mathf.LerpAngle(_blendFromHeld ? _initialAngle : _centerAngle, _startAngle, blend));
            if (visualRoot != null)
            {
                Vector3 start = _blendFromHeld ? _initialVisualPosition : _restVisualPosition;
                Vector3 end = _restVisualPosition - (_thrust ? Vector3.right * _thrustTravel : Vector3.zero);
                visualRoot.localPosition = Vector3.Lerp(start, end, blend);
                if (_blendFromHeld) visualRoot.localScale = Vector3.Lerp(_initialVisualScale, _targetVisualScale, blend);
            }
        }
        else if (_elapsedTime <= activeEnd) ApplyStrikePose((_elapsedTime - activeStart) / _timing.Swing);
        else
        {
            float blend = Mathf.SmoothStep(0, 1, (_elapsedTime - activeEnd) / _timing.Recovery);
            float restAngle = _centerAngle;
            if (_sourceWeapon != null)
            {
                Vector2 aim = _sourceWeapon.CurrentAimDirection;
                restAngle = Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg;
            }
            transform.position = Vector3.Lerp(_attackPivot, _owner.position, blend);
            transform.rotation = Quaternion.Euler(0, 0, Mathf.LerpAngle(_endAngle, restAngle, blend));
            if (visualRoot != null) visualRoot.localPosition = _restVisualPosition;
        }
        if (_elapsedTime >= _duration)
        {
            if (ExpansionOwner != null)
                ExpansionOwner.CompleteMelee(_expansionOrigin, Quaternion.Euler(0, 0, _centerAngle) * Vector2.right,
                    _hitTargets, _damage, _hitSnapshot, _empowered);
            ReleaseToPool();
        }
    }

    /// <summary>仅计算主动段表现姿态；横挥半径固定，突刺沿锁定方向前伸，不叠加椭圆缩放。</summary>
    private void ApplyStrikePose(float progress)
    {
        transform.position = _attackPivot;
        if (_blendFromHeld) _hitCollider.size = _targetColliderSize;
        float angle = _thrust ? _centerAngle : Mathf.Lerp(_startAngle, _endAngle, progress);
        transform.rotation = Quaternion.Euler(0, 0, angle);
        Vector3 offset = _restVisualPosition - (_thrust ? Vector3.right * _thrustTravel * (1f - progress) : Vector3.zero);
        _hitCollider.offset = (Vector2)offset;
        if (visualRoot != null)
        {
            visualRoot.localPosition = offset;
            if (_blendFromHeld) visualRoot.localScale = _targetVisualScale;
        }
    }

    /// <summary>按当前胶囊几何查询敌人，不启用回程碰撞；共享本次生命代次集合保证每个目标只受击一次。</summary>
    private void SampleStrike(float progress)
    {
        ApplyStrikePose(progress);
        var filter = new ContactFilter2D { useTriggers = true };
        filter.SetLayerMask(DamageTargetFilter.EnemyLayerMask);
        _overlaps.Clear();
        Vector2 size = Vector2.Scale(_blendFromHeld ? _targetColliderSize : _hitCollider.size, (Vector2)transform.lossyScale);
        Physics2D.OverlapCapsule(transform.TransformPoint(_hitCollider.offset), size, CapsuleDirection2D.Horizontal,
            transform.eulerAngles.z, filter, _overlaps);
        for (int i = 0; i < _overlaps.Count; i++) OnTriggerEnter2D(_overlaps[i]);
    }

    /// <summary>主动作从当前持武姿势起步，范围刚改变时也连续过渡；额外多发保留各自散射方向。</summary>
    public void BeginFromHeld(WeaponHeldView view)
    {
        if (view == null || view.Renderer == null || visualRoot == null) return;
        _heldView = view;
        // 攻击与持武沿用同一排序，移动到怪物旁边后也不会因为模板层级突然被遮住。
        spriteRenderer.sortingLayerID = view.Renderer.sortingLayerID;
        spriteRenderer.sortingOrder = view.Renderer.sortingOrder;
        _blendFromHeld = true;
        _targetVisualScale = visualRoot.localScale;
        _initialVisualScale = view.Renderer.transform.lossyScale / Mathf.Max(.001f, transform.lossyScale.x);
        _initialAngle = view.Renderer.transform.eulerAngles.z - _weaponData.visualAngleOffset;
        transform.rotation = Quaternion.Euler(0f, 0f, _initialAngle);
        _initialVisualPosition = transform.InverseTransformPoint(view.Renderer.transform.position);
        _targetColliderSize = _hitCollider.size;
        _initialColliderSize = _targetColliderSize * (_initialVisualScale.x / Mathf.Max(.001f, _targetVisualScale.x));
        _hitCollider.size = _initialColliderSize;
        _hitCollider.offset = (Vector2)_initialVisualPosition;
        visualRoot.localScale = _initialVisualScale;
        visualRoot.localPosition = _initialVisualPosition;
        view.FollowAttack(spriteRenderer);
    }

    /// <summary>
    /// 让根节点保持在武器挂点，并把视觉和胶囊判定放到内圈之外、攻击范围之内。
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
            Vector2 spriteSize = spriteRenderer != null && spriteRenderer.sprite != null
                ? (Vector2)spriteRenderer.sprite.bounds.size : Vector2.one;
            float radians = visualAngleOffset * Mathf.Deg2Rad;
            float spriteLength = Mathf.Max(.01f, Mathf.Abs(Mathf.Cos(radians)) * spriteSize.x * _baseVisualScale.x
                + Mathf.Abs(Mathf.Sin(radians)) * spriteSize.y * _baseVisualScale.y);
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

        if (_targeted && !RoundController.AllowsCombat)
        {
            // 暂停保留当前姿态和命中集合；换波、结算才取消，不能把暂停误认为攻击来源已结束。
            if (RoundController.Enabled && RoundController.Instance.Phase != RoundPhase.Combat) ReleaseToPool();
            return;
        }
        if (_targeted && Time.deltaTime <= 0f) return;
        float previousTime = _elapsedTime;
        _elapsedTime += Time.deltaTime;
        if (_targeted) { UpdateTargeted(previousTime); return; }
        float normalizedTime = Mathf.Clamp01(_elapsedTime / _duration);
        float easedTime = Mathf.SmoothStep(0f, 1f, normalizedTime);
        float angle = Mathf.Lerp(_startAngle, _endAngle, easedTime);

        if (_directed)
        {
            // 先从休息方向预备，扫过扇面后收回；端点都回到瞄准方向，交接没有姿势跳变。
            if (!_thrust)
                angle = normalizedTime < .2f
                    ? Mathf.Lerp(_centerAngle, _startAngle, Mathf.SmoothStep(0f, 1f, normalizedTime / .2f))
                    : normalizedTime < .8f
                        ? Mathf.Lerp(_startAngle, _endAngle, Mathf.SmoothStep(0f, 1f, (normalizedTime - .2f) / .6f))
                        : Mathf.Lerp(_endAngle, _centerAngle, Mathf.SmoothStep(0f, 1f, (normalizedTime - .8f) / .2f));
            // 前半程伸出，后半程收回；平滑曲线在两端速度为零。
            float progress = normalizedTime < .5f ? normalizedTime * 2f : (1f - normalizedTime) * 2f;
            // 横挥的武器中心与刀尖半径保持不变；叠加径向伸缩会把圆弧挤成椭圆。
            float extension = _thrust ? Mathf.SmoothStep(0f, 1f, progress) * _thrustTravel : 0f;
            _hitCollider.offset = _restColliderOffset + Vector2.right * extension;
            if (visualRoot != null) visualRoot.localPosition = _restVisualPosition + Vector3.right * extension;
        }
        if (_blendFromHeld)
        {
            float blend = Mathf.SmoothStep(0f, 1f, normalizedTime / .2f);
            angle = Mathf.LerpAngle(_initialAngle, angle, blend);
            visualRoot.localPosition = Vector3.Lerp(_initialVisualPosition, visualRoot.localPosition, blend);
            visualRoot.localScale = Vector3.Lerp(_initialVisualScale, _targetVisualScale, blend);
            _hitCollider.size = Vector2.Lerp(_initialColliderSize, _targetColliderSize, blend);
            _hitCollider.offset = Vector2.Lerp((Vector2)_initialVisualPosition, _hitCollider.offset, blend);
        }
        transform.position = _owner.position;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);

        if (_elapsedTime >= _duration)
        {
            if (ExpansionOwner != null)
                ExpansionOwner.CompleteMelee(_expansionOrigin, Quaternion.Euler(0, 0, _centerAngle) * Vector2.right,
                    _hitTargets, _damage, _hitSnapshot, _empowered);
            ReleaseToPool();
        }
    }

    /// <summary>
    /// 对首次进入本次挥击的可受击实体结算伤害。
    /// </summary>
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!DamageTargetFilter.TryGetEnemyDamageable(other, out IDamageable damageable))
        {
            return;
        }

        Component identity = WeaponTargeting.Identity(other);
        if (!WeaponTargeting.IsValidCached(other, identity, 0)) return;
        uint currentGeneration = identity is EnemyBase current ? current.LifeGeneration : 0;
        if (_hitTargets.TryGetValue(identity, out uint previous) && previous == currentGeneration) return;
        _hitTargets[identity] = currentGeneration;
        _hitColliders.Add(other);
        uint generation = identity is EnemyBase prior ? prior.LifeGeneration : 0;
        CombatDamageResult result = _hitSnapshot.Apply(damageable, _damage, _weaponData);
        if (result.Accepted && ExpansionOwner != null && _weaponData.expansionKind == ExpansionWeaponKind.Whip
            && identity is EnemyBase enemy && enemy.CurrentHealth > 0 && enemy.LifeGeneration == generation)
            enemy.CombatStatus.ApplyBurn(ExpansionOwner.transform, _burnDamage, _weaponData);
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
        _hitTargets.Clear();

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
