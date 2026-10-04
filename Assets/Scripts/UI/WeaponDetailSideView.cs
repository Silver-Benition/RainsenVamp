using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>商店与暂停背包共用的右侧附属窗；羁绊和上一波伤害各自拥有独立边框。</summary>
public sealed class WeaponDetailSideView
{
    private readonly RectTransform _canvas, _root, _sets, _damage;
    private readonly TMP_Text _setText, _damageText;
    private readonly Vector3[] _corners = new Vector3[4];
    public RectTransform Root => _root;

    /// <summary>仅初始化一次控件；后续悬停只换内容与尺寸，不在 Update 中遍历装备。</summary>
    public WeaponDetailSideView(RectTransform canvas, TMP_FontAsset font, string name)
    {
        _canvas = canvas;
        _root = NewRect(name, canvas);
        _sets = Pane("Sets", _root); _damage = Pane("WaveDamage", _root);
        _setText = Label(_sets, font); _damageText = Label(_damage, font);
        Hide();
    }

    /// <summary>显示全档总收益；实例非空时增加伤害窗，商品没有上一波战斗记录。</summary>
    public void Show(RectTransform anchor, WeaponDataSO data, LevelUpManager loadout, WeaponBase instance = null)
    {
        string tiers = WeaponSetPresentation.AllTiers(data, loadout);
        _setText.text = tiers;
        _damageText.text = instance != null ? string.Format(
            RoundShopPresentation.Text("weapon.previousWaveDamage", "这把武器在上一波造成了 <color=#B5E780>{0}</color> 点伤害"),
            instance.WaveDamage.LastWaveDamage.ToString("0.##")) : "";
        bool hasSets = !string.IsNullOrEmpty(tiers), hasDamage = instance != null;
        if (!hasSets && !hasDamage) { Hide(); return; }
        _root.gameObject.SetActive(true); _root.SetAsLastSibling();
        _sets.gameObject.SetActive(hasSets); _damage.gameObject.SetActive(hasDamage);
        // 最大宽度保证最右商品的窗仍能完整摆在商品右侧，库存详情则整体向左让位。
        float width = Mathf.Min(400, _canvas.rect.width * .215f);
        float setHeight = hasSets ? _setText.GetPreferredValues(tiers, width - 28, float.PositiveInfinity).y + 28 : 0;
        float damageHeight = hasDamage ? _damageText.GetPreferredValues(_damageText.text, width - 28, float.PositiveInfinity).y + 28 : 0;
        float gap = hasSets && hasDamage ? 8 : 0;
        float height = setHeight + gap + damageHeight;
        SetRect(_root, 0, 0, width, height);
        if (hasSets) SetRect(_sets, 0, height - setHeight, width, setHeight);
        if (hasDamage) SetRect(_damage, 0, 0, width, damageHeight);
        Canvas.ForceUpdateCanvases();
        anchor.GetWorldCorners(_corners);
        Vector3 rightTop = _canvas.InverseTransformPoint(_corners[2]);
        float x = rightTop.x - _canvas.rect.xMin + 8;
        float top = rightTop.y - _canvas.rect.yMin;
        float maxX = _canvas.rect.width - width - 12;
        if (instance != null)
        {
            // 附属窗始终在主详情右侧；整体平移并保持顶部对齐，双标签也不被屏幕底部裁切。
            float shiftX = Mathf.Min(0, maxX - x);
            float alignedTop = Mathf.Clamp(top, height + 12, _canvas.rect.height - 12);
            anchor.anchoredPosition += new Vector2(shiftX, alignedTop - top);
            x += shiftX; top = alignedTop;
        }
        _root.anchoredPosition = new Vector2(x, Mathf.Clamp(top - height, 12, Mathf.Max(12, _canvas.rect.height - height - 12)));
    }

    /// <summary>关闭父详情、离开标签、翻页和恢复战斗时同步隐藏两个附属窗。</summary>
    public void Hide() { if (_root != null) _root.gameObject.SetActive(false); }
    /// <summary>暂停视图销毁时回收其创建的同级控件。</summary>
    public void Dispose() { if (_root != null) Object.Destroy(_root.gameObject); }

    /// <summary>创建左下锚定矩形，便于按实际文本高度定位。</summary>
    private static RectTransform NewRect(string name, Transform parent)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
        return rect;
    }
    /// <summary>创建不遮挡源标签射线的深色浮层与边框。</summary>
    private static RectTransform Pane(string name, Transform parent)
    {
        RectTransform rect = NewRect(name, parent);
        Image image = rect.gameObject.AddComponent<Image>(); image.color = new Color32(13, 16, 14, 255); image.raycastTarget = false;
        Outline outline = rect.gameObject.AddComponent<Outline>(); outline.effectColor = new Color32(111, 116, 99, 255);
        outline.effectDistance = new Vector2(1, -1); return rect;
    }
    /// <summary>统一内边距与字体，禁用文字射线；换行高度由内容测量决定。</summary>
    private static TMP_Text Label(RectTransform parent, TMP_FontAsset font)
    {
        RectTransform rect = NewRect("Description", parent);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(14, 14); rect.offsetMax = new Vector2(-14, -14);
        TMP_Text text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font; text.fontSize = 22; text.color = Color.white; text.alignment = TextAlignmentOptions.TopLeft;
        text.raycastTarget = false; return text;
    }
    /// <summary>更新左下坐标和明确尺寸，不改变子文本边距。</summary>
    private static void SetRect(RectTransform rect, float x, float y, float width, float height)
    { rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(width, height); }
}
