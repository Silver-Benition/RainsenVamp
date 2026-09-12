using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>商店输入状态；确认卡片与执行交易分开，避免单次 Submit 误扣金币。</summary>
public enum AccountShopInteractionState { Browsing, Locked }

/// <summary>三列卡片商店，固定布局由场景持有，账号交易只调用已有服务。</summary>
public sealed class AccountShopUI : MonoBehaviour
{
    [SerializeField] private AccountUpgradeCatalogSO catalog;
    [SerializeField] private GameContentCatalogSO contentCatalog;
    [SerializeField] private GameObject panel;
    [SerializeField] private Button basicTab;
    [SerializeField] private Button advancedTab;
    [SerializeField] private Button exclusionTab;
    [SerializeField] private Button backButton;
    [SerializeField] private Button buyButton;
    [SerializeField] private Button refundButton;
    [SerializeField] private TMP_Text goldText;
    [SerializeField] private TMP_Text detailText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text buyLabel;
    [SerializeField] private TMP_Text refundLabel;
    [SerializeField] private ScrollRect scroll;
    [SerializeField] private AccountShopEntryUI entryTemplate;
    [SerializeField] private TMP_Text detailName;
    [SerializeField] private Image detailIcon;
    [SerializeField] private TMP_Text capacityText;
    [SerializeField] private Image goldIcon;
    [SerializeField] private TMP_Text inputHints;
    private readonly List<AccountShopEntryUI> _entries = new List<AccountShopEntryUI>();
    private readonly List<Item> _items = new List<Item>();
    private AccountProgressService _account;
    private int _tab;
    private int _selectedIndex = -1;
    private int _lockFrame = -1;
    private int _cancelFrame = -1;
    public event Action Closed;
    public bool IsVisible => panel != null && panel.activeSelf;
    public AccountShopInteractionState State { get; private set; }
    public int ActiveEntryCount => _items.Count;
    public string SelectedId => _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex].Id : null;

    /// <summary>只在菜单构建时创建的展示模型，稳定 ID 与显示名明确分开。</summary>
    private sealed class Item
    {
        public string Id;
        public string Name;
        public string Description;
        public Sprite Icon;
        public AccountUpgradeDataSO Definition;
        public bool IsSlot;
        public bool IsExclusion;
        public bool IsPlaceholder;
    }

#if UNITY_EDITOR
    /// <summary>仅供编辑器页面搭建工具配置序列化引用。</summary>
    public AccountUpgradeCatalogSO Catalog { get => catalog; set => catalog = value; }
    /// <summary>仅供编辑器页面搭建工具配置序列化引用。</summary>
    public GameContentCatalogSO ContentCatalog { get => contentCatalog; set => contentCatalog = value; }
    /// <summary>仅供编辑器页面搭建工具配置序列化引用。</summary>
    public GameObject Panel { get => panel; set => panel = value; }
    /// <summary>仅供编辑器页面搭建工具配置序列化引用。</summary>
    public Button BasicTab { get => basicTab; set => basicTab = value; }
    /// <summary>仅供编辑器页面搭建工具配置序列化引用。</summary>
    public Button AdvancedTab { get => advancedTab; set => advancedTab = value; }
    /// <summary>仅供编辑器页面搭建工具配置序列化引用。</summary>
    public Button ExclusionTab { get => exclusionTab; set => exclusionTab = value; }
    /// <summary>仅供编辑器页面搭建工具配置序列化引用。</summary>
    public Button BackButton { get => backButton; set => backButton = value; }
    /// <summary>仅供编辑器页面搭建工具配置序列化引用。</summary>
    public Button BuyButton { get => buyButton; set => buyButton = value; }
    /// <summary>仅供编辑器页面搭建工具配置序列化引用。</summary>
    public Button RefundButton { get => refundButton; set => refundButton = value; }
    /// <summary>仅供编辑器页面搭建工具配置序列化引用。</summary>
    public TMP_Text GoldText { get => goldText; set => goldText = value; }
    /// <summary>仅供编辑器页面搭建工具配置序列化引用。</summary>
    public TMP_Text DetailText { get => detailText; set => detailText = value; }
    /// <summary>仅供编辑器页面搭建工具配置序列化引用。</summary>
    public TMP_Text StatusText { get => statusText; set => statusText = value; }
    /// <summary>仅供编辑器页面搭建工具配置序列化引用。</summary>
    public TMP_Text BuyLabel { get => buyLabel; set => buyLabel = value; }
    /// <summary>仅供编辑器页面搭建工具配置序列化引用。</summary>
    public TMP_Text RefundLabel { get => refundLabel; set => refundLabel = value; }
    /// <summary>仅供编辑器页面搭建工具配置序列化引用。</summary>
    public ScrollRect Scroll { get => scroll; set => scroll = value; }
    /// <summary>仅供编辑器页面搭建工具配置序列化引用。</summary>
    public AccountShopEntryUI EntryTemplate { get => entryTemplate; set => entryTemplate = value; }
    /// <summary>仅供编辑器绑定新版详情与提示区域。</summary>
    public TMP_Text DetailName { get => detailName; set => detailName = value; }
    /// <summary>仅供编辑器绑定新版详情与提示区域。</summary>
    public Image DetailIcon { get => detailIcon; set => detailIcon = value; }
    /// <summary>仅供编辑器绑定新版详情与提示区域。</summary>
    public TMP_Text CapacityText { get => capacityText; set => capacityText = value; }
    /// <summary>仅供编辑器绑定新版详情与提示区域。</summary>
    public Image GoldIcon { get => goldIcon; set => goldIcon = value; }
    /// <summary>仅供编辑器绑定新版详情与提示区域。</summary>
    public TMP_Text InputHints { get => inputHints; set => inputHints = value; }
#endif
    /// <summary>绑定静态控件和 Cancel 转发；不在运行时生成固定页面骨架。</summary>
    private void Awake()
    {
        _account = AccountProgressService.Current;
        basicTab.onClick.AddListener(ShowBasic);
        advancedTab.onClick.AddListener(ShowAdvanced);
        exclusionTab.onClick.AddListener(ShowExclusions);
        backButton.onClick.AddListener(CancelOperation);
        buyButton.onClick.AddListener(Buy);
        refundButton.onClick.AddListener(Refund);
        panel.SetActive(false);
    }

    /// <summary>仅监听低频账号变化，不扫描战斗对象。</summary>
    private void OnEnable() { if (_account != null) _account.Changed += Refresh; }
    /// <summary>解除账号订阅并清除隐藏页面的交易状态。</summary>
    private void OnDisable()
    {
        if (_account != null) _account.Changed -= Refresh;
        State = AccountShopInteractionState.Browsing;
        _selectedIndex = -1;
    }
    /// <summary>EventSystem 无选中对象时也允许 Escape 返回；同帧 Cancel 通过去重避免连续退两层。</summary>
    private void Update()
    {
        if (IsVisible && (Input.GetKeyDown(KeyCode.Escape) || Input.GetButtonDown("Cancel"))) CancelOperation();
    }
    /// <summary>Canvas 缩放时重新计算三列宽度；布局值不变时不反复触发布局重建。</summary>
    private void OnRectTransformDimensionsChange() { ResizeGrid(); }
    /// <summary>显示商店并从浏览状态进入基础页。</summary>
    public void Show()
    {
        panel.SetActive(true);
        panel.transform.SetAsLastSibling();
        ShowBasic();
    }
    /// <summary>关闭时失效所有锁定和操作按钮，防止隐藏页面收到迟到事件后交易。</summary>
    public void Close()
    {
        if (!IsVisible) return;
        State = AccountShopInteractionState.Browsing;
        _selectedIndex = -1;
        buyButton.gameObject.SetActive(false);
        refundButton.gameObject.SetActive(false);
        panel.SetActive(false);
        Closed?.Invoke();
    }
    /// <summary>先退出操作状态，再次取消才关闭；鼠标返回、Escape 与手柄 Cancel 共用此入口。</summary>
    public void CancelOperation()
    {
        if (!IsVisible || _cancelFrame == Time.frameCount) return;
        _cancelFrame = Time.frameCount;
        if (State == AccountShopInteractionState.Locked)
        {
            State = AccountShopInteractionState.Browsing;
            statusText.text = string.Empty;
            Refresh();
            if (_selectedIndex >= 0) Focus(_entries[_selectedIndex].Control);
        }
        else Close();
    }
    /// <summary>切换基础页：21 属性与一个排除槽购买项目。</summary>
    public void ShowBasic() { ChangeTab(0); }
    /// <summary>切换四项不可购买的进阶占位卡片。</summary>
    public void ShowAdvanced() { ChangeTab(1); }
    /// <summary>切换只管理免费排除/解除的已发现项目页。</summary>
    public void ShowExclusions() { ChangeTab(2); }

    /// <summary>切页清除锁定并复用卡片，页签当前样式独立于鼠标悬停。</summary>
    private void ChangeTab(int tab)
    {
        if (!IsVisible) return;
        State = AccountShopInteractionState.Browsing;
        _selectedIndex = -1;
        _tab = tab;
        _items.Clear();
        foreach (AccountShopEntryUI entry in _entries) entry.gameObject.SetActive(false);
        if (tab == 0 && catalog != null)
        {
            foreach (AccountUpgradeDataSO definition in catalog.upgrades)
                if (definition != null) _items.Add(new Item { Id = definition.stableId, Name = definition.fallbackName,
                    Description = definition.fallbackDescription, Icon = definition.icon, Definition = definition });
            _items.Add(new Item { Id = AccountUpgradeCatalogSO.SealSlotId, Name = "排除槽位", IsSlot = true,
                Description = "保留一个免费槽位。额外槽位可逐级购买；退款前请先解除占用。", Icon = catalog.sealSlotIcon });
        }
        else if (tab == 1)
        {
            string[] names = { "爆炸伤害", "贯穿伤害", "贯穿次数", "击退数值" };
            for (int i = 0; i < names.Length; i++) _items.Add(new Item { Name = names[i], IsPlaceholder = true,
                Description = "尚未开放", Icon = catalog != null && i < catalog.advancedIcons.Count ? catalog.advancedIcons[i] : null });
        }
        else if (contentCatalog != null)
        {
            foreach (UpgradeDataSO upgrade in contentCatalog.Upgrades)
            {
                if (upgrade == null || !_account.IsUpgradeDiscovered(upgrade.GetStableId())) continue;
                _items.Add(new Item { Id = upgrade.GetStableId(), IsExclusion = true,
                    Name = upgrade.weaponToGrant != null ? upgrade.weaponToGrant.GetDisplayName() : upgrade.abilityToGrant != null ? upgrade.abilityToGrant.GetDisplayName() : upgrade.GetDisplayName(),
                    Description = "免费排除或解除此升级候选。本局放逐独立计算。",
                    Icon = upgrade.weaponToGrant != null ? upgrade.weaponToGrant.icon : upgrade.abilityToGrant != null ? upgrade.abilityToGrant.icon : upgrade.icon });
            }
        }
        for (int i = 0; i < _items.Count; i++)
        {
            if (i == _entries.Count) _entries.Add(Instantiate(entryTemplate, scroll.content));
            int index = i;
            _entries[i].gameObject.name = "ShopCard_" + i;
            _entries[i].gameObject.SetActive(true);
            _entries[i].Bind(focused => Preview(index, focused), () => Lock(index), this);
        }
        ResizeGrid();
        Canvas.ForceUpdateCanvases();
        scroll.verticalNormalizedPosition = 1;
        if (_items.Count > 0) _selectedIndex = 0;
        statusText.text = string.Empty;
        Refresh();
        Focus(CurrentTab());
    }

    /// <summary>悬停及 OnSelect 只影响浏览详情；锁定时不会改变交易目标。</summary>
    private void Preview(int index, bool focused)
    {
        if (!IsVisible || index < 0 || index >= _items.Count) return;
        // 锁定只固定交易身份，不能阻止方向焦点滚入视口；鼠标悬停不主动滚动。
        if (focused) ScrollToEntry(index);
        if (State == AccountShopInteractionState.Locked) return;
        _selectedIndex = index;
        statusText.text = string.Empty;
        Refresh();
    }
    /// <summary>点击或 Submit 锁定项目；同一帧禁止后续交易，重复确认卡片不等于购买。</summary>
    private void Lock(int index)
    {
        if (!IsVisible || index < 0 || index >= _items.Count) return;
        _selectedIndex = index;
        State = AccountShopInteractionState.Locked;
        _lockFrame = Time.frameCount;
        statusText.text = string.Empty;
        Refresh();
        ScrollToEntry(index);
        Focus(buyButton.interactable ? buyButton : refundButton.gameObject.activeSelf && refundButton.interactable ? refundButton : _entries[index].Control);
    }

    /// <summary>更新卡片等级/价格、底部详情与交易状态；保持当前锁定身份。</summary>
    private void Refresh()
    {
        if (!IsVisible) return;
        goldText.text = _account.Gold.ToString();
        goldIcon.sprite = catalog != null ? catalog.goldIcon : null;
        capacityText.text = _tab == 2 ? $"排除槽  {_account.ActiveSealCount} / {_account.SealCapacity}" : "";
        inputHints.text = State == AccountShopInteractionState.Locked ? "确认：执行操作    取消：返回浏览" : "确认：选择项目    取消：返回菜单";
        bool validCatalog = catalog != null && catalog.Validate(out _);
        for (int i = 0; i < _items.Count; i++)
        {
            Item card = _items[i];
            int level = _account.GetUpgradeLevel(card.Id);
            int max = MaxLevel(card);
            string price = card.IsPlaceholder ? "尚未开放" : card.IsExclusion ? (_account.IsUpgradeSealed(card.Id) ? "已排除" : "免费") :
                !validCatalog ? "配置不可用" : level >= max ? "已满级" : Cost(card, level).ToString();
            _entries[i].Refresh(card.Icon, level, max, price, State == AccountShopInteractionState.Locked && i == _selectedIndex);
        }
        Button[] tabs = { basicTab, advancedTab, exclusionTab };
        for (int i = 0; i < tabs.Length; i++)
        {
            tabs[i].GetComponent<Image>().color = i == _tab ? new Color32(8, 124, 160, 255) : new Color32(24, 47, 64, 255);
            Transform marker = tabs[i].transform.Find("CurrentMarker");
            if (marker != null) marker.gameObject.SetActive(i == _tab);
        }
        bool locked = State == AccountShopInteractionState.Locked && _selectedIndex >= 0;
        buyButton.gameObject.SetActive(locked);
        refundButton.gameObject.SetActive(locked);
        buyButton.interactable = refundButton.interactable = false;
        if (_selectedIndex < 0 || _selectedIndex >= _items.Count)
        {
            detailName.text = "道具排除";
            detailText.text = "在升级候选中发现项目后，可在此免费管理排除。";
            detailIcon.enabled = false;
            ConfigureNavigation();
            return;
        }
        Item item = _items[_selectedIndex];
        detailName.text = item.Name;
        detailIcon.sprite = item.Icon;
        detailIcon.enabled = item.Icon != null;
        buyLabel.text = item.IsExclusion ? (_account.IsUpgradeSealed(item.Id) ? "解除排除" : "启用排除") : "购买一级";
        refundLabel.text = "退最高一级";
        if (item.IsExclusion) refundButton.gameObject.SetActive(false);
        string reason;
        if (item.IsPlaceholder)
        {
            detailText.text = "尚未开放";
            reason = "尚未开放，暂不可购买";
        }
        else if (item.IsExclusion)
        {
            bool sealedState = _account.IsUpgradeSealed(item.Id);
            detailText.text = item.Description + $"\n当前状态：{(sealedState ? "已排除" : "未排除")}    已用 / 总容量：{_account.ActiveSealCount} / {_account.SealCapacity}";
            reason = _account.IsReadOnly ? "账号只读，无法更改" : !sealedState && _account.ActiveSealCount >= _account.SealCapacity ? "排除槽已用满，请先解除或在基础属性页购买槽位" : "免费操作";
            buyButton.interactable = !_account.IsReadOnly && (sealedState || _account.ActiveSealCount < _account.SealCapacity);
        }
        else
        {
            int level = _account.GetUpgradeLevel(item.Id);
            int max = MaxLevel(item);
            string current = item.IsSlot ? $"{_account.SealCapacity} 个槽位" : AccountShopEffectPresentation.Format(item.Definition, level, false);
            string delta = level >= max ? "已满级" : item.IsSlot ? "+1 个槽位" : AccountShopEffectPresentation.Format(item.Definition, level, true);
            detailText.text = item.Description + $"\n当前加成：{current}    本次升级：{delta}\n等级 {level} / {max}    退款：{_account.GetLastPaidCost(item.Id)} 金币";
            reason = _account.IsReadOnly ? "账号只读，无法购买或退款" : !validCatalog ? "配置不可用" : level >= max ? "已满级" : _account.Gold < Cost(item, level) ? "金币不足" : "选择购买或退款";
            buyButton.interactable = validCatalog && !_account.IsReadOnly && level < max && _account.Gold >= Cost(item, level);
            bool occupied = item.IsSlot && _account.ActiveSealCount >= _account.SealCapacity;
            refundButton.interactable = !_account.IsReadOnly && level > 0 && !occupied;
            if (occupied && level > 0) reason += "；请先解除排除，再退还槽位";
            if (level == 0) reason += "；尚未购买，无法退款";
        }
        if (string.IsNullOrEmpty(statusText.text) || !locked) statusText.text = reason;
        else statusText.text = statusText.text.Split('\n')[0] + "\n" + reason;
        ConfigureNavigation();
    }

    /// <summary>读取展示上限，免费排除与占位不伪造可购买等级。</summary>
    private int MaxLevel(Item item) { return item.IsSlot ? catalog.maxSealSlotLevel : item.Definition != null ? item.Definition.maxLevel : 0; }
    /// <summary>只在已验证且未满级时读取下一级价格。</summary>
    private int Cost(Item item, int level) { return item.IsSlot ? catalog.sealSlotCosts[level] : item.Definition.levels[level].cost; }
    /// <summary>当前页面的页签按钮。</summary>
    private Button CurrentTab() { return _tab == 0 ? basicTab : _tab == 1 ? advancedTab : exclusionTab; }
    /// <summary>只在锁定且页面可见的下一帧以后执行正式购买或免费排除。</summary>
    private void Buy()
    {
        if (!CanTransact() || !buyButton.interactable) return;
        Item item = _items[_selectedIndex];
        FinishOperation(item.IsExclusion ? _account.TrySetUpgradeSealed(item.Id, !_account.IsUpgradeSealed(item.Id)) : _account.TryPurchaseUpgrade(catalog, item.Id));
    }
    /// <summary>只对当前锁定购买项按历史实付退款。</summary>
    private void Refund()
    {
        if (!CanTransact() || !refundButton.interactable) return;
        FinishOperation(_account.TryRefundUpgrade(_items[_selectedIndex].Id));
    }
    /// <summary>拒绝隐藏页面、失效选择及锁定同帧的迟到交易事件。</summary>
    private bool CanTransact() { return IsVisible && State == AccountShopInteractionState.Locked && _selectedIndex >= 0 && _selectedIndex < _items.Count && _lockFrame != Time.frameCount; }
    /// <summary>买退后保持锁定并更新理由；按钮失效时将焦点移至仍有效的操作或锁定卡片。</summary>
    private void FinishOperation(bool success)
    {
        statusText.text = success ? "操作成功" : _account.LastTransactionError;
        Refresh();
        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        Selectable control = selected != null ? selected.GetComponent<Selectable>() : null;
        if (control == null || !control.IsInteractable())
            Focus(buyButton.interactable ? buyButton : refundButton.gameObject.activeSelf && refundButton.interactable ? refundButton : _entries[_selectedIndex].Control);
    }
    /// <summary>三列网格与可见区域保持一致，超出视口由 ScrollRect/Scrollbar 明确滚动。</summary>
    private void ResizeGrid()
    {
        if (scroll == null || scroll.content == null || scroll.viewport == null) return;
        GridLayoutGroup grid = scroll.content.GetComponent<GridLayoutGroup>();
        if (grid == null) return;
        float width = (scroll.viewport.rect.width - grid.padding.horizontal - grid.spacing.x * 2) / 3;
        Vector2 size = new Vector2(Mathf.Max(1, width), 104);
        if ((grid.cellSize - size).sqrMagnitude > 0.01f) grid.cellSize = size;
    }
    /// <summary>把导航选中的卡片滚入视口；鼠标悬停不改变账号数据。</summary>
    private void ScrollToEntry(int index)
    {
        Canvas.ForceUpdateCanvases();
        RectTransform row = (RectTransform)_entries[index].transform;
        float overflow = scroll.content.rect.height - scroll.viewport.rect.height;
        if (overflow <= 0) return;
        // GridLayoutGroup 以内容顶部为锚点；anchoredPosition 指向卡片 pivot，需扣除 pivot 到上边缘的距离。
        // 使用真实上/下边缘，避免向上导航时只滚入中心、仍裁掉半张卡片。
        float top = -row.anchoredPosition.y - (1f - row.pivot.y) * row.rect.height;
        float position = scroll.content.anchoredPosition.y;
        float desired = top < position ? top : top + row.rect.height > position + scroll.viewport.rect.height ? top + row.rect.height - scroll.viewport.rect.height : position;
        scroll.verticalNormalizedPosition = 1 - Mathf.Clamp01(desired / overflow);
    }
    /// <summary>显式连接页签、三列网格、操作区与返回，最后不足三项的一行仍能到达。</summary>
    private void ConfigureNavigation()
    {
        Button[] tabs = { basicTab, advancedTab, exclusionTab };
        for (int i = 0; i < tabs.Length; i++) SetNavigation(tabs[i], i == 0 ? backButton : tabs[i - 1], i == 2 ? backButton : tabs[i + 1], null, _items.Count > 0 ? _entries[0].Control : backButton);
        Button action = buyButton.gameObject.activeSelf && buyButton.interactable ? buyButton : refundButton.gameObject.activeSelf && refundButton.interactable ? refundButton : backButton;
        for (int i = 0; i < _items.Count; i++)
        {
            int nextRow = i + 3;
            if (nextRow >= _items.Count && i / 3 < (_items.Count - 1) / 3) nextRow = _items.Count - 1;
            SetNavigation(_entries[i].Control, i >= 3 ? _entries[i - 3].Control : CurrentTab(), nextRow < _items.Count ? _entries[nextRow].Control : action,
                i % 3 == 0 ? CurrentTab() : _entries[i - 1].Control,
                i % 3 < 2 && i + 1 < _items.Count ? _entries[i + 1].Control : CurrentTab());
        }
        Button card = _selectedIndex >= 0 ? _entries[_selectedIndex].Control : CurrentTab();
        SetNavigation(buyButton, card, backButton, CurrentTab(), refundButton.gameObject.activeSelf && refundButton.interactable ? refundButton : backButton);
        SetNavigation(refundButton, card, backButton, buyButton.interactable ? buyButton : CurrentTab(), backButton);
        SetNavigation(backButton, card, basicTab, CurrentTab(), action == backButton ? CurrentTab() : action);
    }
    /// <summary>设置显式四向引用，避免布局变化导致自动导航跳过项目。</summary>
    private static void SetNavigation(Button target, Selectable up, Selectable down, Selectable left, Selectable right)
    {
        target.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = up, selectOnDown = down, selectOnLeft = left, selectOnRight = right };
    }
    /// <summary>统一通过 EventSystem 设置低频焦点。</summary>
    private static void Focus(Button button) { if (button != null && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(button.gameObject); }
}
