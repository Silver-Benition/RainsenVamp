using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 一次武器 CSV 导入的资产结果。
/// </summary>
internal sealed class WeaponImportResult
{
    public readonly List<WeaponDataSO> Weapons = new List<WeaponDataSO>();
    public readonly List<UpgradeDataSO> Upgrades = new List<UpgradeDataSO>();
    public readonly List<string> Errors = new List<string>();
}

/// <summary>
/// 把已经通过语法解析的 CSV 预览写入 ScriptableObject，并提供显式场景同步入口。
/// </summary>
public static class WeaponConfigAssetImporter
{
    internal const string DefaultCsvPath = "Assets/Data/WeaponBalance.csv";
    private const string MainScenePath = "Assets/Scenes/MainLevel.unity";

    /// <summary>
    /// 在不写入资产的前提下检查路径、Prefab 类型和基础数值约束。
    /// </summary>
    internal static List<string> Validate(List<WeaponCsvGroup> groups)
    {
        var errors = new List<string>();
        var weaponIds = new HashSet<string>();

        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            WeaponCsvGroup group = groups[groupIndex];
            WeaponCsvRow header = group.Header;
            if (!weaponIds.Add(header.WeaponId))
            {
                errors.Add($"武器 ID 重复：{header.WeaponId}");
            }

            if (header.AssetName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                errors.Add($"{header.WeaponId}：assetName 含无效文件名字符。");
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(header.PrefabPath);
            Sprite icon = AssetDatabase.LoadAssetAtPath<Sprite>(header.IconPath);
            if (prefab == null)
            {
                errors.Add($"{header.WeaponId}：找不到 Prefab：{header.PrefabPath}");
            }
            else
            {
                ValidatePrefabComponent(header, prefab, errors);
            }

            if (icon == null)
            {
                errors.Add($"{header.WeaponId}：找不到 Sprite：{header.IconPath}");
            }

            for (int rowIndex = 0; rowIndex < group.Rows.Count; rowIndex++)
            {
                ValidateLevel(group.Rows[rowIndex], errors);
            }
        }

        return errors;
    }

    /// <summary>
    /// 创建或更新武器与配套升级资产；任何预检错误都会阻止本次写入。
    /// </summary>
    internal static WeaponImportResult Import(List<WeaponCsvGroup> groups)
    {
        var result = new WeaponImportResult();
        result.Errors.AddRange(Validate(groups));
        if (result.Errors.Count > 0)
        {
            return result;
        }

        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            WeaponCsvGroup group = groups[groupIndex];
            WeaponCsvRow header = group.Header;
            string weaponPath = $"Assets/Data/{header.AssetName}.asset";
            string upgradePath = $"Assets/Data/{header.AssetName}_Upgrade.asset";

            WeaponDataSO weapon = AssetDatabase.LoadAssetAtPath<WeaponDataSO>(weaponPath);
            bool weaponCreated = weapon == null;
            if (weaponCreated)
            {
                weapon = ScriptableObject.CreateInstance<WeaponDataSO>();
            }
            else
            {
                Undo.RecordObject(weapon, "导入武器配置");
            }

            weapon.weaponID = header.WeaponId;
            weapon.weaponNameKey = $"weapon.{header.WeaponId}.name";
            weapon.descriptionKey = $"weapon.{header.WeaponId}.description";
            weapon.runtimeType = header.RuntimeType;
            weapon.projectilePrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(header.PrefabPath);
            weapon.levelConfigs = new List<WeaponLevelData>(group.Rows.Count);
            for (int rowIndex = 0; rowIndex < group.Rows.Count; rowIndex++)
            {
                weapon.levelConfigs.Add(group.Rows[rowIndex].Stats);
            }

            if (weaponCreated)
            {
                AssetDatabase.CreateAsset(weapon, weaponPath);
                Undo.RegisterCreatedObjectUndo(weapon, "创建武器配置");
            }
            EditorUtility.SetDirty(weapon);

            UpgradeDataSO upgrade = AssetDatabase.LoadAssetAtPath<UpgradeDataSO>(upgradePath);
            bool upgradeCreated = upgrade == null;
            if (upgradeCreated)
            {
                upgrade = ScriptableObject.CreateInstance<UpgradeDataSO>();
            }
            else
            {
                Undo.RecordObject(upgrade, "导入武器升级配置");
            }

            upgrade.upgradeName = header.DisplayName;
            upgrade.description = header.Description;
            upgrade.icon = AssetDatabase.LoadAssetAtPath<Sprite>(header.IconPath);
            upgrade.weaponToGrant = weapon;

            if (upgradeCreated)
            {
                AssetDatabase.CreateAsset(upgrade, upgradePath);
                Undo.RegisterCreatedObjectUndo(upgrade, "创建武器升级配置");
            }
            EditorUtility.SetDirty(upgrade);
            result.Weapons.Add(weapon);
            result.Upgrades.Add(upgrade);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Refresh 可能替换新建资产的托管包装对象；重新按路径加载，确保场景同步拿到稳定引用。
        result.Weapons.Clear();
        result.Upgrades.Clear();
        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            string assetName = groups[groupIndex].Header.AssetName;
            result.Weapons.Add(
                AssetDatabase.LoadAssetAtPath<WeaponDataSO>($"Assets/Data/{assetName}.asset"));
            result.Upgrades.Add(
                AssetDatabase.LoadAssetAtPath<UpgradeDataSO>($"Assets/Data/{assetName}_Upgrade.asset"));
        }
        return result;
    }

    /// <summary>
    /// 校验运行类型与 Prefab 根节点组件一致，保证对象池取出后可以完成初始化。
    /// </summary>
    private static void ValidatePrefabComponent(
        WeaponCsvRow header,
        GameObject prefab,
        List<string> errors)
    {
        bool valid;
        switch (header.RuntimeType)
        {
            case WeaponRuntimeType.Aura:
                valid = prefab.GetComponent<AuraDamageZone>() != null;
                break;
            case WeaponRuntimeType.Orbiting:
                valid = prefab.GetComponent<OrbitingProjectile>() != null;
                break;
            case WeaponRuntimeType.Lobbed:
                valid = prefab.GetComponent<LobbedProjectile>() != null;
                break;
            case WeaponRuntimeType.Melee:
                valid = prefab.GetComponent<MeleeSwingHitbox>() != null;
                break;
            default:
                valid = prefab.GetComponent<ProjectileBase>() != null;
                break;
        }

        if (!valid)
        {
            errors.Add(
                $"{header.WeaponId}：Prefab 根节点缺少 {header.RuntimeType} 对应运行组件。");
        }
    }

    /// <summary>
    /// 校验单级数值的安全下限和本类型关键参数。
    /// </summary>
    private static void ValidateLevel(WeaponCsvRow row, List<string> errors)
    {
        string prefix = $"{row.WeaponId} Lv.{row.Level}";
        if (row.Stats.damage < 0f
            || row.Stats.cooldown < 0.05f
            || row.Stats.projectileCount < 1)
        {
            errors.Add($"{prefix}：伤害、冷却或数量超出安全范围。");
        }

        switch (row.RuntimeType)
        {
            case WeaponRuntimeType.Aura:
                if (row.Stats.auraRadius <= 0f || row.Stats.tickInterval <= 0f)
                {
                    errors.Add($"{prefix}：光环半径和伤害间隔必须大于 0。");
                }
                break;
            case WeaponRuntimeType.Orbiting:
                if (row.Stats.orbitRadius <= 0f
                    || Mathf.Abs(row.Stats.orbitAngularSpeed) < 0.01f)
                {
                    errors.Add($"{prefix}：环绕半径和角速度必须有效。");
                }
                break;
            case WeaponRuntimeType.Lobbed:
                if (row.Stats.projectileSpeed <= 0f
                    || row.Stats.lifeTime <= 0f
                    || row.Stats.lobGravity <= 0f)
                {
                    errors.Add($"{prefix}：飞斧投掷力度、最大生命周期和下坠重力必须大于 0。");
                }
                break;
            case WeaponRuntimeType.Melee:
                if (row.Stats.meleeRange <= 0f || row.Stats.activeDuration <= 0f)
                {
                    errors.Add($"{prefix}：近战范围和判定时间必须大于 0。");
                }
                break;
        }
    }

    /// <summary>
    /// 用本次导入的武器升级替换当前场景中的武器候选，并保留未来的非武器升级。
    /// </summary>
    internal static bool SyncOpenSceneUpgradePool(List<UpgradeDataSO> upgrades)
    {
        LevelUpManager manager = Object.FindObjectOfType<LevelUpManager>(true);
        if (manager == null)
        {
            return false;
        }

        // 新建资产在同一编辑器帧内经历 Refresh 时，调用方持有的包装对象可能暂时失效。
        // 这里重新扫描 Data 目录，以稳定 GUID 解析全部武器升级卡。
        var resolvedUpgrades = new List<UpgradeDataSO>();
        var seenUpgradeGuids = new HashSet<string>();
        for (int index = 0; index < upgrades.Count; index++)
        {
            UpgradeDataSO upgrade = upgrades[index];
            string path = upgrade != null ? AssetDatabase.GetAssetPath(upgrade) : string.Empty;
            string guid = string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
            if (upgrade != null && upgrade.weaponToGrant != null && seenUpgradeGuids.Add(guid))
            {
                resolvedUpgrades.Add(upgrade);
            }
        }

        string[] upgradeGuids = AssetDatabase.FindAssets(
            "t:UpgradeDataSO",
            new[] { "Assets/Data" });
        for (int index = 0; index < upgradeGuids.Length; index++)
        {
            string guid = upgradeGuids[index];
            if (!seenUpgradeGuids.Add(guid))
            {
                continue;
            }

            string path = AssetDatabase.GUIDToAssetPath(guid);
            UpgradeDataSO upgrade = AssetDatabase.LoadAssetAtPath<UpgradeDataSO>(path);
            if (upgrade != null && upgrade.weaponToGrant != null)
            {
                resolvedUpgrades.Add(upgrade);
            }
        }
        resolvedUpgrades.Sort((left, right) => string.CompareOrdinal(
            AssetDatabase.GetAssetPath(left),
            AssetDatabase.GetAssetPath(right)));

        Undo.RecordObject(manager, "同步武器升级候选池");
        if (manager.allAvailableUpgrades == null)
        {
            manager.allAvailableUpgrades = new List<UpgradeDataSO>();
        }
        manager.allAvailableUpgrades.RemoveAll(
            item => item != null && item.weaponToGrant != null);

        for (int index = 0; index < resolvedUpgrades.Count; index++)
        {
            manager.allAvailableUpgrades.Add(resolvedUpgrades[index]);
        }

        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        return true;
    }

    /// <summary>
    /// 批处理入口：导入默认平衡表、打开主场景、同步候选池并保存。
    /// </summary>
    public static void ImportDefaultBalanceSheet()
    {
        var parseErrors = new List<string>();
        string csvText = File.ReadAllText(DefaultCsvPath);
        List<WeaponCsvGroup> groups = WeaponCsvParser.Parse(csvText, parseErrors);
        if (parseErrors.Count > 0)
        {
            LogErrors(parseErrors);
            return;
        }

        WeaponImportResult result = Import(groups);
        if (result.Errors.Count > 0)
        {
            LogErrors(result.Errors);
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        if (!SyncOpenSceneUpgradePool(result.Upgrades))
        {
            Debug.LogError("[WeaponConfig] MainLevel 中找不到 LevelUpManager。");
            return;
        }

        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[WeaponConfig] 已导入 {result.Weapons.Count} 种武器并同步主场景。");
    }

    /// <summary>
    /// 把一组导入错误逐条写入 Unity Console，便于批处理定位。
    /// </summary>
    private static void LogErrors(List<string> errors)
    {
        for (int index = 0; index < errors.Count; index++)
        {
            Debug.LogError($"[WeaponConfig] {errors[index]}");
        }
    }
}
