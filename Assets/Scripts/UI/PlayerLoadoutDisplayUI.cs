using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 在屏幕右上角展示玩家持有的武器与能力槽位，并在手动暂停时展开等级信息。
/// 本组件只读取装备状态并负责 UI 表现，不参与容量判定、升级结算或暂停规则决策。
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerLoadoutDisplayUI : MonoBehaviour
{
    private const int DotDisplayLevelLimit = 9;
    private const int DotColumnCount = 3;

    [Header("基础布局")]
    [SerializeField] private RectTransform panelRoot;
    [SerializeField] private Vector2 anchoredOffset = new Vector2(-24f, -24f);
    [SerializeField] private Vector2 slotSize = new Vector2(48f, 48f);
    [SerializeField] private Vector2 spacing = new Vector2(4f, 4f);
    [SerializeField, Min(0f)] private float borderThickness = 2f;
    [SerializeField, Min(0f)] private float iconPadding;

    [Header("暂停等级布局")]
    [SerializeField, Min(1f)] private float levelAreaHeight = 32f;
    [SerializeField, Min(0f)] private float levelAreaSpacing = 4f;
    [SerializeField] private Vector2 levelDotSize = new Vector2(8f, 8f);
    [SerializeField] private Vector2 levelDotSpacing = new Vector2(2f, 2f);
    [SerializeField, Min(0f)] private float levelPanelBorderThickness = 1f;
    [SerializeField, Min(0f)] private float levelPanelPadding = 1f;
    [SerializeField, Min(0f)] private float levelDotBorderThickness = 1f;

    [Header("颜色与字体")]
    [SerializeField] private Color borderColor = new Color(0.68f, 0.65f, 0.50f, 0.95f);
    [SerializeField] private Color slotBackgroundColor = new Color(0.12f, 0.12f, 0.10f, 0.82f);
    [SerializeField] private Color activeLevelColor = new Color(1f, 0.88f, 0.35f, 1f);
    [SerializeField] private Color inactiveLevelColor = new Color(0.25f, 0.23f, 0.16f, 0.65f);
    [SerializeField] private Color levelNumberColor = new Color(1f, 0.88f, 0.35f, 1f);
    [SerializeField] private Font levelFont;

    private readonly List<LoadoutSlotView> _weaponSlots =
        new List<LoadoutSlotView>(PlayerLoadoutRules.MaxWeaponCount);
    private readonly List<LoadoutSlotView> _abilitySlots =
        new List<LoadoutSlotView>(PlayerLoadoutRules.MaxAbilityCount);

    private LevelUpManager _levelUpManager;
    private GameFlowManager _gameFlowManager;
    private int _displayedWeaponCount;
    private bool _showLevels;

    /// <summary>当前已构建的武器槽位数量。</summary>
    public int WeaponSlotCount => _weaponSlots.Count;

    /// <summary>当前已构建的能力槽位数量。</summary>
    public int AbilitySlotCount => _abilitySlots.Count;

    /// <summary>当前成功绑定了武器数据的槽位数量；图标缺失时仍视为已占用。</summary>
    public int DisplayedWeaponCount => _displayedWeaponCount;

    /// <summary>等级点阵或数字当前是否因手动暂停而展开。</summary>
    public bool IsShowingLevels => _showLevels;

    /// <summary>当前网格单元高度，供自动化测试验证紧凑与展开布局。</summary>
    public float CurrentCellHeight => CalculateCellHeight();

    /// <summary>初始化固定的 6×2 槽位；仅在场景创建时产生一次 UI 对象。</summary>
    private void Awake()
    {
        BuildSlotsIfNeeded();
    }

    /// <summary>组件启用时订阅装备与手动暂停状态。</summary>
    private void OnEnable()
    {
        ResolveManagersAndSubscribe();
    }

    /// <summary>所有场景对象完成 Awake 后再次解析管理器并同步首帧状态。</summary>
    private void Start()
    {
        ResolveManagersAndSubscribe();
        RefreshWeaponSlots();
        SetLevelVisibility(_gameFlowManager != null && _gameFlowManager.IsManuallyPaused);
    }

    /// <summary>组件停用时解除全部事件，避免场景重载后旧 UI 被继续回调。</summary>
    private void OnDisable()
    {
        UnsubscribeLevelUpManager();
        UnsubscribeGameFlowManager();
    }

    /// <summary>在 Inspector 修改布局参数时钳制非法尺寸。</summary>
    private void OnValidate()
    {
        slotSize.x = Mathf.Max(1f, slotSize.x);
        slotSize.y = Mathf.Max(1f, slotSize.y);
        spacing.x = Mathf.Max(0f, spacing.x);
        spacing.y = Mathf.Max(0f, spacing.y);
        borderThickness = Mathf.Max(0f, borderThickness);
        iconPadding = Mathf.Max(0f, iconPadding);
        levelAreaHeight = Mathf.Max(1f, levelAreaHeight);
        levelAreaSpacing = Mathf.Max(0f, levelAreaSpacing);
        levelDotSize.x = Mathf.Max(1f, levelDotSize.x);
        levelDotSize.y = Mathf.Max(1f, levelDotSize.y);
        levelDotSpacing.x = Mathf.Max(0f, levelDotSpacing.x);
        levelDotSpacing.y = Mathf.Max(0f, levelDotSpacing.y);
        levelPanelBorderThickness = Mathf.Max(0f, levelPanelBorderThickness);
        levelPanelPadding = Mathf.Max(0f, levelPanelPadding);
        levelDotBorderThickness = Mathf.Max(0f, levelDotBorderThickness);
    }

    /// <summary>
    /// 创建右上角网格和十二个固定槽位。
    /// 每个槽位预建九个等级点与一个数字文本，暂停切换时只改变显隐，不产生运行时分配。
    /// </summary>
    private void BuildSlotsIfNeeded()
    {
        if (_weaponSlots.Count == PlayerLoadoutRules.MaxWeaponCount &&
            _abilitySlots.Count == PlayerLoadoutRules.MaxAbilityCount)
        {
            return;
        }

        if (panelRoot == null)
        {
            GameObject panelObject = new GameObject(
                "PlayerLoadoutDisplay",
                typeof(RectTransform),
                typeof(GridLayoutGroup));
            panelObject.layer = gameObject.layer;
            panelRoot = panelObject.GetComponent<RectTransform>();
            panelRoot.SetParent(transform, false);
        }

        panelRoot.SetAsLastSibling();
        _weaponSlots.Clear();
        _abilitySlots.Clear();

        for (int index = 0; index < PlayerLoadoutRules.MaxWeaponCount; index++)
        {
            _weaponSlots.Add(CreateSlot($"WeaponSlot_{index + 1}"));
        }

        for (int index = 0; index < PlayerLoadoutRules.MaxAbilityCount; index++)
        {
            _abilitySlots.Add(CreateSlot($"AbilitySlot_{index + 1}"));
        }

        ConfigurePanelLayout();
    }

    /// <summary>把网格固定到父 Canvas 右上角，并按暂停状态计算整体区域。</summary>
    private void ConfigurePanelLayout()
    {
        if (panelRoot == null)
        {
            return;
        }

        float cellHeight = CalculateCellHeight();
        panelRoot.anchorMin = Vector2.one;
        panelRoot.anchorMax = Vector2.one;
        panelRoot.pivot = Vector2.one;
        panelRoot.anchoredPosition = anchoredOffset;
        panelRoot.sizeDelta = new Vector2(
            slotSize.x * PlayerLoadoutRules.MaxWeaponCount +
            spacing.x * (PlayerLoadoutRules.MaxWeaponCount - 1),
            cellHeight * 2f + spacing.y);

        GridLayoutGroup grid = panelRoot.GetComponent<GridLayoutGroup>();
        if (grid == null)
        {
            grid = panelRoot.gameObject.AddComponent<GridLayoutGroup>();
        }

        grid.padding = new RectOffset();
        grid.cellSize = new Vector2(slotSize.x, cellHeight);
        grid.spacing = spacing;
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.childAlignment = TextAnchor.UpperRight;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = PlayerLoadoutRules.MaxWeaponCount;
    }

    /// <summary>返回紧凑图标高度；手动暂停时额外包含等级区域与间距。</summary>
    private float CalculateCellHeight()
    {
        return slotSize.y + (_showLevels ? levelAreaSpacing + levelAreaHeight : 0f);
    }

    /// <summary>
    /// 创建一个图标框、裁切视口和暂停等级区域组成的非交互槽位。
    /// 图标裁切只作用于 HUD，不会改变原始 Sprite 或战斗对象。
    /// </summary>
    private LoadoutSlotView CreateSlot(string slotName)
    {
        GameObject slotObject = new GameObject(slotName, typeof(RectTransform));
        slotObject.layer = gameObject.layer;
        slotObject.transform.SetParent(panelRoot, false);
        RectTransform slotRect = slotObject.GetComponent<RectTransform>();

        GameObject frameObject = new GameObject(
            "IconFrame",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        frameObject.layer = gameObject.layer;
        frameObject.transform.SetParent(slotObject.transform, false);
        RectTransform frameRect = frameObject.GetComponent<RectTransform>();
        frameRect.anchorMin = new Vector2(0f, 1f);
        frameRect.anchorMax = new Vector2(1f, 1f);
        frameRect.pivot = new Vector2(0.5f, 1f);
        frameRect.anchoredPosition = Vector2.zero;
        frameRect.sizeDelta = new Vector2(0f, slotSize.y);
        Image borderImage = frameObject.GetComponent<Image>();
        borderImage.color = borderColor;
        borderImage.raycastTarget = false;

        GameObject backgroundObject = new GameObject(
            "Background",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        backgroundObject.layer = gameObject.layer;
        backgroundObject.transform.SetParent(frameObject.transform, false);
        StretchWithInset(backgroundObject.GetComponent<RectTransform>(), borderThickness);
        Image backgroundImage = backgroundObject.GetComponent<Image>();
        backgroundImage.color = slotBackgroundColor;
        backgroundImage.raycastTarget = false;

        GameObject viewportObject = new GameObject(
            "IconViewport",
            typeof(RectTransform),
            typeof(RectMask2D));
        viewportObject.layer = gameObject.layer;
        viewportObject.transform.SetParent(frameObject.transform, false);
        StretchWithInset(
            viewportObject.GetComponent<RectTransform>(),
            borderThickness + iconPadding);

        GameObject iconObject = new GameObject(
            "Icon",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        iconObject.layer = gameObject.layer;
        iconObject.transform.SetParent(viewportObject.transform, false);
        RectTransform iconRect = iconObject.GetComponent<RectTransform>();
        StretchWithInset(iconRect, 0f);
        Image iconImage = iconObject.GetComponent<Image>();
        iconImage.color = Color.white;
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;
        iconImage.enabled = false;

        GameObject levelRootObject = new GameObject(
            "Level",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        levelRootObject.layer = gameObject.layer;
        levelRootObject.transform.SetParent(slotObject.transform, false);
        RectTransform levelRect = levelRootObject.GetComponent<RectTransform>();
        levelRect.anchorMin = new Vector2(0f, 1f);
        levelRect.anchorMax = new Vector2(1f, 1f);
        levelRect.pivot = new Vector2(0.5f, 1f);
        levelRect.anchoredPosition = new Vector2(0f, -slotSize.y - levelAreaSpacing);
        levelRect.sizeDelta = new Vector2(0f, levelAreaHeight);
        Image levelBorderImage = levelRootObject.GetComponent<Image>();
        levelBorderImage.color = borderColor;
        levelBorderImage.raycastTarget = false;

        GameObject levelBackgroundObject = new GameObject(
            "Background",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        levelBackgroundObject.layer = gameObject.layer;
        levelBackgroundObject.transform.SetParent(levelRootObject.transform, false);
        StretchWithInset(
            levelBackgroundObject.GetComponent<RectTransform>(),
            levelPanelBorderThickness);
        Image levelBackgroundImage = levelBackgroundObject.GetComponent<Image>();
        levelBackgroundImage.color = slotBackgroundColor;
        levelBackgroundImage.raycastTarget = false;

        GameObject dotsRootObject = new GameObject(
            "Dots",
            typeof(RectTransform),
            typeof(GridLayoutGroup));
        dotsRootObject.layer = gameObject.layer;
        dotsRootObject.transform.SetParent(levelRootObject.transform, false);
        StretchWithInset(
            dotsRootObject.GetComponent<RectTransform>(),
            levelPanelBorderThickness + levelPanelPadding);
        GridLayoutGroup dotsGrid = dotsRootObject.GetComponent<GridLayoutGroup>();
        dotsGrid.padding = new RectOffset();
        dotsGrid.cellSize = levelDotSize;
        dotsGrid.spacing = levelDotSpacing;
        dotsGrid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        dotsGrid.startAxis = GridLayoutGroup.Axis.Horizontal;
        dotsGrid.childAlignment = TextAnchor.MiddleCenter;
        dotsGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        dotsGrid.constraintCount = DotColumnCount;

        GameObject[] levelDotRoots = new GameObject[DotDisplayLevelLimit];
        Image[] levelDotFills = new Image[DotDisplayLevelLimit];
        for (int index = 0; index < levelDotRoots.Length; index++)
        {
            GameObject dotObject = new GameObject(
                $"Dot_{index + 1}",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            dotObject.layer = gameObject.layer;
            dotObject.transform.SetParent(dotsRootObject.transform, false);
            Image dotBorderImage = dotObject.GetComponent<Image>();
            dotBorderImage.color = borderColor;
            dotBorderImage.raycastTarget = false;

            GameObject dotFillObject = new GameObject(
                "Fill",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            dotFillObject.layer = gameObject.layer;
            dotFillObject.transform.SetParent(dotObject.transform, false);
            StretchWithInset(
                dotFillObject.GetComponent<RectTransform>(),
                levelDotBorderThickness);
            Image dotFillImage = dotFillObject.GetComponent<Image>();
            dotFillImage.color = inactiveLevelColor;
            dotFillImage.raycastTarget = false;

            levelDotRoots[index] = dotObject;
            levelDotFills[index] = dotFillImage;
        }

        GameObject levelNumberObject = new GameObject(
            "LevelNumber",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Text));
        levelNumberObject.layer = gameObject.layer;
        levelNumberObject.transform.SetParent(levelRootObject.transform, false);
        StretchWithInset(
            levelNumberObject.GetComponent<RectTransform>(),
            levelPanelBorderThickness + levelPanelPadding);
        Text levelNumber = levelNumberObject.GetComponent<Text>();
        levelNumber.font = levelFont;
        levelNumber.fontSize = Mathf.Max(14, Mathf.RoundToInt(levelAreaHeight * 0.75f));
        levelNumber.alignment = TextAnchor.MiddleCenter;
        levelNumber.color = levelNumberColor;
        levelNumber.raycastTarget = false;
        levelNumber.supportRichText = false;

        levelRootObject.SetActive(false);
        dotsRootObject.SetActive(false);
        levelNumberObject.SetActive(false);

        return new LoadoutSlotView(
            slotRect,
            iconRect,
            iconImage,
            levelRootObject,
            dotsRootObject,
            levelDotRoots,
            levelDotFills,
            levelNumber);
    }

    /// <summary>把子 RectTransform 拉伸到父级四边，并应用相同的内缩距离。</summary>
    private static void StretchWithInset(RectTransform rectTransform, float inset)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.offsetMin = new Vector2(inset, inset);
        rectTransform.offsetMax = new Vector2(-inset, -inset);
    }

    /// <summary>解析当前场景中的升级与流程管理器，并保证每个事件只订阅一次。</summary>
    private void ResolveManagersAndSubscribe()
    {
        ResolveLevelUpManagerAndSubscribe();
        ResolveGameFlowManagerAndSubscribe();
    }

    /// <summary>解析升级管理器并订阅武器变化事件。</summary>
    private void ResolveLevelUpManagerAndSubscribe()
    {
        LevelUpManager resolvedManager = LevelUpManager.Instance;
        if (resolvedManager == null)
        {
            resolvedManager = FindObjectOfType<LevelUpManager>();
        }

        if (_levelUpManager == resolvedManager)
        {
            return;
        }

        UnsubscribeLevelUpManager();
        _levelUpManager = resolvedManager;
        if (_levelUpManager != null && isActiveAndEnabled)
        {
            _levelUpManager.OwnedWeaponsChanged += RefreshWeaponSlots;
        }
    }

    /// <summary>解析流程管理器并订阅手动暂停状态事件。</summary>
    private void ResolveGameFlowManagerAndSubscribe()
    {
        GameFlowManager resolvedManager = GameFlowManager.Instance;
        if (resolvedManager == null)
        {
            resolvedManager = FindObjectOfType<GameFlowManager>();
        }

        if (_gameFlowManager == resolvedManager)
        {
            return;
        }

        UnsubscribeGameFlowManager();
        _gameFlowManager = resolvedManager;
        if (_gameFlowManager != null && isActiveAndEnabled)
        {
            _gameFlowManager.ManualPauseChanged += HandleManualPauseChanged;
            SetLevelVisibility(_gameFlowManager.IsManuallyPaused);
        }
        else
        {
            SetLevelVisibility(false);
        }
    }

    /// <summary>解除当前升级管理器事件并清空缓存引用。</summary>
    private void UnsubscribeLevelUpManager()
    {
        if (_levelUpManager != null)
        {
            _levelUpManager.OwnedWeaponsChanged -= RefreshWeaponSlots;
        }

        _levelUpManager = null;
    }

    /// <summary>解除当前流程管理器事件并清空缓存引用。</summary>
    private void UnsubscribeGameFlowManager()
    {
        if (_gameFlowManager != null)
        {
            _gameFlowManager.ManualPauseChanged -= HandleManualPauseChanged;
        }

        _gameFlowManager = null;
    }

    /// <summary>响应手动暂停状态，只在该状态下展开等级区域。</summary>
    private void HandleManualPauseChanged(bool isManuallyPaused)
    {
        SetLevelVisibility(isManuallyPaused);
    }

    /// <summary>切换等级区域并重新计算网格高度，不创建或销毁任何 UI 对象。</summary>
    private void SetLevelVisibility(bool showLevels)
    {
        if (_showLevels == showLevels)
        {
            RefreshAllLevelIndicators();
            return;
        }

        _showLevels = showLevels;
        ConfigurePanelLayout();
        if (_showLevels && panelRoot != null)
        {
            panelRoot.SetAsLastSibling();
        }

        RefreshAllLevelIndicators();
    }

    /// <summary>
    /// 按获得顺序刷新第一排武器槽，应用每项数据的 HUD 专用缩放与等级。
    /// 能力系统尚未实现，第二排保持空槽但使用同一套可扩展槽位结构。
    /// </summary>
    private void RefreshWeaponSlots()
    {
        _displayedWeaponCount = 0;
        for (int index = 0; index < _weaponSlots.Count; index++)
        {
            ClearSlot(_weaponSlots[index]);
        }

        for (int index = 0; index < _abilitySlots.Count; index++)
        {
            ClearSlot(_abilitySlots[index]);
        }

        if (_levelUpManager == null)
        {
            return;
        }

        IReadOnlyList<WeaponBase> ownedWeapons = _levelUpManager.OwnedWeapons;
        int visibleCount = Mathf.Min(ownedWeapons.Count, _weaponSlots.Count);
        for (int index = 0; index < visibleCount; index++)
        {
            WeaponBase weapon = ownedWeapons[index];
            if (weapon == null || weapon.weaponData == null)
            {
                continue;
            }

            WeaponDataSO weaponData = weapon.weaponData;
            ApplySlotContent(
                _weaponSlots[index],
                weaponData.icon,
                weaponData.loadoutIconScale,
                weaponData.loadoutIconOffset,
                weapon.CurrentLevel,
                weapon.MaxLevel);
            _displayedWeaponCount++;
        }
    }

    /// <summary>清空一个槽位的内容、变换和等级状态，但保留已经创建的 UI 对象。</summary>
    private void ClearSlot(LoadoutSlotView slot)
    {
        slot.IsOccupied = false;
        slot.CurrentLevel = 0;
        slot.MaxLevel = 0;
        slot.IconImage.sprite = null;
        slot.IconImage.enabled = false;
        slot.IconRect.localScale = Vector3.one;
        slot.IconRect.anchoredPosition = Vector2.zero;
        RefreshLevelIndicator(slot);
    }

    /// <summary>向一个槽位绑定图标、HUD 变换以及当前/最大等级。</summary>
    private void ApplySlotContent(
        LoadoutSlotView slot,
        Sprite icon,
        float iconScale,
        Vector2 iconOffset,
        int currentLevel,
        int maxLevel)
    {
        slot.IsOccupied = true;
        slot.CurrentLevel = Mathf.Max(1, currentLevel);
        slot.MaxLevel = Mathf.Max(1, maxLevel);
        slot.IconImage.sprite = icon;
        slot.IconImage.enabled = icon != null;
        float safeScale = Mathf.Max(0.01f, iconScale);
        slot.IconRect.localScale = new Vector3(safeScale, safeScale, 1f);
        slot.IconRect.anchoredPosition = iconOffset;
        RefreshLevelIndicator(slot);
    }

    /// <summary>刷新全部槽位等级显隐，供暂停切换时一次性同步。</summary>
    private void RefreshAllLevelIndicators()
    {
        for (int index = 0; index < _weaponSlots.Count; index++)
        {
            RefreshLevelIndicator(_weaponSlots[index]);
        }

        for (int index = 0; index < _abilitySlots.Count; index++)
        {
            RefreshLevelIndicator(_abilitySlots[index]);
        }
    }

    /// <summary>
    /// 刷新单个槽位等级：九级以内显示总等级点数，超过九级显示当前等级数字。
    /// 只有已占用槽位且处于手动暂停时才展示。
    /// </summary>
    private void RefreshLevelIndicator(LoadoutSlotView slot)
    {
        bool shouldShow = _showLevels && slot.IsOccupied;
        slot.LevelRoot.SetActive(shouldShow);
        if (!shouldShow)
        {
            return;
        }

        if (slot.MaxLevel <= DotDisplayLevelLimit)
        {
            slot.DotsRoot.SetActive(true);
            slot.LevelNumber.gameObject.SetActive(false);
            for (int index = 0; index < slot.LevelDotRoots.Length; index++)
            {
                bool dotExists = index < slot.MaxLevel;
                slot.LevelDotRoots[index].SetActive(dotExists);
                if (dotExists)
                {
                    slot.LevelDotFills[index].color = index < slot.CurrentLevel
                        ? activeLevelColor
                        : inactiveLevelColor;
                }
            }
            return;
        }

        slot.DotsRoot.SetActive(false);
        slot.LevelNumber.gameObject.SetActive(true);
        slot.LevelNumber.text = slot.CurrentLevel.ToString();
    }

    /// <summary>保存单个装备槽位的预构建 UI 引用与当前展示状态。</summary>
    private sealed class LoadoutSlotView
    {
        /// <summary>建立槽位视图引用；所有对象在组件 Awake 阶段一次性创建。</summary>
        public LoadoutSlotView(
            RectTransform root,
            RectTransform iconRect,
            Image iconImage,
            GameObject levelRoot,
            GameObject dotsRoot,
            GameObject[] levelDotRoots,
            Image[] levelDotFills,
            Text levelNumber)
        {
            Root = root;
            IconRect = iconRect;
            IconImage = iconImage;
            LevelRoot = levelRoot;
            DotsRoot = dotsRoot;
            LevelDotRoots = levelDotRoots;
            LevelDotFills = levelDotFills;
            LevelNumber = levelNumber;
        }

        public RectTransform Root { get; }
        public RectTransform IconRect { get; }
        public Image IconImage { get; }
        public GameObject LevelRoot { get; }
        public GameObject DotsRoot { get; }
        public GameObject[] LevelDotRoots { get; }
        public Image[] LevelDotFills { get; }
        public Text LevelNumber { get; }
        public bool IsOccupied { get; set; }
        public int CurrentLevel { get; set; }
        public int MaxLevel { get; set; }
    }
}
