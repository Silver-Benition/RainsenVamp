using System.Collections.Generic;
using UnityEngine;

/// <summary>单个玩家的工程道具部署权威；每波固定位置、副本增减和专属攻速均按来源维护。</summary>
public sealed class EngineeringLoadout:MonoBehaviour
{
    /// <summary>一个道具来源的副本数及本波实例；仅本容器修改其状态。</summary>
    public sealed class Entry
    {
        internal StructureItemMechanicSO Config;
        internal int Count;
        internal readonly List<EngineeringStructure> Instances=new List<EngineeringStructure>(3);
    }
    private readonly List<Entry> _entries=new List<Entry>(5);
    private readonly List<Vector2> _positions=new List<Vector2>(8);
    private int _generation=-1;
    private Vector2 _center;
    private PlayerStats _stats;
    public float AttackSpeedBonus { get; private set; }
    public int ActiveCount { get { int n=0;foreach(Entry e in _entries)n+=e.Instances.Count;return n; } }

    /// <summary>缓存权威玩家属性。</summary>
    private void Awake(){_stats=GetComponent<PlayerStats>();}
    /// <summary>登记一个道具来源；首轮部署留到正式战斗阶段。</summary>
    public Entry Register(StructureItemMechanicSO config,int count)
    {var entry=new Entry{Config=config,Count=Mathf.Max(0,count)};_entries.Add(entry);RefreshSpeed();return entry;}
    /// <summary>副本变化只增减差额，保留既有建筑和冷却。</summary>
    public void SetCount(Entry entry,int count)
    {if(!_entries.Contains(entry))return;entry.Count=Mathf.Max(0,count);Trim(entry);RefreshSpeed();}
    /// <summary>移除指定来源；清理其在途效果后再释放实体。</summary>
    public void Remove(Entry entry)
    {if(!_entries.Remove(entry))return;entry.Count=0;Trim(entry);RefreshSpeed();}
    /// <summary>每个来源线性相加专属攻速，普通武器攻速完全不参与。</summary>
    private void RefreshSpeed()
    {AttackSpeedBonus=0;foreach(Entry e in _entries)AttackSpeedBonus+=e.Count*e.Config.structureAttackSpeed;}
    /// <summary>回合代次改变才重置部署；暂停/恢复不重新创建或刷新冷却。</summary>
    private void Update()
    {
        RoundController round=RoundController.Instance;
        if(round==null||round.Phase!=RoundPhase.Combat){ClearInstances();_generation=-1;return;}
        if(_generation!=round.Current.Generation)
        {ClearInstances();_generation=round.Current.Generation;_center=transform.position;}
        if(!RoundController.AllowsCombat||PoolManager.Instance==null)return;
        // 按固定道具类型顺序部署；喷塔使用内圈，其他建筑使用外圈，购买顺序不改变布局。
        for(int kind=1;kind<=4;kind++)foreach(Entry e in _entries)
        {
            if((int)e.Config.kind!=kind)continue;
            for(int i=e.Instances.Count-1;i>=0;i--)if(e.Instances[i]==null||!e.Instances[i].gameObject.activeInHierarchy)e.Instances.RemoveAt(i);
            while(e.Instances.Count<e.Count)
            {
                Vector2 point=FindPosition(e.Config.kind,e.Instances.Count);
                GameObject go=PoolManager.Instance.Spawn(e.Config.structurePrefab,point,Quaternion.identity);
                if(go==null)break;
                EngineeringStructure actor=go.GetComponent<EngineeringStructure>();
                actor.Configure(this,e.Config,_stats);e.Instances.Add(actor);
            }
        }
    }
    /// <summary>使用固定候选点并向场内夹紧；边界处跳过重叠候选，最多检查三十二个位置。</summary>
    private Vector2 FindPosition(StructureKind kind,int copy)
    {
        _positions.Clear();foreach(Entry e in _entries)foreach(EngineeringStructure s in e.Instances)if(s!=null)_positions.Add(s.transform.position);
        int offset=kind==StructureKind.Sprayer?0:kind==StructureKind.Sentry?4:kind==StructureKind.Mortar?9:13;
        Vector2 candidate=_center;
        for(int i=0;i<32;i++)
        {
            float angle=(offset+copy*3+i)*Mathf.PI/8;float radius=(kind==StructureKind.Sprayer?2.2f:3.4f)+(i/16)*.8f;
            candidate=RoundArena.Clamp(_center+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius,.6f);
            bool clear=Vector2.Distance(candidate,_center)>.8f;
            foreach(Vector2 used in _positions)if(Vector2.Distance(candidate,used)<1)clear=false;
            if(clear)return candidate;
        }
        return candidate;
    }
    /// <summary>达到副本上限后从末尾收回差额，不改变其他实例。</summary>
    private void Trim(Entry entry)
    {while(entry.Instances.Count>entry.Count){int last=entry.Instances.Count-1;if(entry.Instances[last]!=null)entry.Instances[last].Release();entry.Instances.RemoveAt(last);}}
    /// <summary>清空本波实体，保留道具来源供下一波重新部署。</summary>
    private void ClearInstances()
    {foreach(Entry e in _entries){foreach(EngineeringStructure actor in e.Instances)if(actor!=null)actor.Release();e.Instances.Clear();}}
    /// <summary>玩家禁用或退出场景时取消所有建筑和在途伤害。</summary>
    private void OnDisable(){ClearInstances();_generation=-1;}
}
