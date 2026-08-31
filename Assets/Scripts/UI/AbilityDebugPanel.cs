#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 仅供编辑器和 Development Build 使用的正式能力测试面板。
/// F7 打开后通过 AbilityManager 正式授予入口逐级验证能力，不参与正式候选流程。
/// </summary>
public sealed class AbilityDebugPanel : MonoBehaviour
{
    private const KeyCode ToggleKey = KeyCode.F7;
    private const float WindowWidth = 760f;
    private const float WindowHeight = 400f;

    private readonly List<AbilityDataSO> _abilities = new List<AbilityDataSO>();
    private Rect _windowRect = new Rect(20f, 440f, WindowWidth, WindowHeight);
    private Vector2 _scrollPosition;
    private LevelUpManager _levelUpManager;
    private AbilityManager _abilityManager;
    private bool _visible;

    /// <summary>每次进入场景后创建唯一调试面板，无需修改正式 Canvas 层级。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateRuntimePanel()
    {
        if (FindObjectOfType<AbilityDebugPanel>() != null)
        {
            return;
        }

        GameObject panelObject = new GameObject(nameof(AbilityDebugPanel));
        panelObject.hideFlags = HideFlags.DontSave;
        DontDestroyOnLoad(panelObject);
        panelObject.AddComponent<AbilityDebugPanel>();
    }

    /// <summary>首次创建时解析正式管理器和能力池。</summary>
    private void Awake()
    {
        ResolveManagers();
        RefreshAbilityList();
    }

    /// <summary>监听 F7 开关；打开时重新解析场景重载后的引用。</summary>
    private void Update()
    {
        if (!Input.GetKeyDown(ToggleKey))
        {
            return;
        }

        _visible = !_visible;
        if (_visible)
        {
            ResolveManagers();
            RefreshAbilityList();
        }
    }

    /// <summary>仅在面板可见时绘制开发专用 IMGUI。</summary>
    private void OnGUI()
    {
        if (!_visible)
        {
            return;
        }

        _windowRect = GUI.Window(GetInstanceID(), _windowRect, DrawWindow, "Ability Debug (F7)");
    }

    /// <summary>绘制刷新入口、依赖诊断和能力等级按钮。</summary>
    private void DrawWindow(int windowId)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("Grant or upgrade formal run abilities. Restart Play Mode to reset.");
        if (GUILayout.Button("Refresh", GUILayout.Width(80f)))
        {
            ResolveManagers();
            RefreshAbilityList();
        }
        GUILayout.EndHorizontal();

        if (_abilityManager == null)
        {
            GUILayout.Label("AbilityManager was not found on the active player.");
            GUI.DragWindow(new Rect(0f, 0f, WindowWidth, 24f));
            return;
        }

        _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);
        for (int index = 0; index < _abilities.Count; index++)
        {
            DrawAbilityRow(_abilities[index]);
        }
        GUILayout.EndScrollView();

        if (_abilities.Count == 0)
        {
            GUILayout.Label("No ability upgrades are configured on LevelUpManager.");
        }

        GUI.DragWindow(new Rect(0f, 0f, WindowWidth, 24f));
    }

    /// <summary>绘制一项能力的当前状态与所有目标等级按钮。</summary>
    private void DrawAbilityRow(AbilityDataSO abilityData)
    {
        if (abilityData == null)
        {
            return;
        }

        OwnedAbilityState state = _abilityManager.GetOwnedAbility(abilityData);
        int currentLevel = state != null ? state.CurrentLevel : 0;
        GUILayout.BeginHorizontal("box");
        GUILayout.Label(abilityData.GetDisplayName(), GUILayout.Width(210f));
        GUILayout.Label(currentLevel > 0 ? $"Lv.{currentLevel}" : "Not owned", GUILayout.Width(80f));
        for (int level = 1; level <= abilityData.MaxLevel; level++)
        {
            bool previousEnabled = GUI.enabled;
            GUI.enabled = currentLevel < level;
            if (GUILayout.Button($"Lv.{level}", GUILayout.Width(64f)))
            {
                _abilityManager.DebugEnsureAbilityLevel(abilityData, level);
            }
            GUI.enabled = previousEnabled;
        }
        GUILayout.EndHorizontal();
    }

    /// <summary>解析场景中的升级管理器与玩家能力管理器。</summary>
    private void ResolveManagers()
    {
        _levelUpManager = LevelUpManager.Instance;
        if (_levelUpManager == null)
        {
            _levelUpManager = FindObjectOfType<LevelUpManager>();
        }

        _abilityManager = FindObjectOfType<AbilityManager>();
    }

    /// <summary>从正式候选池收集去重后的能力资产，不依赖 AssetDatabase。</summary>
    private void RefreshAbilityList()
    {
        _abilities.Clear();
        if (_levelUpManager == null || _levelUpManager.allAvailableUpgrades == null)
        {
            return;
        }

        var seenIds = new HashSet<string>();
        for (int index = 0; index < _levelUpManager.allAvailableUpgrades.Count; index++)
        {
            UpgradeDataSO upgrade = _levelUpManager.allAvailableUpgrades[index];
            AbilityDataSO ability = upgrade != null ? upgrade.abilityToGrant : null;
            if (ability != null && seenIds.Add(ability.GetStableId()))
            {
                _abilities.Add(ability);
            }
        }
    }
}
#endif
