#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 仅供编辑器和 Development Build 使用的运行时武器测试面板。
/// 通过正式的 LevelUpManager 授予路径快速获得指定武器等级，不参与正式升级流程。
/// </summary>
public sealed class WeaponDebugPanel : MonoBehaviour
{
    private const KeyCode ToggleKey = KeyCode.F8;
    private const float WindowWidth = 760f;
    private const float WindowHeight = 400f;

    private readonly List<UpgradeDataSO> _weaponUpgrades = new List<UpgradeDataSO>();

    private LevelUpManager _levelUpManager;
    private Rect _windowRect = new Rect(20f, 20f, WindowWidth, WindowHeight);
    private Vector2 _scrollPosition;
    private bool _visible;

    /// <summary>
    /// 每次进入场景后自动创建唯一调试面板，无需向场景或 Canvas 手动挂载组件。
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateRuntimePanel()
    {
        if (FindObjectOfType<WeaponDebugPanel>() != null)
        {
            return;
        }

        GameObject panelObject = new GameObject(nameof(WeaponDebugPanel));
        panelObject.hideFlags = HideFlags.DontSave;
        DontDestroyOnLoad(panelObject);
        panelObject.AddComponent<WeaponDebugPanel>();
    }

    /// <summary>
    /// 初始化管理器引用和可测试武器列表。
    /// </summary>
    private void Awake()
    {
        ResolveLevelUpManager();
        RefreshWeaponList();
    }

    /// <summary>
    /// 监听 F8 开关；打开时重新读取武器池，确保运行时配置变化可以立即反映。
    /// </summary>
    private void Update()
    {
        if (!Input.GetKeyDown(ToggleKey))
        {
            return;
        }

        _visible = !_visible;
        if (_visible)
        {
            ResolveLevelUpManager();
            RefreshWeaponList();
        }
    }

    /// <summary>
    /// 使用 IMGUI 绘制开发专用窗口；隐藏时立即返回，不产生列表布局开销。
    /// </summary>
    private void OnGUI()
    {
        if (!_visible)
        {
            return;
        }

        _windowRect = GUI.Window(
            GetInstanceID(),
            _windowRect,
            DrawWindow,
            "Weapon Debug (F8)");
    }

    /// <summary>
    /// 绘制面板标题、刷新入口以及所有武器的目标等级按钮。
    /// </summary>
    /// <param name="windowId">Unity IMGUI 分配给当前窗口的稳定标识。</param>
    private void DrawWindow(int windowId)
    {
        GUILayout.Space(4f);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Select a target level. Debug actions only upgrade; restart Play Mode to reset.");
        if (GUILayout.Button("Refresh", GUILayout.Width(80f)))
        {
            ResolveLevelUpManager();
            RefreshWeaponList();
        }
        GUILayout.EndHorizontal();

        if (_levelUpManager == null)
        {
            GUILayout.Space(12f);
            GUILayout.Label("LevelUpManager was not found in the active scene.");
            GUI.DragWindow(new Rect(0f, 0f, WindowWidth, 24f));
            return;
        }

        _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);
        for (int index = 0; index < _weaponUpgrades.Count; index++)
        {
            DrawWeaponRow(_weaponUpgrades[index]);
        }
        GUILayout.EndScrollView();

        if (_weaponUpgrades.Count == 0)
        {
            GUILayout.Label("No weapon upgrades are configured on LevelUpManager.");
        }

        GUI.DragWindow(new Rect(0f, 0f, WindowWidth, 24f));
    }

    /// <summary>
    /// 绘制单把武器的持有状态和所有可用目标等级按钮。
    /// </summary>
    /// <param name="upgradeData">包含显示名称和武器配置引用的升级资产。</param>
    private void DrawWeaponRow(UpgradeDataSO upgradeData)
    {
        if (upgradeData == null || upgradeData.weaponToGrant == null)
        {
            return;
        }

        WeaponDataSO weaponData = upgradeData.weaponToGrant;
        WeaponBase ownedWeapon = _levelUpManager.GetOwnedWeapon(weaponData);
        int currentLevel = ownedWeapon != null ? ownedWeapon.CurrentLevel : 0;

        GUILayout.BeginHorizontal("box");
        GUILayout.Label(GetWeaponDisplayName(upgradeData), GUILayout.Width(210f));
        GUILayout.Label(currentLevel > 0 ? $"Lv.{currentLevel}" : "Not owned", GUILayout.Width(80f));

        for (int level = 1; level <= weaponData.MaxLevel; level++)
        {
            bool previousEnabled = GUI.enabled;
            GUI.enabled = currentLevel < level;
            if (GUILayout.Button($"Lv.{level}", GUILayout.Width(64f)))
            {
                _levelUpManager.DebugEnsureWeaponLevel(weaponData, level);
            }
            GUI.enabled = previousEnabled;
        }

        GUILayout.EndHorizontal();
    }

    /// <summary>
    /// 从场景单例获取升级管理器；场景切换导致旧引用失效时允许重新解析。
    /// </summary>
    private void ResolveLevelUpManager()
    {
        _levelUpManager = LevelUpManager.Instance;
        if (_levelUpManager == null)
        {
            _levelUpManager = FindObjectOfType<LevelUpManager>();
        }
    }

    /// <summary>
    /// 从正式升级池收集去重后的武器升级资产，不使用 AssetDatabase 或额外测试配置。
    /// </summary>
    private void RefreshWeaponList()
    {
        _weaponUpgrades.Clear();
        if (_levelUpManager == null || _levelUpManager.allAvailableUpgrades == null)
        {
            return;
        }

        var seenWeaponIds = new HashSet<string>();
        for (int index = 0; index < _levelUpManager.allAvailableUpgrades.Count; index++)
        {
            UpgradeDataSO upgrade = _levelUpManager.allAvailableUpgrades[index];
            if (upgrade == null || upgrade.weaponToGrant == null)
            {
                continue;
            }

            WeaponDataSO weaponData = upgrade.weaponToGrant;
            string weaponId = !string.IsNullOrEmpty(weaponData.weaponID)
                ? weaponData.weaponID
                : weaponData.name;
            if (seenWeaponIds.Add(weaponId))
            {
                _weaponUpgrades.Add(upgrade);
            }
        }
    }

    /// <summary>
    /// 返回兼顾资产识别和策划显示名的调试标签。
    /// </summary>
    /// <param name="upgradeData">需要生成标签的升级资产。</param>
    /// <returns>优先包含资产名，并在存在本地化前显示名时追加该名称。</returns>
    private static string GetWeaponDisplayName(UpgradeDataSO upgradeData)
    {
        WeaponDataSO weaponData = upgradeData.weaponToGrant;
        if (string.IsNullOrEmpty(upgradeData.upgradeName))
        {
            return weaponData.name;
        }

        return $"{weaponData.name} / {upgradeData.upgradeName}";
    }
}
#endif
