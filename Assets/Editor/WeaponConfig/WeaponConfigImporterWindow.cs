using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 武器配置工作台：提供 CSV 预览导入、场景同步和类型化单资产编辑。
/// </summary>
public sealed class WeaponConfigImporterWindow : EditorWindow
{
    private TextAsset _csvAsset;
    private string _externalCsvPath;
    private WeaponDataSO _selectedWeapon;
    private UpgradeDataSO _selectedUpgrade;
    private SerializedObject _weaponSerializedObject;
    private SerializedObject _upgradeSerializedObject;
    private readonly List<WeaponCsvGroup> _previewGroups = new List<WeaponCsvGroup>();
    private readonly List<string> _messages = new List<string>();
    private WeaponImportResult _lastImportResult;
    private Vector2 _scrollPosition;

    /// <summary>
    /// 从 Unity 菜单打开工作台。
    /// </summary>
    [MenuItem("Tools/RainsenVamp/Weapon Config Studio")]
    public static void OpenWindow()
    {
        var window = GetWindow<WeaponConfigImporterWindow>("Weapon Config Studio");
        window.minSize = new Vector2(720f, 520f);
        window.Show();
    }

    /// <summary>
    /// Project 选择变化时自动接管武器或升级资产，减少手动拖拽。
    /// </summary>
    private void OnSelectionChange()
    {
        if (Selection.activeObject is WeaponDataSO weapon)
        {
            SetSelectedWeapon(weapon);
            Repaint();
        }
        else if (Selection.activeObject is UpgradeDataSO upgrade)
        {
            _selectedUpgrade = upgrade;
            _upgradeSerializedObject = new SerializedObject(upgrade);
            Repaint();
        }
    }

    /// <summary>
    /// 绘制可滚动工作台；所有写入都必须由明确按钮触发。
    /// </summary>
    private void OnGUI()
    {
        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
        DrawCsvSection();
        EditorGUILayout.Space(12f);
        DrawAssetEditor();
        EditorGUILayout.EndScrollView();
    }

    /// <summary>
    /// 绘制 CSV 来源、预览、导入和场景同步区域。
    /// </summary>
    private void DrawCsvSection()
    {
        EditorGUILayout.LabelField("批量 CSV 导入", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "先点“预览并校验”，确认无错误后再导入。导入只更新 Assets/Data 下同名武器与升级资产；场景同步需要单独点击。",
            MessageType.Info);

        _csvAsset = (TextAsset)EditorGUILayout.ObjectField(
            "Assets 内 CSV",
            _csvAsset,
            typeof(TextAsset),
            false);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("外部 CSV");
        EditorGUILayout.SelectableLabel(
            string.IsNullOrEmpty(_externalCsvPath) ? "未选择" : _externalCsvPath,
            EditorStyles.textField,
            GUILayout.Height(EditorGUIUtility.singleLineHeight));
        if (GUILayout.Button("选择", GUILayout.Width(72f)))
        {
            _externalCsvPath = EditorUtility.OpenFilePanel("选择武器 CSV", string.Empty, "csv");
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("载入默认平衡表"))
        {
            _csvAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(
                WeaponConfigAssetImporter.DefaultCsvPath);
            _externalCsvPath = string.Empty;
        }
        if (GUILayout.Button("预览并校验"))
        {
            BuildPreview();
        }
        using (new EditorGUI.DisabledScope(_previewGroups.Count == 0 || HasErrors()))
        {
            if (GUILayout.Button("导入 / 更新资产"))
            {
                ImportPreview();
            }
        }
        using (new EditorGUI.DisabledScope(_lastImportResult == null))
        {
            if (GUILayout.Button("同步当前场景升级池"))
            {
                SyncCurrentScene();
            }
        }
        EditorGUILayout.EndHorizontal();

        if (_previewGroups.Count > 0)
        {
            int levelCount = 0;
            for (int index = 0; index < _previewGroups.Count; index++)
            {
                levelCount += _previewGroups[index].Rows.Count;
            }
            EditorGUILayout.LabelField(
                $"预览：{_previewGroups.Count} 种武器，{levelCount} 行等级配置");
        }

        for (int index = 0; index < _messages.Count; index++)
        {
            MessageType type = _messages[index].StartsWith("错误：")
                ? MessageType.Error
                : MessageType.Info;
            EditorGUILayout.HelpBox(_messages[index], type);
        }
    }

    /// <summary>
    /// 读取当前 CSV 来源并构建完全只读的内存预览。
    /// </summary>
    private void BuildPreview()
    {
        _previewGroups.Clear();
        _messages.Clear();
        _lastImportResult = null;

        string csvText = ReadCsvText();
        if (string.IsNullOrEmpty(csvText))
        {
            _messages.Add("错误：请选择 CSV 资产或外部文件。");
            return;
        }

        var parseErrors = new List<string>();
        _previewGroups.AddRange(WeaponCsvParser.Parse(csvText, parseErrors));
        for (int index = 0; index < parseErrors.Count; index++)
        {
            _messages.Add($"错误：{parseErrors[index]}");
        }

        if (parseErrors.Count == 0)
        {
            List<string> validationErrors = WeaponConfigAssetImporter.Validate(_previewGroups);
            for (int index = 0; index < validationErrors.Count; index++)
            {
                _messages.Add($"错误：{validationErrors[index]}");
            }
        }

        if (_messages.Count == 0)
        {
            _messages.Add("校验通过：可以导入。");
        }
    }

    /// <summary>
    /// 在预览无错误时执行资产导入，并选中第一把生成的武器。
    /// </summary>
    private void ImportPreview()
    {
        _lastImportResult = WeaponConfigAssetImporter.Import(_previewGroups);
        _messages.Clear();
        for (int index = 0; index < _lastImportResult.Errors.Count; index++)
        {
            _messages.Add($"错误：{_lastImportResult.Errors[index]}");
        }

        if (_lastImportResult.Errors.Count == 0)
        {
            _messages.Add($"已导入 {_lastImportResult.Weapons.Count} 种武器。");
            if (_lastImportResult.Weapons.Count > 0)
            {
                SetSelectedWeapon(_lastImportResult.Weapons[0]);
                Selection.activeObject = _selectedWeapon;
            }
        }
    }

    /// <summary>
    /// 把最近一次成功导入的升级资产写入当前打开场景，并保留非武器候选。
    /// </summary>
    private void SyncCurrentScene()
    {
        if (_lastImportResult != null
            && WeaponConfigAssetImporter.SyncOpenSceneUpgradePool(_lastImportResult.Upgrades))
        {
            _messages.Add("已同步当前场景；场景已标记为未保存。");
        }
        else
        {
            _messages.Add("错误：当前场景中找不到 LevelUpManager。");
        }
    }

    /// <summary>
    /// 优先读取外部路径，否则读取 Project 内 TextAsset。
    /// </summary>
    private string ReadCsvText()
    {
        if (!string.IsNullOrEmpty(_externalCsvPath) && File.Exists(_externalCsvPath))
        {
            return File.ReadAllText(_externalCsvPath);
        }
        return _csvAsset != null ? _csvAsset.text : string.Empty;
    }

    /// <summary>
    /// 判断当前消息集合是否包含阻止导入的错误。
    /// </summary>
    private bool HasErrors()
    {
        for (int index = 0; index < _messages.Count; index++)
        {
            if (_messages[index].StartsWith("错误："))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 绘制单武器直接编辑区和配套升级卡 UI 字段。
    /// </summary>
    private void DrawAssetEditor()
    {
        EditorGUILayout.LabelField("单武器快速编辑", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        WeaponDataSO nextWeapon = (WeaponDataSO)EditorGUILayout.ObjectField(
            "武器资产",
            _selectedWeapon,
            typeof(WeaponDataSO),
            false);
        if (nextWeapon != _selectedWeapon)
        {
            SetSelectedWeapon(nextWeapon);
        }
        if (GUILayout.Button("新建武器与升级卡", GUILayout.Width(150f)))
        {
            CreateWeaponPair();
        }
        EditorGUILayout.EndHorizontal();

        if (_weaponSerializedObject == null || _selectedWeapon == null)
        {
            EditorGUILayout.HelpBox("选择一个 WeaponDataSO 后可进行类型化编辑。", MessageType.None);
            return;
        }

        _weaponSerializedObject.Update();
        EditorGUILayout.PropertyField(_weaponSerializedObject.FindProperty("weaponID"));
        EditorGUILayout.PropertyField(_weaponSerializedObject.FindProperty("weaponNameKey"));
        EditorGUILayout.PropertyField(_weaponSerializedObject.FindProperty("descriptionKey"));
        SerializedProperty runtimeTypeProperty =
            _weaponSerializedObject.FindProperty("runtimeType");
        EditorGUILayout.PropertyField(runtimeTypeProperty);
        EditorGUILayout.PropertyField(_weaponSerializedObject.FindProperty("projectilePrefab"));

        DrawLevelList(
            _weaponSerializedObject.FindProperty("levelConfigs"),
            (WeaponRuntimeType)runtimeTypeProperty.enumValueIndex);
        _weaponSerializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(6f);
        UpgradeDataSO nextUpgrade = (UpgradeDataSO)EditorGUILayout.ObjectField(
            "配套升级卡",
            _selectedUpgrade,
            typeof(UpgradeDataSO),
            false);
        if (nextUpgrade != _selectedUpgrade)
        {
            _selectedUpgrade = nextUpgrade;
            _upgradeSerializedObject = nextUpgrade != null
                ? new SerializedObject(nextUpgrade)
                : null;
        }

        if (_upgradeSerializedObject != null)
        {
            _upgradeSerializedObject.Update();
            EditorGUILayout.PropertyField(_upgradeSerializedObject.FindProperty("upgradeName"));
            EditorGUILayout.PropertyField(_upgradeSerializedObject.FindProperty("description"));
            EditorGUILayout.PropertyField(_upgradeSerializedObject.FindProperty("icon"));
            EditorGUILayout.PropertyField(_upgradeSerializedObject.FindProperty("customLevelDescs"), true);
            EditorGUILayout.PropertyField(_upgradeSerializedObject.FindProperty("weaponToGrant"));
            _upgradeSerializedObject.ApplyModifiedProperties();
        }
    }

    /// <summary>
    /// 为当前武器类型绘制每级完整快照，并提供复制与删除操作。
    /// </summary>
    private void DrawLevelList(SerializedProperty levels, WeaponRuntimeType runtimeType)
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField($"等级配置（{levels.arraySize} 级）", EditorStyles.boldLabel);
        int deleteIndex = -1;

        for (int index = 0; index < levels.arraySize; index++)
        {
            SerializedProperty level = levels.GetArrayElementAtIndex(index);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Lv.{index + 1}", EditorStyles.boldLabel);
            if (GUILayout.Button("删除", GUILayout.Width(60f)))
            {
                deleteIndex = index;
            }
            EditorGUILayout.EndHorizontal();
            DrawLevelFields(level, runtimeType);
            EditorGUILayout.EndVertical();
        }

        if (deleteIndex >= 0 && levels.arraySize > 1)
        {
            levels.DeleteArrayElementAtIndex(deleteIndex);
        }

        if (GUILayout.Button("新增等级（复制上一等级）"))
        {
            if (levels.arraySize == 0)
            {
                levels.arraySize = 1;
            }
            else
            {
                levels.InsertArrayElementAtIndex(levels.arraySize - 1);
            }
        }
    }

    /// <summary>
    /// 根据运行类型只绘制真正参与该武器行为的字段。
    /// </summary>
    private void DrawLevelFields(SerializedProperty level, WeaponRuntimeType runtimeType)
    {
        EditorGUILayout.PropertyField(level.FindPropertyRelative("damage"));
        EditorGUILayout.PropertyField(level.FindPropertyRelative("cooldown"));

        switch (runtimeType)
        {
            case WeaponRuntimeType.Aura:
                EditorGUILayout.PropertyField(level.FindPropertyRelative("auraRadius"));
                EditorGUILayout.PropertyField(level.FindPropertyRelative("tickInterval"));
                EditorGUILayout.PropertyField(level.FindPropertyRelative("lifeTime"));
                break;
            case WeaponRuntimeType.Orbiting:
                EditorGUILayout.PropertyField(level.FindPropertyRelative("projectileCount"));
                EditorGUILayout.PropertyField(level.FindPropertyRelative("orbitRadius"));
                EditorGUILayout.PropertyField(level.FindPropertyRelative("orbitAngularSpeed"));
                break;
            case WeaponRuntimeType.Lobbed:
                EditorGUILayout.PropertyField(level.FindPropertyRelative("projectileCount"));
                EditorGUILayout.PropertyField(
                    level.FindPropertyRelative("projectileSpeed"),
                    new GUIContent("投掷力度"));
                EditorGUILayout.PropertyField(
                    level.FindPropertyRelative("lifeTime"),
                    new GUIContent("最大生命周期"));
                EditorGUILayout.PropertyField(level.FindPropertyRelative("pierceCount"));
                EditorGUILayout.PropertyField(level.FindPropertyRelative("spreadAngle"));
                EditorGUILayout.PropertyField(
                    level.FindPropertyRelative("lobGravity"),
                    new GUIContent("下坠重力"));
                EditorGUILayout.PropertyField(level.FindPropertyRelative("spinSpeed"));
                break;
            case WeaponRuntimeType.Melee:
                EditorGUILayout.PropertyField(level.FindPropertyRelative("meleeRange"));
                EditorGUILayout.PropertyField(level.FindPropertyRelative("meleeArc"));
                EditorGUILayout.PropertyField(level.FindPropertyRelative("activeDuration"));
                break;
            default:
                EditorGUILayout.PropertyField(level.FindPropertyRelative("projectileCount"));
                EditorGUILayout.PropertyField(level.FindPropertyRelative("projectileSpeed"));
                EditorGUILayout.PropertyField(level.FindPropertyRelative("lifeTime"));
                EditorGUILayout.PropertyField(level.FindPropertyRelative("pierceCount"));
                EditorGUILayout.PropertyField(level.FindPropertyRelative("spreadAngle"));
                EditorGUILayout.PropertyField(level.FindPropertyRelative("bounceCount"));
                EditorGUILayout.PropertyField(level.FindPropertyRelative("bounceMode"));
                break;
        }

        EditorGUILayout.PropertyField(level.FindPropertyRelative("features"), true);
    }

    /// <summary>
    /// 切换直接编辑目标，并按同目录同名规则自动寻找配套升级卡。
    /// </summary>
    private void SetSelectedWeapon(WeaponDataSO weapon)
    {
        _selectedWeapon = weapon;
        _weaponSerializedObject = weapon != null ? new SerializedObject(weapon) : null;
        _selectedUpgrade = null;
        _upgradeSerializedObject = null;

        if (weapon == null)
        {
            return;
        }

        string weaponPath = AssetDatabase.GetAssetPath(weapon);
        string directory = Path.GetDirectoryName(weaponPath);
        string assetName = Path.GetFileNameWithoutExtension(weaponPath);
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(assetName))
        {
            return;
        }

        string upgradePath = $"{directory.Replace('\\', '/')}/{assetName}_Upgrade.asset";
        _selectedUpgrade = AssetDatabase.LoadAssetAtPath<UpgradeDataSO>(upgradePath);
        _upgradeSerializedObject = _selectedUpgrade != null
            ? new SerializedObject(_selectedUpgrade)
            : null;
    }

    /// <summary>
    /// 通过保存面板一次创建一对武器与升级资产，并建立奖励引用。
    /// </summary>
    private void CreateWeaponPair()
    {
        string weaponPath = EditorUtility.SaveFilePanelInProject(
            "创建武器配置",
            "NewWeapon",
            "asset",
            "选择武器数据资产保存位置。",
            "Assets/Data");
        if (string.IsNullOrEmpty(weaponPath))
        {
            return;
        }

        string directory = Path.GetDirectoryName(weaponPath).Replace('\\', '/');
        string assetName = Path.GetFileNameWithoutExtension(weaponPath);
        string upgradePath = $"{directory}/{assetName}_Upgrade.asset";
        if (AssetDatabase.LoadMainAssetAtPath(upgradePath) != null)
        {
            EditorUtility.DisplayDialog("无法创建", $"配套升级资产已存在：{upgradePath}", "确定");
            return;
        }

        var weapon = ScriptableObject.CreateInstance<WeaponDataSO>();
        weapon.weaponID = assetName.ToLowerInvariant();
        weapon.weaponNameKey = $"weapon.{weapon.weaponID}.name";
        weapon.descriptionKey = $"weapon.{weapon.weaponID}.description";
        AssetDatabase.CreateAsset(weapon, weaponPath);

        var upgrade = ScriptableObject.CreateInstance<UpgradeDataSO>();
        upgrade.upgradeName = assetName;
        upgrade.weaponToGrant = weapon;
        AssetDatabase.CreateAsset(upgrade, upgradePath);
        AssetDatabase.SaveAssets();

        Undo.RegisterCreatedObjectUndo(weapon, "创建武器配置");
        Undo.RegisterCreatedObjectUndo(upgrade, "创建武器升级配置");
        SetSelectedWeapon(weapon);
        Selection.activeObject = weapon;
    }
}
