using UnityEngine;

/// <summary>工程道具机制配置；建筑与专属攻速分开配置，不占用普通武器属性。</summary>
[CreateAssetMenu(menuName="GameData/Ability Mechanics/Structure")]
public sealed class StructureItemMechanicSO : AbilityMechanicSO
{
    public StructureKind kind;
    public GameObject structurePrefab, effectPrefab;
    public Sprite projectileSprite;
    public float damage, engineeringScaling, interval=1, range=4.5f, structureAttackSpeed;

    /// <summary>为持有来源登记独立副本计数，生命周期由能力管理器负责释放。</summary>
    public override IAbilityMechanicRuntime CreateRuntime(AbilityRuntimeContext context,AbilityDataSO abilityData,int initialLevel)
    {
        if(context?.Owner==null||context.PlayerStats==null)return null;
        EngineeringLoadout loadout=context.Owner.GetComponent<EngineeringLoadout>();
        if(loadout==null)loadout=context.Owner.gameObject.AddComponent<EngineeringLoadout>();
        return new Runtime(loadout,loadout.Register(this,initialLevel));
    }
    /// <summary>桥接正式道具副本变化，不把持有数写回共享配置。</summary>
    private sealed class Runtime:IAbilityMechanicRuntime
    {
        private readonly EngineeringLoadout _loadout;private readonly EngineeringLoadout.Entry _entry;
        /// <summary>保存本来源的注册句柄。</summary>
        public Runtime(EngineeringLoadout loadout,EngineeringLoadout.Entry entry){_loadout=loadout;_entry=entry;}
        /// <summary>能力等级在逐件道具中代表当前持有副本数。</summary>
        public void SetLevel(int level){if(_loadout!=null)_loadout.SetCount(_entry,level);}
        /// <summary>移除本来源与其实体，重复释放安全无副作用。</summary>
        public void Dispose(){if(_loadout!=null)_loadout.Remove(_entry);}
    }
}

/// <summary>四种独立建筑，零值用于没有实体的专属增益道具。</summary>
public enum StructureKind { None, Sentry, Sprayer, Mortar, Nexus }
