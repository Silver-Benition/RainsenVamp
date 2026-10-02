using System.Collections.Generic;
using UnityEngine;

/// <summary>池化效果的运动/判定类型，不通过素材名称决定行为。</summary>
public enum ExpansionEffectKind { Bolt, Beam, Cone, Mine, Returning, Frost, Seed, SeedChild, Ink, Wave, Echo, Mortar, Chain }

/// <summary>一次发射的值快照；所有者负责配置，效果实例只保存本次运行状态。</summary>
public struct EffectSpec
{
    public ExpansionEffectKind Kind;
    public Transform Source, Owner;
    public WeaponDataSO Weapon;
    public WeaponHitSnapshot Hit;
    public Sprite Sprite;
    public Vector2 Direction, Target;
    public float Damage, Range, Width, Duration, Delay, Speed, VisualLength, Slow, SlowSeconds;
    public int Tier, MaxHits;
}

/// <summary>复用的直线、定点和持续攻击实体；局部物理查询、伤害与可视线分离，跨波/来源消失立即回收。</summary>
[RequireComponent(typeof(SpriteRenderer), typeof(LineRenderer))]
public sealed class ExpansionEffect : MonoBehaviour, IPoolable
{
    private static readonly List<ExpansionEffect> Active = new List<ExpansionEffect>(64);
    private readonly List<Collider2D> _candidates = new List<Collider2D>(128);
    private readonly Dictionary<Component, uint> _hits = new Dictionary<Component, uint>(64);
    private readonly HashSet<Component> _chainHits = new HashSet<Component>();
    private EffectSpec _spec;
    private SpriteRenderer _sprite;
    private LineRenderer _line;
    private GameObject _prefab;
    private Vector2 _origin, _direction;
    private float _time, _distance, _nextTick, _detonatedAt, _bornAt, _inkTickAt;
    private int _round, _ticks, _hitCount;
    private bool _configured, _fired, _returning, _detonated;
    public ExpansionEffectKind Kind => _spec.Kind;
    public float Damage => _spec.Damage;
    public bool IsConfigured => _configured;

    /// <summary>缓存表现组件；实际判定使用物理查询而非可视线宽。</summary>
    private void Awake() { _sprite = GetComponent<SpriteRenderer>(); _line = GetComponent<LineRenderer>(); }
    /// <summary>对象池登记资产键。</summary>
    public void SetPrefabReference(GameObject prefab) { _prefab = prefab; }
    /// <summary>完全初始化一次池生命周期，旧目标、旧时间及旧表现均不复用。</summary>
    public void Configure(EffectSpec spec)
    {
        _spec = spec; _origin = transform.position; _bornAt = Time.time;
        _direction = spec.Direction.sqrMagnitude > .001f ? spec.Direction.normalized : Vector2.right;
        _time = _distance = _detonatedAt = 0; _ticks = _hitCount = 0; _nextTick = spec.Kind == ExpansionEffectKind.Ink ? .5f : .2f;
        _fired = _returning = _detonated = false; _hits.Clear(); _chainHits.Clear();
        _round = RoundController.Instance?.Current?.Generation ?? -1; _configured = true;
        if (!Active.Contains(this)) Active.Add(this);
        _sprite.sprite = spec.Sprite; _sprite.enabled = spec.Sprite != null; _sprite.color = Color.white;
        _line.enabled = false; _line.positionCount = 0; _line.loop = false; _line.useWorldSpace = true;
        transform.localScale = Vector3.one;
        if (spec.Sprite != null)
        {
            float length = Mathf.Max(.001f, spec.Sprite.bounds.size.x);
            transform.localScale = Vector3.one * Mathf.Max(.1f, spec.VisualLength) / length;
        }
        transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(_direction.y, _direction.x) * Mathf.Rad2Deg);
        if (spec.Kind == ExpansionEffectKind.Ink) _sprite.enabled = false;
    }
    /// <summary>确认引用仍属于原来源；用于阻止同一池对象跨武器复用后被误回收。</summary>
    public bool BelongsTo(Transform source) { return _configured && gameObject.activeInHierarchy && _spec.Source == source; }
    /// <summary>复制近战实际命中的身份代次，余波不能再次击中本次拳头已经命中的敌人。</summary>
    public void Exclude(Dictionary<Component, uint> targets)
    { foreach (var target in targets) if (target.Key != null) _hits[target.Key] = target.Value; }
    /// <summary>回收同一来源的全部子效果，包括没有直接登记到武器列表的分裂弹。</summary>
    public static void ReleaseSource(Transform source)
    { for (int i = Active.Count - 1; i >= 0; i--) if (Active[i]._spec.Source == source) Active[i].Release(); }
    /// <summary>暂停不推进时间；波次、所有者、来源不符时不能留下延迟伤害。</summary>
    private void Update()
    {
        if (!_configured) return;
        if (_spec.Source == null || !_spec.Source.gameObject.activeInHierarchy ||
            (RoundController.Enabled && (RoundController.Instance.Phase != RoundPhase.Combat || _round != RoundController.Instance.Current.Generation)))
        { Release(); return; }
        if (!RoundController.AllowsCombat) return;
        _time += Time.deltaTime;
        switch (_spec.Kind)
        {
            case ExpansionEffectKind.Beam: Beam(); break;
            case ExpansionEffectKind.Cone: Cone(); break;
            case ExpansionEffectKind.Mine: Mine(); break;
            case ExpansionEffectKind.Returning: Returning(); break;
            case ExpansionEffectKind.Frost: Area(false); break;
            case ExpansionEffectKind.Mortar: Area(true); break;
            case ExpansionEffectKind.Ink: Ink(); break;
            case ExpansionEffectKind.Echo: Echo(); break;
            case ExpansionEffectKind.Chain: Chain(); break;
            default: Fly(); break;
        }
    }
    /// <summary>蓄能阶段保留短预兆；实际射束按沿射线的前后顺序命中有限目标。</summary>
    private void Beam()
    {
        Vector2 origin = _spec.Source.position; _sprite.enabled = false;
        DrawSegment(origin, origin + _direction * (_time < _spec.Delay ? .6f : _spec.Range), _time < _spec.Delay ? .04f : .12f, Color.cyan);
        if (!_fired && _time >= _spec.Delay) { _fired = true; HitLine(origin, origin + _direction * _spec.Range, _spec.Width, _spec.MaxHits); }
        if (_time >= _spec.Delay + .14f) Release();
    }
    /// <summary>固定三次喷射，不因攻速增加跳数；玩家移动时喷口随挂点移动。</summary>
    private void Cone()
    {
        Vector2 origin = _spec.Source.position; DrawCone(origin, _spec.Range, _spec.Width, new Color(.2f,.85f,1,.75f));
        while (_ticks < 3 && _time + .0001f >= _nextTick)
        { _hits.Clear(); HitCone(origin, _spec.Range, _spec.Width); _ticks++; _nextTick += .2f; }
        if (_time >= .65f) Release();
    }
    /// <summary>逐帧扫过上一位置到本帧位置，避免高速弹体穿过薄目标。</summary>
    private void Fly()
    {
        Vector2 before = transform.position; float step = Mathf.Min(Mathf.Max(.1f, _spec.Speed) * Time.deltaTime, Mathf.Max(0, _spec.Range - _distance));
        Vector2 after = before + _direction * step; transform.position = after; _distance += step;
        int limit = _spec.Kind == ExpansionEffectKind.Wave ? _spec.MaxHits : 1;
        if (_spec.Kind == ExpansionEffectKind.Wave) DrawSegment(after - _direction * .15f, after, _spec.Width, new Color(1,.7f,.2f,.85f));
        Collider2D first = HitLine(before, after, _spec.Width > 0 ? _spec.Width : .14f, limit);
        if (first != null && _spec.Kind == ExpansionEffectKind.Seed) Split(first);
        if ((_hitCount >= Mathf.Max(1,limit)) || _distance >= _spec.Range || _time > 8) Release();
    }
    /// <summary>去程与回程各有独立命中集合；回程追随玩家当前位置并有超时兜底。</summary>
    private void Returning()
    {
        if (_spec.Owner == null) { Release(); return; }
        Vector2 before = transform.position;
        float step = Mathf.Max(1, _spec.Speed) * Time.deltaTime;
        Vector2 after;
        if (!_returning)
        {
            step = Mathf.Min(step, _spec.Range - _distance); after = before + _direction * step; _distance += step;
        }
        else after = Vector2.MoveTowards(before, _spec.Owner.position, step);
        transform.position = after; transform.Rotate(0,0,540 * Time.deltaTime);
        HitLine(before, after, .22f, int.MaxValue);
        if (!_returning && _distance >= _spec.Range) { _returning = true; _hits.Clear(); _hitCount = 0; }
        else if (_returning && Vector2.Distance(after, _spec.Owner.position) < .15f) Release();
        if (_time > 8) Release();
    }
    /// <summary>子种继承发射快照，排除母种目标且不再递归分裂。</summary>
    private void Split(Collider2D first)
    {
        Component excluded = WeaponTargeting.Identity(first);
        for (int i = -1; i <= 1; i++)
        {
            EffectSpec child = _spec; child.Kind = ExpansionEffectKind.SeedChild;
            child.Direction = Quaternion.Euler(0,0,i * 24) * _direction;
            child.Damage = Mathf.Max(1, Mathf.Floor(_spec.Damage * .35f)); child.Range = 1.8f + .2f * (_spec.Tier - 1); child.VisualLength *= .65f;
            GameObject go = PoolManager.Instance.Spawn(_prefab, first.ClosestPoint(transform.position), Quaternion.identity);
            if (go == null) continue;
            ExpansionEffect effect = go.GetComponent<ExpansionEffect>(); effect.Configure(child);
            if (excluded != null) effect._hits[excluded] = Generation(excluded);
        }
    }
    /// <summary>投送、布防、接近触发和爆炸分阶段执行；到期回收不额外爆炸。</summary>
    private void Mine()
    {
        if (_time < .35f) { transform.position = Vector2.Lerp(_origin,_spec.Target,_time/.35f) + Vector2.up * Mathf.Sin(_time/.35f*Mathf.PI)*.35f; return; }
        transform.position = _spec.Target; transform.rotation = Quaternion.identity;
        if (_detonated)
        { _sprite.enabled = false; DrawCircle(_spec.Target,_spec.Width,new Color(1,.55f,.15f,.85f)); if (_time > _detonatedAt + .2f) Release(); return; }
        _sprite.color = _time < .7f ? Color.gray : Color.white;
        if (_time >= .7f && WeaponTargeting.FindNearest(_spec.Target,.45f) != null)
        { _detonated = true; _detonatedAt = _time; HitCircle(_spec.Target,_spec.Width); }
        else if (_time >= .35f + _spec.Duration) Release();
    }
    /// <summary>定点预警之后一次结算；迫击炮的飞行时间不受建筑攻速缩短。</summary>
    private void Area(bool mortar)
    {
        float delay = mortar ? .7f : .15f;
        DrawCircle(_spec.Target,_spec.Width,_fired ? new Color(.4f,.8f,1,.9f) : new Color(1,.65f,.2f,.5f));
        if (mortar && _time < delay) transform.position = Vector2.Lerp(_origin,_spec.Target,_time/delay)+Vector2.up*Mathf.Sin(_time/delay*Mathf.PI)*1.2f;
        else _sprite.enabled = false;
        if (!_fired && _time >= delay) { _fired = true; HitCircle(_spec.Target,_spec.Width); }
        if (_time >= delay + .25f) Release();
    }
    /// <summary>残影锁定原攻击位置和方向，延迟后结算一次扇面。</summary>
    private void Echo()
    {
        _sprite.enabled = false; DrawCone(_origin,_spec.Range,_spec.Width,new Color(.7f,.3f,1,_time < _spec.Delay ? .2f : .85f));
        if (!_fired && _time >= _spec.Delay) { _fired=true; HitCone(_origin,_spec.Range,_spec.Width); }
        if (_time >= _spec.Delay + .18f) Release();
    }
    /// <summary>每半秒处理一次墨带；重叠区域由最强有效墨带负责，敌人另有共同节拍去重。</summary>
    private void Ink()
    {
        DrawSegment(_origin,_origin+_direction*_spec.Range,_spec.Width,new Color(.18f,.1f,.32f,.75f));
        while (_ticks < 3 && _time + .0001f >= _nextTick)
        { _inkTickAt = _bornAt + _nextTick; _hits.Clear(); HitLine(_origin,_origin+_direction*_spec.Range,_spec.Width,int.MaxValue); _ticks++; _nextTick += .5f; }
        if (_time >= _spec.Duration + .03f) Release();
    }
    /// <summary>闪电每段重新找附近未命中的身份，按首段伤害衰减且不递归。</summary>
    private void Chain()
    {
        if (!_fired)
        {
            _fired=true; _sprite.enabled=false; _line.enabled=true; _line.loop=false; _line.positionCount=1; _line.SetPosition(0,_origin);
            _line.startWidth=_line.endWidth=.06f; _line.startColor=_line.endColor=new Color(.8f,.4f,1,1);
            Vector2 point=_origin;
            for (int i=0;i<4;i++)
            {
                Collider2D target=WeaponTargeting.FindNearest(point,i==0?_spec.Range:2f,_chainHits);
                if (target==null) break;
                Component identity=WeaponTargeting.Identity(target);_chainHits.Add(identity);
                point=target.transform.position;_line.positionCount=i+2;_line.SetPosition(i+1,point);
                _spec.Hit.Apply((IDamageable)identity,Mathf.Max(1,Mathf.Floor(_spec.Damage*Mathf.Pow(.8f,i))),_spec.Weapon);
            }
        }
        if (_time>.16f) Release();
    }
    /// <summary>共用局部候选缓存，候选密集时列表扩容而不静默漏掉目标。</summary>
    private void Gather(Vector2 origin,float radius)
    {
        var filter=new ContactFilter2D { useTriggers=true };filter.SetLayerMask(DamageTargetFilter.EnemyLayerMask);
        _candidates.Clear();Physics2D.OverlapCircle(origin,Mathf.Max(.05f,radius),filter,_candidates);
    }
    /// <summary>线段使用碰撞体最近边缘判定，有限穿透按沿线距离依次处理。</summary>
    private Collider2D HitLine(Vector2 from,Vector2 to,float width,int limit)
    {
        Vector2 delta=to-from;float length=delta.magnitude;Vector2 forward=length>.0001f?delta/length:_direction;
        Gather((from+to)*.5f,length*.5f+width);
        Collider2D first=null;
        // 只在一次查询范围内选最靠前候选；命中数量通常很少，无全场排序或临时数组。
        while (_hitCount<Mathf.Max(1,limit))
        {
            Collider2D best=null;float bestDistance=float.PositiveInfinity;
            for(int i=0;i<_candidates.Count;i++)
            {
                Collider2D c=_candidates[i];if(!Eligible(c))continue;
                float along=Mathf.Clamp(Vector2.Dot((Vector2)c.transform.position-from,forward),0,length);
                Vector2 onLine=from+forward*along;
                if(Vector2.Distance(c.ClosestPoint(onLine),onLine)>width*.5f)continue;
                if(along<bestDistance){best=c;bestDistance=along;}
            }
            if(best==null)break;
            Component identity=WeaponTargeting.Identity(best);_hits[identity]=Generation(identity);
            if(_spec.Kind==ExpansionEffectKind.Ink && !OwnsInkHit(best))continue;
            Apply(best);_hitCount++;if(first==null)first=best;
        }
        return first;
    }
    /// <summary>圆形效果按目标身份去重，多个受击碰撞体不会重复结算。</summary>
    private void HitCircle(Vector2 origin,float radius)
    { Gather(origin,radius);for(int i=0;i<_candidates.Count;i++)if(Eligible(_candidates[i]))Apply(_candidates[i]); }
    /// <summary>扇面只处理方向夹角内目标；零距离接触目标允许命中。</summary>
    private void HitCone(Vector2 origin,float radius,float arc)
    {
        Gather(origin,radius);float cosine=Mathf.Cos(arc*.5f*Mathf.Deg2Rad);
        for(int i=0;i<_candidates.Count;i++)
        {Collider2D c=_candidates[i];if(!Eligible(c))continue;Vector2 v=c.ClosestPoint(origin)-origin;if(v.sqrMagnitude<.001f||Vector2.Dot(v.normalized,_direction)>=cosine)Apply(c);}
    }
    /// <summary>目标必须仍活动，且本次效果未命中过其当前生命代次。</summary>
    private bool Eligible(Collider2D c)
    {
        Component identity=WeaponTargeting.Identity(c);
        return WeaponTargeting.IsValidCached(c,identity,0)&&(!_hits.TryGetValue(identity,out uint g)||g!=Generation(identity));
    }
    /// <summary>统一结算快照与可选减速；先记录身份，防止同步回调重复命中。</summary>
    private void Apply(Collider2D c)
    {
        Component target=WeaponTargeting.Identity(c);if(target==null)return;uint generation=Generation(target);_hits[target]=generation;
        CombatDamageResult result=_spec.Hit.Apply((IDamageable)target,_spec.Damage,_spec.Weapon);
        if(result.Accepted&&target is EnemyBase enemy&&enemy.CurrentHealth>0&&enemy.LifeGeneration==generation&&_spec.Slow>0)
            enemy.CombatStatus.ApplySlow(_spec.Source,_spec.Slow,_spec.SlowSeconds);
    }
    /// <summary>重叠墨带只保留最高伤害，平值以实例 ID 固定裁决，结果不依赖 Update 顺序。</summary>
    private bool OwnsInkHit(Collider2D target)
    {
        foreach(ExpansionEffect other in Active)
        {
            if(other==this||!other._configured||other._spec.Kind!=ExpansionEffectKind.Ink||other._spec.Source==null)continue;
            if(other._spec.Damage<_spec.Damage||(other._spec.Damage==_spec.Damage&&other.GetInstanceID()>GetInstanceID()))continue;
            Vector2 relative=(Vector2)target.transform.position-other._origin;
            float along=Mathf.Clamp(Vector2.Dot(relative,other._direction),0,other._spec.Range);
            Vector2 point=other._origin+other._direction*along;
            if(Vector2.Distance(target.ClosestPoint(point),point)<=other._spec.Width*.5f)return false;
        }
        return !(WeaponTargeting.Identity(target) is EnemyBase enemy)||enemy.CombatStatus.ConsumeInkTick(_inkTickAt);
    }
    /// <summary>非池化测试目标以零代次兼容。</summary>
    private static uint Generation(Component target) { return target is EnemyBase enemy?enemy.LifeGeneration:0; }
    /// <summary>表现线段使用世界坐标，避免素材缩放改变实际效果长度。</summary>
    private void DrawSegment(Vector2 start,Vector2 end,float width,Color color)
    { _line.enabled=true;_line.loop=false;_line.positionCount=2;_line.startWidth=_line.endWidth=width;_line.startColor=_line.endColor=color;_line.SetPosition(0,start);_line.SetPosition(1,end); }
    /// <summary>圆环只表达范围，不挂物理碰撞体。</summary>
    private void DrawCircle(Vector2 center,float radius,Color color)
    {
        _line.enabled=true;_line.loop=true;_line.positionCount=32;_line.startWidth=_line.endWidth=.045f;_line.startColor=_line.endColor=color;
        for(int i=0;i<32;i++){float a=i*Mathf.PI*2/32;_line.SetPosition(i,center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius);}
    }
    /// <summary>扇面轮廓随方向与范围绘制，不改变伤害判定。</summary>
    private void DrawCone(Vector2 origin,float range,float arc,Color color)
    {
        _sprite.enabled=false;_line.enabled=true;_line.loop=true;_line.positionCount=12;_line.startWidth=_line.endWidth=.07f;_line.startColor=_line.endColor=color;_line.SetPosition(0,origin);
        for(int i=1;i<12;i++)_line.SetPosition(i,origin+(Vector2)(Quaternion.Euler(0,0,-arc*.5f+arc*(i-1)/10)*_direction)*range);
    }
    /// <summary>结束时通过原始资产键回池，未绑定池的测试夹具只停用。</summary>
    public void Release() { if(PoolManager.Instance!=null&&_prefab!=null)PoolManager.Instance.Release(_prefab,gameObject);else gameObject.SetActive(false); }
    /// <summary>回池时清理所有来源与集合，不让新生命周期继承计时或伤害。</summary>
    private void OnDisable() { Active.Remove(this);_configured=false;_spec=default;_hits.Clear();_candidates.Clear();_chainHits.Clear();if(_line!=null)_line.enabled=false; }
}
