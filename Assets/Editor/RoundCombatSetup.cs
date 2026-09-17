using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>Session 23 定向作者工具：生成回合配置与有限主世界，保存可人工检查的场景接线。</summary>
public static class RoundCombatSetup
{
    public const string Root = "Assets/Data/Rounds";
    public const string ConfigPath = Root + "/Standard20.asset";

    /// <summary>在独立 QA 分支构建正式二十回合配置；重复执行复用资产 GUID。</summary>
    [MenuItem("RainsenVampSur/Rounds/Build Session 23")]
    public static void Build()
    {
        EnsureFolder(Root);
        EnsureFolder(Root + "/Waves");
        EnsureFolder(Root + "/Tiles");
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/MainLevel.unity");
        LevelUpManager loadout = UnityEngine.Object.FindObjectOfType<LevelUpManager>();
        if (loadout == null) throw new InvalidOperationException("MainLevel 缺少升级与装备管理器。");
        RunShopCatalogSO shop = Asset<RunShopCatalogSO>(Root + "/ShopCatalog.asset");
        shop.products.Clear();
        foreach (UpgradeDataSO upgrade in loadout.allAvailableUpgrades)
        {
            if (upgrade == null || !upgrade.HasExactlyOneReward()) continue;
            shop.products.Add(new RunShopProduct { content = upgrade, basePrice = upgrade.weaponToGrant != null ? 12 : 18 });
            if (upgrade.weaponToGrant == null) continue;
            WeaponDataSO weapon = upgrade.weaponToGrant;
            if (weapon.roundTierConfigs.Count != 4)
            {
                weapon.roundTierConfigs.Clear();
                // 四档覆盖旧表起点和终点，中间档采用等距取样，保留既有武器特征。
                for (int tier = 0; tier < 4; tier++)
                {
                    int oldLevel = 1 + Mathf.RoundToInt(tier * (weapon.MaxLevel - 1) / 3f);
                    weapon.roundTierConfigs.Add(JsonUtility.FromJson<WeaponLevelData>(JsonUtility.ToJson(weapon.GetLevelConfig(oldLevel))));
                }
                EditorUtility.SetDirty(weapon);
            }
        }
        shop.stats.Clear();
        AddStat(shop, PlayerStatType.MaxHealth, PlayerStatModifierMode.Flat, 3);
        AddStat(shop, PlayerStatType.Recovery, PlayerStatModifierMode.Flat, .1f);
        AddStat(shop, PlayerStatType.Armor, PlayerStatModifierMode.Flat, 1);
        AddStat(shop, PlayerStatType.MoveSpeed, PlayerStatModifierMode.AdditivePercent, .03f);
        AddStat(shop, PlayerStatType.Might, PlayerStatModifierMode.Flat, .05f);
        AddStat(shop, PlayerStatType.Cooldown, PlayerStatModifierMode.Flat, -.03f);
        AddStat(shop, PlayerStatType.Area, PlayerStatModifierMode.Flat, .05f);
        AddStat(shop, PlayerStatType.Luck, PlayerStatModifierMode.Flat, .05f);
        AddStat(shop, PlayerStatType.Growth, PlayerStatModifierMode.Flat, .05f);
        AddStat(shop, PlayerStatType.Magnet, PlayerStatModifierMode.AdditivePercent, .05f);
        EditorUtility.SetDirty(shop);
        RoundRunConfigSO config = Asset<RoundRunConfigSO>(ConfigPath);
        config.shopCatalog = shop;
        config.rounds.Clear();
        WaveConfigSO original = AssetDatabase.LoadAssetAtPath<WaveConfigSO>("Assets/Data/Map/GrassWaveConfig.asset");
        for (int wave = 1; wave <= 20; wave++)
        {
            WaveConfigSO spawn = Asset<WaveConfigSO>(Root + "/Waves/Wave" + wave.ToString("00") + ".asset");
            spawn.duration = 0;
            spawn.rules.Clear();
            for (int i = 0; i < original.rules.Count; i++)
            {
                if (i > 0 && wave < 4) continue;
                WaveConfigSO.SpawnRule rule = JsonUtility.FromJson<WaveConfigSO.SpawnRule>(JsonUtility.ToJson(original.rules[i]));
                rule.startTime = i == 0 ? 0 : 4;
                rule.endTime = 0;
                rule.spawnsPerSecond = i == 0 ? 1.5f + wave * .35f : .15f + wave * .035f;
                rule.maxAlive = i == 0 ? 30 + wave * 5 : 4 + wave;
                spawn.rules.Add(rule);
            }
            EditorUtility.SetDirty(spawn);
            config.rounds.Add(new RoundDefinition { survivalSeconds = wave == 20 ? 90 : Mathf.Min(60, 15 + wave * 5),
                spawnConfig = spawn, enemyHealthMultiplier = 1 + (wave - 1) * .15f, enemyDamageMultiplier = 1 + (wave - 1) * .05f, spawnBoss = wave == 20, allowEarlyBossVictory = wave == 20 });
        }
        EditorUtility.SetDirty(config);
        RunDirector director = UnityEngine.Object.FindObjectOfType<RunDirector>();
        RoundController rounds = director.GetComponent<RoundController>();
        if (rounds == null) rounds = director.gameObject.AddComponent<RoundController>();
        rounds.config = config;
        var directorObject = new SerializedObject(director);
        directorObject.FindProperty("mapNameKey").stringValue = "map.round_arena";
        directorObject.FindProperty("mapDisplayName").stringValue = "主世界竞技场";
        directorObject.ApplyModifiedPropertiesWithoutUndo();
        foreach (MapStreamManager stream in UnityEngine.Object.FindObjectsOfType<MapStreamManager>(true)) stream.enabled = false;
        BuildArena(config);
        Canvas canvas = loadout.levelUpPanel.GetComponentInParent<Canvas>().rootCanvas;
        RoundIntermissionUI ui = canvas.GetComponent<RoundIntermissionUI>();
        if (ui == null) ui = canvas.gameObject.AddComponent<RoundIntermissionUI>();
        ui.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/msyh SDF.asset");
        GameTimerUI timer = UnityEngine.Object.FindObjectOfType<GameTimerUI>();
        if (timer != null)
        {
            SerializedObject timerObject = new SerializedObject(timer);
            TMP_Text label = timerObject.FindProperty("timerText").objectReferenceValue as TMP_Text;
            if (label != null) label.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 520);
        }
        if (!config.Validate(out string error)) throw new InvalidOperationException(error);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Session 23: 20 回合竞技场与局间商店已保存。");
    }

    /// <summary>建立具有稳定 ID 的基础属性候选，图标复用已有属性商店素材。</summary>
    private static void AddStat(RunShopCatalogSO shop, PlayerStatType type, PlayerStatModifierMode mode, float value)
    {
        Sprite icon = null;
        foreach (string guid in AssetDatabase.FindAssets("t:AccountUpgradeDataSO", new[] { "Assets/Data/AccountUpgrades" }))
        {
            var data = AssetDatabase.LoadAssetAtPath<AccountUpgradeDataSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (data != null && data.statType == type) { icon = data.icon; break; }
        }
        shop.stats.Add(new RoundStatUpgrade { id = "round.stat." + type, nameKey = "round.stat." + type,
            displayName = PlayerStatPresentation.GetDisplayName(type), icon = icon, modifier = new PlayerStatModifier(type, mode, value) });
    }

    /// <summary>生成有限地面与可见围墙；原世界流式组件保留引用但不再运行。</summary>
    private static void BuildArena(RoundRunConfigSO config)
    {
        GameObject previous = GameObject.Find("RoundArena");
        if (previous != null) UnityEngine.Object.DestroyImmediate(previous);
        var root = new GameObject("RoundArena");
        root.AddComponent<RoundArena>().size = config.arenaSize;
        var grid = new GameObject("Ground", typeof(Grid));
        grid.transform.SetParent(root.transform, false);
        var ground = new GameObject("Tiles", typeof(Tilemap), typeof(TilemapRenderer));
        ground.transform.SetParent(grid.transform, false);
        Tilemap tilemap = ground.GetComponent<Tilemap>();
        ground.GetComponent<TilemapRenderer>().sortingOrder = -100;
        MapThemeDataSO theme = AssetDatabase.LoadAssetAtPath<MapThemeDataSO>("Assets/Data/Map/GrassMapTheme.asset");
        SerializedObject themeObject = new SerializedObject(theme);
        SerializedProperty sprites = themeObject.FindProperty("groundSprites");
        var tiles = new List<Tile>();
        for (int i = 0; i < sprites.arraySize; i++)
        {
            Tile tile = Asset<Tile>(Root + "/Tiles/Grass" + i + ".asset");
            tile.sprite = sprites.GetArrayElementAtIndex(i).objectReferenceValue as Sprite;
            tile.colliderType = Tile.ColliderType.None;
            EditorUtility.SetDirty(tile);
            tiles.Add(tile);
        }
        int halfX = Mathf.RoundToInt(config.arenaSize.x * .5f), halfY = Mathf.RoundToInt(config.arenaSize.y * .5f);
        for (int y = -halfY; y < halfY; y++)
            for (int x = -halfX; x < halfX; x++)
                tilemap.SetTile(new Vector3Int(x, y, 0), tiles[Mathf.Abs(x * 17 + y * 7) % tiles.Count]);
        WorldLineDataSO world = AssetDatabase.LoadAssetAtPath<WorldLineDataSO>("Assets/Data/Map/GrassWorldLine.asset");
        Sprite wallSprite = new SerializedObject(world).FindProperty("coverSprite").objectReferenceValue as Sprite;
        for (int i = 0; i < 4; i++)
        {
            var wall = new GameObject("Boundary" + i, typeof(BoxCollider2D), typeof(SpriteRenderer));
            wall.transform.SetParent(root.transform, false);
            bool horizontal = i < 2;
            wall.transform.localPosition = horizontal ? new Vector3(0, i == 0 ? -halfY - .4f : halfY + .4f, 0)
                : new Vector3(i == 2 ? -halfX - .4f : halfX + .4f, 0, 0);
            Vector2 size = horizontal ? new Vector2(halfX * 2 + 1.6f, .8f) : new Vector2(.8f, halfY * 2);
            wall.GetComponent<BoxCollider2D>().size = size;
            var renderer = wall.GetComponent<SpriteRenderer>();
            renderer.sprite = wallSprite; renderer.drawMode = SpriteDrawMode.Tiled; renderer.size = size;
            renderer.color = new Color(.65f, .82f, .7f); renderer.sortingOrder = 1;
        }
    }

    /// <summary>加载或创建指定类型资产；既有资产保持 GUID。</summary>
    private static T Asset<T>(string path) where T : ScriptableObject
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null) return asset;
        asset = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(asset, path); return asset;
    }

    /// <summary>逐级创建必要数据目录。</summary>
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent); AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
