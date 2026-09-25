using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>暂停背包的只读视图；由装备 HUD 管理生命周期，不持有或修改任何交易状态。</summary>
public sealed class PauseInventoryView
{
    private readonly RectTransform _canvas, _weapons, _items, _content, _tooltip;
    private readonly TMP_Text _title, _body, _empty;
    private readonly ScrollRect _scroll;
    private readonly GridLayoutGroup _grid;
    private readonly List<ItemCell> _cells = new List<ItemCell>();
    private LevelUpManager _loadout;
    private AbilityManager _abilities;
    private PlayerStats _player;
    private TMP_FontAsset _font;
    private RectTransform _owner;
    private bool _visible;

    /// <summary>道具槽按持有数量增长后复用，隐藏期间不重建。</summary>
    private sealed class ItemCell
    {
        public RectTransform Root;
        public Image Icon;
        public TMP_Text Count;
        public RoundHoverTarget Hover;
    }

    /// <summary>创建左下滚动背包和共用详情窗，并将现有六个武器槽接入悬停。</summary>
    public PauseInventoryView(RectTransform canvas, RectTransform weapons)
    {
        _canvas = canvas; _weapons = weapons;
        TMP_Text existing = canvas.GetComponentInChildren<TMP_Text>(true);
        _font = existing != null ? existing.font : TMP_Settings.defaultFontAsset;
        _items = Box("PauseItems", canvas, .035f, .035f, .34f, .285f, new Color32(30, 33, 29, 245));
        Label("Heading", _items, RoundShopPresentation.Text("pause.items", "已持有道具"), .04f, .78f, .96f, .97f, 25);
        _empty = Label("Empty", _items, RoundShopPresentation.Text("pause.noItems", "暂无道具"), .04f, .15f, .96f, .70f, 22);
        RectTransform viewport = Box("Viewport", _items, .04f, .05f, .96f, .76f, Color.clear);
        viewport.gameObject.AddComponent<RectMask2D>();
        _content = Rect("Content", viewport, 0, 1, 1, 1);
        _content.pivot = new Vector2(.5f, 1);
        _grid = _content.gameObject.AddComponent<GridLayoutGroup>();
        _grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        _grid.constraintCount = 6; _grid.spacing = new Vector2(6, 6);
        _scroll = viewport.gameObject.AddComponent<ScrollRect>();
        _scroll.viewport = viewport; _scroll.content = _content; _scroll.horizontal = false;
        _scroll.movementType = ScrollRect.MovementType.Clamped; _scroll.scrollSensitivity = 28;
        _scroll.onValueChanged.AddListener(OnScroll);
        _tooltip = Box("PauseInventoryTooltip", canvas, 0, 0, 0, 0, new Color32(22, 25, 21, 255));
        _tooltip.GetComponent<Image>().raycastTarget = false;
        _title = Label("Name", _tooltip, "", .05f, .80f, .95f, .96f, 27);
        _body = Label("Description", _tooltip, "", .05f, .05f, .95f, .77f, 23);
        _body.alignment = TextAlignmentOptions.TopLeft;
        StatIconPresentation.Bind(_body);
        for (int index = 0; index < PlayerLoadoutRules.MaxWeaponCount; index++)
        {
            int captured = index;
            var slot = (RectTransform)weapons.Find("WeaponSlot_" + (index + 1));
            if (slot == null) continue;
            Image hit = slot.gameObject.AddComponent<Image>(); hit.color = Color.clear;
            RoundHoverTarget hover = slot.gameObject.AddComponent<RoundHoverTarget>();
            hover.Bind(() => ShowWeapon(slot, captured), () => HideFrom(slot));
        }
        _items.gameObject.SetActive(false); _tooltip.gameObject.SetActive(false);
    }

    /// <summary>在装备事件和打开暂停时更新快照；悬停回调读取最新实例，避免合成后指向旧武器。</summary>
    public void Refresh(LevelUpManager loadout, AbilityManager abilities)
    {
        _loadout = loadout; _abilities = abilities;
        _player = abilities != null ? abilities.GetComponent<PlayerStats>() : null;
        if (_player == null && loadout != null && loadout.OwnedWeapons.Count > 0 && loadout.OwnedWeapons[0] != null)
            _player = loadout.OwnedWeapons[0].GetComponentInParent<PlayerStats>();
        Hide();
        int count = abilities != null ? abilities.OwnedAbilities.Count : 0;
        for (int i = 0; i < count; i++)
        {
            if (i == _cells.Count) _cells.Add(CreateItem(i));
            ItemCell cell = _cells[i]; OwnedAbilityState item = abilities.OwnedAbilities[i];
            bool valid = item != null && item.Data != null;
            cell.Root.gameObject.SetActive(valid);
            if (!valid) continue;
            cell.Icon.sprite = item.Data.icon; cell.Icon.enabled = item.Data.icon != null;
            cell.Count.text = "x" + item.CurrentLevel;
        }
        for (int i = count; i < _cells.Count; i++) _cells[i].Root.gameObject.SetActive(false);
        _empty.gameObject.SetActive(count == 0);
        Layout();
    }

    /// <summary>仅手动暂停可展示；恢复、停用和重载时同步清空详情。</summary>
    public void SetVisible(bool visible)
    {
        _visible = visible; _items.gameObject.SetActive(visible); Hide();
        if (visible) { _items.SetAsLastSibling(); _weapons.SetAsLastSibling(); Layout(); }
    }

    /// <summary>窗口变化时重排固定列数及滚动内容高度，保持全部道具可达。</summary>
    public void Layout()
    {
        if (_items == null) return;
        float size = Mathf.Max(24, (_scroll.viewport.rect.width - 5 * 6) / 6);
        _grid.cellSize = new Vector2(size, size);
        int count = _abilities != null ? _abilities.OwnedAbilities.Count : 0;
        _content.sizeDelta = new Vector2(0, Mathf.Ceil(count / 6f) * (size + 6));
        Vector2 offset = _content.anchoredPosition;
        offset.y = Mathf.Clamp(offset.y, 0, Mathf.Max(0, _content.rect.height - _scroll.viewport.rect.height));
        _content.anchoredPosition = offset;
        Hide();
    }

    /// <summary>创建带叠加数量的道具格，图标与文字不阻挡父格悬停射线。</summary>
    private ItemCell CreateItem(int index)
    {
        RectTransform root = Box("ItemSlot" + index, _content, 0, 0, 1, 1, new Color32(59, 62, 51, 255));
        Image icon = Box("Icon", root, .08f, .12f, .92f, .94f, Color.white).GetComponent<Image>();
        icon.preserveAspect = true; icon.raycastTarget = false;
        TMP_Text count = Label("Count", root, "", .05f, .01f, .95f, .32f, 19);
        count.alignment = TextAlignmentOptions.BottomRight;
        RoundHoverTarget hover = root.gameObject.AddComponent<RoundHoverTarget>();
        hover.Bind(() => ShowItem(root, index), () => HideFrom(root));
        return new ItemCell { Root = root, Icon = icon, Count = count, Hover = hover };
    }

    /// <summary>显示所悬停武器当前品质的实际属性；不授予商店操作权限。</summary>
    private void ShowWeapon(RectTransform owner, int index)
    {
        if (!_visible || _loadout == null || index >= _loadout.OwnedWeapons.Count) return;
        WeaponBase weapon = _loadout.OwnedWeapons[index];
        if (weapon == null || weapon.weaponData == null) return;
        Show(owner, weapon.weaponData.GetDisplayName() + " · " + RoundShopPresentation.Tier(weapon.CurrentLevel),
            RoundShopPresentation.WeaponDetails(weapon.weaponData, weapon.CurrentLevel, _player, true));
    }

    /// <summary>显示所悬停道具当前叠加等级的累计收益。</summary>
    private void ShowItem(RectTransform owner, int index)
    {
        if (!_visible || _abilities == null || index >= _abilities.OwnedAbilities.Count) return;
        OwnedAbilityState item = _abilities.OwnedAbilities[index];
        if (item == null || item.Data == null) return;
        Show(owner, item.Data.GetDisplayName() + " x" + item.CurrentLevel + " · " + RoundShopPresentation.Tier(item.Data.quality),
            RoundShopPresentation.ItemDetails(item.Data, item.CurrentLevel));
    }

    /// <summary>把详情放在源图标上方；由 Canvas 坐标约束四边，适配相机与覆盖层 Canvas。</summary>
    private void Show(RectTransform owner, string title, string body)
    {
        _owner = owner; _title.text = title; _body.text = body;
        Canvas.ForceUpdateCanvases();
        float width = Mathf.Min(460, _canvas.rect.width * .34f);
        float bodyHeight = _body.GetPreferredValues(body, width * .9f, float.PositiveInfinity).y;
        float height = Mathf.Clamp(bodyHeight + 76, 160, _canvas.rect.height * .72f);
        var corners = new Vector3[4]; owner.GetWorldCorners(corners);
        Vector3 top = _canvas.InverseTransformPoint(corners[1]);
        // 左下原点下定位，保证详情不因武器位于屏幕右边而超出窗口。
        float x = Mathf.Clamp(top.x - _canvas.rect.xMin, 12, _canvas.rect.width - width - 12);
        float y = Mathf.Clamp(top.y - _canvas.rect.yMin + 12, 12, _canvas.rect.height - height - 12);
        _tooltip.anchorMin = _tooltip.anchorMax = Vector2.zero;
        _tooltip.pivot = Vector2.zero; _tooltip.sizeDelta = new Vector2(width, height);
        _tooltip.anchoredPosition = new Vector2(x, y);
        _title.rectTransform.anchorMin = new Vector2(.05f, 1 - 55 / height);
        _title.rectTransform.anchorMax = new Vector2(.95f, 1 - 10 / height);
        _body.rectTransform.anchorMin = new Vector2(.05f, 12 / height);
        _body.rectTransform.anchorMax = new Vector2(.95f, 1 - 65 / height);
        _tooltip.gameObject.SetActive(true); _tooltip.SetAsLastSibling();
    }

    /// <summary>滚动时关闭旧位置的提示，避免提示与已移出视口的道具脱离。</summary>
    private void OnScroll(Vector2 position) { Hide(); }
    /// <summary>只允许当前悬停源关闭提示，避免相邻槽位事件顺序导致误关。</summary>
    private void HideFrom(RectTransform owner) { if (_owner == owner) Hide(); }
    /// <summary>隐藏详情并释放当前源引用。</summary>
    private void Hide() { _owner = null; if (_tooltip != null) _tooltip.gameObject.SetActive(false); }

    /// <summary>组件单独销毁时移除它创建的同级 UI；场景销毁时失效引用可安全跳过。</summary>
    public void Dispose()
    {
        _visible = false;
        if (_scroll != null) _scroll.onValueChanged.RemoveListener(OnScroll);
        if (_items != null) Object.Destroy(_items.gameObject);
        if (_tooltip != null) Object.Destroy(_tooltip.gameObject);
    }

    /// <summary>创建相对父区域的布局矩形。</summary>
    private static RectTransform Rect(string name, Transform parent, float x0, float y0, float x1, float y1)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = new Vector2(x0, y0); rect.anchorMax = new Vector2(x1, y1);
        rect.offsetMin = rect.offsetMax = Vector2.zero; return rect;
    }
    /// <summary>创建可接收悬停或滚动的背景块。</summary>
    private static RectTransform Box(string name, Transform parent, float x0, float y0, float x1, float y1, Color color)
    {
        RectTransform rect = Rect(name, parent, x0, y0, x1, y1);
        rect.gameObject.AddComponent<Image>().color = color; return rect;
    }
    /// <summary>创建复用现有中文字体的文本，长名称允许有限缩小。</summary>
    private TMP_Text Label(string name, Transform parent, string text, float x0, float y0, float x1, float y1, int size)
    {
        TMP_Text label = Rect(name, parent, x0, y0, x1, y1).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = _font; label.text = text; label.fontSize = size;
        label.enableAutoSizing = true; label.fontSizeMin = size * .78f; label.fontSizeMax = size;
        label.raycastTarget = false; label.color = Color.white; label.alignment = TextAlignmentOptions.MidlineLeft;
        return label;
    }
}
