using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 战斗数据结果页的纯表现层。
/// 双栏布局只绑定 RunResultSnapshot；动态行和网格单元在低频结果打开时创建，不进入战斗热路径。
/// </summary>
[DisallowMultipleComponent]
public sealed class RunResultsUI : MonoBehaviour
{
    public static RunResultsUI Instance { get; private set; }

    private const float WeaponColumnWeaponWidth = 190f;
    private const float WeaponColumnLevelWidth = 64f;
    private const float WeaponColumnDamageWidth = 80f;
    private const float WeaponColumnTimeWidth = 72f;
    private const float WeaponColumnDpsWidth = 90f;
    private const float WeaponColumnSpacing = 4f;
    private const float WeaponHeaderHeight = 28f;
    private const float WeaponRowHeight = 32f;
    private const float WeaponIconSize = 22f;
    private const float LoadoutCellWidth = 94f;
    private const float LoadoutCellHeight = 76f;

    private readonly List<GameObject> _weaponRows = new List<GameObject>(PlayerLoadoutRules.MaxWeaponCount);
    private readonly List<GameObject> _itemCells = new List<GameObject>(PlayerLoadoutRules.MaxAbilityCount);
    private readonly List<GameObject> _abilityCells = new List<GameObject>(PlayerLoadoutRules.MaxAbilityCount);
    private readonly List<GameObject> _pickupRows = new List<GameObject>(8);

    private GameObject _overlay;
    private Transform _weaponContent;
    private Transform _itemGrid;
    private Transform _abilityGrid;
    private Transform _pickupContent;
    private GameObject _weaponRowTemplate;
    private GameObject _cellTemplate;
    private GameObject _pickupRowTemplate;
    private TextMeshProUGUI _outcomeText;
    private TextMeshProUGUI _basicsText;
    private TextMeshProUGUI _characterNameText;
    private Image _characterIcon;
    private TextMeshProUGUI _itemTitle;
    private TextMeshProUGUI _abilityTitle;
    private TextMeshProUGUI _pickupEmptyText;
    private Button _reviveButton;
    private Button _restartButton;
    private Button _mainMenuButton;
    private TextMeshProUGUI _reviveButtonText;
    private RunDirector _runDirector;

    /// <summary>建立结果页单例和稳定的双栏/模板结构，默认保持隐藏。</summary>
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        BuildView();
        _overlay.SetActive(false);
    }

    /// <summary>组件启用时订阅最终结果与死亡预览事件。</summary>
    private void OnEnable()
    {
        BindRunDirector();
    }

    /// <summary>场景对象完成 Start 后再次绑定，覆盖脚本执行顺序差异。</summary>
    private void Start()
    {
        BindRunDirector();
    }

    /// <summary>场景销毁时解除事件并释放静态实例引用。</summary>
    private void OnDestroy()
    {
        UnbindRunDirector();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>显示不可变最终结果；重复调用只更新一次表现，不修改快照内容。</summary>
    public void Show(RunResultSnapshot snapshot)
    {
        if (snapshot == null)
        {
            return;
        }

        BindSnapshot(snapshot);
        ConfigureButtons(snapshot.IsPreview);
        _overlay.SetActive(true);
        _overlay.transform.SetAsLastSibling();
    }

    /// <summary>显示可复活死亡预览；预览按钮不会提交账号统计。</summary>
    public void ShowPreview(RunResultSnapshot snapshot)
    {
        if (snapshot == null)
        {
            return;
        }

        BindSnapshot(snapshot);
        ConfigureButtons(true);
        _overlay.SetActive(true);
        _overlay.transform.SetAsLastSibling();
    }

    /// <summary>复活成功后关闭临时预览，最终结果仍由 RunDirector 单独管理。</summary>
    public void HidePreview()
    {
        if (_overlay != null)
        {
            _overlay.SetActive(false);
        }
    }

    /// <summary>接收 RunDirector 最终冻结事件并显示结果。</summary>
    private void HandleResultFrozen(RunResultSnapshot snapshot)
    {
        Show(snapshot);
    }

    /// <summary>接收 RunDirector 死亡预览事件并刷新当前结果页。</summary>
    private void HandleDeathPreviewChanged(RunResultSnapshot snapshot)
    {
        ShowPreview(snapshot);
    }

    /// <summary>接收复活事件并清除旧预览，避免死亡时的临时数据残留在画面。</summary>
    private void HandleDeathPreviewDiscarded()
    {
        HidePreview();
    }

    /// <summary>绑定当前场景 RunDirector，避免结果页依赖场景中的具体层级名称。</summary>
    private void BindRunDirector()
    {
        RunDirector resolved = RunDirector.Instance != null
            ? RunDirector.Instance
            : FindObjectOfType<RunDirector>();
        if (_runDirector == resolved)
        {
            return;
        }

        UnbindRunDirector();
        _runDirector = resolved;
        if (_runDirector != null)
        {
            _runDirector.ResultFrozen += HandleResultFrozen;
            _runDirector.DeathPreviewChanged += HandleDeathPreviewChanged;
            _runDirector.DeathPreviewDiscarded += HandleDeathPreviewDiscarded;
        }
    }

    /// <summary>解除 RunDirector 事件，避免场景重载后旧 Canvas 继续响应。</summary>
    private void UnbindRunDirector()
    {
        if (_runDirector == null)
        {
            return;
        }

        _runDirector.ResultFrozen -= HandleResultFrozen;
        _runDirector.DeathPreviewChanged -= HandleDeathPreviewChanged;
        _runDirector.DeathPreviewDiscarded -= HandleDeathPreviewDiscarded;
        _runDirector = null;
    }

    /// <summary>构建 1920/1080 基准下可按比例缩放的双列战斗数据框架。</summary>
    private void BuildView()
    {
        _overlay = CreateUiObject("RunResultsOverlay", transform, typeof(Image));
        RectTransform overlayRect = _overlay.GetComponent<RectTransform>();
        Stretch(overlayRect, 0f);
        Image overlayImage = _overlay.GetComponent<Image>();
        overlayImage.color = new Color(0.015f, 0.025f, 0.05f, 0.94f);
        overlayImage.raycastTarget = true;

        GameObject panel = CreateUiObject("RunResultsPanel", _overlay.transform, typeof(Image));
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        Stretch(panelRect, 28f);
        Image panelImage = panel.GetComponent<Image>();
        panelImage.color = new Color(0.05f, 0.075f, 0.12f, 0.98f);
        panelImage.raycastTarget = false;

        CreateText("Title", panel.transform, "战斗数据", new Vector2(0.04f, 0.92f), new Vector2(0.96f, 0.985f), 34f, TextAlignmentOptions.Center);
        _outcomeText = CreateText("Outcome", panel.transform, "胜利", new Vector2(0.04f, 0.85f), new Vector2(0.96f, 0.92f), 24f, TextAlignmentOptions.Center);

        _basicsText = CreateTextSection(
            panel.transform,
            "RunBasics",
            "本局概览",
            new Vector2(0.03f, 0.65f),
            new Vector2(0.48f, 0.845f),
            out Transform basicsContent);
        basicsContent.GetComponent<VerticalLayoutGroup>().spacing = 2f;

        CreateSection(
            panel.transform,
            "WeaponTable",
            "武器统计  ·  实际扣血",
            new Vector2(0.03f, 0.14f),
            new Vector2(0.48f, 0.63f),
            out _weaponContent);
        ConfigureVerticalList(_weaponContent, 2f);
        CreateWeaponTableHeader(_weaponContent);

        GameObject loadoutSection = CreateSection(panel.transform, "Loadout", "角色与局内装备", new Vector2(0.51f, 0.34f), new Vector2(0.97f, 0.845f), out Transform loadoutContent);
        BuildLoadoutSection(loadoutContent);

        CreateTextSection(
            panel.transform,
            "InstantPickups",
            "地图即时效果拾取",
            new Vector2(0.51f, 0.14f),
            new Vector2(0.97f, 0.31f),
            out _pickupContent);
        _pickupEmptyText = CreateText("PickupEmpty", _pickupContent, "暂无地图即时效果拾取物", Vector2.zero, Vector2.one, 18f, TextAlignmentOptions.Center);

        GameObject buttons = CreateUiObject("ResultButtons", panel.transform, typeof(HorizontalLayoutGroup));
        RectTransform buttonRect = buttons.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.18f, 0.025f);
        buttonRect.anchorMax = new Vector2(0.82f, 0.115f);
        buttonRect.offsetMin = Vector2.zero;
        buttonRect.offsetMax = Vector2.zero;
        HorizontalLayoutGroup buttonLayout = buttons.GetComponent<HorizontalLayoutGroup>();
        buttonLayout.spacing = 18f;
        buttonLayout.childAlignment = TextAnchor.MiddleCenter;
        buttonLayout.childControlWidth = false;
        buttonLayout.childControlHeight = false;
        buttonLayout.childForceExpandWidth = false;
        buttonLayout.childForceExpandHeight = false;

        _reviveButton = CreateButton("ReviveButton", buttons.transform, "复活");
        _reviveButtonText = _reviveButton.GetComponentInChildren<TextMeshProUGUI>(true);
        _restartButton = CreateButton("RestartButton", buttons.transform, "重新开始");
        _mainMenuButton = CreateButton("MainMenuButton", buttons.transform, "返回主菜单");

        _weaponRowTemplate = CreateWeaponRowTemplate(_weaponContent, "WeaponRowTemplate");
        _cellTemplate = CreateCellTemplate(loadoutContent, "LoadoutCellTemplate");
        _pickupRowTemplate = CreateTextRowTemplate(_pickupContent, "PickupRowTemplate");
        _weaponRowTemplate.SetActive(false);
        _cellTemplate.SetActive(false);
        _pickupRowTemplate.SetActive(false);
    }

    /// <summary>建立角色头像以及独立 Item/Ability 网格，不从 mechanic 空值推断分类。</summary>
    private void BuildLoadoutSection(Transform content)
    {
        GameObject characterObject = CreateUiObject("Character", content, typeof(HorizontalLayoutGroup));
        RectTransform characterRect = characterObject.GetComponent<RectTransform>();
        characterRect.anchorMin = new Vector2(0f, 0.78f);
        characterRect.anchorMax = new Vector2(1f, 1f);
        characterRect.offsetMin = new Vector2(10f, 4f);
        characterRect.offsetMax = new Vector2(-10f, -4f);
        HorizontalLayoutGroup characterLayout = characterObject.GetComponent<HorizontalLayoutGroup>();
        characterLayout.spacing = 12f;
        characterLayout.childAlignment = TextAnchor.MiddleLeft;

        GameObject iconObject = CreateUiObject("CharacterIcon", characterObject.transform, typeof(Image));
        LayoutElement iconLayout = iconObject.AddComponent<LayoutElement>();
        iconLayout.preferredWidth = 58f;
        iconLayout.preferredHeight = 58f;
        _characterIcon = iconObject.GetComponent<Image>();
        _characterIcon.preserveAspect = true;
        _characterIcon.raycastTarget = false;
        _characterNameText = CreateText("CharacterName", characterObject.transform, "未知角色", Vector2.zero, Vector2.one, 21f, TextAlignmentOptions.Left);
        LayoutElement nameLayout = _characterNameText.gameObject.AddComponent<LayoutElement>();
        nameLayout.flexibleWidth = 1f;

        _itemTitle = CreateText("ItemTitle", content, "Items", new Vector2(0f, 0.60f), new Vector2(1f, 0.74f), 16f, TextAlignmentOptions.Left);
        _abilityTitle = CreateText("AbilityTitle", content, "Abilities", new Vector2(0f, 0.22f), new Vector2(1f, 0.34f), 16f, TextAlignmentOptions.Left);
        _itemGrid = CreateGrid(content, "ItemGrid", new Vector2(0f, 0.37f), new Vector2(1f, 0.58f));
        _abilityGrid = CreateGrid(content, "AbilityGrid", new Vector2(0f, 0.01f), new Vector2(1f, 0.20f));
    }

    /// <summary>把冻结快照绑定到所有静态标签、动态统计行和装备网格。</summary>
    private void BindSnapshot(RunResultSnapshot snapshot)
    {
        _outcomeText.text = snapshot.IsPreview
            ? "战斗数据（可复活）"
            : snapshot.Outcome == RunOutcome.Victory ? "胜利" : "失败";
        _outcomeText.color = snapshot.IsPreview
            ? new Color(0.35f, 0.95f, 1f, 1f)
            : snapshot.Outcome == RunOutcome.Victory
                ? new Color(1f, 0.88f, 0.3f, 1f)
                : new Color(1f, 0.42f, 0.4f, 1f);

        _basicsText.text =
            $"地图：{snapshot.MapDisplayName}\n" +
            $"生存时间：{FormatTime(snapshot.SurvivalTimeSeconds)}\n" +
            $"金币：{snapshot.Gold}\n" +
            $"击杀：{snapshot.Kills}\n" +
            $"等级：Lv.{snapshot.Level}";

        _characterNameText.text = snapshot.Character.DisplayName;
        _characterIcon.sprite = snapshot.Character.Avatar;
        _characterIcon.enabled = snapshot.Character.Avatar != null;
        _itemTitle.text = $"Items  ({snapshot.Items.Count})";
        _abilityTitle.text = $"Abilities  ({snapshot.Abilities.Count})";

        ClearObjects(_weaponRows);
        int weaponCount = Mathf.Min(snapshot.Weapons.Count, PlayerLoadoutRules.MaxWeaponCount);
        for (int index = 0; index < weaponCount; index++)
        {
            RunResultWeaponSnapshot weapon = snapshot.Weapons[index];
            GameObject row = Instantiate(_weaponRowTemplate, _weaponContent);
            row.SetActive(true);
            BindWeaponRow(row.transform, weapon);
            _weaponRows.Add(row);
        }

        BindAbilityCells(snapshot.Items, _itemGrid, _itemCells);
        BindAbilityCells(snapshot.Abilities, _abilityGrid, _abilityCells);

        ClearObjects(_pickupRows);
        _pickupEmptyText.gameObject.SetActive(snapshot.InstantEffectPickups.Count == 0);
        for (int index = 0; index < snapshot.InstantEffectPickups.Count; index++)
        {
            RunResultPickupSnapshot pickup = snapshot.InstantEffectPickups[index];
            GameObject row = Instantiate(_pickupRowTemplate, _pickupContent);
            row.SetActive(true);
            Transform label = row.transform.Find("Label");
            Transform value = row.transform.Find("Value");
            if (label != null) label.GetComponent<TMP_Text>().text = pickup.DisplayName;
            if (value != null) value.GetComponent<TMP_Text>().text = $"×{pickup.Count}";
            _pickupRows.Add(row);
        }
    }

    /// <summary>把一条武器快照绑定到五个独立列，保持列语义与表头一一对应。</summary>
    private static void BindWeaponRow(Transform row, RunResultWeaponSnapshot weapon)
    {
        Transform weaponColumn = row.Find("Weapon");
        if (weaponColumn != null)
        {
            Transform iconTransform = weaponColumn.Find("Icon");
            if (iconTransform != null)
            {
                Image icon = iconTransform.GetComponent<Image>();
                icon.sprite = weapon.Icon;
                icon.enabled = weapon.Icon != null;
                iconTransform.gameObject.SetActive(weapon.Icon != null);
            }

            Transform textTransform = weaponColumn.Find("Text");
            if (textTransform != null)
            {
                textTransform.GetComponent<TMP_Text>().text = weapon.DisplayName;
            }
        }

        SetWeaponColumnText(row, "Level", $"Lv.{weapon.CurrentLevel}/{weapon.MaxLevel}");
        SetWeaponColumnText(row, "Damage", $"{weapon.ActualTotalDamage:F0}");
        SetWeaponColumnText(row, "Time", FormatTime(weapon.FirstEffectTime));
        SetWeaponColumnText(row, "Dps", $"{weapon.DamagePerSecond:F1}");
    }

    /// <summary>写入武器表指定列的唯一文本节点，避免回退到拼接字符串布局。</summary>
    private static void SetWeaponColumnText(Transform row, string columnName, string value)
    {
        Transform column = row.Find(columnName);
        Transform text = column != null ? column.Find("Text") : null;
        if (text != null)
        {
            text.GetComponent<TMP_Text>().text = value;
        }
    }

    /// <summary>为物品或能力网格绑定固定分类的图标、名称和等级。</summary>
    private void BindAbilityCells(
        IReadOnlyList<RunResultAbilitySnapshot> entries,
        Transform content,
        List<GameObject> createdCells)
    {
        ClearObjects(createdCells);
        for (int index = 0; index < entries.Count; index++)
        {
            RunResultAbilitySnapshot entry = entries[index];
            GameObject cell = Instantiate(_cellTemplate, content);
            cell.SetActive(true);
            Transform icon = cell.transform.Find("Icon");
            Transform text = cell.transform.Find("Text");
            if (icon != null)
            {
                Image image = icon.GetComponent<Image>();
                image.sprite = entry.Icon;
                image.enabled = entry.Icon != null;
            }

            if (text != null)
            {
                text.GetComponent<TMP_Text>().text = $"{entry.DisplayName}\nLv.{entry.CurrentLevel}/{entry.MaxLevel}";
            }

            createdCells.Add(cell);
        }
    }

    /// <summary>显示或隐藏结果按钮，并统一绑定 GameFlowManager 的一次性流程入口。</summary>
    private void ConfigureButtons(bool preview)
    {
        GameFlowManager flow = GameFlowManager.Instance;
        _reviveButton.gameObject.SetActive(preview);
        _reviveButton.interactable = preview;
        _restartButton.interactable = flow != null;
        _mainMenuButton.interactable = flow != null;
        _reviveButton.onClick.RemoveAllListeners();
        _restartButton.onClick.RemoveAllListeners();
        _mainMenuButton.onClick.RemoveAllListeners();

        if (flow != null)
        {
            _reviveButton.onClick.AddListener(flow.RequestRevive);
            _restartButton.onClick.AddListener(flow.RestartGame);
            _mainMenuButton.onClick.AddListener(flow.ReturnToMainMenu);
        }

        if (_reviveButtonText != null)
        {
            RunState state = FindObjectOfType<RunState>();
            int remaining = state != null ? state.RemainingRevivals : 0;
            _reviveButtonText.text = $"复活  ×{remaining}";
            _reviveButton.interactable = preview && remaining > 0;
        }
    }

    /// <summary>创建武器统计表的固定表头，五列顺序与所有数据行完全一致。</summary>
    private static GameObject CreateWeaponTableHeader(Transform parent)
    {
        GameObject header = CreateUiObject(
            "WeaponTableHeader",
            parent,
            typeof(Image),
            typeof(HorizontalLayoutGroup),
            typeof(LayoutElement));
        header.GetComponent<Image>().color = new Color(0.12f, 0.19f, 0.29f, 1f);
        ConfigureWeaponRowLayout(header, WeaponHeaderHeight);

        CreateWeaponTextColumn(
            header.transform,
            "Weapon",
            WeaponColumnWeaponWidth,
            "武器",
            TextAlignmentOptions.Left,
            true,
            true);
        CreateWeaponTextColumn(
            header.transform,
            "Level",
            WeaponColumnLevelWidth,
            "等级",
            TextAlignmentOptions.Right,
            true,
            false);
        CreateWeaponTextColumn(
            header.transform,
            "Damage",
            WeaponColumnDamageWidth,
            "伤害",
            TextAlignmentOptions.Right,
            true,
            false);
        CreateWeaponTextColumn(
            header.transform,
            "Time",
            WeaponColumnTimeWidth,
            "时间",
            TextAlignmentOptions.Right,
            true,
            false);
        CreateWeaponTextColumn(
            header.transform,
            "Dps",
            WeaponColumnDpsWidth,
            "每秒伤害",
            TextAlignmentOptions.Right,
            true,
            false);
        return header;
    }

    /// <summary>创建与表头共享列宽常量的五列武器行模板。</summary>
    private static GameObject CreateWeaponRowTemplate(Transform parent, string name)
    {
        GameObject row = CreateUiObject(
            name,
            parent,
            typeof(Image),
            typeof(HorizontalLayoutGroup),
            typeof(LayoutElement));
        row.GetComponent<Image>().color = new Color(0.08f, 0.11f, 0.17f, 0.9f);
        ConfigureWeaponRowLayout(row, WeaponRowHeight);

        CreateWeaponNameColumn(row.transform);
        CreateWeaponTextColumn(
            row.transform,
            "Level",
            WeaponColumnLevelWidth,
            "Lv.1/1",
            TextAlignmentOptions.Right,
            false,
            false);
        CreateWeaponTextColumn(
            row.transform,
            "Damage",
            WeaponColumnDamageWidth,
            "0",
            TextAlignmentOptions.Right,
            false,
            false);
        CreateWeaponTextColumn(
            row.transform,
            "Time",
            WeaponColumnTimeWidth,
            "00:00",
            TextAlignmentOptions.Right,
            false,
            false);
        CreateWeaponTextColumn(
            row.transform,
            "Dps",
            WeaponColumnDpsWidth,
            "0.0",
            TextAlignmentOptions.Right,
            false,
            false);
        return row;
    }

    /// <summary>配置武器表头和数据行共同使用的背景、行高、内边距与列间距。</summary>
    private static void ConfigureWeaponRowLayout(GameObject row, float preferredHeight)
    {
        LayoutElement rowLayout = row.GetComponent<LayoutElement>();
        rowLayout.preferredHeight = preferredHeight;
        rowLayout.minHeight = preferredHeight;

        HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 2, 2);
        layout.spacing = WeaponColumnSpacing;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = false;
    }

    /// <summary>创建带固定宽度的普通武器统计列，并只在列内部放置一个文本节点。</summary>
    private static GameObject CreateWeaponTextColumn(
        Transform parent,
        string name,
        float width,
        string text,
        TextAlignmentOptions alignment,
        bool header,
        bool flexibleWidth)
    {
        GameObject column = CreateUiObject(name, parent, typeof(LayoutElement));
        LayoutElement columnLayout = column.GetComponent<LayoutElement>();
        columnLayout.minWidth = width;
        columnLayout.preferredWidth = width;
        columnLayout.flexibleWidth = flexibleWidth ? 1f : 0f;

        TextMeshProUGUI columnText = CreateText(
            "Text",
            column.transform,
            text,
            Vector2.zero,
            Vector2.one,
            header ? 12f : 13f,
            alignment);
        columnText.fontStyle = header ? FontStyles.Bold : FontStyles.Normal;
        return column;
    }

    /// <summary>创建武器名称列，按需显示图标并保持名称左对齐。</summary>
    private static GameObject CreateWeaponNameColumn(Transform parent)
    {
        GameObject column = CreateUiObject(
            "Weapon",
            parent,
            typeof(LayoutElement),
            typeof(HorizontalLayoutGroup));
        LayoutElement columnLayout = column.GetComponent<LayoutElement>();
        columnLayout.minWidth = WeaponColumnWeaponWidth;
        columnLayout.preferredWidth = WeaponColumnWeaponWidth;
        columnLayout.flexibleWidth = 1f;

        HorizontalLayoutGroup layout = column.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 4f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        GameObject iconObject = CreateUiObject(
            "Icon",
            column.transform,
            typeof(Image),
            typeof(LayoutElement));
        LayoutElement iconLayout = iconObject.GetComponent<LayoutElement>();
        iconLayout.preferredWidth = WeaponIconSize;
        iconLayout.preferredHeight = WeaponIconSize;
        iconLayout.minWidth = WeaponIconSize;
        iconLayout.minHeight = WeaponIconSize;
        Image icon = iconObject.GetComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        TextMeshProUGUI text = CreateText(
            "Text",
            column.transform,
            "武器",
            Vector2.zero,
            Vector2.one,
            13f,
            TextAlignmentOptions.Left);
        LayoutElement textLayout = text.gameObject.AddComponent<LayoutElement>();
        textLayout.flexibleWidth = 1f;
        textLayout.minWidth = 0f;
        return column;
    }

    /// <summary>创建带背景和双文本列的通用动态行模板，继续供 Part4 拾取统计使用。</summary>
    private static GameObject CreateTextRowTemplate(Transform parent, string name)
    {
        GameObject row = CreateUiObject(name, parent, typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.GetComponent<Image>().color = new Color(0.08f, 0.11f, 0.17f, 0.9f);
        LayoutElement rowLayout = row.GetComponent<LayoutElement>();
        rowLayout.preferredHeight = 32f;
        HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 2, 2);
        layout.spacing = 4f;
        layout.childControlHeight = true;
        layout.childControlWidth = false;

        TextMeshProUGUI label = CreateText("Label", row.transform, "名称", Vector2.zero, Vector2.one, 14f, TextAlignmentOptions.Left);
        LayoutElement labelLayout = label.gameObject.AddComponent<LayoutElement>();
        labelLayout.flexibleWidth = 1f;
        TextMeshProUGUI value = CreateText("Value", row.transform, "数据", Vector2.zero, Vector2.one, 13f, TextAlignmentOptions.Right);
        LayoutElement valueLayout = value.gameObject.AddComponent<LayoutElement>();
        valueLayout.preferredWidth = 260f;
        return row;
    }

    /// <summary>创建带图标和两行文本的装备网格单元模板。</summary>
    private static GameObject CreateCellTemplate(Transform parent, string name)
    {
        GameObject cell = CreateUiObject(name, parent, typeof(Image), typeof(LayoutElement));
        cell.GetComponent<Image>().color = new Color(0.08f, 0.11f, 0.17f, 0.9f);
        LayoutElement layout = cell.GetComponent<LayoutElement>();
        layout.preferredWidth = LoadoutCellWidth;
        layout.preferredHeight = LoadoutCellHeight;

        GameObject icon = CreateUiObject("Icon", cell.transform, typeof(Image));
        RectTransform iconRect = icon.GetComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0f, 0.42f);
        iconRect.anchorMax = new Vector2(1f, 0.98f);
        iconRect.offsetMin = new Vector2(8f, 0f);
        iconRect.offsetMax = new Vector2(-8f, 0f);
        Image iconImage = icon.GetComponent<Image>();
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;

        TextMeshProUGUI text = CreateText("Text", cell.transform, "装备\nLv.1/1", new Vector2(0f, 0.02f), new Vector2(1f, 0.4f), 12f, TextAlignmentOptions.Center);
        text.enableWordWrapping = true;
        return cell;
    }

    /// <summary>创建结果页的区域背景、标题和内容锚点。</summary>
    private static GameObject CreateSection(
        Transform parent,
        string name,
        string title,
        Vector2 anchorMin,
        Vector2 anchorMax,
        out Transform content)
    {
        GameObject section = CreateUiObject(name, parent, typeof(Image));
        RectTransform sectionRect = section.GetComponent<RectTransform>();
        sectionRect.anchorMin = anchorMin;
        sectionRect.anchorMax = anchorMax;
        sectionRect.offsetMin = Vector2.zero;
        sectionRect.offsetMax = Vector2.zero;
        section.GetComponent<Image>().color = new Color(0.035f, 0.05f, 0.085f, 0.98f);
        CreateText("Header", section.transform, title, new Vector2(0.02f, 0.84f), new Vector2(0.98f, 0.99f), 16f, TextAlignmentOptions.Left);
        GameObject contentObject = CreateUiObject("Content", section.transform, typeof(RectTransform));
        RectTransform contentRect = contentObject.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0.02f, 0.03f);
        contentRect.anchorMax = new Vector2(0.98f, 0.82f);
        contentRect.offsetMin = Vector2.zero;
        contentRect.offsetMax = Vector2.zero;
        content = contentObject.transform;
        return section;
    }

    /// <summary>创建带垂直布局的文本区域并返回第一行基础文本。</summary>
    private static TextMeshProUGUI CreateTextSection(
        Transform parent,
        string name,
        string title,
        Vector2 anchorMin,
        Vector2 anchorMax,
        out Transform content)
    {
        CreateSection(parent, name, title, anchorMin, anchorMax, out content);
        ConfigureVerticalList(content, 0f);
        return CreateText("Summary", content, string.Empty, Vector2.zero, Vector2.one, 17f, TextAlignmentOptions.Left);
    }

    /// <summary>为低频结果列表统一设置内边距、行距和子项尺寸控制。</summary>
    private static VerticalLayoutGroup ConfigureVerticalList(Transform content, float spacing)
    {
        VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 4, 4);
        layout.spacing = spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return layout;
    }

    /// <summary>创建网格容器，使用固定单元尺寸确保两种目标分辨率下不重叠。</summary>
    private static Transform CreateGrid(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
    {
        GameObject gridObject = CreateUiObject(name, parent, typeof(GridLayoutGroup));
        RectTransform rect = gridObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = new Vector2(6f, 0f);
        rect.offsetMax = new Vector2(-6f, 0f);
        GridLayoutGroup grid = gridObject.GetComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(LoadoutCellWidth, LoadoutCellHeight);
        grid.spacing = new Vector2(5f, 4f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 6;
        grid.childAlignment = TextAnchor.UpperLeft;
        return gridObject.transform;
    }

    /// <summary>创建一个结果页按钮并应用统一的可读尺寸。</summary>
    private static Button CreateButton(string name, Transform parent, string label)
    {
        GameObject buttonObject = CreateUiObject(name, parent, typeof(Image), typeof(Button), typeof(LayoutElement));
        buttonObject.GetComponent<Image>().color = new Color(0.12f, 0.24f, 0.38f, 1f);
        LayoutElement layout = buttonObject.GetComponent<LayoutElement>();
        layout.preferredWidth = 160f;
        layout.preferredHeight = 42f;
        CreateText("Label", buttonObject.transform, label, Vector2.zero, Vector2.one, 16f, TextAlignmentOptions.Center);
        return buttonObject.GetComponent<Button>();
    }

    /// <summary>创建 TMP 文本并设置不换行、描边和锚点。</summary>
    private static TextMeshProUGUI CreateText(
        string name,
        Transform parent,
        string text,
        Vector2 anchorMin,
        Vector2 anchorMax,
        float fontSize,
        TextAlignmentOptions alignment)
    {
        GameObject textObject = CreateUiObject(name, parent, typeof(TextMeshProUGUI));
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        TextMeshProUGUI textComponent = textObject.GetComponent<TextMeshProUGUI>();
        textComponent.text = text;
        textComponent.fontSize = fontSize;
        textComponent.alignment = alignment;
        textComponent.color = Color.white;
        textComponent.enableWordWrapping = false;
        textComponent.raycastTarget = false;
        return textComponent;
    }

    /// <summary>创建带 RectTransform 的 UI GameObject。</summary>
    private static GameObject CreateUiObject(string name, Transform parent, params System.Type[] components)
    {
        GameObject result = new GameObject(name, components);
        result.layer = parent != null ? parent.gameObject.layer : 5;
        if (parent != null)
        {
            result.transform.SetParent(parent, false);
        }

        return result;
    }

    /// <summary>把 RectTransform 拉伸到父级并使用统一像素内缩。</summary>
    private static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    /// <summary>销毁低频动态行/单元并清空登记表，不影响隐藏模板。</summary>
    private static void ClearObjects(List<GameObject> objects)
    {
        for (int index = 0; index < objects.Count; index++)
        {
            if (objects[index] != null)
            {
                Destroy(objects[index]);
            }
        }

        objects.Clear();
    }

    /// <summary>将有限非负秒数格式化为分钟和秒，用于获得时间与生存时间。</summary>
    private static string FormatTime(float seconds)
    {
        float safeSeconds = RunResultValueSanitizer.SanitizeNonNegative(seconds);
        int totalSeconds = safeSeconds >= int.MaxValue
            ? int.MaxValue
            : Mathf.FloorToInt(safeSeconds);
        return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
    }
}
