using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>局间商店与属性四选一视图；只绑定流程和交易服务，不持有独立经济状态。</summary>
public sealed class RoundIntermissionUI : MonoBehaviour
{
    public TMP_FontAsset font;
    private GameObject _panel;
    private TMP_Text _title, _balance, _status, _items;
    private readonly TMP_Text[] _cardTexts = new TMP_Text[4];
    private readonly Image[] _icons = new Image[4];
    private readonly Button[] _actions = new Button[4];
    private readonly Button[] _secondary = new Button[4];
    private readonly TMP_Text[] _weaponTexts = new TMP_Text[6];
    private readonly Button[] _combine = new Button[6], _recycle = new Button[6];
    private Button _refresh, _skip, _next, _exit;
    private RoundController _rounds;
    private RoundPhase _lastPhase = RoundPhase.Finished;
    public GameObject Panel => _panel;

    /// <summary>一次建立 UI 并默认隐藏，交易后只更新已有控件。</summary>
    private void Awake()
    {
        RectTransform panel = Box("RoundIntermission", transform, 0, 0, 1, 1, new Color32(12, 22, 32, 255));
        _panel = panel.gameObject;
        _title = Text("Title", panel, "", .04f, .9f, .7f, .98f, 42);
        _balance = Text("Balance", panel, "", .62f, .9f, .96f, .98f, 28);
        _balance.alignment = TextAlignmentOptions.MidlineRight;
        for (int i = 0; i < 4; i++)
        {
            int slot = i;
            float left = .04f + i * .235f;
            RectTransform card = Box("Offer" + i, panel, left, .51f, left + .215f, .86f, new Color32(25, 45, 57, 255));
            RectTransform icon = Box("Icon", card, .35f, .61f, .65f, .91f, Color.clear);
            _icons[i] = icon.GetComponent<Image>(); _icons[i].preserveAspect = true;
            _cardTexts[i] = Text("Description", card, "", .06f, .24f, .94f, .62f, 25);
            _cardTexts[i].alignment = TextAlignmentOptions.Center;
            _actions[i] = Button("Action", card, "", .06f, .05f, .57f, .21f, () => Act(slot));
            _secondary[i] = Button("Secondary", card, "", .61f, .05f, .94f, .21f, () => Secondary(slot));
        }
        for (int i = 0; i < 6; i++)
        {
            int slot = i;
            float left = .04f + i * .155f;
            RectTransform cell = Box("WeaponSlot" + i, panel, left, .26f, left + .145f, .46f, new Color32(22, 35, 47, 255));
            _weaponTexts[i] = Text("Weapon", cell, "", .05f, .43f, .95f, .96f, 23);
            _weaponTexts[i].alignment = TextAlignmentOptions.Center;
            _combine[i] = Button("Combine", cell, "合并", .04f, .06f, .48f, .34f, () => WeaponAction(slot, false));
            _recycle[i] = Button("Recycle", cell, "回收", .52f, .06f, .96f, .34f, () => WeaponAction(slot, true));
        }
        _items = Text("Items", panel, "", .04f, .15f, .96f, .245f, 22);
        _status = Text("Status", panel, "", .04f, .095f, .96f, .15f, 22);
        _refresh = Button("Refresh", panel, "", .04f, .025f, .27f, .087f, RefreshOffers);
        _skip = Button("Skip", panel, "跳过", .29f, .025f, .43f, .087f, Skip);
        _exit = Button("Exit", panel, "返回主菜单", .48f, .025f, .68f, .087f,
            () => GameFlowManager.Instance.ReturnToMainMenu());
        _next = Button("Next", panel, "开始下一回合", .71f, .025f, .96f, .087f, Next);
        _panel.SetActive(false);
    }

    /// <summary>绑定回合事件；无模式的测试/性能场景不会显示此页面。</summary>
    private void Start()
    {
        _rounds = RoundController.Instance;
        if (_rounds == null) return;
        _rounds.Changed += Refresh;
        Refresh();
    }

    /// <summary>解除事件，避免重开后旧视图继续接收刷新。</summary>
    private void OnDestroy() { if (_rounds != null) _rounds.Changed -= Refresh; }

    /// <summary>交易或属性选择统一入口，失败保持原报价并给出反馈。</summary>
    private void Act(int index)
    {
        bool success = _rounds.Phase == RoundPhase.Upgrades ? _rounds.Choose(index) : _rounds.Shop.Buy(index);
        _status.text = success ? "" : "材料不足、已满级或装备槽位不足";
        Refresh();
    }

    /// <summary>同位置的辅助操作在升级阶段为放逐，在商店阶段为锁定。</summary>
    private void Secondary(int index)
    {
        bool success = _rounds.Phase == RoundPhase.Upgrades ? _rounds.Banish(index) : _rounds.Shop.ToggleLock(index);
        _status.text = success ? "" : "当前无法执行此操作";
        Refresh();
    }

    /// <summary>刷新属性或商品；服务校验余额与免费次数。</summary>
    private void RefreshOffers()
    {
        bool success = _rounds.Phase == RoundPhase.Upgrades ? _rounds.RerollUpgrade() : _rounds.Shop.Refresh();
        _status.text = success ? "" : "材料不足或所有商品已锁定";
        Refresh();
    }

    /// <summary>请求消费跳过次数。</summary>
    private void Skip() { _rounds.SkipUpgrade(); Refresh(); }
    /// <summary>请求开始下一回合；重复请求由流程状态拒绝。</summary>
    private void Next() { _rounds.BeginNextRound(); Refresh(); }

    /// <summary>按实例槽索引提交回收或合并，操作结束后重新绑定六槽。</summary>
    private void WeaponAction(int slot, bool recycle)
    {
        if (slot >= _rounds.Loadout.OwnedWeapons.Count) return;
        WeaponBase weapon = _rounds.Loadout.OwnedWeapons[slot];
        bool success = recycle ? _rounds.Shop.Recycle(weapon) : _rounds.Shop.Combine(weapon);
        _status.text = success ? "" : "需要另一把同种同品质武器，且未达到最高品质";
        Refresh();
    }

    /// <summary>把只读状态绑定到稳定控件，保持按钮焦点并过滤不可操作入口。</summary>
    public void Refresh()
    {
        if (_rounds == null || _rounds.Shop == null) return;
        bool upgrades = _rounds.Phase == RoundPhase.Upgrades;
        bool shop = _rounds.Phase == RoundPhase.Shop;
        _panel.SetActive(upgrades || shop);
        if (!upgrades && !shop) { _lastPhase = _rounds.Phase; return; }
        _panel.transform.SetAsLastSibling();
        _title.text = upgrades ? $"回合 {_rounds.RoundNumber} 完成 · 属性成长（剩余 {_rounds.Player.PendingLevelUps} 次）"
            : $"回合 {_rounds.RoundNumber} 完成 · 商店";
        _balance.text = $"材料 {_rounds.Wallet.Balance}　储备 {_rounds.Wallet.Bagged}";
        for (int i = 0; i < 4; i++)
        {
            RunShopOffer offer = _rounds.Shop.Offers[i];
            RoundStatUpgrade stat = upgrades && i < _rounds.Choices.Count ? _rounds.Choices[i] : null;
            bool has = upgrades ? stat != null : offer != null;
            _icons[i].sprite = upgrades ? stat?.icon : offer?.Product.Icon;
            _icons[i].enabled = _icons[i].sprite != null;
            if (upgrades && stat != null)
            {
                PlayerStatModifier mod = stat.modifier;
                bool percent = mod.Mode != PlayerStatModifierMode.Flat || mod.StatType == PlayerStatType.Defang
                    || mod.StatType == PlayerStatType.Might || mod.StatType == PlayerStatType.Cooldown
                    || mod.StatType == PlayerStatType.Luck || mod.StatType == PlayerStatType.Growth
                    || mod.StatType == PlayerStatType.Area || mod.StatType == PlayerStatType.Greed;
                float value = mod.Value * _rounds.ChoiceTier * (percent ? 100 : 1);
                _cardTexts[i].text = stat.displayName + $"\n{value:+0.##;-0.##;0}" + (percent ? "%" : "")
                    + "\n当前 " + PlayerStatPresentation.FormatFinalValue(mod.StatType, _rounds.Player.GetFinalStat(mod.StatType));
            }
            else _cardTexts[i].text = offer == null ? "暂无商品" : offer.Product.Name
                + (offer.Product.IsWeapon ? $" · 品质 {offer.Tier}" : "")
                + $"\n{offer.Price} 材料";
            Label(_actions[i], upgrades ? "选择" : "购买");
            Label(_secondary[i], upgrades ? "放逐" : offer != null && offer.Locked ? "已锁定" : "锁定");
            _actions[i].interactable = has;
            _secondary[i].interactable = has && (!upgrades || RunState.Instance.RemainingBanishes > 0);
        }
        for (int i = 0; i < 6; i++)
        {
            WeaponBase weapon = i < _rounds.Loadout.OwnedWeapons.Count ? _rounds.Loadout.OwnedWeapons[i] : null;
            _weaponTexts[i].text = weapon != null ? weapon.weaponData.GetDisplayName() + $"\n品质 {weapon.CurrentLevel}" : "空槽";
            _combine[i].interactable = shop && weapon != null && weapon.CurrentLevel < 4;
            _recycle[i].interactable = shop && weapon != null;
        }
        var items = new StringBuilder("道具：");
        foreach (OwnedAbilityState item in _rounds.Items.OwnedAbilities)
            items.Append(item.Data.GetDisplayName()).Append(" ×").Append(item.CurrentLevel).Append("　");
        _items.text = items.ToString();
        Label(_refresh, upgrades ? RunState.Instance.RemainingRerolls > 0
            ? $"重投（免费 {RunState.Instance.RemainingRerolls}）" : $"重投 {_rounds.UpgradeRerollPrice} 材料"
            : $"刷新 {_rounds.Shop.RefreshPrice} 材料");
        _skip.gameObject.SetActive(upgrades);
        _skip.interactable = RunState.Instance.RemainingSkips > 0;
        _next.gameObject.SetActive(shop);
        Label(_next, $"开始第 {_rounds.RoundNumber + 1} 回合");
        if (_lastPhase != _rounds.Phase && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(_actions[0].gameObject);
        _lastPhase = _rounds.Phase;
    }

    /// <summary>更新按钮标签，不重建 Button 本体。</summary>
    private static void Label(Button button, string value) { button.GetComponentInChildren<TMP_Text>().text = value; }

    /// <summary>创建归一化布局矩形，在低频初始化阶段使用。</summary>
    private static RectTransform Rect(string name, Transform parent, float x0, float y0, float x1, float y1)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = go.GetComponent<RectTransform>(); rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(x0, y0); rect.anchorMax = new Vector2(x1, y1);
        rect.offsetMin = rect.offsetMax = Vector2.zero; return rect;
    }

    /// <summary>创建背景块与可选按钮底图。</summary>
    private static RectTransform Box(string name, Transform parent, float x0, float y0, float x1, float y1, Color color)
    {
        RectTransform rect = Rect(name, parent, x0, y0, x1, y1);
        rect.gameObject.AddComponent<Image>().color = color; return rect;
    }

    /// <summary>创建统一字体文本；允许缩小但保持最低字号，避免裁切。</summary>
    private TMP_Text Text(string name, Transform parent, string text, float x0, float y0, float x1, float y1, int size)
    {
        var label = Rect(name, parent, x0, y0, x1, y1).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font; label.text = text; label.fontSize = size; label.enableAutoSizing = true;
        label.fontSizeMin = size * .75f; label.fontSizeMax = size; label.color = Color.white;
        label.raycastTarget = false; label.alignment = TextAlignmentOptions.MidlineLeft; return label;
    }

    /// <summary>创建可导航按钮，业务操作仅通过回调调用服务。</summary>
    private Button Button(string name, Transform parent, string text, float x0, float y0, float x1, float y1, Action action)
    {
        RectTransform rect = Box(name, parent, x0, y0, x1, y1, new Color32(36, 93, 106, 255));
        Button button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = rect.GetComponent<Image>();
        button.onClick.AddListener(() => action());
        TMP_Text label = Text("Label", rect, text, .03f, .04f, .97f, .96f, 24);
        label.alignment = TextAlignmentOptions.Center;
        return button;
    }
}
