using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>第二批工程内容的可重复导入入口，只更新本批稳定 ID 和资源，不重建旧武器。</summary>
public static class EngineeringExpansionSetup
{
    public const string Root="Assets/Data/EngineeringExpansion",Art="Assets/Art/EngineeringExpansion",Prefabs="Assets/Prefab/EngineeringExpansion";
    [Serializable] public sealed class Definition { public Weapon[] weapons; public Item[] items; }
    [Serializable] public sealed class Weapon { public string id,name,description;public int kind,runtime,price;public bool retired;public List<WeaponLevelData> tiers; }
    [Serializable] public sealed class Item { public string id,name,description;public int kind,quality,price,maxCopies;public float damage,scaling,interval,range,speed;public List<PlayerStatModifier> modifiers; }

    /// <summary>先建立项目资产，再注册正式目录、商店、收藏与场景升级池；重复导入保留 GUID。</summary>
    [MenuItem("RainsenVampSur/Content/Import Engineering Expansion")]
    public static void Build()
    {
        Definition definitions=JsonUtility.FromJson<Definition>(File.ReadAllText(Root+"/definitions.json"));
        if(definitions.weapons.Length!=10||definitions.items.Length!=10)throw new InvalidOperationException("工程扩展必须恰好十件武器和十件道具。");
        foreach(string folder in new[]{Root+"/Weapons",Root+"/Items",Root+"/Upgrades",Prefabs})Directory.CreateDirectory(folder);
        AssetDatabase.Refresh();
        Material material=Asset<Material>(Root+"/Effect.mat",()=>new Material(Shader.Find("Sprites/Default")));
        GameObject effects=EffectPrefab(material);
        Sprite bolt=Icon("copper_bolt"),mine=Icon("clockwork_mine");
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/MainLevel.unity");
        var shop=AssetDatabase.LoadAssetAtPath<RunShopCatalogSO>("Assets/Data/Rounds/ShopCatalog.asset");
        var catalog=AssetDatabase.LoadAssetAtPath<GameContentCatalogSO>("Assets/Data/GameContentCatalog.asset");
        var catalogObject=new SerializedObject(catalog);
        LevelUpManager loadout=UnityEngine.Object.FindObjectOfType<LevelUpManager>();
        foreach(Weapon w in definitions.weapons)
        {
            WeaponDataSO data=Asset<WeaponDataSO>(Root+"/Weapons/"+w.id+".asset");
            data.weaponID=w.id;data.weaponDisplayName=w.name;data.weaponNameKey="weapon."+w.id+".name";data.descriptionKey="weapon."+w.id+".description";data.displayDescription=w.description;
            data.expansionKind=(ExpansionWeaponKind)w.kind;data.runtimeType=(WeaponRuntimeType)w.runtime;data.icon=Icon(w.id);data.visualAngleOffset=0;data.heldSize=.65f;data.retiredFromPool=w.retired;
            data.roundTierConfigs=w.tiers;data.levelConfigs=JsonUtility.FromJson<Weapon>(JsonUtility.ToJson(w)).tiers;
            data.expansionEffectPrefab=effects;data.effectSprite=w.kind==4?mine:w.kind==8?data.icon:bolt;
            data.meleePattern=w.kind==1?MeleeAttackPattern.Thrust:MeleeAttackPattern.Sweep;
            data.projectilePrefab=w.runtime==(int)WeaponRuntimeType.Melee?MeleePrefab(data):effects;
            EditorUtility.SetDirty(data);Reference(catalogObject.FindProperty("weapons"),data);
            Register(shop,catalogObject,loadout,w.id,w.name,w.description,w.price,data.icon,null,data);
        }
        foreach(Item item in definitions.items)
        {
            AbilityDataSO data=Asset<AbilityDataSO>(Root+"/Items/"+item.id+".asset");
            data.abilityID=item.id;data.abilityNameKey="item."+item.id+".name";data.descriptionKey="item."+item.id+".description";data.abilityDisplayName=item.name;data.displayDescription=item.description;
            data.quality=item.quality;data.stackPerCopy=true;data.maxCopies=item.maxCopies;data.presentationCategory=AbilityPresentationCategory.Item;data.icon=Icon(item.id);
            data.levelConfigs=new List<AbilityLevelData>{new AbilityLevelData{upgradeDescription=item.description,statModifiers=item.modifiers}};
            data.mechanic=null;
            if(item.kind>0||item.speed>0)
            {
                StructureItemMechanicSO mechanic=Asset<StructureItemMechanicSO>(Root+"/Items/"+item.id+"_mechanic.asset");
                mechanic.kind=(StructureKind)item.kind;mechanic.damage=item.damage;mechanic.engineeringScaling=item.scaling;mechanic.interval=item.interval;mechanic.range=item.range;
                mechanic.structureAttackSpeed=item.speed;mechanic.effectPrefab=effects;mechanic.projectileSprite=bolt;
                mechanic.structurePrefab=item.kind>0?StructurePrefab(item.id,data.icon):null;data.mechanic=mechanic;EditorUtility.SetDirty(mechanic);
            }
            EditorUtility.SetDirty(data);Register(shop,catalogObject,loadout,item.id,item.name,item.description,item.price,data.icon,data,null);
        }
        RoundStatUpgrade engineering=shop.stats.Find(s=>s.modifier.StatType==PlayerStatType.Engineering);
        if(engineering==null){engineering=new RoundStatUpgrade();shop.stats.Add(engineering);}
        engineering.id="engineering";engineering.nameKey="stat.Engineering";engineering.displayName="工程学";engineering.icon=Icon("workshop_blueprint");
        engineering.modifier=new PlayerStatModifier(PlayerStatType.Engineering,PlayerStatModifierMode.Flat,2);engineering.tierValues=new[]{2f,3f,4f,5f};
        catalogObject.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(catalog);EditorUtility.SetDirty(shop);EditorUtility.SetDirty(loadout);
        if(!shop.Validate(out string error))throw new InvalidOperationException(error);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        Debug.Log("Engineering expansion: 10 weapons / 40 tiers, 10 items / 4 structures registered.");
    }
    /// <summary>导入原始透明素材，保留 alpha 与点采样，不用压缩破坏小尺寸轮廓。</summary>
    private static Sprite Icon(string id)
    {
        string path=Art+"/"+id+".png";AssetDatabase.ImportAsset(path);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.spritePixelsPerUnit=256;
        importer.alphaIsTransparency=true;importer.filterMode=FilterMode.Point;importer.mipmapEnabled=false;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.maxTextureSize=256;
        importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
    /// <summary>建立无实体碰撞的池化效果；线条只负责表现，命中由局部物理查询裁决。</summary>
    private static GameObject EffectPrefab(Material material)
    {
        var go=new GameObject("EngineeringEffect",typeof(SpriteRenderer),typeof(LineRenderer),typeof(ExpansionEffect));
        var line=go.GetComponent<LineRenderer>();line.sharedMaterial=material;line.useWorldSpace=true;line.positionCount=0;line.enabled=false;
        line.sortingLayerName="Player";line.sortingOrder=8;go.GetComponent<SpriteRenderer>().sortingLayerName="Player";go.GetComponent<SpriteRenderer>().sortingOrder=8;
        GameObject prefab=PrefabUtility.SaveAsPrefabAsset(go,Prefabs+"/Effect.prefab");UnityEngine.Object.DestroyImmediate(go);return prefab;
    }
    /// <summary>复制已验证的近战判定几何，只替换本件主体素材和水平朝向。</summary>
    private static GameObject MeleePrefab(WeaponDataSO data)
    {
        var template=AssetDatabase.LoadAssetAtPath<WeaponDataSO>("Assets/Data/Umbrella.asset");
        GameObject root=PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(template.projectilePrefab));root.name=data.weaponID;
        var hitbox=root.GetComponent<MeleeSwingHitbox>();var serialized=new SerializedObject(hitbox);
        serialized.FindProperty("minimumInnerRadius").floatValue=.05f;serialized.FindProperty("visualScaleMultiplier").floatValue=1;serialized.FindProperty("visualAngleOffset").floatValue=0;serialized.ApplyModifiedPropertiesWithoutUndo();
        var visual=root.GetComponentInChildren<SpriteRenderer>(true);visual.sprite=data.icon;visual.color=Color.white;
        GameObject prefab=PrefabUtility.SaveAsPrefabAsset(root,Prefabs+"/"+data.weaponID+".prefab");PrefabUtility.UnloadPrefabContents(root);return prefab;
    }
    /// <summary>构筑物只创建可视主体，不阻挡玩家、敌人或弹体。</summary>
    private static GameObject StructurePrefab(string id,Sprite icon)
    {
        var go=new GameObject(id,typeof(SpriteRenderer),typeof(EngineeringStructure));var renderer=go.GetComponent<SpriteRenderer>();
        renderer.sprite=icon;renderer.sortingLayerName="Player";renderer.sortingOrder=0;
        go.transform.localScale=Vector3.one*(.9f/Mathf.Max(icon.bounds.size.x,icon.bounds.size.y));
        GameObject prefab=PrefabUtility.SaveAsPrefabAsset(go,Prefabs+"/"+id+".prefab");UnityEngine.Object.DestroyImmediate(go);return prefab;
    }
    /// <summary>同一稳定 ID 更新同一包装和商品，不重复增加抽取权重。</summary>
    private static void Register(RunShopCatalogSO shop,SerializedObject catalog,LevelUpManager loadout,string id,string name,string description,int price,Sprite icon,AbilityDataSO item,WeaponDataSO weapon)
    {
        UpgradeDataSO upgrade=Asset<UpgradeDataSO>(Root+"/Upgrades/"+id+".asset");upgrade.upgradeID="upgrade."+id;upgrade.upgradeName=name;upgrade.description=description;
        upgrade.upgradeNameKey=upgrade.upgradeID+".name";upgrade.descriptionKey=upgrade.upgradeID+".description";upgrade.icon=icon;upgrade.abilityToGrant=item;upgrade.weaponToGrant=weapon;EditorUtility.SetDirty(upgrade);
        RunShopProduct product=shop.products.Find(p=>p!=null&&p.Id==upgrade.upgradeID);if(product==null){product=new RunShopProduct();shop.products.Add(product);}product.content=upgrade;product.basePrice=price;
        if(!loadout.allAvailableUpgrades.Contains(upgrade))loadout.allAvailableUpgrades.Add(upgrade);Reference(catalog.FindProperty("upgrades"),upgrade);
    }
    /// <summary>保存目录引用时保持原序并拒绝重复。</summary>
    private static void Reference(SerializedProperty list,UnityEngine.Object value)
    {for(int i=0;i<list.arraySize;i++)if(list.GetArrayElementAtIndex(i).objectReferenceValue==value)return;list.GetArrayElementAtIndex(list.arraySize++).objectReferenceValue=value;}
    /// <summary>复用现有配置 GUID，首次导入才创建资产。</summary>
    private static T Asset<T>(string path) where T:ScriptableObject
    {return Asset(path,()=>ScriptableObject.CreateInstance<T>());}
    /// <summary>材质等非 ScriptableObject 资产使用显式工厂创建。</summary>
    private static T Asset<T>(string path,Func<T> create) where T:UnityEngine.Object
    {T value=AssetDatabase.LoadAssetAtPath<T>(path);if(value!=null)return value;value=create();AssetDatabase.CreateAsset(value,path);return value;}
}
