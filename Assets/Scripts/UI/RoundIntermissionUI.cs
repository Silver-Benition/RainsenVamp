using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>局间视图：四商品、右侧属性、下方图标持有栏与悬停详情；所有交易仍由原服务裁决。</summary>
public sealed class RoundIntermissionUI : MonoBehaviour
{
    public TMP_FontAsset font;
    private static readonly Color Background = new Color32(45, 45, 45, 205);
    private static readonly Color Surface = new Color32(18, 20, 19, 255);
    private static readonly Color Border = new Color32(88, 88, 74, 255);
    private static readonly Color Accent = new Color32(170, 221, 111, 255);
    private static readonly PlayerStatType[] PrimaryStats = {
        PlayerStatType.MaxHealth, PlayerStatType.Recovery, PlayerStatType.Armor, PlayerStatType.MoveSpeed,
        PlayerStatType.Might, PlayerStatType.Cooldown, PlayerStatType.Area, PlayerStatType.Amount,
        PlayerStatType.ProjectileSpeed, PlayerStatType.Duration, PlayerStatType.Luck, PlayerStatType.Magnet };
    private static readonly PlayerStatType[] SecondaryStats = {
        PlayerStatType.Growth, PlayerStatType.Greed, PlayerStatType.Curse, PlayerStatType.Revival,
        PlayerStatType.Reroll, PlayerStatType.Skip, PlayerStatType.Banish, PlayerStatType.Charm, PlayerStatType.Defang };

    private GameObject _panel;
    private RectTransform _passOverlay, _progressHud, _levelIcons, _crateIcons, _crateCard;
    private TMP_Text _passText, _crateName, _crateBody, _crateHeading;
    private Image _crateImage;
    private Button _takeCrate, _recycleCrate, _banishCrate;
    private readonly List<Graphic> _chestIcons = new List<Graphic>();
    private RunCrateReward _displayedCrate;
    private readonly List<Graphic> _progressIcons = new List<Graphic>();
    private readonly List<CanvasGroup> _combatHud = new List<CanvasGroup>();
    private TMP_Text _title, _balance, _reserve, _status, _combatBalance, _weaponHeading, _itemHeading, _level;
    private readonly TMP_Text[] _cardNames = new TMP_Text[4], _cardTypes = new TMP_Text[4], _cardTexts = new TMP_Text[4];
    private readonly Image[] _icons = new Image[4];
    private readonly Button[] _actions = new Button[4], _secondary = new Button[4], _banish = new Button[4];
    private readonly List<InventoryCell> _itemCells = new List<InventoryCell>();
    private readonly InventoryCell[] _weaponCells = new InventoryCell[6];
    private readonly TMP_Text[] _statNames = new TMP_Text[12], _statValues = new TMP_Text[12];
    private readonly Image[] _statIcons = new Image[12];
    private readonly GameObject[] _statRows = new GameObject[12];
    private ScrollRect _itemScroll;
    private GridLayoutGroup _itemGrid;
    private RectTransform _weaponGrid;
    private Button _refresh, _skip, _next, _mainTab, _otherTab;
    private bool _secondaryStats, _layoutDirty = true;

    private RectTransform _tooltip, _tooltipOwner;
    private TMP_Text _tooltipName, _tooltipKind, _tooltipBody;
    private Image _tooltipIcon;
    private Button _tooltipCombine, _tooltipRecycle;
    private WeaponBase _inspectedWeapon;
    private float _hideAt = -1;
    private bool _pinned;
    private RoundController _rounds;
    private RoundPhase _lastPhase = RoundPhase.Finished;
    public GameObject Panel => _panel;
    public GameObject Tooltip => _tooltip.gameObject;

    /// <summary>持有图标控件只在容量增长时创建，刷新只替换内容与实例回调。</summary>
    private sealed class InventoryCell
    {
        public RectTransform Root;
        public Button Button;
        public Image Icon;
        public TMP_Text Badge;
        public RoundHoverTarget Hover;
    }

    /// <summary>通过稳定键读取界面文案，避免把翻译接入点散落到业务服务中。</summary>
    private static string T(string key, string fallback) => RoundShopPresentation.Text("round.ui." + key, fallback);

    /// <summary>按参考布局建立四个主要区域；正式 Canvas 继续使用既有覆盖模式。</summary>
    private void Awake()
    {
        _combatBalance = Text("RoundMaterials", transform, "", .04f, .84f, .38f, .90f, 26);
        _passOverlay = Box("PassOverlay", transform, 0, 0, 1, 1, Background);
        _passText = Text("Passed", _passOverlay, T("passed", "通过！"), .25f, .43f, .75f, .60f, 72);
        _passText.alignment = TextAlignmentOptions.Center;
        _passOverlay.gameObject.SetActive(false);
        RectTransform panel = Box("RoundIntermission", transform, 0, 0, 1, 1, Color.clear);
        _panel = panel.gameObject;
        _title = Text("Title", panel, "", .035f, .89f, .36f, .965f, 38);
        _balance = Text("Balance", panel, "", .38f, .92f, .56f, .967f, 35);
        _balance.color = Accent; _balance.alignment = TextAlignmentOptions.MidlineRight;
        _reserve = Text("Reserve", panel, "", .38f, .875f, .56f, .919f, 20);
        _reserve.alignment = TextAlignmentOptions.MidlineRight;
        _refresh = Button("Refresh", panel, "", .59f, .89f, .75f, .962f, RefreshOffers);
        for (int i = 0; i < 4; i++)
        {
            int index = i;
            float left = .035f + i * .181f;
            RectTransform card = Box("Offer" + i, panel, left, .365f, left + .172f, .853f, Surface);
            Outline(card);
            _icons[i] = Icon("Icon", card, .055f, .79f, .31f, .955f);
            _cardNames[i] = Text("Name", card, "", .35f, .845f, .95f, .966f, 27);
            _cardTypes[i] = Text("Kind", card, "", .35f, .773f, .96f, .838f, 19);
            _cardTexts[i] = Text("Description", card, "", .065f, .23f, .935f, .735f, 23);
            _cardTexts[i].alignment = TextAlignmentOptions.TopLeft;
            _actions[i] = Button("Action", card, "", .22f, .05f, .78f, .175f, () => Act(index));
            _secondary[i] = Button("Secondary", card, "", .0f, -.102f, 1f, -.02f, () => Secondary(index));
            _banish[i] = Button("Banish", card, "", .51f, -.102f, 1f, -.02f, () => Banish(index));
        }
        _skip = Button("Skip", panel, "", .785f, .055f, .965f, .135f, Skip);

        BuildStats(panel);
        BuildInventory(panel);
        BuildCrate(panel);
        BuildTooltip(panel);
        _next = Button("Next", panel, "", .785f, .055f, .965f, .135f, Next);
        _next.image.color = new Color32(65, 91, 49, 255);
        _status = Text("Status", panel, "", .035f, .012f, .75f, .045f, 19);
        _panel.SetActive(false);
        BuildProgressHud();
    }

    /// <summary>宝箱页使用单张详情卡和三种互斥操作，物品效果仍由控制器执行。</summary>
    private void BuildCrate(RectTransform panel)
    {
        _crateCard = Box("CrateReward", panel, .20f, .365f, .58f, .85f, Color.clear);
        _crateHeading = Text("Heading", _crateCard, T("foundItem", "发现道具！"), 0, .88f, 1, 1, 38);
        _crateHeading.alignment = TextAlignmentOptions.Center;
        RectTransform details = Box("Details", _crateCard, .12f, .34f, .88f, .85f, Surface);
        Outline(details);
        _crateImage = Icon("Icon", details, .055f, .64f, .25f, .94f);
        _crateName = Text("Name", details, "", .29f, .64f, .95f, .94f, 26);
        _crateBody = Text("Description", details, "", .06f, .045f, .94f, .60f, 22);
        _crateBody.alignment = TextAlignmentOptions.TopLeft;
        _takeCrate = Button("Take", _crateCard, T("take", "拿取"), .04f, .225f, .96f, .32f, () => ResolveCrate(CrateRewardAction.Take));
        _recycleCrate = Button("Recycle", _crateCard, "", .04f, .112f, .96f, .207f, () => ResolveCrate(CrateRewardAction.Recycle), true);
        _banishCrate = Button("Banish", _crateCard, "", .04f, 0, .96f, .095f, () => ResolveCrate(CrateRewardAction.Banish), true);
    }

    /// <summary>右上角使用复用图标展示待领取升级，宝箱同样逐个显示在升级下方；不创建业务副本。</summary>
    private void BuildProgressHud()
    {
        _progressHud = Rect("RoundProgress", transform, .785f, .85f, .965f, .99f);
        _levelIcons = Rect("Levels", _progressHud, 0, .53f, 1, 1);
        _crateIcons = Rect("Crates", _progressHud, 0, 0, 1, .47f);
    }

    /// <summary>一次处理后显示下一个宝箱；失败保留原卡，成功后恢复可用操作焦点。</summary>
    private void ResolveCrate(CrateRewardAction action)
    {
        HideTooltip();
        bool success = _rounds.ResolveCrate(action);
        _status.text = success ? "" : T("unavailable", "当前无法执行此操作");
        Refresh();
        if (success && _rounds.Phase == RoundPhase.Crates && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(_takeCrate.gameObject);
    }

    /// <summary>只在变化事件或窗口尺寸变化时更新图标；过多升级换行并缩放到保留区域内。</summary>
    private void RefreshProgress()
    {
        bool visible = _rounds.Phase != RoundPhase.Finished && _rounds.Phase != RoundPhase.Preparing && _rounds.Phase != RoundPhase.Shop;
        _progressHud.gameObject.SetActive(visible);
        if (!visible) return;
        _progressHud.SetAsLastSibling();
        _progressHud.anchorMax = new Vector2(.965f, _rounds.Phase == RoundPhase.Combat ? .945f : .99f);
        RefreshIconRow(_progressIcons, _levelIcons, _rounds.PendingUpgrades, false);
        RefreshIconRow(_chestIcons, _crateIcons, _rounds.PendingCrates, true);
    }

    /// <summary>按权威待处理数量复用图标；升级和宝箱分别换行，始终留在各自的区域内。</summary>
    private void RefreshIconRow(List<Graphic> icons, RectTransform parent, int count, bool chest)
    {
        while (icons.Count < count)
        {
            RectTransform rect = Rect((chest ? "Crate" : "Level") + icons.Count, parent, 0, 0, 1, 1);
            rect.gameObject.AddComponent<CanvasRenderer>();
            Graphic icon;
            if (chest)
            {
                Image image = rect.gameObject.AddComponent<Image>();
                image.sprite = _rounds.config.crateIcon; image.preserveAspect = true; icon = image;
            }
            else { icon = rect.gameObject.AddComponent<RoundUpgradeIcon>(); icon.color = Accent; }
            icon.raycastTarget = false; icons.Add(icon);
        }
        int rows = Mathf.Max(1, Mathf.CeilToInt(count / 6f));
        float size = Mathf.Max(1, Mathf.Min(32, Mathf.Min(parent.rect.width / 6, parent.rect.height / rows)));
        for (int i = 0; i < icons.Count; i++)
        {
            Graphic icon = icons[i]; icon.gameObject.SetActive(i < count);
            if (i >= count) continue;
            RectTransform rect = icon.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 1);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = new Vector2(-(i % 6) * size, -(i / 6) * size);
        }
    }

    /// <summary>属性栏按主要/次要分组，读取当前最终值和整局剩余次数。</summary>
    private void BuildStats(RectTransform parent)
    {
        RectTransform board = Box("StatsBoard", parent, .775f, .285f, .965f, .963f, Surface);
        Outline(board);
        Text("Heading", board, T("stats", "属性"), .08f, .905f, .92f, .984f, 33).alignment = TextAlignmentOptions.Center;
        _mainTab = Button("Primary", board, T("primary", "主要"), .06f, .815f, .48f, .89f, () => SelectStats(false));
        _otherTab = Button("Secondary", board, T("secondary", "次要"), .52f, .815f, .94f, .89f, () => SelectStats(true));
        _level = Text("Level", board, "", .09f, .75f, .93f, .805f, 22);
        for (int i = 0; i < _statRows.Length; i++)
        {
            float top = .73f - i * .056f;
            RectTransform row = Box("Stat" + i, board, .07f, top - .049f, .94f, top, Color.clear);
            row.GetComponent<Image>().raycastTarget = false; _statRows[i] = row.gameObject;
            _statIcons[i] = Icon("Icon", row, 0, .06f, .10f, .94f);
            _statNames[i] = Text("Name", row, "", .135f, 0, .72f, 1, 21);
            _statValues[i] = Text("Value", row, "", .72f, 0, 1, 1, 21);
            _statValues[i].alignment = TextAlignmentOptions.MidlineRight;
            _statValues[i].color = Accent;
        }
    }

    /// <summary>下方使用可滚动道具图标和三列两行武器图标，物品名称不常驻占位。</summary>
    private void BuildInventory(RectTransform parent)
    {
        _itemHeading = Text("ItemsHeading", parent, "", .035f, .252f, .5f, .304f, 29);
        _weaponHeading = Text("WeaponsHeading", parent, "", .54f, .252f, .75f, .304f, 29);
        RectTransform area = Box("ItemsArea", parent, .035f, .059f, .518f, .246f, new Color32(37, 38, 33, 255));
        _itemScroll = area.gameObject.AddComponent<ScrollRect>();
        RectTransform viewport = Rect("Viewport", area, .008f, .015f, .99f, .985f);
        viewport.gameObject.AddComponent<RectMask2D>();
        RectTransform content = Rect("Content", viewport, 0, 1, 1, 1);
        content.pivot = new Vector2(.5f, 1);
        _itemGrid = content.gameObject.AddComponent<GridLayoutGroup>();
        _itemGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; _itemGrid.constraintCount = 10;
        _itemGrid.spacing = new Vector2(8, 8); _itemGrid.padding = new RectOffset(4, 4, 4, 4);
        var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _itemScroll.viewport = viewport; _itemScroll.content = content;
        _itemScroll.horizontal = false; _itemScroll.vertical = true; _itemScroll.scrollSensitivity = 35;
        _itemScroll.movementType = ScrollRect.MovementType.Clamped;
        _weaponGrid = Rect("WeaponsArea", parent, .54f, .059f, .75f, .246f);
        for (int i = 0; i < 6; i++) _weaponCells[i] = CreateCell("WeaponSlot" + i, _weaponGrid);
    }

    /// <summary>图标详情包含武器操作；延迟离开允许鼠标从图标进入窗体，点击图标可固定。</summary>
    private void BuildTooltip(RectTransform parent)
    {
        _tooltip = Box("InspectTooltip", parent, .30f, .30f, .61f, .83f, new Color32(13, 16, 14, 255));
        Outline(_tooltip);
        _tooltipIcon = Icon("Icon", _tooltip, .06f, .80f, .22f, .96f);
        _tooltipName = Text("Name", _tooltip, "", .27f, .855f, .81f, .96f, 29);
        _tooltipKind = Text("Kind", _tooltip, "", .27f, .78f, .91f, .85f, 20);
        _tooltipBody = Text("Description", _tooltip, "", .065f, .22f, .935f, .75f, 23);
        _tooltipBody.alignment = TextAlignmentOptions.TopLeft;
        _tooltipCombine = Button("Combine", _tooltip, T("combine", "合并"), .06f, .055f, .47f, .16f, () => WeaponAction(false));
        _tooltipRecycle = Button("Recycle", _tooltip, T("recycle", "回收"), .53f, .055f, .94f, .16f, () => WeaponAction(true));
        Button("Close", _tooltip, T("close", "关闭"), .82f, .905f, .96f, .975f, HideTooltip);
        _tooltip.gameObject.AddComponent<RoundHoverTarget>().Bind(() => _hideAt = -1, () => ScheduleHide(_tooltipOwner));
        foreach (Button control in _tooltip.GetComponentsInChildren<Button>())
            control.gameObject.AddComponent<RoundHoverTarget>().Bind(() => _hideAt = -1, () => ScheduleHide(_tooltipOwner));
        _tooltip.gameObject.SetActive(false);
    }

    /// <summary>建立可复用图标按钮与角标；背景保留射线以支持整个格子的悬停。</summary>
    private InventoryCell CreateCell(string name, Transform parent)
    {
        RectTransform root = Box(name, parent, 0, 0, 1, 1, Border);
        Box("Inset", root, .035f, .035f, .965f, .965f, Surface).GetComponent<Image>().raycastTarget = false;
        root.gameObject.AddComponent<RectMask2D>();
        Button button = root.gameObject.AddComponent<Button>(); button.targetGraphic = root.GetComponent<Image>();
        ConfigureButton(button);
        return new InventoryCell { Root = root, Button = button, Icon = Icon("Icon", root, .11f, .11f, .89f, .89f),
            Badge = Text("Badge", root, "", .48f, .015f, .94f, .30f, 19),
            Hover = root.gameObject.AddComponent<RoundHoverTarget>() };
    }

    /// <summary>场景初始化后绑定低频变化事件，刷新不改变任何玩家状态。</summary>
    private void Start()
    {
        _rounds = RoundController.Instance;
        if (_rounds == null) return;
        _rounds.Changed += Refresh; _rounds.Wallet.Changed += Refresh;
        foreach (string name in new[] { "ExpBarContainer", "RunStatsDisplay", "GameTimer" })
        {
            Transform root = transform.Find(name);
            if (root == null) continue;
            CanvasGroup group = root.GetComponent<CanvasGroup>();
            // Unity 的缺失组件使用其重载空值判断，不使用 CLR 空合并运算符。
            if (group == null) group = root.gameObject.AddComponent<CanvasGroup>();
            _combatHud.Add(group);
        }
        Refresh();
    }

    /// <summary>解除事件，场景重开后不留下旧视图引用。</summary>
    private void OnDestroy()
    {
        if (_rounds == null) return;
        _rounds.Changed -= Refresh; _rounds.Wallet.Changed -= Refresh;
    }

    /// <summary>分辨率变化只标记布局脏状态，下帧在 Canvas 尺寸确定后重排图标。</summary>
    private void OnRectTransformDimensionsChange() { _layoutDirty = true; }

    /// <summary>仅处理延时退出和布局脏状态；不在逐帧路径构造商品或详情字符串。</summary>
    private void Update()
    {
        if (_layoutDirty && _weaponGrid != null) { LayoutInventory(); if (_rounds != null) RefreshProgress(); _layoutDirty = false; }
        if (_hideAt >= 0 && !_pinned && Time.unscaledTime >= _hideAt) HideTooltip();
        if (_tooltip != null && _tooltip.gameObject.activeSelf && Input.GetKeyDown(KeyCode.Escape)) HideTooltip();
    }

    /// <summary>用实际视口宽度保持图标正方形，武器始终三列两行。</summary>
    private void LayoutInventory()
    {
        float itemSize = Mathf.Max(20, (_itemScroll.viewport.rect.width - 80) / 10);
        _itemGrid.cellSize = new Vector2(itemSize, itemSize);
        float size = Mathf.Min((_weaponGrid.rect.width - 16) / 3, (_weaponGrid.rect.height - 8) / 2);
        for (int i = 0; i < 6; i++)
        {
            RectTransform rect = _weaponCells[i].Root;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = new Vector2((_weaponGrid.rect.width - size * 3 - 16) * .5f + (i % 3) * (size + 8), -(i / 3) * (size + 8));
        }
    }

    /// <summary>交易和选择仍走原服务，失败不会修改报价或材料。</summary>
    private void Act(int index)
    {
        HideTooltip();
        bool success = _rounds.Phase == RoundPhase.Upgrades ? _rounds.Choose(index) : _rounds.Shop.Buy(index);
        _status.text = success ? "" : T("buyFailed", "材料不足、已满级或武器槽位不足");
        Refresh();
    }
    /// <summary>商品辅助栏切换报价锁定。</summary>
    private void Secondary(int index)
    {
        bool success = _rounds.Shop.ToggleLock(index);
        _status.text = success ? "" : T("unavailable", "当前无法执行此操作"); Refresh();
    }
    /// <summary>道具卡上的放逐消耗单局次数；操作后收起详情并刷新剩余次数。</summary>
    private void Banish(int index)
    {
        HideTooltip();
        bool success = _rounds.Shop.Banish(index);
        _status.text = success ? T("banishedItem", "已放逐，该道具本局不再出现在商店") : T("unavailable", "当前无法执行此操作");
        Refresh();
    }
    /// <summary>顶部刷新按钮复用原有免费重投和材料刷新规则。</summary>
    private void RefreshOffers()
    {
        HideTooltip();
        bool success = _rounds.Phase == RoundPhase.Upgrades ? _rounds.RerollUpgrade() : _rounds.Shop.Refresh();
        _status.text = success ? "" : T("refreshFailed", "材料不足或所有商品已锁定"); Refresh();
    }
    /// <summary>跳过当前属性选择。</summary>
    private void Skip() { HideTooltip(); _rounds.SkipUpgrade(); Refresh(); }
    /// <summary>右下主按钮开始下一回合并关闭所有详情。</summary>
    private void Next() { HideTooltip(); _rounds.BeginNextRound(); Refresh(); }

    /// <summary>使用详情绑定的实例引用执行合并或回收，避免图标列表重排后操作错误武器。</summary>
    private void WeaponAction(bool recycle)
    {
        WeaponBase weapon = _inspectedWeapon;
        HideTooltip();
        bool success = recycle ? _rounds.Shop.Recycle(weapon) : _rounds.Shop.Combine(weapon);
        _status.text = success ? "" : T("combineFailed", "需要另一把同种同品质武器，且未达到最高品质");
        Refresh();
    }

    /// <summary>切换属性分组，不触发购买或角色属性变化。</summary>
    private void SelectStats(bool secondary) { _secondaryStats = secondary; RefreshStats(); }

    /// <summary>将权威状态绑定到现有控件，保持已获得道具只显示图标与叠加角标。</summary>
    public void Refresh()
    {
        if (_rounds == null || _rounds.Shop == null) return;
        bool upgrades = _rounds.Phase == RoundPhase.Upgrades, shop = _rounds.Phase == RoundPhase.Shop;
        bool crates = _rounds.Phase == RoundPhase.Crates, settling = _rounds.Phase == RoundPhase.Settling;
        foreach (CanvasGroup group in _combatHud)
        {
            bool visible = _rounds.Phase == RoundPhase.Combat;
            group.alpha = visible ? 1 : 0; group.blocksRaycasts = visible;
        }
        _passOverlay.gameObject.SetActive(settling || upgrades || crates || shop);
        // 商店遮挡已复位的竞技场；升级和宝箱仍保留原半透明过渡背景。
        Color overlayColor = Background; if (shop) overlayColor.a = 1;
        _passOverlay.GetComponent<Image>().color = overlayColor;
        _passText.gameObject.SetActive(settling);
        RefreshProgress();
        _combatBalance.gameObject.SetActive(_rounds.Phase == RoundPhase.Combat);
        _combatBalance.text = string.Format(T("combatBalance", "材料 {0}　储备 {1}"), _rounds.Wallet.Balance, _rounds.Wallet.Bagged);
        _panel.SetActive(upgrades || shop || crates);
        if (!upgrades && !shop && !crates) { HideTooltip(); _lastPhase = _rounds.Phase; return; }
        _panel.transform.SetAsLastSibling();
        _progressHud.SetAsLastSibling();
        RectTransform board = (RectTransform)_panel.transform.Find("StatsBoard");
        board.anchorMax = new Vector2(.965f, shop ? .963f : .835f);
        if (_lastPhase != _rounds.Phase)
        {
            HideTooltip();
            _status.text = string.IsNullOrEmpty(_rounds.LastReward) ? "" : T("crate", "宝箱奖励：") + _rounds.LastReward;
        }
        _title.text = upgrades ? string.Format(T("growthLevelTitle", "升级至 {0} 级 · 剩余 {1} 次"), _rounds.UpgradeLevel, _rounds.Player.PendingLevelUps)
            : crates ? "" : string.Format(T("shopTitle", "商店（第 {0} 波）"), _rounds.RoundNumber);
        _balance.text = string.Format(T("balance", "材料  {0}"), _rounds.Wallet.Balance);
        _reserve.text = string.Format(T("reserve", "储备  {0}"), _rounds.Wallet.Bagged);
        for (int i = 0; i < 4; i++)
        {
            RectTransform card = (RectTransform)_actions[i].transform.parent;
            card.gameObject.SetActive(!crates);
            float left = .035f + i * .181f;
            card.anchorMin = new Vector2(left, upgrades ? .40f : .365f);
            card.anchorMax = new Vector2(left + .172f, upgrades ? .77f : .853f);
            if (!crates) RefreshCard(i, upgrades);
        }
        _refresh.gameObject.SetActive(!crates);
        _crateCard.gameObject.SetActive(crates);
        if (crates) RefreshCrate();
        RefreshInventory(); RefreshStats();
        Label(_refresh, upgrades ? RunState.Instance.RemainingRerolls > 0
            ? string.Format(T("freeReroll", "重投 · 免费 {0}"), RunState.Instance.RemainingRerolls)
            : string.Format(T("paidReroll", "重投 · {0}"), _rounds.UpgradeRerollPrice)
            : string.Format(T("refresh", "刷新 · {0}"), _rounds.Shop.RefreshPrice));
        _skip.gameObject.SetActive(upgrades);
        _skip.interactable = RunState.Instance.RemainingSkips > 0;
        Label(_skip, string.Format(T("skip", "跳过（{0}）"), RunState.Instance.RemainingSkips));
        _next.gameObject.SetActive(shop);
        Label(_next, string.Format(T("next", "出发（第 {0} 波）"), _rounds.RoundNumber + 1));
        if (_lastPhase != _rounds.Phase && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(crates ? _takeCrate.gameObject : _actions[0].gameObject);
        _lastPhase = _rounds.Phase;
        _layoutDirty = true;
    }

    /// <summary>展示固定宝箱奖励及回收金额，禁用计数读取共享单局名单和容量。</summary>
    private void RefreshCrate()
    {
        RunCrateReward reward = _rounds.CurrentCrate;
        if (reward == null) return;
        if (_displayedCrate != reward)
        {
            ((HoldToConfirmButton)_recycleCrate).CancelHold();
            ((HoldToConfirmButton)_banishCrate).CancelHold();
            _displayedCrate = reward;
        }
        AbilityDataSO data = reward.Product.content.abilityToGrant;
        OwnedAbilityState owned = _rounds.Items.GetOwnedAbility(data);
        _crateImage.sprite = reward.Product.Icon;
        _crateName.text = reward.Product.Name + "\n" + string.Format(T("itemLimit", "持有 {0}/{1}"), owned?.CurrentLevel ?? 0, data.MaxLevel);
        _crateBody.text = RoundShopPresentation.ItemDetails(data, (owned?.CurrentLevel ?? 0) + 1);
        _crateHeading.text = string.Format(T("foundItemCount", "发现道具！剩余 {0}"), _rounds.PendingCrates);
        Label(_recycleCrate, string.Format(T("crateRecycleHold", "长按回收（+{0}）"), reward.RecycleValue));
        RunState run = RunState.Instance;
        Label(_banishCrate, string.Format(T("crateBanishHold", "长按禁用（{0}/{1}）（+{2}）"), run.BanishedUpgradeIds.Count, run.BanishCapacity, reward.RecycleValue));
        _banishCrate.interactable = run.RemainingBanishes > 0;
        _takeCrate.interactable = owned == null || owned.CurrentLevel < data.MaxLevel;
    }

    /// <summary>商品卡直接展示购买前说明，已持有装备的详情则只出现在悬停窗。</summary>
    private void RefreshCard(int i, bool upgrades)
    {
        RunShopOffer offer = _rounds.Shop.Offers[i];
        RoundStatUpgrade stat = upgrades && i < _rounds.Choices.Count ? _rounds.Choices[i] : null;
        bool has = upgrades ? stat != null : offer != null;
        int tier = stat != null ? _rounds.ChoiceTiers[i] : offer?.Tier ?? 1;
        _icons[i].sprite = upgrades ? stat?.icon : offer?.Product.Icon;
        _icons[i].enabled = _icons[i].sprite != null;
        _cardNames[i].text = upgrades ? stat?.displayName ?? "" : offer?.Product.Name ?? T("emptyOffer", "暂无商品");
        _cardTypes[i].text = upgrades ? RoundShopPresentation.Tier(tier)
            : offer == null ? "" : offer.Product.IsWeapon ? RoundShopPresentation.Tier(offer.Tier) : T("item", "道具");
        _cardTypes[i].color = RoundShopPresentation.TierColor(upgrades ? tier : offer?.Tier ?? 1);
        if (stat != null)
        {
            PlayerStatModifier mod = stat.modifier;
            bool percent = mod.Mode != PlayerStatModifierMode.Flat || mod.StatType == PlayerStatType.Defang
                || mod.StatType == PlayerStatType.Might || mod.StatType == PlayerStatType.Cooldown
                || mod.StatType == PlayerStatType.Luck || mod.StatType == PlayerStatType.Growth
                || mod.StatType == PlayerStatType.Area || mod.StatType == PlayerStatType.Greed;
            float value = mod.Value * tier * (percent ? 100 : 1);
            _cardTexts[i].text = $"<color=#B5E780>{value:+0.##;-0.##;0}" + (percent ? "%" : "") + "</color>\n\n"
                + T("current", "当前：") + PlayerStatPresentation.FormatFinalValue(mod.StatType, _rounds.Player.GetFinalStat(mod.StatType));
        }
        else if (offer != null)
        {
            if (offer.Product.IsWeapon) _cardTexts[i].text = RoundShopPresentation.WeaponDetails(offer.Product.content.weaponToGrant, offer.Tier);
            else
            {
                AbilityDataSO data = offer.Product.content.abilityToGrant;
                OwnedAbilityState owned = _rounds.Items.GetOwnedAbility(data);
                _cardTexts[i].text = RoundShopPresentation.ItemDetails(data, (owned?.CurrentLevel ?? 0) + 1);
            }
        }
        else _cardTexts[i].text = "";
        Label(_actions[i], upgrades ? T("choose", "选择") : offer == null ? "—" : string.Format(T("price", "{0} 材料"), offer.Price));
        Label(_secondary[i], offer != null && offer.Locked ? T("locked", "已锁定") : T("lock", "锁定"));
        bool canBanish = !upgrades && offer != null && !offer.Product.IsWeapon && RunState.Instance.RemainingBanishes > 0;
        _secondary[i].gameObject.SetActive(!upgrades && has);
        _banish[i].gameObject.SetActive(canBanish);
        Label(_banish[i], string.Format(T("banishCount", "放逐 {0}"), RunState.Instance.RemainingBanishes));
        RectTransform lockRect = (RectTransform)_secondary[i].transform;
        lockRect.anchorMax = new Vector2(canBanish ? .49f : 1f, -.02f);
        _actions[i].interactable = has;
        _secondary[i].interactable = has;
    }

    /// <summary>持有栏只根据实际实例更新图标；空武器格不可交互，道具超出区域可滚动。</summary>
    private void RefreshInventory()
    {
        var weapons = _rounds.Loadout.OwnedWeapons;
        _weaponHeading.text = string.Format(T("weapons", "武器（{0}/6）"), weapons.Count);
        for (int i = 0; i < 6; i++)
        {
            InventoryCell cell = _weaponCells[i];
            WeaponBase weapon = i < weapons.Count ? weapons[i] : null;
            cell.Icon.sprite = weapon != null ? weapon.weaponData.icon : null; cell.Icon.enabled = cell.Icon.sprite != null;
            cell.Icon.rectTransform.localScale = Vector3.one * (weapon != null ? weapon.weaponData.loadoutIconScale : 1);
            cell.Icon.rectTransform.anchoredPosition = weapon != null ? weapon.weaponData.loadoutIconOffset : Vector2.zero;
            cell.Badge.text = weapon != null ? weapon.CurrentLevel.ToString() : ""; cell.Badge.alignment = TextAlignmentOptions.BottomRight;
            cell.Button.image.color = weapon != null ? RoundShopPresentation.TierColor(weapon.CurrentLevel) : Border;
            cell.Button.interactable = weapon != null; cell.Button.onClick.RemoveAllListeners();
            cell.Hover.Bind(() => ShowWeapon(cell.Root, weapon, false), () => ScheduleHide(cell.Root));
            if (weapon != null) cell.Button.onClick.AddListener(() => ShowWeapon(cell.Root, weapon, true));
        }
        var items = _rounds.Items.OwnedAbilities;
        _itemHeading.text = string.Format(T("items", "道具（{0}）"), items.Count);
        while (_itemCells.Count < items.Count) _itemCells.Add(CreateCell("ItemSlot" + _itemCells.Count, _itemScroll.content));
        for (int i = 0; i < _itemCells.Count; i++)
        {
            InventoryCell cell = _itemCells[i]; cell.Root.gameObject.SetActive(i < items.Count);
            if (i >= items.Count) continue;
            OwnedAbilityState item = items[i];
            cell.Icon.sprite = item.Data.icon; cell.Icon.enabled = cell.Icon.sprite != null;
            cell.Icon.rectTransform.localScale = Vector3.one * item.Data.loadoutIconScale;
            cell.Icon.rectTransform.anchoredPosition = item.Data.loadoutIconOffset;
            cell.Badge.text = item.CurrentLevel > 1 ? "×" + item.CurrentLevel : ""; cell.Badge.alignment = TextAlignmentOptions.BottomRight;
            cell.Hover.Bind(() => ShowItem(cell.Root, item), () => ScheduleHide(cell.Root));
            cell.Button.onClick.RemoveAllListeners(); cell.Button.onClick.AddListener(() => ShowItem(cell.Root, item, true));
        }
    }

    /// <summary>显示角色最终属性；资源栏显示剩余次数而非已消耗前的容量。</summary>
    private void RefreshStats()
    {
        if (_rounds?.Player == null) return;
        _mainTab.image.color = _secondaryStats ? Surface : new Color32(64, 74, 51, 255);
        _otherTab.image.color = _secondaryStats ? new Color32(64, 74, 51, 255) : Surface;
        _level.text = string.Format(T("level", "当前等级  {0}"), _rounds.Player.currentLevel);
        PlayerStatType[] stats = _secondaryStats ? SecondaryStats : PrimaryStats;
        for (int i = 0; i < 12; i++)
        {
            _statRows[i].SetActive(i < stats.Length);
            if (i >= stats.Length) continue;
            PlayerStatType stat = stats[i];
            _statNames[i].text = PlayerStatPresentation.GetDisplayName(stat);
            float value = _rounds.Player.GetFinalStat(stat);
            if (stat == PlayerStatType.Revival) value = RunState.Instance.RemainingRevivals;
            if (stat == PlayerStatType.Reroll) value = RunState.Instance.RemainingRerolls;
            if (stat == PlayerStatType.Skip) value = RunState.Instance.RemainingSkips;
            if (stat == PlayerStatType.Banish) value = RunState.Instance.RemainingBanishes;
            _statValues[i].text = PlayerStatPresentation.FormatFinalValue(stat, value);
            Sprite icon = null;
            foreach (RoundStatUpgrade definition in _rounds.config.shopCatalog.stats)
                if (definition.modifier.StatType == stat) { icon = definition.icon; break; }
            _statIcons[i].sprite = icon; _statIcons[i].enabled = icon != null;
        }
    }

    /// <summary>武器详情绑定实例，库存变动后的按钮永远重新走服务校验。</summary>
    private void ShowWeapon(RectTransform owner, WeaponBase weapon, bool pin)
    {
        if (weapon == null || !_panel.activeSelf || (_pinned && !pin)) return;
        // 点击锁定后，悬停及导航经过其他图标均不得覆盖操作实例；再次点击才切换。
        _inspectedWeapon = weapon; _pinned = pin; ShowTooltip(owner, weapon.weaponData.icon,
            weapon.weaponData.GetDisplayName(), RoundShopPresentation.Tier(weapon.CurrentLevel),
            RoundShopPresentation.WeaponDetails(weapon.weaponData, weapon.CurrentLevel));
        _tooltipCombine.gameObject.SetActive(true); _tooltipRecycle.gameObject.SetActive(true);
        bool shop = _rounds.Phase == RoundPhase.Shop;
        bool pair = false;
        foreach (WeaponBase other in _rounds.Loadout.OwnedWeapons)
            if (other != weapon && other.weaponData == weapon.weaponData && other.CurrentLevel == weapon.CurrentLevel) pair = true;
        _tooltipCombine.interactable = shop && pair && weapon.CurrentLevel < 4;
        _tooltipRecycle.interactable = shop;
    }

    /// <summary>道具悬停只显示当前叠加层数与对应说明，没有新增消费行为。</summary>
    private void ShowItem(RectTransform owner, OwnedAbilityState item, bool explicitSelection = false)
    {
        if (item == null || !_panel.activeSelf || (_pinned && !explicitSelection)) return;
        _pinned = false; _inspectedWeapon = null;
        RevealItem(owner);
        ShowTooltip(owner, item.Data.icon, item.Data.GetDisplayName(),
            string.Format(T("stacks", "已获得 ×{0}"), item.CurrentLevel),
            RoundShopPresentation.ItemDetails(item.Data, item.CurrentLevel));
        _tooltipCombine.gameObject.SetActive(false); _tooltipRecycle.gameObject.SetActive(false);
    }

    /// <summary>键盘聚焦到滚动区域外的道具时滚动到可见范围，再定位详情窗。</summary>
    private void RevealItem(RectTransform owner)
    {
        Canvas.ForceUpdateCanvases();
        RectTransform viewport = _itemScroll.viewport;
        Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, owner);
        float delta = bounds.min.y < viewport.rect.yMin ? viewport.rect.yMin - bounds.min.y
            : bounds.max.y > viewport.rect.yMax ? viewport.rect.yMax - bounds.max.y : 0;
        Vector2 position = _itemScroll.content.anchoredPosition;
        position.y = Mathf.Clamp(position.y + delta, 0, Mathf.Max(0, _itemScroll.content.rect.height - viewport.rect.height));
        _itemScroll.content.anchoredPosition = position;
    }

    /// <summary>详情窗放在源图标上方并约束在屏幕安全区；保持静止便于进入按钮区域。</summary>
    private void ShowTooltip(RectTransform owner, Sprite icon, string name, string kind, string body)
    {
        _tooltipOwner = owner; _hideAt = -1;
        _tooltipName.text = name; _tooltipKind.text = kind; _tooltipBody.text = body;
        _tooltipIcon.sprite = icon; _tooltipIcon.enabled = icon != null;
        RectTransform panel = (RectTransform)_panel.transform;
        var corners = new Vector3[4]; owner.GetWorldCorners(corners);
        // 世界坐标转换到统一 Canvas 坐标后归一化，不依赖窗口像素或相机投影。
        Vector2 top = panel.InverseTransformPoint(corners[1]);
        bool weapon = _inspectedWeapon != null;
        float bodyHeight = _tooltipBody.GetPreferredValues(body, panel.rect.width * .31f * .87f, float.PositiveInfinity).y;
        float height = Mathf.Clamp(bodyHeight + 114 + (weapon ? 95 : 20), 220, panel.rect.height * .62f);
        float normalizedHeight = height / panel.rect.height;
        float x = Mathf.Clamp((top.x - panel.rect.xMin) / panel.rect.width, .025f, .655f);
        float y = Mathf.Clamp((top.y - panel.rect.yMin) / panel.rect.height + .014f, .035f, .97f - normalizedHeight);
        _tooltip.anchorMin = new Vector2(x, y); _tooltip.anchorMax = new Vector2(x + .31f, y + normalizedHeight);
        _tooltip.offsetMin = _tooltip.offsetMax = Vector2.zero;
        SetAnchors(_tooltipIcon.rectTransform, .06f, 1 - 90 / height, .22f, 1 - 18 / height);
        SetAnchors(_tooltipName.rectTransform, .27f, 1 - 65 / height, .81f, 1 - 18 / height);
        SetAnchors(_tooltipKind.rectTransform, .27f, 1 - 94 / height, .94f, 1 - 68 / height);
        SetAnchors(_tooltipBody.rectTransform, .065f, (weapon ? 95 : 20) / height, .935f, 1 - 114 / height);
        SetAnchors((RectTransform)_tooltipCombine.transform, .06f, 18 / height, .47f, 68 / height);
        SetAnchors((RectTransform)_tooltipRecycle.transform, .53f, 18 / height, .94f, 68 / height);
        SetAnchors((RectTransform)_tooltip.Find("Close"), .83f, 1 - 44 / height, .96f, 1 - 10 / height);
        _tooltip.gameObject.SetActive(true); _tooltip.SetAsLastSibling();
    }

    /// <summary>内容高度变化时重排详情内部元素，保留固定文字行高和边距。</summary>
    private static void SetAnchors(RectTransform rect, float x0, float y0, float x1, float y1)
    {
        rect.anchorMin = new Vector2(x0, y0); rect.anchorMax = new Vector2(x1, y1);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    /// <summary>仅允许当前源图标关闭自己的提示，避免相邻图标快速切换时被旧退出事件干扰。</summary>
    private void ScheduleHide(RectTransform owner)
    {
        if (owner != _tooltipOwner || _pinned) return;
        _hideAt = Time.unscaledTime + .15f;
    }

    /// <summary>关闭时把焦点从不可见详情按钮送回源图标。</summary>
    private void HideTooltip()
    {
        if (_tooltip == null) return;
        bool restore = EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null
            && EventSystem.current.currentSelectedGameObject.transform.IsChildOf(_tooltip);
        RectTransform owner = _tooltipOwner;
        _tooltip.gameObject.SetActive(false); _hideAt = -1; _pinned = false; _inspectedWeapon = null; _tooltipOwner = null;
        if (restore && owner != null && owner.gameObject.activeInHierarchy)
        {
            EventSystem.current.SetSelectedGameObject(owner.gameObject);
            // OnSelect 可再次打开详情；显式关闭请求最终保持隐藏。
            _tooltip.gameObject.SetActive(false); _tooltipOwner = null;
        }
    }

    /// <summary>更新按钮标签而不重建控件。</summary>
    private static void Label(Button button, string value) { button.GetComponentInChildren<TMP_Text>().text = value; }
    /// <summary>创建归一化布局矩形。</summary>
    private static RectTransform Rect(string name, Transform parent, float x0, float y0, float x1, float y1)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = go.GetComponent<RectTransform>(); rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(x0, y0); rect.anchorMax = new Vector2(x1, y1);
        rect.offsetMin = rect.offsetMax = Vector2.zero; return rect;
    }
    /// <summary>创建基础背景块。</summary>
    private static RectTransform Box(string name, Transform parent, float x0, float y0, float x1, float y1, Color color)
    {
        RectTransform rect = Rect(name, parent, x0, y0, x1, y1);
        rect.gameObject.AddComponent<Image>().color = color; return rect;
    }
    /// <summary>矩形边线复用 UI 顶点效果，无需新增位图资源。</summary>
    private static void Outline(RectTransform rect)
    {
        var outline = rect.gameObject.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = new Color32(10, 12, 10, 255); outline.effectDistance = new Vector2(2, -2);
    }
    /// <summary>使用原图与正方形区域，禁止图像拦截父控件的悬停射线。</summary>
    private static Image Icon(string name, Transform parent, float x0, float y0, float x1, float y1)
    {
        Image icon = Box(name, parent, x0, y0, x1, y1, Color.white).GetComponent<Image>();
        icon.preserveAspect = true; icon.raycastTarget = false; return icon;
    }
    /// <summary>创建统一字体文本，允许有限缩放应对长中文和较小窗口。</summary>
    private TMP_Text Text(string name, Transform parent, string text, float x0, float y0, float x1, float y1, int size)
    {
        var label = Rect(name, parent, x0, y0, x1, y1).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font; label.text = text; label.fontSize = size; label.enableAutoSizing = true;
        label.fontSizeMin = size * .78f; label.fontSizeMax = size; label.color = Color.white;
        label.raycastTarget = false; label.alignment = TextAlignmentOptions.MidlineLeft; return label;
    }
    /// <summary>统一聚焦、悬停与不可用反馈，避免只改变文字颜色而缺乏焦点提示。</summary>
    private static void ConfigureButton(Button button)
    {
        ColorBlock colors = button.colors; colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.35f, 1.35f, 1.2f);
        colors.selectedColor = new Color(1.25f, 1.38f, 1.13f);
        colors.disabledColor = new Color(.48f, .48f, .48f, 1);
        button.colors = colors;
    }
    /// <summary>创建可导航按钮，点击只调用服务或展示回调。</summary>
    private Button Button(string name, Transform parent, string text, float x0, float y0, float x1, float y1, Action action, bool hold = false)
    {
        RectTransform rect = Box(name, parent, x0, y0, x1, y1, new Color32(48, 53, 44, 255));
        Button button = hold ? rect.gameObject.AddComponent<HoldToConfirmButton>() : rect.gameObject.AddComponent<Button>(); button.targetGraphic = rect.GetComponent<Image>();
        ConfigureButton(button); button.onClick.AddListener(() => action());
        TMP_Text label = Text("Label", rect, text, .025f, .025f, .975f, .975f, 27);
        label.alignment = TextAlignmentOptions.Center; return button;
    }
}
