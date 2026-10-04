using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 池化近战实体。正式动作以挂点为参考，叠加瞄准旋转、局部平移与刀身旋转，并补采伤害路径。
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
    private float _motionDistance, _motionRecoil, _trackingRadius, _aimOffset;
    private bool _facesRightAtStart;
    private MeleeMotionPose _initialPose;
    private Vector3 _previousMountPosition;
    private float _previousAimAngle;
    private Collider2D _trackedTarget;
    private Component _trackedIdentity;
    private uint _trackedGeneration;
    private readonly List<Collider2D> _overlaps = new List<Collider2D>(64);
    public bool IsStriking => _targeted && _elapsedTime >= _timing.Windup && _elapsedTime <= _timing.Windup + _timing.Swing;
    public Vector3 AttackPivot => _owner != null ? _owner.position : transform.position;
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
    { ExpansionOwner = source; _empowered = empowered; _burnDamage = burnDamage; _expansionOrigin = _owner != null ? (Vector2)_owner.position : (Vector2)transform.position; }


    /// <summary>对象池回收时清除旧武器来源、玩家跟随和本次命中集合。</summary>
    private void OnDisable()
    {
        if (_heldView != null) _heldView.CompleteAttack(spriteRenderer);
        _heldView = null;
        _blendFromHeld = false;
        _targeted = false;
        _sourceWeapon = null;
        _trackedTarget = null; _trackedIdentity = null; _trackedGeneration = 0;
        _initialPose = default;
        if (spriteRenderer != null) spriteRenderer.flipY = false;
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

    /// <summary>准备池化近战；刀身长度/宽度来自固定配置，范围仅保存在动作行程中。</summary>
    public void InitializeDirected(WeaponDataSO data, Transform mount, Vector2 direction,
        bool thrust, float damage, float range, float arc, float duration, WeaponHitSnapshot snapshot)
    {
        Initialize(data, mount, direction.x > 0f, damage, range, arc, duration, 0f, snapshot);
        _thrust = thrust;
        _directed = true;
        _centerAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        _motionDistance = range;
        // 攻击实体不成为玩家的子对象，避免改动对象池所有权；每帧显式组合世界姿势。
        transform.localScale = Vector3.one;
        float length = Mathf.Max(.05f, data.heldSize);
        _restVisualPosition = Vector3.right * (data.meleeGripOffset + length * .5f);
        _restColliderOffset = _restVisualPosition;
        _hitCollider.size = new Vector2(length, Mathf.Clamp(data.meleeHitWidth, .01f, length));
        _hitCollider.offset = _restColliderOffset;
        _hitCollider.enabled = false;
        if (spriteRenderer != null && data.icon != null) spriteRenderer.sprite = data.icon;
        if (visualRoot != null)
        {
            float size = length / WeaponVisualGeometry.ProjectedLength(spriteRenderer.sprite, data.visualAngleOffset);
            visualRoot.localScale = new Vector3(size, size, 1f);
            visualRoot.localPosition = _restVisualPosition;
        }
        transform.rotation = Quaternion.Euler(0f, 0f, _centerAngle);
        ApplyMirror();
    }

    /// <summary>捕获一次自动目标身份与行程；自动模式只跟踪此目标，手动模式固定出手方向。</summary>
    public void ConfigureMotion(WeaponBase source, Collider2D target, bool automatic)
    {
        _targeted = true;
        _sourceWeapon = source;
        _trackedTarget = automatic ? target : null;
        _trackedIdentity = WeaponTargeting.Identity(_trackedTarget);
        _trackedGeneration = _trackedIdentity is EnemyBase enemy ? enemy.LifeGeneration : 0;
        float targetDistance = _trackedIdentity != null
            ? Vector2.Distance(_owner.position, _trackedIdentity.transform.position) : source.CurrentVisualRange;
        _motionDistance = MeleeAttackMotion.Distance(_thrust, source.CurrentVisualRange, targetDistance);
        _motionRecoil = source.CurrentMeleeRecoil;
        _timing = source.GetMeleeTiming(_motionDistance);
        _duration = _timing.Total;
        _facesRightAtStart = MeleeAttackMotion.FacesRight(_centerAngle);
        _trackingRadius = source.CurrentVisualRange + 2f;
        Vector2 baseDirection = source.CurrentAimDirection;
        _aimOffset = Mathf.DeltaAngle(Mathf.Atan2(baseDirection.y, baseDirection.x) * Mathf.Rad2Deg, _centerAngle);
        _previousAimAngle = _centerAngle;
        _previousMountPosition = _owner.position;
        _initialPose = default;
        ApplyMotionPose(0f, _previousMountPosition, _centerAngle);
    }

    /// <summary>来源校验必须包含活跃状态，避免武器保留的池引用取消其他装备的动作。</summary>
    public bool BelongsTo(Transform mount) => gameObject.activeInHierarchy && _owner == mount;

    /// <summary>来源移除或换波时只回收，不触发余波、残影等正常完成效果。</summary>
    public void Cancel() { ReleaseToPool(); }

    /// <summary>推进局部动作并补采有效伤害窗口；失效目标不重选，回收段不造成直接伤害。</summary>
    private void UpdateTargeted(float previousTime)
    {
        Vector3 mount = _owner.position;
        // 地图/回合正常迁移会先取消动作；额外大幅位置跳变也取消，避免在瞬移连线上制造伤害。
        float discontinuity = Mathf.Max(4f, _motionDistance * 2f);
        if ((mount - _previousMountPosition).sqrMagnitude > discontinuity * discontinuity)
        {
            if (_heldView != null) _heldView.ResetView();
            Cancel();
            return;
        }
        UpdateTrackedAim(mount);
        float from = Mathf.Max(previousTime, _timing.Windup);
        float to = Mathf.Min(_elapsedTime, _timing.Windup + _timing.Swing);
        if (to >= from && _elapsedTime >= _timing.Windup && previousTime <= _timing.Windup + _timing.Swing)
            SampleMotion(from, to, previousTime, mount);
        ApplyMotionPose(_elapsedTime, mount, _centerAngle);
        _previousMountPosition = mount;
        _previousAimAngle = _centerAngle;
        if (_elapsedTime >= _duration)
        {
            if (ExpansionOwner != null)
                ExpansionOwner.CompleteMelee(_expansionOrigin, Quaternion.Euler(0, 0, _centerAngle) * Vector2.right,
                    _hitTargets, _damage, _hitSnapshot, _empowered);
            ReleaseToPool();
        }
    }

    /// <summary>自动攻击持续朝向原目标；死亡、回池或离开检测圈后永久释放引用，保留最后角度。</summary>
    private void UpdateTrackedAim(Vector3 mount)
    {
        if (_trackedTarget == null) return;
        if (!WeaponTargeting.IsValidCached(_trackedTarget, _trackedIdentity, _trackedGeneration)
            || ((Vector2)_trackedIdentity.transform.position - (Vector2)mount).sqrMagnitude > _trackingRadius * _trackingRadius)
        {
            _trackedTarget = null; _trackedIdentity = null; _trackedGeneration = 0;
            return;
        }
        Vector2 direction = _trackedIdentity.transform.position - mount;
        if (direction.sqrMagnitude > .000001f)
            _centerAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg + _aimOffset;
    }

    /// <summary>把局部握柄姿势组合为世界位置/旋转，碰撞和表现消费同一结果。</summary>
    private void EvaluateWorldPose(float time, Vector3 mount, float aim, out Vector3 position, out float angle)
    {
        MeleeMotionPose pose = MeleeAttackMotion.Evaluate(_thrust, _facesRightAtStart, _motionDistance,
            _motionRecoil, _timing, time, _initialPose);
        position = mount + Quaternion.Euler(0f, 0f, aim) * (Vector3)pose.Position;
        angle = aim + pose.Angle;
    }

    /// <summary>只更新最终可见姿势，不在补采中反复改 Transform，减少密集战斗的变换同步开销。</summary>
    private void ApplyMotionPose(float time, Vector3 mount, float aim)
    {
        EvaluateWorldPose(time, mount, aim, out Vector3 position, out float angle);
        transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, angle));
        if (visualRoot != null) visualRoot.localPosition = _restVisualPosition;
        ApplyMirror();
    }

    /// <summary>镜像只改变贴图；校正角随镜像反号，使斜向素材也绕攻击轴镜像，不反转碰撞偏移。</summary>
    private void ApplyMirror()
    {
        bool flip = !MeleeAttackMotion.FacesRight(_centerAngle);
        if (spriteRenderer != null) spriteRenderer.flipY = flip;
        if (visualRoot != null && _weaponData != null)
            visualRoot.localRotation = Quaternion.Euler(0f, 0f, _weaponData.visualAngleOffset * (flip ? -1f : 1f));
    }

    /// <summary>
    /// 根据位移/角速度上界决定采样密度，覆盖主动段两条折线、突刺缓出及玩家移动/瞄准变化。
    /// 只查询本帧有效时间窗；复用结果列表及生命代次字典，无逐帧数组分配。
    /// </summary>
    private void SampleMotion(float from, float to, float previousTime, Vector3 mount)
    {
        float frameSpan = Mathf.Max(.000001f, _elapsedTime - previousTime);
        float activeFraction = Mathf.Clamp01((to - from) / _timing.Swing);
        float radius = _restColliderOffset.x + _hitCollider.size.x * .5f;
        // Expo Out 最大导数为 10*ln(2)；横挥两半段长度相同，旋转总量为 324 度。
        float pathBound = _thrust ? 6.932f * (_motionDistance + _motionRecoil)
            : 2f * new Vector2(.75f * _motionDistance + _motionRecoil, .5f * _motionDistance).magnitude
                + 2f * MeleeAttackMotion.SweepHalfAngle * Mathf.Deg2Rad * radius;
        float frameFraction = Mathf.Clamp01((to - from) / frameSpan);
        pathBound = pathBound * activeFraction + Vector3.Distance(mount, _previousMountPosition) * frameFraction
            + Mathf.Abs(Mathf.DeltaAngle(_previousAimAngle, _centerAngle)) * Mathf.Deg2Rad
                * (_motionDistance + _motionRecoil + radius) * frameFraction;
        int steps = Mathf.Max(1, Mathf.CeilToInt(pathBound / Mathf.Max(.005f, _hitCollider.size.y * .4f)));
        var filter = new ContactFilter2D { useTriggers = true };
        filter.SetLayerMask(DamageTargetFilter.EnemyLayerMask);
        for (int i = 0; i <= steps; i++)
        {
            float time = Mathf.Lerp(from, to, i / (float)steps);
            float blend = Mathf.Clamp01((time - previousTime) / frameSpan);
            Vector3 atMount = Vector3.Lerp(_previousMountPosition, mount, blend);
            float atAim = Mathf.LerpAngle(_previousAimAngle, _centerAngle, blend);
            EvaluateWorldPose(time, atMount, atAim, out Vector3 position, out float angle);
            Vector2 center = position + Quaternion.Euler(0f, 0f, angle) * (Vector3)_restColliderOffset;
            _overlaps.Clear();
            Physics2D.OverlapCapsule(center, _hitCollider.size, CapsuleDirection2D.Horizontal, angle, filter, _overlaps);
            for (int j = 0; j < _overlaps.Count; j++) OnTriggerEnter2D(_overlaps[j]);
        }
    }

    /// <summary>主动作从当前持武姿势起步，范围刚改变时也连续过渡；额外多发保留各自散射方向。</summary>
    public void BeginFromHeld(WeaponHeldView view)
    {
        if (view == null || view.Renderer == null || visualRoot == null) return;
        _heldView = view;
        // 攻击与持武沿用同一排序，移动到怪物旁边后也不会因为模板层级突然被遮住。
        spriteRenderer.sortingLayerID = view.Renderer.sortingLayerID;
        spriteRenderer.sortingOrder = view.Renderer.sortingOrder;
        if (_targeted)
        {
            // 从实际持武刀身中心反推出握柄世界位置，再转换到本次瞄准的局部坐标。
            float correction = _weaponData.visualAngleOffset * (view.Renderer.flipY ? -1f : 1f);
            float heldAngle = view.Renderer.transform.eulerAngles.z - correction;
            Vector3 grip = view.Renderer.transform.position - Quaternion.Euler(0f, 0f, heldAngle) * _restVisualPosition;
            Vector2 offset = Quaternion.Euler(0f, 0f, -_centerAngle) * (grip - _owner.position);
            _initialPose = new MeleeMotionPose(offset, Mathf.DeltaAngle(_centerAngle, heldAngle));
            ApplyMotionPose(0f, _owner.position, _centerAngle);
            view.FollowAttack(spriteRenderer);
            return;
        }
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
        if (_targeted && (Time.timeScale <= 0f || Time.deltaTime <= 0f)) return;
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
            enemy.CombatStatus.ApplyWeaponBurn(ExpansionOwner.transform, _burnDamage, _weaponData, _hitSnapshot);
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
