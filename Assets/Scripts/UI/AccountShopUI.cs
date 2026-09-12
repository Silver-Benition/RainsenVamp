using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>独立全屏商店面板，持有固定骨架与条目模板引用，仅在页面或账号变化时刷新。</summary>
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
    private readonly List<AccountShopEntryUI> _entries = new List<AccountShopEntryUI>();
    private AccountProgressService _account;
    private int _tab;
    private string _selectedId;
    private string _placeholder;
    private bool _isExclusion;
    public event Action Closed;
    public bool IsVisible => panel != null && panel.activeSelf;


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
#endif
    /// <summary>绑定固定控件；面板初始关闭，控制器本身保持启用。</summary>
    private void Awake()
    {
        _account = AccountProgressService.Current;
        basicTab.onClick.AddListener(ShowBasic);
        advancedTab.onClick.AddListener(ShowAdvanced);
        exclusionTab.onClick.AddListener(ShowExclusions);
        backButton.onClick.AddListener(Close);
        buyButton.onClick.AddListener(Buy);
        refundButton.onClick.AddListener(Refund);
        panel.SetActive(false);
    }

    /// <summary>只订阅账号低频变化事件。</summary>
    private void OnEnable()
    {
        if (_account != null) _account.Changed += Refresh;
    }

    /// <summary>停用时清理订阅。</summary>
    private void OnDisable()
    {
        if (_account != null) _account.Changed -= Refresh;
    }

    /// <summary>支持键盘 Escape 与现有输入映射的手柄 Cancel 返回。</summary>
    private void Update()
    {
        if (IsVisible && (Input.GetKeyDown(KeyCode.Escape) || Input.GetButtonDown("Cancel"))) Close();
    }

    /// <summary>打开基础属性页，焦点落到页签，列表通过导航或鼠标进入。</summary>
    public void Show()
    {
        panel.SetActive(true);
        panel.transform.SetAsLastSibling();
        ShowBasic();
    }

    /// <summary>关闭面板并通知主菜单恢复商店入口焦点。</summary>
    public void Close()
    {
        panel.SetActive(false);
        Closed?.Invoke();
    }

    /// <summary>切换基础页。</summary>
    public void ShowBasic() { ChangeTab(0); }

    /// <summary>切换仅展示占位内容的进阶页。</summary>
    public void ShowAdvanced() { ChangeTab(1); }

    /// <summary>切换排除槽和已发现项目列表。</summary>
    public void ShowExclusions() { ChangeTab(2); }

    /// <summary>重建小规模菜单列表，重复切页复用条目对象，不在战斗路径执行。</summary>
    private void ChangeTab(int tab)
    {
        _tab = tab;
        _selectedId = null;
        _placeholder = null;
        _isExclusion = false;
        foreach (AccountShopEntryUI entry in _entries) entry.gameObject.SetActive(false);
        int index = 0;
        if (tab == 0 && catalog != null)
        {
            foreach (AccountUpgradeDataSO upgrade in catalog.upgrades)
            {
                if (upgrade == null) continue;
                AddEntry(index++, upgrade.fallbackName, upgrade.stableId, null, false);
            }
        }
        else if (tab == 1)
        {
            foreach (string name in new[] { "爆炸伤害", "贯穿伤害", "贯穿次数", "击退数值" })
                AddEntry(index++, name + " · 尚未开放", null, name, false);
        }
        else if (tab == 2)
        {
            AddEntry(index++, "排除槽位", AccountUpgradeCatalogSO.SealSlotId, null, false);
            if (contentCatalog != null)
            {
                foreach (UpgradeDataSO upgrade in contentCatalog.Upgrades)
                {
                    if (upgrade == null || !_account.IsUpgradeDiscovered(upgrade.GetStableId())) continue;
                    string name = upgrade.weaponToGrant != null ? upgrade.weaponToGrant.GetDisplayName() :
                        upgrade.abilityToGrant != null ? upgrade.abilityToGrant.GetDisplayName() : upgrade.GetDisplayName();
                    AddEntry(index++, name, upgrade.GetStableId(), name, true);
                }
            }
        }
        Canvas.ForceUpdateCanvases();
        scroll.verticalNormalizedPosition = 1f;
        // 第一项回调持有真实 ID；借助事件选择避免把对象名当成业务 ID。
        if (index > 0) _entries[0].OnSelect(null);
        SetFocus(tab == 0 ? basicTab : tab == 1 ? advancedTab : exclusionTab);
        statusText.text = string.Empty;
        Refresh();
    }

    /// <summary>从模板获得条目并绑定稳定身份，不依赖本地化名称做存档键。</summary>
    private void AddEntry(int index, string title, string id, string placeholder, bool exclusion)
    {
        if (index == _entries.Count) _entries.Add(Instantiate(entryTemplate, scroll.content));
        AccountShopEntryUI entry = _entries[index];
        entry.gameObject.SetActive(true);
        entry.Bind(title, () => SelectEntry(entry, id, placeholder, exclusion));
    }

    /// <summary>更新详情并把键盘/手柄选中的条目滚入视口，滚动仅在选择事件发生时计算。</summary>
    private void SelectEntry(AccountShopEntryUI entry, string id, string placeholder, bool exclusion)
    {
        _selectedId = id;
        _placeholder = placeholder;
        _isExclusion = exclusion;
        statusText.text = string.Empty;
        Refresh();
        Canvas.ForceUpdateCanvases();
        RectTransform row = (RectTransform)entry.transform;
        float overflow = scroll.content.rect.height - scroll.viewport.rect.height;
        if (overflow > 0f)
        {
            float top = -row.anchoredPosition.y;
            float position = scroll.content.anchoredPosition.y;
            float desired = top < position ? top : top + row.rect.height > position + scroll.viewport.rect.height
                ? top + row.rect.height - scroll.viewport.rect.height : position;
            scroll.verticalNormalizedPosition = 1f - Mathf.Clamp01(desired / overflow);
        }
    }

    /// <summary>刷新当前余额、等级、累计效果与操作可用性，不重建列表以保留焦点。</summary>
    private void Refresh()
    {
        if (!IsVisible) return;
        goldText.text = $"金币  {_account.Gold}";
        buyLabel.text = _isExclusion ? (_account.IsUpgradeSealed(_selectedId) ? "解除排除" : "启用排除") : "购买一级";
        refundLabel.text = "退最高一级";
        buyButton.interactable = false;
        refundButton.interactable = false;
        if (_isExclusion)
        {
            detailText.text = $"{_placeholder}\n\n{(_account.IsUpgradeSealed(_selectedId) ? "已排除" : "未排除")}\n排除槽：{_account.ActiveSealCount} / {_account.SealCapacity}\n\n免费启用或解除排除。";
            if (_account.IsReadOnly) detailText.text += "\n账号为只读状态，无法更改排除";
            else if (!_account.IsUpgradeSealed(_selectedId) && _account.ActiveSealCount >= _account.SealCapacity)
                detailText.text += "\n排除槽已用满，请先解除排除或购买槽位";
            buyButton.interactable = !_account.IsReadOnly && (_account.IsUpgradeSealed(_selectedId) || _account.ActiveSealCount < _account.SealCapacity);
            return;
        }
        if (_selectedId == null)
        {
            detailText.text = (_placeholder ?? "选择一个项目") + "\n\n尚未开放";
            return;
        }
        int level = _account.GetUpgradeLevel(_selectedId);
        bool slots = _selectedId == AccountUpgradeCatalogSO.SealSlotId;
        AccountUpgradeDataSO definition = catalog != null ? catalog.Find(_selectedId) : null;
        bool valid = catalog != null && catalog.Validate(out _) && (slots || definition != null);
        int max = valid ? (slots ? catalog.maxSealSlotLevel : definition.maxLevel) : 0;
        int cost = valid && level < max ? (slots ? catalog.sealSlotCosts[level] : definition.levels[level].cost) : 0;
        string current = slots ? $"{_account.SealCapacity} 个槽位" : FormatEffect(definition, Mathf.Min(level, max));
        string next = level < max ? (slots ? $"{_account.SealCapacity + 1} 个槽位" : FormatEffect(definition, level + 1)) : "已达上限";
        detailText.text = $"{(slots ? "排除槽位" : definition != null ? definition.fallbackName : "历史升级")}\n等级 {level} / {max}\n\n{(slots ? "保留一个免费槽；退款前请先解除占用。" : definition != null ? definition.fallbackDescription : "")}\n\n当前累计：{current}\n下一级：{next}\n\n{(valid ? level < max ? "价格：" + cost + " 金币" : "当前不可继续购买" : "配置不可用")}\n退款：{_account.GetLastPaidCost(_selectedId)} 金币";
        if (_account.IsReadOnly) detailText.text += "\n账号为只读状态，无法购买或退款";
        else if (valid && level < max && _account.Gold < cost) detailText.text += "\n金币不足";
        buyButton.interactable = valid && !_account.IsReadOnly && level < max && _account.Gold >= cost;
        refundButton.interactable = !_account.IsReadOnly && level > 0;
    }

    /// <summary>把累计修改器转换为效果文案；不向玩家展示枚举和内部公式。</summary>
    private static string FormatEffect(AccountUpgradeDataSO definition, int level)
    {
        if (level <= 0 || definition == null || !definition.Validate(out _)) return "无额外加成";
        var text = new StringBuilder();
        foreach (PlayerStatModifier modifier in definition.levels[level - 1].modifiers)
        {
            if (text.Length > 0) text.Append("，");
            bool ratio = modifier.StatType == PlayerStatType.Might || modifier.StatType == PlayerStatType.Area ||
                modifier.StatType == PlayerStatType.ProjectileSpeed || modifier.StatType == PlayerStatType.Duration ||
                modifier.StatType == PlayerStatType.Cooldown || modifier.StatType == PlayerStatType.Luck ||
                modifier.StatType == PlayerStatType.Growth || modifier.StatType == PlayerStatType.Greed ||
                modifier.StatType == PlayerStatType.Curse || modifier.StatType == PlayerStatType.Defang;
            bool percent = modifier.Mode != PlayerStatModifierMode.Flat || ratio;
            float value = modifier.Mode == PlayerStatModifierMode.Multiplicative ? (modifier.Value - 1f) * 100f :
                percent ? modifier.Value * 100f : modifier.Value;
            text.Append(value.ToString("+0.##;-0.##;0", System.Globalization.CultureInfo.InvariantCulture));
            if (percent) text.Append(modifier.Mode == PlayerStatModifierMode.Flat ? " 个百分点" : "%");
            else if (modifier.StatType == PlayerStatType.Recovery) text.Append("/秒");
            else if (modifier.StatType == PlayerStatType.Revival || modifier.StatType == PlayerStatType.Reroll ||
                modifier.StatType == PlayerStatType.Skip || modifier.StatType == PlayerStatType.Banish) text.Append(" 次");
            else if (modifier.StatType == PlayerStatType.MoveSpeed) text.Append(" 单位/秒");
            else if (modifier.StatType == PlayerStatType.Magnet) text.Append(" 单位");

        }
        return text.ToString();
    }

    /// <summary>请求服务完成购买或免费排除，保存失败时只显示错误且不预扣余额。</summary>
    private void Buy()
    {
        bool success = _isExclusion ? _account.TrySetUpgradeSealed(_selectedId, !_account.IsUpgradeSealed(_selectedId)) :
            _account.TryPurchaseUpgrade(catalog, _selectedId);
        FinishOperation(success);
    }

    /// <summary>请求服务按历史实付退还最高一级。</summary>
    private void Refund()
    {
        FinishOperation(_account.TryRefundUpgrade(_selectedId));
    }

    /// <summary>同步操作结果，按钮变为不可交互时恢复页签焦点，防止导航丢失。</summary>
    private void FinishOperation(bool success)
    {
        Refresh();
        statusText.text = success ? "操作成功" : _account.LastTransactionError;
        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        Selectable control = selected != null ? selected.GetComponent<Selectable>() : null;
        if (control != null && !control.IsInteractable()) SetFocus(_tab == 0 ? basicTab : exclusionTab);
    }

    /// <summary>使用现有 EventSystem 建立鼠标、键盘和手柄共同焦点。</summary>
    private static void SetFocus(Button button)
    {
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(button.gameObject);
    }
}
