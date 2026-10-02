using UnityEngine;

/// <summary>固定在地面的自主建筑；不挂阻挡碰撞、不占武器槽，仅按工程学快照发射。</summary>
[RequireComponent(typeof(SpriteRenderer))]
public sealed class EngineeringStructure:MonoBehaviour,IPoolable
{
    private GameObject _prefab;
    private EngineeringLoadout _owner;
    private StructureItemMechanicSO _config;
    private PlayerStats _stats;
    private SpriteRenderer _sprite;
    private float _remaining,_interval,_nextSearch;
    private int _generation;
    public float RemainingCooldown=>_remaining;
    public StructureKind Kind=>_config!=null?_config.kind:StructureKind.None;
    /// <summary>缓存建筑主体的显示组件。</summary>
    private void Awake(){_sprite=GetComponent<SpriteRenderer>();}
    /// <summary>保存原始项目资产作为回池键。</summary>
    public void SetPrefabReference(GameObject prefab){_prefab=prefab;}
    /// <summary>新波次从完整冷却开始；不继承上一副本的玩家与计时。</summary>
    public void Configure(EngineeringLoadout owner,StructureItemMechanicSO config,PlayerStats stats)
    {
        _owner=owner;_config=config;_stats=stats;_generation=RoundController.Instance?.Current?.Generation??-1;
        _interval=config.interval/(1+owner.AttackSpeedBonus*.01f);_remaining=_interval;_nextSearch=Time.time+((int)config.kind%4)*.02f;
        _sprite.flipX=false;
    }
    /// <summary>就绪时至多每 .08 秒局部索敌；专属攻速变化保留冷却完成比例。</summary>
    private void Update()
    {
        if(_config==null)return;
        if(_owner==null||!_owner.gameObject.activeInHierarchy||RoundController.Instance==null||RoundController.Instance.Phase!=RoundPhase.Combat||RoundController.Instance.Current.Generation!=_generation)
        {Release();return;}
        if(!RoundController.AllowsCombat)return;
        float interval=Mathf.Max(.05f,_config.interval/(1+_owner.AttackSpeedBonus*.01f));
        if(!Mathf.Approximately(interval,_interval)){_remaining*=interval/_interval;_interval=interval;}
        _remaining=Mathf.Max(0,_remaining-Time.deltaTime);
        if(_remaining>0||Time.time<_nextSearch)return;
        _nextSearch=Time.time+.08f;
        Collider2D target=WeaponTargeting.FindNearest(transform.position,_config.range);
        if(target==null)return;
        Vector2 direction=((Vector2)target.transform.position-(Vector2)transform.position).normalized;
        _sprite.flipX=direction.x<0;
        EffectSpec spec=new EffectSpec{Source=transform,Owner=_owner.transform,Damage=BrotatoStatRules.EngineeringPower(_config.damage,_config.engineeringScaling,_stats.GetFinalStat(PlayerStatType.Engineering)),
            Direction=direction,Target=target.transform.position,Range=_config.range,Speed=10,Width=.14f,VisualLength=.18f,Sprite=_config.projectileSprite,MaxHits=1};
        switch(_config.kind)
        {
            case StructureKind.Sentry:spec.Kind=ExpansionEffectKind.Bolt;break;
            case StructureKind.Sprayer:spec.Kind=ExpansionEffectKind.Echo;spec.Width=60;spec.Delay=0;spec.Slow=.3f;spec.SlowSeconds=1.5f;break;
            case StructureKind.Mortar:spec.Kind=ExpansionEffectKind.Mortar;spec.Width=1.1f;break;
            case StructureKind.Nexus:spec.Kind=ExpansionEffectKind.Chain;break;
        }
        GameObject effect=PoolManager.Instance.Spawn(_config.effectPrefab,transform.position,Quaternion.identity);
        if(effect!=null)effect.GetComponent<ExpansionEffect>().Configure(spec);
        _remaining=_interval;
    }
    /// <summary>主动回收不改变道具持有数量。</summary>
    public void Release(){if(PoolManager.Instance!=null&&_prefab!=null)PoolManager.Instance.Release(_prefab,gameObject);else gameObject.SetActive(false);}
    /// <summary>来源先清理派生效果，再解除玩家引用，避免旧炮弹穿越波次。</summary>
    private void OnDisable(){ExpansionEffect.ReleaseSource(transform);_owner=null;_config=null;_stats=null;}
}
