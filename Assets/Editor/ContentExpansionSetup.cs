using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>本轮审阅内容的可重复导入工具；只追加指定稳定 ID，保留既有内容与 GUID。</summary>
public static class ContentExpansionSetup
{
    public const string Root = "Assets/Data/ContentExpansion";
    public const string Art = "Assets/Art/ContentExpansion";
    public const string Prefabs = "Assets/Prefab/Weapon/ContentExpansion";
    [Serializable] public sealed class Definitions { public Item[] items; public Weapon[] weapons; }
    [Serializable] public sealed class Item
    {
        public string id, name, description;
        public int quality, maxCopies, price;
        public List<PlayerStatModifier> modifiers;
    }
    [Serializable] public sealed class Weapon
    {
        public string id, name, template, description;
        public int price;
        public List<WeaponLevelData> tiers;
    }

    /// <summary>导入图标、配置与独立池化攻击实体，接入商店、收藏和正式场景候选表。</summary>
    [MenuItem("RainsenVampSur/Content/Import Reviewed Items and Weapons")]
    public static void Build()
    {
        Definitions definitions = JsonUtility.FromJson<Definitions>(File.ReadAllText(Root + "/definitions.json"));
        if (definitions.items.Length != 20 || definitions.weapons.Length != 10) throw new InvalidOperationException("内容数量不完整。");
        Directory.CreateDirectory(Root + "/Items"); Directory.CreateDirectory(Root + "/Weapons");
        Directory.CreateDirectory(Root + "/Upgrades"); Directory.CreateDirectory(Prefabs);
        AssetDatabase.Refresh();
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/MainLevel.unity");
        var shop = AssetDatabase.LoadAssetAtPath<RunShopCatalogSO>("Assets/Data/Rounds/ShopCatalog.asset");
        var catalog = AssetDatabase.LoadAssetAtPath<GameContentCatalogSO>("Assets/Data/GameContentCatalog.asset");
        var catalogObject = new SerializedObject(catalog);
        LevelUpManager loadout = UnityEngine.Object.FindObjectOfType<LevelUpManager>();
        foreach (Item item in definitions.items)
        {
            AbilityDataSO data = Asset<AbilityDataSO>(Root + "/Items/" + item.id + ".asset");
            data.abilityID = item.id; data.abilityNameKey = "item." + item.id + ".name";
            data.descriptionKey = "item." + item.id + ".description";
            data.abilityDisplayName = item.name; data.displayDescription = item.description;
            data.quality = item.quality; data.stackPerCopy = true; data.maxCopies = item.maxCopies;
            data.presentationCategory = AbilityPresentationCategory.Item;
            data.icon = ImportIcon(Art + "/Items/" + item.id + ".png");
            data.levelConfigs = new List<AbilityLevelData> { new AbilityLevelData {
                upgradeDescription = item.description, statModifiers = item.modifiers } };
            data.mechanic = null;
            if (item.id == "emergency_candy" || item.id == "hourglass_pendant")
            {
                var mechanic = Asset<RoundItemMechanicSO>(Root + "/Items/" + item.id + "_mechanic.asset");
                mechanic.trigger = item.id == "emergency_candy" ? RoundItemTrigger.SurvivedLowHealthDamage : RoundItemTrigger.CombatTime;
                data.mechanic = mechanic; EditorUtility.SetDirty(mechanic);
            }
            EditorUtility.SetDirty(data);
            Register(shop, catalogObject, loadout, item.id, item.name, item.description, item.price, data.icon, data, null);
        }
        foreach (Weapon weapon in definitions.weapons)
        {
            var template = AssetDatabase.LoadAssetAtPath<WeaponDataSO>("Assets/Data/" + weapon.template + ".asset");
            var data = Asset<WeaponDataSO>(Root + "/Weapons/" + weapon.id + ".asset");
            data.weaponID = weapon.id; data.weaponNameKey = "weapon." + weapon.id + ".name";
            data.descriptionKey = "weapon." + weapon.id + ".description";
            data.weaponDisplayName = weapon.name; data.displayDescription = weapon.description;
            data.runtimeType = template.runtimeType;
            data.icon = ImportIcon(Art + "/Weapons/" + weapon.id + ".png");
            data.roundTierConfigs = weapon.tiers;
            data.levelConfigs = JsonUtility.FromJson<Weapon>(JsonUtility.ToJson(weapon)).tiers;
            data.projectilePrefab = BuildPrefab(weapon, template, data.icon);
            EditorUtility.SetDirty(data);
            AddReference(catalogObject.FindProperty("weapons"), data);
            Register(shop, catalogObject, loadout, weapon.id, weapon.name, weapon.description, weapon.price, data.icon, null, data);
        }
        catalogObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(shop); EditorUtility.SetDirty(catalog);
        EditorUtility.SetDirty(loadout); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        if (!shop.Validate(out string error)) throw new InvalidOperationException(error);
        AssetDatabase.SaveAssets();
        Debug.Log("Content expansion imported: 20 items, 10 weapons, four tiers each.");
    }

    /// <summary>配置实际透明 Sprite，关闭压缩与平滑过滤；原图留存，UI 和攻击实体共用此引用。</summary>
    private static Sprite ImportIcon(string path)
    {
        AssetDatabase.ImportAsset(path);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("缺少素材：" + path);
        importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
        importer.filterMode = FilterMode.Point; importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 2048; importer.spritePixelsPerUnit = 1024;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    /// <summary>独立复制原有攻击实体，不修改共享 Prefab；保留池组件和碰撞层。</summary>
    private static GameObject BuildPrefab(Weapon weapon, WeaponDataSO template, Sprite icon)
    {
        string path = Prefabs + "/" + weapon.id + ".prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(template.projectilePrefab));
        root.name = weapon.id;
        SpriteRenderer renderer = root.GetComponentInChildren<SpriteRenderer>(true);
        if (template.runtimeType == WeaponRuntimeType.Aura)
        {
            renderer.color = new Color(1, .4f, .1f, .25f);
            // 圆形范围贴图继续表达真实判定，中心香炉显示武器身份。
            var center = new GameObject("Censer", typeof(SpriteRenderer));
            center.transform.SetParent(root.transform, false); center.transform.localScale = Vector3.one * .45f;
            var visual = center.GetComponent<SpriteRenderer>(); visual.sprite = icon; visual.sortingOrder = renderer.sortingOrder + 1;
        }
        else if (weapon.id == "04_briar_bolt" || weapon.id == "05_rivet_spike")
        {
            // 弩和枪的图标是发射器，战斗中沿用小型弹体轮廓，避免整把枪成为飞行弹药。
            renderer.color = weapon.id == "04_briar_bolt" ? new Color(.5f, 1, .55f) : new Color(1, .75f, .35f);
        }
        else
        {
            renderer.sprite = icon; renderer.color = Color.white;
            if (template.runtimeType == WeaponRuntimeType.Lobbed)
            {
                // 抛物线实体用根渲染器计算离屏边界，必须保持其启用。
                // 缩放根节点显示新图，同时反向校正碰撞半径，保持原有世界判定尺寸。
                const float visualScale = .7f;
                root.transform.localScale = Vector3.one * visualScale;
                root.GetComponent<CircleCollider2D>().radius /= visualScale;
            }
            else if (template.runtimeType != WeaponRuntimeType.Melee)
            {
                root.transform.localScale = Vector3.one;
                // 只缩放图像子物体，不缩放物理根节点，避免更换高分辨率图后改变碰撞范围。
                if (renderer.transform == root.transform)
                {
                    var child = new GameObject("Visual", typeof(SpriteRenderer)); child.transform.SetParent(root.transform, false);
                    var visual = child.GetComponent<SpriteRenderer>(); EditorUtility.CopySerialized(renderer, visual);
                    renderer.enabled = false;
                    child.transform.localScale = Vector3.one * .4f;
                    child.transform.localRotation = Quaternion.Euler(0, 0, -45);
                }
            }
        }
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);
        return prefab;
    }

    /// <summary>登记统一升级包装，重复导入更新同一条记录，不产生重复抽取项。</summary>
    private static void Register(RunShopCatalogSO shop, SerializedObject catalog, LevelUpManager loadout,
        string id, string name, string description, int price, Sprite icon, AbilityDataSO item, WeaponDataSO weapon)
    {
        var upgrade = Asset<UpgradeDataSO>(Root + "/Upgrades/" + id + ".asset");
        upgrade.upgradeID = "upgrade." + id; upgrade.upgradeName = name; upgrade.description = description;
        upgrade.upgradeNameKey = upgrade.upgradeID + ".name"; upgrade.descriptionKey = upgrade.upgradeID + ".description";
        upgrade.icon = icon; upgrade.abilityToGrant = item; upgrade.weaponToGrant = weapon;
        EditorUtility.SetDirty(upgrade);
        RunShopProduct product = shop.products.Find(p => p != null && p.Id == upgrade.upgradeID);
        if (product == null) { product = new RunShopProduct(); shop.products.Add(product); }
        product.content = upgrade; product.basePrice = price;
        if (!loadout.allAvailableUpgrades.Contains(upgrade)) loadout.allAvailableUpgrades.Add(upgrade);
        AddReference(catalog.FindProperty("upgrades"), upgrade);
    }

    /// <summary>只追加未登记的资产引用，保留原目录顺序。</summary>
    private static void AddReference(SerializedProperty list, UnityEngine.Object value)
    {
        for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).objectReferenceValue == value) return;
        int index = list.arraySize++; list.GetArrayElementAtIndex(index).objectReferenceValue = value;
    }

    /// <summary>存在时复用 GUID，否则创建指定类型的数据资产。</summary>
    private static T Asset<T>(string path) where T : ScriptableObject
    {
        T data = AssetDatabase.LoadAssetAtPath<T>(path);
        if (data != null) return data;
        data = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(data, path); return data;
    }
}
