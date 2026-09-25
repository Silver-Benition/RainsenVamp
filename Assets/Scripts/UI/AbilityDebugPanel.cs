#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// F7 道具获取面板，仅编译到编辑器和开发构建。
/// 保留原组件名以兼容既有入口；通过正式授予路径修改局内持有状态，不写共享资产。
/// </summary>
public sealed class AbilityDebugPanel : MonoBehaviour
{
    private const KeyCode ToggleKey = KeyCode.F7;
    private const float WindowWidth = 760f;
    private const float WindowHeight = 480f;
    private const int MaxGrantBatch = 5;
    private readonly List<AbilityDataSO> _items = new List<AbilityDataSO>();
    private readonly HashSet<string> _seenIds = new HashSet<string>();
    private Rect _windowRect = new Rect(20f, 120f, WindowWidth, WindowHeight);
    private Vector2 _scrollPosition;
    private AbilityManager _abilityManager;
    private PlayerStats _player;
    private bool _visible;
    private string _status = "";

    /// <summary>场景首次加载时创建唯一的跨场景面板，不修改正式 Canvas 或 Scene。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateRuntimePanel()
    {
        if (FindObjectOfType<AbilityDebugPanel>() != null) return;
        var panel = new GameObject(nameof(AbilityDebugPanel));
        panel.hideFlags = HideFlags.DontSave;
        DontDestroyOnLoad(panel);
        panel.AddComponent<AbilityDebugPanel>();
    }

    /// <summary>首次创建时绑定当前角色与正式道具目录。</summary>
    private void Awake() { RefreshItems(); }

    /// <summary>F7 在战斗或暂停时均可开关；打开时重新绑定场景重载后的对象。</summary>
    private void Update()
    {
        if (Input.GetKeyDown(ToggleKey)) TogglePanel();
    }

    /// <summary>切换显示状态；重复打开仅更新列表，不生成任何道具。</summary>
    private void TogglePanel()
    {
        _visible = !_visible;
        if (_visible) { RefreshItems(); _status = ""; }
    }

    /// <summary>绘制开发窗口并约束在当前视口内；绘制开销只与道具种类数有关。</summary>
    private void OnGUI()
    {
        if (!_visible) return;
        _windowRect.width = Mathf.Min(WindowWidth, Mathf.Max(1f, Screen.width - 20f));
        _windowRect.height = Mathf.Min(WindowHeight, Mathf.Max(1f, Screen.height - 20f));
        _windowRect.x = Mathf.Clamp(_windowRect.x, 0, Mathf.Max(0, Screen.width - _windowRect.width));
        _windowRect.y = Mathf.Clamp(_windowRect.y, 0, Mathf.Max(0, Screen.height - _windowRect.height));
        _windowRect = GUI.Window(GetInstanceID(), _windowRect, DrawWindow, Text("title", "道具调试（F7）"));
    }

    /// <summary>绘制当前角色诊断、固定批量说明和可滚动道具列表。</summary>
    private void DrawWindow(int windowId)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(Text("help", "直接获得局内道具；每次最多 5 件，达到上限自动停止。"));
        if (GUILayout.Button(Text("refresh", "刷新"), GUILayout.Width(60))) RefreshItems();
        if (GUILayout.Button(Text("close", "关闭"), GUILayout.Width(60))) _visible = false;
        GUILayout.EndHorizontal();
        if (_abilityManager == null)
        {
            GUILayout.Label(Text("noPlayer", "当前没有可用角色，请进入游戏后刷新。"));
            GUI.DragWindow(new Rect(0, 0, _windowRect.width, 24));
            return;
        }
        GUILayout.Label(string.Format(Text("count", "可获取道具：{0} 种 · 唯一道具和叠加上限仍然有效"), _items.Count));
        _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);
        foreach (AbilityDataSO item in _items) DrawItemRow(item);
        GUILayout.EndScrollView();
        if (_items.Count == 0) GUILayout.Label(Text("empty", "正式目录中没有可获取的道具。"));
        if (!string.IsNullOrEmpty(_status)) GUILayout.Label(_status);
        GUI.DragWindow(new Rect(0, 0, _windowRect.width, 24));
    }

    /// <summary>每行只有两个固定按钮；不限叠加的数值上限绝不能作为控件数量。</summary>
    private void DrawItemRow(AbilityDataSO item)
    {
        if (item == null) return;
        int owned = _abilityManager.GetOwnedAbility(item)?.CurrentLevel ?? 0;
        GUILayout.BeginHorizontal("box");
        GUILayout.Label(item.GetDisplayName(), GUILayout.Width(180));
        GUILayout.Label(RoundShopPresentation.Tier(item.quality), GUILayout.Width(65));
        GUILayout.Label(string.Format(Text("owned", "持有 {0}/{1}"), owned, item.CopyLimitText), GUILayout.Width(120));
        bool enabled = GUI.enabled;
        GUI.enabled = enabled && CanGrant(item);
        if (GUILayout.Button(Text("one", "获取 1 件"), GUILayout.Width(95))) GrantItems(item, 1);
        GUI.enabled = enabled && item.MaxLevel > 1 && CanGrant(item);
        if (GUILayout.Button(Text("five", "获取最多 5 件"), GUILayout.Width(125))) GrantItems(item, MaxGrantBatch);
        GUI.enabled = enabled;
        GUILayout.EndHorizontal();
    }

    /// <summary>单次操作最多调用五次正式授予入口，失败立即停止；不能绕过唯一或限量规则。</summary>
    private int GrantItems(AbilityDataSO item, int requested)
    {
        if (requested <= 0 || !_items.Contains(item) || !CanGrant(item)) return 0;
        int granted = 0;
        int count = Mathf.Min(requested, MaxGrantBatch);
        for (int i = 0; i < count && CanGrant(item); i++)
        {
            if (_abilityManager.GrantOrUpgrade(item) == null) break;
            granted++;
        }
        _status = string.Format(Text("granted", "{0}：本次获得 {1} 件"), item.GetDisplayName(), granted);
        return granted;
    }

    /// <summary>按钮与实际操作共用当前角色、分类、可用属性和持有上限检查。</summary>
    private bool CanGrant(AbilityDataSO item)
    {
        return _abilityManager != null && IsValidItem(item) && _abilityManager.CanAcquireAbility(item);
    }

    /// <summary>按策划分类识别道具，保留机制型道具；过滤停用属性和旧能力分类。</summary>
    private bool IsValidItem(AbilityDataSO item)
    {
        return item != null && item.presentationCategory == AbilityPresentationCategory.Item
            && (_player == null || !_player.UsesBrotatoStats || item.IsAvailableInBrotato());
    }

    /// <summary>优先读取正式商店目录；非回合场景兼容既有升级目录，按稳定 ID 去重。</summary>
    private void RefreshItems()
    {
        _abilityManager = RoundController.Enabled ? RoundController.Instance.Items : FindObjectOfType<AbilityManager>();
        if (_abilityManager == null) _abilityManager = FindObjectOfType<AbilityManager>();
        _player = _abilityManager != null ? _abilityManager.GetComponent<PlayerStats>() : null;
        _items.Clear(); _seenIds.Clear();
        RunShopCatalogSO catalog = RoundController.Enabled ? RoundController.Instance.config.shopCatalog : null;
        if (catalog != null)
        {
            if (catalog.products != null)
                foreach (RunShopProduct product in catalog.products)
                    if (product != null && !product.IsWeapon) AddItem(product.content?.abilityToGrant);
        }
        else
        {
            LevelUpManager loadout = LevelUpManager.Instance;
            if (loadout == null) loadout = FindObjectOfType<LevelUpManager>();
            if (loadout != null && loadout.allAvailableUpgrades != null)
                foreach (UpgradeDataSO upgrade in loadout.allAvailableUpgrades) AddItem(upgrade?.abilityToGrant);
        }
    }

    /// <summary>只登记有效道具，避免共享包装或重复商品产生重复行。</summary>
    private void AddItem(AbilityDataSO item)
    {
        if (IsValidItem(item) && _seenIds.Add(item.GetStableId())) _items.Add(item);
    }

    /// <summary>通过统一翻译入口提供调试文案，未接翻译表时回退中文。</summary>
    private static string Text(string key, string fallback) => RoundShopPresentation.Text("debug.items." + key, fallback);
}
#endif
